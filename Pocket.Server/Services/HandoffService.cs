using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Pocket.Server.Services
{
    /// <summary>
    /// On graceful shutdown of a node with Cluster:HandoffUrl set (both nodes; each points at the
    /// other): starts draining (new connections refused, /health answers 503 so the peer's
    /// watchdog brings the peer up), waits for the peer to be healthy, syncs the identity registry,
    /// disconnects clients so they move over, then pushes every still-undelivered payload to the
    /// peer's /internal/handoff — memory to memory, nothing touches disk (BR-201).
    /// </summary>
    public class HandoffService : IHostedLifecycleService
    {
        public const string SecretHeader = "X-Pocket-Cluster-Secret";

        /// <summary>Device ids that already confirmed the handed-off payload, comma separated.</summary>
        public const string ConfirmedDevicesHeader = "X-Pocket-Confirmed-Devices";

        /// <summary>The recipient's active devices as "id=lastSeenUnixSeconds", comma separated.</summary>
        public const string RecipientDevicesHeader = "X-Pocket-Recipient-Devices";

        private readonly ClusterOptions _options;
        private readonly ClusterState _state;
        private readonly TransientMemoryStore _store;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IdentitySync _identitySync;
        private readonly ILogger<HandoffService> _logger;

        public HandoffService(IOptions<ClusterOptions> options, ClusterState state, TransientMemoryStore store,
            IHttpClientFactory httpClientFactory, IdentitySync identitySync, ILogger<HandoffService> logger)
        {
            _options = options.Value;
            _state = state;
            _store = store;
            _httpClientFactory = httpClientFactory;
            _identitySync = identitySync;
            _logger = logger;
        }

        public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        // StoppingAsync runs before any hosted service is stopped — Kestrel (the web host) stops
        // first in StopAsync order — so /health can still answer 503 and the hub can still talk to
        // clients while we wait for the peer. The token is the host's shutdown timeout.
        public async Task StoppingAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_options.HandoffUrl) || string.IsNullOrWhiteSpace(_options.Secret))
                return;

            var baseUrl = _options.HandoffUrl.TrimEnd('/');
            var http = _httpClientFactory.CreateClient("handoff");

            // Draining first: /health turns 503, which the peer's watchdog takes as "start now".
            // Connected clients stay here until the peer can take them.
            _state.Draining = true;
            bool peerUp = await WaitForPeerAsync(http, baseUrl, cancellationToken);

            // Users who signed up here must be known there before the clients we are about to
            // disconnect log in to it.
            if (peerUp)
                await _identitySync.SyncWithPeerAsync(TimeSpan.FromSeconds(10), cancellationToken);

            _state.AbortAll();
            await Task.Delay(500, CancellationToken.None); // let in-flight SendPayload calls land

            var pending = _store.SnapshotForHandoff();
            if (pending.Count == 0)
            {
                _logger.LogInformation("Handoff: no pending payloads.");
                return;
            }
            if (!peerUp)
            {
                _logger.LogWarning("Handoff: peer {Peer} did not come up; {Total} pending payloads are dropped.", baseUrl, pending.Count);
                return;
            }

            int sent = 0;

            foreach (var item in pending)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var payload = item.Payload;
                try
                {
                    // Same body as before; the delivery state rides in headers an older peer simply ignores.
                    using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/internal/handoff")
                    {
                        Content = JsonContent.Create(payload)
                    };
                    request.Headers.Add(SecretHeader, _options.Secret);
                    if (item.ConfirmedDevices.Length > 0)
                        request.Headers.Add(ConfirmedDevicesHeader, string.Join(",", item.ConfirmedDevices));
                    if (item.RecipientDevices.Count > 0)
                        request.Headers.Add(RecipientDevicesHeader, string.Join(",",
                            item.RecipientDevices.Select(d => $"{d.Key}={new DateTimeOffset(d.Value).ToUnixTimeSeconds()}")));
                    using var response = await http.SendAsync(request, cancellationToken);
                    if (response.IsSuccessStatusCode) sent++;
                    else _logger.LogWarning("Handoff of {MessageId} failed: {Status}", payload.MessageId, (int)response.StatusCode);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Handoff of {MessageId} failed: {Error}", payload.MessageId, ex.Message);
                }
            }

            _logger.LogInformation("Handoff: {Sent}/{Total} pending payloads transferred to {Peer}.", sent, pending.Count, baseUrl);
        }

        /// <summary>Polls the peer's /health until it answers 200, up to Cluster:HandoffWaitSeconds.</summary>
        private async Task<bool> WaitForPeerAsync(HttpClient http, string baseUrl, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddSeconds(Math.Max(0, _options.HandoffWaitSeconds));
            bool logged = false;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(TimeSpan.FromSeconds(5));
                    using var response = await http.GetAsync($"{baseUrl}/health", cts.Token);
                    if (response.IsSuccessStatusCode) return true;
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // peer not reachable yet
                }

                if (DateTime.UtcNow >= deadline) break;
                if (!logged)
                {
                    _logger.LogInformation("Handoff: waiting up to {Seconds}s for peer {Peer} to come up.", _options.HandoffWaitSeconds, baseUrl);
                    logged = true;
                }
                try { await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
            return false;
        }
    }
}

using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Pocket.Server.Services
{
    /// <summary>
    /// On graceful shutdown of a node with Cluster:HandoffUrl set (the standby), refuses new
    /// connections, disconnects clients so they fail over, then pushes every still-undelivered
    /// payload to the peer's /internal/handoff — memory to memory, nothing touches disk (BR-201).
    /// </summary>
    public class HandoffService : IHostedService
    {
        public const string SecretHeader = "X-Pocket-Cluster-Secret";

        private readonly ClusterOptions _options;
        private readonly ClusterState _state;
        private readonly TransientMemoryStore _store;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<HandoffService> _logger;

        public HandoffService(IOptions<ClusterOptions> options, ClusterState state, TransientMemoryStore store,
            IHttpClientFactory httpClientFactory, ILogger<HandoffService> logger)
        {
            _options = options.Value;
            _state = state;
            _store = store;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        // Registered after the web host, so this runs before Kestrel stops.
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_options.HandoffUrl) || string.IsNullOrWhiteSpace(_options.Secret))
                return;

            _state.Draining = true;
            _state.AbortAll();
            await Task.Delay(500, CancellationToken.None); // let in-flight SendPayload calls land

            var pending = _store.SnapshotAll();
            if (pending.Count == 0)
            {
                _logger.LogInformation("Handoff: no pending payloads.");
                return;
            }

            var baseUrl = _options.HandoffUrl.TrimEnd('/');
            var http = _httpClientFactory.CreateClient("handoff");
            int sent = 0;

            foreach (var payload in pending)
            {
                if (cancellationToken.IsCancellationRequested) break;
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/internal/handoff")
                    {
                        Content = JsonContent.Create(payload)
                    };
                    request.Headers.Add(SecretHeader, _options.Secret);
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
    }
}

using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Pocket.Server.Services
{
    /// <summary>
    /// Keeps the PC's and the Mac's identity registries in step by exchanging them with the peer's
    /// /internal/identities/sync (both sides merge, see <see cref="IdentityRegistry.Merge"/>).
    /// Runs before a node starts accepting logins and, on the standby, before it hands back.
    /// Only public keys and usernames are exchanged — no messages or media (BR-201).
    /// </summary>
    public class IdentitySync
    {
        private readonly ClusterOptions _options;
        private readonly IdentityRegistry _registry;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<IdentitySync> _logger;

        public IdentitySync(IOptions<ClusterOptions> options, IdentityRegistry registry,
            IHttpClientFactory httpClientFactory, ILogger<IdentitySync> logger)
        {
            _options = options.Value;
            _registry = registry;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        private string? PeerUrl =>
            (string.IsNullOrWhiteSpace(_options.PeerUrl) ? _options.HandoffUrl : _options.PeerUrl)?.TrimEnd('/');

        /// <summary>Best effort: an unreachable peer (it is usually stopped) is logged and ignored.</summary>
        public async Task SyncWithPeerAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var peer = PeerUrl;
            if (string.IsNullOrWhiteSpace(peer) || string.IsNullOrWhiteSpace(_options.Secret)) return;

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout);

                using var request = new HttpRequestMessage(HttpMethod.Post, $"{peer}/internal/identities/sync")
                {
                    Content = JsonContent.Create(_registry.Snapshot())
                };
                request.Headers.Add(HandoffService.SecretHeader, _options.Secret);

                using var response = await _httpClientFactory.CreateClient("handoff").SendAsync(request, cts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Identity sync with {Peer} skipped: HTTP {Status}.", peer, (int)response.StatusCode);
                    return;
                }

                var theirs = await response.Content.ReadFromJsonAsync<Dictionary<string, IdentityRegistry.Entry>>(cts.Token);
                var result = _registry.Merge(theirs);
                _logger.LogInformation("Identity sync with {Peer}: {Added} added, {Updated} updated, {Total} total.",
                    peer, result.Added, result.Updated, _registry.Count);
            }
            catch (Exception ex)
            {
                _logger.LogInformation("Identity sync with {Peer} skipped: {Error}", peer, ex.Message);
            }
        }
    }
}

using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace Pocket.Server.Services
{
    /// <summary>
    /// "Cluster" section of configuration. Each node runs a watchdog (deploy/README.md): the
    /// primary (Mac) keeps its server running, the standby (PC) starts its server only while the
    /// primary is unreachable or draining and stops it (handing off) once the primary is back.
    /// </summary>
    public class ClusterOptions
    {
        /// <summary>Human-readable node name reported by /health (e.g. "pc", "mac").</summary>
        public string NodeName { get; set; } = Environment.MachineName.ToLowerInvariant();

        /// <summary>"primary" or "standby".</summary>
        public string Role { get; set; } = "primary";

        /// <summary>Shared secret required by /internal/handoff. Handoff is disabled when empty.</summary>
        public string? Secret { get; set; }

        /// <summary>
        /// Base URL of the node that receives this node's pending payloads on graceful shutdown
        /// (set on both nodes, pointing at the other one). Empty = no handoff on shutdown.
        /// </summary>
        public string? HandoffUrl { get; set; }

        /// <summary>
        /// On shutdown, how long to wait for the peer to come up before handing off. While this node
        /// drains, its /health answers 503, so the peer's watchdog starts the peer at once; a node
        /// that is already up (the primary, when the standby hands back) is used immediately.
        /// </summary>
        public int HandoffWaitSeconds { get; set; } = 90;

        /// <summary>
        /// Base URL of the other node, used to sync the identity registry (on startup and, on the
        /// way out, before handing off). Defaults to HandoffUrl.
        /// </summary>
        public string? PeerUrl { get; set; }
    }

    /// <summary>Runtime state shared by the hub, the endpoints and the shutdown handoff.</summary>
    public class ClusterState
    {
        private readonly ConcurrentDictionary<string, HubCallerContext> _connections = new();

        public DateTime StartedAtUtc { get; } = DateTime.UtcNow;

        /// <summary>Set while handing off: new hub connections are refused so clients fail over.</summary>
        public volatile bool Draining;

        public int ConnectionCount => _connections.Count;

        public void Track(HubCallerContext context) => _connections[context.ConnectionId] = context;

        public void Untrack(string connectionId) => _connections.TryRemove(connectionId, out _);

        /// <summary>Disconnects one user's devices so they log in and register again.</summary>
        public void AbortUser(string userId)
        {
            foreach (var context in _connections.Values)
            {
                if (context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value != userId) continue;
                try { context.Abort(); } catch { /* connection already gone */ }
            }
        }

        public void AbortAll()
        {
            foreach (var context in _connections.Values)
            {
                try { context.Abort(); } catch { /* connection already gone */ }
            }
        }
    }
}

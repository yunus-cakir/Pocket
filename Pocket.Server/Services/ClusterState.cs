using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace Pocket.Server.Services
{
    /// <summary>
    /// "Cluster" section of configuration. Only one relay node may be active at a time:
    /// the primary (PC) is normally live, the standby (Mac) is started by its watchdog only
    /// while the primary is unreachable.
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
        /// (set on the standby, pointing at the primary). Empty = no handoff on shutdown.
        /// </summary>
        public string? HandoffUrl { get; set; }

        /// <summary>
        /// Base URL of the other node, used to sync the identity registry (on startup and, on the
        /// standby, before handing off). Defaults to HandoffUrl. The primary sets it to the standby.
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

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Pocket.Shared.DTOs;
using Pocket.Shared.Interfaces;
using Pocket.Server.Services;
using System.Security.Claims;

namespace Pocket.Server.Hubs
{
    [Authorize]
    public class RelayHub : Hub<IRelayClient>
    {
        // Maps UserId to a set of ConnectionIds (supports multi-device)
        private static readonly ConcurrentDictionary<string, HashSet<string>> UserConnections = new();
        private static readonly ConcurrentDictionary<string, UserIdentityDto> ActiveUsernames = new(StringComparer.OrdinalIgnoreCase);
        private readonly TransientMemoryStore _memoryStore;
        private readonly ClusterState _cluster;

        public RelayHub(TransientMemoryStore memoryStore, ClusterState cluster)
        {
            _memoryStore = memoryStore;
            _cluster = cluster;
        }

        /// <summary>Current connection ids of a user (used to push handed-off payloads).</summary>
        public static string[] GetConnectionIds(string userId)
        {
            if (UserConnections.TryGetValue(userId, out var connections))
            {
                lock (connections)
                {
                    return connections.ToArray();
                }
            }
            return Array.Empty<string>();
        }

        public override async Task OnConnectedAsync()
        {
            if (_cluster.Draining)
            {
                // Handing off to the peer node: refuse so the client fails over there.
                Context.Abort();
                return;
            }
            _cluster.Track(Context);

            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var username = Context.User?.FindFirst(ClaimTypes.Name)?.Value ?? Context.User?.FindFirst("username")?.Value;

            if (!string.IsNullOrEmpty(userId))
            {
                var connections = UserConnections.GetOrAdd(userId, _ => new HashSet<string>());
                lock (connections)
                {
                    connections.Add(Context.ConnectionId);
                }

                // Deliver any pending offline payloads immediately
                var pendingPayloads = _memoryStore.GetPendingPayloadsForUser(userId);
                foreach (var payload in pendingPayloads)
                {
                    await Clients.Caller.ReceivePayload(payload);
                }
            }

            await base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            _cluster.Untrack(Context.ConnectionId);
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var username = Context.User?.FindFirst(ClaimTypes.Name)?.Value ?? Context.User?.FindFirst("username")?.Value;

            if (!string.IsNullOrEmpty(userId) && UserConnections.TryGetValue(userId, out var connections))
            {
                lock (connections)
                {
                    connections.Remove(Context.ConnectionId);
                    if (connections.Count == 0)
                    {
                        UserConnections.TryRemove(userId, out _);
                        if (!string.IsNullOrEmpty(username))
                        {
                            // Remove from active usernames only if all devices disconnected
                            ActiveUsernames.TryRemove(username, out _);
                        }
                    }
                }
            }

            return base.OnDisconnectedAsync(exception);
        }

        // Clients still call this to publish their PublicKey, but identity is validated against the JWT
        public Task RegisterUser(UserIdentityDto identity)
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            
            if (userId != identity.UserId)
            {
                throw new HubException("Unauthorized: Token UserId does not match the provided Identity UserId.");
            }

            // Remove any previous username mapping for this UserId if the user renamed their account
            foreach (var kvp in ActiveUsernames)
            {
                if (kvp.Value.UserId == identity.UserId && !string.Equals(kvp.Key, identity.Username, StringComparison.OrdinalIgnoreCase))
                {
                    ActiveUsernames.TryRemove(kvp.Key, out _);
                }
            }

            ActiveUsernames[identity.Username] = identity;
            return Task.CompletedTask;
        }

        public async Task SendPayload(EncryptedPayloadDto payload)
        {
            var authenticatedUserId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(authenticatedUserId))
            {
                payload.SenderId = authenticatedUserId;
            }

            if (payload.MediaType == Pocket.Shared.Enums.MediaType.PostDeleted && payload.MediaPostId.HasValue)
            {
                _memoryStore.RemovePostFromUserQueue(payload.RecipientId, payload.SenderId, payload.MediaPostId.Value);
            }

            if (payload.MediaType == Pocket.Shared.Enums.MediaType.MessageDeleted && payload.MediaPostId.HasValue)
            {
                if (_memoryStore.RemoveMessage(payload.RecipientId, payload.MediaPostId.Value))
                {
                    // Original message was still pending in RAM and never delivered; no need to notify recipient.
                    return;
                }
            }

            // Store transiently in memory until recipient explicitly confirms delivery (BR-201, BR-202)
            _memoryStore.EnqueuePayload(payload);

            if (UserConnections.TryGetValue(payload.RecipientId, out var connections) && connections.Count > 0)
            {
                string[] targetIds;
                lock (connections)
                {
                    targetIds = connections.ToArray();
                }

                foreach (var connectionId in targetIds)
                {
                    await Clients.Client(connectionId).ReceivePayload(payload);
                }
            }
        }

        public async Task ConfirmDelivery(string senderId, Guid messageId)
        {
            var recipientId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            
            if (!string.IsNullOrEmpty(recipientId))
            {
                _memoryStore.RemoveMessage(recipientId, messageId);
            }

            if (UserConnections.TryGetValue(senderId, out var senderConnections))
            {
                string[] targetIds;
                lock (senderConnections)
                {
                    targetIds = senderConnections.ToArray();
                }

                foreach (var senderConnectionId in targetIds)
                {
                    await Clients.Client(senderConnectionId).AcknowledgeDelivery(messageId);
                }
            }
        }

        public Task<UserIdentityDto?> LookupUser(string username)
        {
            if (ActiveUsernames.TryGetValue(username, out var identity))
            {
                return Task.FromResult<UserIdentityDto?>(identity);
            }
            return Task.FromResult<UserIdentityDto?>(null);
        }

        public async Task SendFriendRequest(FriendRequestDto request)
        {
            if (UserConnections.TryGetValue(request.RecipientId, out var connections))
            {
                string[] targetIds;
                lock (connections)
                {
                    targetIds = connections.ToArray();
                }

                foreach (var connectionId in targetIds)
                {
                    await Clients.Client(connectionId).ReceiveFriendRequest(request);
                }
            }
        }

        public async Task SendFriendAccept(FriendAcceptDto accept)
        {
            if (UserConnections.TryGetValue(accept.RecipientId, out var connections))
            {
                string[] targetIds;
                lock (connections)
                {
                    targetIds = connections.ToArray();
                }

                foreach (var connectionId in targetIds)
                {
                    await Clients.Client(connectionId).ReceiveFriendAccept(accept);
                }
            }
        }
    }
}

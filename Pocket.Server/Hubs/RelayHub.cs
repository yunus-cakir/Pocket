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

        public RelayHub(TransientMemoryStore memoryStore)
        {
            _memoryStore = memoryStore;
        }

        public override async Task OnConnectedAsync()
        {
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

            ActiveUsernames[identity.Username] = identity;
            return Task.CompletedTask;
        }

        public async Task SendPayload(EncryptedPayloadDto payload)
        {
            if (UserConnections.TryGetValue(payload.RecipientId, out var connections) && connections.Count > 0)
            {
                // Recipient is online on one or more devices - relay directly to all
                lock (connections)
                {
                    foreach (var connectionId in connections)
                    {
                        Clients.Client(connectionId).ReceivePayload(payload);
                    }
                }
            }
            else
            {
                // Recipient is completely offline - store transiently in memory
                _memoryStore.EnqueuePayload(payload);
            }
        }

        public Task ConfirmDelivery(string senderId, Guid messageId)
        {
            var recipientId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            
            if (!string.IsNullOrEmpty(recipientId))
            {
                _memoryStore.RemoveMessage(recipientId, messageId);
            }

            if (UserConnections.TryGetValue(senderId, out var senderConnections))
            {
                lock (senderConnections)
                {
                    foreach (var senderConnectionId in senderConnections)
                    {
                        Clients.Client(senderConnectionId).AcknowledgeDelivery(messageId);
                    }
                }
            }

            return Task.CompletedTask;
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
                lock (connections)
                {
                    foreach (var connectionId in connections)
                    {
                        Clients.Client(connectionId).ReceiveFriendRequest(request);
                    }
                }
            }
        }

        public async Task SendFriendAccept(FriendAcceptDto accept)
        {
            if (UserConnections.TryGetValue(accept.RecipientId, out var connections))
            {
                lock (connections)
                {
                    foreach (var connectionId in connections)
                    {
                        Clients.Client(connectionId).ReceiveFriendAccept(accept);
                    }
                }
            }
        }
    }
}

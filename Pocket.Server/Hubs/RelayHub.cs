using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Pocket.Shared.DTOs;
using Pocket.Shared.Interfaces;
using Pocket.Server.Services;

namespace Pocket.Server.Hubs
{
    public class RelayHub : Hub<IRelayClient>
    {
        private static readonly ConcurrentDictionary<string, string> UserConnections = new();
        private static readonly ConcurrentDictionary<string, UserIdentityDto> ActiveUsernames = new(StringComparer.OrdinalIgnoreCase);
        private readonly TransientMemoryStore _memoryStore;

        public RelayHub(TransientMemoryStore memoryStore)
        {
            _memoryStore = memoryStore;
        }

        public async Task RegisterUser(UserIdentityDto identity)
        {
            UserConnections[identity.UserId] = Context.ConnectionId;
            ActiveUsernames[identity.Username] = identity;
            Context.Items["UserId"] = identity.UserId;
            Context.Items["Username"] = identity.Username;

            // Deliver any pending offline payloads immediately and purge them
            var pendingPayloads = _memoryStore.DequeuePayloadsForUser(identity.UserId);
            foreach (var payload in pendingPayloads)
            {
                await Clients.Caller.ReceivePayload(payload);
            }
        }

        public async Task SendPayload(EncryptedPayloadDto payload)
        {
            if (UserConnections.TryGetValue(payload.RecipientId, out var connectionId))
            {
                // Recipient is online - relay directly
                await Clients.Client(connectionId).ReceivePayload(payload);
            }
            else
            {
                // Recipient is offline - store transiently in memory
                _memoryStore.EnqueuePayload(payload);
            }
        }

        public Task ConfirmDelivery(string senderId, Guid messageId)
        {
            // Immediate purge confirmation as per BR-202
            if (Context.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is string recipientId)
            {
                _memoryStore.RemoveMessage(recipientId, messageId);
            }

            if (UserConnections.TryGetValue(senderId, out var senderConnectionId))
            {
                Clients.Client(senderConnectionId).AcknowledgeDelivery(messageId);
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
            if (UserConnections.TryGetValue(request.RecipientId, out var connectionId))
            {
                await Clients.Client(connectionId).ReceiveFriendRequest(request);
            }
        }

        public async Task SendFriendAccept(FriendAcceptDto accept)
        {
            if (UserConnections.TryGetValue(accept.RecipientId, out var connectionId))
            {
                await Clients.Client(connectionId).ReceiveFriendAccept(accept);
            }
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            if (Context.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is string userId)
            {
                UserConnections.TryRemove(userId, out _);
            }
            if (Context.Items.TryGetValue("Username", out var usernameObj) && usernameObj is string username)
            {
                ActiveUsernames.TryRemove(username, out _);
            }
            return superOnDisconnected(exception);
        }

        private Task superOnDisconnected(Exception? exception) => base.OnDisconnectedAsync(exception);
    }
}

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
        private readonly TransientMemoryStore _memoryStore;

        public RelayHub(TransientMemoryStore memoryStore)
        {
            _memoryStore = memoryStore;
        }

        public async Task RegisterUser(string userId)
        {
            UserConnections[userId] = Context.ConnectionId;
            Context.Items["UserId"] = userId;

            // Deliver any pending offline payloads immediately and purge them
            var pendingPayloads = _memoryStore.DequeuePayloadsForUser(userId);
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

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            if (Context.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is string userId)
            {
                UserConnections.TryRemove(userId, out _);
            }
            return superOnDisconnected(exception);
        }

        private Task superOnDisconnected(Exception? exception) => base.OnDisconnectedAsync(exception);
    }
}

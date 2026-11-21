using System.Collections.Concurrent;
using Pocket.Shared.DTOs;

namespace Pocket.Server.Services
{
    /// <summary>
    /// Holds encrypted transient payloads in-memory ONLY until recipients pull/receive them.
    /// Strictly abides by BR-201 (Zero Persistent Cloud) & BR-202 (Delivery & Purge).
    /// </summary>
    public class TransientMemoryStore
    {
        // Outer key: UserId, Inner key: MessageId
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, EncryptedPayloadDto>> _pendingPayloads = new();

        public void EnqueuePayload(EncryptedPayloadDto payload)
        {
            var userMessages = _pendingPayloads.GetOrAdd(payload.RecipientId, _ => new ConcurrentDictionary<Guid, EncryptedPayloadDto>());
            userMessages[payload.MessageId] = payload;
        }

        /// <summary>
        /// Reads pending payloads without removing them (to prevent data loss if connection drops).
        /// Messages are only removed when explicitly confirmed via RemoveMessage.
        /// </summary>
        public List<EncryptedPayloadDto> GetPendingPayloadsForUser(string userId)
        {
            if (_pendingPayloads.TryGetValue(userId, out var userMessages))
            {
                // Return a snapshot of current messages
                return userMessages.Values.ToList();
            }
            return new List<EncryptedPayloadDto>();
        }

        /// <summary>Snapshot of every undelivered payload (used for standby → primary handoff).</summary>
        public List<EncryptedPayloadDto> SnapshotAll()
        {
            return _pendingPayloads.Values.SelectMany(m => m.Values).ToList();
        }

        /// <summary>
        /// Safely removes a specific message after it has been confirmed delivered.
        /// Thread-safe and O(1) removal.
        /// </summary>
        public bool RemoveMessage(string userId, Guid messageId)
        {
            if (_pendingPayloads.TryGetValue(userId, out var userMessages))
            {
                return userMessages.TryRemove(messageId, out _);
            }
            return false;
        }

        // Delivery receipts for senders that were offline when the recipient confirmed.
        // Outer key: sender UserId, inner key: (MessageId, recipient UserId). Ids only, no content (BR-201).
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<(Guid MessageId, string RecipientId), byte>> _pendingAcks = new();

        public void EnqueueAck(string senderId, Guid messageId, string recipientId)
        {
            var acks = _pendingAcks.GetOrAdd(senderId, _ => new ConcurrentDictionary<(Guid, string), byte>());
            acks[(messageId, recipientId)] = 0;
        }

        /// <summary>Removes and returns the receipts waiting for a sender.</summary>
        public List<(Guid MessageId, string RecipientId)> TakeAcks(string senderId)
        {
            return _pendingAcks.TryRemove(senderId, out var acks)
                ? acks.Keys.ToList()
                : new List<(Guid, string)>();
        }

        public void RemovePostFromUserQueue(string userId, string senderId, Guid postId)
        {
            if (_pendingPayloads.TryGetValue(userId, out var userMessages))
            {
                userMessages.TryRemove(postId, out _);
                foreach (var kvp in userMessages.ToArray())
                {
                    if (kvp.Value.SenderId == senderId && kvp.Value.MediaPostId == postId &&
                        kvp.Value.MediaType != Pocket.Shared.Enums.MediaType.PostDeleted)
                    {
                        userMessages.TryRemove(kvp.Key, out _);
                    }
                }
            }
        }
    }
}

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
    }
}

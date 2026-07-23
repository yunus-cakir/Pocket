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
        private readonly ConcurrentDictionary<string, ConcurrentQueue<EncryptedPayloadDto>> _pendingPayloads = new();

        public void EnqueuePayload(EncryptedPayloadDto payload)
        {
            var queue = _pendingPayloads.GetOrAdd(payload.RecipientId, _ => new ConcurrentQueue<EncryptedPayloadDto>());
            queue.Enqueue(payload);
        }

        public List<EncryptedPayloadDto> DequeuePayloadsForUser(string userId)
        {
            if (_pendingPayloads.TryRemove(userId, out var queue))
            {
                return queue.ToList();
            }
            return new List<EncryptedPayloadDto>();
        }

        public bool RemoveMessage(string userId, Guid messageId)
        {
            if (_pendingPayloads.TryGetValue(userId, out var queue))
            {
                var remaining = queue.Where(p => p.MessageId != messageId).ToList();
                _pendingPayloads[userId] = new ConcurrentQueue<EncryptedPayloadDto>(remaining);
                return true;
            }
            return false;
        }
    }
}

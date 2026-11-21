using Pocket.Shared.Enums;

namespace Pocket.Shared.DTOs
{
    public class EncryptedPayloadDto
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public string SenderId { get; set; } = string.Empty;
        public string RecipientId { get; set; } = string.Empty;
        public Guid? MediaPostId { get; set; }
        public byte[] EncryptedContent { get; set; } = Array.Empty<byte>();
        public byte[]? EncryptedReaction { get; set; }
        public byte[]? EncryptedMedia { get; set; }
        public MediaType MediaType { get; set; } = MediaType.None;
        /// <summary>When the sender wrote it, by the sender's (server-corrected) clock.</summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Set by the relay when it first accepted the payload (clients cannot set it). Null from older relays;
        /// older clients ignore it.
        /// </summary>
        public DateTime? ServerReceivedAtUtc { get; set; }
    }
}

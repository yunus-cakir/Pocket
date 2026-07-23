using Pocket.Shared.Enums;

namespace Pocket.Shared.DTOs
{
    public class EncryptedPayloadDto
    {
        public Guid MessageId { get; set; } = Guid.NewGuid();
        public string SenderId { get; set; } = string.Empty;
        public string RecipientId { get; set; } = string.Empty;
        public byte[] EncryptedContent { get; set; } = Array.Empty<byte>();
        public byte[]? EncryptedMedia { get; set; }
        public MediaType MediaType { get; set; } = MediaType.None;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}

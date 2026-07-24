namespace Pocket.Shared.DTOs
{
    public class FriendRequestDto
    {
        public string SenderId { get; set; } = string.Empty;
        public string SenderUsername { get; set; } = string.Empty;
        public string SenderPublicKey { get; set; } = string.Empty;
        public string RecipientId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}

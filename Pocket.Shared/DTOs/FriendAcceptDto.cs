namespace Pocket.Shared.DTOs
{
    public class FriendAcceptDto
    {
        public string SenderId { get; set; } = string.Empty; // Bob accepting the request
        public string SenderPublicKey { get; set; } = string.Empty; // Bob's Public Key
        public string RecipientId { get; set; } = string.Empty; // Alice (who sent the original request)
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}

using SQLite;
using System;

namespace Pocket.Client.Models
{
    public class Friend
    {
        [PrimaryKey]
        public string Id { get; set; } = string.Empty;

        [MaxLength(50), Indexed]
        public string Username { get; set; } = string.Empty;

        [MaxLength(100)]
        public string DisplayName { get; set; } = string.Empty;

        public string PublicKey { get; set; } = string.Empty;

        public bool IsMuted { get; set; }

        public DateTime AddedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastInteractionAt { get; set; } = DateTime.UtcNow;
    }
}

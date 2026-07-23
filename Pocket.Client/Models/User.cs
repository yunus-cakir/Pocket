using SQLite;
using System;

namespace Pocket.Client.Models
{
    public class User
    {
        [PrimaryKey]
        public string Id { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [MaxLength(100)]
        public string DisplayName { get; set; } = string.Empty;

        public string PublicKey { get; set; } = string.Empty;

        public string EncryptedPrivateKey { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

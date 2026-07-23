using SQLite;
using System;
using Pocket.Shared.Enums;

namespace Pocket.Client.Models
{
    public class DirectMessage
    {
        [PrimaryKey]
        public Guid Id { get; set; }

        [Indexed]
        public Guid? MediaPostId { get; set; }

        [Indexed]
        public string FriendId { get; set; } = string.Empty;

        public bool IsIncoming { get; set; }

        public string? TextContent { get; set; }

        public string? ReactionEmoji { get; set; }

        [Indexed]
        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;
    }
}

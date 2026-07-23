using SQLite;
using System;
using Pocket.Shared.Enums;

namespace Pocket.Client.Models
{
    public class MediaPost
    {
        [PrimaryKey]
        public Guid Id { get; set; }

        [Indexed]
        public string FriendId { get; set; } = string.Empty;

        public bool IsIncoming { get; set; }

        public string MediaLocalPath { get; set; } = string.Empty;
        
        public MediaType MediaType { get; set; } = MediaType.None;

        public string? Caption { get; set; }

        public bool IsIncludedInRollcall { get; set; }

        [Indexed]
        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        public SyncStatus SyncStatus { get; set; } = SyncStatus.Pending;
    }
}

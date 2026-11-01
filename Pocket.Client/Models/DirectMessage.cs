using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Controls;
using SQLite;
using System;
using Pocket.Shared.Enums;

namespace Pocket.Client.Models
{
    public partial class DirectMessage : ObservableObject
    {
        private SyncStatus _syncStatus = SyncStatus.Pending;
        private string _referencedPostPath = string.Empty;
        private string _referencedPostCaption = string.Empty;

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

        public SyncStatus SyncStatus
        {
            get => _syncStatus;
            set
            {
                if (SetProperty(ref _syncStatus, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        [Ignore]
        public bool HasMediaReference => MediaPostId.HasValue && !string.IsNullOrEmpty(ReferencedPostPath);

        [Ignore]
        public string ReferencedPostPath
        {
            get => _referencedPostPath;
            set
            {
                if (SetProperty(ref _referencedPostPath, value))
                {
                    OnPropertyChanged(nameof(HasMediaReference));
                }
            }
        }

        [Ignore]
        public string ReferencedPostCaption
        {
            get => string.IsNullOrWhiteSpace(_referencedPostCaption)
                ? "📸 Fotoğrafa yanıt"
                : $"📸 {_referencedPostCaption}";
            set => SetProperty(ref _referencedPostCaption, value);
        }

        [Ignore]
        public bool HasReaction => !string.IsNullOrWhiteSpace(ReactionEmoji);

        [Ignore]
        public bool HasText => !string.IsNullOrWhiteSpace(TextContent);

        [Ignore]
        public bool IsPostReactionOnly => !string.IsNullOrWhiteSpace(ReactionEmoji) && string.IsNullOrWhiteSpace(TextContent);

        [Ignore]
        public LayoutOptions BubbleAlignment => IsIncoming ? LayoutOptions.Start : LayoutOptions.End;

        [Ignore]
        public string BubbleColor => IsIncoming ? "#1C1C1E" : "#2C2C2E";

        [Ignore]
        public string StatusText => IsIncoming
            ? $"🔐 E2EE Çözüldü • {SentAt.ToLocalTime():HH:mm}"
            : SyncStatus switch
            {
                SyncStatus.Pending => $"⏳ Bekliyor • {SentAt.ToLocalTime():HH:mm}",
                SyncStatus.Sent => $"🔒 E2EE Gönderildi • {SentAt.ToLocalTime():HH:mm}",
                SyncStatus.Delivered => $"✅ E2EE İletildi & Silindi • {SentAt.ToLocalTime():HH:mm}",
                SyncStatus.Failed => $"❌ Başarısız • {SentAt.ToLocalTime():HH:mm}",
                _ => string.Empty
            };
    }
}

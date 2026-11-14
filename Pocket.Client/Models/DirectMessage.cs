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
        private string _referencedPostAuthorLabel = "You";
        private string _referencedPostAuthorAvatarUrl = string.Empty;
        private string _referencedPostTimeText = string.Empty;
        private string _senderAvatarUrl = string.Empty;
        private bool _showMediaCard = true;

        [PrimaryKey]
        public Guid Id { get; set; }

        [Indexed]
        public Guid? MediaPostId { get; set; }

        [Indexed]
        public string FriendId { get; set; } = string.Empty;

        public bool IsIncoming { get; set; }

        [Ignore]
        public bool IsOutgoing => !IsIncoming;

        public string? TextContent { get; set; }

        public string? ReactionEmoji { get; set; }

        public bool IsReactionAnimationShown { get; set; }

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
        public bool ShowMediaCard
        {
            get => HasMediaReference && _showMediaCard;
            set => SetProperty(ref _showMediaCard, value);
        }

        [Ignore]
        public string ReferencedPostPath
        {
            get => _referencedPostPath;
            set
            {
                if (SetProperty(ref _referencedPostPath, value))
                {
                    OnPropertyChanged(nameof(HasMediaReference));
                    OnPropertyChanged(nameof(ShowMediaCard));
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
        public string ReferencedPostAuthorLabel
        {
            get => _referencedPostAuthorLabel;
            set => SetProperty(ref _referencedPostAuthorLabel, value);
        }

        [Ignore]
        public string ReferencedPostAuthorAvatarUrl
        {
            get => _referencedPostAuthorAvatarUrl;
            set => SetProperty(ref _referencedPostAuthorAvatarUrl, value);
        }

        [Ignore]
        public string ReferencedPostTimeText
        {
            get => _referencedPostTimeText;
            set => SetProperty(ref _referencedPostTimeText, value);
        }

        [Ignore]
        public string SenderAvatarUrl
        {
            get => _senderAvatarUrl;
            set => SetProperty(ref _senderAvatarUrl, value);
        }

        [Ignore]
        public bool HasReaction => !string.IsNullOrWhiteSpace(ReactionEmoji);

        [Ignore]
        public bool HasText => !string.IsNullOrWhiteSpace(TextContent);

        [Ignore]
        public bool HasReactionAndText => HasReaction && HasText;

        [Ignore]
        public bool HasReactionOnlyInChat => HasReaction && !HasText;

        [Ignore]
        public string DisplayText => HasText ? TextContent! : (ReactionEmoji ?? string.Empty);

        [Ignore]
        public bool IsEmojiOnly => !HasReactionAndText && IsOnlyEmojis(DisplayText);

        public static bool IsOnlyEmojis(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            text = text.Trim();

            if (text.Length > 60)
                return false;

            int emojiCount = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (!System.Text.Rune.TryGetRuneAt(text, i, out var rune))
                    return false;

                i += rune.Utf16SequenceLength - 1;

                if (System.Text.Rune.IsWhiteSpace(rune))
                    continue;

                int val = rune.Value;

                // Modifiers / joiners / selectors
                if (val is 0x200D or 0x200C or 0x20E3 or >= 0xFE00 and <= 0xFE0F or >= 0xE0100 and <= 0xE01EF or >= 0x1F3FB and <= 0x1F3FF)
                    continue;

                // Emoji Unicode ranges
                if (val is >= 0x1F600 and <= 0x1F64F or // Emoticons
                           >= 0x1F300 and <= 0x1F5FF or // Misc Symbols and Pictographs
                           >= 0x1F680 and <= 0x1F6FF or // Transport and Map
                           >= 0x1F700 and <= 0x1F77F or // Alchemical Symbols
                           >= 0x1F780 and <= 0x1F7FF or // Geometric Shapes Extended
                           >= 0x1F800 and <= 0x1F8FF or // Supplemental Arrows-C
                           >= 0x1F900 and <= 0x1F9FF or // Supplemental Symbols and Pictographs
                           >= 0x1FA00 and <= 0x1FAFF or // Symbols and Pictographs Extended-A
                           >= 0x1F1E6 and <= 0x1F1FF or // Regional Indicator Symbols (Flags)
                           >= 0x2600 and <= 0x26FF or   // Misc symbols
                           >= 0x2700 and <= 0x27BF or   // Dingbats
                           >= 0x2300 and <= 0x23FF or   // Misc Technical
                           >= 0x2B00 and <= 0x2BFF or   // Misc Symbols and Arrows
                           >= 0x25A0 and <= 0x25FF or   // Geometric Shapes
                           >= 0x2900 and <= 0x297F or   // Supplemental Arrows-B
                           >= 0x3030 and <= 0x303D or
                           >= 0x3297 and <= 0x3299 or
                           >= 0x1F200 and <= 0x1F2FF or
                           0x00A9 or 0x00AE or 0x203C or 0x2049 or 0x2122 or 0x2139)
                {
                    emojiCount++;
                    continue;
                }

                return false;
            }

            return emojiCount > 0 && emojiCount <= 5;
        }

        [Ignore]
        public bool IsPostReactionOnly => MediaPostId.HasValue && !string.IsNullOrWhiteSpace(ReactionEmoji) && string.IsNullOrWhiteSpace(TextContent);

        [Ignore]
        public LayoutOptions BubbleAlignment => IsIncoming ? LayoutOptions.Start : LayoutOptions.End;

        [Ignore]
        public string BubbleColor => IsIncoming ? "#262629" : "#EBEBF0";

        [Ignore]
        public string BubbleTextColor => IsIncoming ? "#FFFFFF" : "#0A0A0A";

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

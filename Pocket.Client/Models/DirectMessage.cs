using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Controls;
using SQLite;
using System;
using Pocket.Shared.Enums;

namespace Pocket.Client.Models
{
    public partial class DirectMessage : ObservableObject, Pocket.Client.Services.ISendStateTicking
    {
        private bool _lastIsSlowPending;
        private bool _lastIsGivenUp;
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

        private string? _textContent;

        public string? TextContent
        {
            get => _textContent;
            set
            {
                if (SetProperty(ref _textContent, value))
                {
                    OnPropertyChanged(nameof(HasText));
                    OnPropertyChanged(nameof(DisplayText));
                    OnPropertyChanged(nameof(IsEmojiOnly));
                    OnPropertyChanged(nameof(HasReactionAndText));
                    OnPropertyChanged(nameof(HasReactionOnlyInChat));
                    OnPropertyChanged(nameof(ShowPostReactionBadge));
                    OnPropertyChanged(nameof(QuotedText));
                }
            }
        }

        /// <summary>How long after sending our own message can still be edited.</summary>
        public static readonly TimeSpan EditWindow = TimeSpan.FromHours(1);

        private DateTime? _editedAt;

        /// <summary>When the text was last edited (UTC); null = never edited. Newer edits win.</summary>
        public DateTime? EditedAt
        {
            get => _editedAt;
            set
            {
                if (SetProperty(ref _editedAt, value is DateTime v ? Services.ServerClock.AsUtc(v) : null))
                    OnPropertyChanged(nameof(IsEdited));
            }
        }

        /// <summary>Our edit of this (outgoing) message is not on a server yet: resent on the next connection.</summary>
        public bool EditPending { get; set; }

        [Ignore]
        public bool IsEdited => EditedAt.HasValue;

        /// <summary>Our own text message, sent less than <see cref="EditWindow"/> ago.</summary>
        [Ignore]
        public bool CanEdit => IsOutgoing && HasText && !IsPostReactionOnly &&
                               Services.ServerClock.UtcNow - SentAt < EditWindow;

        private bool _isDimmed;

        /// <summary>Another message is being edited: this row fades into the background.</summary>
        [Ignore]
        public bool IsDimmed
        {
            get => _isDimmed;
            set
            {
                if (SetProperty(ref _isDimmed, value))
                    OnPropertyChanged(nameof(RowOpacity));
            }
        }

        [Ignore]
        public double RowOpacity => IsDimmed ? 0.28 : 1.0;

        public string? ReactionEmoji { get; set; }

        public bool IsReactionAnimationShown { get; set; }

        private string? _messageReaction;
        private DateTime? _messageReactionAt;

        /// <summary>
        /// Emoji reaction ON this message (1:1 chat: on an incoming message it is ours, on an outgoing one the friend's).
        /// Not to be confused with <see cref="ReactionEmoji"/>, which is a photo reaction carried by this message.
        /// </summary>
        public string? MessageReaction
        {
            get => _messageReaction;
            set
            {
                if (SetProperty(ref _messageReaction, string.IsNullOrWhiteSpace(value) ? null : value))
                {
                    OnPropertyChanged(nameof(HasMessageReaction));
                    OnPropertyChanged(nameof(ShowPostReactionBadge));
                    OnPropertyChanged(nameof(RowPadding));
                }
            }
        }

        /// <summary>When <see cref="MessageReaction"/> was last set or removed (UTC); newer reaction payloads win.</summary>
        public DateTime? MessageReactionAt
        {
            get => _messageReactionAt;
            set => _messageReactionAt = value is DateTime v ? Services.ServerClock.AsUtc(v) : null;
        }

        /// <summary>Our reaction on this (incoming) message is not on a server yet: resent on the next connection.</summary>
        public bool MessageReactionPending { get; set; }

        [Ignore]
        public bool HasMessageReaction => !string.IsNullOrWhiteSpace(MessageReaction);

        /// <summary>Extra room above the bubble so the corner reaction badge is not clipped / doesn't touch the previous row.</summary>
        [Ignore]
        public Thickness RowPadding => HasMessageReaction ? new Thickness(12, 18, 12, 3) : new Thickness(12, 3);

        /// <summary>The message text as quoted in reaction previews / notifications.</summary>
        [Ignore]
        public string QuotedText
        {
            get
            {
                var text = (DisplayText ?? string.Empty).Replace('\n', ' ').Trim();
                if (string.IsNullOrEmpty(text))
                    text = "photo";
                var info = new System.Globalization.StringInfo(text);
                return info.LengthInTextElements > 40 ? info.SubstringByTextElements(0, 40).TrimEnd() + "…" : text;
            }
        }

        /// <summary>"You reacted 🔥 to "eee"" / "Reacted 🔥 to your message: "eee"".</summary>
        [Ignore]
        public string MessageReactionPreviewText => IsIncoming
            ? $"You reacted {MessageReaction} to \"{QuotedText}\""
            : $"Reacted {MessageReaction} to your message: \"{QuotedText}\"";

        private DateTime _sentAt = Services.ServerClock.UtcNow;
        private DateTime? _serverReceivedAt;

        /// <summary>When it was sent (UTC; sqlite-net reads ticks back as Unspecified, so the setter marks it UTC).</summary>
        [Indexed]
        public DateTime SentAt
        {
            get => _sentAt;
            set => _sentAt = Services.ServerClock.AsUtc(value);
        }

        /// <summary>Incoming: when the relay accepted it (null from older relays).</summary>
        public DateTime? ServerReceivedAt
        {
            get => _serverReceivedAt;
            set => _serverReceivedAt = value is DateTime v ? Services.ServerClock.AsUtc(v) : null;
        }

        /// <summary>Relay process that accepted this outgoing message (resent if we later connect to another).</summary>
        public string? ServerInstance { get; set; }

        public SyncStatus SyncStatus
        {
            get => _syncStatus;
            set
            {
                if (SetProperty(ref _syncStatus, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    NotifySendStateChanged();
                }
            }
        }

        private TimeSpan PendingFor => Services.ServerClock.UtcNow - SentAt;

        /// <summary>Outgoing and not yet accepted by a server (whether still retried or given up).</summary>
        [Ignore]
        public bool IsUnsentOutgoing => !IsIncoming && SyncStatus == SyncStatus.Pending;

        /// <summary>Not on a server after <see cref="Services.SendTiming.MessageGiveUpAfter"/>: no more automatic resends.</summary>
        [Ignore]
        public bool IsGivenUp => IsUnsentOutgoing && PendingFor >= Pocket.Client.Services.SendTiming.MessageGiveUpAfter;

        /// <summary>Still being retried, but slower than a normal send → yellow hourglass beside the bubble.</summary>
        [Ignore]
        public bool IsSlowPending => IsUnsentOutgoing && !IsGivenUp && PendingFor >= Pocket.Client.Services.SendTiming.SlowAfter;

        /// <summary>Needs the user: red outline with retry / delete.</summary>
        [Ignore]
        public bool IsFailed => !IsIncoming && (SyncStatus == SyncStatus.Failed || IsGivenUp);

        /// <summary>Delivery state of an outgoing message, in the same words as the photo Delivery list.</summary>
        [Ignore]
        public string DeliveryStatusText => IsFailed
            ? "Not sent"
            : SyncStatus switch
            {
                SyncStatus.Delivered => "Sent",
                SyncStatus.Sent => "Waiting to Receive",
                _ => Pocket.Client.PageModels.AppShellModel.HasNetworkAccess() ? "Waiting for Server" : "Waiting for Internet"
            };

        [Ignore]
        public bool NeedsSendStateTick => IsUnsentOutgoing && !IsGivenUp;

        public void RefreshSendState()
        {
            if (_lastIsSlowPending != IsSlowPending || _lastIsGivenUp != IsGivenUp)
                NotifySendStateChanged();
        }

        private void NotifySendStateChanged()
        {
            _lastIsSlowPending = IsSlowPending;
            _lastIsGivenUp = IsGivenUp;
            OnPropertyChanged(nameof(IsSlowPending));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(DeliveryStatusText));
            OnPropertyChanged(nameof(OutgoingBubbleStrokeThickness));
            OnPropertyChanged(nameof(OutgoingBubbleStrokeColor));
        }

        [Ignore]
        public double OutgoingBubbleStrokeThickness => IsFailed ? 2.0 : 0.0;

        [Ignore]
        public string OutgoingBubbleStrokeColor => IsFailed ? "#FF453A" : "Transparent";

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
                ? "📸 Replied to a photo"
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

        /// <summary>Photo-reaction corner badge; a message reaction takes the same corner and wins.</summary>
        [Ignore]
        public bool ShowPostReactionBadge => HasReactionAndText && !HasMessageReaction;

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
            ? $"🔐 E2EE Decrypted • {SentAt.ToLocalTime():HH:mm}{MediaPost.ArrivalSuffix(SentAt, ServerReceivedAt)}"
            : SyncStatus switch
            {
                SyncStatus.Pending => $"⏳ Pending • {SentAt.ToLocalTime():HH:mm}",
                SyncStatus.Sent => $"🔒 E2EE Sent • {SentAt.ToLocalTime():HH:mm}",
                SyncStatus.Delivered => $"✅ E2EE Delivered & Deleted • {SentAt.ToLocalTime():HH:mm}",
                SyncStatus.Failed => $"❌ Failed • {SentAt.ToLocalTime():HH:mm}",
                _ => string.Empty
            };
    }
}

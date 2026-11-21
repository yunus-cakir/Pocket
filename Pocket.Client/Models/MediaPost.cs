using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SQLite;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Pocket.Shared.Enums;

namespace Pocket.Client.Models
{
    public partial class PostActivityItem : ObservableObject
    {
        private string _friendId = string.Empty;
        private string _friendName = string.Empty;
        private string _friendAvatarUrl = string.Empty;
        private string _emojisText = string.Empty;
        private string _latestEmoji = string.Empty;

        public List<string> Emojis { get; } = new();

        public string FriendId
        {
            get => _friendId;
            set => SetProperty(ref _friendId, value);
        }

        public string FriendName
        {
            get => _friendName;
            set => SetProperty(ref _friendName, value);
        }

        public string FriendAvatarUrl
        {
            get => _friendAvatarUrl;
            set => SetProperty(ref _friendAvatarUrl, value);
        }

        public string EmojisText
        {
            get => _emojisText;
            set => SetProperty(ref _emojisText, value);
        }

        public string LatestEmoji
        {
            get => _latestEmoji;
            set => SetProperty(ref _latestEmoji, value);
        }

        public void AddEmoji(string emoji)
        {
            if (string.IsNullOrWhiteSpace(emoji))
                return;
            Emojis.Remove(emoji);
            Emojis.Insert(0, emoji);
            while (Emojis.Count > 3)
            {
                Emojis.RemoveAt(Emojis.Count - 1);
            }
            EmojisText = string.Join("  ", Emojis);
            LatestEmoji = emoji;
        }
    }

    public partial class MediaPost : ObservableObject, Pocket.Client.Services.ISendStateTicking
    {
        private SyncStatus _syncStatus = SyncStatus.Pending;
        private bool _isWaitingForServer;
        private string _senderDisplayName = string.Empty;
        private double _viewportHeight = 820;
        private double _cardSize = 399;
        private double _gridItemSize = 130;
        private double _topHeaderHeight = 140;
        private double _feedBottomSpacerHeight = 136;

        [Ignore]
        public double ViewportHeight
        {
            get => _viewportHeight;
            set => SetProperty(ref _viewportHeight, value);
        }

        [Ignore]
        public double CardSize
        {
            get => _cardSize;
            set => SetProperty(ref _cardSize, value);
        }

        [Ignore]
        public double GridItemSize
        {
            get => _gridItemSize;
            set => SetProperty(ref _gridItemSize, value);
        }

        [Ignore]
        public double TopHeaderHeight
        {
            get => _topHeaderHeight;
            set => SetProperty(ref _topHeaderHeight, value);
        }

        [Ignore]
        public double FeedBottomSpacerHeight
        {
            get => _feedBottomSpacerHeight;
            set => SetProperty(ref _feedBottomSpacerHeight, value);
        }

        [Ignore]
        public Action<MediaPost, string?>? OnReplyOrReactRequested { get; set; }

        [Ignore]
        public Action<MediaPost>? OnGridPostTapped { get; set; }

        [RelayCommand]
        private void SelectFromGrid() => OnGridPostTapped?.Invoke(this);

        [RelayCommand]
        private void Reply() => OnReplyOrReactRequested?.Invoke(this, null);

        [RelayCommand]
        private void ReactHeart() => OnReplyOrReactRequested?.Invoke(this, "💗");

        [RelayCommand]
        private void ReactFire() => OnReplyOrReactRequested?.Invoke(this, "🙃");

        [RelayCommand]
        private void ReactLaugh() => OnReplyOrReactRequested?.Invoke(this, "😋");

        [PrimaryKey]
        public Guid Id { get; set; }

        [Indexed]
        public string FriendId { get; set; } = string.Empty;

        public bool IsIncoming { get; set; }

        [Ignore]
        public bool IsOwnPost => !IsIncoming;

        [Ignore]
        public ObservableCollection<PostActivityItem> ActivityItems { get; } = new();

        [Ignore]
        public List<(Guid DmId, string Emoji)> PendingReactionAnimations { get; } = new();

        [Ignore]
        public HashSet<Guid> ShownReactionDmIds { get; } = new();

        public void EnqueuePendingReaction(Guid dmId, string emoji)
        {
            if (string.IsNullOrWhiteSpace(emoji) || ShownReactionDmIds.Contains(dmId))
                return;
            if (PendingReactionAnimations.All(x => x.DmId != dmId))
            {
                PendingReactionAnimations.Add((dmId, emoji));
            }
        }

        [Ignore]
        public bool HasActivity => ActivityItems.Count > 0;

        [Ignore]
        public bool HasNoActivity => IsOwnPost && ActivityItems.Count == 0;

        [Ignore]
        public bool ShowActivityPill => IsOwnPost && ActivityItems.Count > 0;

        [Ignore]
        public string LatestActivityAvatarUrl => ActivityItems.FirstOrDefault()?.FriendAvatarUrl ?? string.Empty;

        [Ignore]
        public string ActivityPillText
        {
            get
            {
                var latest = ActivityItems.FirstOrDefault();
                if (latest == null)
                    return string.Empty;

                var firstName = GetFirstWord(latest.FriendName);
                if (string.IsNullOrWhiteSpace(firstName))
                    firstName = "Friend";

                return string.IsNullOrWhiteSpace(latest.LatestEmoji)
                    ? $"{firstName} sent"
                    : $"{firstName} sent {latest.LatestEmoji}";
            }
        }

        private static string GetFirstWord(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : text.Trim();
        }

        public void AddOrUpdateReaction(string friendId, string friendName, string friendAvatarUrl, string emoji)
        {
            if (string.IsNullOrWhiteSpace(emoji))
                return;

            var existing = ActivityItems.FirstOrDefault(a => a.FriendId == friendId);
            if (existing == null)
            {
                existing = new PostActivityItem
                {
                    FriendId = friendId,
                    FriendName = string.IsNullOrWhiteSpace(friendName) ? "Friend" : friendName,
                    FriendAvatarUrl = friendAvatarUrl ?? string.Empty
                };
                existing.AddEmoji(emoji);
                ActivityItems.Insert(0, existing);
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(friendName))
                    existing.FriendName = friendName;
                if (!string.IsNullOrWhiteSpace(friendAvatarUrl))
                    existing.FriendAvatarUrl = friendAvatarUrl;
                existing.AddEmoji(emoji);
                int idx = ActivityItems.IndexOf(existing);
                if (idx > 0)
                {
                    ActivityItems.Move(idx, 0);
                }
            }

            NotifyActivityChanged();
        }

        public void NotifyActivityChanged()
        {
            OnPropertyChanged(nameof(HasActivity));
            OnPropertyChanged(nameof(HasNoActivity));
            OnPropertyChanged(nameof(ShowActivityPill));
            OnPropertyChanged(nameof(LatestActivityAvatarUrl));
            OnPropertyChanged(nameof(ActivityPillText));
        }

        public string MediaLocalPath { get; set; } = string.Empty;
        
        public MediaType MediaType { get; set; } = MediaType.None;

        public string? Caption { get; set; }

        public bool IsIncludedInRollcall { get; set; }

        public bool IsUnseen { get; set; }

        [Indexed]
        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        public SyncStatus SyncStatus
        {
            get => _syncStatus;
            set
            {
                if (SetProperty(ref _syncStatus, value))
                {
                    OnPropertyChanged(nameof(StatusBadgeText));
                    OnPropertyChanged(nameof(IsPendingUpload));
                    OnPropertyChanged(nameof(UploadStatusText));
                    NotifySendStateChanged();
                }
            }
        }

        private bool _lastIsSlowUpload;

        /// <summary>Not on the server a few seconds after sending → yellow hourglass after "You 3h" (still retried).</summary>
        [Ignore]
        public bool IsSlowUpload => IsPendingUpload &&
                                    DateTime.UtcNow - SentAt.ToUniversalTime() >= Pocket.Client.Services.SendTiming.SlowAfter;

        /// <summary>The first seconds of a send: spinner + "Uploading..." in place of the author row.</summary>
        [Ignore]
        public bool ShowUploadingRow => IsPendingUpload && !IsSlowUpload;

        [Ignore]
        public bool NeedsSendStateTick => IsPendingUpload && !IsSlowUpload;

        public void RefreshSendState()
        {
            if (_lastIsSlowUpload != IsSlowUpload)
                NotifySendStateChanged();
        }

        private void NotifySendStateChanged()
        {
            _lastIsSlowUpload = IsSlowUpload;
            OnPropertyChanged(nameof(IsSlowUpload));
            OnPropertyChanged(nameof(ShowUploadingRow));
            OnPropertyChanged(nameof(ShowNormalAuthorRow));
        }

        [Ignore]
        public bool IsWaitingForServer
        {
            get => _isWaitingForServer;
            set
            {
                if (SetProperty(ref _isWaitingForServer, value))
                {
                    OnPropertyChanged(nameof(UploadStatusText));
                }
            }
        }

        [Ignore]
        public bool IsPendingUpload => !IsIncoming && SyncStatus == SyncStatus.Pending;

        [Ignore]
        public bool ShowNormalAuthorRow => !ShowUploadingRow;

        [Ignore]
        public string UploadStatusText => IsWaitingForServer ? "Waiting server..." : "Uploading...";

        [Ignore]
        public string SenderDisplayName
        {
            get => string.IsNullOrWhiteSpace(_senderDisplayName)
                ? (IsIncoming ? "Friend" : "You")
                : _senderDisplayName;
            set
            {
                if (SetProperty(ref _senderDisplayName, value))
                {
                    OnPropertyChanged(nameof(HeaderTitleText));
                    OnPropertyChanged(nameof(AuthorLabel));
                }
            }
        }

        [Ignore]
        public string AuthorLabel => IsIncoming ? SenderDisplayName : "You";

        [Ignore]
        public string RelativeTimeText => FormatRelativeTime(SentAt);

        [Ignore]
        public string HeaderTitleText => IsIncoming
            ? $"🔐 {SenderDisplayName}"
            : $"You";

        [Ignore]
        public string StatusBadgeText => IsIncoming
            ? $"🔒 E2EE • {SentAt.ToLocalTime():HH:mm}"
            : SyncStatus switch
            {
                SyncStatus.Pending => $"⏳ Pending • {SentAt.ToLocalTime():HH:mm}",
                SyncStatus.Sent => $"🔒 Sent • {SentAt.ToLocalTime():HH:mm}",
                SyncStatus.Delivered => $"✅ Delivered • {SentAt.ToLocalTime():HH:mm}",
                SyncStatus.Failed => $"❌ Failed • {SentAt.ToLocalTime():HH:mm}",
                _ => string.Empty
            };

        public static string FormatRelativeTime(DateTime utcTime)
        {
            var elapsed = DateTime.UtcNow - utcTime.ToUniversalTime();
            if (elapsed.TotalMinutes < 1)
                return "1m";
            if (elapsed.TotalHours < 1)
                return $"{(int)Math.Max(1, elapsed.TotalMinutes)}m";
            if (elapsed.TotalDays < 1)
                return $"{(int)Math.Max(1, elapsed.TotalHours)}h";
            return $"{(int)Math.Max(1, elapsed.TotalDays)}d";
        }
    }
}

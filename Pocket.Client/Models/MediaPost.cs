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
        }
    }

    public partial class MediaPost : ObservableObject
    {
        private SyncStatus _syncStatus = SyncStatus.Pending;
        private string _senderDisplayName = string.Empty;
        private double _viewportHeight = 820;
        private double _cardSize = 399;

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
        public Action<MediaPost, string?>? OnReplyOrReactRequested { get; set; }

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
        public bool HasActivity => ActivityItems.Count > 0;

        [Ignore]
        public bool HasNoActivity => IsOwnPost && ActivityItems.Count == 0;

        [Ignore]
        public bool ShowActivityPill => IsOwnPost && ActivityItems.Count > 0;

        [Ignore]
        public string LatestActivityAvatarUrl => ActivityItems.FirstOrDefault()?.FriendAvatarUrl ?? string.Empty;

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
            }

            NotifyActivityChanged();
        }

        public void NotifyActivityChanged()
        {
            OnPropertyChanged(nameof(HasActivity));
            OnPropertyChanged(nameof(HasNoActivity));
            OnPropertyChanged(nameof(ShowActivityPill));
            OnPropertyChanged(nameof(LatestActivityAvatarUrl));
        }

        public string MediaLocalPath { get; set; } = string.Empty;
        
        public MediaType MediaType { get; set; } = MediaType.None;

        public string? Caption { get; set; }

        public bool IsIncludedInRollcall { get; set; }

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
                }
            }
        }

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
            ? $"🔐 @{SenderDisplayName}"
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

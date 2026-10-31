using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SQLite;
using System;
using Pocket.Shared.Enums;

namespace Pocket.Client.Models
{
    public partial class MediaPost : ObservableObject
    {
        private SyncStatus _syncStatus = SyncStatus.Pending;
        private string _senderDisplayName = string.Empty;

        [Ignore]
        public Action<MediaPost, string?>? OnReplyOrReactRequested { get; set; }

        [RelayCommand]
        private void Reply() => OnReplyOrReactRequested?.Invoke(this, null);

        [RelayCommand]
        private void ReactHeart() => OnReplyOrReactRequested?.Invoke(this, "❤️");

        [RelayCommand]
        private void ReactFire() => OnReplyOrReactRequested?.Invoke(this, "🔥");

        [RelayCommand]
        private void ReactLaugh() => OnReplyOrReactRequested?.Invoke(this, "😂");

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
                ? (IsIncoming ? "Arkadaş" : "Sen")
                : _senderDisplayName;
            set
            {
                if (SetProperty(ref _senderDisplayName, value))
                {
                    OnPropertyChanged(nameof(HeaderTitleText));
                }
            }
        }

        [Ignore]
        public string HeaderTitleText => IsIncoming
            ? $"🔐 @{SenderDisplayName}"
            : $"📤 Sen";

        [Ignore]
        public string StatusBadgeText => IsIncoming
            ? $"🔒 E2EE Çözüldü • {SentAt.ToLocalTime():HH:mm}"
            : SyncStatus switch
            {
                SyncStatus.Pending => $"⏳ Yerel Kaydedildi • {SentAt.ToLocalTime():HH:mm}",
                SyncStatus.Sent => $"🔒 E2EE Gönderildi • {SentAt.ToLocalTime():HH:mm}",
                SyncStatus.Delivered => $"✅ E2EE İletildi & Silindi • {SentAt.ToLocalTime():HH:mm}",
                SyncStatus.Failed => $"❌ Gönderim Başarısız • {SentAt.ToLocalTime():HH:mm}",
                _ => string.Empty
            };
    }
}

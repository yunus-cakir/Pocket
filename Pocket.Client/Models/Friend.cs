using CommunityToolkit.Mvvm.ComponentModel;
using SQLite;
using System;

namespace Pocket.Client.Models
{
    public class Friend : ObservableObject
    {
        private string _username = string.Empty;
        private string _displayName = string.Empty;
        private string _profilePictureUrl = string.Empty;
        private string _lastMessagePreview = "No messages yet";
        private string _lastMessageTimeText = string.Empty;
        private bool _isSelectedForSend;

        [Ignore]
        public bool IsSelectedForSend
        {
            get => _isSelectedForSend;
            set => SetProperty(ref _isSelectedForSend, value);
        }

        [PrimaryKey]
        public string Id { get; set; } = string.Empty;

        [MaxLength(50), Indexed]
        public string Username
        {
            get => _username;
            set
            {
                var cleaned = value?.Trim().TrimStart('@') ?? string.Empty;
                if (SetProperty(ref _username, cleaned))
                {
                    OnPropertyChanged(nameof(DisplayName));
                    OnPropertyChanged(nameof(FirstName));
                }
            }
        }

        [MaxLength(100)]
        public string DisplayName
        {
            get => string.IsNullOrWhiteSpace(_displayName) ? _username : _displayName;
            set
            {
                if (SetProperty(ref _displayName, value))
                {
                    OnPropertyChanged(nameof(FirstName));
                }
            }
        }

        [Ignore]
        public string FirstName
        {
            get
            {
                var name = DisplayName?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                    return string.Empty;
                var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length > 0 ? parts[0] : name;
            }
        }

        public string ProfilePictureUrl
        {
            get => _profilePictureUrl;
            set => SetProperty(ref _profilePictureUrl, value);
        }

        public string PublicKey { get; set; } = string.Empty;

        public bool IsMuted { get; set; }

        public DateTime AddedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastInteractionAt { get; set; } = DateTime.UtcNow;

        [Ignore]
        public string LastMessagePreview
        {
            get => _lastMessagePreview;
            set => SetProperty(ref _lastMessagePreview, value);
        }

        [Ignore]
        public string LastMessageTimeText
        {
            get => _lastMessageTimeText;
            set => SetProperty(ref _lastMessageTimeText, value);
        }

        [Ignore]
        public System.Windows.Input.ICommand OpenChatCommand => new Microsoft.Maui.Controls.Command(async () =>
        {
            if (Microsoft.Maui.Controls.Shell.Current != null && !string.IsNullOrEmpty(Id))
            {
                await Microsoft.Maui.Controls.Shell.Current.GoToAsync($"{nameof(Pocket.Client.Pages.DmPage)}?FriendId={Uri.EscapeDataString(Id)}");
            }
        });
    }
}

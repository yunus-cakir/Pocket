using CommunityToolkit.Mvvm.ComponentModel;
using SQLite;
using System;

namespace Pocket.Client.Models
{
    public class User : ObservableObject
    {
        private string _username = string.Empty;
        private string _displayName = string.Empty;
        private string _profilePictureUrl = string.Empty;

        [PrimaryKey]
        public string Id { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        [MaxLength(100)]
        public string DisplayName
        {
            get => _displayName;
            set => SetProperty(ref _displayName, value);
        }

        public string ProfilePictureUrl
        {
            get => _profilePictureUrl;
            set => SetProperty(ref _profilePictureUrl, value);
        }

        public string PublicKey { get; set; } = string.Empty;

        public string EncryptedPrivateKey { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

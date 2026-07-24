using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using Pocket.Client.Data;
using Pocket.Client.Models;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Pocket.Client.PageModels
{
    public partial class AppShellModel : ObservableObject
    {
        private readonly LocalDatabase? _database;

        [ObservableProperty]
        private User _currentUser;

        public ObservableCollection<Friend> Friends { get; } = new();

        public AppShellModel(LocalDatabase database)
        {
            _database = database;

            // Placeholder Current User
            CurrentUser = new User
            {
                Id = Guid.NewGuid().ToString(),
                Username = "antigravity",
                DisplayName = "Antigravity"
            };

            _ = LoadUserAsync();

            // Placeholder Friends
            Friends.Add(new Friend { Id = Guid.NewGuid().ToString(), Username = "alice", DisplayName = "Alice" });
            Friends.Add(new Friend { Id = Guid.NewGuid().ToString(), Username = "bob", DisplayName = "Bob" });
            Friends.Add(new Friend { Id = Guid.NewGuid().ToString(), Username = "charlie", DisplayName = "Charlie" });
        }

        public AppShellModel() : this(null!)
        {
        }

        private async Task LoadUserAsync()
        {
            if (_database != null)
            {
                var user = await _database.GetUserAsync();
                if (user != null)
                {
                    CurrentUser = user;
                }
            }
        }

        private IAsyncRelayCommand? _editProfileCommand;
        public IAsyncRelayCommand EditProfileCommand => _editProfileCommand ??= new AsyncRelayCommand(EditProfileAsync);

        public async Task EditProfileAsync()
        {
            if (CurrentUser == null || Microsoft.Maui.Controls.Shell.Current == null) return;

            string initialValue = string.IsNullOrWhiteSpace(CurrentUser.Username)
                ? "@username"
                : $"@{CurrentUser.Username}";

            string result = await Microsoft.Maui.Controls.Shell.Current.DisplayPromptAsync(
                title: "Kullanıcı Adını Değiştir",
                message: "Lütfen yeni kullanıcı adınızı girin (ör: @username):",
                accept: "Kaydet",
                cancel: "İptal",
                placeholder: "@username",
                maxLength: 30,
                keyboard: Keyboard.Text,
                initialValue: initialValue);

            if (result != null)
            {
                string cleanUsername = result.Trim().TrimStart('@');
                if (!string.IsNullOrWhiteSpace(cleanUsername))
                {
                    CurrentUser.Username = cleanUsername;
                    CurrentUser.DisplayName = cleanUsername;

                    OnPropertyChanged(nameof(CurrentUser));

                    if (_database != null)
                    {
                        await _database.SaveUserAsync(CurrentUser);
                    }
                }
            }
        }

        [RelayCommand]
        private void AddFriend()
        {
            Debug.WriteLine("Add friend tapped!");
            // TODO: Open Add Friend modal or navigate to search
        }

        [RelayCommand]
        private void CloseDrawer()
        {
            Microsoft.Maui.Controls.Shell.Current.FlyoutIsPresented = false;
        }
    }
}

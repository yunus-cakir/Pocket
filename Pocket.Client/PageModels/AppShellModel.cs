using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pocket.Client.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System;

namespace Pocket.Client.PageModels
{
    public partial class AppShellModel : ObservableObject
    {
        [ObservableProperty]
        private User _currentUser;

        public ObservableCollection<Friend> Friends { get; } = new();

        public AppShellModel()
        {
            // Placeholder Current User
            CurrentUser = new User
            {
                Id = Guid.NewGuid().ToString(),
                Username = "antigravity",
                DisplayName = "Antigravity"
            };

            // Placeholder Friends
            Friends.Add(new Friend { Id = Guid.NewGuid().ToString(), Username = "alice", DisplayName = "Alice" });
            Friends.Add(new Friend { Id = Guid.NewGuid().ToString(), Username = "bob", DisplayName = "Bob" });
            Friends.Add(new Friend { Id = Guid.NewGuid().ToString(), Username = "charlie", DisplayName = "Charlie" });
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

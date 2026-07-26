using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using Pocket.Client.Data;
using Pocket.Client.Models;
using Pocket.Client.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Pocket.Client.PageModels
{
    public partial class AppShellModel : ObservableObject
    {
        private readonly LocalDatabase? _database;
        private readonly ICryptoService? _cryptoService;
        private readonly IRelayService? _relayService;

        [ObservableProperty]
        private User _currentUser = new User(); // Default empty user until loaded

        public ObservableCollection<Friend> Friends { get; } = new();

        public AppShellModel(LocalDatabase database, ICryptoService cryptoService, IRelayService relayService)
        {
            _database = database;
            _cryptoService = cryptoService;
            _relayService = relayService;

            _ = LoadUserAsync();
        }

        public AppShellModel() : this(null!, null!, null!)
        {
        }

        private async Task LoadUserAsync()
        {
            if (_database != null && _cryptoService != null && _relayService != null)
            {
                var user = await _database.GetUserAsync();
                
                if (user == null)
                {
                    // First launch: Generate identity and keys
                    var keys = _cryptoService.GenerateKeyPair();
                    user = new User
                    {
                        Id = Guid.NewGuid().ToString(),
                        Username = "User_" + new Random().Next(1000, 9999),
                        PublicKey = keys.PublicKey,
                        EncryptedPrivateKey = keys.PrivateKey // Storing as-is for now
                    };
                    user.DisplayName = user.Username;
                    
                    await _database.SaveUserAsync(user);
                }
                
                CurrentUser = user;

                // Load real friends from database
                var friends = await _database.GetFriendsAsync();
                Friends.Clear();
                foreach (var friend in friends)
                {
                    Friends.Add(friend);
                }

                // Connect to Relay Server securely
                await _relayService.ConnectAsync(CurrentUser.Id, CurrentUser.Username);

                // Subscribe to real-time events
                _relayService.OnFriendRequestReceived -= HandleFriendRequestReceived;
                _relayService.OnFriendRequestReceived += HandleFriendRequestReceived;

                _relayService.OnFriendAcceptReceived -= HandleFriendAcceptReceived;
                _relayService.OnFriendAcceptReceived += HandleFriendAcceptReceived;
            }
        }

        private void HandleFriendRequestReceived(Pocket.Shared.DTOs.FriendRequestDto request)
        {
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (Microsoft.Maui.Controls.Shell.Current == null) return;
                
                bool accept = await Microsoft.Maui.Controls.Shell.Current.DisplayAlert(
                    "Arkadaşlık İsteği", 
                    $"{request.SenderUsername} size bir arkadaşlık isteği gönderdi. Kabul ediyor musunuz?", 
                    "Kabul Et", 
                    "Reddet");
                    
                if (accept && _database != null && _relayService != null)
                {
                    var friend = new Friend
                    {
                        Id = request.SenderId,
                        Username = request.SenderUsername,
                        DisplayName = request.SenderUsername,
                        PublicKey = request.SenderPublicKey,
                        AddedAt = DateTime.UtcNow
                    };
                    
                    var existing = await _database.GetFriendAsync(friend.Id);
                    if (existing == null)
                    {
                        await _database.SaveFriendAsync(friend);
                        Friends.Add(friend);
                    }

                    var acceptDto = new Pocket.Shared.DTOs.FriendAcceptDto
                    {
                        SenderId = CurrentUser.Id,
                        SenderUsername = CurrentUser.Username,
                        SenderPublicKey = CurrentUser.PublicKey,
                        RecipientId = request.SenderId,
                        Timestamp = DateTime.UtcNow
                    };
                    await _relayService.SendFriendAcceptAsync(acceptDto);
                }
            });
        }

        private void HandleFriendAcceptReceived(Pocket.Shared.DTOs.FriendAcceptDto acceptDto)
        {
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
            {
                if (_database != null)
                {
                    var friend = new Friend
                    {
                        Id = acceptDto.SenderId,
                        Username = acceptDto.SenderUsername,
                        DisplayName = acceptDto.SenderUsername,
                        PublicKey = acceptDto.SenderPublicKey,
                        AddedAt = DateTime.UtcNow
                    };
                    
                    var existing = await _database.GetFriendAsync(friend.Id);
                    if (existing == null)
                    {
                        await _database.SaveFriendAsync(friend);
                        Friends.Add(friend);
                        
                        if (Microsoft.Maui.Controls.Shell.Current != null)
                        {
                            await Microsoft.Maui.Controls.Shell.Current.DisplayAlert("İstek Kabul Edildi", $"{acceptDto.SenderUsername} arkadaşlık isteğinizi kabul etti!", "Tamam");
                        }
                    }
                }
            });
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

                    if (_database != null && _relayService != null)
                    {
                        await _database.SaveUserAsync(CurrentUser);
                        
                        // Reconnect to generate a new JWT with the updated username
                        await _relayService.ConnectAsync(CurrentUser.Id, CurrentUser.Username);
                    }
                }
            }
        }

        [RelayCommand]
        private async Task AddFriendAsync()
        {
            Debug.WriteLine("Add friend tapped!");
            if (Microsoft.Maui.Controls.Shell.Current != null)
            {
                await Microsoft.Maui.Controls.Shell.Current.GoToAsync(nameof(Pocket.Client.Pages.DmListPage));
            }
        }

        [RelayCommand]
        private void CloseDrawer()
        {
            Microsoft.Maui.Controls.Shell.Current.FlyoutIsPresented = false;
        }
    }
}

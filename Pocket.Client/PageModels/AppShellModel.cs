using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using Pocket.Client.Data;
using Pocket.Client.Models;
using Pocket.Client.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
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

        [ObservableProperty]
        private string _serverIp = string.Empty;

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private string _connectionStatusText = "Bağlı Değil";

        [ObservableProperty]
        private string _connectionStatusColor = "#EF4444";

        [ObservableProperty]
        private string _connectedUrlText = "-";

        [ObservableProperty]
        private string _connectedAtText = "-";

        public ObservableCollection<Friend> Friends { get; } = new();

        public event Action<MediaPost>? OnMediaPostReceived;
        public event Action<Guid>? OnMediaPostDelivered;
        public event Action<DirectMessage>? OnDirectMessageReceived;
        public event Action<Guid>? OnDirectMessageDelivered;

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

        private string? _cachedPrivateKey;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> _sharedSecretCache = new();

        private byte[] GetOrDeriveSharedSecret(string privateKey, string friendPublicKey)
        {
            return _sharedSecretCache.GetOrAdd(friendPublicKey, pk => _cryptoService!.DeriveSharedSecret(privateKey, pk));
        }

        public async Task<string> GetPrivateKeyAsync()
        {
            if (!string.IsNullOrEmpty(_cachedPrivateKey))
                return _cachedPrivateKey;

            if (CurrentUser == null || string.IsNullOrEmpty(CurrentUser.Id))
                return string.Empty;

            try
            {
                var secureKey = await Microsoft.Maui.Storage.SecureStorage.Default.GetAsync($"pocket_pk_{CurrentUser.Id}");
                if (!string.IsNullOrEmpty(secureKey))
                {
                    _cachedPrivateKey = secureKey;
                    return secureKey;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SecureStorage] Fallback to SQLite private key: {ex.Message}");
            }

            _cachedPrivateKey = CurrentUser.EncryptedPrivateKey ?? string.Empty;
            return _cachedPrivateKey;
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
                        EncryptedPrivateKey = keys.PrivateKey
                    };
                    user.DisplayName = user.Username;
                    
                    try
                    {
                        await Microsoft.Maui.Storage.SecureStorage.Default.SetAsync($"pocket_pk_{user.Id}", keys.PrivateKey);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SecureStorage] Could not store key in hardware storage: {ex.Message}");
                    }

                    await _database.SaveUserAsync(user);
                }
                
                CurrentUser = user;

                // Load Server IP from Preferences
                ServerIp = Microsoft.Maui.Storage.Preferences.Get("ServerIp", RelayService.GetDefaultServerIp());

                // Load real friends from database
                var friends = await _database.GetFriendsAsync();
                Friends.Clear();
                foreach (var friend in friends)
                {
                    Friends.Add(friend);
                }

                // Subscribe to real-time events BEFORE connecting so offline queued payloads aren't missed
                _relayService.OnConnectionStateChanged -= HandleConnectionStateChanged;
                _relayService.OnConnectionStateChanged += HandleConnectionStateChanged;

                _relayService.OnFriendRequestReceived -= HandleFriendRequestReceived;
                _relayService.OnFriendRequestReceived += HandleFriendRequestReceived;

                _relayService.OnFriendAcceptReceived -= HandleFriendAcceptReceived;
                _relayService.OnFriendAcceptReceived += HandleFriendAcceptReceived;

                _relayService.OnPayloadReceived -= HandlePayloadReceived;
                _relayService.OnPayloadReceived += HandlePayloadReceived;

                _relayService.OnDeliveryAcknowledged -= HandleDeliveryAcknowledged;
                _relayService.OnDeliveryAcknowledged += HandleDeliveryAcknowledged;

                // Connect to Relay Server securely
                await _relayService.ConnectAsync(CurrentUser.Id, CurrentUser.Username);
                if (_relayService.IsConnected)
                {
                    await _relayService.RegisterUserAsync(new Pocket.Shared.DTOs.UserIdentityDto
                    {
                        UserId = CurrentUser.Id,
                        Username = CurrentUser.Username,
                        PublicKey = CurrentUser.PublicKey
                    });
                }

                // Initial connection state
                HandleConnectionStateChanged(_relayService.IsConnected);
            }
        }

        public async Task<DirectMessage?> SendDirectMessageAsync(
            string friendId,
            string? textContent,
            string? reactionEmoji = null,
            Guid? mediaPostId = null)
        {
            if (_database == null || _cryptoService == null || _relayService == null)
                return null;

            var friend = await _database.GetFriendAsync(friendId);
            if (friend == null || string.IsNullOrEmpty(friend.PublicKey))
                return null;

            var dm = new DirectMessage
            {
                Id = Guid.NewGuid(),
                FriendId = friendId,
                MediaPostId = mediaPostId,
                IsIncoming = false,
                TextContent = textContent,
                ReactionEmoji = reactionEmoji,
                SentAt = DateTime.UtcNow,
                SyncStatus = Pocket.Shared.Enums.SyncStatus.Pending
            };

            if (mediaPostId.HasValue)
            {
                var refPost = await _database.GetMediaPostAsync(mediaPostId.Value);
                if (refPost != null)
                {
                    dm.ReferencedPostPath = refPost.MediaLocalPath;
                    dm.ReferencedPostCaption = refPost.Caption ?? string.Empty;
                }
            }

            // Local-First obligation: save to SQLite before network transmission
            await _database.SaveDirectMessageAsync(dm);

            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                OnDirectMessageReceived?.Invoke(dm);
            });

            var privateKey = await GetPrivateKeyAsync();
            if (!string.IsNullOrEmpty(privateKey) && _relayService.IsConnected)
            {
                try
                {
                    var sharedSecret = GetOrDeriveSharedSecret(privateKey, friend.PublicKey);
                    var textBytes = Encoding.UTF8.GetBytes(textContent ?? string.Empty);
                    var encryptedContent = _cryptoService.EncryptData(textBytes, sharedSecret);

                    byte[]? encryptedReaction = null;
                    if (!string.IsNullOrEmpty(reactionEmoji))
                    {
                        var reactionBytes = Encoding.UTF8.GetBytes(reactionEmoji);
                        encryptedReaction = _cryptoService.EncryptData(reactionBytes, sharedSecret);
                    }

                    var payload = new Pocket.Shared.DTOs.EncryptedPayloadDto
                    {
                        MessageId = dm.Id,
                        SenderId = CurrentUser.Id,
                        RecipientId = friend.Id,
                        MediaPostId = mediaPostId,
                        EncryptedContent = encryptedContent,
                        EncryptedReaction = encryptedReaction,
                        EncryptedMedia = null,
                        MediaType = Pocket.Shared.Enums.MediaType.None,
                        Timestamp = dm.SentAt
                    };

                    await _relayService.SendPayloadAsync(payload);

                    // Guard against race condition if DeliveryAcknowledged already arrived while SendPayloadAsync was completing
                    var currentInDb = await _database.GetDirectMessageAsync(dm.Id);
                    if (currentInDb != null && currentInDb.SyncStatus == Pocket.Shared.Enums.SyncStatus.Delivered)
                    {
                        dm.SyncStatus = Pocket.Shared.Enums.SyncStatus.Delivered;
                    }
                    else if (dm.SyncStatus != Pocket.Shared.Enums.SyncStatus.Delivered)
                    {
                        dm.SyncStatus = Pocket.Shared.Enums.SyncStatus.Sent;
                        await _database.SaveDirectMessageAsync(dm);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[E2EE DM Send Error] {ex.Message}");
                    dm.SyncStatus = Pocket.Shared.Enums.SyncStatus.Failed;
                    await _database.SaveDirectMessageAsync(dm);
                }
            }

            return dm;
        }

        private async void HandlePayloadReceived(Pocket.Shared.DTOs.EncryptedPayloadDto payload)
        {
            if (_database == null || _cryptoService == null || _relayService == null)
                return;

            try
            {
                // Zero Trust & BR-601: Verify sender is an established mutual friend with a known PublicKey
                var senderFriend = await _database.GetFriendAsync(payload.SenderId);
                if (senderFriend == null || string.IsNullOrEmpty(senderFriend.PublicKey))
                {
                    Debug.WriteLine($"[E2EE] Rejected payload {payload.MessageId} from unknown sender {payload.SenderId}");
                    return;
                }

                var privateKey = await GetPrivateKeyAsync();
                if (string.IsNullOrEmpty(privateKey))
                    return;

                var sharedSecret = GetOrDeriveSharedSecret(privateKey, senderFriend.PublicKey);

                if (payload.MediaType == Pocket.Shared.Enums.MediaType.None)
                {
                    // Direct Message / Photo Reaction (BR-301, BR-302)
                    var existingDm = await _database.GetDirectMessageAsync(payload.MessageId);
                    if (existingDm != null)
                    {
                        await _relayService.ConfirmDeliveryAsync(payload.SenderId, payload.MessageId);
                        return;
                    }

                    string textContent = string.Empty;
                    if (payload.EncryptedContent is { Length: > 0 })
                    {
                        var decryptedTextBytes = _cryptoService.DecryptData(payload.EncryptedContent, sharedSecret);
                        textContent = Encoding.UTF8.GetString(decryptedTextBytes);
                    }

                    string? reactionEmoji = null;
                    if (payload.EncryptedReaction is { Length: > 0 })
                    {
                        var decryptedReactionBytes = _cryptoService.DecryptData(payload.EncryptedReaction, sharedSecret);
                        reactionEmoji = Encoding.UTF8.GetString(decryptedReactionBytes);
                    }

                    var dm = new DirectMessage
                    {
                        Id = payload.MessageId,
                        FriendId = payload.SenderId,
                        MediaPostId = payload.MediaPostId,
                        IsIncoming = true,
                        TextContent = textContent,
                        ReactionEmoji = reactionEmoji,
                        SentAt = payload.Timestamp,
                        SyncStatus = Pocket.Shared.Enums.SyncStatus.Delivered
                    };

                    if (dm.MediaPostId.HasValue)
                    {
                        var refPost = await _database.GetMediaPostAsync(dm.MediaPostId.Value);
                        if (refPost != null)
                        {
                            dm.ReferencedPostPath = refPost.MediaLocalPath;
                            dm.ReferencedPostCaption = refPost.Caption ?? string.Empty;
                        }
                    }

                    await _database.SaveDirectMessageAsync(dm);

                    Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                    {
                        OnDirectMessageReceived?.Invoke(dm);
                    });

                    // BR-202: Confirm delivery so server immediately purges payload from RAM
                    await _relayService.ConfirmDeliveryAsync(payload.SenderId, payload.MessageId);
                    return;
                }

                // Idempotency check for MediaPost: if already received, confirm delivery and skip duplicate save
                var existingPost = await _database.GetMediaPostAsync(payload.MessageId);
                if (existingPost != null)
                {
                    await _relayService.ConfirmDeliveryAsync(payload.SenderId, payload.MessageId);
                    return;
                }

                string caption = string.Empty;
                if (payload.EncryptedContent is { Length: > 0 })
                {
                    var decryptedCaptionBytes = _cryptoService.DecryptData(payload.EncryptedContent, sharedSecret);
                    caption = Encoding.UTF8.GetString(decryptedCaptionBytes);
                }

                string localMediaPath = string.Empty;
                if (payload.EncryptedMedia is { Length: > 0 })
                {
                    var decryptedMediaBytes = _cryptoService.DecryptData(payload.EncryptedMedia, sharedSecret);
                    var mediaDir = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "Media");
                    Directory.CreateDirectory(mediaDir);

                    var extension = payload.MediaType == Pocket.Shared.Enums.MediaType.Video ? ".mp4" : ".jpg";
                    localMediaPath = Path.Combine(mediaDir, $"recv_{payload.MessageId}{extension}");
                    await File.WriteAllBytesAsync(localMediaPath, decryptedMediaBytes);
                }

                var mediaPost = new MediaPost
                {
                    Id = payload.MessageId,
                    FriendId = payload.SenderId,
                    IsIncoming = true,
                    MediaLocalPath = localMediaPath,
                    MediaType = payload.MediaType,
                    Caption = caption,
                    SentAt = payload.Timestamp,
                    IsIncludedInRollcall = false,
                    SyncStatus = Pocket.Shared.Enums.SyncStatus.Delivered,
                    SenderDisplayName = senderFriend.Username
                };

                await _database.SaveMediaPostAsync(mediaPost);

                // BR-202: Immediately confirm delivery so the Relay Server purges the transient payload from RAM
                await _relayService.ConfirmDeliveryAsync(payload.SenderId, payload.MessageId);

                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                {
                    OnMediaPostReceived?.Invoke(mediaPost);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[E2EE Receive Error] Failed to decrypt/save payload {payload.MessageId}: {ex.Message}");
            }
        }

        private async void HandleDeliveryAcknowledged(Guid messageId)
        {
            if (_database == null)
                return;

            try
            {
                var post = await _database.GetMediaPostAsync(messageId);
                if (post != null && post.SyncStatus != Pocket.Shared.Enums.SyncStatus.Delivered)
                {
                    post.SyncStatus = Pocket.Shared.Enums.SyncStatus.Delivered;
                    await _database.SaveMediaPostAsync(post);

                    Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                    {
                        OnMediaPostDelivered?.Invoke(messageId);
                    });
                }

                var dm = await _database.GetDirectMessageAsync(messageId);
                if (dm != null && dm.SyncStatus != Pocket.Shared.Enums.SyncStatus.Delivered)
                {
                    dm.SyncStatus = Pocket.Shared.Enums.SyncStatus.Delivered;
                    await _database.SaveDirectMessageAsync(dm);

                    Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                    {
                        OnDirectMessageDelivered?.Invoke(messageId);
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[E2EE Ack Error] Failed to update delivery status for {messageId}: {ex.Message}");
            }
        }

        private void HandleConnectionStateChanged(bool isConnected)
        {
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                IsConnected = isConnected;
                if (isConnected)
                {
                    ConnectionStatusText = "Sunucuya Bağlandı";
                    ConnectionStatusColor = "#10B981"; // Emerald Green
                    ConnectedUrlText = _relayService?.CurrentHubUrl ?? "-";
                    ConnectedAtText = _relayService?.ConnectedAt?.ToString("HH:mm:ss") ?? "-";
                }
                else
                {
                    ConnectionStatusText = "Bağlantı Kesildi";
                    ConnectionStatusColor = "#EF4444"; // Red
                    ConnectedUrlText = _relayService?.CurrentHubUrl ?? "-";
                    ConnectedAtText = "Yeniden Bağlanılıyor...";
                }
            });
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

        [RelayCommand]
        private async Task SaveServerIpAsync()
        {
            if (string.IsNullOrWhiteSpace(ServerIp))
            {
                ServerIp = RelayService.GetDefaultServerIp();
            }

            string cleanIp = ServerIp.Trim();
            Microsoft.Maui.Storage.Preferences.Set("ServerIp", cleanIp);
            ServerIp = cleanIp;

            if (_relayService != null && CurrentUser != null && !string.IsNullOrEmpty(CurrentUser.Id))
            {
                await _relayService.ConnectAsync(CurrentUser.Id, CurrentUser.Username);
                if (_relayService.IsConnected)
                {
                    await _relayService.RegisterUserAsync(new Pocket.Shared.DTOs.UserIdentityDto
                    {
                        UserId = CurrentUser.Id,
                        Username = CurrentUser.Username,
                        PublicKey = CurrentUser.PublicKey
                    });
                }

                if (Microsoft.Maui.Controls.Shell.Current != null)
                {
                    if (_relayService.IsConnected)
                    {
                        await Microsoft.Maui.Controls.Shell.Current.DisplayAlert("Bağlantı Başarılı", $"Sunucuya '{cleanIp}' adresi üzerinden erişildi!", "Tamam");
                    }
                    else
                    {
                        await Microsoft.Maui.Controls.Shell.Current.DisplayAlert("Bağlantı Başarısız", $"'{cleanIp}:5200' adresindeki sunucuya ulaşılamadı. Sunucunun açık olduğundan emin olun.", "Tamam");
                    }
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

                    if (_database != null && _relayService != null)
                    {
                        await _database.SaveUserAsync(CurrentUser);
                        
                        // Reconnect to generate a new JWT with the updated username
                        await _relayService.ConnectAsync(CurrentUser.Id, CurrentUser.Username);
                        if (_relayService.IsConnected)
                        {
                            await _relayService.RegisterUserAsync(new Pocket.Shared.DTOs.UserIdentityDto
                            {
                                UserId = CurrentUser.Id,
                                Username = CurrentUser.Username,
                                PublicKey = CurrentUser.PublicKey
                            });
                        }
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

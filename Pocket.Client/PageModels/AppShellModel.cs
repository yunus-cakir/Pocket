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
        private readonly IBackupService? _backupService;
        private readonly INotificationService? _notificationService;

        [ObservableProperty] private User _currentUser = new User(); // Default empty user until loaded

        [ObservableProperty] private string _serverIp = string.Empty;

        [ObservableProperty] private bool _isConnected;

        [ObservableProperty] private string _connectionStatusText = "Bağlı Değil";

        [ObservableProperty] private string _connectionStatusColor = "#EF4444";

        [ObservableProperty] private string _connectedUrlText = "-";

        [ObservableProperty] private string _connectedAtText = "-";

        [ObservableProperty] private bool _isStreakOnWidget = true;

        public const string ReactionsNotificationPreferenceKey = "ReactionsNotificationEnabled";
        public const string MessagesNotificationPreferenceKey = "MessagesNotificationEnabled";

        [ObservableProperty] private bool _isReactionsNotificationEnabled = Microsoft.Maui.Storage.Preferences.Get(
            ReactionsNotificationPreferenceKey, true);

        [ObservableProperty] private bool _isMessagesNotificationEnabled = Microsoft.Maui.Storage.Preferences.Get(
            MessagesNotificationPreferenceKey, true);

        /// <summary>
        /// When true, the shutter saves the exact preview frame (WYSIWYG colors).
        /// When false, the camera's native still-capture pipeline is used (may alter tone/colors).
        /// Persisted via <see cref="Pocket.Client.Controls.CameraView.CaptureFromPreviewPreferenceKey"/>.
        /// </summary>
        [ObservableProperty] private bool _isCaptureFromPreview = Microsoft.Maui.Storage.Preferences.Get(
            Pocket.Client.Controls.CameraView.CaptureFromPreviewPreferenceKey,
            Pocket.Client.Controls.CameraView.CaptureFromPreviewDefault);

        /// <summary>Show a vertical zoom slider on the right of the viewfinder after tap-to-focus.</summary>
        [ObservableProperty] private bool _isZoomSliderEnabled = Microsoft.Maui.Storage.Preferences.Get(
            Pocket.Client.Controls.CameraView.ZoomSliderPreferenceKey,
            Pocket.Client.Controls.CameraView.ZoomSliderDefault);

        /// <summary>Show a horizontal exposure slider at the bottom of the viewfinder after tap-to-focus.</summary>
        [ObservableProperty] private bool _isExposureSliderEnabled = Microsoft.Maui.Storage.Preferences.Get(
            Pocket.Client.Controls.CameraView.ExposureSliderPreferenceKey,
            Pocket.Client.Controls.CameraView.ExposureSliderDefault);

        /// <summary>Keep the last zoom level instead of resetting it to 1.0x.</summary>
        [ObservableProperty] private bool _isKeepLastZoom = Microsoft.Maui.Storage.Preferences.Get(
            Pocket.Client.Controls.CameraView.KeepZoomPreferenceKey,
            Pocket.Client.Controls.CameraView.KeepZoomDefault);

        /// <summary>Keep the last exposure compensation instead of resetting it to 0 EV.</summary>
        [ObservableProperty] private bool _isKeepLastExposure = Microsoft.Maui.Storage.Preferences.Get(
            Pocket.Client.Controls.CameraView.KeepExposurePreferenceKey,
            Pocket.Client.Controls.CameraView.KeepExposureDefault);

        public const string AccentColorPreferenceKey = "AccentColorHex";
        public const string AccentYellowHex = "#FFB800";
        public const string AccentLightBlueHex = "#00B8D4";
        public const string AccentDarkBlueHex = "#0066D4";
        public const string AccentGreenHex = "#00D458";
        public const string AccentRedHex = "#D43800";

        public static string GetSavedAccentColorHex()
        {
            var raw = Microsoft.Maui.Storage.Preferences.Default.Get(AccentColorPreferenceKey, AccentYellowHex);
            return NormalizeAccentColorHex(raw);
        }

        public static string NormalizeAccentColorHex(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return AccentYellowHex;

            string clean = hex.Trim().ToUpperInvariant();
            if (!clean.StartsWith("#", StringComparison.Ordinal))
                clean = "#" + clean;

            return clean switch
            {
                AccentYellowHex => AccentYellowHex,
                AccentLightBlueHex => AccentLightBlueHex,
                AccentDarkBlueHex => AccentDarkBlueHex,
                AccentGreenHex => AccentGreenHex,
                AccentRedHex => AccentRedHex,
                _ => AccentYellowHex
            };
        }

        public static Microsoft.Maui.Graphics.Color GetAccentColor() =>
            Microsoft.Maui.Graphics.Color.FromArgb(GetSavedAccentColorHex());

        public static void ApplyAccentColorResources(string hex)
        {
            string normalized = NormalizeAccentColorHex(hex);
            var color = Microsoft.Maui.Graphics.Color.FromArgb(normalized);
            var resources = Application.Current?.Resources;
            if (resources != null)
            {
                resources["AccentColor"] = color;
                resources["AccentBrush"] = new SolidColorBrush(color);
                resources["Primary"] = color;
                resources["PrimaryDark"] = color;
                resources["LocketGold"] = color;
                resources["PrimaryBrush"] = new SolidColorBrush(color);
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(AccentColorValue))]
        [NotifyPropertyChangedFor(nameof(IsAccentYellowSelected))]
        [NotifyPropertyChangedFor(nameof(IsAccentLightBlueSelected))]
        [NotifyPropertyChangedFor(nameof(IsAccentDarkBlueSelected))]
        [NotifyPropertyChangedFor(nameof(IsAccentGreenSelected))]
        [NotifyPropertyChangedFor(nameof(IsAccentRedSelected))]
        private string _accentColorHex = GetSavedAccentColorHex();

        public Microsoft.Maui.Graphics.Color AccentColorValue =>
            Microsoft.Maui.Graphics.Color.FromArgb(NormalizeAccentColorHex(AccentColorHex));

        public bool IsAccentYellowSelected =>
            string.Equals(AccentColorHex, AccentYellowHex, StringComparison.OrdinalIgnoreCase);

        public bool IsAccentLightBlueSelected =>
            string.Equals(AccentColorHex, AccentLightBlueHex, StringComparison.OrdinalIgnoreCase);

        public bool IsAccentDarkBlueSelected =>
            string.Equals(AccentColorHex, AccentDarkBlueHex, StringComparison.OrdinalIgnoreCase);

        public bool IsAccentGreenSelected =>
            string.Equals(AccentColorHex, AccentGreenHex, StringComparison.OrdinalIgnoreCase);

        public bool IsAccentRedSelected =>
            string.Equals(AccentColorHex, AccentRedHex, StringComparison.OrdinalIgnoreCase);

        public event Action<string>? OnAccentColorChanged;

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasUnseenPhotos))]
        private int _unseenPhotoCount;

        public bool HasUnseenPhotos => UnseenPhotoCount > 0;

        public bool IsViewingHistory { get; set; }

        public bool IsAppInForeground { get; set; } = true;

        public bool IsInDmWindow { get; set; }

        public ObservableCollection<Friend> Friends { get; } = new();

        public string FriendsCountText => Friends.Count == 1 ? "1 Friend" : $"{Friends.Count} Friends";

        public event Action<MediaPost>? OnMediaPostReceived;
        public event Action<Guid>? OnMediaPostSent;
        public event Action<Guid>? OnMediaPostDelivered;
        public event Action<Guid>? OnMediaPostDeleted;
        public event Action<bool>? OnConnectionStateChangedEvent;
        public event Action<DirectMessage>? OnDirectMessageReceived;
        public event Action<Guid>? OnDirectMessageDelivered;
        public event Action<DirectMessage>? OnDirectMessageSendFailed;
        public event Action<Guid>? OnDirectMessageDeleted;
        public event Action<Friend>? OnFriendProfileUpdated;
        public event Action<User>? OnUserProfileUpdated;
        public event Action? OnDataImported;

        public AppShellModel(
            LocalDatabase database,
            ICryptoService cryptoService,
            IRelayService relayService,
            IBackupService backupService,
            INotificationService notificationService)
        {
            _database = database;
            _cryptoService = cryptoService;
            _relayService = relayService;
            _backupService = backupService;
            _notificationService = notificationService;

            Friends.CollectionChanged += (_, _) => OnPropertyChanged(nameof(FriendsCountText));
            OnDirectMessageReceived += UpdateFriendLastMessage;

            ApplyAccentColorResources(AccentColorHex);

            _ = LoadUserAsync();
        }

        [RelayCommand]
        private void SelectAccentColor(string? hex)
        {
            AccentColorHex = NormalizeAccentColorHex(hex);
        }

        partial void OnAccentColorHexChanged(string value)
        {
            string normalized = NormalizeAccentColorHex(value);
            if (!string.Equals(value, normalized, StringComparison.Ordinal))
            {
                AccentColorHex = normalized;
                return;
            }

            Microsoft.Maui.Storage.Preferences.Default.Set(AccentColorPreferenceKey, normalized);
            ApplyAccentColorResources(normalized);
            OnAccentColorChanged?.Invoke(normalized);
            _ = UpdateLatestPhotoWidgetAsync();
        }

        [RelayCommand]
        private async Task AddWidgetAsync()
        {
            bool requested = false;
#if ANDROID
            requested = Pocket.Client.Platforms.Android.Widgets.LatestPhotoWidgetProvider.RequestPin();
#endif
            if (!requested && Microsoft.Maui.Controls.Shell.Current != null)
            {
                await Microsoft.Maui.Controls.Shell.Current.DisplayAlert(
                    "Widget Eklenemedi",
                    "Ana ekranınız otomatik widget eklemeyi desteklemiyor. Ana ekranda boş bir alana basılı tutup \"Widget'lar\" menüsünden Pocket'ı seçerek ekleyebilirsiniz.",
                    "Tamam");
            }
        }

        [RelayCommand]
        private void ToggleStreakOnWidget()
        {
            IsStreakOnWidget = !IsStreakOnWidget;
        }

        [RelayCommand]
        private void ToggleReactionsNotification()
        {
            IsReactionsNotificationEnabled = !IsReactionsNotificationEnabled;
        }

        partial void OnIsReactionsNotificationEnabledChanged(bool value)
        {
            Microsoft.Maui.Storage.Preferences.Set(ReactionsNotificationPreferenceKey, value);
        }

        [RelayCommand]
        private void ToggleMessagesNotification()
        {
            IsMessagesNotificationEnabled = !IsMessagesNotificationEnabled;
        }

        partial void OnIsMessagesNotificationEnabledChanged(bool value)
        {
            Microsoft.Maui.Storage.Preferences.Set(MessagesNotificationPreferenceKey, value);
        }

        [RelayCommand]
        private void ToggleCaptureFromPreview()
        {
            IsCaptureFromPreview = !IsCaptureFromPreview;
        }

        partial void OnIsCaptureFromPreviewChanged(bool value)
        {
            Microsoft.Maui.Storage.Preferences.Set(Pocket.Client.Controls.CameraView.CaptureFromPreviewPreferenceKey,
                value);
        }

        [RelayCommand]
        private void ToggleZoomSlider()
        {
            IsZoomSliderEnabled = !IsZoomSliderEnabled;
        }

        partial void OnIsZoomSliderEnabledChanged(bool value)
        {
            Microsoft.Maui.Storage.Preferences.Set(Pocket.Client.Controls.CameraView.ZoomSliderPreferenceKey, value);
        }

        [RelayCommand]
        private void ToggleExposureSlider()
        {
            IsExposureSliderEnabled = !IsExposureSliderEnabled;
        }

        partial void OnIsExposureSliderEnabledChanged(bool value)
        {
            Microsoft.Maui.Storage.Preferences.Set(Pocket.Client.Controls.CameraView.ExposureSliderPreferenceKey,
                value);
        }

        [RelayCommand]
        private void ToggleKeepLastZoom()
        {
            IsKeepLastZoom = !IsKeepLastZoom;
        }

        partial void OnIsKeepLastZoomChanged(bool value)
        {
            Microsoft.Maui.Storage.Preferences.Set(Pocket.Client.Controls.CameraView.KeepZoomPreferenceKey, value);
        }

        [RelayCommand]
        private void ToggleKeepLastExposure()
        {
            IsKeepLastExposure = !IsKeepLastExposure;
        }

        partial void OnIsKeepLastExposureChanged(bool value)
        {
            Microsoft.Maui.Storage.Preferences.Set(Pocket.Client.Controls.CameraView.KeepExposurePreferenceKey, value);
        }

        private void UpdateFriendLastMessage(DirectMessage dm)
        {
            foreach (var f in Friends)
            {
                if (f.Id == dm.FriendId)
                {
                    f.LastMessagePreview = !string.IsNullOrWhiteSpace(dm.TextContent)
                        ? dm.TextContent
                        : (!string.IsNullOrWhiteSpace(dm.ReactionEmoji) ? dm.ReactionEmoji : "Fotoğrafa yanıt verdi");
                    f.LastMessageTimeText = MediaPost.FormatRelativeTime(dm.SentAt);
                    break;
                }
            }
        }

        public AppShellModel() : this(null!, null!, null!, null!, null!)
        {
        }

        private string? _cachedPrivateKey;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> _sharedSecretCache = new();

        private byte[] GetOrDeriveSharedSecret(string privateKey, string friendPublicKey)
        {
            return _sharedSecretCache.GetOrAdd(friendPublicKey,
                pk => _cryptoService!.DeriveSharedSecret(privateKey, pk));
        }

        public async Task<string> GetPrivateKeyAsync()
        {
            if (!string.IsNullOrEmpty(_cachedPrivateKey))
                return _cachedPrivateKey;

            if (CurrentUser == null || string.IsNullOrEmpty(CurrentUser.Id))
                return string.Empty;

            try
            {
                var secureKey =
                    await Microsoft.Maui.Storage.SecureStorage.Default.GetAsync($"pocket_pk_{CurrentUser.Id}");
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
                        await Microsoft.Maui.Storage.SecureStorage.Default.SetAsync($"pocket_pk_{user.Id}",
                            keys.PrivateKey);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SecureStorage] Could not store key in hardware storage: {ex.Message}");
                    }

                    await _database.SaveUserAsync(user);
                }

                CurrentUser = user;

                // Load Server IP from Preferences
                ServerIp = RelayService.GetSavedServer();

                // Load real friends from database
                var friends = await _database.GetFriendsAsync();
                Friends.Clear();
                foreach (var friend in friends)
                {
                    var dms = await _database.GetDirectMessagesForFriendAsync(friend.Id);
                    var chatDms = dms.Where(d => !d.IsPostReactionOnly).ToList();
                    if (chatDms.Count > 0)
                    {
                        var lastDm = chatDms[chatDms.Count - 1];
                        friend.LastMessagePreview = !string.IsNullOrWhiteSpace(lastDm.TextContent)
                            ? lastDm.TextContent
                            : "Fotoğrafa yanıt verdi";
                        friend.LastMessageTimeText = MediaPost.FormatRelativeTime(lastDm.SentAt);
                    }
                    else
                    {
                        friend.LastMessagePreview = "Sohbeti başlatmak için dokun";
                        friend.LastMessageTimeText = MediaPost.FormatRelativeTime(friend.AddedAt);
                    }

                    Friends.Add(friend);
                }

                // Sync home-screen widget with the latest stored photo
                _ = UpdateLatestPhotoWidgetAsync();

                // Subscribe to real-time events BEFORE connecting so offline queued payloads aren't missed
                EnsureConnectivitySubscribed();
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

                // Connect to Relay Server securely: login signs the server's challenge with our identity key
                _relayService.SetIdentityKeyProvider(async () => (CurrentUser.PublicKey, await GetPrivateKeyAsync()));
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
                SyncStatus = Pocket.Shared.Enums.SyncStatus.Pending,
                SenderAvatarUrl = CurrentUser.ProfilePictureUrl ?? string.Empty
            };

            if (mediaPostId.HasValue)
            {
                var refPost = await _database.GetMediaPostAsync(mediaPostId.Value);
                if (refPost != null)
                {
                    dm.ReferencedPostPath = refPost.MediaLocalPath;
                    dm.ReferencedPostCaption = refPost.Caption ?? string.Empty;
                    dm.ReferencedPostAuthorLabel = refPost.IsIncoming ? friend.DisplayName : "You";
                    dm.ReferencedPostAuthorAvatarUrl = refPost.IsIncoming
                        ? (friend.ProfilePictureUrl ?? string.Empty)
                        : (CurrentUser.ProfilePictureUrl ?? string.Empty);
                    dm.ReferencedPostTimeText = MediaPost.FormatRelativeTime(refPost.SentAt);
                }
            }

            // Local-First obligation: save to SQLite before network transmission
            await _database.SaveDirectMessageAsync(dm);

            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!dm.IsPostReactionOnly)
                {
                    var targetFriend = Friends.FirstOrDefault(f => f.Id == friendId);
                    if (targetFriend != null)
                    {
                        targetFriend.LastMessagePreview = !string.IsNullOrWhiteSpace(dm.TextContent)
                            ? dm.TextContent
                            : "Fotoğrafa yanıt verdi";
                        targetFriend.LastMessageTimeText = MediaPost.FormatRelativeTime(dm.SentAt);
                    }
                }

                OnDirectMessageReceived?.Invoke(dm);
            });

            bool sent = await TryTransmitDirectMessageAsync(dm, friend);
            if (!sent)
            {
                dm.SyncStatus = Pocket.Shared.Enums.SyncStatus.Failed;
                await _database.SaveDirectMessageAsync(dm);
                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                {
                    OnDirectMessageSendFailed?.Invoke(dm);
                });
            }

            return dm;
        }

        public async Task<bool> RetryDirectMessageAsync(DirectMessage dm)
        {
            if (_database == null || _cryptoService == null || _relayService == null || dm == null)
                return false;

            var friend = await _database.GetFriendAsync(dm.FriendId);
            if (friend == null || string.IsNullOrEmpty(friend.PublicKey))
            {
                dm.SyncStatus = Pocket.Shared.Enums.SyncStatus.Failed;
                await _database.SaveDirectMessageAsync(dm);
                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                {
                    OnDirectMessageSendFailed?.Invoke(dm);
                });
                return false;
            }

            bool sent = await TryTransmitDirectMessageAsync(dm, friend);
            if (!sent)
            {
                dm.SyncStatus = Pocket.Shared.Enums.SyncStatus.Failed;
                await _database.SaveDirectMessageAsync(dm);
                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                {
                    OnDirectMessageSendFailed?.Invoke(dm);
                });
                return false;
            }

            return true;
        }

        public async Task DeleteDirectMessageAsync(DirectMessage dm, bool undoSendForRecipient = false)
        {
            if (_database == null || dm == null)
                return;

            bool shouldNotifyRecipient = undoSendForRecipient &&
                                         !dm.IsIncoming &&
                                         dm.SyncStatus != Pocket.Shared.Enums.SyncStatus.Failed;

            await _database.DeleteDirectMessageAsync(dm.Id);
            await RefreshFriendLastMessagePreviewAsync(dm.FriendId);

            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                OnDirectMessageDeleted?.Invoke(dm.Id);
            });

            if (shouldNotifyRecipient && _cryptoService != null && _relayService != null && _relayService.IsConnected)
            {
                var privateKey = await GetPrivateKeyAsync();
                var friend = await _database.GetFriendAsync(dm.FriendId);
                if (!string.IsNullOrEmpty(privateKey) && friend != null && !string.IsNullOrEmpty(friend.PublicKey))
                {
                    try
                    {
                        var sharedSecret = GetOrDeriveSharedSecret(privateKey, friend.PublicKey);
                        var dmIdBytes = Encoding.UTF8.GetBytes(dm.Id.ToString());
                        var encryptedContent = _cryptoService.EncryptData(dmIdBytes, sharedSecret);

                        var payload = new Pocket.Shared.DTOs.EncryptedPayloadDto
                        {
                            MessageId = Guid.NewGuid(),
                            SenderId = CurrentUser.Id,
                            RecipientId = friend.Id,
                            MediaPostId = dm.Id,
                            EncryptedContent = encryptedContent,
                            MediaType = Pocket.Shared.Enums.MediaType.MessageDeleted,
                            Timestamp = DateTime.UtcNow
                        };

                        await _relayService.SendPayloadAsync(payload);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[E2EE Undo Send Error] Failed for friend {friend.Username}: {ex.Message}");
                    }
                }
            }
        }

        private async Task RefreshFriendLastMessagePreviewAsync(string friendId)
        {
            if (_database == null || string.IsNullOrEmpty(friendId))
                return;

            var remainingDms = await _database.GetDirectMessagesForFriendAsync(friendId);
            var chatDms = remainingDms.Where(d => !d.IsPostReactionOnly).ToList();

            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                var targetFriend = Friends.FirstOrDefault(f => f.Id == friendId);
                if (targetFriend != null)
                {
                    if (chatDms.Count > 0)
                    {
                        var lastDm = chatDms[chatDms.Count - 1];
                        targetFriend.LastMessagePreview = !string.IsNullOrWhiteSpace(lastDm.TextContent)
                            ? lastDm.TextContent
                            : "Fotoğrafa yanıt verdi";
                        targetFriend.LastMessageTimeText = MediaPost.FormatRelativeTime(lastDm.SentAt);
                    }
                    else
                    {
                        targetFriend.LastMessagePreview = "Sohbeti başlatmak için dokun";
                        targetFriend.LastMessageTimeText = MediaPost.FormatRelativeTime(targetFriend.AddedAt);
                    }
                }
            });
        }

        private async Task<bool> TryTransmitDirectMessageAsync(DirectMessage dm, Friend friend)
        {
            if (_database == null || _cryptoService == null || _relayService == null || CurrentUser == null)
                return false;

            var access = Microsoft.Maui.Networking.Connectivity.Current.NetworkAccess;
            bool hasNetwork = access == Microsoft.Maui.Networking.NetworkAccess.Internet ||
                              access == Microsoft.Maui.Networking.NetworkAccess.ConstrainedInternet ||
                              access == Microsoft.Maui.Networking.NetworkAccess.Local;

            if (!hasNetwork)
                return false;

            if (!_relayService.IsConnected)
            {
                if (!string.IsNullOrEmpty(CurrentUser.Id))
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
                        HandleConnectionStateChanged(true);
                    }
                }

                if (!_relayService.IsConnected)
                    return false;
            }

            var privateKey = await GetPrivateKeyAsync();
            if (string.IsNullOrEmpty(privateKey))
                return false;

            try
            {
                var sharedSecret = GetOrDeriveSharedSecret(privateKey, friend.PublicKey);
                var textBytes = Encoding.UTF8.GetBytes(dm.TextContent ?? string.Empty);
                var encryptedContent = _cryptoService.EncryptData(textBytes, sharedSecret);

                byte[]? encryptedReaction = null;
                if (!string.IsNullOrEmpty(dm.ReactionEmoji))
                {
                    var reactionBytes = Encoding.UTF8.GetBytes(dm.ReactionEmoji);
                    encryptedReaction = _cryptoService.EncryptData(reactionBytes, sharedSecret);
                }

                var payload = new Pocket.Shared.DTOs.EncryptedPayloadDto
                {
                    MessageId = dm.Id,
                    SenderId = CurrentUser.Id,
                    RecipientId = friend.Id,
                    MediaPostId = dm.MediaPostId,
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

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[E2EE DM Send Error] {ex.Message}");
                return false;
            }
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
                    Debug.WriteLine(
                        $"[E2EE] Rejected payload {payload.MessageId} from unknown sender {payload.SenderId}");
                    return;
                }

                var privateKey = await GetPrivateKeyAsync();
                if (string.IsNullOrEmpty(privateKey))
                    return;

                var sharedSecret = GetOrDeriveSharedSecret(privateKey, senderFriend.PublicKey);

                if (payload.MediaType == Pocket.Shared.Enums.MediaType.ProfileUpdate)
                {
                    // E2EE Profile Update (Username / DisplayName / Avatar Photo)
                    if (payload.EncryptedContent is { Length: > 0 })
                    {
                        var decryptedBytes = _cryptoService.DecryptData(payload.EncryptedContent, sharedSecret);
                        var profileUpdate =
                            System.Text.Json.JsonSerializer.Deserialize<Pocket.Shared.DTOs.ProfileUpdateDto>(
                                decryptedBytes);
                        if (profileUpdate != null)
                        {
                            if (!string.IsNullOrWhiteSpace(profileUpdate.Username))
                            {
                                senderFriend.Username = profileUpdate.Username;
                            }

                            if (!string.IsNullOrWhiteSpace(profileUpdate.DisplayName))
                            {
                                senderFriend.DisplayName = profileUpdate.DisplayName;
                            }

                            if (profileUpdate.AvatarCleared)
                            {
                                TryDeleteLocalFile(senderFriend.ProfilePictureUrl);
                                senderFriend.ProfilePictureUrl = string.Empty;
                            }
                        }
                    }

                    if (payload.EncryptedMedia is { Length: > 0 })
                    {
                        var decryptedAvatarBytes = _cryptoService.DecryptData(payload.EncryptedMedia, sharedSecret);
                        var profileDir = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "Profile");
                        Directory.CreateDirectory(profileDir);

                        var oldAvatarPath = senderFriend.ProfilePictureUrl;
                        var newAvatarPath = Path.Combine(profileDir,
                            $"friend_{senderFriend.Id}_{payload.MessageId:N}.jpg");
                        await File.WriteAllBytesAsync(newAvatarPath, decryptedAvatarBytes);
                        senderFriend.ProfilePictureUrl = newAvatarPath;
                        TryDeleteLocalFile(oldAvatarPath);
                    }

                    await _database.SaveFriendAsync(senderFriend);

                    // Sender avatar may be shown on the home-screen widget
                    await UpdateLatestPhotoWidgetAsync();

                    Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var targetFriend = Friends.FirstOrDefault(f => f.Id == senderFriend.Id);
                        if (targetFriend != null)
                        {
                            targetFriend.Username = senderFriend.Username;
                            targetFriend.DisplayName = senderFriend.DisplayName;
                            targetFriend.ProfilePictureUrl = senderFriend.ProfilePictureUrl;
                        }

                        OnFriendProfileUpdated?.Invoke(senderFriend);
                    });

                    // BR-202: Confirm delivery so server immediately purges payload from RAM
                    await _relayService.ConfirmDeliveryAsync(payload.SenderId, payload.MessageId);
                    return;
                }

                if (payload.MediaType == Pocket.Shared.Enums.MediaType.PostDeleted)
                {
                    if (payload.EncryptedContent is { Length: > 0 })
                    {
                        var decryptedBytes = _cryptoService.DecryptData(payload.EncryptedContent, sharedSecret);
                        var postIdStr = Encoding.UTF8.GetString(decryptedBytes);
                        if (Guid.TryParse(postIdStr, out var targetPostId))
                        {
                            var postToDelete = await _database.GetMediaPostAsync(targetPostId);
                            if (postToDelete != null && postToDelete.IsIncoming &&
                                postToDelete.FriendId == payload.SenderId)
                            {
                                TryDeleteLocalFile(postToDelete.MediaLocalPath);
                                await _database.DeleteMediaPostAsync(targetPostId);
                                await UpdateLatestPhotoWidgetAsync();

                                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                                {
                                    OnMediaPostDeleted?.Invoke(targetPostId);
                                });
                            }
                        }
                    }

                    await _relayService.ConfirmDeliveryAsync(payload.SenderId, payload.MessageId);
                    return;
                }

                if (payload.MediaType == Pocket.Shared.Enums.MediaType.MessageDeleted)
                {
                    if (payload.EncryptedContent is { Length: > 0 })
                    {
                        var decryptedBytes = _cryptoService.DecryptData(payload.EncryptedContent, sharedSecret);
                        var dmIdStr = Encoding.UTF8.GetString(decryptedBytes);
                        if (Guid.TryParse(dmIdStr, out var targetDmId))
                        {
                            var dmToDelete = await _database.GetDirectMessageAsync(targetDmId);
                            if (dmToDelete != null && dmToDelete.IsIncoming &&
                                dmToDelete.FriendId == payload.SenderId)
                            {
                                await _database.DeleteDirectMessageAsync(targetDmId);
                                await RefreshFriendLastMessagePreviewAsync(dmToDelete.FriendId);

                                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                                {
                                    OnDirectMessageDeleted?.Invoke(targetDmId);
                                });
                            }
                        }
                    }

                    await _relayService.ConfirmDeliveryAsync(payload.SenderId, payload.MessageId);
                    return;
                }

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
                        var decryptedReactionBytes =
                            _cryptoService.DecryptData(payload.EncryptedReaction, sharedSecret);
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
                        SyncStatus = Pocket.Shared.Enums.SyncStatus.Delivered,
                        SenderAvatarUrl = senderFriend.ProfilePictureUrl ?? string.Empty
                    };

                    if (dm.MediaPostId.HasValue)
                    {
                        var refPost = await _database.GetMediaPostAsync(dm.MediaPostId.Value);
                        if (refPost != null)
                        {
                            dm.ReferencedPostPath = refPost.MediaLocalPath;
                            dm.ReferencedPostCaption = refPost.Caption ?? string.Empty;
                            dm.ReferencedPostAuthorLabel = refPost.IsIncoming ? senderFriend.DisplayName : "You";
                            dm.ReferencedPostAuthorAvatarUrl = refPost.IsIncoming
                                ? (senderFriend.ProfilePictureUrl ?? string.Empty)
                                : (CurrentUser.ProfilePictureUrl ?? string.Empty);
                            dm.ReferencedPostTimeText = MediaPost.FormatRelativeTime(refPost.SentAt);
                        }
                    }

                    await _database.SaveDirectMessageAsync(dm);

                    string senderHeader = !string.IsNullOrWhiteSpace(senderFriend.DisplayName)
                        ? senderFriend.DisplayName
                        : senderFriend.Username;

                    if (!string.IsNullOrWhiteSpace(reactionEmoji) && string.IsNullOrWhiteSpace(textContent))
                    {
                        if (IsReactionsNotificationEnabled)
                        {
                            _notificationService?.ShowReactionNotification(
                                senderFriend.Id,
                                senderHeader,
                                senderFriend.ProfilePictureUrl,
                                dm.MediaPostId);
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(textContent))
                    {
                        bool isForeground = IsAppInForeground;
#if ANDROID
                        isForeground = isForeground && MainActivity.IsActivityResumed;
#endif
                        bool isDmWindowActive = isForeground &&
                                                (IsInDmWindow ||
                                                 Microsoft.Maui.Controls.Shell.Current?.CurrentPage is Pocket.Client
                                                     .Pages.DmPage);
                        if (!isDmWindowActive && IsMessagesNotificationEnabled)
                        {
                            _notificationService?.ShowMessageNotification(
                                senderFriend.Id,
                                senderHeader,
                                textContent,
                                senderFriend.ProfilePictureUrl);
                        }
                    }

                    Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (!dm.IsPostReactionOnly)
                        {
                            var targetFriend = Friends.FirstOrDefault(f => f.Id == dm.FriendId);
                            if (targetFriend != null)
                            {
                                targetFriend.LastMessagePreview = !string.IsNullOrWhiteSpace(dm.TextContent)
                                    ? dm.TextContent
                                    : "Fotoğrafa yanıt verdi";
                                targetFriend.LastMessageTimeText = MediaPost.FormatRelativeTime(dm.SentAt);
                            }
                        }

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
                    IsUnseen = !IsViewingHistory,
                    SyncStatus = Pocket.Shared.Enums.SyncStatus.Delivered,
                    SenderDisplayName = senderFriend.DisplayName
                };

                await _database.SaveMediaPostAsync(mediaPost);

                // Refresh the home-screen "latest photo" widget
                await UpdateLatestPhotoWidgetAsync();

                // BR-202: Immediately confirm delivery so the Relay Server purges the transient payload from RAM
                await _relayService.ConfirmDeliveryAsync(payload.SenderId, payload.MessageId);

                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                {
                    OnMediaPostReceived?.Invoke(mediaPost);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[E2EE Receive Error] Failed to decrypt/save payload {payload.MessageId}: {ex.Message}");
            }
        }

        public async Task MarkAllPhotosSeenAsync()
        {
            if (_database == null)
                return;

            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() => { UnseenPhotoCount = 0; });

            try
            {
                await _database.MarkAllIncomingPhotosSeenAsync();
                await UpdateLatestPhotoWidgetAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Unseen] MarkAllPhotosSeenAsync failed: {ex.Message}");
            }
        }

        public async Task UpdateLatestPhotoWidgetAsync()
        {
            if (_database == null)
                return;

            try
            {
                int unseenCount = await _database.GetUnseenIncomingPhotoCountAsync();
                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                {
                    UnseenPhotoCount = unseenCount;
                });

#if ANDROID
                var post = await _database.GetLatestIncomingPhotoPostAsync();
                if (post == null || string.IsNullOrWhiteSpace(post.MediaLocalPath) || !File.Exists(post.MediaLocalPath))
                {
                    Pocket.Client.Platforms.Android.Widgets.LatestPhotoWidgetProvider.Clear();
                    return;
                }

                var friend = await _database.GetFriendAsync(post.FriendId);
                Pocket.Client.Platforms.Android.Widgets.LatestPhotoWidgetProvider.Publish(
                    post.Id,
                    post.MediaLocalPath,
                    friend?.ProfilePictureUrl,
                    post.Caption,
                    friend?.DisplayName,
                    unseenCount);
#endif
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Widget] Update failed: {ex.Message}");
            }
        }

        public async Task DeleteMediaPostAsync(MediaPost post)
        {
            if (_database == null)
                return;

            bool isOwnPost = !post.IsIncoming;
            Guid postId = post.Id;
            string recipientFriendId = post.FriendId;

            TryDeleteLocalFile(post.MediaLocalPath);
            await _database.DeleteMediaPostAsync(postId);
            await UpdateLatestPhotoWidgetAsync();

            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                OnMediaPostDeleted?.Invoke(postId);
            });

            if (isOwnPost && _cryptoService != null && _relayService != null && _relayService.IsConnected)
            {
                var privateKey = await GetPrivateKeyAsync();
                if (!string.IsNullOrEmpty(privateKey))
                {
                    var allFriends = await _database.GetFriendsAsync();
                    var targetFriends = !string.IsNullOrEmpty(recipientFriendId)
                        ? allFriends.Where(f => f.Id == recipientFriendId).ToList()
                        : allFriends;

                    var postIdBytes = Encoding.UTF8.GetBytes(postId.ToString());

                    foreach (var friend in targetFriends)
                    {
                        if (string.IsNullOrEmpty(friend.PublicKey))
                            continue;

                        try
                        {
                            var sharedSecret = GetOrDeriveSharedSecret(privateKey, friend.PublicKey);
                            var encryptedContent = _cryptoService.EncryptData(postIdBytes, sharedSecret);

                            var payload = new Pocket.Shared.DTOs.EncryptedPayloadDto
                            {
                                MessageId = Guid.NewGuid(),
                                SenderId = CurrentUser.Id,
                                RecipientId = friend.Id,
                                MediaPostId = postId,
                                EncryptedContent = encryptedContent,
                                MediaType = Pocket.Shared.Enums.MediaType.PostDeleted,
                                Timestamp = DateTime.UtcNow
                            };

                            await _relayService.SendPayloadAsync(payload);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(
                                $"[E2EE Delete Post Error] Failed for friend {friend.Username}: {ex.Message}");
                        }
                    }
                }
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

        private readonly System.Threading.SemaphoreSlim _pendingPostSyncLock = new(1, 1);
        private bool _isConnectivitySubscribed;

        private void EnsureConnectivitySubscribed()
        {
            if (_isConnectivitySubscribed)
                return;
            _isConnectivitySubscribed = true;

            try
            {
                Microsoft.Maui.Networking.Connectivity.Current.ConnectivityChanged += OnDeviceConnectivityChanged;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Connectivity] Subscription skipped: {ex.Message}");
            }
        }

        private async void OnDeviceConnectivityChanged(object? sender,
            Microsoft.Maui.Networking.ConnectivityChangedEventArgs e)
        {
            bool hasInternet = e.NetworkAccess == Microsoft.Maui.Networking.NetworkAccess.Internet ||
                               e.NetworkAccess == Microsoft.Maui.Networking.NetworkAccess.ConstrainedInternet ||
                               e.NetworkAccess == Microsoft.Maui.Networking.NetworkAccess.Local;

            if (hasInternet && _relayService != null && !_relayService.IsConnected && CurrentUser != null &&
                !string.IsNullOrEmpty(CurrentUser.Id))
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
                    HandleConnectionStateChanged(true);
                }
            }
            else if (!hasInternet)
            {
                HandleConnectionStateChanged(false);
            }
        }

        public async Task<bool> TryUploadMediaPostAsync(MediaPost mediaPost,
            System.Collections.Generic.IReadOnlyList<Friend>? explicitTargetFriends = null)
        {
            if (_database == null || _cryptoService == null || _relayService == null || CurrentUser == null)
                return false;

            if (mediaPost.IsIncoming || mediaPost.SyncStatus == Pocket.Shared.Enums.SyncStatus.Sent ||
                mediaPost.SyncStatus == Pocket.Shared.Enums.SyncStatus.Delivered)
            {
                mediaPost.IsWaitingForServer = false;
                return true;
            }

            if (string.IsNullOrWhiteSpace(mediaPost.MediaLocalPath) || !File.Exists(mediaPost.MediaLocalPath))
                return false;

            var access = Microsoft.Maui.Networking.Connectivity.Current.NetworkAccess;
            bool hasNetwork = access == Microsoft.Maui.Networking.NetworkAccess.Internet ||
                              access == Microsoft.Maui.Networking.NetworkAccess.ConstrainedInternet ||
                              access == Microsoft.Maui.Networking.NetworkAccess.Local;

            if (!hasNetwork)
            {
                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                {
                    mediaPost.IsWaitingForServer = true;
                });
                return false;
            }

            if (!_relayService.IsConnected)
            {
                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                {
                    mediaPost.IsWaitingForServer = true;
                });

                if (!string.IsNullOrEmpty(CurrentUser.Id))
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
                }

                if (!_relayService.IsConnected)
                {
                    return false;
                }
            }

            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                mediaPost.IsWaitingForServer = false;
            });

            var privateKey = await GetPrivateKeyAsync();
            if (string.IsNullOrEmpty(privateKey))
                return false;

            System.Collections.Generic.IReadOnlyList<Friend> targetFriends;
            if (explicitTargetFriends != null && explicitTargetFriends.Count > 0)
            {
                targetFriends = explicitTargetFriends;
            }
            else
            {
                var allFriends = await _database.GetFriendsAsync();
                targetFriends = !string.IsNullOrEmpty(mediaPost.FriendId)
                    ? allFriends.Where(f => f.Id == mediaPost.FriendId).ToList()
                    : allFriends;
            }

            var validFriends = targetFriends.Where(f => !string.IsNullOrEmpty(f.PublicKey)).ToList();
            if (validFriends.Count == 0)
            {
                // No recipients with public keys; mark as Sent once connected to server
                if (_relayService.IsConnected)
                {
                    mediaPost.SyncStatus = Pocket.Shared.Enums.SyncStatus.Sent;
                    mediaPost.IsWaitingForServer = false;
                    await _database.SaveMediaPostAsync(mediaPost);
                    Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                    {
                        OnMediaPostSent?.Invoke(mediaPost.Id);
                    });
                    return true;
                }

                return false;
            }

            byte[] photoBytes;
            try
            {
                photoBytes = await File.ReadAllBytesAsync(mediaPost.MediaLocalPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaUpload] Could not read photo file: {ex.Message}");
                return false;
            }

            var captionBytes = Encoding.UTF8.GetBytes(mediaPost.Caption ?? string.Empty);
            int sentCount = 0;

            foreach (var friend in validFriends)
            {
                if (!_relayService.IsConnected)
                    break;

                try
                {
                    var sharedSecret = GetOrDeriveSharedSecret(privateKey, friend.PublicKey);
                    var encryptedCaption = _cryptoService.EncryptData(captionBytes, sharedSecret);
                    var encryptedMedia = _cryptoService.EncryptData(photoBytes, sharedSecret);

                    var payload = new Pocket.Shared.DTOs.EncryptedPayloadDto
                    {
                        MessageId = mediaPost.Id,
                        SenderId = CurrentUser.Id,
                        RecipientId = friend.Id,
                        EncryptedContent = encryptedCaption,
                        EncryptedMedia = encryptedMedia,
                        MediaType = mediaPost.MediaType,
                        Timestamp = mediaPost.SentAt
                    };

                    await _relayService.SendPayloadAsync(payload);
                    sentCount++;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[E2EE Send Error] Failed for friend {friend.Username}: {ex.Message}");
                }
            }

            if (sentCount > 0)
            {
                var currentInDb = await _database.GetMediaPostAsync(mediaPost.Id);
                if (currentInDb != null && currentInDb.SyncStatus == Pocket.Shared.Enums.SyncStatus.Delivered)
                {
                    mediaPost.SyncStatus = Pocket.Shared.Enums.SyncStatus.Delivered;
                }
                else if (mediaPost.SyncStatus != Pocket.Shared.Enums.SyncStatus.Delivered)
                {
                    mediaPost.SyncStatus = Pocket.Shared.Enums.SyncStatus.Sent;
                    await _database.SaveMediaPostAsync(mediaPost);
                }

                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                {
                    mediaPost.IsWaitingForServer = false;
                    OnMediaPostSent?.Invoke(mediaPost.Id);
                });
                return true;
            }

            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                mediaPost.IsWaitingForServer = true;
            });
            return false;
        }

        public async Task FlushPendingOutgoingMediaPostsAsync()
        {
            if (_database == null || _relayService == null || !_relayService.IsConnected)
                return;

            if (!await _pendingPostSyncLock.WaitAsync(0))
                return;

            try
            {
                var pendingPosts = await _database.GetPendingOutgoingMediaPostsAsync();
                foreach (var pendingPost in pendingPosts)
                {
                    if (!_relayService.IsConnected)
                        break;

                    await TryUploadMediaPostAsync(pendingPost);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PendingSync] FlushPendingOutgoingMediaPostsAsync error: {ex.Message}");
            }
            finally
            {
                _pendingPostSyncLock.Release();
            }
        }

        private void HandleConnectionStateChanged(bool isConnected)
        {
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                IsConnected = isConnected;
                OnConnectionStateChangedEvent?.Invoke(isConnected);

                if (isConnected)
                {
                    ConnectionStatusText = _relayService?.IsOnFallback == true
                        ? "Sunucuya Bağlandı (yedek: Mac)"
                        : "Sunucuya Bağlandı";
                    ConnectionStatusColor = "#10B981"; // Emerald Green
                    ConnectedUrlText = _relayService?.CurrentHubUrl ?? "-";
                    ConnectedAtText = _relayService?.ConnectedAt?.ToString("HH:mm:ss") ?? "-";

                    if (Microsoft.Maui.Storage.Preferences.Default.Get("PendingProfileBroadcast", false))
                    {
                        Microsoft.Maui.Storage.Preferences.Default.Set("PendingProfileBroadcast", false);
                        _ = BroadcastProfileUpdateAsync();
                    }

                    _ = FlushPendingOutgoingMediaPostsAsync();
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
                    else
                    {
                        friend = existing;
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

                    // Immediately share our E2EE profile (name + avatar photo) with the new friend
                    _ = BroadcastProfileUpdateAsync(friend);
                    _ = FlushPendingOutgoingMediaPostsAsync();
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
                            await Microsoft.Maui.Controls.Shell.Current.DisplayAlert("İstek Kabul Edildi",
                                $"{acceptDto.SenderUsername} arkadaşlık isteğinizi kabul etti!", "Tamam");
                        }
                    }
                    else
                    {
                        friend = existing;
                    }

                    // Immediately share our E2EE profile (name + avatar photo) with the new friend
                    _ = BroadcastProfileUpdateAsync(friend);
                    _ = FlushPendingOutgoingMediaPostsAsync();
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
                        await Microsoft.Maui.Controls.Shell.Current.DisplayAlert("Bağlantı Başarılı",
                            $"Sunucuya '{cleanIp}' adresi üzerinden erişildi!", "Tamam");
                    }
                    else
                    {
                        await Microsoft.Maui.Controls.Shell.Current.DisplayAlert("Bağlantı Başarısız",
                            $"'{RelayService.BuildHubUrl(cleanIp)}' adresindeki sunucuya ulaşılamadı. Sunucunun açık olduğundan emin olun.",
                            "Tamam");
                    }
                }
            }
        }

        private IAsyncRelayCommand? _editProfileCommand;
        public IAsyncRelayCommand EditProfileCommand => _editProfileCommand ??= new AsyncRelayCommand(EditProfileAsync);

        private IAsyncRelayCommand? _editProfilePhotoCommand;

        public IAsyncRelayCommand EditProfilePhotoCommand =>
            _editProfilePhotoCommand ??= new AsyncRelayCommand(EditProfilePhotoAsync);

        private bool _isPickingProfilePhoto;

        public async Task EditProfilePhotoAsync()
        {
            if (_isPickingProfilePhoto || CurrentUser == null || Microsoft.Maui.Controls.Shell.Current == null)
                return;

            _isPickingProfilePhoto = true;
            try
            {
                bool hasExistingPhoto = !string.IsNullOrWhiteSpace(CurrentUser.ProfilePictureUrl) &&
                                        File.Exists(CurrentUser.ProfilePictureUrl);

                if (hasExistingPhoto)
                {
                    string action = await Microsoft.Maui.Controls.Shell.Current.DisplayActionSheet(
                        "Profil Fotoğrafı",
                        "İptal",
                        "Fotoğrafı Kaldır",
                        "Galeriden Yeni Fotoğraf Seç");

                    if (action == "Fotoğrafı Kaldır")
                    {
                        var oldPath = CurrentUser.ProfilePictureUrl;
                        CurrentUser.ProfilePictureUrl = string.Empty;
                        TryDeleteLocalFile(oldPath);

                        if (_database != null)
                        {
                            await _database.SaveUserAsync(CurrentUser);
                        }

                        OnPropertyChanged(nameof(CurrentUser));
                        OnUserProfileUpdated?.Invoke(CurrentUser);
                        await BroadcastProfileUpdateAsync();
                        return;
                    }

                    if (action != "Galeriden Yeni Fotoğraf Seç")
                    {
                        return;
                    }
                }

                string? newAvatarPath = await PickAndSaveAvatarPhotoAsync();
                if (!string.IsNullOrEmpty(newAvatarPath) && File.Exists(newAvatarPath))
                {
                    var oldPath = CurrentUser.ProfilePictureUrl;
                    CurrentUser.ProfilePictureUrl = newAvatarPath;
                    TryDeleteLocalFile(oldPath);

                    if (_database != null)
                    {
                        await _database.SaveUserAsync(CurrentUser);
                    }

                    OnPropertyChanged(nameof(CurrentUser));
                    OnUserProfileUpdated?.Invoke(CurrentUser);
                    await BroadcastProfileUpdateAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProfilePhoto] Failed to update profile photo: {ex.Message}");
            }
            finally
            {
                _isPickingProfilePhoto = false;
            }
        }

        private static void TryDeleteLocalFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProfilePhoto] Failed to delete old file '{path}': {ex.Message}");
            }
        }

        private static async Task<string?> PickAndSaveAvatarPhotoAsync()
        {
            var profileDir = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "Profile");
            Directory.CreateDirectory(profileDir);
            var targetPath = Path.Combine(profileDir, $"avatar_{Guid.NewGuid():N}.jpg");

#if ANDROID
            var uri = await MainActivity.PickSinglePhotoAsync();
            if (uri == null)
                return null;

            var resolver = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.ContentResolver
                           ?? global::Android.App.Application.Context?.ContentResolver;
            if (resolver == null)
                return null;

            return await Task.Run(() =>
            {
                int rotationDegrees = 0;
                try
                {
                    if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.N)
                    {
                        using var exifStream = resolver.OpenInputStream(uri);
                        if (exifStream != null)
                        {
                            var exif = new global::Android.Media.ExifInterface(exifStream);
                            int orientation = exif.GetAttributeInt(
                                global::Android.Media.ExifInterface.TagOrientation,
                                (int)global::Android.Media.Orientation.Normal);

                            rotationDegrees = orientation switch
                            {
                                (int)global::Android.Media.Orientation.Rotate90 => 90,
                                (int)global::Android.Media.Orientation.Rotate180 => 180,
                                (int)global::Android.Media.Orientation.Rotate270 => 270,
                                _ => 0
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ProfilePhoto] EXIF read skipped: {ex.Message}");
                }

                using var inputStream = resolver.OpenInputStream(uri);
                if (inputStream == null)
                    return null;

                using var decoded = global::Android.Graphics.BitmapFactory.DecodeStream(inputStream);
                if (decoded == null)
                {
                    using var fallbackIn = resolver.OpenInputStream(uri);
                    if (fallbackIn == null) return null;
                    using var outFile = File.Create(targetPath);
                    fallbackIn.CopyTo(outFile);
                    return targetPath;
                }

                global::Android.Graphics.Bitmap? rotated = null;
                global::Android.Graphics.Bitmap? squareCropped = null;
                global::Android.Graphics.Bitmap? scaled = null;

                try
                {
                    global::Android.Graphics.Bitmap current = decoded;
                    if (rotationDegrees != 0)
                    {
                        using var matrix = new global::Android.Graphics.Matrix();
                        matrix.PostRotate(rotationDegrees);
                        rotated = global::Android.Graphics.Bitmap.CreateBitmap(
                            decoded, 0, 0, decoded.Width, decoded.Height, matrix, true);
                        if (rotated != null)
                            current = rotated;
                    }

                    int side = Math.Min(current.Width, current.Height);
                    int cropX = (current.Width - side) / 2;
                    int cropY = (current.Height - side) / 2;

                    if (cropX > 0 || cropY > 0 || current.Width != current.Height)
                    {
                        squareCropped = global::Android.Graphics.Bitmap.CreateBitmap(current, cropX, cropY, side, side);
                        if (squareCropped != null)
                            current = squareCropped;
                    }

                    const int maxAvatarDim = 512;
                    if (current.Width > maxAvatarDim)
                    {
                        scaled = global::Android.Graphics.Bitmap.CreateScaledBitmap(current, maxAvatarDim, maxAvatarDim,
                            true);
                        if (scaled != null)
                            current = scaled;
                    }

                    using var outStream = File.Create(targetPath);
                    current.Compress(global::Android.Graphics.Bitmap.CompressFormat.Jpeg!, 88, outStream);
                }
                finally
                {
                    if (scaled != null && !ReferenceEquals(scaled, squareCropped) &&
                        !ReferenceEquals(scaled, rotated) && !ReferenceEquals(scaled, decoded))
                        scaled.Dispose();
                    if (squareCropped != null && !ReferenceEquals(squareCropped, rotated) &&
                        !ReferenceEquals(squareCropped, decoded))
                        squareCropped.Dispose();
                    if (rotated != null && !ReferenceEquals(rotated, decoded))
                        rotated.Dispose();
                }

                return targetPath;
            });
#else
            var result = await Microsoft.Maui.Media.MediaPicker.Default.PickPhotoAsync();
            if (result == null)
                return null;

            await using var inStream = await result.OpenReadAsync();
            await using var outStream = File.Create(targetPath);
            await inStream.CopyToAsync(outStream);
            return targetPath;
#endif
        }

        public async Task BroadcastProfileUpdateAsync(Friend? specificFriend = null)
        {
            if (_database == null || _cryptoService == null || _relayService == null || CurrentUser == null)
                return;

            if (!_relayService.IsConnected)
            {
                Microsoft.Maui.Storage.Preferences.Default.Set("PendingProfileBroadcast", true);
                return;
            }

            var privateKey = await GetPrivateKeyAsync();
            if (string.IsNullOrEmpty(privateKey))
                return;

            var targetFriends = specificFriend != null
                ? new System.Collections.Generic.List<Friend> { specificFriend }
                : await _database.GetFriendsAsync();

            if (targetFriends.Count == 0)
                return;

            var profileUpdate = new Pocket.Shared.DTOs.ProfileUpdateDto
            {
                Username = CurrentUser.Username,
                DisplayName = CurrentUser.DisplayName,
                AvatarCleared = string.IsNullOrWhiteSpace(CurrentUser.ProfilePictureUrl)
            };

            var contentBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(profileUpdate);

            byte[]? avatarBytes = null;
            if (!string.IsNullOrWhiteSpace(CurrentUser.ProfilePictureUrl) && File.Exists(CurrentUser.ProfilePictureUrl))
            {
                try
                {
                    avatarBytes = await File.ReadAllBytesAsync(CurrentUser.ProfilePictureUrl);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ProfileBroadcast] Failed to read local avatar file: {ex.Message}");
                }
            }

            foreach (var friend in targetFriends)
            {
                if (string.IsNullOrEmpty(friend.PublicKey))
                    continue;

                try
                {
                    var sharedSecret = GetOrDeriveSharedSecret(privateKey, friend.PublicKey);
                    var encryptedContent = _cryptoService.EncryptData(contentBytes, sharedSecret);
                    var encryptedMedia = avatarBytes is { Length: > 0 }
                        ? _cryptoService.EncryptData(avatarBytes, sharedSecret)
                        : null;

                    var payload = new Pocket.Shared.DTOs.EncryptedPayloadDto
                    {
                        MessageId = Guid.NewGuid(),
                        SenderId = CurrentUser.Id,
                        RecipientId = friend.Id,
                        EncryptedContent = encryptedContent,
                        EncryptedMedia = encryptedMedia,
                        MediaType = Pocket.Shared.Enums.MediaType.ProfileUpdate,
                        Timestamp = DateTime.UtcNow
                    };

                    await _relayService.SendPayloadAsync(payload);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ProfileBroadcast] E2EE send failed for friend {friend.Username}: {ex.Message}");
                }
            }
        }

        public async Task EditProfileAsync()
        {
            if (CurrentUser == null || Microsoft.Maui.Controls.Shell.Current == null) return;

            string initialValue = CurrentUser.Username ?? string.Empty;

            string result = await Microsoft.Maui.Controls.Shell.Current.DisplayPromptAsync(
                title: "Kullanıcı Adını Değiştir",
                message: "Lütfen yeni kullanıcı adınızı girin (şifreli iletişim anahtarlarınız korunur):",
                accept: "Kaydet",
                cancel: "İptal",
                placeholder: "kullanıcı adı",
                maxLength: 30,
                keyboard: Keyboard.Text,
                initialValue: initialValue);

            if (result != null)
            {
                string cleanUsername = result.Trim().TrimStart('@').Trim();
                if (!string.IsNullOrWhiteSpace(cleanUsername))
                {
                    if (_relayService != null && _relayService.IsConnected &&
                        !string.Equals(cleanUsername, CurrentUser.Username, StringComparison.OrdinalIgnoreCase))
                    {
                        var existingActive = await _relayService.LookupUserAsync(cleanUsername);
                        if (existingActive != null && existingActive.UserId != CurrentUser.Id)
                        {
                            await Microsoft.Maui.Controls.Shell.Current.DisplayAlert(
                                "Kullanıcı Adı Kullanımda",
                                $"{cleanUsername} şu anda başka bir kullanıcı tarafından kullanılıyor. Lütfen farklı bir isim seçin.",
                                "Tamam");
                            return;
                        }
                    }

                    CurrentUser.Username = cleanUsername;
                    CurrentUser.DisplayName = cleanUsername;

                    OnPropertyChanged(nameof(CurrentUser));
                    OnUserProfileUpdated?.Invoke(CurrentUser);

                    if (_database != null && _relayService != null)
                    {
                        await _database.SaveUserAsync(CurrentUser);

                        // Reconnect to generate a new JWT with the updated username while preserving UserId & ECDH PublicKey
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

                        // Sync updated username/display name over E2EE to all mutual friends
                        await BroadcastProfileUpdateAsync();
                    }
                }
            }
        }

        private bool _isBackupOperationInProgress;

        [RelayCommand]
        private async Task ExportDataAsync()
        {
            if (_isBackupOperationInProgress || _backupService == null || Microsoft.Maui.Controls.Shell.Current == null)
                return;

            _isBackupOperationInProgress = true;
            try
            {
                var privateKey = await GetPrivateKeyAsync();
                var package = await _backupService.CreateExportPackageAsync(privateKey);

                string? savedDisplayPath = null;
#if ANDROID
                savedDisplayPath = await MainActivity.SaveToDownloadsAsync(package.SuggestedFileName, package.ZipBytes);
#endif
                if (string.IsNullOrEmpty(savedDisplayPath))
                {
                    var fallbackPath = Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory,
                        package.SuggestedFileName);
                    await File.WriteAllBytesAsync(fallbackPath, package.ZipBytes);
                    savedDisplayPath = fallbackPath;
                }

                string summaryMessage =
                    $"Kullanıcı profili, arkadaşlar, gönderiler, mesajlar, reaksiyonlar ve E2EE şifreleme anahtarları başarıyla dışa aktarıldı.\n\n" +
                    $"📁 Konum: {savedDisplayPath}\n" +
                    $"📦 Boyut: {package.ZipBytes.Length / 1024.0:F1} KB";

                bool share = await Microsoft.Maui.Controls.Shell.Current.DisplayAlert(
                    "Dışa Aktarma (Export) Tamamlandı",
                    summaryMessage,
                    "Paylaş / Farklı Kaydet",
                    "Tamam");

                if (share)
                {
                    var shareTempPath = Path.Combine(Microsoft.Maui.Storage.FileSystem.CacheDirectory,
                        package.SuggestedFileName);
                    await File.WriteAllBytesAsync(shareTempPath, package.ZipBytes);
                    await Microsoft.Maui.ApplicationModel.DataTransfer.Share.Default.RequestAsync(
                        new Microsoft.Maui.ApplicationModel.DataTransfer.ShareFileRequest
                        {
                            Title = "Pocket Yedeğini Paylaş",
                            File = new Microsoft.Maui.ApplicationModel.DataTransfer.ShareFile(shareTempPath)
                        });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Backup Export Error] {ex}");
                if (Microsoft.Maui.Controls.Shell.Current != null)
                {
                    await Microsoft.Maui.Controls.Shell.Current.DisplayAlert(
                        "Dışa Aktarma Hatası",
                        $"Yedek oluşturulurken bir hata oluştu:\n{ex.Message}",
                        "Tamam");
                }
            }
            finally
            {
                _isBackupOperationInProgress = false;
            }
        }

        [RelayCommand]
        private async Task ImportDataAsync()
        {
            if (_isBackupOperationInProgress || _backupService == null || Microsoft.Maui.Controls.Shell.Current == null)
                return;

            _isBackupOperationInProgress = true;
            try
            {
                Stream? fileStream = null;
                string fileName = "backup.zip";

#if ANDROID
                var pickedUri = await MainActivity.PickBackupFileAsync();
                if (pickedUri == null)
                    return;

                var resolver = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.ContentResolver
                               ?? global::Android.App.Application.Context?.ContentResolver;
                if (resolver == null)
                    return;

                fileName = pickedUri.LastPathSegment ?? "backup.zip";
                fileStream = resolver.OpenInputStream(pickedUri);
#else
                var picked = await Microsoft.Maui.Storage.FilePicker.Default.PickAsync();
                if (picked == null)
                    return;

                fileName = picked.FileName;
                fileStream = await picked.OpenReadAsync();
#endif
                if (fileStream == null)
                    return;

                await using (fileStream)
                {
                    const string optionMerge = "Mevcut Verilerin Üzerine Ekle (Birleştir)";
                    const string optionClean = "Öncekileri Sıfırla (Temiz Kurulum)";

                    string? modeChoice = await Microsoft.Maui.Controls.Shell.Current.DisplayActionSheet(
                        "Varolan Verilere Ne Yapılsın?",
                        "İptal",
                        optionClean,
                        optionMerge);

                    if (string.IsNullOrEmpty(modeChoice) || modeChoice == "İptal")
                        return;

                    bool mergeWithExisting = (modeChoice == optionMerge);
                    var importResult = await _backupService.ImportBackupAsync(fileStream, fileName, mergeWithExisting);

                    if (!importResult.Success)
                    {
                        await Microsoft.Maui.Controls.Shell.Current.DisplayAlert(
                            "İçe Aktarma Başarısız",
                            importResult.ErrorMessage ?? "Yedek dosyası okunamadı.",
                            "Tamam");
                        return;
                    }

                    await ReloadAfterImportAsync();

                    string modeLabel = mergeWithExisting
                        ? "Üzerine Eklendi (Birleştirildi)"
                        : "Temiz Kurulum Olarak Yüklendi";
                    await Microsoft.Maui.Controls.Shell.Current.DisplayAlert(
                        "İçe Aktarma (Import) Başarılı",
                        $"Mod: {modeLabel}\n" +
                        $"👤 Kullanıcı: {CurrentUser.DisplayName} (@{CurrentUser.Username})\n" +
                        $"👥 Arkadaşlar: {importResult.FriendsCount} (🔐 {importResult.SharedSecretsCount} E2EE Anahtar)\n" +
                        $"📸 Gönderiler: {importResult.PostsCount}\n" +
                        $"💬 Mesaj & Reaksiyonlar: {importResult.MessagesAndReactionsCount}\n" +
                        $"🗄️ İşlenen Tablo / Satır: {importResult.TablesProcessed} tablo, {importResult.TotalRowsImported} kayıt",
                        "Tamam");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Backup Import Error] {ex}");
                if (Microsoft.Maui.Controls.Shell.Current != null)
                {
                    await Microsoft.Maui.Controls.Shell.Current.DisplayAlert(
                        "İçe Aktarma Hatası",
                        $"Yedek geri yüklenirken bir hata oluştu:\n{ex.Message}",
                        "Tamam");
                }
            }
            finally
            {
                _isBackupOperationInProgress = false;
            }
        }

        public async Task ReloadAfterImportAsync()
        {
            _cachedPrivateKey = null;
            _sharedSecretCache.Clear();

            // Refresh preference-bound properties
            IsCaptureFromPreview = Microsoft.Maui.Storage.Preferences.Get(
                Pocket.Client.Controls.CameraView.CaptureFromPreviewPreferenceKey,
                Pocket.Client.Controls.CameraView.CaptureFromPreviewDefault);
            IsZoomSliderEnabled = Microsoft.Maui.Storage.Preferences.Get(
                Pocket.Client.Controls.CameraView.ZoomSliderPreferenceKey,
                Pocket.Client.Controls.CameraView.ZoomSliderDefault);
            IsExposureSliderEnabled = Microsoft.Maui.Storage.Preferences.Get(
                Pocket.Client.Controls.CameraView.ExposureSliderPreferenceKey,
                Pocket.Client.Controls.CameraView.ExposureSliderDefault);
            IsKeepLastZoom = Microsoft.Maui.Storage.Preferences.Get(
                Pocket.Client.Controls.CameraView.KeepZoomPreferenceKey,
                Pocket.Client.Controls.CameraView.KeepZoomDefault);
            IsKeepLastExposure = Microsoft.Maui.Storage.Preferences.Get(
                Pocket.Client.Controls.CameraView.KeepExposurePreferenceKey,
                Pocket.Client.Controls.CameraView.KeepExposureDefault);
            AccentColorHex = GetSavedAccentColorHex();
            ApplyAccentColorResources(AccentColorHex);

            await LoadUserAsync();

            // Pre-warm E2EE shared secrets for all restored friends
            var privateKey = await GetPrivateKeyAsync();
            if (!string.IsNullOrEmpty(privateKey) && _cryptoService != null)
            {
                foreach (var friend in Friends)
                {
                    if (!string.IsNullOrEmpty(friend.PublicKey))
                    {
                        try
                        {
                            _ = GetOrDeriveSharedSecret(privateKey, friend.PublicKey);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(
                                $"[E2EE Warmup] Could not derive secret for {friend.Username}: {ex.Message}");
                        }
                    }
                }
            }

            OnPropertyChanged(nameof(CurrentUser));
            OnUserProfileUpdated?.Invoke(CurrentUser);
            OnDataImported?.Invoke();
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

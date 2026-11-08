using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pocket.Client.Data;
using Pocket.Client.Models;
using Pocket.Client.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pocket.Client.PageModels
{
    public partial class CameraPageModel : ObservableObject
    {
        private readonly LocalDatabase _database;
        private readonly ICryptoService _cryptoService;
        private readonly IRelayService _relayService;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsBottomBarVisible))]
        [NotifyPropertyChangedFor(nameof(IsNotPreviewing))]
        [NotifyPropertyChangedFor(nameof(ShowPreviewControls))]
        [NotifyPropertyChangedFor(nameof(ShowSaveToGalleryButton))]
        [NotifyPropertyChangedFor(nameof(ShowHistoryButton))]
        [NotifyPropertyChangedFor(nameof(ShowFeedOverlayBar))]
        [NotifyPropertyChangedFor(nameof(ShowFeedNoActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedReplyBar))]
        private bool _isPreviewing;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowSaveToGalleryButton))]
        private bool _isFromGallery;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowPreviewControls))]
        [NotifyPropertyChangedFor(nameof(ShowSaveToGalleryButton))]
        private bool _isEditingCaption;

        [ObservableProperty]
        private bool _isAllSelected = true;

        public bool IsNotPreviewing => !IsPreviewing;
        public bool ShowPreviewControls => IsPreviewing && !IsEditingCaption;
        public bool ShowSaveToGalleryButton => ShowPreviewControls && !IsFromGallery;

        [ObservableProperty]
        private string _capturedImagePath = string.Empty;

        [ObservableProperty]
        private string _caption = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowHistoryButton))]
        [NotifyPropertyChangedFor(nameof(ShowFeedOverlayBar))]
        [NotifyPropertyChangedFor(nameof(ShowFeedNoActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedReplyBar))]
        private bool _isViewingFeed;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsBottomBarVisible))]
        [NotifyPropertyChangedFor(nameof(IsCameraPanelActive))]
        [NotifyPropertyChangedFor(nameof(IsChatPanelActive))]
        [NotifyPropertyChangedFor(nameof(ShowHomePillInBottomBar))]
        [NotifyPropertyChangedFor(nameof(ShowHomeIconInBottomBar))]
        [NotifyPropertyChangedFor(nameof(ShowMiniShutterInBottomBar))]
        [NotifyPropertyChangedFor(nameof(ShowFeedSideButtonsInBottomBar))]
        [NotifyPropertyChangedFor(nameof(ShowHistoryButton))]
        [NotifyPropertyChangedFor(nameof(ShowFeedOverlayBar))]
        [NotifyPropertyChangedFor(nameof(ShowFeedNoActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedReplyBar))]
        private int _activePanelIndex = 1; // 0=Profile, 1=Camera/Feed, 2=Chat

        public bool IsBottomBarVisible => ActivePanelIndex != 0 && !IsPreviewing;
        public bool IsCameraPanelActive => ActivePanelIndex == 1;
        public bool IsChatPanelActive => ActivePanelIndex == 2;
        public bool ShowHomePillInBottomBar => ActivePanelIndex == 1 && !IsViewingFeed;
        public bool ShowHomeIconInBottomBar => ActivePanelIndex == 2;
        public bool ShowMiniShutterInBottomBar => ActivePanelIndex == 1 && IsViewingFeed;
        public bool ShowFeedSideButtonsInBottomBar => ActivePanelIndex == 1 && IsViewingFeed;

        public bool ShowHistoryButton => !IsPreviewing && !IsViewingFeed;
        public bool ShowFeedOverlayBar => !IsPreviewing && IsViewingFeed && ActivePanelIndex == 1 && CurrentFeedPost != null;
        public bool ShowFeedNoActivityPill => ShowFeedOverlayBar && CurrentFeedPost!.HasNoActivity;
        public bool ShowFeedActivityPill => ShowFeedOverlayBar && CurrentFeedPost!.ShowActivityPill;
        public bool ShowFeedReplyBar => ShowFeedOverlayBar && CurrentFeedPost!.IsIncoming;

        [ObservableProperty]
        private double _viewportHeight = 840;

        [ObservableProperty]
        private double _cardSize = 399;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowFeedOverlayBar))]
        [NotifyPropertyChangedFor(nameof(ShowFeedNoActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedReplyBar))]
        private MediaPost? _currentFeedPost;

        [ObservableProperty]
        private string _firstRecentEmoji = "💗";

        [ObservableProperty]
        private string _secondRecentEmoji = "🙃";

        [ObservableProperty]
        private string _thirdRecentEmoji = "😋";

        [ObservableProperty]
        private bool _isActivitySheetOpen;

        public Action? OpenActivitySheetAction;
        public Action? CloseActivitySheetAction;

        [RelayCommand]
        private void OpenActivitySheet()
        {
            if (CurrentFeedPost != null && CurrentFeedPost.HasActivity)
            {
                IsActivitySheetOpen = true;
                OpenActivitySheetAction?.Invoke();
            }
        }

        [RelayCommand]
        private void CloseActivitySheet()
        {
            if (CloseActivitySheetAction != null)
            {
                CloseActivitySheetAction.Invoke();
            }
            else
            {
                IsActivitySheetOpen = false;
            }
        }

        partial void OnCurrentFeedPostChanged(MediaPost? value)
        {
            if (CloseActivitySheetAction != null)
            {
                CloseActivitySheetAction.Invoke();
            }
            else
            {
                IsActivitySheetOpen = false;
            }
        }

        partial void OnIsViewingFeedChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowHomePillInBottomBar));
            OnPropertyChanged(nameof(ShowMiniShutterInBottomBar));
            OnPropertyChanged(nameof(ShowFeedSideButtonsInBottomBar));

            if (!value)
            {
                if (CloseActivitySheetAction != null)
                {
                    CloseActivitySheetAction.Invoke();
                }
                else
                {
                    IsActivitySheetOpen = false;
                }
            }
        }

        partial void OnViewportHeightChanged(double value)
        {
            foreach (var post in FeedPosts)
            {
                post.ViewportHeight = value;
            }
        }

        partial void OnCardSizeChanged(double value)
        {
            foreach (var post in FeedPosts)
            {
                post.CardSize = value;
            }
        }

        public AppShellModel ShellModel { get; }

        public Action? SnapToProfileAction;
        public Action? SnapToCameraAction;
        public Action? SnapToChatAction;
        public Action? ScrollToHistoryAction;
        public Action? ScrollToCameraAction;
        public Action<bool>? PreviewStateChangedAction;

        public CameraPageModel(
            LocalDatabase database,
            AppShellModel shellModel,
            ICryptoService cryptoService,
            IRelayService relayService)
        {
            _database = database;
            ShellModel = shellModel;
            _cryptoService = cryptoService;
            _relayService = relayService;

            ShellModel.OnMediaPostReceived += HandleMediaPostReceived;
            ShellModel.OnMediaPostDelivered += HandleMediaPostDelivered;
            ShellModel.OnDirectMessageReceived += HandleDirectMessageReceived;

            LoadRecentEmojis();
        }

        private void LoadRecentEmojis()
        {
            var saved = Microsoft.Maui.Storage.Preferences.Default.Get("RecentEmojis", "💗,🙃,😋");
            var parts = saved.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0) FirstRecentEmoji = parts[0].Trim();
            if (parts.Length > 1) SecondRecentEmoji = parts[1].Trim();
            if (parts.Length > 2) ThirdRecentEmoji = parts[2].Trim();
        }

        public void UpdateRecentEmoji(string emoji)
        {
            if (string.IsNullOrWhiteSpace(emoji)) return;

            var list = new System.Collections.Generic.List<string> { FirstRecentEmoji, SecondRecentEmoji, ThirdRecentEmoji };
            list.Remove(emoji);
            list.Insert(0, emoji); // En son kullanılan emoji en soldan başlar

            FirstRecentEmoji = list.Count > 0 ? list[0] : "💗";
            SecondRecentEmoji = list.Count > 1 ? list[1] : "🙃";
            ThirdRecentEmoji = list.Count > 2 ? list[2] : "😋";

            Microsoft.Maui.Storage.Preferences.Default.Set("RecentEmojis", $"{FirstRecentEmoji},{SecondRecentEmoji},{ThirdRecentEmoji}");
        }

        [RelayCommand]
        private void SendFirstRecentEmoji()
        {
            SendRecentEmoji(FirstRecentEmoji);
        }

        [RelayCommand]
        private void SendSecondRecentEmoji()
        {
            SendRecentEmoji(SecondRecentEmoji);
        }

        [RelayCommand]
        private void SendThirdRecentEmoji()
        {
            SendRecentEmoji(ThirdRecentEmoji);
        }

        private void SendRecentEmoji(string emoji)
        {
            UpdateRecentEmoji(emoji);
            if (CurrentFeedPost != null)
            {
                HandlePostReplyOrReact(CurrentFeedPost, emoji);
            }
        }

        public ObservableCollection<MediaPost> FeedPosts { get; } = new();

        private void HandleDirectMessageReceived(DirectMessage dm)
        {
            if (dm.IsIncoming && dm.MediaPostId.HasValue && !string.IsNullOrWhiteSpace(dm.ReactionEmoji))
            {
                var targetPost = FeedPosts.FirstOrDefault(p => p.Id == dm.MediaPostId.Value);
                if (targetPost != null)
                {
                    var friend = ShellModel.Friends.FirstOrDefault(f => f.Id == dm.FriendId);
                    var friendName = friend?.DisplayName ?? "Friend";
                    var friendAvatar = friend?.ProfilePictureUrl ?? string.Empty;
                    targetPost.AddOrUpdateReaction(dm.FriendId, friendName, friendAvatar, dm.ReactionEmoji);
                    if (ReferenceEquals(CurrentFeedPost, targetPost))
                    {
                        OnPropertyChanged(nameof(ShowFeedNoActivityPill));
                        OnPropertyChanged(nameof(ShowFeedActivityPill));
                    }
                }
            }
        }

        private void HandleMediaPostReceived(MediaPost post)
        {
            post.ViewportHeight = ViewportHeight;
            post.CardSize = CardSize;
            post.OnReplyOrReactRequested = HandlePostReplyOrReact;
            if (FeedPosts.All(p => p.Id != post.Id))
            {
                FeedPosts.Insert(0, post);
                CurrentFeedPost ??= post;
            }
        }

        private void HandleMediaPostDelivered(Guid messageId)
        {
            var existing = FeedPosts.FirstOrDefault(p => p.Id == messageId);
            if (existing != null)
            {
                existing.SyncStatus = Shared.Enums.SyncStatus.Delivered;
            }
        }

        private async void HandlePostReplyOrReact(MediaPost post, string? reactionEmoji)
        {
            string targetFriendId = !string.IsNullOrEmpty(post.FriendId)
                ? post.FriendId
                : ShellModel.Friends.FirstOrDefault()?.Id ?? string.Empty;

            if (string.IsNullOrEmpty(targetFriendId))
            {
                if (Microsoft.Maui.Controls.Shell.Current != null)
                {
                    await Microsoft.Maui.Controls.Shell.Current.DisplayAlert(
                        "Arkadaş Bulunamadı",
                        "Yanıt veya reaksiyon göndermek için önce bir arkadaş ekleyin.",
                        "Tamam");
                }
                return;
            }

            if (!string.IsNullOrEmpty(reactionEmoji))
            {
                UpdateRecentEmoji(reactionEmoji);
                // Send reaction silently in background without navigating to DM page or entering replying mode
                _ = ShellModel.SendDirectMessageAsync(targetFriendId, null, reactionEmoji, post.Id);
                return;
            }

            if (Microsoft.Maui.Controls.Shell.Current != null)
            {
                await Microsoft.Maui.Controls.Shell.Current.GoToAsync(
                    $"{nameof(Pages.DmPage)}?FriendId={Uri.EscapeDataString(targetFriendId)}&MediaPostId={post.Id}");
            }
        }

        public async Task LoadFeedAsync()
        {
            var friends = await _database.GetFriendsAsync();
            var friendMap = friends.ToDictionary(f => f.Id, f => f);

            var posts = await _database.GetAllMediaPostsAsync();
            var preparedPosts = new System.Collections.Generic.List<MediaPost>(posts.Count);
            foreach (var post in posts)
            {
                if (post.IsIncoming && !string.IsNullOrEmpty(post.FriendId) && friendMap.TryGetValue(post.FriendId, out var senderFriend))
                {
                    post.SenderDisplayName = senderFriend.Username;
                }
                else if (!post.IsIncoming)
                {
                    post.SenderDisplayName = ShellModel.CurrentUser.Username;
                }

                var postDms = await _database.GetDirectMessagesForPostAsync(post.Id);
                foreach (var dm in postDms.Where(d => d.IsIncoming && !string.IsNullOrWhiteSpace(d.ReactionEmoji)))
                {
                    friendMap.TryGetValue(dm.FriendId, out var reactFriend);
                    var rName = reactFriend?.DisplayName ?? "Friend";
                    var rAvatar = reactFriend?.ProfilePictureUrl ?? string.Empty;
                    post.AddOrUpdateReaction(dm.FriendId, rName, rAvatar, dm.ReactionEmoji!);
                }

                post.ViewportHeight = ViewportHeight;
                post.CardSize = CardSize;
                post.OnReplyOrReactRequested = HandlePostReplyOrReact;
                preparedPosts.Add(post);
            }

            // If the feed list ids haven't changed, update existing items in place so RecyclerView doesn't lose scroll/layout state
            bool sameSequence = FeedPosts.Count == preparedPosts.Count &&
                                FeedPosts.Select(p => p.Id).SequenceEqual(preparedPosts.Select(p => p.Id));

            if (sameSequence)
            {
                for (int i = 0; i < preparedPosts.Count; i++)
                {
                    var existing = FeedPosts[i];
                    var updated = preparedPosts[i];
                    existing.SenderDisplayName = updated.SenderDisplayName;
                    existing.ViewportHeight = ViewportHeight;
                    existing.CardSize = CardSize;
                    existing.OnReplyOrReactRequested = HandlePostReplyOrReact;
                    existing.ActivityItems.Clear();
                    foreach (var item in updated.ActivityItems)
                    {
                        existing.ActivityItems.Add(item);
                    }
                    existing.NotifyActivityChanged();
                }
            }
            else
            {
                FeedPosts.Clear();
                foreach (var post in preparedPosts)
                {
                    FeedPosts.Add(post);
                }
            }

            if (FeedPosts.Count == 0)
            {
                CurrentFeedPost = null;
                IsViewingFeed = false;
            }
            else if (CurrentFeedPost == null || FeedPosts.All(p => p.Id != CurrentFeedPost.Id))
            {
                CurrentFeedPost = FeedPosts.FirstOrDefault();
            }

            OnPropertyChanged(nameof(ShowFeedOverlayBar));
            OnPropertyChanged(nameof(ShowFeedNoActivityPill));
            OnPropertyChanged(nameof(ShowFeedActivityPill));
            OnPropertyChanged(nameof(ShowFeedReplyBar));
        }

        public void OnPhotoCaptured(string imagePath, bool isFromGallery = false)
        {
            IsViewingFeed = false;
            CapturedImagePath = imagePath;
            IsFromGallery = isFromGallery;
            IsEditingCaption = false;
            IsAllSelected = true;
            foreach (var f in ShellModel.Friends)
            {
                f.IsSelectedForSend = false;
            }
            IsPreviewing = true;
            PreviewStateChangedAction?.Invoke(true);
        }

        [RelayCommand]
        private void SelectAllRecipients()
        {
            IsAllSelected = true;
            foreach (var f in ShellModel.Friends)
            {
                f.IsSelectedForSend = false;
            }
        }

        [RelayCommand]
        private void ToggleRecipient(Friend? friend)
        {
            if (friend == null) return;

            if (IsAllSelected)
            {
                IsAllSelected = false;
                foreach (var f in ShellModel.Friends)
                {
                    f.IsSelectedForSend = (f.Id == friend.Id);
                }
            }
            else
            {
                friend.IsSelectedForSend = !friend.IsSelectedForSend;
                if (!ShellModel.Friends.Any(f => f.IsSelectedForSend))
                {
                    IsAllSelected = true;
                }
            }
        }

        [RelayCommand]
        private void SnapToProfile()
        {
            SnapToProfileAction?.Invoke();
        }

        [RelayCommand]
        private void SnapToCamera()
        {
            SnapToCameraAction?.Invoke();
        }

        [RelayCommand]
        private void SnapToChat()
        {
            SnapToChatAction?.Invoke();
        }

        [RelayCommand]
        private void ScrollToHistory()
        {
            ScrollToHistoryAction?.Invoke();
        }

        [RelayCommand]
        private void OnCenterBottomTabTapped()
        {
            if (ActivePanelIndex == 2)
            {
                SnapToCameraAction?.Invoke();
            }
            else if (ActivePanelIndex == 1 && IsViewingFeed)
            {
                ScrollToCameraAction?.Invoke();
            }
        }

        [RelayCommand]
        private void Cancel()
        {
            if (!string.IsNullOrEmpty(CapturedImagePath) && System.IO.File.Exists(CapturedImagePath))
            {
                try
                {
                    System.IO.File.Delete(CapturedImagePath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to delete temp cache file on cancel: {ex.Message}");
                }
            }

            IsEditingCaption = false;
            IsPreviewing = false;
            CapturedImagePath = string.Empty;
            Caption = string.Empty;
            PreviewStateChangedAction?.Invoke(false);
        }

        [RelayCommand]
        private async Task SendAsync()
        {
            if (string.IsNullOrEmpty(CapturedImagePath) || !System.IO.File.Exists(CapturedImagePath)) return;

            // Migrate temporary cached photo to persistent AppDataDirectory storage
            var mediaDir = System.IO.Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "Media");
            System.IO.Directory.CreateDirectory(mediaDir);

            var persistentPath = System.IO.Path.Combine(mediaDir, $"post_{Guid.NewGuid()}.jpg");
            System.IO.File.Copy(CapturedImagePath, persistentPath, true);

            // Clean up temporary cache file
            try
            {
                System.IO.File.Delete(CapturedImagePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to delete temp cache file: {ex.Message}");
            }

            var allFriends = await _database.GetFriendsAsync();
            var selectedFriendIds = ShellModel.Friends
                .Where(f => f.IsSelectedForSend)
                .Select(f => f.Id)
                .ToHashSet();

            var targetFriends = (IsAllSelected || selectedFriendIds.Count == 0)
                ? allFriends
                : allFriends.Where(f => selectedFriendIds.Contains(f.Id)).ToList();

            var sentCaption = Caption;
            var mediaPost = new MediaPost
            {
                Id = Guid.NewGuid(),
                FriendId = targetFriends.Count == 1 ? targetFriends[0].Id : string.Empty,
                IsIncoming = false,
                MediaLocalPath = persistentPath,
                MediaType = Shared.Enums.MediaType.Photo,
                Caption = sentCaption,
                SentAt = DateTime.UtcNow,
                IsIncludedInRollcall = true,
                SyncStatus = Shared.Enums.SyncStatus.Pending,
                SenderDisplayName = ShellModel.CurrentUser.Username,
                ViewportHeight = ViewportHeight,
                CardSize = CardSize,
                OnReplyOrReactRequested = HandlePostReplyOrReact
            };

            await _database.SaveMediaPostAsync(mediaPost);

            FeedPosts.Insert(0, mediaPost);
            CurrentFeedPost = mediaPost;

            IsViewingFeed = false;
            IsEditingCaption = false;
            IsPreviewing = false;
            CapturedImagePath = string.Empty;
            Caption = string.Empty;
            PreviewStateChangedAction?.Invoke(false);

            // E2EE Encryption & Transmission to Mutual Friends (BR-601, BR-201)
            var privateKey = await ShellModel.GetPrivateKeyAsync();

            if (targetFriends.Count > 0 && !string.IsNullOrEmpty(privateKey) && _relayService.IsConnected)
            {
                var photoBytes = await System.IO.File.ReadAllBytesAsync(persistentPath);
                var captionBytes = Encoding.UTF8.GetBytes(sentCaption ?? string.Empty);
                int sentCount = 0;

                foreach (var friend in targetFriends)
                {
                    if (string.IsNullOrEmpty(friend.PublicKey)) continue;

                    try
                    {
                        var sharedSecret = _cryptoService.DeriveSharedSecret(privateKey, friend.PublicKey);
                        var encryptedCaption = _cryptoService.EncryptData(captionBytes, sharedSecret);
                        var encryptedMedia = _cryptoService.EncryptData(photoBytes, sharedSecret);

                        var payload = new Pocket.Shared.DTOs.EncryptedPayloadDto
                        {
                            MessageId = mediaPost.Id,
                            SenderId = ShellModel.CurrentUser.Id,
                            RecipientId = friend.Id,
                            EncryptedContent = encryptedCaption,
                            EncryptedMedia = encryptedMedia,
                            MediaType = Shared.Enums.MediaType.Photo,
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
                    mediaPost.SyncStatus = Shared.Enums.SyncStatus.Sent;
                    await _database.SaveMediaPostAsync(mediaPost);
                }
            }
        }
    }
}

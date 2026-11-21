using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui;
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
    public enum FeedFilterMode
    {
        AllFriends,
        Friend,
        You
    }

    public partial class CameraPageModel : ObservableObject
    {
        private readonly LocalDatabase _database;
        private readonly ICryptoService _cryptoService;
        private readonly IRelayService _relayService;
        private readonly System.Collections.Generic.List<MediaPost> _allFeedPosts = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsBottomBarVisible))]
        [NotifyPropertyChangedFor(nameof(IsNotPreviewing))]
        [NotifyPropertyChangedFor(nameof(IsTopOverlayBarVisible))]
        [NotifyPropertyChangedFor(nameof(IsSwipeAllowed))]
        [NotifyPropertyChangedFor(nameof(ShowPreviewControls))]
        [NotifyPropertyChangedFor(nameof(ShowSaveToGalleryButton))]
        [NotifyPropertyChangedFor(nameof(ShowHistoryButton))]
        [NotifyPropertyChangedFor(nameof(ShowFeedOverlayBar))]
        [NotifyPropertyChangedFor(nameof(ShowFeedNoActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedReplyBar))]
        private bool _isPreviewing;

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowSaveToGalleryButton))]
        private bool _isFromGallery;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowPreviewControls))]
        [NotifyPropertyChangedFor(nameof(ShowSaveToGalleryButton))]
        private bool _isEditingCaption;

        [ObservableProperty] private bool _isAllSelected = true;

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(SelectedFilterTitle))]
        private FeedFilterMode _selectedFilterMode = FeedFilterMode.AllFriends;

        [ObservableProperty] private string _selectedFilterFriendId = string.Empty;

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(SelectedFilterTitle))]
        private string _selectedFilterFriendName = string.Empty;

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsSwipeAllowed))]
        private bool _isFilterMenuOpen;

        public string SelectedFilterTitle => SelectedFilterMode switch
        {
            FeedFilterMode.You => "You",
            FeedFilterMode.Friend => !string.IsNullOrWhiteSpace(SelectedFilterFriendName)
                ? GetFirstWord(SelectedFilterFriendName)
                : "Friend",
            _ => "All Friends"
        };

        private static string GetFirstWord(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : text;
        }

        public int AllFeedPostsCount => _allFeedPosts.Count;
        public bool ShowCameraFriendsPill => !IsViewingFeed && !IsGridGalleryOpen;
        public bool ShowFeedFilterPill => IsViewingFeed || IsGridGalleryOpen;
        public bool ShowEmptyFilteredFeedCard => IsViewingFeed && !IsGridGalleryOpen && FeedPosts.Count == 0;

        public bool IsNotPreviewing => !IsPreviewing;
        public bool IsTopOverlayBarVisible => !IsPreviewing && !IsReplyingToFeedPost;

        public bool IsSwipeAllowed => !IsPreviewing && !IsReplyingToFeedPost && !IsActivitySheetOpen &&
                                      !IsFilterMenuOpen && !IsEmojiPickerOpen && !IsPostOptionsMenuOpen &&
                                      !IsDeleteConfirmOpen && !IsNotificationsSheetOpen && !IsConnectionSheetOpen;

        public bool ShowPreviewControls => IsPreviewing && !IsEditingCaption;
        public bool ShowSaveToGalleryButton => ShowPreviewControls && !IsFromGallery;

        [ObservableProperty] private string _capturedImagePath = string.Empty;

        [ObservableProperty] private string _caption = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowHistoryButton))]
        [NotifyPropertyChangedFor(nameof(ShowFeedOverlayBar))]
        [NotifyPropertyChangedFor(nameof(ShowFeedNoActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedReplyBar))]
        [NotifyPropertyChangedFor(nameof(ShowCameraFriendsPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedFilterPill))]
        [NotifyPropertyChangedFor(nameof(ShowEmptyFilteredFeedCard))]
        private bool _isViewingFeed;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowHomePillInBottomBar))]
        [NotifyPropertyChangedFor(nameof(ShowMiniShutterInBottomBar))]
        [NotifyPropertyChangedFor(nameof(ShowFeedSideButtonsInBottomBar))]
        [NotifyPropertyChangedFor(nameof(ShowHistoryButton))]
        [NotifyPropertyChangedFor(nameof(ShowFeedOverlayBar))]
        [NotifyPropertyChangedFor(nameof(ShowFeedNoActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedReplyBar))]
        [NotifyPropertyChangedFor(nameof(ShowCameraFriendsPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedFilterPill))]
        [NotifyPropertyChangedFor(nameof(ShowEmptyFilteredFeedCard))]
        private bool _isGridGalleryOpen;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsBottomBarVisible))]
        [NotifyPropertyChangedFor(nameof(IsProfilePanelActive))]
        [NotifyPropertyChangedFor(nameof(IsNotProfilePanelActive))]
        [NotifyPropertyChangedFor(nameof(IsCameraPanelActive))]
        [NotifyPropertyChangedFor(nameof(IsChatPanelActive))]
        [NotifyPropertyChangedFor(nameof(IsNotChatPanelActive))]
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

        public bool IsBottomBarVisible =>
            !IsPreviewing && !IsReplyingToFeedPost && !IsEmojiPickerOpen;

        public bool IsProfilePanelActive => ActivePanelIndex == 0;
        public bool IsNotProfilePanelActive => ActivePanelIndex != 0;
        public bool IsCameraPanelActive => ActivePanelIndex == 1;
        public bool IsChatPanelActive => ActivePanelIndex == 2;
        public bool IsNotChatPanelActive => ActivePanelIndex != 2;
        public bool ShowHomePillInBottomBar => ActivePanelIndex == 1 && !IsViewingFeed && !IsGridGalleryOpen;
        public bool ShowHomeIconInBottomBar => ActivePanelIndex == 0 || ActivePanelIndex == 2;
        public bool ShowMiniShutterInBottomBar => ActivePanelIndex == 1 && (IsViewingFeed || IsGridGalleryOpen);
        public bool ShowFeedSideButtonsInBottomBar => ActivePanelIndex == 1 && IsViewingFeed && !IsGridGalleryOpen;

        public bool ShowHistoryButton => !IsPreviewing && !IsViewingFeed && !IsGridGalleryOpen;

        public bool ShowFeedOverlayBar => !IsPreviewing && !IsReplyingToFeedPost && !IsEmojiPickerOpen &&
                                          IsViewingFeed && !IsGridGalleryOpen && ActivePanelIndex == 1 &&
                                          CurrentFeedPost != null;

        public bool ShowFeedNoActivityPill => ShowFeedOverlayBar && CurrentFeedPost!.HasNoActivity;
        public bool ShowFeedActivityPill => ShowFeedOverlayBar && CurrentFeedPost!.ShowActivityPill;
        public bool ShowFeedReplyBar => ShowFeedOverlayBar && CurrentFeedPost!.IsIncoming;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsReactionSentPillVisible))]
        [NotifyPropertyChangedFor(nameof(IsDefaultActivityPillVisible))]
        private bool _isReactionAnimationPlaying;

        public bool IsReactionSentPillVisible =>
            IsReactionAnimationPlaying ||
            (CurrentFeedPost != null && CurrentFeedPost.PendingReactionAnimations.Count > 0);

        public bool IsDefaultActivityPillVisible => !IsReactionSentPillVisible;

        [ObservableProperty] private double _viewportHeight = 840;

        [ObservableProperty] private double _cardSize = 399;

        [ObservableProperty] private double _gridItemSize = 130;

        [ObservableProperty] private double _topHeaderHeight = 140;

        [ObservableProperty] private double _galleryTopSpacerHeight = 72;

        [ObservableProperty] private double _cameraBottomSpacerHeight = 118;

        [ObservableProperty] private double _feedBottomSpacerHeight = 136;

        [ObservableProperty] private double _chatBottomSpacerHeight = 90;

        [ObservableProperty] private double _shutterButtonSize = 92;

        [ObservableProperty] private double _shutterOuterRingSize = 90;

        [ObservableProperty] private double _shutterInnerCircleSize = 72;

        [ObservableProperty] private Thickness _topOverlayMargin = new(16, 14, 16, 0);

        [ObservableProperty] private Thickness _profileTopHeaderPadding = new(16, 14, 16, 0);

        [ObservableProperty] private Thickness _chatTopHeaderMargin = new(16, 14, 16, 12);

        [ObservableProperty] private Thickness _filterMenuMargin = new(0, 72, 0, 0);

        [ObservableProperty] private Thickness _postOptionsMenuMargin = new(18, 0, 18, 78);

        [ObservableProperty] private Thickness _sharedBottomBarMargin = new(18, 0, 18, 18);

        [ObservableProperty] private Thickness _historyOverlayMargin = new(18, 0, 18, 90);

        [ObservableProperty] private Thickness _previewBottomOverlayMargin = new(16, 0, 16, 22);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowFeedOverlayBar))]
        [NotifyPropertyChangedFor(nameof(ShowFeedNoActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedReplyBar))]
        [NotifyPropertyChangedFor(nameof(IsReactionSentPillVisible))]
        [NotifyPropertyChangedFor(nameof(IsDefaultActivityPillVisible))]
        [NotifyPropertyChangedFor(nameof(DeleteConfirmDescription))]
        private MediaPost? _currentFeedPost;

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsSwipeAllowed))]
        private bool _isDeleteConfirmOpen;

        public string DeleteConfirmDescription => CurrentFeedPost != null && CurrentFeedPost.IsIncoming
            ? "This will permanently delete this photo from your device only. Your friend will still be able to see it."
            : "This will permanently delete your photo from your history and your friends will no longer be able to see the photo.";

        [ObservableProperty] private string _firstRecentEmoji = "💗";

        [ObservableProperty] private string _secondRecentEmoji = "🙃";

        [ObservableProperty] private string _thirdRecentEmoji = "😋";

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsSwipeAllowed))]
        private bool _isActivitySheetOpen;

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsSwipeAllowed))]
        private bool _isNotificationsSheetOpen;

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsSwipeAllowed))]
        private bool _isConnectionSheetOpen;

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsSwipeAllowed))]
        private bool _isPostOptionsMenuOpen;

        /// <summary>Per-recipient delivery lines shown in the options menu of an outgoing post.</summary>
        public ObservableCollection<PostDeliveryRow> PostDeliveryRows { get; } = new();

        [ObservableProperty]
        private bool _showPostDeliveryStatus;

        /// <summary>Post the current <see cref="PostDeliveryRows"/> belong to (preloaded while scrolling).</summary>
        private Guid? _deliveryRowsPostId;

        private async Task LoadPostDeliveryRowsAsync(MediaPost? post)
        {
            if (post == null || post.IsIncoming)
            {
                PostDeliveryRows.Clear();
                ShowPostDeliveryStatus = false;
                _deliveryRowsPostId = post?.Id;
                return;
            }

            bool hasInternet = AppShellModel.HasNetworkAccess();
            var friends = (await _database.GetFriendsAsync())
                .GroupBy(f => f.Id)
                .ToDictionary(g => g.Key, g => g.First());
            string NameOf(string id) => friends.TryGetValue(id, out var f)
                ? (string.IsNullOrWhiteSpace(f.DisplayName) ? f.Username : f.DisplayName)
                : "Removed friend";
            string? AvatarOf(string? id) =>
                id != null && friends.TryGetValue(id, out var f) && !string.IsNullOrWhiteSpace(f.ProfilePictureUrl) &&
                File.Exists(f.ProfilePictureUrl)
                    ? f.ProfilePictureUrl
                    : null;

            var deliveries = await _database.GetPostDeliveriesAsync(post.Id);
            List<PostDeliveryRow> rows;
            if (deliveries.Count > 0)
            {
                rows = deliveries
                    .Select(d => PostDeliveryRow.Create(NameOf(d.RecipientId), AvatarOf(d.RecipientId), d.State, hasInternet))
                    .OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            else
            {
                // Sent before per-recipient tracking: only the overall status is known.
                var state = post.SyncStatus switch
                {
                    Shared.Enums.SyncStatus.Delivered => DeliveryState.Delivered,
                    Shared.Enums.SyncStatus.Sent => DeliveryState.OnServer,
                    _ => DeliveryState.Pending
                };
                string name = !string.IsNullOrEmpty(post.FriendId) ? NameOf(post.FriendId) : "Friends";
                string? avatar = !string.IsNullOrEmpty(post.FriendId) ? AvatarOf(post.FriendId) : null;
                rows = new List<PostDeliveryRow> { PostDeliveryRow.Create(name, avatar, state, hasInternet) };
            }

            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
            {
                if (CurrentFeedPost?.Id != post.Id) return;

                // Unchanged (e.g. a refresh on menu open): keep the rows so the open menu doesn't flicker.
                static (string, string, string) Key(PostDeliveryRow r) => (r.Name, r.AvatarUrl, r.StatusText);
                if (_deliveryRowsPostId == post.Id && PostDeliveryRows.Select(Key).SequenceEqual(rows.Select(Key)))
                    return;

                PostDeliveryRows.Clear();
                foreach (var row in rows) PostDeliveryRows.Add(row);
                ShowPostDeliveryStatus = rows.Count > 0;
                _deliveryRowsPostId = post.Id;
            });
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsBottomBarVisible))]
        [NotifyPropertyChangedFor(nameof(IsSwipeAllowed))]
        [NotifyPropertyChangedFor(nameof(ShowFeedOverlayBar))]
        [NotifyPropertyChangedFor(nameof(ShowFeedNoActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedReplyBar))]
        private bool _isEmojiPickerOpen;

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasEmojiSearchText))]
        private string _emojiSearchText = string.Empty;

        public bool HasEmojiSearchText => !string.IsNullOrWhiteSpace(EmojiSearchText);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsEmojiCat0Selected))]
        [NotifyPropertyChangedFor(nameof(IsEmojiCat1Selected))]
        [NotifyPropertyChangedFor(nameof(IsEmojiCat2Selected))]
        [NotifyPropertyChangedFor(nameof(IsEmojiCat3Selected))]
        [NotifyPropertyChangedFor(nameof(IsEmojiCat4Selected))]
        [NotifyPropertyChangedFor(nameof(IsEmojiCat5Selected))]
        [NotifyPropertyChangedFor(nameof(IsEmojiCat6Selected))]
        [NotifyPropertyChangedFor(nameof(IsEmojiCat7Selected))]
        [NotifyPropertyChangedFor(nameof(IsEmojiCat8Selected))]
        private int _selectedEmojiCategoryIndex;

        public bool IsEmojiCat0Selected => SelectedEmojiCategoryIndex == 0;
        public bool IsEmojiCat1Selected => SelectedEmojiCategoryIndex == 1;
        public bool IsEmojiCat2Selected => SelectedEmojiCategoryIndex == 2;
        public bool IsEmojiCat3Selected => SelectedEmojiCategoryIndex == 3;
        public bool IsEmojiCat4Selected => SelectedEmojiCategoryIndex == 4;
        public bool IsEmojiCat5Selected => SelectedEmojiCategoryIndex == 5;
        public bool IsEmojiCat6Selected => SelectedEmojiCategoryIndex == 6;
        public bool IsEmojiCat7Selected => SelectedEmojiCategoryIndex == 7;
        public bool IsEmojiCat8Selected => SelectedEmojiCategoryIndex == 8;

        public ObservableCollection<EmojiListRow> EmojiPickerRows { get; } = new();
        private System.Collections.Generic.Dictionary<int, int> _emojiCategoryHeaderIndices = new();
        private string _lastBuiltEmojiQuery = "\u0000";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsBottomBarVisible))]
        [NotifyPropertyChangedFor(nameof(IsTopOverlayBarVisible))]
        [NotifyPropertyChangedFor(nameof(IsSwipeAllowed))]
        [NotifyPropertyChangedFor(nameof(ShowFeedOverlayBar))]
        [NotifyPropertyChangedFor(nameof(ShowFeedNoActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedReplyBar))]
        private bool _isReplyingToFeedPost;

        [ObservableProperty] private string _replyingToFriendId = string.Empty;

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(FeedReplyPlaceholder))]
        private string _replyingToFriendName = string.Empty;

        [ObservableProperty] private string _replyingToFriendAvatarUrl = string.Empty;

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasFeedReplyText))]
        private string _feedReplyText = string.Empty;

        public string FeedReplyPlaceholder => string.IsNullOrWhiteSpace(ReplyingToFriendName)
            ? "Reply..."
            : $"Reply to {ReplyingToFriendName}...";

        public bool HasFeedReplyText => !string.IsNullOrWhiteSpace(FeedReplyText);

        public Action? OpenActivitySheetAction;
        public Action? CloseActivitySheetAction;
        public Action? OpenNotificationsSheetAction;
        public Action? CloseNotificationsSheetAction;
        public Action? OpenConnectionSheetAction;
        public Action? CloseConnectionSheetAction;
        public Action? OpenEmojiPickerAction;
        public Action? CloseEmojiPickerAction;
        public Action<int>? ScrollToEmojiCategoryAction;
        public Action? FocusEmojiSearchAction;
        public Action? OpenFeedReplyAction;
        public Action? CloseFeedReplyAction;
        public Action? OpenFilterMenuAction;
        public Action? CloseFilterMenuAction;
        public Action? OpenPostOptionsMenuAction;
        public Action? ClosePostOptionsMenuAction;
        public Action? OpenDeleteConfirmAction;
        public Action? CloseDeleteConfirmAction;
        public Action? ShowSavedToastAction;
        public Action? ShowSendFailedToastAction;
        public Action<Guid, System.Collections.Generic.IReadOnlyList<string>>? PlayReactionEmojiRainAction;
        public Action? StopReactionEmojiRainAction;

        private readonly System.Collections.Generic.HashSet<Guid> _shownReactionDmIds = new();
        private System.Threading.CancellationTokenSource? _reactionCheckCts;

        partial void OnEmojiSearchTextChanged(string value)
        {
            EnsureEmojiPickerRowsBuilt(value);
        }

        public void EnsureEmojiPickerRowsBuilt(string? query)
        {
            string normalized = query?.Trim() ?? string.Empty;
            if (_lastBuiltEmojiQuery == normalized && EmojiPickerRows.Count > 0)
                return;

            _lastBuiltEmojiQuery = normalized;
            var rows = EmojiCatalog.BuildRows(normalized, out var headerIndices);
            _emojiCategoryHeaderIndices = headerIndices;

            EmojiPickerRows.Clear();
            foreach (var row in rows)
            {
                EmojiPickerRows.Add(row);
            }
        }

        public int GetEmojiCategoryHeaderRowIndex(int categoryIndex)
        {
            return _emojiCategoryHeaderIndices.TryGetValue(categoryIndex, out int rowIndex) ? rowIndex : 0;
        }

        [RelayCommand]
        private void OpenEmojiPicker()
        {
            if (IsFilterMenuOpen)
            {
                CloseFilterMenu();
            }

            if (IsPostOptionsMenuOpen)
            {
                ClosePostOptionsMenu();
            }

            if (IsActivitySheetOpen)
            {
                CloseActivitySheet();
            }

            if (IsReplyingToFeedPost)
            {
                CloseFeedReply();
            }

            EmojiSearchText = string.Empty;
            EnsureEmojiPickerRowsBuilt(string.Empty);
            SelectedEmojiCategoryIndex = 0;
            IsEmojiPickerOpen = true;
            OpenEmojiPickerAction?.Invoke();
        }

        [RelayCommand]
        private void CloseEmojiPicker()
        {
            if (CloseEmojiPickerAction != null)
            {
                CloseEmojiPickerAction.Invoke();
            }
            else
            {
                IsEmojiPickerOpen = false;
            }
        }

        [RelayCommand]
        private void ClearEmojiSearch()
        {
            EmojiSearchText = string.Empty;
        }

        [RelayCommand]
        private void FocusEmojiSearch()
        {
            FocusEmojiSearchAction?.Invoke();
        }

        [RelayCommand]
        private void SelectEmojiCategory(string? categoryIndexParameter)
        {
            if (!int.TryParse(categoryIndexParameter, out int categoryIndex))
                return;

            if (!string.IsNullOrEmpty(EmojiSearchText))
            {
                EmojiSearchText = string.Empty;
            }

            SelectedEmojiCategoryIndex = categoryIndex;
            int targetRow = GetEmojiCategoryHeaderRowIndex(categoryIndex);
            ScrollToEmojiCategoryAction?.Invoke(targetRow);
        }

        [RelayCommand]
        private void SelectEmojiFromPicker(string? emoji)
        {
            if (string.IsNullOrWhiteSpace(emoji))
                return;

            SendRecentEmoji(emoji);
            CloseEmojiPicker();
        }

        [RelayCommand]
        private void ToggleFilterMenu()
        {
            if (IsFilterMenuOpen)
            {
                CloseFilterMenu();
            }
            else
            {
                if (IsActivitySheetOpen)
                {
                    CloseActivitySheet();
                }

                if (IsPostOptionsMenuOpen)
                {
                    ClosePostOptionsMenu();
                }

                if (IsEmojiPickerOpen)
                {
                    CloseEmojiPicker();
                }

                if (IsReplyingToFeedPost)
                {
                    CloseFeedReply();
                }

                IsFilterMenuOpen = true;
                OpenFilterMenuAction?.Invoke();
            }
        }

        [RelayCommand]
        private void TogglePostOptionsMenu()
        {
            if (CurrentFeedPost == null)
                return;

            if (IsPostOptionsMenuOpen)
            {
                ClosePostOptionsMenu();
            }
            else
            {
                if (IsFilterMenuOpen)
                {
                    CloseFilterMenu();
                }

                if (IsActivitySheetOpen)
                {
                    CloseActivitySheet();
                }

                if (IsEmojiPickerOpen)
                {
                    CloseEmojiPicker();
                }

                if (IsReplyingToFeedPost)
                {
                    CloseFeedReply();
                }

                // Rows are normally preloaded when the post scrolled into view; then this only refreshes
                // them (e.g. "Waiting for Internet" vs "Server"). Otherwise clear first so another
                // post's lines never flash while this one's load.
                if (_deliveryRowsPostId != CurrentFeedPost?.Id)
                {
                    PostDeliveryRows.Clear();
                    ShowPostDeliveryStatus = false;
                }
                _ = LoadPostDeliveryRowsAsync(CurrentFeedPost);

                IsPostOptionsMenuOpen = true;
                OpenPostOptionsMenuAction?.Invoke();
            }
        }

        [RelayCommand]
        private void ClosePostOptionsMenu()
        {
            if (ClosePostOptionsMenuAction != null)
            {
                ClosePostOptionsMenuAction.Invoke();
            }
            else
            {
                IsPostOptionsMenuOpen = false;
            }
        }

        [RelayCommand]
        private async Task DownloadCurrentFeedPostAsync()
        {
            var post = CurrentFeedPost;
            if (post == null || string.IsNullOrWhiteSpace(post.MediaLocalPath) ||
                !System.IO.File.Exists(post.MediaLocalPath))
                return;

            ClosePostOptionsMenu();

            bool saved = await SavePhotoToGalleryAsync(post.MediaLocalPath);
            if (saved)
            {
                ShowSavedToastAction?.Invoke();
            }
        }

        private static async Task<bool> SavePhotoToGalleryAsync(string filePath)
        {
#if ANDROID
            return await Task.Run(() =>
            {
                try
                {
                    var context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity
                                  ?? global::Android.App.Application.Context;
                    var resolver = context?.ContentResolver;
                    if (resolver == null)
                        return false;

                    string fileName = $"Pocket_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.jpg";
                    var values = new global::Android.Content.ContentValues();
                    values.Put(global::Android.Provider.MediaStore.IMediaColumns.DisplayName, fileName);
                    values.Put(global::Android.Provider.MediaStore.IMediaColumns.MimeType, "image/jpeg");

                    if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Q)
                    {
                        values.Put(
                            global::Android.Provider.MediaStore.IMediaColumns.RelativePath,
                            global::Android.OS.Environment.DirectoryPictures + "/Pocket");
                        values.Put(global::Android.Provider.MediaStore.IMediaColumns.IsPending, 1);
                    }

                    var collection = global::Android.Provider.MediaStore.Images.Media.ExternalContentUri;
                    if (collection == null)
                        return false;

                    var uri = resolver.Insert(collection, values);
                    if (uri == null)
                        return false;

                    using (var outStream = resolver.OpenOutputStream(uri))
                    using (var inStream = System.IO.File.OpenRead(filePath))
                    {
                        if (outStream == null)
                            return false;
                        inStream.CopyTo(outStream);
                    }

                    if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.Q)
                    {
                        var updateValues = new global::Android.Content.ContentValues();
                        updateValues.Put(global::Android.Provider.MediaStore.IMediaColumns.IsPending, 0);
                        resolver.Update(uri, updateValues, null, null);
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[GallerySave] Failed to save photo to gallery: {ex.Message}");
                    return false;
                }
            });
#else
            await Task.CompletedTask;
            return true;
#endif
        }

        [RelayCommand]
        private async Task SaveCapturedPreviewToGalleryAsync()
        {
            if (string.IsNullOrWhiteSpace(CapturedImagePath) || !System.IO.File.Exists(CapturedImagePath))
                return;

            bool saved = await SavePhotoToGalleryAsync(CapturedImagePath);
            if (saved)
            {
                ShowSavedToastAction?.Invoke();
            }
        }

        [RelayCommand]
        private void PromptDeleteCurrentFeedPost()
        {
            if (CurrentFeedPost == null)
                return;

            ClosePostOptionsMenu();
            OnPropertyChanged(nameof(DeleteConfirmDescription));
            IsDeleteConfirmOpen = true;
            OpenDeleteConfirmAction?.Invoke();
        }

        [RelayCommand]
        private void CloseDeleteConfirm()
        {
            if (CloseDeleteConfirmAction != null)
            {
                CloseDeleteConfirmAction.Invoke();
            }
            else
            {
                IsDeleteConfirmOpen = false;
            }
        }

        [RelayCommand]
        private async Task DeleteCurrentFeedPostAsync()
        {
            var post = CurrentFeedPost;
            if (post == null)
                return;

            CloseDeleteConfirm();
            ClosePostOptionsMenu();
            await ShellModel.DeleteMediaPostAsync(post);
        }

        [RelayCommand]
        private void CloseFilterMenu()
        {
            if (CloseFilterMenuAction != null)
            {
                CloseFilterMenuAction.Invoke();
            }
            else
            {
                IsFilterMenuOpen = false;
            }
        }

        [RelayCommand]
        private void SelectFilterAllFriends()
        {
            SelectedFilterMode = FeedFilterMode.AllFriends;
            SelectedFilterFriendId = string.Empty;
            SelectedFilterFriendName = string.Empty;
            CloseFilterMenu();
            ApplyFeedFilter(scrollToTop: true);
        }

        [RelayCommand]
        private void SelectFilterFriend(Friend? friend)
        {
            if (friend == null) return;

            SelectedFilterMode = FeedFilterMode.Friend;
            SelectedFilterFriendId = friend.Id;
            SelectedFilterFriendName = friend.FirstName;
            CloseFilterMenu();
            ApplyFeedFilter(scrollToTop: true);
        }

        [RelayCommand]
        private void SelectFilterYou()
        {
            SelectedFilterMode = FeedFilterMode.You;
            SelectedFilterFriendId = string.Empty;
            SelectedFilterFriendName = string.Empty;
            CloseFilterMenu();
            ApplyFeedFilter(scrollToTop: true);
        }

        private bool DoesPostMatchFilter(MediaPost post)
        {
            return SelectedFilterMode switch
            {
                FeedFilterMode.You => !post.IsIncoming,
                FeedFilterMode.Friend => post.IsIncoming && post.FriendId == SelectedFilterFriendId,
                _ => true
            };
        }

        /// <summary>
        /// Updates FeedPosts in place (remove / move / insert / replace) instead of Clear() + Add().
        /// Clear() raises a Reset, which makes the feed RecyclerView detach and recycle every row —
        /// including the header that hosts the camera TextureView — destroying the preview surface
        /// and causing a visible black gap in the camera on app launch.
        /// </summary>
        private void SyncFeedPosts(System.Collections.Generic.List<MediaPost> target)
        {
            var targetIds = target.Select(p => p.Id).ToHashSet();
            for (int i = FeedPosts.Count - 1; i >= 0; i--)
            {
                if (!targetIds.Contains(FeedPosts[i].Id))
                    FeedPosts.RemoveAt(i);
            }

            for (int i = 0; i < target.Count; i++)
            {
                var post = target[i];
                if (i < FeedPosts.Count && FeedPosts[i].Id == post.Id)
                {
                    if (!ReferenceEquals(FeedPosts[i], post))
                        FeedPosts[i] = post;
                    continue;
                }

                int existingIndex = -1;
                for (int j = i + 1; j < FeedPosts.Count; j++)
                {
                    if (FeedPosts[j].Id == post.Id) { existingIndex = j; break; }
                }

                if (existingIndex >= 0)
                {
                    FeedPosts.Move(existingIndex, i);
                    if (!ReferenceEquals(FeedPosts[i], post))
                        FeedPosts[i] = post;
                }
                else
                {
                    FeedPosts.Insert(i, post);
                }
            }
        }

        private void ApplyFeedFilter(bool scrollToTop)
        {
            var filtered = _allFeedPosts.Where(DoesPostMatchFilter).ToList();
            bool sameSequence = FeedPosts.Count == filtered.Count &&
                                FeedPosts.Select(p => p.Id).SequenceEqual(filtered.Select(p => p.Id));

            if (!sameSequence)
            {
                SyncFeedPosts(filtered);
            }

            if (FeedPosts.Count > 0)
            {
                if (scrollToTop || CurrentFeedPost == null || FeedPosts.All(p => p.Id != CurrentFeedPost.Id))
                {
                    CurrentFeedPost = FeedPosts[0];
                }

                if (scrollToTop && IsViewingFeed && !IsGridGalleryOpen)
                {
                    ScrollToFeedPostAction?.Invoke(FeedPosts[0]);
                }
            }
            else
            {
                CurrentFeedPost = null;
            }

            OnPropertyChanged(nameof(AllFeedPostsCount));
            OnPropertyChanged(nameof(ShowEmptyFilteredFeedCard));
            OnPropertyChanged(nameof(ShowFeedOverlayBar));
            OnPropertyChanged(nameof(ShowFeedNoActivityPill));
            OnPropertyChanged(nameof(ShowFeedActivityPill));
            OnPropertyChanged(nameof(ShowFeedReplyBar));
        }

        [RelayCommand]
        private void OpenActivitySheet()
        {
            if (CurrentFeedPost != null && CurrentFeedPost.HasActivity)
            {
                if (IsPostOptionsMenuOpen)
                {
                    ClosePostOptionsMenu();
                }

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

        [RelayCommand]
        private void OpenNotificationsSheet()
        {
            if (IsNotificationsSheetOpen)
                return;

            IsNotificationsSheetOpen = true;
            OpenNotificationsSheetAction?.Invoke();
        }

        [RelayCommand]
        private void CloseNotificationsSheet()
        {
            if (CloseNotificationsSheetAction != null)
            {
                CloseNotificationsSheetAction.Invoke();
            }
            else
            {
                IsNotificationsSheetOpen = false;
            }
        }

        [RelayCommand]
        private void OpenConnectionSheet()
        {
            if (IsConnectionSheetOpen)
                return;

            // Start from the saved addresses, discarding edits from an unsaved earlier visit.
            ShellModel.ReloadServerSettings();
            IsConnectionSheetOpen = true;
            OpenConnectionSheetAction?.Invoke();
        }

        [RelayCommand]
        private void CloseConnectionSheet()
        {
            if (CloseConnectionSheetAction != null)
            {
                CloseConnectionSheetAction.Invoke();
            }
            else
            {
                IsConnectionSheetOpen = false;
            }
        }

        [RelayCommand]
        private async Task SaveConnectionSettingsAsync()
        {
            CloseConnectionSheet();
            await ShellModel.SaveServersAsync();
        }

        [RelayCommand]
        private void CloseFeedReply()
        {
            if (CloseFeedReplyAction != null)
            {
                CloseFeedReplyAction.Invoke();
            }
            else
            {
                IsReplyingToFeedPost = false;
            }
        }

        [RelayCommand]
        private void SendFeedReply()
        {
            if (string.IsNullOrWhiteSpace(FeedReplyText) || CurrentFeedPost == null)
                return;

            var text = FeedReplyText.Trim();
            var targetPostId = CurrentFeedPost.Id;
            var targetFriendId = !string.IsNullOrEmpty(ReplyingToFriendId)
                ? ReplyingToFriendId
                : (!string.IsNullOrEmpty(CurrentFeedPost.FriendId)
                    ? CurrentFeedPost.FriendId
                    : ShellModel.Friends.FirstOrDefault()?.Id ?? string.Empty);

            FeedReplyText = string.Empty;
            CloseFeedReply();

            if (!string.IsNullOrEmpty(targetFriendId))
            {
                _ = ShellModel.SendDirectMessageAsync(targetFriendId, text, null, targetPostId);
            }
        }

        partial void OnCurrentFeedPostChanged(MediaPost? value)
        {
            // Preload the options menu's Delivery rows so they are ready when "..." is tapped.
            _ = LoadPostDeliveryRowsAsync(value);

            IsReactionAnimationPlaying = false;
            StopReactionEmojiRainAction?.Invoke();

            if (CloseActivitySheetAction != null)
            {
                CloseActivitySheetAction.Invoke();
            }
            else
            {
                IsActivitySheetOpen = false;
            }

            if (IsPostOptionsMenuOpen)
            {
                ClosePostOptionsMenu();
            }

            if (IsDeleteConfirmOpen)
            {
                CloseDeleteConfirm();
            }

            if (IsEmojiPickerOpen)
            {
                CloseEmojiPicker();
            }

            if (IsReplyingToFeedPost)
            {
                CloseFeedReply();
            }

            SchedulePendingReactionsCheck();

            if (_relayService.IsConnected &&
                _allFeedPosts.Any(p => !p.IsIncoming && p.SyncStatus == Shared.Enums.SyncStatus.Pending))
            {
                _ = ShellModel.FlushPendingOutgoingMediaPostsAsync();
            }
        }

        public void SchedulePendingReactionsCheck()
        {
            _reactionCheckCts?.Cancel();

            if (!IsViewingFeed || ActivePanelIndex != 1 || IsGridGalleryOpen || IsPreviewing || CurrentFeedPost == null)
                return;

            var post = CurrentFeedPost;
            if (post.PendingReactionAnimations.Count == 0)
                return;

            var cts = new System.Threading.CancellationTokenSource();
            _reactionCheckCts = cts;

            _ = TriggerPendingReactionsAfterSettleAsync(post, cts.Token);
        }

        private async Task TriggerPendingReactionsAfterSettleAsync(
            MediaPost post,
            System.Threading.CancellationToken token)
        {
            try
            {
                await Task.Delay(120, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested)
                return;

            if (!IsViewingFeed || ActivePanelIndex != 1 || IsGridGalleryOpen || IsPreviewing ||
                !ReferenceEquals(CurrentFeedPost, post))
                return;

            if (post.PendingReactionAnimations.Count == 0)
                return;

            var pending = post.PendingReactionAnimations.ToList();
            post.PendingReactionAnimations.Clear();

            var dmIds = new System.Collections.Generic.List<Guid>(pending.Count);
            var emojis = new System.Collections.Generic.List<string>(pending.Count);
            foreach (var (dmId, emoji) in pending)
            {
                post.ShownReactionDmIds.Add(dmId);
                _shownReactionDmIds.Add(dmId);
                dmIds.Add(dmId);
                if (!string.IsNullOrWhiteSpace(emoji))
                {
                    emojis.Add(emoji);
                }
            }

            _ = _database.MarkPostReactionsAnimationShownAsync(dmIds);

            if (emojis.Count > 0)
            {
                IsReactionAnimationPlaying = true;
                PlayReactionEmojiRainAction?.Invoke(post.Id, emojis);
            }
            else
            {
                OnPropertyChanged(nameof(IsReactionSentPillVisible));
                OnPropertyChanged(nameof(IsDefaultActivityPillVisible));
            }
        }

        private void SyncViewingHistoryState()
        {
            bool viewingHistory = ActivePanelIndex == 1 && (IsViewingFeed || IsGridGalleryOpen);
            ShellModel.IsViewingHistory = viewingHistory;
            if (viewingHistory)
            {
                foreach (var p in _allFeedPosts)
                {
                    p.IsUnseen = false;
                }

                _ = ShellModel.MarkAllPhotosSeenAsync();
            }
        }

        partial void OnIsGridGalleryOpenChanged(bool value)
        {
            SyncViewingHistoryState();
            if (value)
            {
                StopReactionEmojiRainAction?.Invoke();
            }
            else
            {
                SchedulePendingReactionsCheck();
            }
        }

        partial void OnActivePanelIndexChanged(int value)
        {
            SyncViewingHistoryState();
            if (value != 1)
            {
                StopReactionEmojiRainAction?.Invoke();
            }
            else
            {
                SchedulePendingReactionsCheck();
            }
        }

        partial void OnIsViewingFeedChanged(bool value)
        {
            SyncViewingHistoryState();
            OnPropertyChanged(nameof(ShowHomePillInBottomBar));
            OnPropertyChanged(nameof(ShowMiniShutterInBottomBar));
            OnPropertyChanged(nameof(ShowFeedSideButtonsInBottomBar));

            if (!value)
            {
                StopReactionEmojiRainAction?.Invoke();

                if (CloseActivitySheetAction != null)
                {
                    CloseActivitySheetAction.Invoke();
                }
                else
                {
                    IsActivitySheetOpen = false;
                }

                if (IsPostOptionsMenuOpen)
                {
                    ClosePostOptionsMenu();
                }

                if (IsEmojiPickerOpen)
                {
                    CloseEmojiPicker();
                }

                if (IsReplyingToFeedPost)
                {
                    CloseFeedReply();
                }

                if (IsFilterMenuOpen && !IsGridGalleryOpen)
                {
                    CloseFilterMenu();
                }
            }
            else
            {
                SchedulePendingReactionsCheck();
            }
        }

        partial void OnViewportHeightChanged(double value)
        {
            foreach (var post in _allFeedPosts)
            {
                post.ViewportHeight = value;
            }

            foreach (var post in FeedPosts)
            {
                post.ViewportHeight = value;
            }
        }

        partial void OnCardSizeChanged(double value)
        {
            foreach (var post in _allFeedPosts)
            {
                post.CardSize = value;
            }

            foreach (var post in FeedPosts)
            {
                post.CardSize = value;
            }
        }

        partial void OnGridItemSizeChanged(double value)
        {
            foreach (var post in _allFeedPosts)
            {
                post.GridItemSize = value;
            }

            foreach (var post in FeedPosts)
            {
                post.GridItemSize = value;
            }
        }

        partial void OnTopHeaderHeightChanged(double value)
        {
            foreach (var post in _allFeedPosts)
            {
                post.TopHeaderHeight = value;
            }

            foreach (var post in FeedPosts)
            {
                post.TopHeaderHeight = value;
            }
        }

        partial void OnFeedBottomSpacerHeightChanged(double value)
        {
            foreach (var post in _allFeedPosts)
            {
                post.FeedBottomSpacerHeight = value;
            }

            foreach (var post in FeedPosts)
            {
                post.FeedBottomSpacerHeight = value;
            }
        }

        /// <summary>
        /// Computes responsive spacing, card size, and shutter zone geometry based on the
        /// usable viewport dimensions and Android system bar / display cutout insets.
        /// Ensures the viewfinder card and shutter button sit at a consistent, balanced
        /// vertical alignment across devices with/without camera cutouts, 3-button vs gesture
        /// navigation, and different screen aspect ratios.
        /// </summary>
        public void UpdateResponsiveMetrics(double width, double height, double topSystemInsetDp,
            double bottomSystemInsetDp)
        {
            if (width <= 0 || height <= 0)
                return;

            // Standard Android baseline insets: ~28dp status bar (no cutout) and ~24dp gesture nav bar.
            // When a device has a tall display cutout (e.g. ~52dp on API 34 Pixel 7) or a 3-button nav bar (~37dp),
            // the OS already pushes the ContentPage inward by that extra amount. Absorbing the excess inset
            // into our internal top/bottom margins keeps the top bar, viewfinder card, shutter button, and
            // bottom bar at the same physical screen alignment across API levels and navigation modes.
            double excessTopInset = Math.Max(0.0, topSystemInsetDp - 28.0);
            double excessBottomInset = Math.Max(0.0, bottomSystemInsetDp - 24.0);
            double compactShortfall = Math.Max(0.0, 800.0 - height);

            double topBarTopMargin =
                Math.Round(Math.Clamp(14.0 - (excessTopInset * 0.55) - (compactShortfall * 0.05), 2.0, 14.0));
            double headerToCardGap =
                Math.Round(Math.Clamp(16.0 - Math.Max(0.0, excessTopInset - 12.0) - (compactShortfall * 0.05), 6.0,
                    16.0));
            double topBarBottom = topBarTopMargin + 42.0;
            double filterHeaderHeight = topBarBottom + headerToCardGap;

            double bottomBarBottomMargin =
                Math.Round(Math.Clamp(18.0 - (excessBottomInset * 0.8) - (compactShortfall * 0.05), 8.0, 18.0));
            double historyGapAboveBottomBar =
                Math.Round(Math.Clamp(16.0 - (excessBottomInset * 0.3) - (compactShortfall * 0.04), 10.0, 16.0));
            double historyBottomMargin = bottomBarBottomMargin + 56.0 + historyGapAboveBottomBar;

            // Reserve Row 3 in CameraHeaderGrid up to the exact top edge of the "History ⌄" button (~28dp tall)
            // so Row 2 (*) spans exclusively the open visual space between the Camera Card and "History ⌄",
            // centering the shutter button with equal space above and below.
            double cameraBottomSpacerHeight = historyBottomMargin + 28.0;
            double feedBottomSpacerHeight = historyBottomMargin + 46.0;
            double chatBottomSpacerHeight = bottomBarBottomMargin + 56.0 + 16.0;
            double previewBottomMargin =
                Math.Round(Math.Clamp(22.0 - (excessBottomInset * 0.7) - (compactShortfall * 0.05), 10.0, 22.0));

            double shutterButtonSize = height >= 700.0
                ? 92.0
                : Math.Clamp(Math.Round(92.0 - ((700.0 - height) * 0.12)), 72.0, 92.0);
            double shutterOuterRingSize = shutterButtonSize - 2.0;
            double shutterInnerCircleSize = Math.Round(shutterButtonSize * (72.0 / 92.0));

            double minShutterZoneHeight = shutterButtonSize + Math.Clamp((height - 620.0) * 0.22, 24.0, 64.0);
            double maxCardHeight = height - filterHeaderHeight - 4.0 - cameraBottomSpacerHeight - minShutterZoneHeight;
            double maxCardWidth = Math.Max(200.0, width - 12.0);
            double cardSize = Math.Clamp(Math.Min(maxCardWidth, maxCardHeight), 200.0, maxCardWidth);

            // Vertically center/balance the camera and feed photo cards in the open space between the
            // top bar and bottom controls (matching Locket's layout) while preserving minShutterZoneHeight.
            double availableBodyHeight = height - topBarBottom - 4.0 - cameraBottomSpacerHeight;
            double freeVerticalSpace = Math.Max(0.0, availableBodyHeight - cardSize);
            double maxCardTopGap = Math.Max(headerToCardGap, freeVerticalSpace - minShutterZoneHeight);
            double cardTopGap =
                Math.Round(Math.Clamp(freeVerticalSpace * 0.33, headerToCardGap, Math.Min(96.0, maxCardTopGap)));
            double topHeaderHeight = topBarBottom + cardTopGap;

            ViewportHeight = height;
            TopHeaderHeight = topHeaderHeight;
            GalleryTopSpacerHeight = filterHeaderHeight;
            CameraBottomSpacerHeight = cameraBottomSpacerHeight;
            FeedBottomSpacerHeight = feedBottomSpacerHeight;
            ChatBottomSpacerHeight = chatBottomSpacerHeight;
            ShutterButtonSize = shutterButtonSize;
            ShutterOuterRingSize = shutterOuterRingSize;
            ShutterInnerCircleSize = shutterInnerCircleSize;
            CardSize = cardSize;
            GridItemSize = Math.Floor((width - 6.0) / 3.0);

            TopOverlayMargin = new Thickness(16, topBarTopMargin, 16, 0);
            ProfileTopHeaderPadding = new Thickness(16, topBarTopMargin, 16, 0);
            ChatTopHeaderMargin = new Thickness(16, topBarTopMargin, 16, 12);
            FilterMenuMargin = new Thickness(0, filterHeaderHeight, 0, 0);
            PostOptionsMenuMargin = new Thickness(18, 0, 18, bottomBarBottomMargin + 60.0);
            SharedBottomBarMargin = new Thickness(18, 0, 18, bottomBarBottomMargin);
            HistoryOverlayMargin = new Thickness(18, 0, 18, historyBottomMargin);
            PreviewBottomOverlayMargin = new Thickness(16, 0, 16, previewBottomMargin);
        }

        public AppShellModel ShellModel { get; }

        public Action? SnapToProfileAction;
        public Action? SnapToCameraAction;
        public Action? SnapToChatAction;
        public Action? ScrollToHistoryAction;
        public Action? ScrollToCameraAction;
        public Action<MediaPost>? ScrollToFeedPostAction;
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
            ShellModel.OnMediaPostSent += HandleMediaPostSent;
            ShellModel.OnMediaPostDelivered += HandleMediaPostDelivered;
            ShellModel.OnMediaPostDeleted += HandleMediaPostDeleted;
            ShellModel.OnPostDeliveryChanged += postId =>
            {
                // Keep the preloaded rows of the visible post current, menu open or not.
                if (CurrentFeedPost?.Id == postId)
                    _ = LoadPostDeliveryRowsAsync(CurrentFeedPost);
            };
            ShellModel.OnConnectionStateChangedEvent += HandleConnectionStateChanged;
            ShellModel.OnDirectMessageReceived += HandleDirectMessageReceived;
            ShellModel.OnDirectMessageSendFailed += HandleDirectMessageSendFailed;
            ShellModel.OnFriendProfileUpdated += HandleFriendProfileUpdated;
            ShellModel.OnUserProfileUpdated += HandleUserProfileUpdated;
            ShellModel.OnDataImported += HandleDataImported;

            // Slow-upload hourglass is time-based: tick while any own post is still waiting for the server.
            _sendStateTicker = new Services.SendStateTicker(() => _allFeedPosts.Concat(FeedPosts));
            FeedPosts.CollectionChanged += (_, _) => _sendStateTicker.Ensure();

            LoadRecentEmojis();
        }

        private readonly Services.SendStateTicker _sendStateTicker;

        private async void HandleDataImported()
        {
            _shownReactionDmIds.Clear();
            _allFeedPosts.Clear();
            LoadRecentEmojis();
            SelectedFilterMode = FeedFilterMode.AllFriends;
            SelectedFilterFriendId = string.Empty;
            SelectedFilterFriendName = string.Empty;
            await LoadFeedAsync();
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

            var list = new System.Collections.Generic.List<string>
                { FirstRecentEmoji, SecondRecentEmoji, ThirdRecentEmoji };
            list.Remove(emoji);
            list.Insert(0, emoji); // Most recently used emoji goes first (leftmost)

            FirstRecentEmoji = list.Count > 0 ? list[0] : "💗";
            SecondRecentEmoji = list.Count > 1 ? list[1] : "🙃";
            ThirdRecentEmoji = list.Count > 2 ? list[2] : "😋";

            Microsoft.Maui.Storage.Preferences.Default.Set("RecentEmojis",
                $"{FirstRecentEmoji},{SecondRecentEmoji},{ThirdRecentEmoji}");
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

        private void HandleDirectMessageSendFailed(DirectMessage dm)
        {
            if (!ShellModel.IsInDmWindow)
            {
                ShowSendFailedToastAction?.Invoke();
            }
        }

        private void HandleDirectMessageReceived(DirectMessage dm)
        {
            if (dm.IsIncoming && dm.MediaPostId.HasValue && !string.IsNullOrWhiteSpace(dm.ReactionEmoji))
            {
                var targetPost = _allFeedPosts.FirstOrDefault(p => p.Id == dm.MediaPostId.Value)
                                 ?? FeedPosts.FirstOrDefault(p => p.Id == dm.MediaPostId.Value);
                if (targetPost != null)
                {
                    var friend = ShellModel.Friends.FirstOrDefault(f => f.Id == dm.FriendId);
                    var friendName = friend?.DisplayName ?? "Friend";
                    var friendAvatar = friend?.ProfilePictureUrl ?? string.Empty;
                    targetPost.AddOrUpdateReaction(dm.FriendId, friendName, friendAvatar, dm.ReactionEmoji);
                    if (!dm.IsReactionAnimationShown && !_shownReactionDmIds.Contains(dm.Id))
                    {
                        targetPost.EnqueuePendingReaction(dm.Id, dm.ReactionEmoji);
                    }

                    if (ReferenceEquals(CurrentFeedPost, targetPost))
                    {
                        OnPropertyChanged(nameof(ShowFeedNoActivityPill));
                        OnPropertyChanged(nameof(ShowFeedActivityPill));
                        OnPropertyChanged(nameof(IsReactionSentPillVisible));
                        OnPropertyChanged(nameof(IsDefaultActivityPillVisible));
                        SchedulePendingReactionsCheck();
                    }
                }
            }
        }

        private void HandleMediaPostReceived(MediaPost post)
        {
            post.ViewportHeight = ViewportHeight;
            post.CardSize = CardSize;
            post.GridItemSize = GridItemSize;
            post.TopHeaderHeight = TopHeaderHeight;
            post.FeedBottomSpacerHeight = FeedBottomSpacerHeight;
            post.OnReplyOrReactRequested = HandlePostReplyOrReact;
            post.OnGridPostTapped = HandleGridPostTapped;

            if (_allFeedPosts.All(p => p.Id != post.Id))
            {
                _allFeedPosts.Insert(0, post);
                OnPropertyChanged(nameof(AllFeedPostsCount));
            }

            if (DoesPostMatchFilter(post) && FeedPosts.All(p => p.Id != post.Id))
            {
                FeedPosts.Insert(0, post);
                CurrentFeedPost ??= post;
                OnPropertyChanged(nameof(ShowEmptyFilteredFeedCard));
            }
        }

        private void HandleGridPostTapped(MediaPost post)
        {
            if (IsFilterMenuOpen)
            {
                CloseFilterMenu();
            }

            CurrentFeedPost = post;
            IsGridGalleryOpen = false;
            ScrollToFeedPostAction?.Invoke(post);
        }

        private void HandleMediaPostSent(Guid messageId)
        {
            var existingInAll = _allFeedPosts.FirstOrDefault(p => p.Id == messageId);
            if (existingInAll != null)
            {
                existingInAll.IsWaitingForServer = false;
                if (existingInAll.SyncStatus == Shared.Enums.SyncStatus.Pending)
                {
                    existingInAll.SyncStatus = Shared.Enums.SyncStatus.Sent;
                }
            }

            var existing = FeedPosts.FirstOrDefault(p => p.Id == messageId);
            if (existing != null)
            {
                existing.IsWaitingForServer = false;
                if (existing.SyncStatus == Shared.Enums.SyncStatus.Pending)
                {
                    existing.SyncStatus = Shared.Enums.SyncStatus.Sent;
                }
            }
        }

        private void HandleConnectionStateChanged(bool isConnected)
        {
            foreach (var post in _allFeedPosts)
            {
                if (!post.IsIncoming && post.SyncStatus == Shared.Enums.SyncStatus.Pending)
                {
                    post.IsWaitingForServer = !isConnected;
                }
            }

            foreach (var post in FeedPosts)
            {
                if (!post.IsIncoming && post.SyncStatus == Shared.Enums.SyncStatus.Pending)
                {
                    post.IsWaitingForServer = !isConnected;
                }
            }
        }

        private void HandleMediaPostDelivered(Guid messageId)
        {
            var existingInAll = _allFeedPosts.FirstOrDefault(p => p.Id == messageId);
            if (existingInAll != null)
            {
                existingInAll.IsWaitingForServer = false;
                existingInAll.SyncStatus = Shared.Enums.SyncStatus.Delivered;
            }

            var existing = FeedPosts.FirstOrDefault(p => p.Id == messageId);
            if (existing != null)
            {
                existing.IsWaitingForServer = false;
                existing.SyncStatus = Shared.Enums.SyncStatus.Delivered;
            }
        }

        private void HandleMediaPostDeleted(Guid postId)
        {
            var existingInAll = _allFeedPosts.FirstOrDefault(p => p.Id == postId);
            if (existingInAll != null)
            {
                _allFeedPosts.Remove(existingInAll);
                OnPropertyChanged(nameof(AllFeedPostsCount));
            }

            int indexInFeed = -1;
            for (int i = 0; i < FeedPosts.Count; i++)
            {
                if (FeedPosts[i].Id == postId)
                {
                    indexInFeed = i;
                    break;
                }
            }

            if (indexInFeed >= 0)
            {
                bool wasCurrent = CurrentFeedPost?.Id == postId;
                FeedPosts.RemoveAt(indexInFeed);

                if (FeedPosts.Count > 0)
                {
                    if (wasCurrent || CurrentFeedPost == null)
                    {
                        int nextIndex = Math.Clamp(indexInFeed, 0, FeedPosts.Count - 1);
                        CurrentFeedPost = FeedPosts[nextIndex];
                        if (IsViewingFeed && !IsGridGalleryOpen)
                        {
                            ScrollToFeedPostAction?.Invoke(CurrentFeedPost);
                        }
                    }
                }
                else
                {
                    CurrentFeedPost = null;
                    if (_allFeedPosts.Count == 0)
                    {
                        IsGridGalleryOpen = false;
                        IsViewingFeed = false;
                        ScrollToCameraAction?.Invoke();
                    }
                }
            }

            OnPropertyChanged(nameof(ShowEmptyFilteredFeedCard));
            OnPropertyChanged(nameof(ShowFeedOverlayBar));
            OnPropertyChanged(nameof(ShowFeedNoActivityPill));
            OnPropertyChanged(nameof(ShowFeedActivityPill));
            OnPropertyChanged(nameof(ShowFeedReplyBar));
        }

        private void HandleFriendProfileUpdated(Friend friend)
        {
            if (ReplyingToFriendId == friend.Id)
            {
                ReplyingToFriendName = friend.DisplayName;
                ReplyingToFriendAvatarUrl = friend.ProfilePictureUrl;
            }

            if (SelectedFilterMode == FeedFilterMode.Friend && SelectedFilterFriendId == friend.Id)
            {
                SelectedFilterFriendName = friend.FirstName;
            }

            foreach (var post in _allFeedPosts)
            {
                if (post.IsIncoming && post.FriendId == friend.Id)
                {
                    post.SenderDisplayName = friend.DisplayName;
                }

                bool activityUpdated = false;
                foreach (var item in post.ActivityItems)
                {
                    if (item.FriendId == friend.Id)
                    {
                        item.FriendName = friend.DisplayName;
                        item.FriendAvatarUrl = friend.ProfilePictureUrl;
                        activityUpdated = true;
                    }
                }

                if (activityUpdated)
                {
                    post.NotifyActivityChanged();
                }
            }
        }

        private void HandleUserProfileUpdated(User user)
        {
            foreach (var post in _allFeedPosts)
            {
                if (!post.IsIncoming)
                {
                    post.SenderDisplayName = user.DisplayName;
                }
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
                        "No Friends Yet",
                        "Add a friend first to send replies or reactions.",
                        "OK");
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

            var friend = ShellModel.Friends.FirstOrDefault(f => f.Id == targetFriendId);
            ReplyingToFriendId = targetFriendId;
            ReplyingToFriendName = !string.IsNullOrWhiteSpace(friend?.DisplayName)
                ? friend.DisplayName
                : post.SenderDisplayName;
            ReplyingToFriendAvatarUrl = friend?.ProfilePictureUrl ?? string.Empty;
            FeedReplyText = string.Empty;
            IsReplyingToFeedPost = true;
            OpenFeedReplyAction?.Invoke();
        }

        public async Task LoadFeedAsync()
        {
            var friends = await _database.GetFriendsAsync();
            var friendMap = friends.ToDictionary(f => f.Id, f => f);

            var posts = await _database.GetAllMediaPostsAsync();
            var reactionsByPost = (await _database.GetAllPostLinkedDirectMessagesAsync())
                .Where(d => d.IsIncoming && !string.IsNullOrWhiteSpace(d.ReactionEmoji))
                .GroupBy(d => d.MediaPostId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());
            var preparedPosts = new System.Collections.Generic.List<MediaPost>(posts.Count);
            foreach (var post in posts)
            {
                if (post.IsIncoming && !string.IsNullOrEmpty(post.FriendId) &&
                    friendMap.TryGetValue(post.FriendId, out var senderFriend))
                {
                    post.SenderDisplayName = senderFriend.DisplayName;
                }
                else if (!post.IsIncoming)
                {
                    post.SenderDisplayName = ShellModel.CurrentUser.DisplayName;
                    if (post.SyncStatus == Shared.Enums.SyncStatus.Pending)
                    {
                        post.IsWaitingForServer = !_relayService.IsConnected;
                    }
                }

                if (!reactionsByPost.TryGetValue(post.Id, out var postDms))
                    postDms = new System.Collections.Generic.List<DirectMessage>();
                foreach (var dm in postDms)
                {
                    friendMap.TryGetValue(dm.FriendId, out var reactFriend);
                    var rName = reactFriend?.DisplayName ?? "Friend";
                    var rAvatar = reactFriend?.ProfilePictureUrl ?? string.Empty;
                    post.AddOrUpdateReaction(dm.FriendId, rName, rAvatar, dm.ReactionEmoji!);
                    if (dm.IsReactionAnimationShown || _shownReactionDmIds.Contains(dm.Id))
                    {
                        post.ShownReactionDmIds.Add(dm.Id);
                        _shownReactionDmIds.Add(dm.Id);
                    }
                    else
                    {
                        post.EnqueuePendingReaction(dm.Id, dm.ReactionEmoji!);
                    }
                }

                post.ViewportHeight = ViewportHeight;
                post.CardSize = CardSize;
                post.GridItemSize = GridItemSize;
                post.TopHeaderHeight = TopHeaderHeight;
                post.FeedBottomSpacerHeight = FeedBottomSpacerHeight;
                post.OnReplyOrReactRequested = HandlePostReplyOrReact;
                post.OnGridPostTapped = HandleGridPostTapped;
                preparedPosts.Add(post);
            }

            // Update master list _allFeedPosts in-place if IDs match, or replace
            bool sameAllSequence = _allFeedPosts.Count == preparedPosts.Count &&
                                   _allFeedPosts.Select(p => p.Id).SequenceEqual(preparedPosts.Select(p => p.Id));

            if (sameAllSequence)
            {
                for (int i = 0; i < preparedPosts.Count; i++)
                {
                    var existing = _allFeedPosts[i];
                    var updated = preparedPosts[i];
                    existing.SenderDisplayName = updated.SenderDisplayName;
                    existing.SyncStatus = updated.SyncStatus;
                    existing.IsWaitingForServer = updated.IsWaitingForServer;
                    existing.ViewportHeight = ViewportHeight;
                    existing.CardSize = CardSize;
                    existing.GridItemSize = GridItemSize;
                    existing.TopHeaderHeight = TopHeaderHeight;
                    existing.FeedBottomSpacerHeight = FeedBottomSpacerHeight;
                    existing.OnReplyOrReactRequested = HandlePostReplyOrReact;
                    existing.OnGridPostTapped = HandleGridPostTapped;
                    existing.ActivityItems.Clear();
                    foreach (var item in updated.ActivityItems)
                    {
                        existing.ActivityItems.Add(item);
                    }

                    foreach (var shownId in updated.ShownReactionDmIds)
                    {
                        existing.ShownReactionDmIds.Add(shownId);
                    }

                    existing.PendingReactionAnimations.Clear();
                    foreach (var (dmId, emoji) in updated.PendingReactionAnimations)
                    {
                        existing.EnqueuePendingReaction(dmId, emoji);
                    }

                    existing.NotifyActivityChanged();
                }
            }
            else
            {
                _allFeedPosts.Clear();
                foreach (var post in preparedPosts)
                {
                    _allFeedPosts.Add(post);
                }
            }

            if (SelectedFilterMode == FeedFilterMode.Friend && !string.IsNullOrEmpty(SelectedFilterFriendId))
            {
                if (friendMap.TryGetValue(SelectedFilterFriendId, out var filterFriend))
                {
                    SelectedFilterFriendName = filterFriend.FirstName;
                }
            }

            ApplyFeedFilter(scrollToTop: false);

            if (_allFeedPosts.Count == 0)
            {
                CurrentFeedPost = null;
                IsViewingFeed = false;
            }

            OnPropertyChanged(nameof(AllFeedPostsCount));
            OnPropertyChanged(nameof(ShowEmptyFilteredFeedCard));
            OnPropertyChanged(nameof(ShowFeedOverlayBar));
            OnPropertyChanged(nameof(ShowFeedNoActivityPill));
            OnPropertyChanged(nameof(ShowFeedActivityPill));
            OnPropertyChanged(nameof(ShowFeedReplyBar));

            SchedulePendingReactionsCheck();
            _sendStateTicker.Ensure();

            if (_relayService.IsConnected &&
                _allFeedPosts.Any(p => !p.IsIncoming && p.SyncStatus == Shared.Enums.SyncStatus.Pending))
            {
                _ = ShellModel.FlushPendingOutgoingMediaPostsAsync();
            }
        }

        public void OnPhotoCaptured(string imagePath, bool isFromGallery = false)
        {
            if (IsFilterMenuOpen)
            {
                CloseFilterMenu();
            }

            IsViewingFeed = false;
            IsGridGalleryOpen = false;
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
        private void OpenGridGallery()
        {
            if (IsActivitySheetOpen)
            {
                CloseActivitySheet();
            }

            if (IsPostOptionsMenuOpen)
            {
                ClosePostOptionsMenu();
            }

            if (IsDeleteConfirmOpen)
            {
                CloseDeleteConfirm();
            }

            if (IsEmojiPickerOpen)
            {
                CloseEmojiPicker();
            }

            if (IsReplyingToFeedPost)
            {
                CloseFeedReply();
            }

            if (IsFilterMenuOpen)
            {
                CloseFilterMenu();
            }

            IsGridGalleryOpen = true;
        }

        [RelayCommand]
        private void CloseGridGallery()
        {
            if (IsFilterMenuOpen)
            {
                CloseFilterMenu();
            }

            IsGridGalleryOpen = false;
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
            if (IsFilterMenuOpen)
            {
                CloseFilterMenu();
            }

            SnapToProfileAction?.Invoke();
        }

        [RelayCommand]
        private void SnapToCamera()
        {
            if (IsFilterMenuOpen)
            {
                CloseFilterMenu();
            }

            SnapToCameraAction?.Invoke();
        }

        [RelayCommand]
        private void SnapToChat()
        {
            if (IsFilterMenuOpen)
            {
                CloseFilterMenu();
            }

            SnapToChatAction?.Invoke();
        }

        [RelayCommand]
        private void ScrollToHistory()
        {
            if (FeedPosts.Count == 0 && _allFeedPosts.Count > 0)
            {
                SelectedFilterMode = FeedFilterMode.AllFriends;
                SelectedFilterFriendId = string.Empty;
                SelectedFilterFriendName = string.Empty;
                ApplyFeedFilter(scrollToTop: false);
            }

            ScrollToHistoryAction?.Invoke();
        }

        [RelayCommand]
        private void OnCenterBottomTabTapped()
        {
            if (IsFilterMenuOpen)
            {
                CloseFilterMenu();
            }

            if (ActivePanelIndex != 1)
            {
                SnapToCameraAction?.Invoke();
            }
            else if (ActivePanelIndex == 1 && (IsViewingFeed || IsGridGalleryOpen))
            {
                IsGridGalleryOpen = false;
                if (FeedPosts.Count == 0 && _allFeedPosts.Count > 0)
                {
                    SelectedFilterMode = FeedFilterMode.AllFriends;
                    SelectedFilterFriendId = string.Empty;
                    SelectedFilterFriendName = string.Empty;
                    ApplyFeedFilter(scrollToTop: false);
                }

                ScrollToCameraAction?.Invoke();
            }
        }

        /// <summary>
        /// Plays the "sent" exit transition (send button → check, photo flies up, preview UI fades to black).
        /// Awaited before the preview state is reset; the page plays the camera fade-in afterwards.
        /// </summary>
        public Func<Task>? PlaySendOutTransitionAsync;

        private bool _isSending;

        [RelayCommand]
        private void Cancel()
        {
            if (_isSending) return;

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
            if (_isSending) return;
            if (string.IsNullOrEmpty(CapturedImagePath) || !System.IO.File.Exists(CapturedImagePath)) return;
            _isSending = true;
            try
            {
                await SendCoreAsync();
            }
            finally
            {
                _isSending = false;
            }
        }

        private async Task SendCoreAsync()
        {
            // The exit animation runs while the photo is persisted below; state is reset once both finish.
            Task transition = Task.CompletedTask;
            try
            {
                transition = PlaySendOutTransitionAsync?.Invoke() ?? Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Send transition failed to start: {ex.Message}");
            }

            // Migrate temporary cached photo to persistent AppDataDirectory storage
            var mediaDir = System.IO.Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "Media");
            System.IO.Directory.CreateDirectory(mediaDir);

            var persistentPath = System.IO.Path.Combine(mediaDir, $"post_{Guid.NewGuid()}.jpg");
            var tempPath = CapturedImagePath;
            await Task.Run(() => System.IO.File.Copy(tempPath, persistentPath, true));

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
                IsWaitingForServer = false,
                SenderDisplayName = ShellModel.CurrentUser.Username,
                ViewportHeight = ViewportHeight,
                CardSize = CardSize,
                GridItemSize = GridItemSize,
                TopHeaderHeight = TopHeaderHeight,
                FeedBottomSpacerHeight = FeedBottomSpacerHeight,
                OnReplyOrReactRequested = HandlePostReplyOrReact,
                OnGridPostTapped = HandleGridPostTapped
            };

            // Recipients first: once the post is in the DB a reconnect may resend it, and without rows it
            // would fall back to "all friends".
            await ShellModel.RegisterPostRecipientsAsync(mediaPost, targetFriends);
            await _database.SaveMediaPostAsync(mediaPost);

            _allFeedPosts.Insert(0, mediaPost);
            _sendStateTicker.Ensure();
            OnPropertyChanged(nameof(AllFeedPostsCount));
            if (DoesPostMatchFilter(mediaPost))
            {
                FeedPosts.Insert(0, mediaPost);
                CurrentFeedPost = mediaPost;
            }

            OnPropertyChanged(nameof(ShowEmptyFilteredFeedCard));

            try
            {
                await transition;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Send transition failed: {ex.Message}");
            }

            IsViewingFeed = false;
            IsEditingCaption = false;
            IsPreviewing = false;
            CapturedImagePath = string.Empty;
            Caption = string.Empty;
            PreviewStateChangedAction?.Invoke(false);

            // E2EE Encryption & Transmission to Mutual Friends (BR-601, BR-201)
            // Brief initial state so "Uploading..." is visible while attempting connection
            if (!_relayService.IsConnected)
            {
                await Task.Delay(600);
            }

            bool uploaded = await ShellModel.TryUploadMediaPostAsync(mediaPost, targetFriends);
            if (uploaded)
            {
                HandleMediaPostSent(mediaPost.Id);
            }
        }
    }
}

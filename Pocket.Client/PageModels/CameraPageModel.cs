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
                                      !IsFilterMenuOpen && !IsEmojiPickerOpen;

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

        public bool IsBottomBarVisible =>
            ActivePanelIndex != 0 && !IsPreviewing && !IsReplyingToFeedPost && !IsEmojiPickerOpen;

        public bool IsCameraPanelActive => ActivePanelIndex == 1;
        public bool IsChatPanelActive => ActivePanelIndex == 2;
        public bool ShowHomePillInBottomBar => ActivePanelIndex == 1 && !IsViewingFeed && !IsGridGalleryOpen;
        public bool ShowHomeIconInBottomBar => ActivePanelIndex == 2;
        public bool ShowMiniShutterInBottomBar => ActivePanelIndex == 1 && (IsViewingFeed || IsGridGalleryOpen);
        public bool ShowFeedSideButtonsInBottomBar => ActivePanelIndex == 1 && IsViewingFeed && !IsGridGalleryOpen;

        public bool ShowHistoryButton => !IsPreviewing && !IsViewingFeed && !IsGridGalleryOpen;

        public bool ShowFeedOverlayBar => !IsPreviewing && !IsReplyingToFeedPost && !IsEmojiPickerOpen &&
                                          IsViewingFeed && !IsGridGalleryOpen && ActivePanelIndex == 1 &&
                                          CurrentFeedPost != null;

        public bool ShowFeedNoActivityPill => ShowFeedOverlayBar && CurrentFeedPost!.HasNoActivity;
        public bool ShowFeedActivityPill => ShowFeedOverlayBar && CurrentFeedPost!.ShowActivityPill;
        public bool ShowFeedReplyBar => ShowFeedOverlayBar && CurrentFeedPost!.IsIncoming;

        [ObservableProperty] private double _viewportHeight = 840;

        [ObservableProperty] private double _cardSize = 399;

        [ObservableProperty] private double _gridItemSize = 130;

        [ObservableProperty] private double _topHeaderHeight = 72;

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

        [ObservableProperty] private Thickness _sharedBottomBarMargin = new(18, 0, 18, 18);

        [ObservableProperty] private Thickness _historyOverlayMargin = new(18, 0, 18, 90);

        [ObservableProperty] private Thickness _previewBottomOverlayMargin = new(16, 0, 16, 22);

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowFeedOverlayBar))]
        [NotifyPropertyChangedFor(nameof(ShowFeedNoActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedActivityPill))]
        [NotifyPropertyChangedFor(nameof(ShowFeedReplyBar))]
        private MediaPost? _currentFeedPost;

        [ObservableProperty] private string _firstRecentEmoji = "💗";

        [ObservableProperty] private string _secondRecentEmoji = "🙃";

        [ObservableProperty] private string _thirdRecentEmoji = "😋";

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsSwipeAllowed))]
        private bool _isActivitySheetOpen;

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
        public Action? OpenEmojiPickerAction;
        public Action? CloseEmojiPickerAction;
        public Action<int>? ScrollToEmojiCategoryAction;
        public Action? FocusEmojiSearchAction;
        public Action? OpenFeedReplyAction;
        public Action? CloseFeedReplyAction;
        public Action? OpenFilterMenuAction;
        public Action? CloseFilterMenuAction;

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

        private void ApplyFeedFilter(bool scrollToTop)
        {
            var filtered = _allFeedPosts.Where(DoesPostMatchFilter).ToList();
            bool sameSequence = FeedPosts.Count == filtered.Count &&
                                FeedPosts.Select(p => p.Id).SequenceEqual(filtered.Select(p => p.Id));

            if (!sameSequence)
            {
                FeedPosts.Clear();
                foreach (var post in filtered)
                {
                    FeedPosts.Add(post);
                }
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
            if (CloseActivitySheetAction != null)
            {
                CloseActivitySheetAction.Invoke();
            }
            else
            {
                IsActivitySheetOpen = false;
            }

            if (IsEmojiPickerOpen)
            {
                CloseEmojiPicker();
            }

            if (IsReplyingToFeedPost)
            {
                CloseFeedReply();
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
            double topHeaderHeight = topBarTopMargin + 42.0 + headerToCardGap;

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
            double maxCardHeight = height - topHeaderHeight - 4.0 - cameraBottomSpacerHeight - minShutterZoneHeight;
            double maxCardWidth = Math.Max(200.0, width - 12.0);
            double cardSize = Math.Clamp(Math.Min(maxCardWidth, maxCardHeight), 200.0, maxCardWidth);

            ViewportHeight = height;
            TopHeaderHeight = topHeaderHeight;
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
            FilterMenuMargin = new Thickness(0, topHeaderHeight, 0, 0);
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
            ShellModel.OnMediaPostDelivered += HandleMediaPostDelivered;
            ShellModel.OnDirectMessageReceived += HandleDirectMessageReceived;
            ShellModel.OnFriendProfileUpdated += HandleFriendProfileUpdated;
            ShellModel.OnUserProfileUpdated += HandleUserProfileUpdated;

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

            var list = new System.Collections.Generic.List<string>
                { FirstRecentEmoji, SecondRecentEmoji, ThirdRecentEmoji };
            list.Remove(emoji);
            list.Insert(0, emoji); // En son kullanılan emoji en soldan başlar

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

        private void HandleMediaPostDelivered(Guid messageId)
        {
            var existingInAll = _allFeedPosts.FirstOrDefault(p => p.Id == messageId);
            if (existingInAll != null)
            {
                existingInAll.SyncStatus = Shared.Enums.SyncStatus.Delivered;
            }

            var existing = FeedPosts.FirstOrDefault(p => p.Id == messageId);
            if (existing != null)
            {
                existing.SyncStatus = Shared.Enums.SyncStatus.Delivered;
            }
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

            if (ActivePanelIndex == 2)
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
                GridItemSize = GridItemSize,
                TopHeaderHeight = TopHeaderHeight,
                FeedBottomSpacerHeight = FeedBottomSpacerHeight,
                OnReplyOrReactRequested = HandlePostReplyOrReact,
                OnGridPostTapped = HandleGridPostTapped
            };

            await _database.SaveMediaPostAsync(mediaPost);

            _allFeedPosts.Insert(0, mediaPost);
            OnPropertyChanged(nameof(AllFeedPostsCount));
            if (DoesPostMatchFilter(mediaPost))
            {
                FeedPosts.Insert(0, mediaPost);
                CurrentFeedPost = mediaPost;
            }

            OnPropertyChanged(nameof(ShowEmptyFilteredFeedCard));

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

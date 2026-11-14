using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using Pocket.Client.Data;
using Pocket.Client.Models;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Pocket.Client.PageModels
{
    [QueryProperty(nameof(FriendId), "FriendId")]
    [QueryProperty(nameof(MediaPostId), "MediaPostId")]
    public partial class DmPageModel : ObservableObject
    {
        private readonly LocalDatabase _database;
        private readonly AppShellModel _shellModel;

        [ObservableProperty] private string _friendId = string.Empty;

        [ObservableProperty] private string? _mediaPostId;

        [ObservableProperty] private string _friendDisplayName = "Sohbet";

        [ObservableProperty] private string _friendUsername = string.Empty;

        [ObservableProperty] private string _friendAvatarUrl = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasMessageText))]
        [NotifyPropertyChangedFor(nameof(ShowQuickEmojis))]
        private string _messageText = string.Empty;

        public bool HasMessageText => !string.IsNullOrWhiteSpace(MessageText);
        public bool ShowQuickEmojis => string.IsNullOrWhiteSpace(MessageText);

        [ObservableProperty] private string _firstRecentEmoji = "💗";

        [ObservableProperty] private string _secondRecentEmoji = "🙃";

        [ObservableProperty] private string _thirdRecentEmoji = "😘";

        [ObservableProperty] private bool _isEmojiPickerOpen;

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

        public Action? OpenEmojiPickerAction { get; set; }
        public Action? CloseEmojiPickerAction { get; set; }
        public Action<int>? ScrollToEmojiCategoryAction { get; set; }
        public Action? FocusEmojiSearchAction { get; set; }

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
        private async Task SelectEmojiFromPickerAsync(string? emoji)
        {
            if (string.IsNullOrWhiteSpace(emoji))
                return;

            await SendQuickEmojiMessageAsync(emoji);
            CloseEmojiPicker();
        }

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasReplyPost))]
        private MediaPost? _replyingToPost;

        public bool HasReplyPost => ReplyingToPost != null;

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(SelectedMessageDeleteLabel))]
        private DirectMessage? _selectedActionMessage;

        public string SelectedMessageDeleteLabel =>
            SelectedActionMessage?.IsOutgoing == true ? "Undo Send" : "Delete";

        [ObservableProperty] private bool _isMessageActionsOpen;

        public ObservableCollection<DirectMessage> Messages { get; } = new();

        public Action? ScrollToBottomAction { get; set; }
        public Action? ShowSendFailedToastAction { get; set; }
        public Action? OpenMessageActionsAction { get; set; }
        public Action? CloseMessageActionsAction { get; set; }
        public Action? ShowCopiedToastAction { get; set; }

        public DmPageModel(LocalDatabase database, AppShellModel shellModel)
        {
            _database = database;
            _shellModel = shellModel;

            _shellModel.OnDirectMessageReceived += HandleDirectMessageReceived;
            _shellModel.OnDirectMessageDelivered += HandleDirectMessageDelivered;
            _shellModel.OnDirectMessageSendFailed += HandleDirectMessageSendFailed;
            _shellModel.OnDirectMessageDeleted += HandleDirectMessageDeleted;
            _shellModel.OnMediaPostDeleted += HandleMediaPostDeleted;
            _shellModel.OnFriendProfileUpdated += HandleFriendProfileUpdated;
            _shellModel.OnUserProfileUpdated += HandleUserProfileUpdated;

            LoadRecentEmojis();
        }

        public void SetDmWindowActive(bool isActive)
        {
            _shellModel.IsInDmWindow = isActive;
        }

        public void LoadRecentEmojis()
        {
            var saved = Microsoft.Maui.Storage.Preferences.Default.Get("RecentEmojis", "💗,🙃,😘");
            var parts = saved.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0) FirstRecentEmoji = parts[0].Trim();
            if (parts.Length > 1) SecondRecentEmoji = parts[1].Trim();
            if (parts.Length > 2) ThirdRecentEmoji = parts[2].Trim();
        }

        private void UpdateRecentEmoji(string emoji)
        {
            if (string.IsNullOrWhiteSpace(emoji)) return;

            var list = new System.Collections.Generic.List<string>
                { FirstRecentEmoji, SecondRecentEmoji, ThirdRecentEmoji };
            list.Remove(emoji);
            list.Insert(0, emoji);

            FirstRecentEmoji = list.Count > 0 ? list[0] : "💗";
            SecondRecentEmoji = list.Count > 1 ? list[1] : "🙃";
            ThirdRecentEmoji = list.Count > 2 ? list[2] : "😘";

            Microsoft.Maui.Storage.Preferences.Default.Set("RecentEmojis",
                $"{FirstRecentEmoji},{SecondRecentEmoji},{ThirdRecentEmoji}");
        }

        private void HandleFriendProfileUpdated(Friend friend)
        {
            if (friend.Id == FriendId)
            {
                FriendDisplayName = friend.DisplayName;
                FriendUsername = friend.Username;
                FriendAvatarUrl = friend.ProfilePictureUrl ?? string.Empty;

                foreach (var msg in Messages)
                {
                    if (msg.IsIncoming)
                    {
                        msg.SenderAvatarUrl = FriendAvatarUrl;
                    }

                    if (msg.HasMediaReference && msg.ReferencedPostAuthorLabel != "You")
                    {
                        msg.ReferencedPostAuthorLabel = FriendDisplayName;
                        msg.ReferencedPostAuthorAvatarUrl = FriendAvatarUrl;
                    }
                }
            }
        }

        private void HandleUserProfileUpdated(User user)
        {
            foreach (var msg in Messages)
            {
                if (!msg.IsIncoming)
                {
                    msg.SenderAvatarUrl = user.ProfilePictureUrl ?? string.Empty;
                }

                if (msg.HasMediaReference && msg.ReferencedPostAuthorLabel == "You")
                {
                    msg.ReferencedPostAuthorAvatarUrl = user.ProfilePictureUrl ?? string.Empty;
                }
            }
        }

        partial void OnFriendIdChanged(string value)
        {
            _ = LoadConversationAsync();
        }

        partial void OnMediaPostIdChanged(string? value)
        {
            _ = LoadReplyPostAsync(value);
        }

        private async Task LoadReplyPostAsync(string? postIdStr)
        {
            if (!string.IsNullOrWhiteSpace(postIdStr) && Guid.TryParse(postIdStr, out var postGuid))
            {
                ReplyingToPost = await _database.GetMediaPostAsync(postGuid);
            }
            else
            {
                ReplyingToPost = null;
            }
        }

        public async Task LoadConversationAsync()
        {
            if (string.IsNullOrWhiteSpace(FriendId))
                return;

            LoadRecentEmojis();

            var friend = await _database.GetFriendAsync(FriendId);
            if (friend != null)
            {
                FriendDisplayName = friend.DisplayName;
                FriendUsername = friend.Username;
                FriendAvatarUrl = friend.ProfilePictureUrl ?? string.Empty;
            }

            await LoadReplyPostAsync(MediaPostId);

            var dms = await _database.GetDirectMessagesForFriendAsync(FriendId);
            var referencedIds = dms
                .Where(d => d.MediaPostId.HasValue)
                .Select(d => d.MediaPostId!.Value)
                .Distinct()
                .ToList();

            var postMap = new System.Collections.Generic.Dictionary<Guid, MediaPost>();
            foreach (var postId in referencedIds)
            {
                var p = await _database.GetMediaPostAsync(postId);
                if (p != null)
                {
                    postMap[postId] = p;
                }
            }

            string myAvatarUrl = _shellModel.CurrentUser.ProfilePictureUrl ?? string.Empty;
            Guid? lastShownMediaPostId = null;

            Messages.Clear();
            foreach (var dm in dms.Where(d => !d.IsPostReactionOnly))
            {
                dm.SenderAvatarUrl = dm.IsIncoming ? FriendAvatarUrl : myAvatarUrl;

                if (dm.MediaPostId.HasValue && postMap.TryGetValue(dm.MediaPostId.Value, out var refPost))
                {
                    dm.ReferencedPostPath = refPost.MediaLocalPath;
                    dm.ReferencedPostCaption = refPost.Caption ?? string.Empty;
                    dm.ReferencedPostAuthorLabel = refPost.IsIncoming ? FriendDisplayName : "You";
                    dm.ReferencedPostAuthorAvatarUrl = refPost.IsIncoming ? FriendAvatarUrl : myAvatarUrl;
                    dm.ReferencedPostTimeText = MediaPost.FormatRelativeTime(refPost.SentAt);
                    dm.ShowMediaCard = lastShownMediaPostId != dm.MediaPostId;
                    lastShownMediaPostId = dm.MediaPostId;
                }
                else
                {
                    lastShownMediaPostId = null;
                }

                Messages.Add(dm);
            }

            ScrollToBottomAction?.Invoke();
        }

        private async void HandleDirectMessageReceived(DirectMessage dm)
        {
            if (dm.IsPostReactionOnly)
                return;

            if (dm.FriendId == FriendId && Messages.All(m => m.Id != dm.Id))
            {
                string myAvatarUrl = _shellModel.CurrentUser.ProfilePictureUrl ?? string.Empty;
                dm.SenderAvatarUrl = dm.IsIncoming ? FriendAvatarUrl : myAvatarUrl;

                if (dm.MediaPostId.HasValue)
                {
                    if (string.IsNullOrEmpty(dm.ReferencedPostPath))
                    {
                        var refPost = await _database.GetMediaPostAsync(dm.MediaPostId.Value);
                        if (refPost != null)
                        {
                            dm.ReferencedPostPath = refPost.MediaLocalPath;
                            dm.ReferencedPostCaption = refPost.Caption ?? string.Empty;
                            dm.ReferencedPostAuthorLabel = refPost.IsIncoming ? FriendDisplayName : "You";
                            dm.ReferencedPostAuthorAvatarUrl = refPost.IsIncoming ? FriendAvatarUrl : myAvatarUrl;
                            dm.ReferencedPostTimeText = MediaPost.FormatRelativeTime(refPost.SentAt);
                        }
                    }

                    var prevMsg = Messages.LastOrDefault();
                    dm.ShowMediaCard = prevMsg == null || prevMsg.MediaPostId != dm.MediaPostId;
                }

                Messages.Add(dm);
                ScrollToBottomAction?.Invoke();
            }
        }

        private void HandleDirectMessageDelivered(Guid messageId)
        {
            var existing = Messages.FirstOrDefault(m => m.Id == messageId);
            if (existing != null)
            {
                existing.SyncStatus = Shared.Enums.SyncStatus.Delivered;
            }
        }

        private void HandleDirectMessageSendFailed(DirectMessage dm)
        {
            if (dm.FriendId == FriendId)
            {
                var existing = Messages.FirstOrDefault(m => m.Id == dm.Id);
                if (existing != null)
                {
                    existing.SyncStatus = Shared.Enums.SyncStatus.Failed;
                }

                ShowSendFailedToastAction?.Invoke();
            }
        }

        private void HandleDirectMessageDeleted(Guid messageId)
        {
            RemoveMessageFromList(messageId);
        }

        private void RemoveMessageFromList(Guid messageId)
        {
            var existing = Messages.FirstOrDefault(m => m.Id == messageId);
            if (existing != null)
            {
                Messages.Remove(existing);
                RecalculateShowMediaCards();
            }
        }

        [RelayCommand]
        private void OpenMessageActions(DirectMessage? dm)
        {
            if (dm == null)
                return;

            if (IsEmojiPickerOpen)
            {
                CloseEmojiPicker();
            }

            SelectedActionMessage = dm;
            IsMessageActionsOpen = true;
            OpenMessageActionsAction?.Invoke();
        }

        [RelayCommand]
        private void CloseMessageActions()
        {
            if (CloseMessageActionsAction != null)
            {
                CloseMessageActionsAction.Invoke();
            }
            else
            {
                IsMessageActionsOpen = false;
                SelectedActionMessage = null;
            }
        }

        [RelayCommand]
        private async Task CopySelectedMessageAsync()
        {
            var dm = SelectedActionMessage;
            CloseMessageActions();

            if (dm == null || string.IsNullOrEmpty(dm.DisplayText))
                return;

            await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(dm.DisplayText);
            ShowCopiedToastAction?.Invoke();
        }

        [RelayCommand]
        private async Task DeleteSelectedMessageAsync()
        {
            var dm = SelectedActionMessage;
            CloseMessageActions();

            if (dm == null)
                return;

            RemoveMessageFromList(dm.Id);
            await _shellModel.DeleteDirectMessageAsync(dm, undoSendForRecipient: !dm.IsIncoming);
        }

        [RelayCommand]
        private async Task RetryMessageAsync(DirectMessage? dm)
        {
            if (dm == null)
                return;

            await _shellModel.RetryDirectMessageAsync(dm);
        }

        [RelayCommand]
        private async Task DeleteFailedMessageAsync(DirectMessage? dm)
        {
            if (dm == null)
                return;

            RemoveMessageFromList(dm.Id);
            await _shellModel.DeleteDirectMessageAsync(dm);
        }

        private void RecalculateShowMediaCards()
        {
            Guid? lastShownMediaPostId = null;
            foreach (var msg in Messages)
            {
                if (msg.HasMediaReference && msg.MediaPostId.HasValue)
                {
                    msg.ShowMediaCard = lastShownMediaPostId != msg.MediaPostId;
                    lastShownMediaPostId = msg.MediaPostId;
                }
                else
                {
                    msg.ShowMediaCard = false;
                    lastShownMediaPostId = null;
                }
            }
        }

        private void HandleMediaPostDeleted(Guid postId)
        {
            if (ReplyingToPost?.Id == postId)
            {
                ReplyingToPost = null;
                MediaPostId = null;
            }

            foreach (var msg in Messages)
            {
                if (msg.MediaPostId == postId)
                {
                    msg.MediaPostId = null;
                    msg.ReferencedPostPath = string.Empty;
                    msg.ShowMediaCard = false;
                }
            }
        }

        [RelayCommand]
        private async Task SendMessageAsync()
        {
            var text = (MessageText ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(FriendId))
                return;

            var replyPostId = ReplyingToPost?.Id;
            MessageText = string.Empty;
            ReplyingToPost = null;
            MediaPostId = null;

            var dm = await _shellModel.SendDirectMessageAsync(FriendId, text, null, replyPostId);
            if (dm != null && Messages.All(m => m.Id != dm.Id))
            {
                var prevMsg = Messages.LastOrDefault();
                dm.ShowMediaCard = prevMsg == null || prevMsg.MediaPostId != dm.MediaPostId;
                Messages.Add(dm);
                ScrollToBottomAction?.Invoke();
            }
        }

        [RelayCommand]
        private Task SendFirstRecentEmojiAsync() => SendQuickEmojiMessageAsync(FirstRecentEmoji);

        [RelayCommand]
        private Task SendSecondRecentEmojiAsync() => SendQuickEmojiMessageAsync(SecondRecentEmoji);

        [RelayCommand]
        private Task SendThirdRecentEmojiAsync() => SendQuickEmojiMessageAsync(ThirdRecentEmoji);

        private async Task SendQuickEmojiMessageAsync(string? emoji)
        {
            if (string.IsNullOrWhiteSpace(emoji) || string.IsNullOrEmpty(FriendId))
                return;

            UpdateRecentEmoji(emoji);

            var replyPostId = ReplyingToPost?.Id;
            ReplyingToPost = null;
            MediaPostId = null;

            var dm = await _shellModel.SendDirectMessageAsync(FriendId, emoji, null, replyPostId);
            if (dm != null && Messages.All(m => m.Id != dm.Id))
            {
                var prevMsg = Messages.LastOrDefault();
                dm.ShowMediaCard = prevMsg == null || prevMsg.MediaPostId != dm.MediaPostId;
                Messages.Add(dm);
                ScrollToBottomAction?.Invoke();
            }
        }

        [RelayCommand]
        private async Task SendReactionAsync(string? emoji)
        {
            if (string.IsNullOrWhiteSpace(emoji) || string.IsNullOrEmpty(FriendId))
                return;

            UpdateRecentEmoji(emoji);

            var optionalText = string.IsNullOrWhiteSpace(MessageText) ? null : MessageText.Trim();
            var replyPostId = ReplyingToPost?.Id;
            MessageText = string.Empty;
            ReplyingToPost = null;
            MediaPostId = null;

            var dm = await _shellModel.SendDirectMessageAsync(FriendId, optionalText ?? emoji, null, replyPostId);
            if (dm != null && Messages.All(m => m.Id != dm.Id))
            {
                var prevMsg = Messages.LastOrDefault();
                dm.ShowMediaCard = prevMsg == null || prevMsg.MediaPostId != dm.MediaPostId;
                Messages.Add(dm);
                ScrollToBottomAction?.Invoke();
            }
        }

        [RelayCommand]
        private void ClearReplyPost()
        {
            ReplyingToPost = null;
            MediaPostId = null;
        }

        [RelayCommand]
        private async Task GoBackAsync()
        {
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("..");
            }
        }
    }
}

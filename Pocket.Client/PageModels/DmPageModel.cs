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

        [ObservableProperty] private string _friendDisplayName = "Chat";

        [ObservableProperty] private string _friendUsername = string.Empty;

        [ObservableProperty] private string _friendAvatarUrl = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasMessageText))]
        [NotifyPropertyChangedFor(nameof(ShowQuickEmojis))]
        private string _messageText = string.Empty;

        public bool HasMessageText => !string.IsNullOrWhiteSpace(MessageText);
        public bool ShowQuickEmojis => string.IsNullOrWhiteSpace(MessageText) && !IsEditingMessage;
        public bool ShowSendButton => HasMessageText && !IsEditingMessage;

        /// <summary>Our own message whose text is in the input box for editing (null = normal sending).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsEditingMessage))]
        [NotifyPropertyChangedFor(nameof(ShowQuickEmojis))]
        [NotifyPropertyChangedFor(nameof(ShowSendButton))]
        [NotifyPropertyChangedFor(nameof(ShowReplyBanner))]
        [NotifyPropertyChangedFor(nameof(ChromeOpacity))]
        private DirectMessage? _editingMessage;

        public bool IsEditingMessage => EditingMessage != null;

        /// <summary>Header and other chrome darken while a message is edited, like the other messages.</summary>
        public double ChromeOpacity => IsEditingMessage ? 0.28 : 1.0;

        /// <summary>The photo-reply banner steps aside while a message is being edited (the reply is kept).</summary>
        public bool ShowReplyBanner => HasReplyPost && !IsEditingMessage;

        public bool CanEditSelectedMessage => SelectedActionMessage?.CanEdit == true;

        /// <summary>Edit mode started: the page focuses the input box and keeps the message in view.</summary>
        public Action<DirectMessage>? BeginEditMessageAction { get; set; }

        /// <summary>Edit mode ended (saved or cancelled).</summary>
        public Action? EndEditMessageAction { get; set; }

        partial void OnMessageTextChanged(string value)
        {
            OnPropertyChanged(nameof(ShowSendButton));
        }

        [RelayCommand]
        private void EditSelectedMessage()
        {
            var dm = SelectedActionMessage;
            CloseMessageActions();

            if (dm == null || !dm.CanEdit)
                return;

            if (IsEmojiPickerOpen)
                CloseEmojiPicker();

            // A draft typed before is not kept: the box now holds the message being edited.
            EditingMessage = dm;
            foreach (var msg in Messages)
            {
                msg.IsDimmed = !ReferenceEquals(msg, dm);
            }

            MessageText = dm.TextContent ?? string.Empty;
            BeginEditMessageAction?.Invoke(dm);
        }

        [RelayCommand]
        private void CancelEditMessage()
        {
            if (!IsEditingMessage)
                return;

            EndEditMode();
        }

        private void EndEditMode()
        {
            EditingMessage = null;
            foreach (var msg in Messages)
            {
                msg.IsDimmed = false;
            }

            MessageText = string.Empty;
            EndEditMessageAction?.Invoke();
        }

        private async Task SaveEditedMessageAsync()
        {
            var target = EditingMessage;
            var text = (MessageText ?? string.Empty).Trim();
            if (target == null || string.IsNullOrEmpty(text))
                return; // empty text: the check stays disabled, nothing to save

            EndEditMode();

            if (text == target.TextContent)
                return;

            await _shellModel.EditDirectMessageAsync(target, text);
        }

        private void HandleDirectMessageEdited(Guid messageId, string text, DateTime editedAt)
        {
            var existing = Messages.FirstOrDefault(m => m.Id == messageId);
            if (existing != null && (existing.EditedAt is not DateTime known || known < editedAt))
            {
                existing.TextContent = text;
                existing.EditedAt = editedAt;
            }
        }

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

            // Picker opened from the reaction bar's "+": the emoji is a reaction, not a message.
            var reactionTarget = _reactionPickerTarget;
            _reactionPickerTarget = null;
            CloseEmojiPicker();

            if (reactionTarget != null)
            {
                await ApplyReactionAsync(reactionTarget, emoji);
                return;
            }

            await SendQuickEmojiMessageAsync(emoji);
        }

        partial void OnIsEmojiPickerOpenChanged(bool value)
        {
            if (!value)
            {
                _reactionPickerTarget = null;
            }
        }

        /// <summary>Quick reactions in the long-press popup (the current one is highlighted).</summary>
        public ObservableCollection<QuickReactionItem> QuickReactions { get; } = new()
        {
            new("💛"), new("🫶"), new("😍"), new("💕"), new("🤣")
        };

        private DirectMessage? _reactionPickerTarget;

        public bool CanReactToSelectedMessage => SelectedActionMessage?.IsIncoming == true;

        [RelayCommand]
        private async Task ReactToSelectedMessageAsync(string? emoji)
        {
            var target = SelectedActionMessage;
            CloseMessageActions();

            if (target == null || !target.IsIncoming || string.IsNullOrWhiteSpace(emoji))
                return;

            // Tapping the reaction you already gave removes it.
            await ApplyReactionAsync(target, target.MessageReaction == emoji ? null : emoji);
        }

        [RelayCommand]
        private void OpenReactionPicker()
        {
            var target = SelectedActionMessage;
            CloseMessageActions();

            if (target == null || !target.IsIncoming)
                return;

            OpenEmojiPicker();
            _reactionPickerTarget = target;
        }

        private async Task ApplyReactionAsync(DirectMessage target, string? emoji)
        {
            // Visible copy first: its corner badge pops in right away.
            target.MessageReaction = emoji;
            await _shellModel.SetMessageReactionAsync(target, emoji);
        }

        private void HandleDirectMessageReactionChanged(Guid messageId, string? emoji)
        {
            var existing = Messages.FirstOrDefault(m => m.Id == messageId);
            if (existing != null && existing.MessageReaction != emoji)
            {
                existing.MessageReaction = emoji;
            }
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasReplyPost))]
        [NotifyPropertyChangedFor(nameof(ShowReplyBanner))]
        private MediaPost? _replyingToPost;

        public bool HasReplyPost => ReplyingToPost != null;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedMessageDeleteLabel))]
        [NotifyPropertyChangedFor(nameof(CanReactToSelectedMessage))]
        [NotifyPropertyChangedFor(nameof(CanEditSelectedMessage))]
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

        private readonly Services.SendStateTicker _sendStateTicker;

        public DmPageModel(LocalDatabase database, AppShellModel shellModel)
        {
            _database = database;
            _shellModel = shellModel;

            _shellModel.OnDirectMessageReceived += HandleDirectMessageReceived;
            _shellModel.OnDirectMessageDelivered += HandleDirectMessageDelivered;
            _shellModel.OnDirectMessageSendFailed += HandleDirectMessageSendFailed;
            _shellModel.OnDirectMessageStatusChanged += HandleDirectMessageStatusChanged;

            // Slow-send hourglass / 24h give-up are time-based: tick while any own message is still unsent.
            _sendStateTicker = new Services.SendStateTicker(() => Messages);
            Messages.CollectionChanged += (_, _) => _sendStateTicker.Ensure();
            _shellModel.OnDirectMessageDeleted += HandleDirectMessageDeleted;
            _shellModel.OnDirectMessageReactionChanged += HandleDirectMessageReactionChanged;
            _shellModel.OnDirectMessageEdited += HandleDirectMessageEdited;
            _shellModel.OnMediaPostDeleted += HandleMediaPostDeleted;
            _shellModel.OnFriendProfileUpdated += HandleFriendProfileUpdated;
            _shellModel.OnUserProfileUpdated += HandleUserProfileUpdated;

            LoadRecentEmojis();
        }

        public void SetDmWindowActive(bool isActive)
        {
            _shellModel.IsInDmWindow = isActive;
            if (isActive)
                _shellModel.ActiveDmFriendId = FriendId;
            else if (_shellModel.ActiveDmFriendId == FriendId)
                _shellModel.ActiveDmFriendId = null;

            // Opening the chat reads it; leaving it also covers anything that arrived while it was open.
            if (!string.IsNullOrEmpty(FriendId))
                _ = _shellModel.MarkConversationReadAsync(FriendId);
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

            if (IsEditingMessage)
                EndEditMode();

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

                dm.IsDimmed = IsEditingMessage;
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

        private void HandleDirectMessageStatusChanged(Guid messageId, Shared.Enums.SyncStatus status)
        {
            var existing = Messages.FirstOrDefault(m => m.Id == messageId);
            if (existing != null && existing.SyncStatus != Shared.Enums.SyncStatus.Delivered)
            {
                existing.SyncStatus = status;
                _sendStateTicker.Ensure();
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
                if (ReferenceEquals(existing, EditingMessage))
                    EndEditMode();

                Messages.Remove(existing);
                RecalculateShowMediaCards();
            }
        }

        [RelayCommand]
        private void OpenMessageActions(DirectMessage? dm)
        {
            if (dm == null || IsEditingMessage)
                return;

            if (IsEmojiPickerOpen)
            {
                CloseEmojiPicker();
            }

            foreach (var item in QuickReactions)
            {
                item.IsSelected = dm.IsIncoming && item.Emoji == dm.MessageReaction;
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
            if (IsEditingMessage)
            {
                await SaveEditedMessageAsync();
                return;
            }

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

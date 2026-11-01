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

        [ObservableProperty]
        private string _friendId = string.Empty;

        [ObservableProperty]
        private string? _mediaPostId;

        [ObservableProperty]
        private string _friendDisplayName = "Sohbet";

        [ObservableProperty]
        private string _friendUsername = string.Empty;

        [ObservableProperty]
        private string _messageText = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasReplyPost))]
        private MediaPost? _replyingToPost;

        public bool HasReplyPost => ReplyingToPost != null;

        public ObservableCollection<DirectMessage> Messages { get; } = new();

        public DmPageModel(LocalDatabase database, AppShellModel shellModel)
        {
            _database = database;
            _shellModel = shellModel;

            _shellModel.OnDirectMessageReceived += HandleDirectMessageReceived;
            _shellModel.OnDirectMessageDelivered += HandleDirectMessageDelivered;
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

            var friend = await _database.GetFriendAsync(FriendId);
            if (friend != null)
            {
                FriendDisplayName = friend.DisplayName;
                FriendUsername = $"@{friend.Username}";
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

            Messages.Clear();
            foreach (var dm in dms.Where(d => !d.IsPostReactionOnly))
            {
                if (dm.MediaPostId.HasValue && postMap.TryGetValue(dm.MediaPostId.Value, out var refPost))
                {
                    dm.ReferencedPostPath = refPost.MediaLocalPath;
                    dm.ReferencedPostCaption = refPost.Caption ?? string.Empty;
                }
                Messages.Add(dm);
            }
        }

        private void HandleDirectMessageReceived(DirectMessage dm)
        {
            if (dm.IsPostReactionOnly)
                return;

            if (dm.FriendId == FriendId && Messages.All(m => m.Id != dm.Id))
            {
                Messages.Add(dm);
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
                Messages.Add(dm);
            }
        }

        [RelayCommand]
        private async Task SendReactionAsync(string? emoji)
        {
            if (string.IsNullOrWhiteSpace(emoji) || string.IsNullOrEmpty(FriendId))
                return;

            var optionalText = string.IsNullOrWhiteSpace(MessageText) ? null : MessageText.Trim();
            var replyPostId = ReplyingToPost?.Id;
            MessageText = string.Empty;
            ReplyingToPost = null;
            MediaPostId = null;

            var dm = await _shellModel.SendDirectMessageAsync(FriendId, optionalText, emoji, replyPostId);
            if (dm != null && Messages.All(m => m.Id != dm.Id))
            {
                Messages.Add(dm);
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

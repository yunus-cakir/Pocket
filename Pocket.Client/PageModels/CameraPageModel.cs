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
        private bool _isPreviewing;

        [ObservableProperty]
        private string _capturedImagePath = string.Empty;

        [ObservableProperty]
        private string _caption = string.Empty;

        public AppShellModel ShellModel { get; }

        public Action? SnapToProfileAction;
        public Action? SnapToCameraAction;
        public Action? SnapToChatAction;

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
        }

        public ObservableCollection<MediaPost> FeedPosts { get; } = new();

        private void HandleMediaPostReceived(MediaPost post)
        {
            post.OnReplyOrReactRequested = HandlePostReplyOrReact;
            if (FeedPosts.All(p => p.Id != post.Id))
            {
                FeedPosts.Insert(0, post);
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

            if (Microsoft.Maui.Controls.Shell.Current != null)
            {
                await Microsoft.Maui.Controls.Shell.Current.GoToAsync(
                    $"{nameof(Pages.DmPage)}?FriendId={Uri.EscapeDataString(targetFriendId)}&MediaPostId={post.Id}");
            }

            if (!string.IsNullOrEmpty(reactionEmoji))
            {
                _ = ShellModel.SendDirectMessageAsync(targetFriendId, null, reactionEmoji, post.Id);
            }
        }

        public async Task LoadFeedAsync()
        {
            var friends = await _database.GetFriendsAsync();
            var friendMap = friends.ToDictionary(f => f.Id, f => f.Username);

            var posts = await _database.GetAllMediaPostsAsync();
            FeedPosts.Clear();
            foreach (var post in posts)
            {
                if (post.IsIncoming && !string.IsNullOrEmpty(post.FriendId) && friendMap.TryGetValue(post.FriendId, out var friendUsername))
                {
                    post.SenderDisplayName = friendUsername;
                }
                else if (!post.IsIncoming)
                {
                    post.SenderDisplayName = ShellModel.CurrentUser.Username;
                }

                post.OnReplyOrReactRequested = HandlePostReplyOrReact;
                FeedPosts.Add(post);
            }
        }

        public void OnPhotoCaptured(string imagePath)
        {
            CapturedImagePath = imagePath;
            IsPreviewing = true;
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
        private void Cancel()
        {
            IsPreviewing = false;
            CapturedImagePath = string.Empty;
            Caption = string.Empty;
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

            var friends = await _database.GetFriendsAsync();
            var sentCaption = Caption;
            var mediaPost = new MediaPost
            {
                Id = Guid.NewGuid(),
                FriendId = friends.Count == 1 ? friends[0].Id : string.Empty,
                IsIncoming = false,
                MediaLocalPath = persistentPath,
                MediaType = Shared.Enums.MediaType.Photo,
                Caption = sentCaption,
                SentAt = DateTime.UtcNow,
                IsIncludedInRollcall = true,
                SyncStatus = Shared.Enums.SyncStatus.Pending,
                SenderDisplayName = ShellModel.CurrentUser.Username,
                OnReplyOrReactRequested = HandlePostReplyOrReact
            };

            await _database.SaveMediaPostAsync(mediaPost);

            FeedPosts.Insert(0, mediaPost);

            IsPreviewing = false;
            CapturedImagePath = string.Empty;
            Caption = string.Empty;

            // E2EE Encryption & Transmission to Mutual Friends (BR-601, BR-201)
            var privateKey = await ShellModel.GetPrivateKeyAsync();

            if (friends.Count > 0 && !string.IsNullOrEmpty(privateKey) && _relayService.IsConnected)
            {
                var photoBytes = await System.IO.File.ReadAllBytesAsync(persistentPath);
                var captionBytes = Encoding.UTF8.GetBytes(sentCaption ?? string.Empty);
                int sentCount = 0;

                foreach (var friend in friends)
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

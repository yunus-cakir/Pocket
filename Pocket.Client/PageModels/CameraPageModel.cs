using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pocket.Client.Data;
using Pocket.Client.Models;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Pocket.Client.PageModels
{
    public partial class CameraPageModel : ObservableObject
    {
        private readonly LocalDatabase _database;

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

        public CameraPageModel(LocalDatabase database, AppShellModel shellModel)
        {
            _database = database;
            ShellModel = shellModel;
        }

        public ObservableCollection<MediaPost> FeedPosts { get; } = new();

        public async Task LoadFeedAsync()
        {
            var posts = await _database.GetAllMediaPostsAsync();
            FeedPosts.Clear();
            foreach (var post in posts)
            {
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

            var mediaPost = new MediaPost
            {
                Id = Guid.NewGuid(),
                FriendId = string.Empty, // Send to all
                IsIncoming = false,
                MediaLocalPath = persistentPath,
                MediaType = Shared.Enums.MediaType.Photo,
                Caption = Caption,
                SentAt = DateTime.UtcNow,
                IsIncludedInRollcall = true,
                SyncStatus = Shared.Enums.SyncStatus.Pending
            };

            await _database.SaveMediaPostAsync(mediaPost);

            FeedPosts.Insert(0, mediaPost);

            IsPreviewing = false;
            CapturedImagePath = string.Empty;
            Caption = string.Empty;
        }
    }
}

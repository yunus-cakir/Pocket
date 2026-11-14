using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Provider;
using AndroidX.Core.View;

namespace Pocket.Client
{
    [Activity(
        Label = "Pocket",
        Theme = "@style/Maui.SplashTheme",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        ScreenOrientation = ScreenOrientation.Portrait,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        private const int PickPhotoRequestCode = 9102;
        private const int PickBackupFileRequestCode = 9103;
        private const int CreateBackupDocumentRequestCode = 9104;
        private const int RequestPostNotificationsCode = 9105;

        private static TaskCompletionSource<global::Android.Net.Uri?>? _pickPhotoTcs;
        private static TaskCompletionSource<global::Android.Net.Uri?>? _pickBackupFileTcs;
        private static TaskCompletionSource<global::Android.Net.Uri?>? _createDocumentTcs;

        /// <summary>
        /// Launches the Android system Photo Picker (ACTION_PICK_IMAGES on API 33+, fallback to ACTION_GET_CONTENT)
        /// so the user can select a single photo without granting gallery/storage permissions.
        /// </summary>
        public static Task<global::Android.Net.Uri?> PickSinglePhotoAsync()
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity as MainActivity;
            if (activity == null)
                return Task.FromResult<global::Android.Net.Uri?>(null);

            _pickPhotoTcs?.TrySetCanceled();
            var tcs = new TaskCompletionSource<global::Android.Net.Uri?>();
            _pickPhotoTcs = tcs;

            try
            {
                Intent intent;
                if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
                {
                    intent = new Intent(MediaStore.ActionPickImages);
                    intent.SetType("image/*");
                }
                else
                {
                    intent = new Intent(Intent.ActionGetContent);
                    intent.SetType("image/*");
                    intent.AddCategory(Intent.CategoryOpenable);
                }

                activity.StartActivityForResult(intent, PickPhotoRequestCode);
            }
            catch
            {
                _pickPhotoTcs = null;
                tcs.TrySetResult(null);
            }

            return tcs.Task;
        }

        /// <summary>
        /// Launches the Android Storage Access Framework document picker (ACTION_OPEN_DOCUMENT)
        /// so the user can pick a backup file (.zip, .json, or .db3).
        /// </summary>
        public static Task<global::Android.Net.Uri?> PickBackupFileAsync()
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity as MainActivity;
            if (activity == null)
                return Task.FromResult<global::Android.Net.Uri?>(null);

            _pickBackupFileTcs?.TrySetCanceled();
            var tcs = new TaskCompletionSource<global::Android.Net.Uri?>();
            _pickBackupFileTcs = tcs;

            try
            {
                var intent = new Intent(Intent.ActionOpenDocument);
                intent.AddCategory(Intent.CategoryOpenable);
                intent.SetType("*/*");
                activity.StartActivityForResult(intent, PickBackupFileRequestCode);
            }
            catch
            {
                _pickBackupFileTcs = null;
                tcs.TrySetResult(null);
            }

            return tcs.Task;
        }

        /// <summary>
        /// Launches the Android Storage Access Framework "Save As" dialog (ACTION_CREATE_DOCUMENT)
        /// so the user can choose where to save the exported backup archive.
        /// </summary>
        public static Task<global::Android.Net.Uri?> CreateBackupDocumentAsync(string suggestedFileName, string mimeType = "application/zip")
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity as MainActivity;
            if (activity == null)
                return Task.FromResult<global::Android.Net.Uri?>(null);

            _createDocumentTcs?.TrySetCanceled();
            var tcs = new TaskCompletionSource<global::Android.Net.Uri?>();
            _createDocumentTcs = tcs;

            try
            {
                var intent = new Intent(Intent.ActionCreateDocument);
                intent.AddCategory(Intent.CategoryOpenable);
                intent.SetType(mimeType);
                intent.PutExtra(Intent.ExtraTitle, suggestedFileName);
                activity.StartActivityForResult(intent, CreateBackupDocumentRequestCode);
            }
            catch
            {
                _createDocumentTcs = null;
                tcs.TrySetResult(null);
            }

            return tcs.Task;
        }

        /// <summary>
        /// Saves a backup archive directly into the device's public Downloads folder via MediaStore (API 29+)
        /// or external Downloads directory without requiring special permissions.
        /// </summary>
        public static async Task<string?> SaveToDownloadsAsync(string fileName, byte[] data, string mimeType = "application/zip")
        {
            var context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity ?? global::Android.App.Application.Context;
            if (context == null)
                return null;

            return await Task.Run(() =>
            {
                try
                {
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.Q && context.ContentResolver != null)
                    {
                        var values = new ContentValues();
                        values.Put(MediaStore.IMediaColumns.DisplayName, fileName);
                        values.Put(MediaStore.IMediaColumns.MimeType, mimeType);
                        values.Put(MediaStore.IMediaColumns.RelativePath, global::Android.OS.Environment.DirectoryDownloads + "/Pocket");
                        values.Put(MediaStore.IMediaColumns.IsPending, 1);

                        var collection = MediaStore.Downloads.ExternalContentUri;
                        var itemUri = context.ContentResolver.Insert(collection!, values);
                        if (itemUri != null)
                        {
                            using (var outStream = context.ContentResolver.OpenOutputStream(itemUri))
                            {
                                if (outStream != null)
                                {
                                    outStream.Write(data, 0, data.Length);
                                    outStream.Flush();
                                }
                            }

                            var updateValues = new ContentValues();
                            updateValues.Put(MediaStore.IMediaColumns.IsPending, 0);
                            context.ContentResolver.Update(itemUri, updateValues, null, null);
                            return $"Downloads/Pocket/{fileName}";
                        }
                    }

#pragma warning disable CS0618, CA1422
                    var downloadsDir = global::Android.OS.Environment.GetExternalStoragePublicDirectory(global::Android.OS.Environment.DirectoryDownloads)?.AbsolutePath;
#pragma warning restore CS0618, CA1422
                    if (!string.IsNullOrEmpty(downloadsDir))
                    {
                        var pocketDir = System.IO.Path.Combine(downloadsDir, "Pocket");
                        System.IO.Directory.CreateDirectory(pocketDir);
                        var targetPath = System.IO.Path.Combine(pocketDir, fileName);
                        System.IO.File.WriteAllBytes(targetPath, data);
                        return $"Downloads/Pocket/{fileName}";
                    }
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Backup] SaveToDownloadsAsync failed: {ex.Message}");
                }

                return null;
            });
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            if (requestCode == PickPhotoRequestCode)
            {
                var tcs = _pickPhotoTcs;
                _pickPhotoTcs = null;

                if (resultCode == Result.Ok && data != null)
                {
                    var uri = data.Data ?? data.ClipData?.GetItemAt(0)?.Uri;
                    tcs?.TrySetResult(uri);
                }
                else
                {
                    tcs?.TrySetResult(null);
                }
            }
            else if (requestCode == PickBackupFileRequestCode)
            {
                var tcs = _pickBackupFileTcs;
                _pickBackupFileTcs = null;

                if (resultCode == Result.Ok && data != null)
                {
                    var uri = data.Data ?? data.ClipData?.GetItemAt(0)?.Uri;
                    tcs?.TrySetResult(uri);
                }
                else
                {
                    tcs?.TrySetResult(null);
                }
            }
            else if (requestCode == CreateBackupDocumentRequestCode)
            {
                var tcs = _createDocumentTcs;
                _createDocumentTcs = null;

                if (resultCode == Result.Ok && data != null)
                {
                    tcs?.TrySetResult(data.Data);
                }
                else
                {
                    tcs?.TrySetResult(null);
                }
            }
        }

        protected override void AttachBaseContext(Context? @base)
        {
            if (@base != null)
            {
                @base = MainApplication.NormalizeContext(@base);
            }
            base.AttachBaseContext(@base);
            MainApplication.NormalizeMetrics(Resources);
        }

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            MainApplication.NormalizeMetrics(Resources);
            base.OnCreate(savedInstanceState);
            MainApplication.NormalizeMetrics(Resources);
            ConfigureSystemBars();
            Pocket.Client.Services.NotificationService.EnsureChannelInitialized(this);
            EnsureNotificationPermission();
            HandleNotificationIntent(Intent);
        }

        protected override void OnNewIntent(Intent? intent)
        {
            base.OnNewIntent(intent);
            Intent = intent;
            HandleNotificationIntent(intent);
        }

        private void EnsureNotificationPermission()
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                if (AndroidX.Core.Content.ContextCompat.CheckSelfPermission(this, global::Android.Manifest.Permission.PostNotifications) != Permission.Granted)
                {
                    AndroidX.Core.App.ActivityCompat.RequestPermissions(
                        this,
                        new[] { global::Android.Manifest.Permission.PostNotifications },
                        RequestPostNotificationsCode);
                }
            }
        }

        public static bool IsActivityResumed { get; private set; } = true;

        protected override void OnResume()
        {
            base.OnResume();
            IsActivityResumed = true;
            if (Microsoft.Maui.Controls.Shell.Current?.BindingContext is Pocket.Client.PageModels.AppShellModel shellModel)
            {
                shellModel.IsAppInForeground = true;
            }
        }

        protected override void OnPause()
        {
            IsActivityResumed = false;
            if (Microsoft.Maui.Controls.Shell.Current?.BindingContext is Pocket.Client.PageModels.AppShellModel shellModel)
            {
                shellModel.IsAppInForeground = false;
            }
            base.OnPause();
        }

        private static void HandleNotificationIntent(Intent? intent)
        {
            if (intent == null)
                return;

            var friendId = intent.GetStringExtra(Pocket.Client.Services.NotificationService.ExtraFriendId);
            if (string.IsNullOrWhiteSpace(friendId))
                return;

            bool isReaction = intent.GetBooleanExtra(Pocket.Client.Services.NotificationService.ExtraIsReaction, false);
            intent.RemoveExtra(Pocket.Client.Services.NotificationService.ExtraFriendId);
            intent.RemoveExtra(Pocket.Client.Services.NotificationService.ExtraIsReaction);
            intent.RemoveExtra(Pocket.Client.Services.NotificationService.ExtraMediaPostId);

            if (!isReaction)
            {
                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        if (Microsoft.Maui.Controls.Shell.Current != null)
                        {
                            if (Microsoft.Maui.Controls.Shell.Current.CurrentPage is Pocket.Client.Pages.DmPage currentDmPage &&
                                currentDmPage.BindingContext is Pocket.Client.PageModels.DmPageModel currentDmVm &&
                                currentDmVm.FriendId == friendId)
                            {
                                return;
                            }

                            await Microsoft.Maui.Controls.Shell.Current.GoToAsync(
                                $"{nameof(Pocket.Client.Pages.DmPage)}?FriendId={System.Uri.EscapeDataString(friendId)}");
                        }
                    }
                    catch (System.Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Notification] Navigation failed: {ex.Message}");
                    }
                });
            }
        }

        public override void OnConfigurationChanged(Configuration newConfig)
        {
            MainApplication.NormalizeMetrics(Resources);
            base.OnConfigurationChanged(newConfig);
            MainApplication.NormalizeMetrics(Resources);
        }

        private void ConfigureSystemBars()
        {
            if (Window == null)
                return;

            Window.SetSoftInputMode(global::Android.Views.SoftInput.AdjustNothing);

#pragma warning disable CS0618, CA1422
            Window.SetStatusBarColor(global::Android.Graphics.Color.Black);
            Window.SetNavigationBarColor(global::Android.Graphics.Color.Black);
#pragma warning restore CS0618, CA1422

            var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);
            if (controller != null)
            {
                controller.AppearanceLightStatusBars = false;
                controller.AppearanceLightNavigationBars = false;
            }
        }
    }
}

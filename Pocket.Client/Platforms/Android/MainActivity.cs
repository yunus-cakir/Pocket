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
        Theme = "@style/Maui.SplashTheme",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        ScreenOrientation = ScreenOrientation.Portrait,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        private const int PickPhotoRequestCode = 9102;
        private static TaskCompletionSource<global::Android.Net.Uri?>? _pickPhotoTcs;

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

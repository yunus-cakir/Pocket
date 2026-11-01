using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using AndroidX.Core.View;

namespace Pocket.Client
{
    [Activity(
        Theme = "@style/Maui.SplashTheme",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
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

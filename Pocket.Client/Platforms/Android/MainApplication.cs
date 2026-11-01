using System;
using System.Reflection;
using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Runtime;
using Android.Util;

namespace Pocket.Client
{
    [Application]
    public class MainApplication : MauiApplication
    {
        // Reference logical width in dp matching Pixel 7 (1080px @ 420dpi = 411.42857dp)
        public const float DesignWidthDp = 411.42857f;

        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }

        public override void OnCreate()
        {
            NormalizeMetrics(Resources);
            base.OnCreate();
            NormalizeMetrics(Resources);
        }

        public override void OnConfigurationChanged(Configuration newConfig)
        {
            NormalizeMetrics(Resources);
            base.OnConfigurationChanged(newConfig);
            NormalizeMetrics(Resources);
        }

        public static Context NormalizeContext(Context context)
        {
            var metrics = context.Resources?.DisplayMetrics;
            if (metrics == null)
                return context;

            int widthPx = Math.Min(metrics.WidthPixels, metrics.HeightPixels);
            if (widthPx <= 0)
                return context;

            float targetDensity = widthPx / DesignWidthDp;
            int targetDensityDpi = (int)Math.Round(targetDensity * 160f);

            var config = new Configuration(context.Resources!.Configuration);
            config.DensityDpi = targetDensityDpi;
            config.FontScale = 1.0f;

            var normalized = context.CreateConfigurationContext(config);
            if (normalized != null)
            {
                ApplyMetrics(normalized.Resources?.DisplayMetrics, targetDensity, targetDensityDpi);
                ApplyMetrics(metrics, targetDensity, targetDensityDpi);
                UpdateMauiCachedDensity(targetDensity);
                return normalized;
            }

            return context;
        }

        public static void NormalizeMetrics(Resources? resources)
        {
            var metrics = resources?.DisplayMetrics;
            if (metrics == null)
                return;

            int widthPx = Math.Min(metrics.WidthPixels, metrics.HeightPixels);
            if (widthPx <= 0)
                return;

            float targetDensity = widthPx / DesignWidthDp;
            int targetDensityDpi = (int)Math.Round(targetDensity * 160f);

            if (resources?.Configuration != null)
            {
                resources.Configuration.DensityDpi = targetDensityDpi;
                resources.Configuration.FontScale = 1.0f;
            }

            ApplyMetrics(metrics, targetDensity, targetDensityDpi);
            ApplyMetrics(Resources.System?.DisplayMetrics, targetDensity, targetDensityDpi);
            UpdateMauiCachedDensity(targetDensity);
        }

        private static void ApplyMetrics(DisplayMetrics? metrics, float targetDensity, int targetDensityDpi)
        {
            if (metrics == null)
                return;

            metrics.Density = targetDensity;
#pragma warning disable CA1422
            metrics.ScaledDensity = targetDensity;
#pragma warning restore CA1422
            metrics.DensityDpi = (DisplayMetricsDensity)targetDensityDpi;
        }

        private static void UpdateMauiCachedDensity(float targetDensity)
        {
            try
            {
                var contextExtType = typeof(Microsoft.Maui.MauiApplication).Assembly
                    .GetType("Microsoft.Maui.Platform.ContextExtensions");
                var densityField = contextExtType?.GetField(
                    "s_displayDensity",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                densityField?.SetValue(null, targetDensity);
            }
            catch
            {
                // Ignore if internal field name changes
            }
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}

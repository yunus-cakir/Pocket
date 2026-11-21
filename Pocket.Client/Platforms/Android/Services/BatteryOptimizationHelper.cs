using System;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Provider;

namespace Pocket.Client.Services
{
    /// <summary>
    /// Battery optimisation (Doze / app standby) exemption, so the background relay connection is not cut.
    /// Some ROMs (enterprise/Zebra builds, some Xiaomi/Huawei/Android Go images) have no handler for the
    /// direct "Allow Pocket to run in background?" dialog, so every launch falls back step by step:
    /// direct dialog → system "battery optimisation" list → the app's own info page.
    /// </summary>
    public static class BatteryOptimizationHelper
    {
        private const string AskedOnStartupKey = "BatteryOptimizationExemptionAsked";

        public static bool IsIgnoringOptimizations(Context context)
        {
            try
            {
                return context.GetSystemService(Context.PowerService) is PowerManager power &&
                       power.IsIgnoringBatteryOptimizations(context.PackageName);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Shows the direct exemption dialog; falls back to the settings screens if it can't be opened.</summary>
        public static bool TryRequestExemption(Activity activity)
        {
            var package = global::Android.Net.Uri.Parse("package:" + activity.PackageName);
            return TryStart(activity, new Intent(Settings.ActionRequestIgnoreBatteryOptimizations, package))
                || TryOpenSettings(activity);
        }

        /// <summary>Opens the system list where the user can switch the exemption on or off.</summary>
        public static bool TryOpenSettings(Activity activity)
        {
            var package = global::Android.Net.Uri.Parse("package:" + activity.PackageName);
            return TryStart(activity, new Intent(Settings.ActionIgnoreBatteryOptimizationSettings))
                || TryStart(activity, new Intent(Settings.ActionApplicationDetailsSettings, package));
        }

        /// <summary>
        /// Asks once per install, at startup. The "asked" flag is only stored once a screen actually
        /// opened, so a device that failed to show it is asked again next launch.
        /// </summary>
        public static void RequestOnceOnStartup(Activity activity)
        {
            try
            {
                var prefs = Microsoft.Maui.Storage.Preferences.Default;
                if (prefs.Get(AskedOnStartupKey, false) || IsIgnoringOptimizations(activity))
                    return;

                var package = global::Android.Net.Uri.Parse("package:" + activity.PackageName);
                // Startup only uses the direct dialog: dropping the user into a settings list unasked is confusing.
                if (TryStart(activity, new Intent(Settings.ActionRequestIgnoreBatteryOptimizations, package)))
                    prefs.Set(AskedOnStartupKey, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BatteryOptimization] Startup request failed: {ex.Message}");
            }
        }

        private static bool TryStart(Activity activity, Intent intent)
        {
            try
            {
                // No ResolveActivity pre-check: on API 30+ package visibility can hide the handler.
                // A missing handler throws ActivityNotFoundException, caught below.
                activity.StartActivity(intent);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BatteryOptimization] {intent.Action} failed: {ex.Message}");
                return false;
            }
        }
    }
}

using System;
using Android.Content;
using Android.Content.PM;
using Pocket.Client.PageModels;

namespace Pocket.Client.Services
{
    /// <summary>
    /// Switches the launcher icon to match the accent color. Android has no API to change an app's icon,
    /// so AndroidManifest.xml declares one launcher &lt;activity-alias&gt; per accent color (each with its own
    /// mipmap from Resources/AppIcon/Themed) and exactly one of them is kept enabled.
    /// Called when the app goes to the background: toggling launcher components makes the home screen
    /// refresh, which is less jarring than doing it while the user is looking at the color picker.
    /// </summary>
    public static class AppIconSwitcher
    {
        private const string AliasPrefix = "com.yunuscakir.pocket.client.LauncherIcon_";

        /// <summary>Enabled in the manifest (android:enabled="true"); every other alias starts disabled.</summary>
        private const string DefaultVariant = "lightblue";

        private static readonly string[] Variants = { "yellow", "lightblue", "darkblue", "green", "pink" };

        public static string VariantForAccent(string? hex) => AppShellModel.NormalizeAccentColorHex(hex) switch
        {
            AppShellModel.AccentDarkBlueHex => "darkblue",
            AppShellModel.AccentGreenHex => "green",
            AppShellModel.AccentPinkHex => "pink",
            AppShellModel.AccentYellowHex => "yellow",
            _ => "lightblue"
        };

        public static void ApplyForAccent(Context context, string? accentHex)
        {
            try
            {
                var pm = context.PackageManager;
                string? package = context.PackageName;
                if (pm == null || package == null)
                    return;

                string wanted = VariantForAccent(accentHex);

                // Enable the wanted alias before disabling the others so there is never a moment with no launcher entry.
                SetEnabled(pm, package, wanted, true);
                foreach (var variant in Variants)
                {
                    if (variant != wanted)
                        SetEnabled(pm, package, variant, false);
                }
            }
            catch (Exception ex)
            {
                Android.Util.Log.Warn("AppIconSwitcher", $"Failed to switch launcher icon: {ex}");
            }
        }

        private static void SetEnabled(PackageManager pm, string package, string variant, bool enabled)
        {
            var component = new ComponentName(package, AliasPrefix + variant);
            var state = pm.GetComponentEnabledSetting(component);
            bool isEnabled = state == ComponentEnabledState.Enabled ||
                             (state == ComponentEnabledState.Default && variant == DefaultVariant);
            if (isEnabled == enabled)
                return; // no-op writes still make some launchers rebuild their icon grid

            Android.Util.Log.Info("AppIconSwitcher", $"{(enabled ? "Enabling" : "Disabling")} launcher icon '{variant}'");
            pm.SetComponentEnabledSetting(
                component,
                enabled ? ComponentEnabledState.Enabled : ComponentEnabledState.Disabled,
                ComponentEnableOption.DontKillApp);
        }
    }
}

namespace Pocket.Client.Services
{
    /// <summary>Which emoji artwork the app draws emoji with (Settings → Appearance → Emoji layout).</summary>
    public enum EmojiLayout
    {
        /// <summary>The device's own emoji font.</summary>
        System = 0,
        /// <summary>Bundled WhatsApp-style emoji font (Resources/Fonts/WhatsAppEmoji.ttf).</summary>
        WhatsApp = 1,
        /// <summary>Bundled Google Noto Color Emoji font (Resources/Fonts/NotoColorEmoji.ttf).</summary>
        Noto = 2
    }

    /// <summary>
    /// Stores the emoji layout preference and tells the platform renderer when it changes.
    /// The emoji font files are optional: a layout whose font is not bundled falls back to System.
    /// </summary>
    public static class EmojiLayoutSettings
    {
        public const string PreferenceKey = "EmojiLayout";
        public const string WhatsAppFontFile = "WhatsAppEmoji.ttf";
        public const string NotoFontFile = "NotoColorEmoji.ttf";

        private static EmojiLayout? _current;

        /// <summary>Raised on the main thread after the preference changes.</summary>
        public static event Action<EmojiLayout>? Changed;

        public static EmojiLayout Current
        {
            get
            {
                if (_current is { } cached)
                    return cached;
                int raw = Microsoft.Maui.Storage.Preferences.Default.Get(PreferenceKey, (int)EmojiLayout.System);
                var layout = Enum.IsDefined(typeof(EmojiLayout), raw) ? (EmojiLayout)raw : EmojiLayout.System;
                _current = layout;
                return layout;
            }
        }

        public static void Set(EmojiLayout layout)
        {
            if (Current == layout)
                return;
            _current = layout;
            Microsoft.Maui.Storage.Preferences.Default.Set(PreferenceKey, (int)layout);
            Changed?.Invoke(layout);
        }

        public static string? GetFontFile(EmojiLayout layout) => layout switch
        {
            EmojiLayout.WhatsApp => WhatsAppFontFile,
            EmojiLayout.Noto => NotoFontFile,
            _ => null
        };

        public static string GetDisplayName(EmojiLayout layout) => layout switch
        {
            EmojiLayout.WhatsApp => "WhatsApp",
            EmojiLayout.Noto => "Google Noto Color",
            _ => "Default (system)"
        };

        private static readonly Dictionary<EmojiLayout, bool> AvailabilityCache = new();

        /// <summary>True when the layout can actually be drawn (System always; others need their bundled font).</summary>
        public static bool IsAvailable(EmojiLayout layout)
        {
            string? file = GetFontFile(layout);
            if (file == null)
                return true;
            if (AvailabilityCache.TryGetValue(layout, out bool known))
                return known;

            bool available = false;
#if ANDROID
            try
            {
                using var stream = global::Android.App.Application.Context.Assets!.Open(file);
                available = true;
            }
            catch
            {
                available = false;
            }
#endif
            AvailabilityCache[layout] = available;
            return available;
        }

        /// <summary>
        /// Forces a specific emoji layout on one Label regardless of the global preference
        /// (used by the previews in the Emoji layout picker).
        /// </summary>
        public static readonly BindableProperty OverrideProperty = BindableProperty.CreateAttached(
            "Override", typeof(string), typeof(EmojiLayoutSettings), null);

        /// <summary>The forced layout for <paramref name="view"/>, or null to follow the preference.</summary>
        public static EmojiLayout? GetOverride(BindableObject view) =>
            view.GetValue(OverrideProperty) is string raw && Enum.TryParse(raw, true, out EmojiLayout layout)
                ? layout
                : null;

        public static void SetOverride(BindableObject view, string? value) => view.SetValue(OverrideProperty, value);
    }
}

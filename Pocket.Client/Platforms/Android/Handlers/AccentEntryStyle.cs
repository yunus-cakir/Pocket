using Android.Content.Res;
using AndroidX.AppCompat.Widget;
using Microsoft.Maui.Handlers;
using AColor = Android.Graphics.Color;

namespace Pocket.Client.Platforms.Android.Handlers
{
    /// <summary>
    /// Tints every Entry (underline, cursor, selection handles and highlight) with the accent
    /// colour chosen in Settings instead of the theme's static colorAccent (yellow).
    /// Re-applied on focus so a colour change shows up in already-created fields.
    /// </summary>
    public static class AccentEntryStyle
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<AppCompatEditText, object> Subscribed = new();

        public static void Register()
        {
            EntryHandler.Mapper.AppendToMapping("PocketAccent", (handler, view) =>
            {
                if (handler.PlatformView is not AppCompatEditText editText) return;

                Apply(editText);
                if (!Subscribed.TryGetValue(editText, out _))
                {
                    Subscribed.Add(editText, new object()); // subscribe once per native view
                    editText.FocusChange += (s, e) =>
                    {
                        if (e.HasFocus && s is AppCompatEditText et)
                            Apply(et);
                    };
                }
            });

            // MAUI rebuilds the background drawable when Background changes: re-tint the new one.
            EntryHandler.Mapper.AppendToMapping(nameof(Microsoft.Maui.IView.Background), (handler, view) =>
            {
                if (handler.PlatformView is AppCompatEditText editText) TintUnderline(editText);
            });
        }

        public static void Apply(AppCompatEditText editText)
        {
            var accent = AColor.ParseColor(Pocket.Client.PageModels.AppShellModel.GetSavedAccentColorHex());

            TintUnderline(editText);

            // Selected-text highlight.
            editText.SetHighlightColor(new AColor(accent.R, accent.G, accent.B, (byte)0x66));

            // Cursor.
            IncognitoEntryHandler.ApplyCursorStyle(editText);

            // Selection handles (teardrops).
            if (System.OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                editText.TextSelectHandle = Tint(editText.TextSelectHandle, accent);
                editText.TextSelectHandleLeft = Tint(editText.TextSelectHandleLeft, accent);
                editText.TextSelectHandleRight = Tint(editText.TextSelectHandleRight, accent);
            }
        }

        /// <summary>
        /// Tints only the native underline. With a BackgroundColor (the global Entry style sets
        /// Transparent) MAUI wraps it in a MauiLayerDrawable as [colour layer, underline layer];
        /// tinting the whole drawable would also fill the colour layer, painting the whole box.
        /// </summary>
        private static void TintUnderline(AppCompatEditText editText)
        {
            var background = editText.Background;
            if (background == null) return; // no underline (e.g. IncognitoEntry)

            var accent = AColor.ParseColor(Pocket.Client.PageModels.AppShellModel.GetSavedAccentColorHex());
            var tint = new ColorStateList(
                new[] { new[] { global::Android.Resource.Attribute.StateFocused }, System.Array.Empty<int>() },
                new int[] { accent, AColor.ParseColor("#8E8E93") });

            if (background is global::Android.Graphics.Drawables.LayerDrawable layers)
            {
                for (int i = 0; i < layers.NumberOfLayers; i++)
                {
                    var layer = layers.GetDrawable(i);
                    // Layer 0 is MAUI's background colour (MauiDrawable); the native underline is an InsetDrawable.
                    if (layer is not global::Android.Graphics.Drawables.InsetDrawable)
                        continue;
                    layer.Mutate().SetTintList(tint);
                }
                background.InvalidateSelf();
            }
            else if (background is global::Android.Graphics.Drawables.InsetDrawable or global::Android.Graphics.Drawables.StateListDrawable)
            {
                editText.BackgroundTintList = tint;
            }
        }

        private static global::Android.Graphics.Drawables.Drawable? Tint(global::Android.Graphics.Drawables.Drawable? drawable, AColor color)
        {
            if (drawable == null) return null;
            var copy = drawable.Mutate();
            copy.SetTint(color);
            return copy;
        }
    }
}

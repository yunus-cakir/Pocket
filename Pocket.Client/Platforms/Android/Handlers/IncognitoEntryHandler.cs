using Android.Text;
using Android.Views.InputMethods;
using AndroidX.AppCompat.Widget;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Pocket.Client.Controls;

namespace Pocket.Client.Platforms.Android.Handlers
{
    /// <summary>
    /// Enforces Incognito Keyboard mode on Android (BR-301) using ImeFlags.NoPersonalizedLearning
    /// and disabling keyboard suggestions without mutating the global EntryHandler.Mapper per instance.
    /// </summary>
    public class IncognitoEntryHandler : EntryHandler
    {
        public static readonly IPropertyMapper<IncognitoEntry, IncognitoEntryHandler> IncognitoMapper =
            new PropertyMapper<IncognitoEntry, IncognitoEntryHandler>(Mapper)
            {
                [nameof(IncognitoEntry.ReturnType)] = (handler, view) =>
                {
                    MapReturnType(handler, view);
                    ApplyIncognitoFlags(handler.PlatformView);
                    ApplyCursorStyle(handler.PlatformView);
                },
                [nameof(IncognitoEntry.Keyboard)] = (handler, view) =>
                {
                    MapKeyboard(handler, view);
                    ApplyIncognitoFlags(handler.PlatformView);
                    ApplyCursorStyle(handler.PlatformView);
                },
                [nameof(IncognitoEntry.TextColor)] = (handler, view) =>
                {
                    MapTextColor(handler, view);
                    ApplyCursorStyle(handler.PlatformView);
                }
            };

        public IncognitoEntryHandler() : base(IncognitoMapper)
        {
        }

        protected override MauiAppCompatEditText CreatePlatformView()
        {
            var platformView = base.CreatePlatformView();
            platformView.Background = null;
            float density = platformView.Resources?.DisplayMetrics?.Density ?? 2.75f;
            int hPadPx = (int)System.Math.Round(3f * density);
            platformView.SetPadding(hPadPx, 0, hPadPx, 0);
            ApplyIncognitoFlags(platformView);
            ApplyCursorStyle(platformView);
            return platformView;
        }

        protected override void ConnectHandler(MauiAppCompatEditText platformView)
        {
            base.ConnectHandler(platformView);
            ApplyCursorStyle(platformView);
            platformView.FocusChange += OnPlatformViewFocusChange;
        }

        protected override void DisconnectHandler(MauiAppCompatEditText platformView)
        {
            platformView.FocusChange -= OnPlatformViewFocusChange;
            base.DisconnectHandler(platformView);
        }

        private static void OnPlatformViewFocusChange(object? sender, global::Android.Views.View.FocusChangeEventArgs e)
        {
            if (e.HasFocus && sender is AppCompatEditText editText)
            {
                ApplyCursorStyle(editText);
                editText.SetCursorVisible(false);
                editText.SetCursorVisible(true);
            }
        }

        internal static void ApplyCursorStyle(AppCompatEditText? platformView)
        {
            if (platformView == null)
                return;

            platformView.SetCursorVisible(true);

            if (System.OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                float density = platformView.Resources?.DisplayMetrics?.Density ?? 2.75f;
                int widthPx = System.Math.Max(2, (int)System.Math.Round(2.5f * density));
                int heightPx = (int)System.Math.Round(22f * density);
                var cursorDrawable = new global::Android.Graphics.Drawables.GradientDrawable();
                cursorDrawable.SetShape(global::Android.Graphics.Drawables.ShapeType.Rectangle);
                cursorDrawable.SetColor(global::Android.Graphics.Color.ParseColor("#FFB800"));
                cursorDrawable.SetCornerRadius(1.25f * density);
                cursorDrawable.SetSize(widthPx, heightPx);
                platformView.TextCursorDrawable = cursorDrawable;
            }
        }

        internal static void ApplyIncognitoFlags(AppCompatEditText? platformView)
        {
            if (platformView == null)
                return;

            if (System.OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                var targetIme = platformView.ImeOptions | (ImeAction)ImeFlags.NoPersonalizedLearning;
                if (platformView.ImeOptions != targetIme)
                {
                    platformView.ImeOptions = targetIme;
                }
            }

            var targetInputType = platformView.InputType | InputTypes.ClassText | InputTypes.TextFlagNoSuggestions;
            if (platformView.InputType != targetInputType)
            {
                platformView.InputType = targetInputType;
            }
        }
    }
}

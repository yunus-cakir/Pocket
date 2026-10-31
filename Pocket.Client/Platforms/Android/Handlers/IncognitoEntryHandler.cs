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
                },
                [nameof(IncognitoEntry.Keyboard)] = (handler, view) =>
                {
                    MapKeyboard(handler, view);
                    ApplyIncognitoFlags(handler.PlatformView);
                }
            };

        public IncognitoEntryHandler() : base(IncognitoMapper)
        {
        }

        protected override MauiAppCompatEditText CreatePlatformView()
        {
            var platformView = base.CreatePlatformView();
            ApplyIncognitoFlags(platformView);
            return platformView;
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

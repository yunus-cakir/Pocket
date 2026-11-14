using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Pocket.Client.PageModels;
using Pocket.Client.Pages;
using Pocket.Client.Data;

namespace Pocket.Client
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                    fonts.AddFont("SegoeUI-Semibold.ttf", "SegoeSemibold");
                    fonts.AddFont("FluentSystemIcons-Regular.ttf", "FluentUI");
                })
                .ConfigureMauiHandlers(handlers =>
                {
#if ANDROID
                    handlers.AddHandler(typeof(Pocket.Client.Controls.CameraView), typeof(Pocket.Client.Platforms.Android.Handlers.CameraViewHandler));
                    handlers.AddHandler(typeof(Pocket.Client.Controls.CameraSlider), typeof(Pocket.Client.Platforms.Android.Handlers.CameraSliderHandler));
                    handlers.AddHandler(typeof(Pocket.Client.Controls.HorizontalSwipeView), typeof(Pocket.Client.Platforms.Android.Handlers.HorizontalSwipeViewHandler));
                    handlers.AddHandler(typeof(Pocket.Client.Controls.IncognitoEntry), typeof(Pocket.Client.Platforms.Android.Handlers.IncognitoEntryHandler));
                    handlers.AddHandler(typeof(Pocket.Client.Controls.UploadSpinner), typeof(Pocket.Client.Platforms.Android.Handlers.UploadSpinnerHandler));
#elif IOS
                    handlers.AddHandler(typeof(Pocket.Client.Controls.IncognitoEntry), typeof(Pocket.Client.Platforms.iOS.Handlers.IncognitoEntryHandler));
#endif
                });

#if DEBUG
            builder.Logging.AddDebug();
            builder.Services.AddLogging(configure => configure.AddDebug());
#endif
            builder.Services.AddSingleton<LocalDatabase>();
            builder.Services.AddSingleton<Pocket.Client.Services.ICryptoService, Pocket.Client.Services.CryptoService>();
            builder.Services.AddSingleton<Pocket.Client.Services.IRelayService, Pocket.Client.Services.RelayService>();
            builder.Services.AddSingleton<Pocket.Client.Services.IBackupService, Pocket.Client.Services.BackupService>();
            builder.Services.AddSingleton<Pocket.Client.Services.INotificationService, Pocket.Client.Services.NotificationService>();
            // Page Models
            builder.Services.AddTransient<CameraPageModel>();
            builder.Services.AddSingleton<AppShellModel>();

            // Pages
            builder.Services.AddTransient<CameraPage>();
            builder.Services.AddSingleton<AppShell>();

            builder.Services.AddSingleton<FeedPageModel>();
            builder.Services.AddSingleton<FeedPage>();

            builder.Services.AddTransient<DmPageModel>();
            builder.Services.AddTransient<DmPage>();

            builder.Services.AddSingleton<DmListPageModel>();
            builder.Services.AddSingleton<DmListPage>();

            return builder.Build();
        }
    }
}

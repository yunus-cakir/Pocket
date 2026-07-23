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
                })
                .ConfigureMauiHandlers(handlers =>
                {
#if ANDROID
                    handlers.AddHandler(typeof(Pocket.Client.Controls.CameraView), typeof(Pocket.Client.Platforms.Android.Handlers.CameraViewHandler));
#endif
                });

#if DEBUG
            builder.Logging.AddDebug();
            builder.Services.AddLogging(configure => configure.AddDebug());
#endif
            builder.Services.AddSingleton<LocalDatabase>();

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

            return builder.Build();
        }
    }
}

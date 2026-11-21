using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using AndroidX.Core.App;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

namespace Pocket.Client.Services
{
    /// <summary>
    /// Keeps the app process (and with it the relay connection) running while the app is in the
    /// background, so photos and messages arrive when they are sent instead of when the app is
    /// next opened. Android otherwise freezes or kills a backgrounded app within seconds on
    /// MIUI/HyperOS. Shows a silent, low-priority "connected" notification, as Android requires.
    /// Declared in AndroidManifest.xml (foregroundServiceType="specialUse").
    /// </summary>
    [Register("com.yunuscakir.pocket.client.RelayKeepAliveService")]
    public class RelayKeepAliveService : Service
    {
        private const string ChannelId = "pocket_connection_v1";
        private const string ChannelName = "Connection";
        private const int NotificationId = 1001;

        private IRelayService? _relayService;

        /// <summary>
        /// Keeps the CPU awake while a received payload (photo, reaction, message…) is decrypted, saved,
        /// shown on the widget and acknowledged, or while queued sends go out after a reconnect, so a
        /// device that fell back into deep sleep does not stop the work halfway. Released on dispose,
        /// or after <paramref name="timeout"/> at the latest.
        /// </summary>
        public static IDisposable HoldAwake(string tag, TimeSpan timeout)
        {
            try
            {
                if (global::Android.App.Application.Context.GetSystemService(Context.PowerService) is PowerManager power)
                {
                    var wakeLock = power.NewWakeLock(WakeLockFlags.Partial, "Pocket:" + tag);
                    if (wakeLock != null)
                    {
                        wakeLock.SetReferenceCounted(false);
                        wakeLock.Acquire((long)timeout.TotalMilliseconds);
                        return new WakeLockReleaser(wakeLock);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[KeepAlive] Wake lock failed: {ex.Message}");
            }
            return new WakeLockReleaser(null);
        }

        private sealed class WakeLockReleaser : IDisposable
        {
            private PowerManager.WakeLock? _wakeLock;
            public WakeLockReleaser(PowerManager.WakeLock? wakeLock) => _wakeLock = wakeLock;

            public void Dispose()
            {
                var wakeLock = System.Threading.Interlocked.Exchange(ref _wakeLock, null);
                try
                {
                    if (wakeLock?.IsHeld == true)
                        wakeLock.Release();
                }
                catch { /* already released by its timeout */ }
            }
        }

        /// <summary>Starts (or refreshes) the service; safe to call repeatedly.</summary>
        public static void Start(Context context)
        {
            try
            {
                var intent = new Intent(context, typeof(RelayKeepAliveService));
                AndroidX.Core.Content.ContextCompat.StartForegroundService(context, intent);
            }
            catch (Exception ex)
            {
                // e.g. ForegroundServiceStartNotAllowedException when started from the background.
                System.Diagnostics.Debug.WriteLine($"[KeepAlive] Could not start: {ex.Message}");
            }
        }

        public override IBinder? OnBind(Intent? intent) => null;

        public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
        {
            EnsureChannel();
            var notification = BuildNotification(IsConnected());
            if (OperatingSystem.IsAndroidVersionAtLeast(34))
                StartForeground(NotificationId, notification, ForegroundService.TypeSpecialUse);
            else
                StartForeground(NotificationId, notification);

            AttachToRelay();
            return StartCommandResult.Sticky;
        }

        public override void OnDestroy()
        {
            if (_relayService != null)
                _relayService.OnConnectionStateChanged -= HandleConnectionStateChanged;
            base.OnDestroy();
        }

        /// <summary>
        /// When Android restarts only this service (process was killed), resolving the shell model
        /// is what loads the user and opens the connection.
        /// </summary>
        private void AttachToRelay()
        {
            if (_relayService != null)
                return;
            try
            {
                var services = IPlatformApplication.Current?.Services;
                if (services == null)
                    return;
                services.GetService<Pocket.Client.PageModels.AppShellModel>();
                _relayService = services.GetService<IRelayService>();
                if (_relayService != null)
                {
                    _relayService.OnConnectionStateChanged += HandleConnectionStateChanged;
                    UpdateNotification(_relayService.IsConnected);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[KeepAlive] Could not attach to the relay: {ex.Message}");
            }
        }

        private bool IsConnected() => _relayService?.IsConnected == true;

        private void HandleConnectionStateChanged(bool isConnected) => UpdateNotification(isConnected);

        private void UpdateNotification(bool isConnected)
        {
            try
            {
                NotificationManagerCompat.From(this)?.Notify(NotificationId, BuildNotification(isConnected));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[KeepAlive] Could not update the notification: {ex.Message}");
            }
        }

        private Notification BuildNotification(bool isConnected)
        {
            var launchIntent = new Intent(this, typeof(MainActivity));
            launchIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
            var pendingFlags = PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable;
            var pendingIntent = PendingIntent.GetActivity(this, NotificationId, launchIntent, pendingFlags);

            int smallIcon = Resource.Drawable.ic_stat_notification;
            if (smallIcon == 0)
                smallIcon = ApplicationInfo?.Icon ?? Resource.Mipmap.appicon;

            return new NotificationCompat.Builder(this, ChannelId)
                .SetSmallIcon(smallIcon)
                .SetContentTitle("Pocket")
                .SetContentText(isConnected ? "Connected" : "Waiting for connection…")
                .SetContentIntent(pendingIntent)
                .SetOngoing(true)
                .SetOnlyAlertOnce(true)
                .SetSilent(true)
                .SetShowWhen(false)
                .SetPriority(NotificationCompat.PriorityMin)
                .SetCategory(NotificationCompat.CategoryService)
                .SetForegroundServiceBehavior(NotificationCompat.ForegroundServiceImmediate)
                .Build()!;
        }

        private void EnsureChannel()
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(26))
                return;
            var manager = GetSystemService(Context.NotificationService) as NotificationManager;
            if (manager == null || manager.GetNotificationChannel(ChannelId) != null)
                return;

            var channel = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.Min)
            {
                Description = "Keeps Pocket connected so photos and messages arrive right away"
            };
            channel.SetShowBadge(false);
            channel.EnableVibration(false);
            channel.SetSound(null, null);
            manager.CreateNotificationChannel(channel);
        }
    }

    /// <summary>Restarts the keep-alive service after a reboot or an app update.</summary>
    [BroadcastReceiver(Enabled = true, Exported = true)]
    [IntentFilter(new[] { Intent.ActionBootCompleted, Intent.ActionMyPackageReplaced })]
    public class RelayKeepAliveBootReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (context == null)
                return;
            if (intent?.Action == Intent.ActionBootCompleted || intent?.Action == Intent.ActionMyPackageReplaced)
                RelayKeepAliveService.Start(context);
        }
    }
}

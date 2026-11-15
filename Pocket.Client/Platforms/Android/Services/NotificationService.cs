using System;
using System.IO;
using System.Threading;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using AndroidX.Core.App;
using Color = Android.Graphics.Color;
using Paint = Android.Graphics.Paint;
using Rect = Android.Graphics.Rect;
using RectF = Android.Graphics.RectF;

namespace Pocket.Client.Services
{
    public partial class NotificationService
    {
        public const string ChannelId = "pocket_messages_high_v1";
        private const string ChannelName = "Messages and Reactions";
        private const string ChannelDescription = "Notifications for incoming messages and photo reactions";

        public const string ExtraFriendId = "extra_notification_friend_id";
        public const string ExtraIsReaction = "extra_notification_is_reaction";
        public const string ExtraMediaPostId = "extra_notification_media_post_id";

        private static int _nextNotificationId = 2000;

        public static void EnsureChannelInitialized(Context? context = null)
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(26))
                return;

            context ??= Microsoft.Maui.ApplicationModel.Platform.CurrentActivity ?? global::Android.App.Application.Context;
            if (context == null)
                return;

            var manager = context.GetSystemService(Context.NotificationService) as NotificationManager;
            if (manager == null)
                return;

            var existing = manager.GetNotificationChannel(ChannelId);
            if (existing != null)
                return;

            var channel = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.High)
            {
                Description = ChannelDescription,
                LockscreenVisibility = NotificationVisibility.Public
            };
            channel.EnableVibration(true);
            channel.EnableLights(true);
            manager.CreateNotificationChannel(channel);
        }

        partial void ShowPlatformNotification(
            string senderId,
            string header,
            string body,
            string? senderAvatarPath,
            bool isReaction,
            Guid? mediaPostId)
        {
            try
            {
                var context = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity ?? global::Android.App.Application.Context;
                if (context == null)
                    return;

                EnsureChannelInitialized(context);

                var launchIntent = new Intent(context, typeof(MainActivity));
                launchIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
                launchIntent.PutExtra(ExtraFriendId, senderId ?? string.Empty);
                launchIntent.PutExtra(ExtraIsReaction, isReaction);
                if (mediaPostId.HasValue)
                {
                    launchIntent.PutExtra(ExtraMediaPostId, mediaPostId.Value.ToString());
                }

                int notificationId = Interlocked.Increment(ref _nextNotificationId);
                var pendingFlags = PendingIntentFlags.UpdateCurrent;
                if (OperatingSystem.IsAndroidVersionAtLeast(23))
                {
                    pendingFlags |= PendingIntentFlags.Immutable;
                }

                var pendingIntent = PendingIntent.GetActivity(context, notificationId, launchIntent, pendingFlags);

                int smallIcon = Resource.Drawable.ic_stat_notification;
                if (smallIcon == 0)
                {
                    smallIcon = context.ApplicationInfo?.Icon ?? Resource.Mipmap.appicon;
                }

                var style = new NotificationCompat.BigTextStyle();
                style.BigText(body);
                style.SetBigContentTitle(header);

                var builder = new NotificationCompat.Builder(context, ChannelId);
                builder.SetSmallIcon(smallIcon);
                builder.SetContentTitle(header);
                builder.SetContentText(body);
                builder.SetStyle(style);
                builder.SetPriority(NotificationCompat.PriorityHigh);
                builder.SetCategory(NotificationCompat.CategoryMessage);
                builder.SetDefaults(NotificationCompat.DefaultAll);
                builder.SetVisibility(NotificationCompat.VisibilityPublic);
                builder.SetAutoCancel(true);
                builder.SetWhen(Java.Lang.JavaSystem.CurrentTimeMillis());
                builder.SetShowWhen(true);

                if (pendingIntent != null)
                {
                    builder.SetContentIntent(pendingIntent);
                }

                using var avatarBitmap = TryCreateCircularAvatarBitmap(senderAvatarPath);
                if (avatarBitmap != null)
                {
                    builder.SetLargeIcon(avatarBitmap);
                }

                var notification = builder.Build();
                if (notification != null)
                {
                    NotificationManagerCompat.From(context)?.Notify(notificationId, notification);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NotificationService] Failed to show notification: {ex.Message}");
            }
        }

        private static Bitmap? TryCreateCircularAvatarBitmap(string? avatarPath)
        {
            if (string.IsNullOrWhiteSpace(avatarPath) || !File.Exists(avatarPath))
                return null;

            try
            {
                const int targetSize = 128;
                using var source = BitmapFactory.DecodeFile(avatarPath);
                if (source == null || source.Width <= 0 || source.Height <= 0)
                    return null;

                var output = Bitmap.CreateBitmap(targetSize, targetSize, Bitmap.Config.Argb8888!);
                if (output == null)
                    return null;

                using var canvas = new Canvas(output);
                using var paint = new Paint(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);
                float radius = targetSize / 2f;

                int side = Math.Min(source.Width, source.Height);
                var srcRect = new Rect(
                    (source.Width - side) / 2,
                    (source.Height - side) / 2,
                    (source.Width + side) / 2,
                    (source.Height + side) / 2);
                var dstRect = new RectF(0, 0, targetSize, targetSize);

                int layer = canvas.SaveLayer(dstRect, null);
                paint.Color = Color.White;
                canvas.DrawCircle(radius, radius, radius, paint);
                using (var xfer = new PorterDuffXfermode(PorterDuff.Mode.SrcIn!))
                {
                    paint.SetXfermode(xfer);
                    canvas.DrawBitmap(source, srcRect, dstRect, paint);
                    paint.SetXfermode(null);
                }
                canvas.RestoreToCount(layer);

                return output;
            }
            catch
            {
                return null;
            }
        }
    }
}

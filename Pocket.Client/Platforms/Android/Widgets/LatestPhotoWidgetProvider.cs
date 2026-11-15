using System;
using System.IO;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using Color = Android.Graphics.Color;
using Paint = Android.Graphics.Paint;
using Path = Android.Graphics.Path;
using Rect = Android.Graphics.Rect;
using RectF = Android.Graphics.RectF;

namespace Pocket.Client.Platforms.Android.Widgets
{
    /// <summary>
    /// Home-screen widget that shows the most recent photo received from a friend in a strict 1:1 square,
    /// centered inside the widget bounds, with the sender's avatar and single-line caption rendered
    /// directly inside the bottom-left of the 1:1 photo.
    /// </summary>
    [BroadcastReceiver(
        Name = "com.companyname.pocket.client.LatestPhotoWidgetProvider",
        Label = "Pocket - Latest Photo",
        Exported = true)]
    [IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate })]
    [MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/latest_photo_widget_info")]
    public class LatestPhotoWidgetProvider : AppWidgetProvider
    {
        private const string PrefsName = "pocket_latest_photo_widget";
        private const string KeyPostId = "post_id";
        private const string KeyPhotoPath = "photo_path";
        private const string KeyAvatarPath = "avatar_path";
        private const string KeyCaption = "caption";
        private const string KeySenderName = "sender_name";
        private const string KeyUnseenCount = "unseen_count";

        private const int PhotoSquarePx = 720;
        private const float PhotoCornerRadiusRatio = 0.14f;

        public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
        {
            if (context == null || appWidgetManager == null || appWidgetIds == null)
                return;

            var views = BuildViews(context);
            foreach (var id in appWidgetIds)
            {
                appWidgetManager.UpdateAppWidget(id, views);
            }
        }

        public override void OnAppWidgetOptionsChanged(Context? context, AppWidgetManager? appWidgetManager,
            int appWidgetId, Bundle? newOptions)
        {
            base.OnAppWidgetOptionsChanged(context, appWidgetManager, appWidgetId, newOptions);
            if (context == null || appWidgetManager == null)
                return;

            var views = BuildViews(context);
            appWidgetManager.UpdateAppWidget(appWidgetId, views);
        }

        /// <summary>
        /// Persists the latest photo info and refreshes every placed widget instance.
        /// Safe to call from any thread.
        /// </summary>
        public static void Publish(Guid postId, string photoPath, string? avatarPath, string? caption,
            string? senderName, int unseenCount = 0)
        {
            var context = global::Android.App.Application.Context;
            if (context == null)
                return;

            var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            var editor = prefs?.Edit();
            if (editor == null)
                return;

            editor.PutString(KeyPostId, postId.ToString());
            editor.PutString(KeyPhotoPath, photoPath ?? string.Empty);
            editor.PutString(KeyAvatarPath, avatarPath ?? string.Empty);
            editor.PutString(KeyCaption, caption ?? string.Empty);
            editor.PutString(KeySenderName, senderName ?? string.Empty);
            editor.PutInt(KeyUnseenCount, Math.Max(0, unseenCount));
            editor.Apply();

            RefreshAll(context);
        }

        public static void Clear()
        {
            var context = global::Android.App.Application.Context;
            if (context == null)
                return;

            var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            var editor = prefs?.Edit();
            if (editor == null)
                return;

            editor.Clear();
            editor.Apply();

            RefreshAll(context);
        }

        /// <summary>
        /// Returns the id of the post currently shown on the widget (if any).
        /// </summary>
        public static string? GetCurrentPostId()
        {
            var context = global::Android.App.Application.Context;
            var prefs = context?.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            return prefs?.GetString(KeyPostId, null);
        }

        /// <summary>
        /// Asks the launcher to pin this widget to the home screen (system "Add to home screen?" dialog).
        /// Returns false when the device/launcher does not support pinning (API &lt; 26 or unsupported launcher).
        /// </summary>
        public static bool RequestPin()
        {
            try
            {
                var context = global::Android.App.Application.Context;
                if (context == null || !OperatingSystem.IsAndroidVersionAtLeast(26))
                    return false;

                var manager = AppWidgetManager.GetInstance(context);
                if (manager == null || !manager.IsRequestPinAppWidgetSupported)
                    return false;

                var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(LatestPhotoWidgetProvider)));
                return manager.RequestPinAppWidget(component, null, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Widget] Pin request failed: {ex.Message}");
                return false;
            }
        }

        public static void RefreshAll(Context context)
        {
            try
            {
                var manager = AppWidgetManager.GetInstance(context);
                if (manager == null)
                    return;

                var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(LatestPhotoWidgetProvider)));
                var ids = manager.GetAppWidgetIds(component);
                if (ids == null || ids.Length == 0)
                    return;

                var views = BuildViews(context);
                foreach (var id in ids)
                {
                    manager.UpdateAppWidget(id, views);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Widget] Refresh failed: {ex.Message}");
            }
        }

        private static RemoteViews BuildViews(Context context)
        {
            var views = new RemoteViews(context.PackageName, Resource.Layout.widget_latest_photo);

            var prefs = context.GetSharedPreferences(PrefsName, FileCreationMode.Private);
            var photoPath = prefs?.GetString(KeyPhotoPath, string.Empty) ?? string.Empty;
            var avatarPath = prefs?.GetString(KeyAvatarPath, string.Empty) ?? string.Empty;
            var caption = prefs?.GetString(KeyCaption, string.Empty) ?? string.Empty;
            var senderName = prefs?.GetString(KeySenderName, string.Empty) ?? string.Empty;
            var unseenCount = prefs?.GetInt(KeyUnseenCount, 0) ?? 0;

            var composite =
                RenderCompositeSquareBitmap(context, photoPath, avatarPath, caption, senderName, unseenCount);
            views.SetImageViewBitmap(Resource.Id.widget_photo, composite);
            views.SetViewVisibility(Resource.Id.widget_photo, ViewStates.Visible);

            // Tap opens the app
            var launchIntent = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!)
                               ?? new Intent(context, typeof(MainActivity));
            launchIntent.SetFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
            var flags = PendingIntentFlags.UpdateCurrent;
            if (OperatingSystem.IsAndroidVersionAtLeast(23))
                flags |= PendingIntentFlags.Immutable;
            var pending = PendingIntent.GetActivity(context, 0, launchIntent, flags);
            views.SetOnClickPendingIntent(Resource.Id.widget_photo, pending);

            return views;
        }

        private static Bitmap RenderCompositeSquareBitmap(
            Context context,
            string photoPath,
            string avatarPath,
            string caption,
            string senderName,
            int unseenCount)
        {
            var output = Bitmap.CreateBitmap(PhotoSquarePx, PhotoSquarePx, Bitmap.Config.Argb8888!)!;
            using var canvas = new Canvas(output);
            using var paint = new Paint(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);
            var fullRect = new RectF(0, 0, PhotoSquarePx, PhotoSquarePx);
            float cornerRadius = PhotoSquarePx * PhotoCornerRadiusRatio;

            Bitmap? photoSource = null;
            if (!string.IsNullOrWhiteSpace(photoPath) && File.Exists(photoPath))
            {
                try
                {
                    photoSource = DecodeSampled(photoPath, PhotoSquarePx);
                    if (photoSource != null)
                        photoSource = ApplyExifRotation(photoPath, photoSource);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Widget] Photo load failed: {ex.Message}");
                    photoSource = null;
                }
            }

            if (photoSource == null)
            {
                // Empty state: rounded 1:1 dark square with centered placeholder text
                paint.Color = Color.ParseColor("#FF1C1C1E");
                canvas.DrawRoundRect(fullRect, cornerRadius, cornerRadius, paint);

                var emptyText = context.GetString(Resource.String.latest_photo_widget_empty);
                using var emptyTextPaint = new TextPaint(PaintFlags.AntiAlias)
                {
                    Color = Color.ParseColor("#B3FFFFFF"),
                    TextSize = PhotoSquarePx * 0.068f,
                    TextAlign = Paint.Align.Center
                };
                var fm = emptyTextPaint.GetFontMetrics();
                float baseline = (PhotoSquarePx / 2f) - ((fm!.Ascent + fm.Descent) / 2f);
                canvas.DrawText(emptyText, PhotoSquarePx / 2f, baseline, emptyTextPaint);
                return output;
            }

            // Clip the entire 1:1 square to rounded corners so photo + footer stay strictly inside the square
            int clipSave = canvas.Save();
            using (var clipPath = new Path())
            {
                clipPath.AddRoundRect(fullRect, cornerRadius, cornerRadius, Path.Direction.Cw!);
                canvas.ClipPath(clipPath);
            }

            // Center-crop the source photo into the 1:1 square
            int side = Math.Min(photoSource.Width, photoSource.Height);
            var srcRect = new Rect(
                (photoSource.Width - side) / 2,
                (photoSource.Height - side) / 2,
                (photoSource.Width + side) / 2,
                (photoSource.Height + side) / 2);
            canvas.DrawBitmap(photoSource, srcRect, fullRect, paint);
            photoSource.Recycle();

            // Draw bottom-left avatar + single-line ellipsized caption pill strictly inside the 1:1 photo bounds
            float margin = PhotoSquarePx * 0.048f; // ~34.5px on 720px canvas
            float avatarSize = PhotoSquarePx * 0.145f; // ~104px diameter
            float avatarLeft = margin;
            float avatarTop = PhotoSquarePx - margin - avatarSize;

            using (var avatarBmp = CreateAvatar((int)Math.Round(avatarSize), avatarPath, senderName))
            {
                var avatarDst = new RectF(avatarLeft, avatarTop, avatarLeft + avatarSize, avatarTop + avatarSize);
                canvas.DrawBitmap(avatarBmp, null, avatarDst, paint);
            }

            var trimmedCaption = (caption ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Trim();
            if (!string.IsNullOrEmpty(trimmedCaption))
            {
                float gap = PhotoSquarePx * 0.024f;
                float pillLeft = avatarLeft + avatarSize + gap;
                float pillMaxRight = PhotoSquarePx - margin;
                float maxPillWidth = pillMaxRight - pillLeft;

                float padH = PhotoSquarePx * 0.042f;
                float padV = PhotoSquarePx * 0.020f;
                float maxTextWidth = Math.Max(0f, maxPillWidth - (2f * padH));

                if (maxTextWidth > 10f)
                {
                    using var textPaint = new TextPaint(PaintFlags.AntiAlias)
                    {
                        Color = Color.White,
                        TextSize = PhotoSquarePx * 0.064f
                    };
                    var captionTypeface = OperatingSystem.IsAndroidVersionAtLeast(28)
                        ? Typeface.Create(Typeface.Default, 550, false)
                        : Typeface.Create("sans-serif-medium", TypefaceStyle.Normal);
                    textPaint.SetTypeface(captionTypeface);

                    string ellipsized = TextUtils.Ellipsize(
                        trimmedCaption,
                        textPaint,
                        maxTextWidth,
                        TextUtils.TruncateAt.End)?.ToString() ?? trimmedCaption;

                    float measuredWidth = textPaint.MeasureText(ellipsized);
                    float pillWidth = Math.Min(maxPillWidth, measuredWidth + (2f * padH));

                    var fm = textPaint.GetFontMetrics()!;
                    float textHeight = fm.Descent - fm.Ascent;
                    float pillHeight = textHeight + (2f * padV);

                    float avatarCenterY = avatarTop + (avatarSize / 2f);
                    float pillTop = avatarCenterY - (pillHeight / 2f);
                    float pillBottom = pillTop + pillHeight;
                    var pillRect = new RectF(pillLeft, pillTop, pillLeft + pillWidth, pillBottom);

                    using (var pillPaint = new Paint(PaintFlags.AntiAlias))
                    {
                        pillPaint.Color = Color.ParseColor("#73000000");
                        float pillCorner = pillHeight / 2f;
                        canvas.DrawRoundRect(pillRect, pillCorner, pillCorner, pillPaint);
                    }

                    float textX = pillLeft + padH;
                    float textBaseline = avatarCenterY - ((fm.Ascent + fm.Descent) / 2f);
                    canvas.DrawText(ellipsized, textX, textBaseline, textPaint);
                }
            }

            // Draw top-right circular badge for unseen incoming photo count
            if (unseenCount > 0)
            {
                string countText = unseenCount.ToString();
                float badgeHeight = PhotoSquarePx * 0.145f; // ~104px diameter on 720px canvas
                using var badgeTextPaint = new TextPaint(PaintFlags.AntiAlias)
                {
                    Color = Color.ParseColor("#141414"),
                    TextSize = badgeHeight * 0.50f,
                    TextAlign = Paint.Align.Center
                };
                badgeTextPaint.SetTypeface(Typeface.DefaultBold);

                float measuredCountWidth = badgeTextPaint.MeasureText(countText);
                float badgeWidth = countText.Length <= 1
                    ? badgeHeight
                    : Math.Max(badgeHeight, measuredCountWidth + (badgeHeight * 0.52f));

                float badgeRight = PhotoSquarePx - margin;
                float badgeTop = margin;
                float badgeLeft = badgeRight - badgeWidth;
                float badgeBottom = badgeTop + badgeHeight;
                var outerBadgeRect = new RectF(badgeLeft, badgeTop, badgeRight, badgeBottom);
                float outerCorner = badgeHeight / 2f;

                using (var badgePaint = new Paint(PaintFlags.AntiAlias))
                {
                    var accentColor = Color.ParseColor(
                        Pocket.Client.PageModels.AppShellModel.GetSavedAccentColorHex());

                    // Outer rim
                    badgePaint.Color = accentColor;
                    canvas.DrawRoundRect(outerBadgeRect, outerCorner, outerCorner, badgePaint);

                    // Inner fill
                    float rimInset = badgeHeight * 0.055f;
                    var innerBadgeRect = new RectF(
                        badgeLeft + rimInset,
                        badgeTop + rimInset,
                        badgeRight - rimInset,
                        badgeBottom - rimInset);
                    float innerCorner = (badgeHeight - (2f * rimInset)) / 2f;
                    badgePaint.Color = accentColor;
                    canvas.DrawRoundRect(innerBadgeRect, innerCorner, innerCorner, badgePaint);
                }

                var badgeFm = badgeTextPaint.GetFontMetrics()!;
                float badgeCenterX = (badgeLeft + badgeRight) / 2f;
                float badgeCenterY = (badgeTop + badgeBottom) / 2f;
                float badgeBaseline = badgeCenterY - ((badgeFm.Ascent + badgeFm.Descent) / 2f);
                canvas.DrawText(countText, badgeCenterX, badgeBaseline, badgeTextPaint);
            }

            canvas.RestoreToCount(clipSave);
            return output;
        }

        private static Bitmap CreateAvatar(int avatarPx, string avatarPath, string senderName)
        {
            var output = Bitmap.CreateBitmap(avatarPx, avatarPx, Bitmap.Config.Argb8888!)!;
            using var canvas = new Canvas(output);
            using var paint = new Paint(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);
            float radius = avatarPx / 2f;

            Bitmap? source = null;
            if (!string.IsNullOrWhiteSpace(avatarPath) && File.Exists(avatarPath))
            {
                try
                {
                    source = DecodeSampled(avatarPath, avatarPx * 2);
                    if (source != null)
                        source = ApplyExifRotation(avatarPath, source);
                }
                catch
                {
                    source = null;
                }
            }

            if (source != null)
            {
                // Center-crop the source into the circle without border
                int side = Math.Min(source.Width, source.Height);
                var srcRect = new Rect((source.Width - side) / 2, (source.Height - side) / 2,
                    (source.Width + side) / 2, (source.Height + side) / 2);
                var dstRect = new RectF(0, 0, avatarPx, avatarPx);

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
                source.Recycle();
            }
            else
            {
                // Fallback: initial letter on dark circle
                paint.Color = Color.ParseColor("#3A3A3C");
                canvas.DrawCircle(radius, radius, radius, paint);

                var initial = string.IsNullOrWhiteSpace(senderName)
                    ? "?"
                    : senderName.Trim().Substring(0, 1).ToUpperInvariant();
                paint.Color = Color.White;
                paint.TextSize = avatarPx * 0.45f;
                paint.TextAlign = Paint.Align.Center;
                paint.SetTypeface(Typeface.DefaultBold);
                var fm = paint.GetFontMetrics();
                float baseline = radius - (fm!.Ascent + fm.Descent) / 2f;
                canvas.DrawText(initial, radius, baseline, paint);
            }

            return output;
        }

        private static Bitmap? DecodeSampled(string path, int reqMax)
        {
            var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
            BitmapFactory.DecodeFile(path, bounds);
            if (bounds.OutWidth <= 0 || bounds.OutHeight <= 0)
                return null;

            int sample = 1;
            int longest = Math.Max(bounds.OutWidth, bounds.OutHeight);
            while (longest / (sample * 2) >= reqMax)
                sample *= 2;

            var opts = new BitmapFactory.Options { InSampleSize = sample, InPreferredConfig = Bitmap.Config.Argb8888 };
            return BitmapFactory.DecodeFile(path, opts);
        }

        private static Bitmap ApplyExifRotation(string path, Bitmap bitmap)
        {
            try
            {
                // EXIF orientation values: 1=normal, 2=flip horizontal, 3=rotate 180, 6=rotate 90, 8=rotate 270
                using var exif = new global::Android.Media.ExifInterface(path);
                int orientation = exif.GetAttributeInt("Orientation", 1);
                float degrees = orientation switch
                {
                    6 => 90f,
                    3 => 180f,
                    8 => 270f,
                    _ => 0f
                };
                bool flip = orientation == 2;
                if (degrees == 0f && !flip)
                    return bitmap;

                var matrix = new Matrix();
                if (degrees != 0f)
                    matrix.PostRotate(degrees);
                if (flip)
                    matrix.PostScale(-1f, 1f);

                var rotated = Bitmap.CreateBitmap(bitmap, 0, 0, bitmap.Width, bitmap.Height, matrix, true);
                if (rotated != null && rotated != bitmap)
                {
                    bitmap.Recycle();
                    return rotated;
                }
            }
            catch
            {
                // ignore EXIF failures
            }

            return bitmap;
        }
    }
}

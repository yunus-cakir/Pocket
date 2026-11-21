using System;
using Android.App;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;
using Color = Android.Graphics.Color;
using Rect = Android.Graphics.Rect;
using View = Android.Views.View;

namespace Pocket.Client.Services
{
    /// <summary>
    /// Measures the on-screen keyboard on Android 10 and older (API &lt; 30).
    /// <para>
    /// The activity runs with <c>SOFT_INPUT_ADJUST_NOTHING</c> so the pages can move their input bars
    /// themselves. On API 30+ the window still receives IME insets and animation callbacks in that mode,
    /// but on API 29 and older it gets nothing: no IME insets, no inset animation, and
    /// <c>getWindowVisibleDisplayFrame</c> ignores the keyboard (e.g. Galaxy Note 9 on Android 10).
    /// </para>
    /// <para>
    /// The workaround is an invisible, zero-width, full-height <see cref="PopupWindow"/> in
    /// <c>ADJUST_RESIZE</c> mode that sits behind the keyboard. Its visible display frame shrinks when the
    /// keyboard opens, which gives the keyboard's top edge in screen coordinates.
    /// </para>
    /// </summary>
    public static class LegacyImeHeightTracker
    {
        private static PopupWindow? _popup;
        private static View? _probe;
        private static ProbeLayoutListener? _listener;
        private static WeakReference<Activity>? _activity;

        /// <summary>True on Android versions where IME insets are unavailable with ADJUST_NOTHING.</summary>
        public static bool IsNeeded => Build.VERSION.SdkInt < BuildVersionCodes.R;

        /// <summary>Raised on the UI thread whenever the measuring window is laid out again (keyboard moved).</summary>
        public static event Action? Changed;

        /// <summary>Shows the measuring window for <paramref name="activity"/> if it is not already showing.</summary>
        public static void EnsureAttached(Activity? activity)
        {
            if (!IsNeeded || activity == null || activity.IsFinishing)
                return;

            if (_popup != null && _activity != null && _activity.TryGetTarget(out var current) &&
                ReferenceEquals(current, activity))
            {
                if (!_popup.IsShowing)
                    TryShow(activity);
                return;
            }

            Detach();

            var probe = new View(activity);
            var popup = new PopupWindow(probe, 0, ViewGroup.LayoutParams.MatchParent, false)
            {
                SoftInputMode = SoftInput.AdjustResize | SoftInput.StateUnchanged,
                InputMethodMode = global::Android.Widget.InputMethod.Needed,
                Touchable = false,
                OutsideTouchable = false,
            };
            popup.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));

            _listener = new ProbeLayoutListener();
            probe.ViewTreeObserver?.AddOnGlobalLayoutListener(_listener);

            _probe = probe;
            _popup = popup;
            _activity = new WeakReference<Activity>(activity);

            TryShow(activity);
        }

        private static void TryShow(Activity activity)
        {
            var decorView = activity.Window?.DecorView;
            if (decorView == null)
                return;

            void Show()
            {
                if (_popup == null || _popup.IsShowing || activity.IsFinishing || decorView.WindowToken == null)
                    return;

                try
                {
                    _popup.ShowAtLocation(decorView, GravityFlags.NoGravity, 0, 0);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LegacyImeHeightTracker] Show failed: {ex.Message}");
                }
            }

            if (decorView.WindowToken != null)
                Show();
            else
                decorView.Post(Show);
        }

        /// <summary>Dismisses the measuring window (call when the activity is destroyed).</summary>
        public static void Detach()
        {
            try
            {
                if (_probe != null && _listener != null)
                    _probe.ViewTreeObserver?.RemoveOnGlobalLayoutListener(_listener);
                if (_popup?.IsShowing == true)
                    _popup.Dismiss();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LegacyImeHeightTracker] Detach failed: {ex.Message}");
            }

            _popup = null;
            _probe = null;
            _listener = null;
            _activity = null;
        }

        /// <summary>
        /// Distance in pixels from the bottom of <paramref name="decorView"/> up to the top of the keyboard,
        /// measured the same way as <c>decorView.Height - visibleFrame.Bottom</c> (so it includes the
        /// navigation bar when the keyboard is closed). Returns 0 when unavailable.
        /// </summary>
        public static int GetRawKeyboardPx(View decorView)
        {
            if (!IsNeeded || _popup == null || !_popup.IsShowing || _probe == null)
                return 0;

            var rect = new Rect();
            _probe.GetWindowVisibleDisplayFrame(rect);
            if (rect.Bottom <= 0)
                return 0;

            var location = new int[2];
            decorView.GetLocationOnScreen(location);
            int decorBottomOnScreen = location[1] + decorView.Height;
            return Math.Max(0, decorBottomOnScreen - rect.Bottom);
        }

        private sealed class ProbeLayoutListener : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
        {
            public void OnGlobalLayout()
            {
                Changed?.Invoke();
            }
        }
    }
}

using Android.Content;
using Android.Views;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Platform;

namespace Pocket.Client.Platforms.Android.Views
{
    /// <summary>
    /// A ContentViewGroup subclass that overrides OnInterceptTouchEvent to only
    /// intercept horizontal swipe gestures. Taps and vertical scrolls pass through
    /// to child views (Button, RecyclerView/CollectionView) unmodified.
    /// 
    /// This follows the same conditional-interception pattern used by Android's
    /// ViewPager and DrawerLayout.
    /// 
    /// Touch flow:
    ///   ACTION_DOWN  → NOT intercepted → children receive DOWN (tap/scroll can start)
    ///   ACTION_MOVE  → if |dx| > touchSlop AND |dx| > |dy| → INTERCEPT (horizontal pan)
    ///                → otherwise → NOT intercepted (vertical scroll / no-op)
    ///   ACTION_UP    → if not intercepted → children receive UP (Button click fires)
    /// </summary>
    public class SwipeInterceptLayout : ContentViewGroup
    {
        private float _startX;
        private float _startY;
        private float _interceptX;
        private bool _isDecided;
        private bool _isHorizontalSwipe;
        private readonly int _touchSlop;

        /// <summary>
        /// Callback to forward horizontal pan events to the cross-platform layer.
        /// Parameters: (GestureStatus status, double totalX, double totalY)
        /// </summary>
        public Action<GestureStatus, double, double>? PanCallback { get; set; }

        public SwipeInterceptLayout(Context context) : base(context)
        {
            _touchSlop = ViewConfiguration.Get(context)?.ScaledTouchSlop ?? 24;
        }

        public override bool OnInterceptTouchEvent(MotionEvent? ev)
        {
            if (ev == null) return false;

            switch (ev.ActionMasked)
            {
                case MotionEventActions.Down:
                    _startX = ev.GetX();
                    _startY = ev.GetY();
                    _isDecided = false;
                    _isHorizontalSwipe = false;
                    // Do NOT intercept DOWN — children must receive it for taps and scroll starts.
                    return false;

                case MotionEventActions.Move:
                    // If we already decided, return the cached decision.
                    if (_isDecided) return _isHorizontalSwipe;

                    float dx = System.Math.Abs(ev.GetX() - _startX);
                    float dy = System.Math.Abs(ev.GetY() - _startY);

                    if (dx > _touchSlop || dy > _touchSlop)
                    {
                        _isDecided = true;
                        _isHorizontalSwipe = dx > dy;

                        if (_isHorizontalSwipe)
                        {
                            // Record the intercept position. TotalX during Running events
                            // will be measured relative to this point so the first frame
                            // starts at 0 (no visual jump).
                            _interceptX = ev.GetX();

                            // Prevent any ancestor ViewGroup from also intercepting.
                            Parent?.RequestDisallowInterceptTouchEvent(true);

                            // Notify the cross-platform layer that a horizontal pan has started.
                            PanCallback?.Invoke(GestureStatus.Started, 0, 0);
                        }

                        return _isHorizontalSwipe;
                    }
                    // Still inside the touch-slop deadzone — don't intercept yet.
                    return false;

                case MotionEventActions.Up:
                case MotionEventActions.Cancel:
                    _isDecided = false;
                    _isHorizontalSwipe = false;
                    return false;

                default:
                    return false;
            }
        }

        public override bool OnTouchEvent(MotionEvent? ev)
        {
            // This method is called in two scenarios:
            //   1. After OnInterceptTouchEvent returned true (horizontal swipe in progress).
            //   2. No child handled ACTION_DOWN (e.g., user touched empty space).
            if (ev == null) return false;

            switch (ev.ActionMasked)
            {
                case MotionEventActions.Down:
                    // Scenario 2: no child handled DOWN. Accept it so we can
                    // track subsequent MOVE events for potential horizontal pan.
                    _startX = ev.GetX();
                    _startY = ev.GetY();
                    _isDecided = false;
                    _isHorizontalSwipe = false;
                    return true;

                case MotionEventActions.Move:
                    // If direction not yet decided (scenario 2), do inline detection.
                    if (!_isDecided)
                    {
                        float adx = System.Math.Abs(ev.GetX() - _startX);
                        float ady = System.Math.Abs(ev.GetY() - _startY);
                        if (adx > _touchSlop || ady > _touchSlop)
                        {
                            _isDecided = true;
                            _isHorizontalSwipe = adx > ady;
                            if (_isHorizontalSwipe)
                            {
                                _interceptX = ev.GetX();
                                PanCallback?.Invoke(GestureStatus.Started, 0, 0);
                            }
                            else
                            {
                                return false; // Vertical — stop receiving events.
                            }
                        }
                        return true; // Still in deadzone — keep tracking.
                    }

                    if (_isHorizontalSwipe)
                    {
                        double totalX = ev.GetX() - _interceptX;
                        PanCallback?.Invoke(GestureStatus.Running, totalX, 0);
                        return true;
                    }
                    return false;

                case MotionEventActions.Up:
                    if (_isHorizontalSwipe)
                    {
                        double totalX = ev.GetX() - _interceptX;
                        PanCallback?.Invoke(GestureStatus.Completed, totalX, 0);
                    }
                    _isDecided = false;
                    _isHorizontalSwipe = false;
                    return true;

                case MotionEventActions.Cancel:
                    if (_isHorizontalSwipe)
                    {
                        PanCallback?.Invoke(GestureStatus.Canceled, ev.GetX() - _interceptX, 0);
                    }
                    _isDecided = false;
                    _isHorizontalSwipe = false;
                    return true;
            }

            return base.OnTouchEvent(ev);
        }
    }
}

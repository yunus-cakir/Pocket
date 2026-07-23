using Microsoft.Maui.Controls;

namespace Pocket.Client.Controls
{
    /// <summary>
    /// Event args for horizontal swipe pan gestures raised by <see cref="HorizontalSwipeView"/>.
    /// TotalX/TotalY are relative to the point where horizontal movement was first detected.
    /// </summary>
    public class SwipePanEventArgs : EventArgs
    {
        public GestureStatus StatusType { get; }
        public double TotalX { get; }
        public double TotalY { get; }

        public SwipePanEventArgs(GestureStatus statusType, double totalX, double totalY)
        {
            StatusType = statusType;
            TotalX = totalX;
            TotalY = totalY;
        }
    }

    /// <summary>
    /// A ContentView that detects horizontal swipe gestures at the Android platform level
    /// using onInterceptTouchEvent, allowing child views (buttons, scroll views) to receive
    /// taps and vertical scrolls without interference.
    /// 
    /// On non-Android platforms, this behaves as a standard ContentView (no gesture handling).
    /// </summary>
    public class HorizontalSwipeView : ContentView
    {
        /// <summary>
        /// Raised when a horizontal pan gesture is detected, updated, or completed.
        /// Only horizontal swipes trigger this event; taps and vertical gestures are
        /// passed through to child views.
        /// </summary>
        public event EventHandler<SwipePanEventArgs>? SwipePanUpdated;

        /// <summary>
        /// Called by the platform handler to raise the <see cref="SwipePanUpdated"/> event.
        /// </summary>
        internal void RaiseSwipePanUpdated(GestureStatus status, double totalX, double totalY)
        {
            SwipePanUpdated?.Invoke(this, new SwipePanEventArgs(status, totalX, totalY));
        }
    }
}

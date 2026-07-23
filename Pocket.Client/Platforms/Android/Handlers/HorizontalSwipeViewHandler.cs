using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Pocket.Client.Controls;
using Pocket.Client.Platforms.Android.Views;

namespace Pocket.Client.Platforms.Android.Handlers
{
    /// <summary>
    /// Handler that maps <see cref="HorizontalSwipeView"/> to <see cref="SwipeInterceptLayout"/>
    /// on Android. The custom platform view overrides onInterceptTouchEvent for conditional
    /// horizontal-only interception, while the handler wires the native pan callback to the
    /// cross-platform <see cref="HorizontalSwipeView.SwipePanUpdated"/> event.
    /// </summary>
    public class HorizontalSwipeViewHandler : ContentViewHandler
    {
        public HorizontalSwipeViewHandler() : base(Mapper, CommandMapper)
        {
        }

        protected override ContentViewGroup CreatePlatformView()
        {
            return new SwipeInterceptLayout(Context);
        }

        protected override void ConnectHandler(ContentViewGroup platformView)
        {
            base.ConnectHandler(platformView);

            if (platformView is SwipeInterceptLayout swipeLayout)
            {
                // Wire cross-platform layout so OnMeasure/OnLayout delegate correctly.
                if (VirtualView is Microsoft.Maui.ICrossPlatformLayout crossPlatformLayout)
                {
                    swipeLayout.CrossPlatformLayout = crossPlatformLayout;
                }

                // Wire the native pan callback to the cross-platform event.
                if (VirtualView is HorizontalSwipeView swipeView)
                {
                    swipeLayout.PanCallback = (status, totalX, totalY) =>
                    {
                        swipeView.RaiseSwipePanUpdated(status, totalX, totalY);
                    };
                }
            }
        }

        protected override void DisconnectHandler(ContentViewGroup platformView)
        {
            if (platformView is SwipeInterceptLayout swipeLayout)
            {
                swipeLayout.PanCallback = null;
            }

            base.DisconnectHandler(platformView);
        }
    }
}

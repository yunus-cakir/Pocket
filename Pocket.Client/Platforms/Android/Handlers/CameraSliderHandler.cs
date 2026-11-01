using Microsoft.Maui.Handlers;
using Pocket.Client.Controls;
using Pocket.Client.Platforms.Android.Views;

namespace Pocket.Client.Platforms.Android.Handlers
{
    public class CameraSliderHandler : ViewHandler<CameraSlider, CameraSliderView>
    {
        private bool _isUpdatingFromNative;

        public static IPropertyMapper<CameraSlider, CameraSliderHandler> PropertyMapper =
            new PropertyMapper<CameraSlider, CameraSliderHandler>(ViewHandler.ViewMapper)
            {
                [nameof(CameraSlider.Minimum)] = (h, v) => { h.PlatformView.Minimum = (float)v.Minimum; h.PlatformView.Invalidate(); },
                [nameof(CameraSlider.Maximum)] = (h, v) => { h.PlatformView.Maximum = (float)v.Maximum; h.PlatformView.Invalidate(); },
                [nameof(CameraSlider.IsVertical)] = (h, v) => { h.PlatformView.IsVertical = v.IsVertical; h.PlatformView.Invalidate(); },
                [nameof(CameraSlider.ShowCenterTick)] = (h, v) => { h.PlatformView.ShowCenterTick = v.ShowCenterTick; h.PlatformView.Invalidate(); },
                [nameof(CameraSlider.Value)] = MapValue,
            };

        public CameraSliderHandler() : base(PropertyMapper)
        {
        }

        protected override CameraSliderView CreatePlatformView() => new CameraSliderView(Context);

        protected override void ConnectHandler(CameraSliderView platformView)
        {
            base.ConnectHandler(platformView);
            platformView.ValueChangedByUser = OnNativeValueChanged;
            platformView.DragStarted = () => VirtualView?.RaiseDragStarted();
            platformView.DragCompleted = () => VirtualView?.RaiseDragCompleted();
        }

        protected override void DisconnectHandler(CameraSliderView platformView)
        {
            platformView.ValueChangedByUser = null;
            platformView.DragStarted = null;
            platformView.DragCompleted = null;
            base.DisconnectHandler(platformView);
        }

        private void OnNativeValueChanged(float value)
        {
            if (VirtualView == null) return;
            _isUpdatingFromNative = true;
            try { VirtualView.Value = value; }
            finally { _isUpdatingFromNative = false; }
        }

        private static void MapValue(CameraSliderHandler handler, CameraSlider view)
        {
            if (handler._isUpdatingFromNative) return;
            handler.PlatformView.Value = (float)view.Value;
            handler.PlatformView.Invalidate();
        }
    }
}


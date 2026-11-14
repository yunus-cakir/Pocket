using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Pocket.Client.Controls;
using Pocket.Client.Platforms.Android.Views;

namespace Pocket.Client.Platforms.Android.Handlers
{
    public class UploadSpinnerHandler : ViewHandler<UploadSpinner, UploadSpinnerView>
    {
        public static IPropertyMapper<UploadSpinner, UploadSpinnerHandler> PropertyMapper =
            new PropertyMapper<UploadSpinner, UploadSpinnerHandler>(ViewHandler.ViewMapper)
            {
                [nameof(UploadSpinner.IsRunning)] = MapIsRunning,
                [nameof(UploadSpinner.Color)] = MapColor,
                [nameof(UploadSpinner.StrokeThickness)] = MapStrokeThickness,
            };

        public UploadSpinnerHandler() : base(PropertyMapper)
        {
        }

        protected override UploadSpinnerView CreatePlatformView() => new UploadSpinnerView(Context);

        private static void MapIsRunning(UploadSpinnerHandler handler, UploadSpinner view)
        {
            handler.PlatformView.IsRunning = view.IsRunning;
        }

        private static void MapColor(UploadSpinnerHandler handler, UploadSpinner view)
        {
            if (view.Color != null)
            {
                handler.PlatformView.SetArcColor(view.Color.ToPlatform());
            }
        }

        private static void MapStrokeThickness(UploadSpinnerHandler handler, UploadSpinner view)
        {
            handler.PlatformView.SetStrokeThicknessDp((float)view.StrokeThickness);
        }
    }
}

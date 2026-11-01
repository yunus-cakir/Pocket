using Microsoft.Maui.Controls;

namespace Pocket.Client.Controls
{
    public class CameraView : View
    {
        /// <summary>Preferences key for the "capture from preview" shutter mode (General settings).</summary>
        public const string CaptureFromPreviewPreferenceKey = "CaptureFromPreview";
        public const bool CaptureFromPreviewDefault = true;

        /// <summary>Preferences key for showing the vertical zoom slider after tap-to-focus (General settings).</summary>
        public const string ZoomSliderPreferenceKey = "ZoomSliderEnabled";
        public const bool ZoomSliderDefault = false;

        /// <summary>Preferences key for showing the horizontal exposure slider after tap-to-focus (General settings).</summary>
        public const string ExposureSliderPreferenceKey = "ExposureSliderEnabled";
        public const bool ExposureSliderDefault = false;

        /// <summary>
        /// Current shutter mode, read at capture time so settings changes apply immediately.
        /// true = save the exact preview frame, false = native still capture.
        /// </summary>
        public static bool CaptureFromPreview =>
            Microsoft.Maui.Storage.Preferences.Get(CaptureFromPreviewPreferenceKey, CaptureFromPreviewDefault);

        public static bool ZoomSliderEnabled =>
            Microsoft.Maui.Storage.Preferences.Get(ZoomSliderPreferenceKey, ZoomSliderDefault);

        public static bool ExposureSliderEnabled =>
            Microsoft.Maui.Storage.Preferences.Get(ExposureSliderPreferenceKey, ExposureSliderDefault);

        // ---- Camera capabilities (reported by the platform handler when the camera opens) ----

        /// <summary>Maximum zoom level index (0 = zoom not supported).</summary>
        public int MaxZoom { get; private set; }

        /// <summary>Zoom ratios ×100 for each zoom level index (e.g. 100 = 1.0x).</summary>
        public IReadOnlyList<int> ZoomRatios { get; private set; } = System.Array.Empty<int>();

        /// <summary>Minimum / maximum exposure compensation index (both 0 = not supported).</summary>
        public int MinExposure { get; private set; }
        public int MaxExposure { get; private set; }

        /// <summary>EV value of one exposure compensation step.</summary>
        public float ExposureStep { get; private set; }

        public event EventHandler? CapabilitiesChanged;

        public event EventHandler<string>? PhotoCaptured;

        public void CapturePhoto()
        {
            Handler?.Invoke(nameof(CapturePhoto));
        }

        /// <summary>Focuses and meters at a point given in normalized view coordinates (0..1).</summary>
        public void FocusAt(double normalizedX, double normalizedY)
        {
            Handler?.Invoke(nameof(FocusAt), new Point(normalizedX, normalizedY));
        }

        /// <summary>Sets the zoom level index (0..<see cref="MaxZoom"/>).</summary>
        public void SetZoom(int level)
        {
            Handler?.Invoke(nameof(SetZoom), level);
        }

        /// <summary>Sets the exposure compensation index (<see cref="MinExposure"/>..<see cref="MaxExposure"/>).</summary>
        public void SetExposure(int index)
        {
            Handler?.Invoke(nameof(SetExposure), index);
        }

        public void OnPhotoCaptured(string filePath)
        {
            PhotoCaptured?.Invoke(this, filePath);
        }

        public void ReportCapabilities(int maxZoom, IReadOnlyList<int> zoomRatios, int minExposure, int maxExposure, float exposureStep)
        {
            MaxZoom = maxZoom;
            ZoomRatios = zoomRatios;
            MinExposure = minExposure;
            MaxExposure = maxExposure;
            ExposureStep = exposureStep;
            CapabilitiesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

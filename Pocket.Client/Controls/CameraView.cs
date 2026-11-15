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

        /// <summary>Preferences key: keep the last zoom level between taps and camera restarts (General settings).</summary>
        public const string KeepZoomPreferenceKey = "KeepLastZoom";
        public const bool KeepZoomDefault = false;
        public const string LastZoomLevelPreferenceKey = "LastZoomLevelValue"; // double (fractional level)

        /// <summary>Preferences key: keep the last exposure compensation between taps and camera restarts (General settings).</summary>
        public const string KeepExposurePreferenceKey = "KeepLastExposure";
        public const bool KeepExposureDefault = false;
        public const string LastExposureIndexPreferenceKey = "LastExposureIndexValue"; // double (fractional index)

        public static bool KeepLastZoom =>
            Microsoft.Maui.Storage.Preferences.Get(KeepZoomPreferenceKey, KeepZoomDefault);

        public static bool KeepLastExposure =>
            Microsoft.Maui.Storage.Preferences.Get(KeepExposurePreferenceKey, KeepExposureDefault);

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

        /// <summary>True when the active camera has a hardware flash (torch) unit.</summary>
        public bool HasFlash { get; private set; }

        /// <summary>True when the active camera is the front (selfie) camera.</summary>
        public bool IsFrontCamera { get; private set; }

        /// <summary>Number of cameras available on the device (flip is a no-op when &lt; 2).</summary>
        public int CameraCount { get; private set; }

        /// <summary>
        /// Flash toggle state. Read by the platform handler at capture time: when the active camera has a
        /// hardware flash it fires during capture; otherwise the page performs a "screen flash".
        /// </summary>
        public bool IsFlashEnabled { get; set; }

        /// <summary>Preferences key remembering the last used camera facing (true = front).</summary>
        public const string UseFrontCameraPreferenceKey = "UseFrontCamera";

        /// <summary>
        /// Current physical device orientation in degrees (0, 90, 180, 270) reported by the platform accelerometer.
        /// </summary>
        public int DeviceOrientationDegrees { get; private set; }

        /// <summary>
        /// When true, photo capture uses <see cref="LockedOrientationDegrees"/> instead of the live sensor orientation.
        /// </summary>
        public bool IsOrientationLocked { get; private set; }

        /// <summary>
        /// The orientation in degrees (0, 90, 180, 270) captured when <see cref="IsOrientationLocked"/> was enabled.
        /// </summary>
        public int LockedOrientationDegrees { get; private set; }

        /// <summary>
        /// Effective orientation in degrees used at capture time (locked orientation when locked, otherwise live sensor orientation).
        /// </summary>
        public int EffectiveOrientationDegrees => IsOrientationLocked ? LockedOrientationDegrees : DeviceOrientationDegrees;

        public event EventHandler? CapabilitiesChanged;

        public event EventHandler<int>? DeviceOrientationChanged;

        public event EventHandler<string>? PhotoCaptured;

        public void SetOrientationLock(bool locked)
        {
            if (locked)
            {
                LockedOrientationDegrees = DeviceOrientationDegrees;
                IsOrientationLocked = true;
            }
            else
            {
                IsOrientationLocked = false;
            }
        }

        public void OnDeviceOrientationChanged(int degrees)
        {
            if (DeviceOrientationDegrees == degrees)
                return;

            DeviceOrientationDegrees = degrees;
            DeviceOrientationChanged?.Invoke(this, degrees);
        }

        public void CapturePhoto()
        {
            Handler?.Invoke(nameof(CapturePhoto));
        }

        /// <summary>Focuses and meters at a point given in normalized view coordinates (0..1).</summary>
        public void FocusAt(double normalizedX, double normalizedY)
        {
            Handler?.Invoke(nameof(FocusAt), new Point(normalizedX, normalizedY));
        }

        /// <summary>
        /// Sets the zoom level index (0..<see cref="MaxZoom"/>). Fractional values are allowed so the
        /// slider moves smoothly: the software zoom fallback interpolates continuously, while hardware
        /// zoom snaps to the nearest supported level.
        /// </summary>
        public void SetZoom(double level)
        {
            Handler?.Invoke(nameof(SetZoom), level);
        }

        /// <summary>
        /// Sets the exposure compensation index (<see cref="MinExposure"/>..<see cref="MaxExposure"/>).
        /// Fractional values are allowed: the preview brightness filter follows the exact value, while the
        /// hardware compensation snaps to the nearest supported step.
        /// </summary>
        public void SetExposure(double index)
        {
            Handler?.Invoke(nameof(SetExposure), index);
        }

        /// <summary>Switches between the back and front cameras.</summary>
        public void SwitchCamera()
        {
            Handler?.Invoke(nameof(SwitchCamera));
        }

        /// <summary>
        /// Releases the camera device (and the system "camera in use" indicator) while the page is covered,
        /// e.g. when a DM page is pushed on top. The preview surface is kept so <see cref="ResumeCamera"/> can reopen quickly.
        /// </summary>
        public void SuspendCamera()
        {
            Handler?.Invoke(nameof(SuspendCamera));
        }

        /// <summary>Reopens the camera after <see cref="SuspendCamera"/>, once the page is visible again.</summary>
        public void ResumeCamera()
        {
            Handler?.Invoke(nameof(ResumeCamera));
        }

        public void OnPhotoCaptured(string filePath)
        {
            PhotoCaptured?.Invoke(this, filePath);
        }

        public void ReportCapabilities(int maxZoom, IReadOnlyList<int> zoomRatios, int minExposure, int maxExposure, float exposureStep,
                                       bool hasFlash = false, bool isFrontCamera = false, int cameraCount = 1)
        {
            HasFlash = hasFlash;
            IsFrontCamera = isFrontCamera;
            CameraCount = cameraCount;
            MaxZoom = maxZoom;
            ZoomRatios = zoomRatios;
            MinExposure = minExposure;
            MaxExposure = maxExposure;
            ExposureStep = exposureStep;
            CapabilitiesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

using System.Linq;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Microsoft.Maui.Handlers;
using Pocket.Client.Controls;

using Paint = Android.Graphics.Paint;
using Color = Android.Graphics.Color;
using Canvas = Android.Graphics.Canvas;
using Rect = Android.Graphics.Rect;
using Matrix = Android.Graphics.Matrix;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Pocket.Client.Platforms.Android.Handlers
{
    public class CameraViewHandler : ViewHandler<CameraView, TextureView>
    {
        private global::Android.Hardware.Camera? _camera;
        private CameraSurfaceTextureListener? _listener;

        // Center-crop scale currently applied to the TextureView (needed to map taps to sensor coordinates)
        private float _scaleX = 1f;
        private float _scaleY = 1f;
        private int _viewWidth;
        private int _viewHeight;

        // Software zoom fallback when the camera HAL reports IsZoomSupported = false (e.g. Android emulator virtual scene camera)
        private const int FallbackMaxZoom = 30;
        private static readonly int[] FallbackZoomRatios = Enumerable.Range(0, FallbackMaxZoom + 1).Select(i => 100 + i * 10).ToArray(); // 1.0x .. 4.0x
        private bool _useSoftwareZoomFallback;
        private float _softwareZoomFactor = 1f;

        // Software exposure fallback (TextureView brightness adjustment) so EV changes are visible even when the virtual camera ignores ExposureCompensation.
        // Stored as a fractional index so the preview brightness follows the slider smoothly between hardware steps.
        private double _exposureIndex;
        private float _exposureStep = 1f / 6f;
        private bool HasExposureAdjustment => System.Math.Abs(_exposureIndex) > 0.001;

        // Active camera (back / front) and its geometry
        private int _cameraId = -1;
        private bool _isFrontCamera;
        private int _displayOrientation = 90;
        private int _sensorOrientation = 90;
        private bool _hasFlash;
        private bool _isCapturing;

        // Physical device orientation (0, 90, 180, 270) reported by the accelerometer (OrientationEventListener)
        // so photos taken while holding the phone sideways/upside-down are rotated upright even when the UI is locked to Portrait.
        private CameraOrientationEventListener? _orientationListener;
        private int _deviceOrientationDegrees;

        // How long the torch stays on before grabbing the preview frame so auto-exposure can settle
        private const int TorchSettleMs = 700;

        public static IPropertyMapper<CameraView, CameraViewHandler> PropertyMapper = new PropertyMapper<CameraView, CameraViewHandler>(ViewHandler.ViewMapper)
        {
        };

        public static CommandMapper<CameraView, CameraViewHandler> CommandMapper = new(ViewHandler.ViewCommandMapper)
        {
            [nameof(CameraView.CapturePhoto)] = MapCapturePhoto,
            [nameof(CameraView.FocusAt)] = MapFocusAt,
            [nameof(CameraView.SetZoom)] = MapSetZoom,
            [nameof(CameraView.SetExposure)] = MapSetExposure,
            [nameof(CameraView.SwitchCamera)] = MapSwitchCamera,
        };

        public CameraViewHandler() : base(PropertyMapper, CommandMapper)
        {
        }

        protected override TextureView CreatePlatformView()
        {
            var textureView = new TextureView(Context);
            _listener = new CameraSurfaceTextureListener(this);
            textureView.SurfaceTextureListener = _listener;
            return textureView;
        }

        protected override void ConnectHandler(TextureView platformView)
        {
            base.ConnectHandler(platformView);
            StartOrientationListener();
        }

        protected override void DisconnectHandler(TextureView platformView)
        {
            StopOrientationListener();
            StopCamera();
            if (platformView != null)
                platformView.SurfaceTextureListener = null;
            
            _listener?.Dispose();
            _listener = null;
            base.DisconnectHandler(platformView);
        }

        internal void StartOrientationListener()
        {
            try
            {
                if (_orientationListener == null && Context != null)
                {
                    _orientationListener = new CameraOrientationEventListener(Context, this);
                }

                if (_orientationListener?.CanDetectOrientation() == true)
                {
                    _orientationListener.Enable();
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OrientationListener Start Error: {ex.Message}");
            }
        }

        internal void StopOrientationListener()
        {
            try
            {
                _orientationListener?.Disable();
                _orientationListener?.Dispose();
            }
            catch { }
            finally
            {
                _orientationListener = null;
            }
        }

        internal void UpdateDeviceOrientation(int orientationDegrees)
        {
            if (orientationDegrees == OrientationEventListener.OrientationUnknown)
                return;

            // Round to nearest 90° with 10° hysteresis around the 45° boundaries to avoid jitter
            int current = _deviceOrientationDegrees;
            int diff = System.Math.Abs(orientationDegrees - current);
            diff = System.Math.Min(diff, 360 - diff);
            if (diff >= 55)
            {
                int newDegrees = ((orientationDegrees + 45) / 90 * 90) % 360;
                if (newDegrees != _deviceOrientationDegrees)
                {
                    _deviceOrientationDegrees = newDegrees;
                    if (VirtualView is CameraView cv)
                    {
                        cv.OnDeviceOrientationChanged(newDegrees);
                    }
                }
            }
        }

        private int EffectiveDeviceOrientationDegrees =>
            VirtualView is CameraView cv ? cv.EffectiveOrientationDegrees : _deviceOrientationDegrees;

        /// <summary>
        /// Rotation angle (CW) to apply to an upright portrait preview frame so the captured image
        /// matches the physical orientation in which the user held the phone (or the locked orientation).
        /// OrientationEventListener reports CW tilt of the device (90 = right side down, 270 = left side down),
        /// so rotating the portrait frame by (360 - deviceOrientation) % 360 brings the real-world top to the top of the image.
        /// </summary>
        internal int GetPreviewCaptureRotationDegrees()
        {
            return (360 - (EffectiveDeviceOrientationDegrees % 360)) % 360;
        }

        /// <summary>
        /// Rotation angle (CW) to apply to the raw sensor frame from Camera.TakePicture() so the captured image
        /// is upright for the current physical device orientation (or the locked orientation).
        /// </summary>
        internal int GetStillCaptureRotationDegrees()
        {
            int device = EffectiveDeviceOrientationDegrees % 360;
            if (_isFrontCamera)
            {
                // Front sensor image is rotated by _sensorOrientation and then mirrored horizontally;
                // adjusting by -device before the horizontal flip produces the same upright orientation as the preview.
                return (_sensorOrientation - device + 360) % 360;
            }

            return (_sensorOrientation + device) % 360;
        }

        internal void StartCamera(SurfaceTexture? surface)
        {
            if (surface == null) return;
            if (_camera != null) return;

            try
            {
                SelectCamera(Microsoft.Maui.Storage.Preferences.Get(CameraView.UseFrontCameraPreferenceKey, false));
                _camera = _cameraId >= 0
                    ? global::Android.Hardware.Camera.Open(_cameraId)
                    : global::Android.Hardware.Camera.Open();
                if (_camera != null)
                {
                    var parameters = _camera.GetParameters();
                    if (parameters != null)
                    {
                        var supportedPreviewSizes = parameters.SupportedPreviewSizes;
                        var supportedPictureSizes = parameters.SupportedPictureSizes;

                        if (supportedPreviewSizes != null && supportedPreviewSizes.Count > 0)
                        {
                            var optimalPreview = supportedPreviewSizes
                                .OrderByDescending(s => s.Width * s.Height)
                                .FirstOrDefault(s => s.Width <= 1920);

                            if (optimalPreview != null)
                            {
                                parameters.SetPreviewSize(optimalPreview.Width, optimalPreview.Height);

                                // Match picture size aspect ratio to preview size to prevent driver stride corruption
                                double targetRatio = (double)optimalPreview.Width / optimalPreview.Height;
                                var optimalPicture = supportedPictureSizes?
                                    .OrderByDescending(s => s.Width * s.Height)
                                    .FirstOrDefault(s => System.Math.Abs(((double)s.Width / s.Height) - targetRatio) < 0.1 && s.Width <= 1920)
                                    ?? supportedPictureSizes?.FirstOrDefault();

                                if (optimalPicture != null)
                                {
                                    parameters.SetPictureSize(optimalPicture.Width, optimalPicture.Height);
                                }
                            }

                            parameters.PictureFormat = ImageFormatType.Jpeg;
                            _camera.SetParameters(parameters);
                        }

                        // Continuous autofocus by default (tap-to-focus switches to a one-shot "auto" focus)
                        try
                        {
                            var p = _camera.GetParameters();
                            if (p?.SupportedFocusModes?.Contains(global::Android.Hardware.Camera.Parameters.FocusModeContinuousPicture) == true)
                            {
                                p.FocusMode = global::Android.Hardware.Camera.Parameters.FocusModeContinuousPicture;
                                _camera.SetParameters(p);
                            }
                        }
                        catch (System.Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Focus Mode Error: {ex.Message}");
                        }
                    }

                    _camera.SetPreviewTexture(surface);
                    _camera.SetDisplayOrientation(_displayOrientation);
                    _camera.StartPreview();

                    _softwareZoomFactor = 1f;
                    _exposureIndex = 0;
                    UpdatePreviewBrightnessFilter();
                    ReportCapabilities();
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Camera Start Error: {ex.Message}");
            }
        }

        internal void AdjustAspectRatio(int viewWidth, int viewHeight)
        {
            if (_camera == null || PlatformView == null || viewWidth <= 0 || viewHeight <= 0)
                return;

            _viewWidth = viewWidth;
            _viewHeight = viewHeight;

            try
            {
                var parameters = _camera.GetParameters();
                var previewSize = parameters?.PreviewSize;
                if (previewSize == null) return;

                // For 90/270 degree portrait orientation, swap width and height
                bool swap = IsRotated;
                int previewWidth = swap ? previewSize.Height : previewSize.Width;
                int previewHeight = swap ? previewSize.Width : previewSize.Height;

                float scaleX = 1.0f;
                float scaleY = 1.0f;

                float viewRatio = (float)viewWidth / viewHeight;
                float previewRatio = (float)previewWidth / previewHeight;

                // Center Crop (Aspect Fill) Matrix math
                if (viewRatio > previewRatio)
                {
                    scaleY = viewRatio / previewRatio;
                }
                else
                {
                    scaleX = previewRatio / viewRatio;
                }

                _scaleX = scaleX;
                _scaleY = scaleY;
                ApplyTextureTransform();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Aspect Ratio Error: {ex.Message}");
            }
        }

        private void ApplyTextureTransform()
        {
            if (PlatformView == null || _viewWidth <= 0 || _viewHeight <= 0)
                return;

            float zoom = _useSoftwareZoomFallback ? _softwareZoomFactor : 1f;
            using var matrix = new Matrix();
            matrix.SetScale(_scaleX * zoom, _scaleY * zoom, _viewWidth / 2.0f, _viewHeight / 2.0f);
            PlatformView.SetTransform(matrix);
        }

        private bool IsRotated => _displayOrientation % 180 != 0;

        internal bool IsFrontCamera => _isFrontCamera;
        internal int SensorOrientation => _sensorOrientation;

        /// <summary>
        /// Picks the camera id for the requested facing (falls back to any available camera) and
        /// computes the display orientation for the current screen rotation.
        /// </summary>
        private void SelectCamera(bool preferFront)
        {
            _cameraId = -1;
            _isFrontCamera = false;
            _sensorOrientation = 90;
            _displayOrientation = 90;

            int count = global::Android.Hardware.Camera.NumberOfCameras;
            if (count <= 0) return;

            var wanted = preferFront ? global::Android.Hardware.CameraFacing.Front : global::Android.Hardware.CameraFacing.Back;
            int chosen = -1;
            using var info = new global::Android.Hardware.Camera.CameraInfo();
            for (int i = 0; i < count; i++)
            {
                global::Android.Hardware.Camera.GetCameraInfo(i, info);
                if (info.Facing == wanted) { chosen = i; break; }
            }
            if (chosen < 0) chosen = 0;

            global::Android.Hardware.Camera.GetCameraInfo(chosen, info);
            _cameraId = chosen;
            _isFrontCamera = info.Facing == global::Android.Hardware.CameraFacing.Front;
            _sensorOrientation = info.Orientation;

            int degrees = 0;
            try
            {
                var rotation = (Context?.GetSystemService(Context.WindowService) as IWindowManager)?.DefaultDisplay?.Rotation;
                degrees = rotation switch
                {
                    SurfaceOrientation.Rotation90 => 90,
                    SurfaceOrientation.Rotation180 => 180,
                    SurfaceOrientation.Rotation270 => 270,
                    _ => 0
                };
            }
            catch { }

            _displayOrientation = _isFrontCamera
                ? (360 - (info.Orientation + degrees) % 360) % 360 // front preview is mirrored by the HAL
                : (info.Orientation - degrees + 360) % 360;
        }

        /// <summary>Toggles between the back and front cameras and restarts the preview on the same surface.</summary>
        private void SwitchCamera()
        {
            if (PlatformView == null || _isCapturing) return;
            if (global::Android.Hardware.Camera.NumberOfCameras < 2) return;

            bool useFront = !_isFrontCamera;
            Microsoft.Maui.Storage.Preferences.Set(CameraView.UseFrontCameraPreferenceKey, useFront);

            var surface = PlatformView.SurfaceTexture;
            StopCamera();
            if (surface == null) return; // Will start with the new facing once the surface becomes available

            StartCamera(surface);
            AdjustAspectRatio(PlatformView.Width, PlatformView.Height);
        }

        public static void MapSwitchCamera(CameraViewHandler handler, CameraView view, object? arg)
        {
            handler.SwitchCamera();
        }

        internal void StopCamera()
        {
            var cam = _camera;
            _camera = null;
            if (cam != null)
            {
                try
                {
                    cam.StopPreview();
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Camera StopPreview Error: {ex.Message}");
                }

                try
                {
                    cam.Release();
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Camera Release Error: {ex.Message}");
                }
            }
        }

        public static void MapCapturePhoto(CameraViewHandler handler, CameraView view, object? arg)
        {
            handler.CapturePhoto();
        }

        public static void MapFocusAt(CameraViewHandler handler, CameraView view, object? arg)
        {
            if (arg is Microsoft.Maui.Graphics.Point p)
                handler.FocusAt(p.X, p.Y);
        }

        public static void MapSetZoom(CameraViewHandler handler, CameraView view, object? arg)
        {
            if (arg is double level)
                handler.SetZoom(level);
            else if (arg is int intLevel)
                handler.SetZoom(intLevel);
        }

        public static void MapSetExposure(CameraViewHandler handler, CameraView view, object? arg)
        {
            if (arg is double index)
                handler.SetExposure(index);
            else if (arg is int intIndex)
                handler.SetExposure(intIndex);
        }

        private void ReportCapabilities()
        {
            if (_camera == null || VirtualView == null) return;
            try
            {
                var p = _camera.GetParameters();
                if (p == null) return;

                bool hwZoom = p.IsZoomSupported && p.MaxZoom > 0;
                _useSoftwareZoomFallback = !hwZoom;

                int maxZoom = hwZoom ? p.MaxZoom : FallbackMaxZoom;
                var ratios = hwZoom && p.ZoomRatios != null && p.ZoomRatios.Count > 0
                    ? p.ZoomRatios.Select(r => r.IntValue()).ToArray()
                    : FallbackZoomRatios;

                int minExp = p.MinExposureCompensation;
                int maxExp = p.MaxExposureCompensation;
                float expStep = p.ExposureCompensationStep > 0 ? p.ExposureCompensationStep : (1f / 6f);
                if (maxExp <= minExp)
                {
                    minExp = -12;
                    maxExp = 12;
                }
                _exposureStep = expStep;

                var flashModes = p.SupportedFlashModes;
                _hasFlash = flashModes != null &&
                            (flashModes.Contains(global::Android.Hardware.Camera.Parameters.FlashModeTorch) ||
                             flashModes.Contains(global::Android.Hardware.Camera.Parameters.FlashModeOn));

                VirtualView.ReportCapabilities(maxZoom, ratios, minExp, maxExp, expStep,
                    _hasFlash, _isFrontCamera, global::Android.Hardware.Camera.NumberOfCameras);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Capabilities Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Tap-to-focus. (nx, ny) are normalized view coordinates; they are mapped through the
        /// center-crop transform and the 90° display rotation into the camera's -1000..1000 area space.
        /// </summary>
        private void FocusAt(double nx, double ny)
        {
            if (_camera == null) return;
            try
            {
                // View → full (uncropped) displayed frame
                double u = 0.5 + (nx - 0.5) / System.Math.Max(0.0001, _scaleX);
                double v = 0.5 + (ny - 0.5) / System.Math.Max(0.0001, _scaleY);

                // Displayed (rotated 90° CW) frame → sensor frame: sensorX = v, sensorY = 1 - u
                // Front camera preview is additionally mirrored: sensorX = 1 - v
                double sensorV = _isFrontCamera ? 1 - v : v;
                int sx = (int)System.Math.Round(sensorV * 2000 - 1000);
                int sy = (int)System.Math.Round((1 - u) * 2000 - 1000);

                const int half = 100;
                static int Clamp(int x) => System.Math.Clamp(x, -1000, 1000);
                var rect = new global::Android.Graphics.Rect(Clamp(sx - half), Clamp(sy - half), Clamp(sx + half), Clamp(sy + half));
                var areas = new List<global::Android.Hardware.Camera.Area> { new global::Android.Hardware.Camera.Area(rect, 1000) };

                var p = _camera.GetParameters();
                if (p == null) return;

                bool canAutoFocus = p.SupportedFocusModes?.Contains(global::Android.Hardware.Camera.Parameters.FocusModeAuto) == true;
                if (p.MaxNumFocusAreas > 0) p.FocusAreas = areas;
                if (p.MaxNumMeteringAreas > 0) p.MeteringAreas = areas;
                if (canAutoFocus) p.FocusMode = global::Android.Hardware.Camera.Parameters.FocusModeAuto;

                _camera.CancelAutoFocus();
                _camera.SetParameters(p);

                if (canAutoFocus)
                    _camera.AutoFocus(new CameraAutoFocusCallback());
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Focus Error: {ex.Message}");
            }
        }

        private void SetZoom(double level)
        {
            if (_camera == null) return;
            try
            {
                if (_useSoftwareZoomFallback)
                {
                    // Continuous software zoom: interpolate between the fallback ratios so the preview scales smoothly
                    level = System.Math.Clamp(level, 0, FallbackMaxZoom);
                    int lower = (int)System.Math.Floor(level);
                    int upper = System.Math.Min(lower + 1, FallbackMaxZoom);
                    double t = level - lower;
                    _softwareZoomFactor = (float)((FallbackZoomRatios[lower] + (FallbackZoomRatios[upper] - FallbackZoomRatios[lower]) * t) / 100.0);
                    ApplyTextureTransform();
                    return;
                }

                var p = _camera.GetParameters();
                if (p == null || !p.IsZoomSupported) return;
                int hwLevel = System.Math.Clamp((int)System.Math.Round(level), 0, p.MaxZoom);
                if (p.Zoom == hwLevel) return;
                p.Zoom = hwLevel;
                _camera.SetParameters(p);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Zoom Error: {ex.Message}");
            }
        }

        private void SetExposure(double index)
        {
            if (_camera == null) return;
            try
            {
                _exposureIndex = index;
                UpdatePreviewBrightnessFilter();

                var p = _camera.GetParameters();
                if (p == null || p.MaxExposureCompensation <= p.MinExposureCompensation) return;
                int clamped = System.Math.Clamp((int)System.Math.Round(index), p.MinExposureCompensation, p.MaxExposureCompensation);
                if (p.ExposureCompensation == clamped) return;
                p.ExposureCompensation = clamped;
                _camera.SetParameters(p);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exposure Error: {ex.Message}");
            }
        }

        private void UpdatePreviewBrightnessFilter()
        {
            if (PlatformView == null) return;
            if (!HasExposureAdjustment)
            {
                PlatformView.SetLayerType(LayerType.None, null);
                return;
            }

            using var paint = CreateExposurePaint();
            // While dragging, only swap the layer paint instead of recreating the hardware layer on every move
            if (PlatformView.LayerType == LayerType.Hardware)
                PlatformView.SetLayerPaint(paint);
            else
                PlatformView.SetLayerType(LayerType.Hardware, paint);
        }

        private Paint? CreateExposurePaint()
        {
            if (!HasExposureAdjustment) return null;
            float ev = (float)(_exposureIndex * _exposureStep);
            float gain = (float)System.Math.Pow(2.0, ev * 0.5); // smooth ±EV brightness curve
            float offset = ev * 18f;

            var cm = new ColorMatrix(new float[]
            {
                gain, 0, 0, 0, offset,
                0, gain, 0, 0, offset,
                0, 0, gain, 0, offset,
                0, 0, 0, 1, 0
            });
            var paint = new Paint(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);
            paint.SetColorFilter(new ColorMatrixColorFilter(cm));
            return paint;
        }

        internal Bitmap ApplySoftwareZoomAndExposure(Bitmap source)
        {
            bool needZoom = _useSoftwareZoomFallback && _softwareZoomFactor > 1.001f;
            bool needExp = HasExposureAdjustment;
            if (!needZoom && !needExp) return source;

            int w = source.Width;
            int h = source.Height;
            var output = Bitmap.CreateBitmap(w, h, Bitmap.Config.Argb8888!)!;
            using var canvas = new Canvas(output);

            Rect srcRect;
            if (needZoom)
            {
                int cropW = System.Math.Max(1, (int)System.Math.Round(w / _softwareZoomFactor));
                int cropH = System.Math.Max(1, (int)System.Math.Round(h / _softwareZoomFactor));
                int left = (w - cropW) / 2;
                int top = (h - cropH) / 2;
                srcRect = new Rect(left, top, left + cropW, top + cropH);
            }
            else
            {
                srcRect = new Rect(0, 0, w, h);
            }

            using var dstRect = new Rect(0, 0, w, h);
            using var paint = CreateExposurePaint() ?? new Paint(PaintFlags.FilterBitmap);
            canvas.DrawBitmap(source, srcRect, dstRect, paint);
            srcRect.Dispose();
            return output;
        }

        private void SetFlashMode(string mode)
        {
            if (_camera == null) return;
            try
            {
                var p = _camera.GetParameters();
                if (p?.SupportedFlashModes?.Contains(mode) != true) return;
                if (p.FlashMode == mode) return;
                p.FlashMode = mode;
                _camera.SetParameters(p);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Flash Mode Error: {ex.Message}");
            }
        }

        private async void CapturePhoto()
        {
            if (_camera == null || PlatformView == null || _isCapturing) return;

            // Hardware flash only when the user enabled it and the active camera has a flash unit
            // (otherwise the page performs a screen flash before calling CapturePhoto).
            bool useHardwareFlash = VirtualView?.IsFlashEnabled == true && _hasFlash;

            // Shutter mode is configurable from Settings > General
            if (!CameraView.CaptureFromPreview)
            {
                try
                {
                    SetFlashMode(useHardwareFlash
                        ? global::Android.Hardware.Camera.Parameters.FlashModeOn
                        : global::Android.Hardware.Camera.Parameters.FlashModeOff);
                    _camera.TakePicture(null, null, new CameraPictureCallback(this));
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Capture Error: {ex.Message}");
                }
                return;
            }

            if (useHardwareFlash)
            {
                // Preview-frame capture cannot fire a still flash, so light the scene with the torch,
                // give auto-exposure time to settle, grab the frame and switch the torch off again.
                _isCapturing = true;
                try
                {
                    SetFlashMode(global::Android.Hardware.Camera.Parameters.FlashModeTorch);
                    await System.Threading.Tasks.Task.Delay(TorchSettleMs);
                    if (_camera == null || PlatformView == null) return;
                    CaptureFromPreviewFrame();
                }
                finally
                {
                    SetFlashMode(global::Android.Hardware.Camera.Parameters.FlashModeOff);
                    _isCapturing = false;
                }
                return;
            }

            CaptureFromPreviewFrame();
        }

        private void CaptureFromPreviewFrame()
        {
            if (_camera == null || PlatformView == null) return;

            try
            {
                // WYSIWYG capture: grab the exact frame shown in the preview instead of using
                // Camera.TakePicture(), whose still-capture ISP pipeline applies different
                // tone mapping / noise reduction / white balance and shifts the colors.
                var previewSize = _camera.GetParameters()?.PreviewSize;
                Bitmap? frame = previewSize != null
                    // Portrait (90°/270°) display orientation: width/height are swapped.
                    // SetDisplayOrientation is applied to the texture, so the frame is already upright.
                    ? (IsRotated
                        ? PlatformView.GetBitmap(previewSize.Height, previewSize.Width)
                        : PlatformView.GetBitmap(previewSize.Width, previewSize.Height))
                    : PlatformView.Bitmap;

                if (frame == null)
                {
                    // Fallback to the legacy still capture if the frame could not be read
                    _camera.TakePicture(null, null, new CameraPictureCallback(this));
                    return;
                }

                var tempPath = System.IO.Path.Combine(Microsoft.Maui.Storage.FileSystem.CacheDirectory, $"capture_{System.Guid.NewGuid()}.jpg");
                int rotationDegrees = GetPreviewCaptureRotationDegrees();

                // Encode off the UI thread, then notify on the main thread
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        using (frame)
                        using (var processed = ApplySoftwareZoomAndExposure(frame))
                        {
                            Bitmap toSave = processed;
                            try
                            {
                                if (rotationDegrees != 0)
                                {
                                    using var matrix = new Matrix();
                                    matrix.PostRotate(rotationDegrees);
                                    toSave = Bitmap.CreateBitmap(processed, 0, 0, processed.Width, processed.Height, matrix, true)!;
                                }

                                using var stream = System.IO.File.Create(tempPath);
                                toSave.Compress(Bitmap.CompressFormat.Jpeg!, 95, stream);
                            }
                            finally
                            {
                                if (!ReferenceEquals(toSave, processed))
                                    toSave.Dispose();
                            }
                        }

                        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                        {
                            if (VirtualView is CameraView cv)
                                cv.OnPhotoCaptured(tempPath);
                        });
                    }
                    catch (System.Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Frame Encode Error: {ex.Message}");
                    }
                });
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Capture Error: {ex.Message}");
            }
        }
    }

    internal class CameraSurfaceTextureListener : Java.Lang.Object, TextureView.ISurfaceTextureListener
    {
        private readonly CameraViewHandler _handler;

        public CameraSurfaceTextureListener(CameraViewHandler handler)
        {
            _handler = handler;
        }

        public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
        {
            _handler.StartOrientationListener();
            _handler.StartCamera(surface);
            _handler.AdjustAspectRatio(width, height);
        }

        public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
        {
            _handler.StopOrientationListener();
            _handler.StopCamera();
            return true;
        }

        public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
        {
            _handler.AdjustAspectRatio(width, height);
        }

        public void OnSurfaceTextureUpdated(SurfaceTexture surface)
        {
        }
    }

    internal class CameraAutoFocusCallback : Java.Lang.Object, global::Android.Hardware.Camera.IAutoFocusCallback
    {
        public void OnAutoFocus(bool success, global::Android.Hardware.Camera? camera)
        {
            System.Diagnostics.Debug.WriteLine($"AutoFocus result: {success}");
        }
    }

    internal sealed class CameraOrientationEventListener : OrientationEventListener
    {
        private readonly CameraViewHandler _handler;

        public CameraOrientationEventListener(Context context, CameraViewHandler handler)
            : base(context, global::Android.Hardware.SensorDelay.Normal)
        {
            _handler = handler;
        }

        public override void OnOrientationChanged(int orientation)
        {
            _handler.UpdateDeviceOrientation(orientation);
        }
    }

    internal class CameraPictureCallback : Java.Lang.Object, global::Android.Hardware.Camera.IPictureCallback
    {
        private readonly CameraViewHandler _handler;

        public CameraPictureCallback(CameraViewHandler handler)
        {
            _handler = handler;
        }

        public void OnPictureTaken(byte[]? data, global::Android.Hardware.Camera? camera)
        {
            if (data != null && data.Length > 0)
            {
                try
                {
                    using var originalBitmap = BitmapFactory.DecodeByteArray(data, 0, data.Length);
                    if (originalBitmap != null)
                    {
                        using var matrix = new Matrix();
                        matrix.PostRotate(_handler.GetStillCaptureRotationDegrees());
                        if (_handler.IsFrontCamera)
                        {
                            // Mirror selfies so the saved photo matches the (mirrored) preview
                            matrix.PostScale(-1, 1);
                        }

                        using var rotatedBitmap = Bitmap.CreateBitmap(
                            originalBitmap, 0, 0,
                            originalBitmap.Width, originalBitmap.Height,
                            matrix, true)!;

                        using var finalBitmap = _handler.ApplySoftwareZoomAndExposure(rotatedBitmap);
                        var tempPath = System.IO.Path.Combine(Microsoft.Maui.Storage.FileSystem.CacheDirectory, $"capture_{System.Guid.NewGuid()}.jpg");
                        using var stream = System.IO.File.Create(tempPath);
                        finalBitmap.Compress(Bitmap.CompressFormat.Jpeg!, 95, stream);

                        if (_handler.VirtualView is CameraView cv)
                        {
                            cv.OnPhotoCaptured(tempPath);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Picture Processing Error: {ex.Message}");
                }
                
                camera?.StartPreview();
            }
        }
    }
}
#pragma warning restore CS0618

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

        // Software exposure fallback (TextureView brightness adjustment) so EV changes are visible even when the virtual camera ignores ExposureCompensation
        private int _exposureIndex;
        private float _exposureStep = 1f / 6f;

        public static IPropertyMapper<CameraView, CameraViewHandler> PropertyMapper = new PropertyMapper<CameraView, CameraViewHandler>(ViewHandler.ViewMapper)
        {
        };

        public static CommandMapper<CameraView, CameraViewHandler> CommandMapper = new(ViewHandler.ViewCommandMapper)
        {
            [nameof(CameraView.CapturePhoto)] = MapCapturePhoto,
            [nameof(CameraView.FocusAt)] = MapFocusAt,
            [nameof(CameraView.SetZoom)] = MapSetZoom,
            [nameof(CameraView.SetExposure)] = MapSetExposure,
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
        }

        protected override void DisconnectHandler(TextureView platformView)
        {
            StopCamera();
            if (platformView != null)
                platformView.SurfaceTextureListener = null;
            
            _listener?.Dispose();
            _listener = null;
            base.DisconnectHandler(platformView);
        }

        internal void StartCamera(SurfaceTexture? surface)
        {
            if (surface == null) return;
            if (_camera != null) return;

            try
            {
                _camera = global::Android.Hardware.Camera.Open();
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
                    _camera.SetDisplayOrientation(90); // Default portrait orientation
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

                // For 90 degree portrait orientation, swap width and height
                int previewWidth = previewSize.Height;
                int previewHeight = previewSize.Width;

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
            if (arg is int level)
                handler.SetZoom(level);
        }

        public static void MapSetExposure(CameraViewHandler handler, CameraView view, object? arg)
        {
            if (arg is int index)
                handler.SetExposure(index);
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

                VirtualView.ReportCapabilities(maxZoom, ratios, minExp, maxExp, expStep);
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
                int sx = (int)System.Math.Round(v * 2000 - 1000);
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

        private void SetZoom(int level)
        {
            if (_camera == null) return;
            try
            {
                if (_useSoftwareZoomFallback)
                {
                    level = System.Math.Clamp(level, 0, FallbackMaxZoom);
                    _softwareZoomFactor = FallbackZoomRatios[level] / 100f;
                    ApplyTextureTransform();
                    return;
                }

                var p = _camera.GetParameters();
                if (p == null || !p.IsZoomSupported) return;
                level = System.Math.Clamp(level, 0, p.MaxZoom);
                if (p.Zoom == level) return;
                p.Zoom = level;
                _camera.SetParameters(p);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Zoom Error: {ex.Message}");
            }
        }

        private void SetExposure(int index)
        {
            if (_camera == null) return;
            try
            {
                _exposureIndex = index;
                UpdatePreviewBrightnessFilter();

                var p = _camera.GetParameters();
                if (p == null || p.MaxExposureCompensation <= p.MinExposureCompensation) return;
                int clamped = System.Math.Clamp(index, p.MinExposureCompensation, p.MaxExposureCompensation);
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
            if (_exposureIndex == 0)
            {
                PlatformView.SetLayerType(LayerType.None, null);
                return;
            }

            using var paint = CreateExposurePaint();
            PlatformView.SetLayerType(LayerType.Hardware, paint);
        }

        private Paint? CreateExposurePaint()
        {
            if (_exposureIndex == 0) return null;
            float ev = _exposureIndex * _exposureStep;
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
            bool needExp = _exposureIndex != 0;
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

        private void CapturePhoto()
        {
            if (_camera == null || PlatformView == null) return;

            // Shutter mode is configurable from Settings > General
            if (!CameraView.CaptureFromPreview)
            {
                try
                {
                    _camera.TakePicture(null, null, new CameraPictureCallback(this));
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Capture Error: {ex.Message}");
                }
                return;
            }

            try
            {
                // WYSIWYG capture: grab the exact frame shown in the preview instead of using
                // Camera.TakePicture(), whose still-capture ISP pipeline applies different
                // tone mapping / noise reduction / white balance and shifts the colors.
                var previewSize = _camera.GetParameters()?.PreviewSize;
                Bitmap? frame = previewSize != null
                    // Portrait (90°) display orientation: width/height are swapped.
                    // SetDisplayOrientation is applied to the texture, so the frame is already upright.
                    ? PlatformView.GetBitmap(previewSize.Height, previewSize.Width)
                    : PlatformView.Bitmap;

                if (frame == null)
                {
                    // Fallback to the legacy still capture if the frame could not be read
                    _camera.TakePicture(null, null, new CameraPictureCallback(this));
                    return;
                }

                var tempPath = System.IO.Path.Combine(Microsoft.Maui.Storage.FileSystem.CacheDirectory, $"capture_{System.Guid.NewGuid()}.jpg");

                // Encode off the UI thread, then notify on the main thread
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        using (frame)
                        using (var processed = ApplySoftwareZoomAndExposure(frame))
                        using (var stream = System.IO.File.Create(tempPath))
                        {
                            processed.Compress(Bitmap.CompressFormat.Jpeg!, 95, stream);
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
            _handler.StartCamera(surface);
            _handler.AdjustAspectRatio(width, height);
        }

        public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
        {
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
                        matrix.PostRotate(90);

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

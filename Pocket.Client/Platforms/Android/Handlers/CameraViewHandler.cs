using System.Linq;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Microsoft.Maui.Handlers;
using Pocket.Client.Controls;

#pragma warning disable CS0618 // Type or member is obsolete

namespace Pocket.Client.Platforms.Android.Handlers
{
    public class CameraViewHandler : ViewHandler<CameraView, TextureView>
    {
        private global::Android.Hardware.Camera? _camera;
        private CameraSurfaceTextureListener? _listener;

        public static IPropertyMapper<CameraView, CameraViewHandler> PropertyMapper = new PropertyMapper<CameraView, CameraViewHandler>(ViewHandler.ViewMapper)
        {
        };

        public static CommandMapper<CameraView, CameraViewHandler> CommandMapper = new(ViewHandler.ViewCommandMapper)
        {
            [nameof(CameraView.CapturePhoto)] = MapCapturePhoto
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
                    }

                    _camera.SetPreviewTexture(surface);
                    _camera.SetDisplayOrientation(90); // Default portrait orientation
                    _camera.StartPreview();
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

                Matrix matrix = new Matrix();
                matrix.SetScale(scaleX, scaleY, viewWidth / 2.0f, viewHeight / 2.0f);
                PlatformView.SetTransform(matrix);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Aspect Ratio Error: {ex.Message}");
            }
        }

        internal void StopCamera()
        {
            if (_camera != null)
            {
                _camera.StopPreview();
                _camera.Release();
                _camera = null;
            }
        }

        public static void MapCapturePhoto(CameraViewHandler handler, CameraView view, object? arg)
        {
            handler.CapturePhoto();
        }

        private void CapturePhoto()
        {
            if (_camera != null)
            {
                try
                {
                    _camera.TakePicture(null, null, new CameraPictureCallback(this));
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Capture Error: {ex.Message}");
                }
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
                            matrix, true);

                        var tempPath = System.IO.Path.Combine(Microsoft.Maui.Storage.FileSystem.CacheDirectory, $"capture_{System.Guid.NewGuid()}.jpg");
                        using var stream = System.IO.File.Create(tempPath);
                        rotatedBitmap.Compress(Bitmap.CompressFormat.Jpeg, 95, stream);

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

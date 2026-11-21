using System.Linq;
using Android.Content;
using Android.Runtime;
using AndroidX.Camera.Core;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.View;
using AndroidX.Core.Content;
using AndroidX.Lifecycle;
using Android.Graphics;
using Android.Views;
using Microsoft.Maui.Handlers;
using Pocket.Client.Controls;

namespace Pocket.Client.Platforms.Android.Handlers
{
    /// <summary>
    /// CameraX (Camera2-based) camera handler. Work in progress on feature/camerax: preview, capabilities,
    /// exposure, zoom, tap-to-focus and photo capture. Selected with the hidden <see cref="CameraView.CameraEnginePreferenceKey"/> preference;
    /// the legacy Camera1 <see cref="CameraViewHandler"/> remains the default until feature parity.
    /// </summary>
    public class CameraXViewHandler : ViewHandler<CameraView, PreviewView>
    {
        // Zoom is exposed to CameraView as discrete levels over CameraX's continuous linear zoom (0..1)
        private const int ZoomLevels = 100;

        private ProcessCameraProvider? _provider;
        private ICamera? _camera;
        private Preview? _preview;
        private ImageCapture? _imageCapture;
        private bool _isCapturing;

        // Physical device orientation (0/90/180/270, CW tilt) used to rotate captured photos, as in the legacy handler
        private OrientationEventListener? _orientationListener;
        private int _deviceOrientationDegrees;

        // How long the torch stays on before grabbing the preview frame so auto-exposure can settle
        private const int TorchSettleMs = 700;
        private bool _isSuspended;
        private bool _useFrontCamera;

        // CameraX publishes the zoom range asynchronously (LiveData) once the camera is open, so capabilities
        // are reported again when it arrives; only a change of the range matters, not every zoom-ratio update.
        private ZoomStateObserver? _zoomObserver;
        private AndroidX.Lifecycle.LiveData? _observedZoomState;
        private (float Min, float Max) _reportedZoomRange;

        /// <summary>Keeps only the camera with the given Camera2 id.</summary>
        private sealed class CameraIdFilter : Java.Lang.Object, ICameraFilter
        {
            private readonly string _id;
            public CameraIdFilter(string id) => _id = id;

            public System.Collections.Generic.IList<ICameraInfo> Filter(System.Collections.Generic.IList<ICameraInfo> cameraInfos) =>
                cameraInfos.Where(info =>
                {
                    try { return AndroidX.Camera.Camera2.InterOp.Camera2CameraInfo.From(info).CameraId == _id; }
                    catch { return false; }
                }).ToList();
        }

        public static IPropertyMapper<CameraView, CameraXViewHandler> PropertyMapper =
            new PropertyMapper<CameraView, CameraXViewHandler>(ViewHandler.ViewMapper);

        public static CommandMapper<CameraView, CameraXViewHandler> CommandMapper = new(ViewHandler.ViewCommandMapper)
        {
            [nameof(CameraView.CapturePhoto)] = (h, v, a) => h.CapturePhoto(),
            [nameof(CameraView.FocusAt)] = (h, v, a) => { if (a is Microsoft.Maui.Graphics.Point p) h.FocusAt(p.X, p.Y); },
            [nameof(CameraView.SetZoom)] = (h, v, a) => h.SetZoom(a is double d ? d : a is int i ? i : 0),
            [nameof(CameraView.SetExposure)] = (h, v, a) => h.SetExposure(a is double d ? d : a is int i ? i : 0),
            [nameof(CameraView.SwitchCamera)] = (h, v, a) => h.SwitchCamera(),
            [nameof(CameraView.SuspendCamera)] = (h, v, a) => h.Suspend(),
            [nameof(CameraView.ResumeCamera)] = (h, v, a) => h.Resume(),
            [nameof(CameraView.ResumePreview)] = (h, v, a) => h.UnfreezePreview(),
        };

        public CameraXViewHandler() : base(PropertyMapper, CommandMapper)
        {
        }

        protected override PreviewView CreatePlatformView()
        {
            var view = new PreviewView(Context);
            // TextureView-backed so the exact preview frame can be read back (true-color capture)
            view.SetImplementationMode(PreviewView.ImplementationMode.Compatible!);
            view.SetScaleType(PreviewView.ScaleType.FillCenter!);
            return view;
        }

        protected override void ConnectHandler(PreviewView platformView)
        {
            base.ConnectHandler(platformView);
            _useFrontCamera = Microsoft.Maui.Storage.Preferences.Get(CameraView.UseFrontCameraPreferenceKey, false);
            StartOrientationListener();
            Resume();
        }

        protected override void DisconnectHandler(PreviewView platformView)
        {
            StopOrientationListener();
            Unbind();
            base.DisconnectHandler(platformView);
        }

        private static bool HasCameraPermission() =>
            ContextCompat.CheckSelfPermission(global::Android.App.Application.Context, global::Android.Manifest.Permission.Camera)
            == global::Android.Content.PM.Permission.Granted;

        private void Suspend()
        {
            _isSuspended = true;
            Unbind();
            UnfreezePreview();
        }

        private void Resume()
        {
            _isSuspended = false;
            if (!HasCameraPermission()) return; // the page calls ResumeCamera again once permission is granted

            if (_provider != null)
            {
                Bind();
                return;
            }

            var future = ProcessCameraProvider.GetInstance(Context);
            future.AddListener(new Java.Lang.Runnable(() =>
            {
                try
                {
                    _provider = (ProcessCameraProvider)future.Get()!;
                    if (!_isSuspended) Bind();
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CameraX provider error: {ex.Message}");
                }
            }), ContextCompat.GetMainExecutor(Context));
        }

        private void Bind()
        {
            if (_provider == null || PlatformView == null || _isSuspended) return;
            if (Microsoft.Maui.ApplicationModel.Platform.CurrentActivity is not ILifecycleOwner owner) return;

            try
            {
                _provider.UnbindAll();
                _preview ??= new Preview.Builder().Build();
                _preview.SetSurfaceProvider(ContextCompat.GetMainExecutor(Context), PlatformView.SurfaceProvider);
                _imageCapture ??= new ImageCapture.Builder()
                    .SetCaptureMode(ImageCapture.CaptureModeMinimizeLatency)
                    .Build();

                var selector = _useFrontCamera ? CameraSelector.DefaultFrontCamera : CameraSelector.DefaultBackCamera;
                if (!_provider.HasCamera(selector))
                {
                    _useFrontCamera = !_useFrontCamera;
                    selector = _useFrontCamera ? CameraSelector.DefaultFrontCamera : CameraSelector.DefaultBackCamera;
                }

                // Wide selfie: the widest front camera instead of CameraX's first one (Note 9: id 2 = full sensor)
                if (_useFrontCamera && CameraView.WideSelfie &&
                    Platforms.Android.WideCameraSelector.WidestCameraId(front: true) is { } wideId)
                {
                    var wideSelector = new CameraSelector.Builder()
                        .RequireLensFacing(CameraSelector.LensFacingFront)
                        .AddCameraFilter(new CameraIdFilter(wideId))
                        .Build();
                    if (_provider.HasCamera(wideSelector)) selector = wideSelector;
                }

                _camera = _provider.BindToLifecycle(owner, selector, _preview, _imageCapture);
                _reportedZoomRange = default;
                ReportCapabilities();
                ObserveZoomState(owner);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CameraX bind error: {ex.Message}");
            }
        }

        private void ObserveZoomState(ILifecycleOwner owner)
        {
            StopObservingZoomState();
            if (_camera == null) return;
            _zoomObserver ??= new ZoomStateObserver(this);
            _observedZoomState = _camera.CameraInfo.ZoomState;
            _observedZoomState.Observe(owner, _zoomObserver);
        }

        private void StopObservingZoomState()
        {
            if (_observedZoomState != null && _zoomObserver != null)
                _observedZoomState.RemoveObserver(_zoomObserver);
            _observedZoomState = null;
        }

        internal void OnZoomStateChanged(IZoomState? state)
        {
            if (state == null) return;
            if (System.Math.Abs(state.MinZoomRatio - _reportedZoomRange.Min) > 0.001f ||
                System.Math.Abs(state.MaxZoomRatio - _reportedZoomRange.Max) > 0.001f)
                ReportCapabilities();
        }

        private void Unbind()
        {
            StopObservingZoomState();
            try { _provider?.UnbindAll(); }
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine($"CameraX unbind error: {ex.Message}"); }
            _camera = null;
        }

        private void SwitchCamera()
        {
            _useFrontCamera = !_useFrontCamera;
            Microsoft.Maui.Storage.Preferences.Set(CameraView.UseFrontCameraPreferenceKey, _useFrontCamera);
            Bind();
        }

        private void ReportCapabilities()
        {
            if (_camera == null || VirtualView == null) return;

            var info = _camera.CameraInfo;
            var exposure = info.ExposureState;
            int minExp = 0, maxExp = 0;
            float expStep = 0;
            if (exposure.IsExposureCompensationSupported)
            {
                var range = exposure.ExposureCompensationRange;
                minExp = ((Java.Lang.Integer)range.Lower!).IntValue();
                maxExp = ((Java.Lang.Integer)range.Upper!).IntValue();
                expStep = exposure.ExposureCompensationStep.FloatValue();
            }

            // Zoom ratio per level. CameraX linear zoom is linear in crop width, i.e. in 1/ratio.
            // Java interface values need JavaCast; a C# "as" cast always yields null here
            var zoom = info.ZoomState.Value?.JavaCast<IZoomState>();
            float minRatio = zoom?.MinZoomRatio ?? 1f;
            float maxRatio = zoom?.MaxZoomRatio ?? 1f;
            _reportedZoomRange = (minRatio, maxRatio);
            bool hasZoom = maxRatio > minRatio + 0.01f;
            int[] ratios = hasZoom
                ? Enumerable.Range(0, ZoomLevels + 1)
                    .Select(i => (int)System.Math.Round(100.0 / (1.0 / minRatio + (1.0 / maxRatio - 1.0 / minRatio) * i / ZoomLevels)))
                    .ToArray()
                : new[] { 100 };

            int cameraCount = _provider?.AvailableCameraInfos.Count ?? 1;
            VirtualView.ReportCapabilities(hasZoom ? ZoomLevels : 0, ratios, minExp, maxExp, expStep,
                info.HasFlashUnit, _useFrontCamera, cameraCount);
        }

        // ---- Orientation ----

        private void StartOrientationListener()
        {
            try
            {
                _orientationListener ??= new DelegateOrientationListener(Context, UpdateDeviceOrientation);
                if (_orientationListener.CanDetectOrientation())
                    _orientationListener.Enable();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OrientationListener Start Error: {ex.Message}");
            }
        }

        private void StopOrientationListener()
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

        private void UpdateDeviceOrientation(int orientationDegrees)
        {
            if (orientationDegrees == OrientationEventListener.OrientationUnknown) return;

            // Round to nearest 90° with 10° hysteresis around the 45° boundaries to avoid jitter
            int diff = System.Math.Abs(orientationDegrees - _deviceOrientationDegrees);
            diff = System.Math.Min(diff, 360 - diff);
            if (diff < 55) return;

            int newDegrees = ((orientationDegrees + 45) / 90 * 90) % 360;
            if (newDegrees == _deviceOrientationDegrees) return;
            _deviceOrientationDegrees = newDegrees;
            VirtualView?.OnDeviceOrientationChanged(newDegrees);
        }

        /// <summary>
        /// CW rotation that turns an upright portrait image into the orientation the phone is held in
        /// (or the locked orientation), so the real-world top ends up at the top of the photo.
        /// </summary>
        private int GetDeviceRotationDegrees()
        {
            int device = (VirtualView?.EffectiveOrientationDegrees ?? _deviceOrientationDegrees) % 360;
            return (360 - device) % 360;
        }

        // ---- Focus ----

        private void FocusAt(double nx, double ny)
        {
            if (_camera == null || PlatformView == null || PlatformView.Width == 0 || PlatformView.Height == 0) return;
            try
            {
                // PreviewView's factory maps view coordinates through its own crop/rotation transform
                var point = PlatformView.MeteringPointFactory.CreatePoint((float)(nx * PlatformView.Width), (float)(ny * PlatformView.Height));
                var action = new FocusMeteringAction.Builder(point).Build();
                _camera.CameraControl.StartFocusAndMetering(action);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CameraX focus error: {ex.Message}");
            }
        }

        // ---- Capture ----

        private async void CapturePhoto()
        {
            if (_camera == null || PlatformView == null || _imageCapture == null || _isCapturing) return;

            // Hardware flash only when enabled and the camera has a flash unit (otherwise the page does a screen flash)
            bool useHardwareFlash = VirtualView?.IsFlashEnabled == true && _camera.CameraInfo.HasFlashUnit;

            if (!CameraView.CaptureFromPreview)
            {
                _isCapturing = true;
                try
                {
                    // CameraX keeps the preview running during a still capture: hold the shutter-time frame
                    FreezePreview();
                    _imageCapture.FlashMode = useHardwareFlash ? ImageCapture.FlashModeOn : ImageCapture.FlashModeOff;
                    _imageCapture.TakePicture(ContextCompat.GetMainExecutor(Context), new CaptureCallback(this));
                }
                catch (System.Exception ex)
                {
                    _isCapturing = false;
                    UnfreezePreview();
                    System.Diagnostics.Debug.WriteLine($"CameraX capture error: {ex.Message}");
                }
                return;
            }

            _isCapturing = true;
            try
            {
                if (useHardwareFlash)
                {
                    // A preview frame cannot fire a still flash: light the scene with the torch and let AE settle
                    _camera.CameraControl.EnableTorch(true);
                    await System.Threading.Tasks.Task.Delay(TorchSettleMs);
                }
                CaptureFromPreviewFrame();
            }
            finally
            {
                if (useHardwareFlash) _camera?.CameraControl.EnableTorch(false);
                _isCapturing = false;
            }
        }

        /// <summary>WYSIWYG capture: saves exactly what the preview shows (upright and, for selfies, mirrored).</summary>
        private void CaptureFromPreviewFrame()
        {
            var frame = PlatformView?.Bitmap;
            if (frame == null) return;
            FreezePreview();
            SaveBitmapAsync(frame, GetDeviceRotationDegrees(), mirror: false, sourceRotation: 0);
        }

        // Shows a still snapshot of the preview on top of it from the shutter until the page closes the
        // captured photo (ResumePreview), so the viewfinder doesn't visibly keep moving while the photo is
        // still being saved and decoded for display (noticeable on slower phones such as the Galaxy Note 9).
        private global::Android.Widget.ImageView? _freezeOverlay;

        private void FreezePreview()
        {
            var view = PlatformView;
            var snapshot = view?.Bitmap;
            if (view == null || snapshot == null) return;

            if (_freezeOverlay == null)
            {
                _freezeOverlay = new global::Android.Widget.ImageView(Context);
                _freezeOverlay.SetScaleType(global::Android.Widget.ImageView.ScaleType.FitXy!);
            }

            if (_freezeOverlay.Parent == null)
            {
                view.AddView(_freezeOverlay, new ViewGroup.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            }

            ReleaseFreezeBitmap();
            _freezeOverlay.SetImageBitmap(snapshot);
            _freezeOverlay.Visibility = ViewStates.Visible;
            _freezeOverlay.BringToFront();
        }

        internal void UnfreezePreview()
        {
            if (_freezeOverlay == null) return;
            _freezeOverlay.Visibility = ViewStates.Gone;
            ReleaseFreezeBitmap();
        }

        private void ReleaseFreezeBitmap()
        {
            if (_freezeOverlay?.Drawable is global::Android.Graphics.Drawables.BitmapDrawable { Bitmap: { } old })
            {
                _freezeOverlay.SetImageBitmap(null);
                old.Recycle();
            }
        }

        internal void OnStillCaptured(IImageProxy image)
        {
            try
            {
                var planes = image.GetPlanes();
                var buffer = planes[0].Buffer;
                var bytes = new byte[buffer.Remaining()];
                buffer.Get(bytes);
                int sensorRotation = image.ImageInfo.RotationDegrees;

                var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
                if (bitmap != null)
                    SaveBitmapAsync(bitmap, GetDeviceRotationDegrees(), mirror: _useFrontCamera, sourceRotation: sensorRotation);
                else
                    UnfreezePreview();
            }
            catch (System.Exception ex)
            {
                UnfreezePreview();
                System.Diagnostics.Debug.WriteLine($"CameraX picture processing error: {ex.Message}");
            }
            finally
            {
                image.Close();
                _isCapturing = false;
            }
        }

        internal void OnCaptureFailed(System.Exception ex)
        {
            _isCapturing = false;
            UnfreezePreview();
            System.Diagnostics.Debug.WriteLine($"CameraX capture failed: {ex.Message}");
        }

        /// <summary>
        /// Rotates the source upright (sourceRotation), mirrors selfies to match the preview, then applies the
        /// device-orientation rotation; encodes the JPEG off the UI thread and notifies the page on the main thread.
        /// </summary>
        private void SaveBitmapAsync(Bitmap source, int deviceRotation, bool mirror, int sourceRotation)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using (source)
                    {
                        using var matrix = new global::Android.Graphics.Matrix();
                        matrix.PostRotate(sourceRotation);
                        if (mirror) matrix.PostScale(-1, 1);
                        matrix.PostRotate(deviceRotation);

                        bool identity = sourceRotation == 0 && deviceRotation == 0 && !mirror;
                        var output = identity ? source : Bitmap.CreateBitmap(source, 0, 0, source.Width, source.Height, matrix, true)!;
                        try
                        {
                            var tempPath = System.IO.Path.Combine(Microsoft.Maui.Storage.FileSystem.CacheDirectory, $"capture_{System.Guid.NewGuid()}.jpg");
                            using (var stream = System.IO.File.Create(tempPath))
                                output.Compress(Bitmap.CompressFormat.Jpeg!, 95, stream);

                            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() => VirtualView?.OnPhotoCaptured(tempPath));
                        }
                        finally
                        {
                            if (!ReferenceEquals(output, source)) output.Dispose();
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CameraX encode error: {ex.Message}");
                    Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(UnfreezePreview);
                }
            });
        }

        private void SetZoom(double level)
        {
            if (_camera == null) return;
            float linear = (float)System.Math.Clamp(level / ZoomLevels, 0, 1);
            _camera.CameraControl.SetLinearZoom(linear);
        }

        private void SetExposure(double index)
        {
            if (_camera == null) return;
            var exposure = _camera.CameraInfo.ExposureState;
            if (!exposure.IsExposureCompensationSupported) return;
            int target = (int)System.Math.Round(index);
            if (exposure.ExposureCompensationIndex == target) return;
            _camera.CameraControl.SetExposureCompensationIndex(target);
        }
    }

    internal sealed class ZoomStateObserver : Java.Lang.Object, IObserver
    {
        private readonly System.WeakReference<CameraXViewHandler> _handler;

        public ZoomStateObserver(CameraXViewHandler handler) => _handler = new(handler);

        public void OnChanged(Java.Lang.Object? value)
        {
            if (_handler.TryGetTarget(out var handler))
                handler.OnZoomStateChanged(value?.JavaCast<IZoomState>());
        }
    }

    internal sealed class CaptureCallback : ImageCapture.OnImageCapturedCallback
    {
        private readonly CameraXViewHandler _handler;

        public CaptureCallback(CameraXViewHandler handler) => _handler = handler;

        public override void OnCaptureSuccess(IImageProxy image) => _handler.OnStillCaptured(image);

        public override void OnError(ImageCaptureException exception) => _handler.OnCaptureFailed(exception);
    }

    internal sealed class DelegateOrientationListener : OrientationEventListener
    {
        private readonly System.Action<int> _onChanged;

        public DelegateOrientationListener(Context context, System.Action<int> onChanged) : base(context)
            => _onChanged = onChanged;

        public override void OnOrientationChanged(int orientation) => _onChanged(orientation);
    }
}

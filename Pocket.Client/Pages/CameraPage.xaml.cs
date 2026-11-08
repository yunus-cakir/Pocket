using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Pocket.Client.Controls;
using Pocket.Client.PageModels;

namespace Pocket.Client.Pages
{
    public partial class CameraPage : ContentPage
    {
        private int _currentIndex = 1; // 0=Profile, 1=Camera, 2=Chat
        private double _panX = 0;

        // Velocity tracking for fling detection
        private long _lastPanTime = 0;
        private double _lastPanX = 0;
        private double _velocity = 0;

        public CameraPage(CameraPageModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;

            // Wire horizontal swipe events from the custom Android ViewGroup.
            // Direction detection and touch routing happen at the native level —
            // only confirmed horizontal pans reach this handler.
            SwipeContainer.SwipePanUpdated += OnSwipePanUpdated;
            cameraView.CapabilitiesChanged += OnCameraCapabilitiesChanged;
            cameraView.DeviceOrientationChanged += OnDeviceOrientationChanged;

            viewModel.SnapToProfileAction = async () => await SnapToPanel(0);
            viewModel.SnapToCameraAction = async () => await SnapToPanel(1);
            viewModel.SnapToChatAction = async () => await SnapToPanel(2);
            viewModel.ScrollToHistoryAction = () =>
            {
                if (viewModel.FeedPosts.Count > 0)
                {
                    _isScrollingToCamera = false;
                    viewModel.CurrentFeedPost = viewModel.FeedPosts[0];
#if ANDROID
                    if (FeedCollectionView.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
                    {
                        rv.SmoothScrollToPosition(1);
                        viewModel.IsViewingFeed = true;
                        return;
                    }
#endif
                    FeedCollectionView.ScrollTo(0, position: ScrollToPosition.Start, animate: true);
                    viewModel.IsViewingFeed = true;
                }
            };
            viewModel.ScrollToCameraAction = () =>
            {
                _isScrollingToCamera = true;
                viewModel.IsViewingFeed = false;
#if ANDROID
                if (FeedCollectionView.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
                {
                    rv.SmoothScrollToPosition(0);
                    return;
                }
#endif
                _isScrollingToCamera = false;
            };

            viewModel.OpenActivitySheetAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    ActivityOverlayGrid.IsVisible = true;
                    ActivitySheetCard.TranslationY = 400;
                    ActivityBackdrop.Opacity = 0;
                    await Task.WhenAll(
                        ActivitySheetCard.TranslateToAsync(0, 0, 260, Easing.CubicOut),
                        ActivityBackdrop.FadeToAsync(1, 260, Easing.CubicOut)
                    );
                });
            };

            viewModel.CloseActivitySheetAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(async () => { await AnimateCloseActivitySheet(); });
            };

            viewModel.PreviewStateChangedAction = (isPreviewing) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    SetVerticalFeedScrollEnabled(!isPreviewing);
                    UpdateOrientationLockButtonVisibility(isPreviewing);
                    if (!isPreviewing)
                    {
                        UnfocusCaptionEntry();
                    }
                });
            };
        }

#if ANDROID
        private LockableOnItemTouchListener? _feedScrollLockListener;

        private sealed class LockableOnItemTouchListener : Java.Lang.Object,
            AndroidX.RecyclerView.Widget.RecyclerView.IOnItemTouchListener
        {
            public bool IsScrollLocked { get; set; }
            private float _downX;
            private float _downY;
            private readonly int _touchSlop;

            public LockableOnItemTouchListener(global::Android.Content.Context? context)
            {
                _touchSlop = context != null
                    ? (global::Android.Views.ViewConfiguration.Get(context)?.ScaledTouchSlop ?? 24)
                    : 24;
            }

            public bool OnInterceptTouchEvent(AndroidX.RecyclerView.Widget.RecyclerView rv,
                global::Android.Views.MotionEvent e)
            {
                if (!IsScrollLocked) return false;

                switch (e.ActionMasked)
                {
                    case global::Android.Views.MotionEventActions.Down:
                        _downX = e.GetX();
                        _downY = e.GetY();
                        return false;

                    case global::Android.Views.MotionEventActions.Move:
                        float dx = Math.Abs(e.GetX() - _downX);
                        float dy = Math.Abs(e.GetY() - _downY);
                        if (dy > _touchSlop && dy > dx)
                        {
                            // Intercept vertical drag so RecyclerView cannot scroll down to History while previewing
                            return true;
                        }

                        return false;
                }

                return false;
            }

            public void OnTouchEvent(AndroidX.RecyclerView.Widget.RecyclerView rv, global::Android.Views.MotionEvent e)
            {
                // Consume vertical drag events silently when locked
            }

            public void OnRequestDisallowInterceptTouchEvent(bool disallowIntercept)
            {
            }
        }
#endif

        private void SetVerticalFeedScrollEnabled(bool enabled)
        {
#if ANDROID
            if (FeedCollectionView.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
            {
                if (_feedScrollLockListener == null)
                {
                    _feedScrollLockListener = new LockableOnItemTouchListener(rv.Context);
                    rv.AddOnItemTouchListener(_feedScrollLockListener);
                }

                _feedScrollLockListener.IsScrollLocked = !enabled;
                if (!enabled)
                {
                    rv.StopScroll();
                }
            }
#endif
        }

        private void OnCaptionPillTapped(object? sender, TappedEventArgs e)
        {
            CaptionEntry.Focus();
        }

        private void OnPreviewImageTapped(object? sender, TappedEventArgs e)
        {
            UnfocusCaptionEntry();
        }

        private void OnCaptionEntryFocused(object? sender, FocusEventArgs e)
        {
            if (BindingContext is CameraPageModel vm)
            {
                vm.IsEditingCaption = true;
            }
#if ANDROID
            RegisterKeyboardVisibilityWatcher();
#endif
        }

        private void OnCaptionEntryUnfocused(object? sender, FocusEventArgs e)
        {
            if (BindingContext is CameraPageModel vm)
            {
                vm.IsEditingCaption = false;
            }
        }

#if ANDROID
        private KeyboardGlobalLayoutListener? _keyboardListener;

        private void RegisterKeyboardVisibilityWatcher()
        {
            var activity = Platform.CurrentActivity;
            var decorView = activity?.Window?.DecorView;
            if (decorView?.ViewTreeObserver == null || _keyboardListener != null)
                return;

            _keyboardListener = new KeyboardGlobalLayoutListener(decorView, () =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (BindingContext is CameraPageModel vm && vm.IsEditingCaption)
                    {
                        UnfocusCaptionEntry();
                    }
                });
            });
            decorView.ViewTreeObserver.AddOnGlobalLayoutListener(_keyboardListener);
        }

        private sealed class KeyboardGlobalLayoutListener : Java.Lang.Object,
            global::Android.Views.ViewTreeObserver.IOnGlobalLayoutListener
        {
            private readonly global::Android.Views.View _decorView;
            private readonly Action _onKeyboardHidden;
            private bool _wasKeyboardOpen;

            public KeyboardGlobalLayoutListener(global::Android.Views.View decorView, Action onKeyboardHidden)
            {
                _decorView = decorView;
                _onKeyboardHidden = onKeyboardHidden;
            }

            public void OnGlobalLayout()
            {
                var rect = new global::Android.Graphics.Rect();
                _decorView.GetWindowVisibleDisplayFrame(rect);
                int screenHeight = _decorView.RootView?.Height ?? 0;
                if (screenHeight <= 0) return;

                int keypadHeight = screenHeight - rect.Bottom;
                bool isKeyboardOpen = keypadHeight > screenHeight * 0.15;

                if (_wasKeyboardOpen && !isKeyboardOpen)
                {
                    _wasKeyboardOpen = false;
                    _onKeyboardHidden.Invoke();
                }
                else if (isKeyboardOpen)
                {
                    _wasKeyboardOpen = true;
                }
            }
        }
#endif

        private void OnCaptionEntryCompleted(object? sender, EventArgs e)
        {
            UnfocusCaptionEntry();
        }

        private void UnfocusCaptionEntry()
        {
            if (CaptionEntry.IsFocused)
            {
                CaptionEntry.Unfocus();
            }
#if ANDROID
            if (CaptionEntry.Handler?.PlatformView is global::Android.Views.View nativeView)
            {
                nativeView.ClearFocus();
                var imm =
                    nativeView.Context?.GetSystemService(global::Android.Content.Context.InputMethodService) as
                        global::Android.Views.InputMethods.InputMethodManager;
                imm?.HideSoftInputFromWindow(nativeView.WindowToken,
                    global::Android.Views.InputMethods.HideSoftInputFlags.None);
            }
#endif
            if (BindingContext is CameraPageModel vm)
            {
                vm.IsEditingCaption = false;
            }
        }

        private double _sheetPanInitialTranslationY = 0;

        private async void OnActivityBackdropTapped(object? sender, TappedEventArgs e)
        {
            await AnimateCloseActivitySheet();
        }

        private async Task AnimateCloseActivitySheet()
        {
            if (!ActivityOverlayGrid.IsVisible)
                return;

            await Task.WhenAll(
                ActivitySheetCard.TranslateToAsync(0, 420, 220, Easing.CubicIn),
                ActivityBackdrop.FadeToAsync(0, 220, Easing.Linear)
            );
            ActivityOverlayGrid.IsVisible = false;
            if (BindingContext is CameraPageModel vm)
            {
                vm.IsActivitySheetOpen = false;
            }
        }

        private async void OnActivitySheetPanUpdated(object? sender, PanUpdatedEventArgs e)
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _sheetPanInitialTranslationY = ActivitySheetCard.TranslationY;
                    break;

                case GestureStatus.Running:
                    if (e.TotalY > 0)
                    {
                        ActivitySheetCard.TranslationY = _sheetPanInitialTranslationY + e.TotalY;
                        double progress = Math.Clamp(1.0 - (ActivitySheetCard.TranslationY / 400.0), 0.0, 1.0);
                        ActivityBackdrop.Opacity = progress;
                    }

                    break;

                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    // "Biraz aşağı kaydırılıp bırakılırsa otomatik olarak kapanacak şekilde devamında kendi kaysın."
                    if (ActivitySheetCard.TranslationY > 70)
                    {
                        await AnimateCloseActivitySheet();
                    }
                    else
                    {
                        await Task.WhenAll(
                            ActivitySheetCard.TranslateToAsync(0, 0, 160, Easing.CubicOut),
                            ActivityBackdrop.FadeToAsync(1, 160, Easing.Linear)
                        );
                    }

                    break;
            }
        }

        private bool _isScrollingToCamera;

#if ANDROID
        private AndroidX.RecyclerView.Widget.RecyclerView? _attachedFeedRecyclerView;
        private FeedNativeScrollListener? _feedNativeScrollListener;

        private sealed class FeedNativeScrollListener : AndroidX.RecyclerView.Widget.RecyclerView.OnScrollListener
        {
            private readonly CameraPage _page;

            public FeedNativeScrollListener(CameraPage page)
            {
                _page = page;
            }

            public override void OnScrolled(AndroidX.RecyclerView.Widget.RecyclerView recyclerView, int dx, int dy)
            {
                base.OnScrolled(recyclerView, dx, dy);
                _page.UpdateViewingFeedStateFromRecyclerView(recyclerView);
            }

            public override void OnScrollStateChanged(AndroidX.RecyclerView.Widget.RecyclerView recyclerView,
                int newState)
            {
                base.OnScrollStateChanged(recyclerView, newState);
                if (newState == AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateDragging)
                {
                    _page._isScrollingToCamera = false;
                }
                else if (newState == AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateIdle)
                {
                    if (_page._isScrollingToCamera)
                    {
                        _page._isScrollingToCamera = false;
                        recyclerView.ScrollToPosition(0);
                    }

                    _page.UpdateViewingFeedStateFromRecyclerView(recyclerView);
                }
            }
        }

        private void OnNativeFeedLayoutChange(object? sender, global::Android.Views.View.LayoutChangeEventArgs e)
        {
            if (sender is AndroidX.RecyclerView.Widget.RecyclerView rv)
            {
                UpdateViewingFeedStateFromRecyclerView(rv);
            }
        }

        private void UpdateViewingFeedStateFromRecyclerView(AndroidX.RecyclerView.Widget.RecyclerView rv)
        {
            if (BindingContext is not CameraPageModel vm || rv.Height <= 0)
                return;

            if (vm.FeedPosts.Count == 0 || rv.ChildCount == 0)
            {
                _isScrollingToCamera = false;
                if (vm.IsViewingFeed)
                    vm.IsViewingFeed = false;
                return;
            }

            // Adapter position 0 is CollectionView.Header (Camera view / Home).
            // Check if the Camera Header view itself is visible and covers more than half the screen.
            if (rv.GetLayoutManager() is AndroidX.RecyclerView.Widget.LinearLayoutManager llm)
            {
                var headerView = llm.FindViewByPosition(0);
                if (headerView != null && headerView.Bottom >= rv.Height * 0.55f)
                {
                    _isScrollingToCamera = false;
                    if (vm.IsViewingFeed)
                        vm.IsViewingFeed = false;
                    return;
                }
            }

            int centerY = rv.Height / 2;
            int centerAdapterPos = -1;

            for (int i = 0; i < rv.ChildCount; i++)
            {
                var child = rv.GetChildAt(i);
                if (child != null && child.Top <= centerY && child.Bottom >= centerY)
                {
                    centerAdapterPos = rv.GetChildAdapterPosition(child);
                    break;
                }
            }

            if (centerAdapterPos < 0 &&
                rv.GetLayoutManager() is AndroidX.RecyclerView.Widget.LinearLayoutManager fallbackLlm)
            {
                centerAdapterPos = fallbackLlm.FindFirstVisibleItemPosition();
            }

            if (centerAdapterPos <= 0)
            {
                _isScrollingToCamera = false;
                if (vm.IsViewingFeed)
                    vm.IsViewingFeed = false;
                return;
            }

            // While smooth-scrolling back up to the Camera Header (position 0), do not re-enable IsViewingFeed
            if (_isScrollingToCamera)
            {
                if (vm.IsViewingFeed)
                    vm.IsViewingFeed = false;
                return;
            }

            if (!vm.IsViewingFeed)
            {
                vm.IsViewingFeed = true;
            }

            int postIndex = Math.Clamp(centerAdapterPos - 1, 0, vm.FeedPosts.Count - 1);
            var post = vm.FeedPosts[postIndex];
            if (!ReferenceEquals(vm.CurrentFeedPost, post))
            {
                vm.CurrentFeedPost = post;
            }
        }
#endif

        private void OnFeedCollectionViewHandlerChanged(object? sender, EventArgs e)
        {
#if ANDROID
            if (_attachedFeedRecyclerView != null)
            {
                if (_feedNativeScrollListener != null)
                    _attachedFeedRecyclerView.RemoveOnScrollListener(_feedNativeScrollListener);
                _attachedFeedRecyclerView.LayoutChange -= OnNativeFeedLayoutChange;
                _attachedFeedRecyclerView = null;
            }

            if (FeedCollectionView.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
            {
                _attachedFeedRecyclerView = rv;
                _feedNativeScrollListener ??= new FeedNativeScrollListener(this);
                rv.AddOnScrollListener(_feedNativeScrollListener);
                rv.LayoutChange += OnNativeFeedLayoutChange;
                rv.Post(() => UpdateViewingFeedStateFromRecyclerView(rv));
            }
#endif
        }

        private void OnFeedScrolled(object? sender, ItemsViewScrolledEventArgs e)
        {
#if ANDROID
            if (FeedCollectionView.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
            {
                UpdateViewingFeedStateFromRecyclerView(rv);
                return;
            }
#endif
            if (BindingContext is CameraPageModel vm && this.Height > 0)
            {
                if (vm.FeedPosts.Count == 0 || e.FirstVisibleItemIndex <= 0 && e.VerticalOffset <= (this.Height * 0.45))
                {
                    vm.IsViewingFeed = false;
                    return;
                }

                bool viewingFeed = e.VerticalOffset > (this.Height * 0.45);
                vm.IsViewingFeed = viewingFeed;
                if (viewingFeed && vm.FeedPosts.Count > 0)
                {
                    int postIndex = (int)Math.Round(e.VerticalOffset / this.Height) - 1;
                    postIndex = Math.Clamp(postIndex, 0, vm.FeedPosts.Count - 1);
                    vm.CurrentFeedPost = vm.FeedPosts[postIndex];
                }
            }
        }

        protected override void OnSizeAllocated(double width, double height)
        {
            base.OnSizeAllocated(width, height);
            if (width > 0)
            {
                // Each panel takes full width
                ProfilePanel.WidthRequest = width;
                CameraPanel.WidthRequest = width;
                ChatPanel.WidthRequest = width;

                // Ensure the panning container doesn't get clamped to 1x screen width by the ContentView
                PanContainer.WidthRequest = width * 3;

                // Immediately jump to the correct panel without animation
                PanContainer.TranslationX = -(_currentIndex * width);

                if (height > 0 && BindingContext is CameraPageModel vm)
                {
                    vm.ViewportHeight = height;
                    vm.CardSize = Math.Min(width - 12, Math.Max(320, height - 350));
                    CameraHeaderGrid.WidthRequest = width;
                    CameraHeaderGrid.HeightRequest = height;
                }
            }
        }

        private async Task SnapToPanel(int index)
        {
            _currentIndex = Math.Max(0, Math.Min(2, index));
            if (BindingContext is CameraPageModel vm)
            {
                vm.ActivePanelIndex = _currentIndex;
            }

            double targetX = -(_currentIndex * this.Width);

            // Dynamic animation duration based on distance to feel natural
            double distance = Math.Abs(PanContainer.TranslationX - targetX);
            uint duration = (uint)Math.Max(100, Math.Min(250, (distance / Math.Max(1, this.Width)) * 250));

            // Show camera while animating
            cameraView.IsVisible = true;
            await PanContainer.TranslateTo(targetX, 0, duration, Easing.SinOut);

            // Hide camera if off-screen to save resources
            if (_currentIndex != 1)
            {
                HideCameraSliders(animated: false);
                cameraView.IsVisible = false;
            }
        }

        /// <summary>
        /// Handles horizontal pan events from <see cref="HorizontalSwipeView"/>.
        /// Only horizontal swipes reach this handler — taps and vertical gestures are
        /// routed to child views (shutter button, CollectionView) at the native level.
        /// No directional-lock logic needed here.
        /// </summary>
        private async void OnSwipePanUpdated(object? sender, SwipePanEventArgs e)
        {
            long currentTime = Stopwatch.GetTimestamp();

            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _panX = PanContainer.TranslationX;
                    _lastPanTime = currentTime;
                    _lastPanX = 0;
                    _velocity = 0;
                    cameraView.IsVisible = true;
                    break;

                case GestureStatus.Running:
                    // Calculate instantaneous velocity for fling detection
                    double deltaX = e.TotalX - _lastPanX;
                    long deltaTime = currentTime - _lastPanTime;
                    if (deltaTime > 0)
                    {
                        _velocity = deltaX / (double)deltaTime * Stopwatch.Frequency;
                    }

                    _lastPanTime = currentTime;
                    _lastPanX = e.TotalX;

                    // Apply horizontal translation with clamping
                    var newTranslation = _panX + e.TotalX;
                    newTranslation = Math.Max(-this.Width * 2, Math.Min(0, newTranslation));
                    PanContainer.TranslationX = newTranslation;

                    if (this.Width > 0 && BindingContext is CameraPageModel panVm)
                    {
                        int nearestIndex = (int)Math.Round(-newTranslation / this.Width);
                        nearestIndex = Math.Clamp(nearestIndex, 0, 2);
                        if (panVm.ActivePanelIndex != nearestIndex)
                        {
                            panVm.ActivePanelIndex = nearestIndex;
                        }
                    }

                    break;

                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    double currentTranslation = PanContainer.TranslationX;
                    double width = this.Width;

                    // Fling / Velocity snap override
                    if (Math.Abs(_velocity) > 800)
                    {
                        if (_velocity < 0) // Swiping Left -> Next Index
                            await SnapToPanel(_currentIndex + 1);
                        else // Swiping Right -> Prev Index
                            await SnapToPanel(_currentIndex - 1);
                        return;
                    }

                    // Distance-based snapping (50% threshold)
                    double threshold = width / 2;

                    if (currentTranslation > -threshold)
                        await SnapToPanel(0);
                    else if (currentTranslation > -(width + threshold))
                        await SnapToPanel(1);
                    else
                        await SnapToPanel(2);
                    break;
            }
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            cameraView.PhotoCaptured += OnPhotoCaptured;

            if (BindingContext is CameraPageModel vm)
            {
                await vm.LoadFeedAsync();
#if ANDROID
                if (FeedCollectionView.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
                {
                    UpdateViewingFeedStateFromRecyclerView(rv);
                    rv.Post(() => UpdateViewingFeedStateFromRecyclerView(rv));
                }
#endif
            }

            await CheckAndRequestCameraPermission();
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            cameraView.PhotoCaptured -= OnPhotoCaptured;
        }

        private async Task CheckAndRequestCameraPermission()
        {
            var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.Camera>();
            }
        }

        private bool _isCapturingWithFlash;
        private bool _isPickingFromGallery;

        private async void OnGalleryTapped(object? sender, TappedEventArgs e)
        {
            if (_isPickingFromGallery) return;
            if (BindingContext is CameraPageModel vm && vm.IsPreviewing) return;

            HideCameraSliders(animated: false);
            _isPickingFromGallery = true;

            try
            {
                string? tempPath = await PickPhotoToTempCacheAsync();
                if (!string.IsNullOrEmpty(tempPath) && System.IO.File.Exists(tempPath))
                {
                    OnPhotoCaptured(this, tempPath);
                    OnPhotoCapturedCore(tempPath, isFromGallery: true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PhotoPicker] Failed to pick photo: {ex.Message}");
            }
            finally
            {
                _isPickingFromGallery = false;
            }
        }

        private static async Task<string?> PickPhotoToTempCacheAsync()
        {
#if ANDROID
            var uri = await MainActivity.PickSinglePhotoAsync();
            if (uri == null)
                return null;

            var resolver = Platform.CurrentActivity?.ContentResolver
                           ?? global::Android.App.Application.Context?.ContentResolver;
            if (resolver == null)
                return null;

            return await Task.Run(() =>
            {
                int rotationDegrees = 0;
                try
                {
                    if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.N)
                    {
                        using var exifStream = resolver.OpenInputStream(uri);
                        if (exifStream != null)
                        {
                            var exif = new global::Android.Media.ExifInterface(exifStream);
                            int orientation = exif.GetAttributeInt(
                                global::Android.Media.ExifInterface.TagOrientation,
                                (int)global::Android.Media.Orientation.Normal);

                            rotationDegrees = orientation switch
                            {
                                (int)global::Android.Media.Orientation.Rotate90 => 90,
                                (int)global::Android.Media.Orientation.Rotate180 => 180,
                                (int)global::Android.Media.Orientation.Rotate270 => 270,
                                _ => 0
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PhotoPicker] EXIF read skipped: {ex.Message}");
                }

                var tempPath = System.IO.Path.Combine(
                    Microsoft.Maui.Storage.FileSystem.CacheDirectory,
                    $"picked_{Guid.NewGuid()}.jpg");

                using var inputStream = resolver.OpenInputStream(uri);
                if (inputStream == null)
                    return null;

                using var decoded = global::Android.Graphics.BitmapFactory.DecodeStream(inputStream);
                if (decoded == null)
                {
                    using var fallbackIn = resolver.OpenInputStream(uri);
                    if (fallbackIn == null) return null;
                    using var outFile = System.IO.File.Create(tempPath);
                    fallbackIn.CopyTo(outFile);
                    return tempPath;
                }

                global::Android.Graphics.Bitmap finalBitmap = decoded;
                try
                {
                    int w = decoded.Width;
                    int h = decoded.Height;
                    const int maxDim = 1920;
                    float scale = (w > maxDim || h > maxDim)
                        ? Math.Min((float)maxDim / w, (float)maxDim / h)
                        : 1f;

                    if (rotationDegrees != 0 || scale < 0.999f)
                    {
                        using var matrix = new global::Android.Graphics.Matrix();
                        if (scale < 0.999f)
                            matrix.PostScale(scale, scale);
                        if (rotationDegrees != 0)
                            matrix.PostRotate(rotationDegrees);

                        finalBitmap = global::Android.Graphics.Bitmap.CreateBitmap(
                            decoded, 0, 0, w, h, matrix, true)!;
                    }

                    using var outStream = System.IO.File.Create(tempPath);
                    finalBitmap.Compress(global::Android.Graphics.Bitmap.CompressFormat.Jpeg!, 95, outStream);
                }
                finally
                {
                    if (!ReferenceEquals(finalBitmap, decoded))
                        finalBitmap.Dispose();
                }

                return tempPath;
            });
#else
            var result = await Microsoft.Maui.Media.MediaPicker.Default.PickPhotoAsync();
            if (result == null)
                return null;

            var tempPath = System.IO.Path.Combine(
                Microsoft.Maui.Storage.FileSystem.CacheDirectory,
                $"picked_{Guid.NewGuid()}.jpg");

            await using var inStream = await result.OpenReadAsync();
            await using var outStream = System.IO.File.Create(tempPath);
            await inStream.CopyToAsync(outStream);
            return tempPath;
#endif
        }

        private void OnFlashTapped(object? sender, TappedEventArgs e)
        {
            cameraView.IsFlashEnabled = !cameraView.IsFlashEnabled;
            UpdateFlashButtonAppearance();
        }

        private void UpdateFlashButtonAppearance()
        {
            bool on = cameraView.IsFlashEnabled;
            FlashButtonBorder.BackgroundColor = on
                ? Color.FromArgb("#F5B301")
                : Color.FromArgb("#4D000000");
            FlashButtonIcon.Fill = new SolidColorBrush(on
                ? Color.FromArgb("#000000")
                : Color.FromArgb("#FFFFFF"));
        }

        private const int OrientationLockAutoHideMs = 5000;
        private System.Threading.CancellationTokenSource? _orientationLockHideCts;
        private bool _isOrientationLockShown;

        private void OnDeviceOrientationChanged(object? sender, int degrees)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!cameraView.IsOrientationLocked)
                {
                    RotateCameraButtonsToOrientation(degrees);
                }

                if (BindingContext is CameraPageModel vm && vm.IsPreviewing)
                    return;

                ShowOrientationLockButton();
                if (!cameraView.IsOrientationLocked)
                {
                    ScheduleOrientationLockAutoHide();
                }
            });
        }

        private void OnOrientationLockTapped(object? sender, TappedEventArgs e)
        {
            bool newLocked = !cameraView.IsOrientationLocked;
            cameraView.SetOrientationLock(newLocked);
            UpdateOrientationLockButtonAppearance();

            if (newLocked)
            {
                // Pause auto-hide while locked so the yellow lock icon stays visible until unlocked
                _orientationLockHideCts?.Cancel();
                ShowOrientationLockButton();
            }
            else
            {
                // Unlocked: rotate buttons to the current physical orientation and start the 5-second auto-hide countdown
                RotateCameraButtonsToOrientation(cameraView.DeviceOrientationDegrees);
                ScheduleOrientationLockAutoHide();
            }
        }

        private void RotateCameraButtonsToOrientation(int deviceOrientationDegrees)
        {
            // Device orientation is CW tilt of the phone (90 = right side down, 270 = left side down),
            // so counter-rotating by -deviceOrientationDegrees keeps the icons upright in real-world space.
            double targetAngle = (360 - (deviceOrientationDegrees % 360)) % 360;
            bool isLandscape = (deviceOrientationDegrees % 180) != 0;

            // Reserve vertical space for the rotated pill badges in landscape so they don't overlap
            // the reset buttons or slider tracks while keeping the ZoomSlider track at full 180px height.
            ZoomValueBadgeSlot.HeightRequest = isLandscape ? 42 : 24;
            ExposureValueBadgeSlot.HeightRequest = isLandscape ? 54 : 24;

            _ = RotateElementShortestPathAsync(FlashButtonBorder, targetAngle);
            _ = RotateElementShortestPathAsync(OrientationLockButtonBorder, targetAngle);
            _ = RotateElementShortestPathAsync(ZoomResetButton, targetAngle);
            _ = RotateElementShortestPathAsync(ZoomValueBadge, targetAngle);
            _ = RotateElementShortestPathAsync(ExposureResetButton, targetAngle);
            _ = RotateElementShortestPathAsync(ExposureValueBadge, targetAngle);
        }

        private static async Task RotateElementShortestPathAsync(VisualElement element, double targetAngle)
        {
            element.AbortAnimation("RotateTo");
            double current = ((element.Rotation % 360) + 360) % 360;
            double delta = targetAngle - current;
            if (delta > 180)
                delta -= 360;
            else if (delta < -180)
                delta += 360;

            if (Math.Abs(delta) < 0.5)
            {
                element.Rotation = targetAngle;
                return;
            }

            await element.RotateToAsync(element.Rotation + delta, 220, Easing.CubicOut);
            element.Rotation = targetAngle;
        }

        private void UpdateOrientationLockButtonAppearance()
        {
            bool locked = cameraView.IsOrientationLocked;
            OrientationLockButtonBorder.BackgroundColor = locked
                ? Color.FromArgb("#F5B301")
                : Color.FromArgb("#4D000000");
            OrientationLockButtonIcon.Fill = new SolidColorBrush(locked
                ? Color.FromArgb("#000000")
                : Color.FromArgb("#FFFFFF"));
        }

        private void ShowOrientationLockButton()
        {
            _isOrientationLockShown = true;
            if (BindingContext is CameraPageModel vm && vm.IsPreviewing)
            {
                OrientationLockButtonBorder.IsVisible = false;
                return;
            }

            OrientationLockButtonBorder.AbortAnimation("FadeTo");
            OrientationLockButtonBorder.IsVisible = true;
            _ = OrientationLockButtonBorder.FadeToAsync(1, 150);
        }

        private void HideOrientationLockButton(bool animated = true)
        {
            _orientationLockHideCts?.Cancel();
            _isOrientationLockShown = false;

            if (!OrientationLockButtonBorder.IsVisible)
                return;

            if (animated)
            {
                _ = FadeOutAsync(OrientationLockButtonBorder);
            }
            else
            {
                OrientationLockButtonBorder.AbortAnimation("FadeTo");
                OrientationLockButtonBorder.Opacity = 0;
                OrientationLockButtonBorder.IsVisible = false;
            }
        }

        private void UpdateOrientationLockButtonVisibility(bool isPreviewing)
        {
            if (isPreviewing)
            {
                OrientationLockButtonBorder.AbortAnimation("FadeTo");
                OrientationLockButtonBorder.IsVisible = false;
            }
            else if (cameraView.IsOrientationLocked || _isOrientationLockShown)
            {
                OrientationLockButtonBorder.IsVisible = true;
                OrientationLockButtonBorder.Opacity = 1;
            }
        }

        private async void ScheduleOrientationLockAutoHide()
        {
            _orientationLockHideCts?.Cancel();
            var cts = new System.Threading.CancellationTokenSource();
            _orientationLockHideCts = cts;

            try
            {
                await Task.Delay(OrientationLockAutoHideMs, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (!cameraView.IsOrientationLocked && _orientationLockHideCts == cts)
            {
                HideOrientationLockButton();
            }
        }

        private async void OnFlipCameraTapped(object? sender, TappedEventArgs e)
        {
            if (BindingContext is CameraPageModel vm && vm.IsPreviewing) return;

            HideCameraSliders(animated: false);
            cameraView.SwitchCamera();

            FlipCameraButton.AbortAnimation("RotateTo");
            await FlipCameraButton.RotateToAsync(FlipCameraButton.Rotation + 180, 220, Easing.CubicOut);
        }

        private async void OnShutterTapped(object? sender, TappedEventArgs e)
        {
            if (_isCapturingWithFlash) return;
            HideCameraSliders(animated: false);

            // When flash is enabled but the active camera has no hardware flash (front camera or
            // emulator virtual camera), use a full-screen white overlay + max screen brightness
            // as a "screen flash" before grabbing the frame.
            if (cameraView.IsFlashEnabled && !cameraView.HasFlash)
            {
                _isCapturingWithFlash = true;
                try
                {
                    await CaptureWithScreenFlashAsync();
                }
                finally
                {
                    _isCapturingWithFlash = false;
                }

                return;
            }

            cameraView.CapturePhoto();
        }

        private async Task CaptureWithScreenFlashAsync()
        {
#if ANDROID
            var window = Platform.CurrentActivity?.Window;
            float savedBrightness = -1f;
            if (window?.Attributes is global::Android.Views.WindowManagerLayoutParams lp)
            {
                savedBrightness = lp.ScreenBrightness;
                lp.ScreenBrightness = 1.0f;
                window.Attributes = lp;
            }
#endif
            try
            {
                ScreenFlashOverlay.AbortAnimation("FadeTo");
                ScreenFlashOverlay.Opacity = 0;
                ScreenFlashOverlay.IsVisible = true;
                await ScreenFlashOverlay.FadeToAsync(0.95, 90, Easing.CubicOut);
                await Task.Delay(320); // let face/scene illumination + auto-exposure settle

                cameraView.CapturePhoto();

                await Task.Delay(120);
                await ScreenFlashOverlay.FadeToAsync(0, 220, Easing.CubicIn);
            }
            finally
            {
                ScreenFlashOverlay.IsVisible = false;
                ScreenFlashOverlay.Opacity = 0;
#if ANDROID
                if (window?.Attributes is global::Android.Views.WindowManagerLayoutParams restoreLp)
                {
                    restoreLp.ScreenBrightness = savedBrightness;
                    window.Attributes = restoreLp;
                }
#endif
            }
        }

        // ================================================================
        // Tap-to-focus + optional Zoom / Exposure sliders (Settings > General)
        // ================================================================

        private const int SliderAutoHideMs = 4000;
        private System.Threading.CancellationTokenSource? _sliderHideCts;
        private bool _isDraggingSlider;
        private int _focusRingToken;

        private void OnCameraCapabilitiesChanged(object? sender, EventArgs e)
        {
            Dispatcher.Dispatch(() =>
            {
                // Camera (re)opened: zoom and exposure start from their defaults
                ZoomSlider.Minimum = 0;
                ZoomSlider.Maximum = Math.Max(1, cameraView.MaxZoom);
                ZoomSlider.Value = 0;

                ExposureSlider.Minimum = cameraView.MinExposure;
                ExposureSlider.Maximum = cameraView.MaxExposure;
                ExposureSlider.Value = 0;

                double zoomLevel = 0;
                if (CameraView.KeepLastZoom && cameraView.MaxZoom > 0)
                {
                    zoomLevel = Math.Clamp(Preferences.Get(CameraView.LastZoomLevelPreferenceKey, 0d), 0,
                        cameraView.MaxZoom);
                    ZoomSlider.Value = zoomLevel;
                    if (zoomLevel != 0) cameraView.SetZoom(zoomLevel);
                }

                double exposureIndex = 0;
                if (CameraView.KeepLastExposure && cameraView.MaxExposure > cameraView.MinExposure)
                {
                    exposureIndex = Math.Clamp(Preferences.Get(CameraView.LastExposureIndexPreferenceKey, 0d),
                        cameraView.MinExposure, cameraView.MaxExposure);
                    ExposureSlider.Value = exposureIndex;
                    if (exposureIndex != 0) cameraView.SetExposure(exposureIndex);
                }

                UpdateZoomLabel(zoomLevel);
                UpdateExposureLabel(exposureIndex);
            });
        }

        private void OnCameraViewTapped(object? sender, TappedEventArgs e)
        {
            if (BindingContext is CameraPageModel vm && vm.IsPreviewing) return;

            var pos = e.GetPosition(cameraView);
            double w = cameraView.Width, h = cameraView.Height;
            if (pos == null || w <= 0 || h <= 0) return;

            cameraView.FocusAt(pos.Value.X / w, pos.Value.Y / h);
            _ = ShowFocusRingAsync(pos.Value);

            // New focus/metering point → exposure compensation starts again from 0 EV (unless 'keep last exposure' is on)
            if (!CameraView.KeepLastExposure && cameraView.MaxExposure > cameraView.MinExposure)
                ExposureSlider.Value = 0;

            ShowCameraSliders();
        }

        private async Task ShowFocusRingAsync(Point center)
        {
            int token = ++_focusRingToken;
            FocusRing.AbortAnimation("ScaleTo");
            FocusRing.TranslationX = center.X - FocusRing.WidthRequest / 2;
            FocusRing.TranslationY = center.Y - FocusRing.HeightRequest / 2;
            FocusRing.Opacity = 1;
            FocusRing.Scale = 1.4;
            FocusRing.IsVisible = true;

            await FocusRing.ScaleToAsync(1, 180, Easing.CubicOut);
            await Task.Delay(900);
            if (token != _focusRingToken) return;

            await FocusRing.FadeToAsync(0, 250);
            if (token == _focusRingToken) FocusRing.IsVisible = false;
        }

        private void ShowCameraSliders()
        {
            bool showZoom = CameraView.ZoomSliderEnabled && cameraView.MaxZoom > 0;
            bool showExposure = CameraView.ExposureSliderEnabled && cameraView.MaxExposure > cameraView.MinExposure;

            SetPanelVisible(ZoomSliderPanel, showZoom);
            SetPanelVisible(ExposureSliderPanel, showExposure);

            if (showZoom || showExposure)
                ScheduleSliderAutoHide();
        }

        private static void SetPanelVisible(VisualElement panel, bool visible)
        {
            panel.AbortAnimation("FadeTo");
            if (visible)
            {
                panel.IsVisible = true;
                _ = panel.FadeToAsync(1, 150);
            }
            else
            {
                panel.Opacity = 0;
                panel.IsVisible = false;
            }
        }

        private void HideCameraSliders(bool animated = true)
        {
            _sliderHideCts?.Cancel();
            _isDraggingSlider = false;

            foreach (var panel in new VisualElement[] { ZoomSliderPanel, ExposureSliderPanel })
            {
                if (!panel.IsVisible) continue;
                if (animated)
                {
                    _ = FadeOutAsync(panel);
                }
                else
                {
                    panel.AbortAnimation("FadeTo");
                    panel.Opacity = 0;
                    panel.IsVisible = false;
                }
            }
        }

        private static async Task FadeOutAsync(VisualElement panel)
        {
            bool finished = await panel.FadeToAsync(0, 200);
            if (finished) panel.IsVisible = false;
        }

        private async void ScheduleSliderAutoHide()
        {
            _sliderHideCts?.Cancel();
            var cts = new System.Threading.CancellationTokenSource();
            _sliderHideCts = cts;

            try
            {
                await Task.Delay(SliderAutoHideMs, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (!_isDraggingSlider && _sliderHideCts == cts)
                HideCameraSliders();
        }

        private void OnSliderDragStarted(object? sender, EventArgs e)
        {
            _isDraggingSlider = true;
            _sliderHideCts?.Cancel();
        }

        private void OnSliderDragCompleted(object? sender, EventArgs e)
        {
            _isDraggingSlider = false;
            ScheduleSliderAutoHide();
        }

        private void OnResetZoomTapped(object? sender, EventArgs e)
        {
            ZoomSlider.Value = 0;
            cameraView.SetZoom(0);
            Preferences.Set(CameraView.LastZoomLevelPreferenceKey, 0d);
            UpdateZoomLabel(0);
            ScheduleSliderAutoHide();
        }

        private void OnResetExposureTapped(object? sender, EventArgs e)
        {
            ExposureSlider.Value = 0;
            cameraView.SetExposure(0);
            Preferences.Set(CameraView.LastExposureIndexPreferenceKey, 0d);
            UpdateExposureLabel(0);
            ScheduleSliderAutoHide();
        }

        private void OnZoomSliderValueChanged(object? sender, double value)
        {
            // Continuous value (no rounding) so zoom follows the finger smoothly
            cameraView.SetZoom(value);
            Preferences.Set(CameraView.LastZoomLevelPreferenceKey, value);
            UpdateZoomLabel(value);
        }

        private void OnExposureSliderValueChanged(object? sender, double value)
        {
            // Continuous value (no rounding) so the preview brightness follows the finger smoothly
            cameraView.SetExposure(value);
            Preferences.Set(CameraView.LastExposureIndexPreferenceKey, value);
            UpdateExposureLabel(value);
        }

        private void UpdateZoomLabel(double level)
        {
            var ratios = cameraView.ZoomRatios;
            double ratio = 1.0;
            if (ratios.Count > 0)
            {
                double clamped = Math.Clamp(level, 0, ratios.Count - 1);
                int lower = (int)Math.Floor(clamped);
                int upper = Math.Min(lower + 1, ratios.Count - 1);
                double t = clamped - lower;
                ratio = (ratios[lower] + (ratios[upper] - ratios[lower]) * t) / 100.0;
            }

            ZoomValueLabel.Text = $"{ratio:0.0}x";
        }

        private void UpdateExposureLabel(double index)
        {
            double ev = index * cameraView.ExposureStep;
            ExposureValueLabel.Text = ev > 0.05 ? $"+{ev:0.0} EV" : ev < -0.05 ? $"{ev:0.0} EV" : "0.0 EV";
        }

        private void OnPhotoCaptured(object? sender, string filePath)
        {
            OnPhotoCapturedCore(filePath, isFromGallery: false);
        }

        private void OnPhotoCapturedCore(string filePath, bool isFromGallery)
        {
            Dispatcher.Dispatch(() =>
            {
                if (BindingContext is CameraPageModel vm)
                {
                    vm.OnPhotoCaptured(filePath, isFromGallery);
                }
            });
        }
    }
}

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

            viewModel.SnapToProfileAction = async () => await SnapToPanel(0);
            viewModel.SnapToCameraAction = async () => await SnapToPanel(1);
            viewModel.SnapToChatAction = async () => await SnapToPanel(2);
            viewModel.ScrollToHistoryAction = () =>
            {
                if (viewModel.FeedPosts.Count > 0)
                {
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
#if ANDROID
                if (FeedCollectionView.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
                {
                    rv.SmoothScrollToPosition(0);
                    viewModel.IsViewingFeed = false;
                    return;
                }
#endif
                viewModel.IsViewingFeed = false;
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
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await AnimateCloseActivitySheet();
                });
            };

            viewModel.PreviewStateChangedAction = (isPreviewing) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    SetVerticalFeedScrollEnabled(!isPreviewing);
                    if (!isPreviewing)
                    {
                        UnfocusCaptionEntry();
                    }
                });
            };
        }

#if ANDROID
        private LockableOnItemTouchListener? _feedScrollLockListener;

        private sealed class LockableOnItemTouchListener : Java.Lang.Object, AndroidX.RecyclerView.Widget.RecyclerView.IOnItemTouchListener
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

            public bool OnInterceptTouchEvent(AndroidX.RecyclerView.Widget.RecyclerView rv, global::Android.Views.MotionEvent e)
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

        private sealed class KeyboardGlobalLayoutListener : Java.Lang.Object, global::Android.Views.ViewTreeObserver.IOnGlobalLayoutListener
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
                var imm = nativeView.Context?.GetSystemService(global::Android.Content.Context.InputMethodService) as global::Android.Views.InputMethods.InputMethodManager;
                imm?.HideSoftInputFromWindow(nativeView.WindowToken, global::Android.Views.InputMethods.HideSoftInputFlags.None);
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

        private void OnFeedScrolled(object? sender, ItemsViewScrolledEventArgs e)
        {
            if (BindingContext is CameraPageModel vm && this.Height > 0)
            {
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

        private void OnShutterTapped(object? sender, TappedEventArgs e)
        {
            HideCameraSliders(animated: false);
            cameraView.CapturePhoto();
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

                UpdateZoomLabel(0);
                UpdateExposureLabel(0);
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

            // New focus/metering point → exposure compensation starts again from 0 EV
            if (cameraView.MaxExposure > cameraView.MinExposure)
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

        private void OnZoomSliderValueChanged(object? sender, double value)
        {
            int level = (int)Math.Round(value);
            cameraView.SetZoom(level);
            UpdateZoomLabel(level);
        }

        private void OnExposureSliderValueChanged(object? sender, double value)
        {
            int index = (int)Math.Round(value);
            cameraView.SetExposure(index);
            UpdateExposureLabel(index);
        }

        private void UpdateZoomLabel(int level)
        {
            var ratios = cameraView.ZoomRatios;
            double ratio = ratios.Count > 0
                ? ratios[Math.Clamp(level, 0, ratios.Count - 1)] / 100.0
                : 1.0;
            ZoomValueLabel.Text = $"{ratio:0.0}x";
        }

        private void UpdateExposureLabel(int index)
        {
            double ev = index * cameraView.ExposureStep;
            ExposureValueLabel.Text = ev > 0.001 ? $"+{ev:0.0} EV" : $"{ev:0.0} EV";
        }

        private void OnPhotoCaptured(object? sender, string filePath)
        {
            Dispatcher.Dispatch(() =>
            {
                if (BindingContext is CameraPageModel vm)
                {
                    vm.OnPhotoCaptured(filePath);
                }
            });
        }
    }
}

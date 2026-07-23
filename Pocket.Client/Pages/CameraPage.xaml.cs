using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Pocket.Client.PageModels;

namespace Pocket.Client.Pages
{
    public partial class CameraPage : ContentPage
    {
        private int _currentIndex = 1; // 0=Profile, 1=Camera, 2=Chat
        private double _panX = 0;
        
        // Advanced Gesture State
        private bool _isVerticalScrollLocked = false;
        private bool _isHorizontalScrollLocked = false;
        private double _initialTotalX = 0;
        private double _initialTotalY = 0;
        private long _lastPanTime = 0;
        private double _lastPanX = 0;
        private double _velocity = 0;

        public CameraPage(CameraPageModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;

            viewModel.SnapToProfileAction = async () => await SnapToPanel(0);
            viewModel.SnapToCameraAction = async () => await SnapToPanel(1);
            viewModel.SnapToChatAction = async () => await SnapToPanel(2);
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
                
                // Immediately jump to the correct panel without animation
                PanContainer.TranslationX = -(_currentIndex * width);
            }
        }

        private async Task SnapToPanel(int index)
        {
            _currentIndex = Math.Max(0, Math.Min(2, index));
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
                cameraView.IsVisible = false;
            }
        }

        private async void OnPanUpdated(object sender, PanUpdatedEventArgs e)
        {
            long currentTime = Stopwatch.GetTimestamp();

            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _panX = PanContainer.TranslationX;
                    _isVerticalScrollLocked = false;
                    _isHorizontalScrollLocked = false;
                    _initialTotalX = 0;
                    _initialTotalY = 0;
                    _lastPanTime = currentTime;
                    _lastPanX = 0;
                    _velocity = 0;
                    break;

                case GestureStatus.Running:
                    if (_isVerticalScrollLocked) return;

                    // Calculate instantaneous velocity
                    double deltaX = e.TotalX - _lastPanX;
                    long deltaTime = currentTime - _lastPanTime;
                    if (deltaTime > 0)
                    {
                        // Velocity in pixels per second
                        _velocity = deltaX / (double)deltaTime * Stopwatch.Frequency; 
                    }
                    _lastPanTime = currentTime;
                    _lastPanX = e.TotalX;

                    // Directional Locking Phase (15px deadzone)
                    if (!_isHorizontalScrollLocked)
                    {
                        _initialTotalX = e.TotalX;
                        _initialTotalY = e.TotalY;

                        if (Math.Abs(_initialTotalY) > 10 && Math.Abs(_initialTotalY) > Math.Abs(_initialTotalX))
                        {
                            // Vertical swipe detected. Lock out horizontal panning.
                            _isVerticalScrollLocked = true;
                            return;
                        }

                        if (Math.Abs(_initialTotalX) > 15)
                        {
                            // Past horizontal deadzone, lock horizontal
                            _isHorizontalScrollLocked = true;
                            cameraView.IsVisible = true;
                        }
                        else
                        {
                            return; // Still in deadzone
                        }
                    }

                    // Apply horizontal translation
                    var newTranslation = _panX + e.TotalX;
                    newTranslation = Math.Max(-this.Width * 2, Math.Min(0, newTranslation));
                    PanContainer.TranslationX = newTranslation;
                    break;

                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    if (_isVerticalScrollLocked) return;

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

        private void OnShutterTapped(object sender, EventArgs e)
        {
            cameraView.CapturePhoto();
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

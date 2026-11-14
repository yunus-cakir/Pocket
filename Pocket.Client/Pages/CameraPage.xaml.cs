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

            viewModel.ScrollToFeedPostAction = (post) =>
            {
                int postIndex = viewModel.FeedPosts.IndexOf(post);
                if (postIndex < 0)
                    return;

                _isScrollingToCamera = false;
                viewModel.CurrentFeedPost = post;
                viewModel.IsViewingFeed = true;
#if ANDROID
                if (FeedCollectionView.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
                {
                    if (rv.GetLayoutManager() is AndroidX.RecyclerView.Widget.LinearLayoutManager llm)
                    {
                        llm.ScrollToPositionWithOffset(postIndex + 1, 0);
                    }
                    else
                    {
                        rv.ScrollToPosition(postIndex + 1);
                    }

                    return;
                }
#endif
                FeedCollectionView.ScrollTo(postIndex, position: ScrollToPosition.Start, animate: false);
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

            viewModel.OpenEmojiPickerAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    SetVerticalFeedScrollEnabled(false);
                    EmojiPickerSheetCard.AbortAnimation("TranslateTo");
                    EmojiPickerBackdrop.AbortAnimation("FadeTo");

                    EmojiPickerSheetCard.HeightRequest = 476;
                    EmojiPickerSheetCard.Padding = new Thickness(0);
                    EmojiCategoryBarBorder.IsVisible = true;
                    EmojiPickerOverlayGrid.IsVisible = true;
                    EmojiPickerSheetCard.TranslationY = 490;
                    EmojiPickerBackdrop.Opacity = 0;

                    ScrollEmojiPickerToRow(0);

                    await Task.WhenAll(
                        EmojiPickerSheetCard.TranslateToAsync(0, 0, 250, Easing.CubicOut),
                        EmojiPickerBackdrop.FadeToAsync(1, 250, Easing.CubicOut)
                    );
                });
            };

            viewModel.CloseEmojiPickerAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(async () => { await AnimateCloseEmojiPickerAsync(); });
            };

            viewModel.ScrollToEmojiCategoryAction = (rowIndex) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    UnfocusEmojiSearchEntry();
                    ScrollEmojiPickerToRow(rowIndex);
                });
            };

            viewModel.FocusEmojiSearchAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(() => { FocusEmojiSearchEntry(); });
            };

            viewModel.OpenFeedReplyAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    SetVerticalFeedScrollEnabled(false);
                    _isFeedReplyOpenAnimated = false;
                    _isClosingFeedReply = false;
                    _isImeCallbackDriving = false;

                    FeedReplyContentStack.AbortAnimation("TranslateTo");
                    FeedReplyContentStack.AbortAnimation("FadeTo");
                    FeedReplyBackdrop.AbortAnimation("FadeTo");

                    // Start at the bottom edge of the screen so it rises together with the keyboard
                    FeedReplyContentStack.TranslationY = 60;
                    FeedReplyContentStack.Opacity = 0;
                    FeedReplyBackdrop.Opacity = 0;
                    FeedReplyOverlayGrid.IsVisible = true;

                    FocusFeedReplyEntry();
                });
            };

            viewModel.CloseFeedReplyAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(async () => { await AnimateCloseFeedReplyAsync(); });
            };

            viewModel.OpenFilterMenuAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    SetVerticalFeedScrollEnabled(false);
                    FilterMenuCard.AbortAnimation("FadeTo");
                    FilterMenuCard.AbortAnimation("ScaleTo");
                    FilterMenuCard.AbortAnimation("TranslateTo");
                    FilterMenuBackdrop.AbortAnimation("FadeTo");

                    FilterMenuOverlayGrid.IsVisible = true;
                    FilterMenuCard.Opacity = 0;
                    FilterMenuCard.Scale = 0.92;
                    FilterMenuCard.TranslationY = -10;
                    FilterMenuBackdrop.Opacity = 0;

                    await Task.WhenAll(
                        FilterMenuCard.FadeToAsync(1, 160, Easing.CubicOut),
                        FilterMenuCard.ScaleToAsync(1, 180, Easing.CubicOut),
                        FilterMenuCard.TranslateToAsync(0, 0, 180, Easing.CubicOut),
                        FilterMenuBackdrop.FadeToAsync(1, 160, Easing.CubicOut)
                    );
                });
            };

            viewModel.CloseFilterMenuAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(async () => { await AnimateCloseFilterMenuAsync(); });
            };

            viewModel.OpenPostOptionsMenuAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    SetVerticalFeedScrollEnabled(false);
                    PostOptionsMenuCard.AbortAnimation("FadeTo");
                    PostOptionsMenuCard.AbortAnimation("ScaleTo");
                    PostOptionsMenuCard.AbortAnimation("TranslateTo");
                    PostOptionsMenuBackdrop.AbortAnimation("FadeTo");

                    PostOptionsMenuOverlayGrid.IsVisible = true;
                    PostOptionsMenuCard.Opacity = 0;
                    PostOptionsMenuCard.Scale = 0.92;
                    PostOptionsMenuCard.TranslationY = 10;
                    PostOptionsMenuBackdrop.Opacity = 0;

                    await Task.WhenAll(
                        PostOptionsMenuCard.FadeToAsync(1, 160, Easing.CubicOut),
                        PostOptionsMenuCard.ScaleToAsync(1, 180, Easing.CubicOut),
                        PostOptionsMenuCard.TranslateToAsync(0, 0, 180, Easing.CubicOut),
                        PostOptionsMenuBackdrop.FadeToAsync(1, 160, Easing.CubicOut)
                    );
                });
            };

            viewModel.ClosePostOptionsMenuAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(async () => { await AnimateClosePostOptionsMenuAsync(); });
            };

            viewModel.OpenDeleteConfirmAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    SetVerticalFeedScrollEnabled(false);
                    DeleteConfirmCard.AbortAnimation("FadeTo");
                    DeleteConfirmCard.AbortAnimation("ScaleTo");
                    DeleteConfirmBackdrop.AbortAnimation("FadeTo");

                    DeleteConfirmOverlayGrid.IsVisible = true;
                    DeleteConfirmCard.Opacity = 0;
                    DeleteConfirmCard.Scale = 0.92;
                    DeleteConfirmBackdrop.Opacity = 0;

                    await Task.WhenAll(
                        DeleteConfirmCard.FadeToAsync(1, 160, Easing.CubicOut),
                        DeleteConfirmCard.ScaleToAsync(1, 180, Easing.CubicOut),
                        DeleteConfirmBackdrop.FadeToAsync(1, 160, Easing.CubicOut)
                    );
                });
            };

            viewModel.CloseDeleteConfirmAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(async () => { await AnimateCloseDeleteConfirmAsync(); });
            };

            viewModel.ShowSavedToastAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(async () => { await ShowSavedToastAsync(); });
            };

            viewModel.PlayReactionEmojiRainAction = (postId, emojis) =>
            {
                MainThread.BeginInvokeOnMainThread(() => { PlayReactionEmojiRain(postId, emojis); });
            };

            viewModel.StopReactionEmojiRainAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(StopReactionEmojiRain);
            };

            viewModel.PreviewStateChangedAction = (isPreviewing) =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ResetShutterVisualState();
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

        private double _lastKnownKeyboardHeightDp = 286;
        private bool _isFeedReplyOpenAnimated;
        private bool _isClosingFeedReply;
        private bool _isImeCallbackDriving;

#if ANDROID
        private KeyboardGlobalLayoutListener? _keyboardListener;
        private ImeInsetsAnimationCallback? _imeAnimationCallback;
        private Microsoft.Maui.Dispatching.IDispatcherTimer? _keyboardPollTimer;
        private bool _wasKeyboardOpen;
        private long _keyboardWatcherStartTick;
        private int _imeUpperBoundPx;

        private void RegisterKeyboardVisibilityWatcher()
        {
            _wasKeyboardOpen = false;
            _keyboardWatcherStartTick = Environment.TickCount64;

            var activity = Platform.CurrentActivity;
            var decorView = activity?.Window?.DecorView;
            if (decorView != null)
            {
                if (decorView.ViewTreeObserver != null && _keyboardListener == null)
                {
                    _keyboardListener = new KeyboardGlobalLayoutListener(this);
                    decorView.ViewTreeObserver.AddOnGlobalLayoutListener(_keyboardListener);
                }

                if (_imeAnimationCallback == null)
                {
                    _imeAnimationCallback = new ImeInsetsAnimationCallback(this);
                    AndroidX.Core.View.ViewCompat.SetWindowInsetsAnimationCallback(decorView, _imeAnimationCallback);
                }
            }

            if (_keyboardPollTimer == null)
            {
                _keyboardPollTimer = Dispatcher.CreateTimer();
                _keyboardPollTimer.Interval = TimeSpan.FromMilliseconds(16);
                _keyboardPollTimer.Tick += (_, _) => CheckKeyboardState();
            }

            if (!_keyboardPollTimer.IsRunning)
            {
                _keyboardPollTimer.Start();
            }
        }

        private void OnImeAnimationStart(AndroidX.Core.View.WindowInsetsAnimationCompat.BoundsCompat bounds)
        {
            _imeUpperBoundPx = bounds.UpperBound.Bottom;
        }

        private void OnImeAnimationProgress(
            AndroidX.Core.View.WindowInsetsCompat insets,
            AndroidX.Core.View.WindowInsetsAnimationCompat imeAnim)
        {
            if (!FeedReplyOverlayGrid.IsVisible)
                return;

            var activity = Platform.CurrentActivity;
            var decorView = activity?.Window?.DecorView;
            float density = decorView?.Resources?.DisplayMetrics?.Density ?? 2.75f;

            int imeBottomPx = insets.GetInsets(AndroidX.Core.View.WindowInsetsCompat.Type.Ime())?.Bottom ?? 0;
            int navBottomPx = insets.GetInsets(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars())?.Bottom ?? 0;

            float fraction = Math.Clamp(imeAnim.InterpolatedFraction, 0f, 1f);
            if (imeBottomPx <= 0 && _imeUpperBoundPx > 0 && !_isClosingFeedReply)
            {
                imeBottomPx = (int)Math.Round(_imeUpperBoundPx * fraction);
            }

            int overlapPx = Math.Max(0, imeBottomPx - navBottomPx);
            double overlapDp = overlapPx / density;
            if (overlapDp <= 0 && !_isClosingFeedReply)
                return;

            _isImeCallbackDriving = true;
            _isFeedReplyOpenAnimated = true;

            FeedReplyContentStack.AbortAnimation("TranslateTo");
            FeedReplyContentStack.AbortAnimation("FadeTo");
            FeedReplyBackdrop.AbortAnimation("FadeTo");

            double targetTranslationY = (navBottomPx - imeBottomPx) / density;
            if (targetTranslationY < 0)
            {
                _lastKnownKeyboardHeightDp = -targetTranslationY;
            }

            FeedReplyContentStack.TranslationY = targetTranslationY;

            double targetTotalDp = _lastKnownKeyboardHeightDp > 100 ? _lastKnownKeyboardHeightDp : 280;
            double progress = Math.Clamp(overlapDp / targetTotalDp, 0.0, 1.0);
            FeedReplyContentStack.Opacity = Math.Clamp(progress * 1.8, 0.0, 1.0);
            FeedReplyBackdrop.Opacity = progress;
        }

        private void OnImeAnimationEnd()
        {
            _isImeCallbackDriving = false;
        }

        private void AnimateFeedReplyWithKeyboard(double targetOverlapDp)
        {
            if (!FeedReplyOverlayGrid.IsVisible || _isClosingFeedReply || _isImeCallbackDriving)
                return;

            if (!_isFeedReplyOpenAnimated)
            {
                _isFeedReplyOpenAnimated = true;
                FeedReplyContentStack.AbortAnimation("TranslateTo");
                FeedReplyContentStack.AbortAnimation("FadeTo");
                FeedReplyBackdrop.AbortAnimation("FadeTo");

                FeedReplyContentStack.TranslationY = 48;
                FeedReplyContentStack.Opacity = 0;

                _ = FeedReplyContentStack.FadeToAsync(1, 140, Easing.CubicOut);
                _ = FeedReplyContentStack.TranslateToAsync(0, -targetOverlapDp, 250, Easing.CubicOut);
                _ = FeedReplyBackdrop.FadeToAsync(1, 250, Easing.CubicOut);
            }
            else if (Math.Abs(FeedReplyContentStack.TranslationY - (-targetOverlapDp)) > 1.0)
            {
                _ = FeedReplyContentStack.TranslateToAsync(0, -targetOverlapDp, 140, Easing.CubicOut);
            }
        }

        private void CheckKeyboardState()
        {
            var activity = Platform.CurrentActivity;
            var decorView = activity?.Window?.DecorView;
            if (decorView == null)
                return;

            var rootInsets = AndroidX.Core.View.ViewCompat.GetRootWindowInsets(decorView);
            int imeBottomPx = rootInsets?.GetInsets(AndroidX.Core.View.WindowInsetsCompat.Type.Ime())?.Bottom ?? 0;
            int navBottomPx = rootInsets?.GetInsets(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars())?.Bottom ??
                              0;

            var rect = new global::Android.Graphics.Rect();
            decorView.GetWindowVisibleDisplayFrame(rect);
            int screenHeight = decorView.RootView?.Height ?? 0;
            int frameKeypadPx = screenHeight > 0 ? Math.Max(0, screenHeight - rect.Bottom) : 0;

            int rawKeyboardPx = Math.Max(imeBottomPx, frameKeypadPx);
            bool isKeyboardOpen = (rootInsets?.IsVisible(AndroidX.Core.View.WindowInsetsCompat.Type.Ime()) ?? false)
                                  || (screenHeight > 0 && rawKeyboardPx > screenHeight * 0.15);

            if (isKeyboardOpen && rawKeyboardPx > navBottomPx)
            {
                float density = decorView.Resources?.DisplayMetrics?.Density ?? 2.75f;
                int overlapPx = Math.Max(0, rawKeyboardPx - navBottomPx);
                double overlapDp = overlapPx / density;
                if (overlapDp > 120)
                {
                    _lastKnownKeyboardHeightDp = overlapDp;
                    AnimateFeedReplyWithKeyboard(overlapDp);

                    if (EmojiPickerOverlayGrid.IsVisible)
                    {
                        double maxHeight = (BindingContext as CameraPageModel)?.ViewportHeight is double vh && vh > 300
                            ? vh - 72
                            : 580;
                        EmojiPickerSheetCard.HeightRequest = Math.Min(maxHeight, 476 + overlapDp * 0.48);
                        EmojiPickerSheetCard.Padding = new Thickness(0, 0, 0, overlapDp);
                        EmojiCategoryBarBorder.IsVisible = false;
                    }
                }
            }
            else if (FeedReplyOverlayGrid.IsVisible &&
                     !_isFeedReplyOpenAnimated &&
                     !_isClosingFeedReply &&
                     !_isImeCallbackDriving &&
                     (Environment.TickCount64 - _keyboardWatcherStartTick) >= 120)
            {
                // Hardware keyboard fallback: when a physical/emulator keyboard suppresses the on-screen IME,
                // smoothly slide the reply box into view above the bottom edge so it remains usable.
                AnimateFeedReplyWithKeyboard(24);
            }

            if (_wasKeyboardOpen && !isKeyboardOpen && (Environment.TickCount64 - _keyboardWatcherStartTick) > 250)
            {
                _wasKeyboardOpen = false;
                _keyboardPollTimer?.Stop();

                if (EmojiPickerOverlayGrid.IsVisible)
                {
                    EmojiPickerSheetCard.HeightRequest = 476;
                    EmojiPickerSheetCard.Padding = new Thickness(0);
                    EmojiCategoryBarBorder.IsVisible = true;
                    if (EmojiSearchEntry.IsFocused)
                    {
                        EmojiSearchEntry.Unfocus();
                    }
                }

                if (BindingContext is CameraPageModel vm)
                {
                    if (vm.IsEditingCaption)
                    {
                        UnfocusCaptionEntry();
                    }

                    if (vm.IsReplyingToFeedPost)
                    {
                        _ = AnimateCloseFeedReplyAsync();
                    }
                }
            }
            else if (isKeyboardOpen)
            {
                _wasKeyboardOpen = true;
            }
        }

        private sealed class ImeInsetsAnimationCallback : AndroidX.Core.View.WindowInsetsAnimationCompat.Callback
        {
            private readonly CameraPage _page;

            public ImeInsetsAnimationCallback(CameraPage page)
                : base(AndroidX.Core.View.WindowInsetsAnimationCompat.Callback.DispatchModeContinueOnSubtree)
            {
                _page = page;
            }

            public override AndroidX.Core.View.WindowInsetsAnimationCompat.BoundsCompat? OnStart(
                AndroidX.Core.View.WindowInsetsAnimationCompat? animation,
                AndroidX.Core.View.WindowInsetsAnimationCompat.BoundsCompat? bounds)
            {
                if (animation != null && bounds != null &&
                    (animation.TypeMask & AndroidX.Core.View.WindowInsetsCompat.Type.Ime()) != 0)
                {
                    _page.OnImeAnimationStart(bounds);
                }

                return base.OnStart(animation, bounds);
            }

            public override AndroidX.Core.View.WindowInsetsCompat? OnProgress(
                AndroidX.Core.View.WindowInsetsCompat? insets,
                System.Collections.Generic.IList<AndroidX.Core.View.WindowInsetsAnimationCompat>? runningAnimations)
            {
                if (insets != null && runningAnimations != null)
                {
                    for (int i = 0; i < runningAnimations.Count; i++)
                    {
                        var anim = runningAnimations[i];
                        if ((anim.TypeMask & AndroidX.Core.View.WindowInsetsCompat.Type.Ime()) != 0)
                        {
                            _page.OnImeAnimationProgress(insets, anim);
                            break;
                        }
                    }
                }

                return insets;
            }

            public override void OnEnd(AndroidX.Core.View.WindowInsetsAnimationCompat? animation)
            {
                base.OnEnd(animation);
                if (animation != null &&
                    (animation.TypeMask & AndroidX.Core.View.WindowInsetsCompat.Type.Ime()) != 0)
                {
                    _page.OnImeAnimationEnd();
                }
            }
        }

        private sealed class KeyboardGlobalLayoutListener : Java.Lang.Object,
            global::Android.Views.ViewTreeObserver.IOnGlobalLayoutListener
        {
            private readonly CameraPage _page;

            public KeyboardGlobalLayoutListener(CameraPage page)
            {
                _page = page;
            }

            public void OnGlobalLayout()
            {
                _page.CheckKeyboardState();
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

        private void FocusFeedReplyEntry()
        {
            FeedReplyEntry.Focus();
#if ANDROID
            RegisterKeyboardVisibilityWatcher();
            if (FeedReplyEntry.Handler?.PlatformView is AndroidX.AppCompat.Widget.AppCompatEditText editText)
            {
                void ActivateNativeFocus()
                {
                    if (BindingContext is not CameraPageModel vm || !vm.IsReplyingToFeedPost)
                        return;

                    Platforms.Android.Handlers.IncognitoEntryHandler.ApplyCursorStyle(editText);
                    editText.Focusable = true;
                    editText.FocusableInTouchMode = true;
                    editText.RequestFocus();
                    editText.SetCursorVisible(false);
                    editText.SetCursorVisible(true);
                    editText.SetSelection(editText.Text?.Length ?? 0);

                    var imm = editText.Context?.GetSystemService(global::Android.Content.Context.InputMethodService)
                        as global::Android.Views.InputMethods.InputMethodManager;
                    imm?.ShowSoftInput(editText, global::Android.Views.InputMethods.ShowFlags.Implicit);
                }

                ActivateNativeFocus();
                editText.Post(ActivateNativeFocus);
                editText.PostDelayed(ActivateNativeFocus, 80);
            }
#endif
        }

        private void UnfocusFeedReplyEntry()
        {
            if (FeedReplyEntry.IsFocused)
            {
                FeedReplyEntry.Unfocus();
            }
#if ANDROID
            _keyboardPollTimer?.Stop();
            if (FeedReplyEntry.Handler?.PlatformView is global::Android.Views.View nativeView)
            {
                nativeView.ClearFocus();
                var imm =
                    nativeView.Context?.GetSystemService(global::Android.Content.Context.InputMethodService) as
                        global::Android.Views.InputMethods.InputMethodManager;
                imm?.HideSoftInputFromWindow(nativeView.WindowToken,
                    global::Android.Views.InputMethods.HideSoftInputFlags.None);
            }
#endif
        }

        private void OnFeedReplyPillTapped(object? sender, TappedEventArgs e)
        {
            FocusFeedReplyEntry();
        }

        private async void OnFeedReplyBackdropTapped(object? sender, TappedEventArgs e)
        {
            await AnimateCloseFeedReplyAsync();
        }

        private void OnFeedReplyEntryCompleted(object? sender, EventArgs e)
        {
            if (BindingContext is CameraPageModel vm)
            {
                if (vm.HasFeedReplyText)
                {
                    vm.SendFeedReplyCommand.Execute(null);
                }
                else
                {
                    _ = AnimateCloseFeedReplyAsync();
                }
            }
        }

        private async Task AnimateCloseFeedReplyAsync()
        {
            if (_isClosingFeedReply)
                return;

            _isClosingFeedReply = true;
            _isFeedReplyOpenAnimated = false;
            UnfocusFeedReplyEntry();

            if (!FeedReplyOverlayGrid.IsVisible)
            {
                if (BindingContext is CameraPageModel earlyVm)
                {
                    earlyVm.IsReplyingToFeedPost = false;
                    if (!earlyVm.IsPreviewing)
                    {
                        SetVerticalFeedScrollEnabled(true);
                    }
                }

                _isClosingFeedReply = false;
                return;
            }

            FeedReplyContentStack.AbortAnimation("TranslateTo");
            FeedReplyContentStack.AbortAnimation("FadeTo");
            FeedReplyBackdrop.AbortAnimation("FadeTo");

            await Task.WhenAll(
                FeedReplyContentStack.TranslateToAsync(0, 60, 220, Easing.CubicIn),
                FeedReplyContentStack.FadeToAsync(0, 180, Easing.CubicIn),
                FeedReplyBackdrop.FadeToAsync(0, 220, Easing.CubicIn)
            );

            if (BindingContext is CameraPageModel vm)
            {
                vm.IsReplyingToFeedPost = false;
                if (!vm.IsPreviewing)
                {
                    SetVerticalFeedScrollEnabled(true);
                }
            }

            FeedReplyOverlayGrid.IsVisible = false;
            _isClosingFeedReply = false;
        }

        protected override bool OnBackButtonPressed()
        {
            if (BindingContext is CameraPageModel vm)
            {
                if (vm.IsFilterMenuOpen)
                {
                    _ = AnimateCloseFilterMenuAsync();
                    return true;
                }

                if (vm.IsDeleteConfirmOpen)
                {
                    _ = AnimateCloseDeleteConfirmAsync();
                    return true;
                }

                if (vm.IsPostOptionsMenuOpen)
                {
                    _ = AnimateClosePostOptionsMenuAsync();
                    return true;
                }

                if (vm.IsReplyingToFeedPost)
                {
                    _ = AnimateCloseFeedReplyAsync();
                    return true;
                }

                if (vm.IsActivitySheetOpen)
                {
                    _ = AnimateCloseActivitySheet();
                    return true;
                }

                if (vm.IsEmojiPickerOpen)
                {
                    _ = AnimateCloseEmojiPickerAsync();
                    return true;
                }

                if (vm.IsGridGalleryOpen)
                {
                    vm.IsGridGalleryOpen = false;
                    return true;
                }
            }

            return base.OnBackButtonPressed();
        }

        private async void OnFilterMenuBackdropTapped(object? sender, TappedEventArgs e)
        {
            await AnimateCloseFilterMenuAsync();
        }

        private async Task AnimateCloseFilterMenuAsync()
        {
            if (!FilterMenuOverlayGrid.IsVisible)
            {
                if (BindingContext is CameraPageModel earlyVm)
                {
                    earlyVm.IsFilterMenuOpen = false;
                    if (!earlyVm.IsPreviewing && !earlyVm.IsReplyingToFeedPost)
                    {
                        SetVerticalFeedScrollEnabled(true);
                    }
                }

                return;
            }

            FilterMenuCard.AbortAnimation("FadeTo");
            FilterMenuCard.AbortAnimation("ScaleTo");
            FilterMenuCard.AbortAnimation("TranslateTo");
            FilterMenuBackdrop.AbortAnimation("FadeTo");

            await Task.WhenAll(
                FilterMenuCard.FadeToAsync(0, 130, Easing.CubicIn),
                FilterMenuCard.ScaleToAsync(0.94, 130, Easing.CubicIn),
                FilterMenuCard.TranslateToAsync(0, -8, 130, Easing.CubicIn),
                FilterMenuBackdrop.FadeToAsync(0, 130, Easing.Linear)
            );

            FilterMenuOverlayGrid.IsVisible = false;
            if (BindingContext is CameraPageModel vm)
            {
                vm.IsFilterMenuOpen = false;
                if (!vm.IsPreviewing && !vm.IsReplyingToFeedPost && !vm.IsPostOptionsMenuOpen)
                {
                    SetVerticalFeedScrollEnabled(true);
                }
            }
        }

        private async void OnPostOptionsMenuBackdropTapped(object? sender, TappedEventArgs e)
        {
            await AnimateClosePostOptionsMenuAsync();
        }

        private async Task AnimateClosePostOptionsMenuAsync()
        {
            if (!PostOptionsMenuOverlayGrid.IsVisible)
            {
                if (BindingContext is CameraPageModel earlyVm)
                {
                    earlyVm.IsPostOptionsMenuOpen = false;
                    if (!earlyVm.IsPreviewing && !earlyVm.IsReplyingToFeedPost && !earlyVm.IsFilterMenuOpen)
                    {
                        SetVerticalFeedScrollEnabled(true);
                    }
                }

                return;
            }

            PostOptionsMenuCard.AbortAnimation("FadeTo");
            PostOptionsMenuCard.AbortAnimation("ScaleTo");
            PostOptionsMenuCard.AbortAnimation("TranslateTo");
            PostOptionsMenuBackdrop.AbortAnimation("FadeTo");

            await Task.WhenAll(
                PostOptionsMenuCard.FadeToAsync(0, 130, Easing.CubicIn),
                PostOptionsMenuCard.ScaleToAsync(0.94, 130, Easing.CubicIn),
                PostOptionsMenuCard.TranslateToAsync(0, 8, 130, Easing.CubicIn),
                PostOptionsMenuBackdrop.FadeToAsync(0, 130, Easing.Linear)
            );

            PostOptionsMenuOverlayGrid.IsVisible = false;
            if (BindingContext is CameraPageModel vm)
            {
                vm.IsPostOptionsMenuOpen = false;
                if (!vm.IsPreviewing && !vm.IsReplyingToFeedPost && !vm.IsFilterMenuOpen && !vm.IsDeleteConfirmOpen)
                {
                    SetVerticalFeedScrollEnabled(true);
                }
            }
        }

        private async void OnDeleteConfirmBackdropTapped(object? sender, TappedEventArgs e)
        {
            await AnimateCloseDeleteConfirmAsync();
        }

        private async Task AnimateCloseDeleteConfirmAsync()
        {
            if (!DeleteConfirmOverlayGrid.IsVisible)
            {
                if (BindingContext is CameraPageModel earlyVm)
                {
                    earlyVm.IsDeleteConfirmOpen = false;
                    if (!earlyVm.IsPreviewing && !earlyVm.IsReplyingToFeedPost && !earlyVm.IsFilterMenuOpen &&
                        !earlyVm.IsPostOptionsMenuOpen)
                    {
                        SetVerticalFeedScrollEnabled(true);
                    }
                }

                return;
            }

            DeleteConfirmCard.AbortAnimation("FadeTo");
            DeleteConfirmCard.AbortAnimation("ScaleTo");
            DeleteConfirmBackdrop.AbortAnimation("FadeTo");

            await Task.WhenAll(
                DeleteConfirmCard.FadeToAsync(0, 130, Easing.CubicIn),
                DeleteConfirmCard.ScaleToAsync(0.94, 130, Easing.CubicIn),
                DeleteConfirmBackdrop.FadeToAsync(0, 130, Easing.Linear)
            );

            DeleteConfirmOverlayGrid.IsVisible = false;
            if (BindingContext is CameraPageModel vm)
            {
                vm.IsDeleteConfirmOpen = false;
                if (!vm.IsPreviewing && !vm.IsReplyingToFeedPost && !vm.IsFilterMenuOpen && !vm.IsPostOptionsMenuOpen)
                {
                    SetVerticalFeedScrollEnabled(true);
                }
            }
        }

        private System.Threading.CancellationTokenSource? _savedToastCts;

        private async Task ShowSavedToastAsync()
        {
            _savedToastCts?.Cancel();
            var cts = new System.Threading.CancellationTokenSource();
            _savedToastCts = cts;

            SavedToastPill.AbortAnimation("FadeTo");
            SavedToastPill.AbortAnimation("TranslateTo");

            SavedToastPill.Opacity = 0;
            SavedToastPill.TranslationY = 24;
            SavedToastPill.IsVisible = true;

            await Task.WhenAll(
                SavedToastPill.FadeToAsync(1, 180, Easing.CubicOut),
                SavedToastPill.TranslateToAsync(0, 0, 180, Easing.CubicOut)
            );

            try
            {
                await Task.Delay(1600, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (cts.IsCancellationRequested)
                return;

            await Task.WhenAll(
                SavedToastPill.FadeToAsync(0, 180, Easing.CubicIn),
                SavedToastPill.TranslateToAsync(0, 20, 180, Easing.CubicIn)
            );

            if (!cts.IsCancellationRequested)
            {
                SavedToastPill.IsVisible = false;
            }
        }

        private System.Threading.CancellationTokenSource? _emojiRainCts;
        private Guid _activeEmojiRainPostId;

        private void StopReactionEmojiRain()
        {
            _emojiRainCts?.Cancel();
            _emojiRainCts = null;
            _activeEmojiRainPostId = Guid.Empty;

            if (ReactionEmojiRainOverlay.Children.Count > 0)
            {
                foreach (var child in ReactionEmojiRainOverlay.Children)
                {
                    if (child is VisualElement ve)
                    {
                        ve.AbortAnimation("TranslateTo");
                        ve.AbortAnimation("FadeTo");
                        ve.AbortAnimation("RotateTo");
                        ve.AbortAnimation("ScaleTo");
                    }
                }

                ReactionEmojiRainOverlay.Children.Clear();
            }

            ReactionEmojiRainOverlay.IsVisible = false;
            if (BindingContext is CameraPageModel vm)
            {
                vm.IsReactionAnimationPlaying = false;
            }
        }

        private void PlayReactionEmojiRain(Guid postId, System.Collections.Generic.IReadOnlyList<string> emojis)
        {
            if (emojis == null || emojis.Count == 0)
                return;

            if (_activeEmojiRainPostId != postId || _emojiRainCts == null || _emojiRainCts.IsCancellationRequested)
            {
                StopReactionEmojiRain();
                _activeEmojiRainPostId = postId;
                _emojiRainCts = new System.Threading.CancellationTokenSource();
            }

            var cts = _emojiRainCts;
            var vm = BindingContext as CameraPageModel;
            if (vm != null)
            {
                vm.IsReactionAnimationPlaying = true;
            }

            double screenWidth = this.Width > 0 ? this.Width : 390;
            double screenHeight = this.Height > 0
                ? this.Height
                : (vm != null && vm.ViewportHeight > 0 ? vm.ViewportHeight : 840);
            double minX = 16;
            double maxX = Math.Max(minX + 80, screenWidth - 74);
            double startY = -80;
            double endY = screenHeight + 80;

            int particleCount = Math.Clamp(Math.Max(8, emojis.Count * 4), 8, 16);
            var rng = new Random();

            // Build shuffled horizontal lanes so falling emojis spread naturally across the photo card
            int laneCount = 6;
            var lanes = new int[particleCount];
            for (int i = 0; i < particleCount; i++)
            {
                lanes[i] = i % laneCount;
            }

            for (int i = particleCount - 1; i > 0; i--)
            {
                // Keep adjacent particles in different lanes
                int swapIdx = rng.Next(0, i + 1);
                (lanes[i], lanes[swapIdx]) = (lanes[swapIdx], lanes[i]);
            }

            double laneWidth = (maxX - minX) / laneCount;
            ReactionEmojiRainOverlay.IsVisible = true;

            for (int i = 0; i < particleCount; i++)
            {
                string emoji = emojis[i % emojis.Count];
                int lane = lanes[i];
                double xPos = minX + (lane * laneWidth) + (rng.NextDouble() * Math.Max(8, laneWidth - 10));
                xPos = Math.Clamp(xPos, minX, maxX);

                double driftX = (rng.NextDouble() - 0.5) * 26;
                double endX = Math.Clamp(xPos + driftX, minX, maxX);
                double fontSize = 44 + (rng.NextDouble() * 8);
                double startRotation = -16 + (rng.NextDouble() * 24);
                double endRotation = startRotation + (-10 + (rng.NextDouble() * 20));
                int delayMs = (i * 110) + rng.Next(0, 35);
                uint fallDurationMs = (uint)(1050 + rng.Next(0, 240));

                var label = new Label
                {
                    Text = emoji,
                    FontSize = fontSize,
                    HorizontalOptions = LayoutOptions.Start,
                    VerticalOptions = LayoutOptions.Start,
                    InputTransparent = true,
                    TranslationX = xPos,
                    TranslationY = startY,
                    Rotation = startRotation,
                    Opacity = 1
                };

                ReactionEmojiRainOverlay.Children.Add(label);
                _ = AnimateSingleFallingEmojiAsync(label, endX, endY, endRotation, delayMs, fallDurationMs, cts.Token);
            }
        }

        private static readonly Easing GentleFallEasing = new(t => (0.45 * t) + (0.55 * t * t));

        private async Task AnimateSingleFallingEmojiAsync(
            Label label,
            double endX,
            double endY,
            double endRotation,
            int delayMs,
            uint fallDurationMs,
            System.Threading.CancellationToken token)
        {
            try
            {
                if (delayMs > 0)
                {
                    await Task.Delay(delayMs, token);
                }

                if (token.IsCancellationRequested)
                    return;

                var translateTask = label.TranslateToAsync(endX, endY, fallDurationMs, GentleFallEasing);
                var rotateTask = label.RotateToAsync(endRotation, fallDurationMs, Easing.Linear);

                await Task.WhenAll(translateTask, rotateTask);
            }
            catch (TaskCanceledException)
            {
                return;
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    ReactionEmojiRainOverlay.Children.Remove(label);
                    if (ReactionEmojiRainOverlay.Children.Count == 0)
                    {
                        ReactionEmojiRainOverlay.IsVisible = false;
                        _activeEmojiRainPostId = Guid.Empty;
                        if (BindingContext is CameraPageModel vm)
                        {
                            vm.IsReactionAnimationPlaying = false;
                        }
                    }
                }
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

        private double _emojiSheetPanInitialTranslationY = 0;
        private long _ignoreEmojiScrollSyncUntilTick = 0;

        private async void OnEmojiPickerBackdropTapped(object? sender, TappedEventArgs e)
        {
            await AnimateCloseEmojiPickerAsync();
        }

        private async Task AnimateCloseEmojiPickerAsync()
        {
            UnfocusEmojiSearchEntry();

            if (!EmojiPickerOverlayGrid.IsVisible)
            {
                if (BindingContext is CameraPageModel earlyVm)
                {
                    earlyVm.IsEmojiPickerOpen = false;
                    if (!earlyVm.IsPreviewing && !earlyVm.IsReplyingToFeedPost && !earlyVm.IsFilterMenuOpen)
                    {
                        SetVerticalFeedScrollEnabled(true);
                    }
                }

                return;
            }

            EmojiPickerSheetCard.AbortAnimation("TranslateTo");
            EmojiPickerBackdrop.AbortAnimation("FadeTo");

            await Task.WhenAll(
                EmojiPickerSheetCard.TranslateToAsync(0, 500, 220, Easing.CubicIn),
                EmojiPickerBackdrop.FadeToAsync(0, 220, Easing.Linear)
            );

            EmojiPickerOverlayGrid.IsVisible = false;
            EmojiPickerSheetCard.HeightRequest = 476;
            EmojiPickerSheetCard.Padding = new Thickness(0);
            EmojiCategoryBarBorder.IsVisible = true;

            if (BindingContext is CameraPageModel vm)
            {
                vm.IsEmojiPickerOpen = false;
                if (!vm.IsPreviewing && !vm.IsReplyingToFeedPost && !vm.IsFilterMenuOpen)
                {
                    SetVerticalFeedScrollEnabled(true);
                }
            }
        }

        private async void OnEmojiPickerSheetPanUpdated(object? sender, PanUpdatedEventArgs e)
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _emojiSheetPanInitialTranslationY = EmojiPickerSheetCard.TranslationY;
                    break;

                case GestureStatus.Running:
                    if (e.TotalY > 0)
                    {
                        EmojiPickerSheetCard.TranslationY = _emojiSheetPanInitialTranslationY + e.TotalY;
                        double progress = Math.Clamp(1.0 - (EmojiPickerSheetCard.TranslationY / 460.0), 0.0, 1.0);
                        EmojiPickerBackdrop.Opacity = progress;
                    }

                    break;

                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    if (EmojiPickerSheetCard.TranslationY > 75)
                    {
                        await AnimateCloseEmojiPickerAsync();
                    }
                    else
                    {
                        await Task.WhenAll(
                            EmojiPickerSheetCard.TranslateToAsync(0, 0, 160, Easing.CubicOut),
                            EmojiPickerBackdrop.FadeToAsync(1, 160, Easing.Linear)
                        );
                    }

                    break;
            }
        }

        private void ScrollEmojiPickerToRow(int rowIndex)
        {
            if (BindingContext is not CameraPageModel vm || rowIndex < 0 || rowIndex >= vm.EmojiPickerRows.Count)
                return;

            _ignoreEmojiScrollSyncUntilTick = Environment.TickCount64 + 320;

#if ANDROID
            if (EmojiPickerCollectionView.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
            {
                if (rv.GetLayoutManager() is AndroidX.RecyclerView.Widget.LinearLayoutManager llm)
                {
                    llm.ScrollToPositionWithOffset(rowIndex, 0);
                    return;
                }

                rv.ScrollToPosition(rowIndex);
                return;
            }
#endif
            EmojiPickerCollectionView.ScrollTo(rowIndex, position: ScrollToPosition.Start, animate: false);
        }

        private void OnEmojiPickerScrolled(object? sender, ItemsViewScrolledEventArgs e)
        {
            if (Environment.TickCount64 < _ignoreEmojiScrollSyncUntilTick)
                return;

            if (BindingContext is not CameraPageModel vm || !string.IsNullOrEmpty(vm.EmojiSearchText))
                return;

            int firstIdx = e.FirstVisibleItemIndex;
            if (firstIdx >= 0 && firstIdx < vm.EmojiPickerRows.Count)
            {
                int catIdx = vm.EmojiPickerRows[firstIdx].CategoryIndex;
                if (vm.SelectedEmojiCategoryIndex != catIdx)
                {
                    vm.SelectedEmojiCategoryIndex = catIdx;
                }
            }
        }

        private void FocusEmojiSearchEntry()
        {
            EmojiSearchEntry.Focus();
#if ANDROID
            RegisterKeyboardVisibilityWatcher();
            if (EmojiSearchEntry.Handler?.PlatformView is AndroidX.AppCompat.Widget.AppCompatEditText editText)
            {
                void ActivateFocus()
                {
                    if (BindingContext is not CameraPageModel vm || !vm.IsEmojiPickerOpen)
                        return;

                    Platforms.Android.Handlers.IncognitoEntryHandler.ApplyCursorStyle(editText);
                    editText.Focusable = true;
                    editText.FocusableInTouchMode = true;
                    editText.RequestFocus();
                    editText.SetSelection(editText.Text?.Length ?? 0);

                    var imm = editText.Context?.GetSystemService(global::Android.Content.Context.InputMethodService)
                        as global::Android.Views.InputMethods.InputMethodManager;
                    imm?.ShowSoftInput(editText, global::Android.Views.InputMethods.ShowFlags.Implicit);
                }

                ActivateFocus();
                editText.Post(ActivateFocus);
            }
#endif
        }

        private void UnfocusEmojiSearchEntry()
        {
            if (EmojiSearchEntry.IsFocused)
            {
                EmojiSearchEntry.Unfocus();
            }
#if ANDROID
            if (EmojiSearchEntry.Handler?.PlatformView is global::Android.Views.View nativeView)
            {
                nativeView.ClearFocus();
                var imm = nativeView.Context?.GetSystemService(global::Android.Content.Context.InputMethodService)
                    as global::Android.Views.InputMethods.InputMethodManager;
                imm?.HideSoftInputFromWindow(nativeView.WindowToken,
                    global::Android.Views.InputMethods.HideSoftInputFlags.None);
            }
#endif
        }

        private void OnEmojiSearchEntryFocused(object? sender, FocusEventArgs e)
        {
#if ANDROID
            RegisterKeyboardVisibilityWatcher();
#endif
        }

        private void OnEmojiSearchEntryUnfocused(object? sender, FocusEventArgs e)
        {
            EmojiPickerSheetCard.HeightRequest = 476;
            EmojiPickerSheetCard.Padding = new Thickness(0);
            EmojiCategoryBarBorder.IsVisible = true;
        }

        private void OnEmojiSearchEntryCompleted(object? sender, EventArgs e)
        {
            UnfocusEmojiSearchEntry();
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
                    else
                    {
                        // Run after the built-in SnapHelper had its chance to start a settle scroll.
                        recyclerView.Post(() => _page.EnsureFeedPageAligned(recyclerView));
                    }

                    _page.UpdateViewingFeedStateFromRecyclerView(recyclerView);
                }
            }
        }

        /// <summary>
        /// Fallback snap: MAUI's MandatorySingle snap helper refuses to snap when the list is at its
        /// trailing edge (e.g. no feed posts, only the camera header plus the empty-view slot, or the
        /// last post). That lets the user leave the feed half-scrolled. When scrolling has fully stopped
        /// and no page is aligned to the top, smoothly scroll to the nearest reachable page start.
        /// </summary>
        private void EnsureFeedPageAligned(AndroidX.RecyclerView.Widget.RecyclerView rv)
        {
            if (BindingContext is not CameraPageModel vm || rv.Height <= 0 || rv.ChildCount == 0)
                return;
            if (rv.ScrollState != AndroidX.RecyclerView.Widget.RecyclerView.ScrollStateIdle || _isScrollingToCamera)
                return;

            // Use exact child geometry (ComputeVerticalScrollOffset is only an estimate).
            int remainingDown = int.MaxValue;
            var layoutManager = rv.GetLayoutManager();
            if (layoutManager != null)
            {
                var lastChild = rv.GetChildAt(rv.ChildCount - 1);
                if (lastChild != null && rv.GetChildAdapterPosition(lastChild) == layoutManager.ItemCount - 1)
                    remainingDown = Math.Max(0, lastChild.Bottom - (rv.Height - rv.PaddingBottom));
            }

            // Valid snap targets: header (adapter pos 0) and real feed post pages (1..FeedPosts.Count).
            int lastPageAdapterPos = vm.FeedPosts.Count;
            int? bestDelta = null;

            for (int i = 0; i < rv.ChildCount; i++)
            {
                var child = rv.GetChildAt(i);
                if (child == null)
                    continue;

                int adapterPos = rv.GetChildAdapterPosition(child);
                if (adapterPos < 0 || adapterPos > lastPageAdapterPos)
                    continue;

                int delta = child.Top - rv.PaddingTop; // >0 scroll down, <0 scroll up
                if (delta > 0 && delta > remainingDown + 1)
                    continue; // page start is not reachable

                if (bestDelta == null || Math.Abs(delta) < Math.Abs(bestDelta.Value))
                    bestDelta = delta;
            }

            if (bestDelta is int d && Math.Abs(d) > 1)
            {
                rv.SmoothScrollBy(0, d);
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
                if (vm.AllFeedPostsCount == 0 && vm.IsViewingFeed)
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

                if (height > 0)
                {
                    UpdateResponsiveLayout(width, height);
                }
            }
        }

        private void UpdateResponsiveLayout(double width, double height)
        {
            if (width <= 0 || height <= 0 || BindingContext is not CameraPageModel vm)
                return;

            var (topInsetDp, bottomInsetDp) = GetSystemInsetsDp();
            vm.UpdateResponsiveMetrics(width, height, topInsetDp, bottomInsetDp);
            CameraHeaderGrid.WidthRequest = width;
            CameraHeaderGrid.HeightRequest = height;
        }

        private static (double TopInsetDp, double BottomInsetDp) GetSystemInsetsDp()
        {
#if ANDROID
            var decorView = Platform.CurrentActivity?.Window?.DecorView;
            if (decorView != null)
            {
                float density = decorView.Resources?.DisplayMetrics?.Density ?? 2.625f;
                if (density <= 0)
                    density = 2.625f;

                var rootInsets = AndroidX.Core.View.ViewCompat.GetRootWindowInsets(decorView);
                if (rootInsets != null)
                {
                    int mask = AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars()
                               | AndroidX.Core.View.WindowInsetsCompat.Type.DisplayCutout();
                    var bars = rootInsets.GetInsetsIgnoringVisibility(mask);
                    if (bars != null)
                    {
                        return (bars.Top / density, bars.Bottom / density);
                    }
                }
            }
#endif
            return (28.0, 24.0);
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

            if (this.Width > 0 && this.Height > 0)
            {
                UpdateResponsiveLayout(this.Width, this.Height);
            }

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

        private const double ShutterPressedScale = 0.85;
        private const double ShutterPressedOpacity = 0.46;
        private bool _isTriggeringShutter;
        private long _shutterPressDownTick;

#if ANDROID
        private global::Android.Views.View? _attachedShutterNativeView;
        private bool _isShutterPressed;
        private bool _isShutterTouchInside;
#endif

        private void OnShutterButtonHandlerChanged(object? sender, EventArgs e)
        {
#if ANDROID
            if (_attachedShutterNativeView != null)
            {
                _attachedShutterNativeView.Touch -= OnShutterNativeTouch;
                _attachedShutterNativeView = null;
            }

            if (ShutterButtonGrid.Handler?.PlatformView is global::Android.Views.View nativeView)
            {
                _attachedShutterNativeView = nativeView;
                nativeView.Clickable = true;
                nativeView.Touch += OnShutterNativeTouch;
            }
#endif
        }

#if ANDROID
        private void OnShutterNativeTouch(object? sender, global::Android.Views.View.TouchEventArgs e)
        {
            var ev = e.Event;
            if (ev == null || sender is not global::Android.Views.View nativeView)
                return;

            switch (ev.ActionMasked)
            {
                case global::Android.Views.MotionEventActions.Down:
                    if (_isCapturingWithFlash || _isTriggeringShutter ||
                        (BindingContext is CameraPageModel vm && vm.IsPreviewing))
                    {
                        e.Handled = true;
                        return;
                    }

                    _isShutterPressed = true;
                    _isShutterTouchInside = true;
                    _shutterPressDownTick = Environment.TickCount64;
                    nativeView.Parent?.RequestDisallowInterceptTouchEvent(true);
                    SetShutterPressedVisual(true);
                    e.Handled = true;
                    break;

                case global::Android.Views.MotionEventActions.Move:
                    if (!_isShutterPressed)
                        return;

                    float density = nativeView.Resources?.DisplayMetrics?.Density ?? 2.75f;
                    float slop = 24f * density;
                    float x = ev.GetX();
                    float y = ev.GetY();
                    bool inside = x >= -slop && x <= nativeView.Width + slop &&
                                  y >= -slop && y <= nativeView.Height + slop;

                    if (inside != _isShutterTouchInside)
                    {
                        _isShutterTouchInside = inside;
                        SetShutterPressedVisual(inside);
                    }

                    e.Handled = true;
                    break;

                case global::Android.Views.MotionEventActions.Up:
                    if (!_isShutterPressed)
                        return;

                    bool shouldTrigger = _isShutterTouchInside;
                    _isShutterPressed = false;
                    _isShutterTouchInside = false;
                    nativeView.Parent?.RequestDisallowInterceptTouchEvent(false);
                    e.Handled = true;

                    if (shouldTrigger)
                    {
                        _ = ReleaseAndTriggerShutterAsync();
                    }
                    else
                    {
                        SetShutterPressedVisual(false);
                    }

                    break;

                case global::Android.Views.MotionEventActions.Cancel:
                    if (!_isShutterPressed)
                        return;

                    _isShutterPressed = false;
                    _isShutterTouchInside = false;
                    nativeView.Parent?.RequestDisallowInterceptTouchEvent(false);
                    SetShutterPressedVisual(false);
                    e.Handled = true;
                    break;
            }
        }
#endif

        private void SetShutterPressedVisual(bool pressed)
        {
            ShutterInnerCircle.AbortAnimation("ScaleTo");
            ShutterInnerCircle.AbortAnimation("FadeTo");

            if (pressed)
            {
                _ = ShutterInnerCircle.ScaleToAsync(ShutterPressedScale, 80, Easing.CubicOut);
                _ = ShutterInnerCircle.FadeToAsync(ShutterPressedOpacity, 80, Easing.CubicOut);
            }
            else
            {
                _ = ShutterInnerCircle.ScaleToAsync(1.0, 100, Easing.CubicOut);
                _ = ShutterInnerCircle.FadeToAsync(1.0, 100, Easing.CubicOut);
            }
        }

        private void ResetShutterVisualState()
        {
#if ANDROID
            _isShutterPressed = false;
            _isShutterTouchInside = false;
#endif
            ShutterInnerCircle.AbortAnimation("ScaleTo");
            ShutterInnerCircle.AbortAnimation("FadeTo");
            ShutterInnerCircle.Scale = 1.0;
            ShutterInnerCircle.Opacity = 1.0;
        }

        private async Task ReleaseAndTriggerShutterAsync()
        {
            if (_isTriggeringShutter || _isCapturingWithFlash)
            {
                SetShutterPressedVisual(false);
                return;
            }

            _isTriggeringShutter = true;
            try
            {
                long elapsedMs = Environment.TickCount64 - _shutterPressDownTick;
                if (elapsedMs < 70)
                {
                    await Task.Delay((int)(70 - elapsedMs));
                }

                ShutterInnerCircle.AbortAnimation("ScaleTo");
                ShutterInnerCircle.AbortAnimation("FadeTo");
                await Task.WhenAll(
                    ShutterInnerCircle.ScaleToAsync(1.0, 85, Easing.CubicOut),
                    ShutterInnerCircle.FadeToAsync(1.0, 85, Easing.CubicOut)
                );

                await TriggerShutterCaptureAsync();
            }
            finally
            {
                _isTriggeringShutter = false;
            }
        }

        private async Task TriggerShutterCaptureAsync()
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

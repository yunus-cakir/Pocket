using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Pocket.Client.PageModels;

namespace Pocket.Client.Pages
{
    public partial class DmPage : ContentPage
    {
        private double _emojiSheetPanInitialTranslationY = 0;
        private long _ignoreEmojiScrollSyncUntilTick = 0;

        public DmPage(DmPageModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
            viewModel.ScrollToBottomAction = ScrollToBottom;

            viewModel.OpenEmojiPickerAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    if (DmMessageEntry.IsFocused)
                    {
                        DmMessageEntry.Unfocus();
                    }

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

            viewModel.ShowSendFailedToastAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await ShowToastPillAsync(SendFailedToastPill, isFailureToast: true, durationMs: 2500);
                });
            };

            viewModel.ShowCopiedToastAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    await ShowToastPillAsync(CopiedToastPill, isFailureToast: false, durationMs: 1500);
                });
            };

            viewModel.OpenMessageActionsAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    UnfocusDmMessageEntry();

                    MessageActionsSheetCard.AbortAnimation("TranslateTo");
                    MessageActionsBackdrop.AbortAnimation("FadeTo");

                    MessageActionsOverlayGrid.IsVisible = true;
                    MessageActionsSheetCard.TranslationY = 280;
                    MessageActionsBackdrop.Opacity = 0;

                    await Task.WhenAll(
                        MessageActionsSheetCard.TranslateToAsync(0, 0, 220, Easing.CubicOut),
                        MessageActionsBackdrop.FadeToAsync(1, 220, Easing.CubicOut)
                    );
                });
            };

            viewModel.CloseMessageActionsAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(async () => { await AnimateCloseMessageActionsAsync(); });
            };
        }

        private System.Threading.CancellationTokenSource? _sendFailedToastCts;
        private System.Threading.CancellationTokenSource? _copiedToastCts;
        private double _messageActionsPanInitialTranslationY = 0;

#if ANDROID
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<global::Android.Views.View, VisualElement>
            _longPressTargets = new();
#endif

        private void OnMessageBubbleHandlerChanged(object? sender, EventArgs e)
        {
#if ANDROID
            if (sender is VisualElement ve && ve.Handler?.PlatformView is global::Android.Views.View nativeView)
            {
                nativeView.LongClickable = true;
                bool alreadyHooked = _longPressTargets.TryGetValue(nativeView, out _);
                _longPressTargets.AddOrUpdate(nativeView, ve);

                if (!alreadyHooked)
                {
                    nativeView.LongClick += (s, args) =>
                    {
                        if (s is global::Android.Views.View v &&
                            _longPressTargets.TryGetValue(v, out var targetElement) &&
                            targetElement.BindingContext is Pocket.Client.Models.DirectMessage dm &&
                            BindingContext is DmPageModel vm)
                        {
                            v.PerformHapticFeedback(global::Android.Views.FeedbackConstants.LongPress);
                            vm.OpenMessageActionsCommand.Execute(dm);
                            args.Handled = true;
                        }
                    };
                }
            }
#endif
        }

        private void UnfocusDmMessageEntry()
        {
            if (DmMessageEntry.IsFocused)
            {
                DmMessageEntry.Unfocus();
            }
#if ANDROID
            if (DmMessageEntry.Handler?.PlatformView is global::Android.Views.View nativeView)
            {
                nativeView.ClearFocus();
                var imm = nativeView.Context?.GetSystemService(global::Android.Content.Context.InputMethodService)
                    as global::Android.Views.InputMethods.InputMethodManager;
                imm?.HideSoftInputFromWindow(nativeView.WindowToken,
                    global::Android.Views.InputMethods.HideSoftInputFlags.None);
            }
#endif
        }

        private async void OnMessageActionsBackdropTapped(object? sender, TappedEventArgs e)
        {
            await AnimateCloseMessageActionsAsync();
        }

        private async Task AnimateCloseMessageActionsAsync()
        {
            if (!MessageActionsOverlayGrid.IsVisible)
            {
                if (BindingContext is DmPageModel earlyVm)
                {
                    earlyVm.IsMessageActionsOpen = false;
                    earlyVm.SelectedActionMessage = null;
                }

                return;
            }

            MessageActionsSheetCard.AbortAnimation("TranslateTo");
            MessageActionsBackdrop.AbortAnimation("FadeTo");

            await Task.WhenAll(
                MessageActionsSheetCard.TranslateToAsync(0, 280, 190, Easing.CubicIn),
                MessageActionsBackdrop.FadeToAsync(0, 190, Easing.Linear)
            );

            MessageActionsOverlayGrid.IsVisible = false;
            if (BindingContext is DmPageModel vm)
            {
                vm.IsMessageActionsOpen = false;
                vm.SelectedActionMessage = null;
            }
        }

        private async void OnMessageActionsSheetPanUpdated(object? sender, PanUpdatedEventArgs e)
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _messageActionsPanInitialTranslationY = MessageActionsSheetCard.TranslationY;
                    break;

                case GestureStatus.Running:
                    if (e.TotalY > 0)
                    {
                        MessageActionsSheetCard.TranslationY = _messageActionsPanInitialTranslationY + e.TotalY;
                        double progress = Math.Clamp(1.0 - (MessageActionsSheetCard.TranslationY / 240.0), 0.0, 1.0);
                        MessageActionsBackdrop.Opacity = progress;
                    }

                    break;

                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    if (MessageActionsSheetCard.TranslationY > 55)
                    {
                        await AnimateCloseMessageActionsAsync();
                    }
                    else
                    {
                        await Task.WhenAll(
                            MessageActionsSheetCard.TranslateToAsync(0, 0, 150, Easing.CubicOut),
                            MessageActionsBackdrop.FadeToAsync(1, 150, Easing.Linear)
                        );
                    }

                    break;
            }
        }

        private async Task ShowToastPillAsync(Border pill, bool isFailureToast, int durationMs)
        {
            var cts = new System.Threading.CancellationTokenSource();
            if (isFailureToast)
            {
                _sendFailedToastCts?.Cancel();
                _sendFailedToastCts = cts;
            }
            else
            {
                _copiedToastCts?.Cancel();
                _copiedToastCts = cts;
            }

            pill.AbortAnimation("FadeTo");
            pill.AbortAnimation("TranslateTo");

            pill.Opacity = 0;
            pill.TranslationY = 24;
            pill.IsVisible = true;

            await Task.WhenAll(
                pill.FadeToAsync(1, 180, Easing.CubicOut),
                pill.TranslateToAsync(0, 0, 180, Easing.CubicOut)
            );

            try
            {
                await Task.Delay(durationMs, cts.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (cts.IsCancellationRequested)
                return;

            await Task.WhenAll(
                pill.FadeToAsync(0, 180, Easing.CubicIn),
                pill.TranslateToAsync(0, 20, 180, Easing.CubicIn)
            );

            if (!cts.IsCancellationRequested)
            {
                pill.IsVisible = false;
            }
        }

        private void ScrollToBottom()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (BindingContext is DmPageModel vm && vm.Messages.Count > 0)
                {
                    MessagesCollectionView.ScrollTo(vm.Messages.Count - 1, position: ScrollToPosition.End,
                        animate: false);
                }
            });
        }

        private async void OnEmojiPickerBackdropTapped(object? sender, TappedEventArgs e)
        {
            await AnimateCloseEmojiPickerAsync();
        }

        private async Task AnimateCloseEmojiPickerAsync()
        {
            UnfocusEmojiSearchEntry();

            if (!EmojiPickerOverlayGrid.IsVisible)
            {
                if (BindingContext is DmPageModel earlyVm)
                {
                    earlyVm.IsEmojiPickerOpen = false;
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

            if (BindingContext is DmPageModel vm)
            {
                vm.IsEmojiPickerOpen = false;
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
            if (BindingContext is not DmPageModel vm || rowIndex < 0 || rowIndex >= vm.EmojiPickerRows.Count)
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

            if (BindingContext is not DmPageModel vm || !string.IsNullOrEmpty(vm.EmojiSearchText))
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
            if (EmojiSearchEntry.Handler?.PlatformView is AndroidX.AppCompat.Widget.AppCompatEditText editText)
            {
                void ActivateFocus()
                {
                    if (BindingContext is not DmPageModel vm || !vm.IsEmojiPickerOpen)
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

        private void OnDmMessageEntryFocused(object? sender, FocusEventArgs e)
        {
#if ANDROID
            RegisterKeyboardVisibilityWatcher(startPollTimer: true);
#endif
        }

        private void OnDmMessageEntryUnfocused(object? sender, FocusEventArgs e)
        {
        }

        private void OnEmojiSearchEntryFocused(object? sender, FocusEventArgs e)
        {
            EmojiCategoryBarBorder.IsVisible = false;
#if ANDROID
            RegisterKeyboardVisibilityWatcher(startPollTimer: true);
#endif
        }

        private void OnEmojiSearchEntryUnfocused(object? sender, FocusEventArgs e)
        {
            EmojiCategoryBarBorder.IsVisible = true;
        }

        private void OnEmojiSearchEntryCompleted(object? sender, EventArgs e)
        {
            UnfocusEmojiSearchEntry();
        }

        protected override bool OnBackButtonPressed()
        {
            if (BindingContext is DmPageModel vm)
            {
                if (vm.IsMessageActionsOpen)
                {
                    _ = AnimateCloseMessageActionsAsync();
                    return true;
                }

                if (vm.IsEmojiPickerOpen)
                {
                    _ = AnimateCloseEmojiPickerAsync();
                    return true;
                }
            }

            return base.OnBackButtonPressed();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
#if ANDROID
            RegisterKeyboardVisibilityWatcher(startPollTimer: false);
#endif
            if (BindingContext is DmPageModel vm)
            {
                vm.SetDmWindowActive(true);
                await vm.LoadConversationAsync();
            }
        }

        protected override void OnDisappearing()
        {
            if (BindingContext is DmPageModel vm)
            {
                vm.SetDmWindowActive(false);
            }

            base.OnDisappearing();
#if ANDROID
            UnregisterKeyboardVisibilityWatcher();
#endif
        }

#if ANDROID
        private KeyboardGlobalLayoutListener? _keyboardListener;
        private ImeInsetsAnimationCallback? _imeAnimationCallback;
        private Microsoft.Maui.Dispatching.IDispatcherTimer? _keyboardPollTimer;
        private bool _wasKeyboardOpen;
        private bool _isImeCallbackDriving;
        private long _keyboardWatcherStartTick;
        private int _imeUpperBoundPx;
        private AndroidX.RecyclerView.Widget.RecyclerView? _messagesAnchorRv;
        private MessagesLayoutAnchorListener? _messagesAnchorListener;

        private void RegisterKeyboardVisibilityWatcher(bool startPollTimer)
        {
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
                }

                AndroidX.Core.View.ViewCompat.SetWindowInsetsAnimationCallback(decorView, _imeAnimationCallback);
            }

            if (MessagesCollectionView.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView messagesRv &&
                !ReferenceEquals(_messagesAnchorRv, messagesRv))
            {
                if (_messagesAnchorRv != null && _messagesAnchorListener != null)
                {
                    _messagesAnchorRv.RemoveOnLayoutChangeListener(_messagesAnchorListener);
                }

                _messagesAnchorListener ??= new MessagesLayoutAnchorListener(this);
                messagesRv.AddOnLayoutChangeListener(_messagesAnchorListener);
                _messagesAnchorRv = messagesRv;
            }

            if (startPollTimer)
            {
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
        }

        private void UnregisterKeyboardVisibilityWatcher()
        {
            _keyboardPollTimer?.Stop();
            _isImeCallbackDriving = false;
            _wasKeyboardOpen = false;

            var activity = Platform.CurrentActivity;
            var decorView = activity?.Window?.DecorView;
            if (decorView != null)
            {
                if (decorView.ViewTreeObserver != null && _keyboardListener != null)
                {
                    decorView.ViewTreeObserver.RemoveOnGlobalLayoutListener(_keyboardListener);
                }

                AndroidX.Core.View.ViewCompat.SetWindowInsetsAnimationCallback(decorView, null);
            }

            _keyboardListener = null;
            _imeAnimationCallback = null;

            if (_messagesAnchorRv != null && _messagesAnchorListener != null)
            {
                _messagesAnchorRv.RemoveOnLayoutChangeListener(_messagesAnchorListener);
            }

            _messagesAnchorRv = null;

            KeyboardSpacerBox.HeightRequest = 0;
            BottomInputBar.TranslationY = 0;
            PhotoReplyBanner.TranslationY = 0;
        }

        private void OnImeAnimationStart(AndroidX.Core.View.WindowInsetsAnimationCompat.BoundsCompat bounds)
        {
            _imeUpperBoundPx = bounds.UpperBound.Bottom;
            _isImeCallbackDriving = true;

            // Opening while scrolled up: jump to the latest message first; the layout anchor
            // listener then keeps it pinned above the input bar as the list shrinks.
            if (!_wasKeyboardOpen && !EmojiPickerOverlayGrid.IsVisible && DmMessageEntry.IsFocused &&
                MessagesCollectionView.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv &&
                rv.CanScrollVertically(1))
            {
                ScrollToBottom();
            }
        }

        /// <summary>
        /// Resizes the message list by the keyboard overlap (via the bottom spacer row) so the
        /// list really shrinks instead of the input bar just floating over it.
        /// </summary>
        private void ApplyDmKeyboardOverlap(double overlapDp)
        {
            overlapDp = Math.Max(0, overlapDp);

            if (BottomInputBar.TranslationY != 0)
                BottomInputBar.TranslationY = 0;
            if (PhotoReplyBanner.TranslationY != 0)
                PhotoReplyBanner.TranslationY = 0;

            if (Math.Abs(KeyboardSpacerBox.HeightRequest - overlapDp) > 0.5 ||
                (overlapDp == 0 && KeyboardSpacerBox.HeightRequest != 0))
            {
                KeyboardSpacerBox.HeightRequest = overlapDp;
            }
        }

        private void ApplyEmojiSheetKeyboardOverlap(double overlapDp)
        {
            if (overlapDp > 0)
            {
                double maxHeight = DmRootGrid.Height > 300 ? DmRootGrid.Height - 72 : 580;
                EmojiPickerSheetCard.HeightRequest = Math.Min(maxHeight, 476 + overlapDp * 0.48);
                EmojiPickerSheetCard.Padding = new Thickness(0, 0, 0, overlapDp);
                EmojiCategoryBarBorder.IsVisible = false;
            }
        }

        /// <summary>
        /// Keeps the bottom edge of the message list anchored when its height changes
        /// (keyboard open/close, reply banner), like a native chat view.
        /// </summary>
        private void OnMessagesListLayoutChanged(AndroidX.RecyclerView.Widget.RecyclerView rv, int oldHeight, int newHeight)
        {
            if (oldHeight <= 0 || newHeight <= 0 || oldHeight == newHeight)
                return;

            int delta = oldHeight - newHeight;
            if (delta > 0)
            {
                // Shrinking: scroll content up by the lost height so the latest messages stay visible.
                rv.ScrollBy(0, delta);
            }
            else if (rv.CanScrollVertically(1))
            {
                // Growing while not at the end: keep the same messages at the bottom edge.
                // (At the end, LinearLayoutManager already closes the gap itself.)
                rv.ScrollBy(0, delta);
            }
        }

        private void OnImeAnimationProgress(
            AndroidX.Core.View.WindowInsetsCompat insets,
            AndroidX.Core.View.WindowInsetsAnimationCompat imeAnim)
        {
            var activity = Platform.CurrentActivity;
            var decorView = activity?.Window?.DecorView;
            float density = decorView?.Resources?.DisplayMetrics?.Density ?? 2.75f;

            int imeBottomPx = insets.GetInsets(AndroidX.Core.View.WindowInsetsCompat.Type.Ime())?.Bottom ?? 0;
            int navBottomPx = insets.GetInsets(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars())?.Bottom ?? 0;

            float fraction = Math.Clamp(imeAnim.InterpolatedFraction, 0f, 1f);
            if (imeBottomPx <= 0 && _imeUpperBoundPx > 0 && !_wasKeyboardOpen)
            {
                imeBottomPx = (int)Math.Round(_imeUpperBoundPx * fraction);
            }

            int overlapPx = Math.Max(0, imeBottomPx - navBottomPx);
            double overlapDp = overlapPx / density;

            _isImeCallbackDriving = true;

            if (EmojiPickerOverlayGrid.IsVisible)
            {
                ApplyEmojiSheetKeyboardOverlap(overlapDp);
                return;
            }

            ApplyDmKeyboardOverlap(overlapDp);
        }

        private void OnImeAnimationEnd()
        {
            _isImeCallbackDriving = false;
            CheckKeyboardState(enforceCloseGracePeriod: false);

            // Insets / visible frame can lag a frame behind the animation end on some devices;
            // re-check once things settle so the bar never stays stuck mid-screen.
            var decorView = Platform.CurrentActivity?.Window?.DecorView;
            decorView?.PostDelayed(() => CheckKeyboardState(enforceCloseGracePeriod: false), 120);
            decorView?.PostDelayed(() => CheckKeyboardState(enforceCloseGracePeriod: false), 350);
        }

        private void CheckKeyboardState(bool enforceCloseGracePeriod = true)
        {
            if (_isImeCallbackDriving || _keyboardListener == null)
                return;

            var activity = Platform.CurrentActivity;
            var decorView = activity?.Window?.DecorView;
            if (decorView == null)
                return;

            var rootInsets = AndroidX.Core.View.ViewCompat.GetRootWindowInsets(decorView);
            int imeBottomPx = rootInsets?.GetInsets(AndroidX.Core.View.WindowInsetsCompat.Type.Ime())?.Bottom ?? 0;
            int navBottomPx = rootInsets?.GetInsets(AndroidX.Core.View.WindowInsetsCompat.Type.SystemBars())?.Bottom ?? 0;

            var rect = new global::Android.Graphics.Rect();
            decorView.GetWindowVisibleDisplayFrame(rect);
            int screenHeight = decorView.RootView?.Height ?? 0;
            int frameKeypadPx = screenHeight > 0 ? Math.Max(0, screenHeight - rect.Bottom) : 0;

            int rawKeyboardPx = Math.Max(imeBottomPx, frameKeypadPx);
            bool isKeyboardOpen = (rootInsets?.IsVisible(AndroidX.Core.View.WindowInsetsCompat.Type.Ime()) ?? false)
                                  || (screenHeight > 0 && rawKeyboardPx > screenHeight * 0.15);

            float density = decorView.Resources?.DisplayMetrics?.Density ?? 2.75f;
            double overlapDp = Math.Max(0, rawKeyboardPx - navBottomPx) / density;

            if (isKeyboardOpen && overlapDp > 120)
            {
                _wasKeyboardOpen = true;

                if (EmojiPickerOverlayGrid.IsVisible)
                {
                    ApplyEmojiSheetKeyboardOverlap(overlapDp);
                }
                else
                {
                    ApplyDmKeyboardOverlap(overlapDp);
                }

                return;
            }

            if (isKeyboardOpen)
            {
                // Keyboard still partially visible (mid-transition) — leave current layout alone.
                return;
            }

            // Keyboard is closed: the input bar must always return to the bottom,
            // regardless of how we got here.
            if (!EmojiPickerOverlayGrid.IsVisible)
            {
                ApplyDmKeyboardOverlap(0);
            }

            if (_wasKeyboardOpen &&
                (!enforceCloseGracePeriod || (Environment.TickCount64 - _keyboardWatcherStartTick) > 250))
            {
                _wasKeyboardOpen = false;
                _keyboardPollTimer?.Stop();

                ApplyDmKeyboardOverlap(0);

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
                else if (DmMessageEntry.IsFocused)
                {
                    DmMessageEntry.Unfocus();
                }
            }
        }

        private sealed class ImeInsetsAnimationCallback : AndroidX.Core.View.WindowInsetsAnimationCompat.Callback
        {
            private readonly DmPage _page;

            public ImeInsetsAnimationCallback(DmPage page)
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

        private sealed class MessagesLayoutAnchorListener : Java.Lang.Object,
            global::Android.Views.View.IOnLayoutChangeListener
        {
            private readonly DmPage _page;

            public MessagesLayoutAnchorListener(DmPage page)
            {
                _page = page;
            }

            public void OnLayoutChange(global::Android.Views.View? v, int left, int top, int right, int bottom,
                int oldLeft, int oldTop, int oldRight, int oldBottom)
            {
                if (v is AndroidX.RecyclerView.Widget.RecyclerView rv)
                {
                    _page.OnMessagesListLayoutChanged(rv, oldBottom - oldTop, bottom - top);
                }
            }
        }

        private sealed class KeyboardGlobalLayoutListener : Java.Lang.Object,
            global::Android.Views.ViewTreeObserver.IOnGlobalLayoutListener
        {
            private readonly DmPage _page;

            public KeyboardGlobalLayoutListener(DmPage page)
            {
                _page = page;
            }

            public void OnGlobalLayout()
            {
                _page.CheckKeyboardState();
            }
        }
#endif
    }
}

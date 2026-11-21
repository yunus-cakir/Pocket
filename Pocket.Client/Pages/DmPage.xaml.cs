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
                MainThread.BeginInvokeOnMainThread(async () => { await ShowMessagePopupAsync(); });
            };

            viewModel.CloseMessageActionsAction = () =>
            {
                MainThread.BeginInvokeOnMainThread(async () => { await AnimateCloseMessageActionsAsync(); });
            };

            viewModel.BeginEditMessageAction = dm =>
            {
                MainThread.BeginInvokeOnMainThread(async () => { await BeginEditMessageAsync(dm); });
            };
        }

        /// <summary>Opens the keyboard on the input box (cursor at the end) and keeps the edited message in view.</summary>
        private async Task BeginEditMessageAsync(Pocket.Client.Models.DirectMessage dm)
        {
            // Let the action sheet slide away first so it does not fight the keyboard.
            await Task.Delay(200);
            if (BindingContext is not DmPageModel vm || !ReferenceEquals(vm.EditingMessage, dm))
                return;

            DmMessageEntry.Focus();
#if ANDROID
            if (DmMessageEntry.Handler?.PlatformView is AndroidX.AppCompat.Widget.AppCompatEditText editText)
            {
                void ActivateFocus()
                {
                    if (BindingContext is not DmPageModel { IsEditingMessage: true })
                        return;

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
            DmMessageEntry.CursorPosition = DmMessageEntry.Text?.Length ?? 0;

            // The list shrinks as the keyboard opens: bring the edited message back into view once it settled.
            await Task.Delay(380);
            if (ReferenceEquals(vm.EditingMessage, dm) && vm.Messages.Contains(dm))
            {
                MessagesCollectionView.ScrollTo(dm, position: ScrollToPosition.MakeVisible, animate: true);
            }
        }

        private System.Threading.CancellationTokenSource? _sendFailedToastCts;
        private System.Threading.CancellationTokenSource? _copiedToastCts;

#if ANDROID
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<global::Android.Views.View, VisualElement>
            _longPressTargets = new();

        /// <summary>Bubble that was long-pressed: the popup is placed around it.</summary>
        private WeakReference<global::Android.Views.View>? _longPressedBubble;
#endif

        private const double PopupGap = 8;
        private const double PopupSideMargin = 12;
        private const double PopupTopLimit = 44;

        private async Task ShowMessagePopupAsync()
        {
            bool keyboardWasOpen = KeyboardSpacerBox.HeightRequest > 0 || DmMessageEntry.IsFocused;
            UnfocusDmMessageEntry();

            if (keyboardWasOpen)
            {
                // The list moves while the keyboard closes: place the popup once it has settled.
                for (int i = 0; i < 25 && KeyboardSpacerBox.HeightRequest > 0; i++)
                {
                    await Task.Delay(20);
                }

                await Task.Delay(40);
            }

            if (BindingContext is not DmPageModel vm || !vm.IsMessageActionsOpen)
                return;

            if (vm.SelectedActionMessage?.IsIncoming != true)
            {
                // Own messages keep the bottom sheet.
                await ShowMessageSheetAsync();
                return;
            }

            AbortPopupAnimations();
            MessageActionsBackdrop.Opacity = 0;
            foreach (var card in new VisualElement[] { ReactionBarCard, MessageMenuCard })
            {
                card.Opacity = 0;
                card.Scale = 0.8;
            }

            MessageActionsOverlayGrid.Opacity = 1;
            MessageActionsOverlayGrid.IsVisible = true;

            await PositionMessagePopupAsync();
            if (!vm.IsMessageActionsOpen)
                return;

            // Grow out of the bubble downwards.
            ReactionBarCard.AnchorY = 0;
            MessageMenuCard.AnchorY = 0;
            ReactionBarCard.AnchorX = 0.15;
            MessageMenuCard.AnchorX = 0.15;

            // Fade + scale only, no slide; the chat behind dims.
            await Task.WhenAll(
                MessageActionsBackdrop.FadeToAsync(1, 180, Easing.CubicOut),
                ReactionBarCard.FadeToAsync(1, 160, Easing.CubicOut),
                ReactionBarCard.ScaleToAsync(1, 220, Easing.SpringOut),
                MessageMenuCard.FadeToAsync(1, 160, Easing.CubicOut),
                MessageMenuCard.ScaleToAsync(1, 220, Easing.SpringOut));
        }

        private void AbortPopupAnimations()
        {
            MessageActionsBackdrop.AbortAnimation("FadeTo");
            foreach (var card in new VisualElement[] { ReactionBarCard, MessageMenuCard })
            {
                card.AbortAnimation("FadeTo");
                card.AbortAnimation("ScaleTo");
            }
        }

        private async Task ShowMessageSheetAsync()
        {
            MessageActionsSheetCard.AbortAnimation("TranslateTo");
            MessageSheetBackdrop.AbortAnimation("FadeTo");

            MessageSheetOverlayGrid.IsVisible = true;
            MessageActionsSheetCard.TranslationY = 280;
            MessageSheetBackdrop.Opacity = 0;

            await Task.WhenAll(
                MessageActionsSheetCard.TranslateToAsync(0, 0, 220, Easing.CubicOut),
                MessageSheetBackdrop.FadeToAsync(1, 220, Easing.CubicOut)
            );
        }

        private double _messageSheetPanInitialTranslationY;

        private async void OnMessageActionsSheetPanUpdated(object? sender, PanUpdatedEventArgs e)
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _messageSheetPanInitialTranslationY = MessageActionsSheetCard.TranslationY;
                    break;

                case GestureStatus.Running:
                    if (e.TotalY > 0)
                    {
                        MessageActionsSheetCard.TranslationY = _messageSheetPanInitialTranslationY + e.TotalY;
                        MessageSheetBackdrop.Opacity =
                            Math.Clamp(1.0 - (MessageActionsSheetCard.TranslationY / 240.0), 0.0, 1.0);
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
                            MessageSheetBackdrop.FadeToAsync(1, 150, Easing.Linear)
                        );
                    }

                    break;
            }
        }

        /// <summary>Bubble bounds captured at the moment of the long press (fallback if the view is recycled later).</summary>
        private Rect? _longPressedBubbleBoundsAtPress;

        /// <summary>How far the message list was lifted so the popup fits under the bubble (undone on close).</summary>
        private double _popupListLift;

        /// <summary>
        /// Stacks the popup directly under the pressed bubble: reaction bar, then the menu. If there is not
        /// enough room below, the message list is scrolled / lifted so the bubble moves up instead.
        /// </summary>
        private async Task PositionMessagePopupAsync()
        {
            // Real sizes only exist after a layout pass: Measure() before that can report 0 (→ overlapping cards).
            for (int i = 0; i < 20 && (MessageMenuCard.Height <= 0 || (ReactionBarCard.IsVisible && ReactionBarCard.Height <= 0)); i++)
            {
                await Task.Delay(16);
            }

            double rootW = DmRootGrid.Width > 0 ? DmRootGrid.Width : Width;
            double rootH = DmRootGrid.Height > 0 ? DmRootGrid.Height : Height;
            double bottomLimit = rootH - PopupSideMargin;

            double menuW = MessageMenuCard.Width > 0 ? MessageMenuCard.Width : MessageMenuCard.WidthRequest;
            double menuH = MessageMenuCard.Height;
            bool showBar = ReactionBarCard.IsVisible;
            double barW = showBar ? ReactionBarCard.Width : 0;
            double barH = showBar ? ReactionBarCard.Height : 0;

            var bubble = GetLongPressedBubbleBounds() ?? _longPressedBubbleBoundsAtPress
                         ?? new Rect(54, rootH / 2 - 20, 146, 40);

            double stackH = (showBar ? barH + PopupGap : 0) + menuH;
            double deficit = bubble.Bottom + PopupGap + stackH - bottomLimit;
            if (deficit > 0)
            {
                // Not enough room under the bubble: move the chat up (scroll first, lift the rest).
                double maxLift = Math.Max(0, bubble.Top - PopupTopLimit);
                double needed = Math.Min(deficit, maxLift);
                double scrolled = ScrollMessagesBy(needed);
                if (scrolled > 0)
                {
                    await Task.Delay(16);
                    bubble = GetLongPressedBubbleBounds() ?? new Rect(bubble.X, bubble.Y - scrolled, bubble.Width, bubble.Height);
                }

                double rest = bubble.Bottom + PopupGap + stackH - bottomLimit;
                if (rest > 0)
                {
                    _popupListLift = Math.Min(rest, Math.Max(0, bubble.Top - PopupTopLimit));
                    MessagesCollectionView.AbortAnimation("TranslateTo");
                    _ = MessagesCollectionView.TranslateToAsync(0, -_popupListLift, 180, Easing.CubicOut);
                    bubble = new Rect(bubble.X, bubble.Y - _popupListLift, bubble.Width, bubble.Height);
                }
            }

            double Clamp(double v, double min, double max) => max < min ? min : Math.Clamp(v, min, max);

            double barX = Clamp(bubble.Left - 6, PopupSideMargin, rootW - PopupSideMargin - barW);
            double menuX = Clamp(bubble.Left, PopupSideMargin, rootW - PopupSideMargin - menuW);

            double barY = bubble.Bottom + PopupGap;
            double menuY = showBar ? barY + barH + PopupGap : barY;

            ReactionBarCard.Margin = new Thickness(barX, barY, 0, 0);
            MessageMenuCard.Margin = new Thickness(menuX, menuY, 0, 0);
        }

        /// <summary>Scrolls the message list content up by up to <paramref name="dp"/>; returns how much it moved.</summary>
        private double ScrollMessagesBy(double dp)
        {
#if ANDROID
            if (dp > 0 && MessagesCollectionView.Handler?.PlatformView is AndroidX.RecyclerView.Widget.RecyclerView rv)
            {
                float density = rv.Resources?.DisplayMetrics?.Density ?? 2.75f;
                int before = rv.ComputeVerticalScrollOffset();
                rv.ScrollBy(0, (int)Math.Ceiling(dp * density));
                int after = rv.ComputeVerticalScrollOffset();
                return Math.Max(0, (after - before) / density);
            }
#endif
            return 0;
        }

        private void ResetPopupListLift()
        {
            if (_popupListLift == 0 && MessagesCollectionView.TranslationY == 0)
                return;

            _popupListLift = 0;
            MessagesCollectionView.AbortAnimation("TranslateTo");
            _ = MessagesCollectionView.TranslateToAsync(0, 0, 160, Easing.CubicOut);
        }

        /// <summary>Long-pressed bubble's bounds in <see cref="DmRootGrid"/> coordinates (dp).</summary>
        private Rect? GetLongPressedBubbleBounds()
        {
#if ANDROID
            if (_longPressedBubble == null || !_longPressedBubble.TryGetTarget(out var bubbleView) ||
                !bubbleView.IsAttachedToWindow ||
                DmRootGrid.Handler?.PlatformView is not global::Android.Views.View rootView)
            {
                return null;
            }

            // The list recycles rows: make sure this view still shows the pressed message.
            if (BindingContext is DmPageModel { SelectedActionMessage: { } selected } &&
                _longPressTargets.TryGetValue(bubbleView, out var element) &&
                !ReferenceEquals(element.BindingContext, selected))
            {
                return null;
            }

            var bubbleLoc = new int[2];
            var rootLoc = new int[2];
            bubbleView.GetLocationInWindow(bubbleLoc);
            rootView.GetLocationInWindow(rootLoc);
            float density = bubbleView.Resources?.DisplayMetrics?.Density ?? 2.75f;

            return new Rect(
                (bubbleLoc[0] - rootLoc[0]) / density,
                (bubbleLoc[1] - rootLoc[1]) / density,
                bubbleView.Width / density,
                bubbleView.Height / density);
#else
            return null;
#endif
        }

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
                            _longPressedBubble = new WeakReference<global::Android.Views.View>(v);
                            _longPressedBubbleBoundsAtPress = GetLongPressedBubbleBounds();
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
            if (MessageSheetOverlayGrid.IsVisible)
            {
                MessageActionsSheetCard.AbortAnimation("TranslateTo");
                MessageSheetBackdrop.AbortAnimation("FadeTo");

                await Task.WhenAll(
                    MessageActionsSheetCard.TranslateToAsync(0, 280, 190, Easing.CubicIn),
                    MessageSheetBackdrop.FadeToAsync(0, 190, Easing.Linear)
                );

                MessageSheetOverlayGrid.IsVisible = false;
            }
            else if (MessageActionsOverlayGrid.IsVisible)
            {
                AbortPopupAnimations();
                ResetPopupListLift();
                await Task.WhenAll(
                    MessageActionsBackdrop.FadeToAsync(0, 140, Easing.CubicIn),
                    ReactionBarCard.FadeToAsync(0, 120, Easing.CubicIn),
                    ReactionBarCard.ScaleToAsync(0.9, 120, Easing.CubicIn),
                    MessageMenuCard.FadeToAsync(0, 120, Easing.CubicIn),
                    MessageMenuCard.ScaleToAsync(0.9, 120, Easing.CubicIn));

                MessageActionsOverlayGrid.IsVisible = false;
            }

            if (BindingContext is DmPageModel vm)
            {
                vm.IsMessageActionsOpen = false;
                vm.SelectedActionMessage = null;
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

                if (vm.IsEditingMessage)
                {
                    vm.CancelEditMessageCommand.Execute(null);
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
                vm.CancelEditMessageCommand.Execute(null);
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
        private bool _isLegacyImeSubscribed;

        private void OnLegacyImeChanged() => CheckKeyboardState();

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

            // Android 10 and older report no IME insets with ADJUST_NOTHING: measure the keyboard separately.
            Pocket.Client.Services.LegacyImeHeightTracker.EnsureAttached(activity);
            if (Pocket.Client.Services.LegacyImeHeightTracker.IsNeeded && !_isLegacyImeSubscribed)
            {
                Pocket.Client.Services.LegacyImeHeightTracker.Changed += OnLegacyImeChanged;
                _isLegacyImeSubscribed = true;
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

            if (_isLegacyImeSubscribed)
            {
                Pocket.Client.Services.LegacyImeHeightTracker.Changed -= OnLegacyImeChanged;
                _isLegacyImeSubscribed = false;
            }

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
            int legacyKeyboardPx = Pocket.Client.Services.LegacyImeHeightTracker.GetRawKeyboardPx(decorView);

            int rawKeyboardPx = Math.Max(imeBottomPx, Math.Max(frameKeypadPx, legacyKeyboardPx));
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

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
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    FocusEmojiSearchEntry();
                });
            };
        }

        private void ScrollToBottom()
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (BindingContext is DmPageModel vm && vm.Messages.Count > 0)
                {
                    MessagesCollectionView.ScrollTo(vm.Messages.Count - 1, position: ScrollToPosition.End, animate: false);
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

        private void OnEmojiSearchEntryFocused(object? sender, FocusEventArgs e)
        {
            EmojiCategoryBarBorder.IsVisible = false;
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
            if (BindingContext is DmPageModel vm && vm.IsEmojiPickerOpen)
            {
                _ = AnimateCloseEmojiPickerAsync();
                return true;
            }

            return base.OnBackButtonPressed();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
#if ANDROID
            Platform.CurrentActivity?.Window?.SetSoftInputMode(global::Android.Views.SoftInput.AdjustResize);
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
            Platform.CurrentActivity?.Window?.SetSoftInputMode(global::Android.Views.SoftInput.AdjustNothing);
#endif
        }
    }
}

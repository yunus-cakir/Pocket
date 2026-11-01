using Microsoft.Maui.Controls;
using Pocket.Client.PageModels;

namespace Pocket.Client.Pages
{
    public partial class DmPage : ContentPage
    {
        public DmPage(DmPageModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
#if ANDROID
            Platform.CurrentActivity?.Window?.SetSoftInputMode(global::Android.Views.SoftInput.AdjustResize);
#endif
            if (BindingContext is DmPageModel vm)
            {
                await vm.LoadConversationAsync();
            }
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
#if ANDROID
            Platform.CurrentActivity?.Window?.SetSoftInputMode(global::Android.Views.SoftInput.AdjustNothing);
#endif
        }
    }
}

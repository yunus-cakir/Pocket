using Microsoft.Maui.Controls;
using Pocket.Client.PageModels;

namespace Pocket.Client.Pages
{
    public partial class FeedPage : ContentPage
    {
        public FeedPage(FeedPageModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }
    }
}

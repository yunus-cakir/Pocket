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
    }
}

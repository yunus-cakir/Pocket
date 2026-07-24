using Microsoft.Maui.Controls;
using Pocket.Client.PageModels;

namespace Pocket.Client.Pages
{
    public partial class DmListPage : ContentPage
    {
        public DmListPage(DmListPageModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }
    }
}

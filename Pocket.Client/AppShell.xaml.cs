using Pocket.Client.PageModels;
using Pocket.Client.Pages;
using Microsoft.Maui.Controls;

namespace Pocket.Client
{
    public partial class AppShell : Shell
    {
        public AppShell(AppShellModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;

            Routing.RegisterRoute(nameof(DmListPage), typeof(DmListPage));
            Routing.RegisterRoute(nameof(DmPage), typeof(DmPage));
        }
    }
}

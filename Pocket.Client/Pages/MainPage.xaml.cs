using Pocket.Client.Models;
using Pocket.Client.PageModels;

namespace Pocket.Client.Pages
{
    public partial class MainPage : ContentPage
    {
        public MainPage(MainPageModel model)
        {
            InitializeComponent();
            BindingContext = model;
        }
    }
}
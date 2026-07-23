using CommunityToolkit.Mvvm.ComponentModel;

namespace Pocket.Client.PageModels
{
    public partial class FeedPageModel : ObservableObject
    {
        [ObservableProperty]
        private bool isGalleryView;
    }
}

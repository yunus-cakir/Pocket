using CommunityToolkit.Mvvm.ComponentModel;
using System;
using Microsoft.Maui.Controls;

namespace Pocket.Client.PageModels
{
    [QueryProperty(nameof(FriendId), "FriendId")]
    [QueryProperty(nameof(MediaPostId), "MediaPostId")]
    public partial class DmPageModel : ObservableObject
    {
        [ObservableProperty]
        private string friendId = string.Empty;

        [ObservableProperty]
        private string? mediaPostId;
    }
}

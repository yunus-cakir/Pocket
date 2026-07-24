using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pocket.Shared.DTOs;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace Pocket.Client.PageModels
{
    public partial class DmListPageModel : ObservableObject
    {
        [ObservableProperty]
        private string searchQuery = string.Empty;

        [ObservableProperty]
        private bool isSearching;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSearchResult))]
        private UserIdentityDto? searchResult;

        public bool HasSearchResult => SearchResult != null;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSearchMessage))]
        private string searchMessage = string.Empty;

        public bool HasSearchMessage => !string.IsNullOrEmpty(SearchMessage);

        public ObservableCollection<Models.Friend> Friends { get; } = new();

        public DmListPageModel()
        {
            // Placeholder: Load friends from LocalDatabase here
        }

        [RelayCommand]
        private async Task SearchUserAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                SearchMessage = "Please enter a username.";
                SearchResult = null;
                return;
            }

            IsSearching = true;
            SearchMessage = "Searching...";
            SearchResult = null;

            try
            {
                // TODO: Call RelayService.LookupUser(SearchQuery) via SignalR
                // For now, simulating a network delay
                await Task.Delay(1000);

                // Mock response
                if (SearchQuery.ToLower() == "alice")
                {
                    SearchResult = new UserIdentityDto
                    {
                        UserId = "mock-id-alice",
                        Username = "alice",
                        PublicKey = "mock-public-key"
                    };
                    SearchMessage = string.Empty;
                }
                else
                {
                    SearchMessage = "User not found or is offline.";
                }
            }
            finally
            {
                IsSearching = false;
            }
        }

        [RelayCommand]
        private async Task SendFriendRequestAsync()
        {
            if (SearchResult == null) return;

            // TODO: Call RelayService.SendFriendRequest via SignalR
            // using CryptoService to sign/generate public key for the payload
            
            SearchMessage = $"Friend request sent to {SearchResult.Username}!";
            SearchResult = null;
            SearchQuery = string.Empty;

            await Task.Delay(2000);
            SearchMessage = string.Empty;
        }
    }
}

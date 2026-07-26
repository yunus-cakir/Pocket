using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pocket.Shared.DTOs;
using Pocket.Client.Services;
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

        private readonly IRelayService _relayService;
        private readonly AppShellModel _appShellModel;

        public DmListPageModel(IRelayService relayService, AppShellModel appShellModel)
        {
            _relayService = relayService;
            _appShellModel = appShellModel;
        }

        [RelayCommand]
        private async Task SearchUserAsync()
        {
            string cleanQuery = (SearchQuery ?? string.Empty).Trim().TrimStart('@');

            if (string.IsNullOrWhiteSpace(cleanQuery))
            {
                SearchMessage = "Lütfen bir kullanıcı adı girin.";
                SearchResult = null;
                return;
            }

            IsSearching = true;
            SearchMessage = "Aranıyor...";
            SearchResult = null;

            try
            {
                var result = await _relayService.LookupUserAsync(cleanQuery);
                
                if (result != null)
                {
                    SearchResult = result;
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

            var request = new FriendRequestDto
            {
                SenderId = _appShellModel.CurrentUser.Id,
                SenderUsername = _appShellModel.CurrentUser.Username,
                SenderPublicKey = _appShellModel.CurrentUser.PublicKey,
                RecipientId = SearchResult.UserId,
                Timestamp = System.DateTime.UtcNow
            };

            await _relayService.SendFriendRequestAsync(request);
            
            SearchMessage = $"Friend request sent to {SearchResult.Username}!";
            SearchResult = null;
            SearchQuery = string.Empty;

            await Task.Delay(2000);
            SearchMessage = string.Empty;
        }
    }
}

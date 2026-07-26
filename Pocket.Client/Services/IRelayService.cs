using Pocket.Shared.DTOs;

namespace Pocket.Client.Services
{
    public interface IRelayService
    {
        bool IsConnected { get; }
        Task ConnectAsync(string userId, string username);
        Task DisconnectAsync();
        Task SendPayloadAsync(EncryptedPayloadDto payload);
        Task ConfirmDeliveryAsync(string senderId, Guid messageId);
        Task<UserIdentityDto?> LookupUserAsync(string username);
        Task SendFriendRequestAsync(FriendRequestDto request);
        Task SendFriendAcceptAsync(FriendAcceptDto accept);
        
        // Events received from Hub
        event Action<EncryptedPayloadDto>? OnPayloadReceived;
        event Action<Guid>? OnDeliveryAcknowledged;
        event Action<FriendRequestDto>? OnFriendRequestReceived;
        event Action<FriendAcceptDto>? OnFriendAcceptReceived;
    }
}

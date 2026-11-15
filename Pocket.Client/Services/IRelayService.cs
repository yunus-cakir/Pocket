using Pocket.Shared.DTOs;

namespace Pocket.Client.Services
{
    public interface IRelayService
    {
        bool IsConnected { get; }
        Task ConnectAsync(string userId, string username);

        /// <summary>Supplies the identity key pair used to log in (sign the server's challenge).</summary>
        void SetIdentityKeyProvider(Func<Task<(string PublicKey, string PrivateKey)>> provider);
        Task DisconnectAsync();
        Task SendPayloadAsync(EncryptedPayloadDto payload);
        Task ConfirmDeliveryAsync(string senderId, Guid messageId);
        Task<UserIdentityDto?> LookupUserAsync(string username);
        Task RegisterUserAsync(UserIdentityDto identity);
        Task SendFriendRequestAsync(FriendRequestDto request);
        Task SendFriendAcceptAsync(FriendAcceptDto accept);
        
        string CurrentHubUrl { get; }
        bool IsOnFallback { get; }
        DateTime? ConnectedAt { get; }

        // Events received from Hub
        event Action<bool>? OnConnectionStateChanged;
        event Action<EncryptedPayloadDto>? OnPayloadReceived;
        event Action<Guid>? OnDeliveryAcknowledged;
        event Action<FriendRequestDto>? OnFriendRequestReceived;
        event Action<FriendAcceptDto>? OnFriendAcceptReceived;
    }
}

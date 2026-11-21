using Pocket.Client.Models;
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
        /// <summary>
        /// Sends a payload to every connected server and waits until at least one has stored it. False when not connected, the
        /// call failed or the server did not answer in time — the caller must treat it as not sent.
        /// </summary>
        Task<bool> SendPayloadAsync(EncryptedPayloadDto payload);

        /// <summary>
        /// The running relay processes we are connected to ("node|startedAt", several joined with ';' when more
        /// than one server is up). Payloads accepted by a server live only in its memory: when none of the
        /// processes that accepted one is connected any more it may be gone and must be resent
        /// (see RelayService.IsHeldBy). Null when unknown.
        /// </summary>
        string? CurrentServerInstance { get; }
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

        /// <summary>Delivery receipt that names the recipient (newer servers).</summary>
        event Action<Guid, string>? OnDeliveryAcknowledgedFrom;
        event Action<FriendRequestDto>? OnFriendRequestReceived;
        event Action<FriendAcceptDto>? OnFriendAcceptReceived;

        /// <summary>Connection history events (drops, failovers, returns to the primary).</summary>
        event Action<ConnectionEvent>? OnConnectionEvent;
    }
}

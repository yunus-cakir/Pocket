using Pocket.Shared.DTOs;

namespace Pocket.Shared.Interfaces
{
    public interface IRelayClient
    {
        Task ReceivePayload(EncryptedPayloadDto payload);
        Task AcknowledgeDelivery(Guid messageId);
        Task ReceiveFriendRequest(FriendRequestDto request);
        Task ReceiveFriendAccept(FriendAcceptDto accept);
    }
}

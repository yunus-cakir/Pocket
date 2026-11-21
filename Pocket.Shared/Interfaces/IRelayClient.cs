using Pocket.Shared.DTOs;

namespace Pocket.Shared.Interfaces
{
    public interface IRelayClient
    {
        Task ReceivePayload(EncryptedPayloadDto payload);
        Task AcknowledgeDelivery(Guid messageId);

        /// <summary>
        /// Like <see cref="AcknowledgeDelivery"/>, but names the recipient that confirmed — a photo sent to
        /// several friends shares one message id. Sent alongside the legacy call (old clients ignore it).
        /// </summary>
        Task AcknowledgeDeliveryFrom(Guid messageId, string recipientId);
        Task ReceiveFriendRequest(FriendRequestDto request);
        Task ReceiveFriendAccept(FriendAcceptDto accept);
    }
}

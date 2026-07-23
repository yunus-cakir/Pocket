using Pocket.Shared.DTOs;

namespace Pocket.Shared.Interfaces
{
    public interface IRelayClient
    {
        Task ReceivePayload(EncryptedPayloadDto payload);
        Task AcknowledgeDelivery(Guid messageId);
    }
}

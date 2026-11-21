using SQLite;
using System;

namespace Pocket.Client.Models
{
    public enum DeliveryState
    {
        /// <summary>Not yet stored by a relay server (no internet, no server, or not confirmed).</summary>
        Pending = 0,
        /// <summary>Stored by the relay (single tick); the recipient's device has not received it yet.</summary>
        OnServer = 1,
        /// <summary>The recipient's device received and saved it (double tick).</summary>
        Delivered = 2,
    }

    /// <summary>
    /// Delivery state of one outgoing photo for one recipient. A photo sent to several friends shares
    /// its message id, so each recipient is tracked separately and only the missing ones are resent.
    /// </summary>
    public class PostDelivery
    {
        /// <summary>"{PostId}:{RecipientId}".</summary>
        [PrimaryKey]
        public string Id { get; set; } = string.Empty;

        [Indexed]
        public Guid PostId { get; set; }

        public string RecipientId { get; set; } = string.Empty;

        [Indexed]
        public DeliveryState State { get; set; } = DeliveryState.Pending;

        /// <summary>
        /// Relay process that accepted it (see IRelayService.CurrentServerInstance). Relays keep payloads in
        /// memory only: if we later connect to a different instance, an OnServer copy may be gone and is resent.
        /// </summary>
        public string? ServerInstance { get; set; }

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public static string MakeId(Guid postId, string recipientId) => $"{postId}:{recipientId}";
    }
}

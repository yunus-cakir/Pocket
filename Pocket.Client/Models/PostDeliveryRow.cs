namespace Pocket.Client.Models
{
    /// <summary>One recipient line in an outgoing post's options menu (display only, not stored).</summary>
    public class PostDeliveryRow
    {
        public string Name { get; init; } = string.Empty;
        /// <summary>Local path of the recipient's profile photo; empty shows the placeholder.</summary>
        public string AvatarUrl { get; init; } = string.Empty;
        public string StatusText { get; init; } = string.Empty;
        /// <summary>Spinner (waiting for internet / server).</summary>
        public bool IsWaiting { get; init; }
        public bool IsSingleTick { get; init; }
        public bool IsDoubleTick { get; init; }

        public static PostDeliveryRow Create(string name, string? avatarUrl, DeliveryState state, bool hasInternet) => state switch
        {
            DeliveryState.Delivered => new PostDeliveryRow
            {
                Name = name, AvatarUrl = avatarUrl ?? string.Empty,
                StatusText = "Sent", IsDoubleTick = true
            },
            DeliveryState.OnServer => new PostDeliveryRow
            {
                Name = name, AvatarUrl = avatarUrl ?? string.Empty,
                StatusText = "Waiting to Receive", IsSingleTick = true
            },
            _ => new PostDeliveryRow
            {
                Name = name, AvatarUrl = avatarUrl ?? string.Empty,
                StatusText = hasInternet ? "Waiting for Server" : "Waiting for Internet",
                IsWaiting = true
            }
        };
    }
}

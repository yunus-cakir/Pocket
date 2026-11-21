namespace Pocket.Client.Models
{
    /// <summary>One recipient line in an outgoing post's options menu (display only, not stored).</summary>
    public class PostDeliveryRow
    {
        public string Name { get; init; } = string.Empty;
        public string StatusText { get; init; } = string.Empty;
        public string StatusColor { get; init; } = "#8E8E93";
        public bool IsWaiting { get; init; }
        public bool IsSingleTick { get; init; }
        public bool IsDoubleTick { get; init; }

        public const string AccentTickColor = "#34C759";

        public static PostDeliveryRow Create(string name, DeliveryState state, bool hasInternet) => state switch
        {
            DeliveryState.Delivered => new PostDeliveryRow
            {
                Name = name, StatusText = "Sent", StatusColor = AccentTickColor, IsDoubleTick = true
            },
            DeliveryState.OnServer => new PostDeliveryRow
            {
                Name = name, StatusText = "Waiting to Receive", IsSingleTick = true
            },
            _ => new PostDeliveryRow
            {
                Name = name,
                StatusText = hasInternet ? "Waiting for Server" : "Waiting for Internet",
                StatusColor = "#F59E0B",
                IsWaiting = true
            }
        };
    }
}

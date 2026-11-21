namespace Pocket.Shared.Enums
{
    public enum MediaType
    {
        None = 0,
        Photo = 1,
        Video = 2,
        ProfileUpdate = 3,
        PostDeleted = 4,
        MessageDeleted = 5,
        /// <summary>Reaction on a direct message: encrypted "&lt;messageId&gt;|&lt;emoji&gt;" (empty emoji = removed).</summary>
        MessageReaction = 6
    }
}

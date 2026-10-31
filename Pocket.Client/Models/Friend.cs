using SQLite;
using System;

namespace Pocket.Client.Models
{
    public class Friend
    {
        [PrimaryKey]
        public string Id { get; set; } = string.Empty;

        [MaxLength(50), Indexed]
        public string Username { get; set; } = string.Empty;

        [MaxLength(100)]
        public string DisplayName { get; set; } = string.Empty;

        public string ProfilePictureUrl { get; set; } = string.Empty;

        public string PublicKey { get; set; } = string.Empty;

        public bool IsMuted { get; set; }

        public DateTime AddedAt { get; set; } = DateTime.UtcNow;

        public DateTime LastInteractionAt { get; set; } = DateTime.UtcNow;

        [Ignore]
        public System.Windows.Input.ICommand OpenChatCommand => new Microsoft.Maui.Controls.Command(async () =>
        {
            if (Microsoft.Maui.Controls.Shell.Current != null && !string.IsNullOrEmpty(Id))
            {
                await Microsoft.Maui.Controls.Shell.Current.GoToAsync($"{nameof(Pocket.Client.Pages.DmPage)}?FriendId={Uri.EscapeDataString(Id)}");
            }
        });
    }
}

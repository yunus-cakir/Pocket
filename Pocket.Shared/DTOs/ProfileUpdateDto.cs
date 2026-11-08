namespace Pocket.Shared.DTOs
{
    public class ProfileUpdateDto
    {
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool AvatarCleared { get; set; }
    }
}


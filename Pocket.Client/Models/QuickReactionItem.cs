using CommunityToolkit.Mvvm.ComponentModel;

namespace Pocket.Client.Models
{
    /// <summary>One emoji in the message long-press reaction bar.</summary>
    public partial class QuickReactionItem : ObservableObject
    {
        public QuickReactionItem(string emoji)
        {
            Emoji = emoji;
        }

        public string Emoji { get; }

        /// <summary>The reaction already given to the message (shown on a grey circle; tapping it removes it).</summary>
        [ObservableProperty] private bool _isSelected;
    }
}

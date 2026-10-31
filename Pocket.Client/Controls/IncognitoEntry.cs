using Microsoft.Maui.Controls;

namespace Pocket.Client.Controls
{
    /// <summary>
    /// Custom Entry control for Direct Messaging that enforces OS-level Incognito Keyboard mode
    /// (disabling personalized learning, predictive text, and autocorrect per BR-301).
    /// </summary>
    public class IncognitoEntry : Entry
    {
        public IncognitoEntry()
        {
            IsTextPredictionEnabled = false;
            IsSpellCheckEnabled = false;
        }
    }
}


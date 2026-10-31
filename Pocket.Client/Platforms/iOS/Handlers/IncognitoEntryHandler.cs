using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;

namespace Pocket.Client.Platforms.iOS.Handlers
{
    /// <summary>
    /// Enforces Incognito Keyboard mode on iOS (BR-301) by disabling autocorrection,
    /// spell checking, and predictive text on the native UITextField.
    /// </summary>
    public class IncognitoEntryHandler : EntryHandler
    {
        protected override void ConnectHandler(MauiTextField platformView)
        {
            base.ConnectHandler(platformView);

            platformView.AutocorrectionType = UITextAutocorrectionType.No;
            platformView.SpellCheckingType = UITextSpellCheckingType.No;
            platformView.SmartQuotesType = UITextSmartQuotesType.No;
            platformView.SmartDashesType = UITextSmartDashesType.No;
            platformView.SmartInsertDeleteType = UITextSmartInsertDeleteType.No;
        }
    }
}


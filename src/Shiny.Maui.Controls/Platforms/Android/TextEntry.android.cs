using Google.Android.Material.TextField;

namespace Shiny.Maui.Controls;

public partial class TextEntry
{
    partial void PlatformConfiguration()
    {
        if (entry.Handler?.PlatformView is not TextInputLayout textInputLayout)
        {
            return;
        }

        // Remove material 3 styling as it is not compatible with shiny.
        textInputLayout.BoxBackgroundMode = TextInputLayout.BoxBackgroundNone;
        textInputLayout.EditText?.SetPadding(0, 0, 0, 0);
        textInputLayout.EditText?.Background = null;

        //If platform is TextInputLayout material 3 is enabled and a floating entry is expected.
        Variant = TextEntryVariant.Floating;
    }
}

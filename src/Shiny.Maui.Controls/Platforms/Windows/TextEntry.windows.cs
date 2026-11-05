using Microsoft.UI.Xaml.Controls;

namespace Shiny.Maui.Controls;

public partial class TextEntry
{
    partial void PlatformConfiguration()
    {
        if (entry.Handler?.PlatformView is not TextBox textBox)
        {
            return;
        }

        // Remove default WinUI default style as it will add a border line to the text box (https://github.com/microsoft/microsoft-ui-xaml/blob/winui3/release/1.5.1/controls/dev/CommonStyles/TextBox_themeresources.xaml).
        // Shiny is responsible for the style anyway.
        textBox.Style = new Microsoft.UI.Xaml.Style(typeof(TextBox));
    }
}

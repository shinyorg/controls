using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>Switches a view's fill between theme roles and transparent, repeatedly.</summary>
/// <remarks>
/// A day circle flips between "Primary" and "nothing" on every tap. Doing that with
/// <c>SetDynamicResource(BackgroundColorProperty, …)</c> over a literal <c>Transparent</c> left the
/// circle unpainted on iOS - white text on a white page, so the tapped day simply vanished. Painting
/// the <see cref="VisualElement.Background"/> brush from the theme's brush token, clearing the old
/// value first, is what renders.
/// </remarks>
static class RangePickerPaint
{
    public static void Fill(VisualElement view, string? colorKey)
    {
        view.RemoveDynamicResource(VisualElement.BackgroundProperty);
        view.ClearValue(VisualElement.BackgroundProperty);

        if (colorKey is null)
            view.Background = new SolidColorBrush(Colors.Transparent);
        else
            view.SetDynamicResource(VisualElement.BackgroundProperty, colorKey.AsBrush());
    }
}

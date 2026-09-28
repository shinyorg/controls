using Shiny.Controls.Office.Icons;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// Draws one of the shell's icons (<see cref="OfficeShellIcon"/>) from the shared geometry — the same
/// artwork the Blazor shell writes out as SVG.
/// </summary>
/// <remarks>
/// <see cref="Color"/> follows the theme's <c>OnSurface</c> until something sets it; a local value
/// outranks the dynamic resource, which is how the title bar paints its icons in the accent's ink.
/// </remarks>
public class OfficeShellIconView : GraphicsView
{
    readonly OfficeToolbarIconDrawable drawable = new();

    public static readonly BindableProperty IconProperty = BindableProperty.Create(
        nameof(Icon), typeof(OfficeShellIcon), typeof(OfficeShellIconView), OfficeShellIcon.Document,
        propertyChanged: (b, _, _) => ((OfficeShellIconView)b).Refresh());

    public static readonly BindableProperty ColorProperty = BindableProperty.Create(
        nameof(Color), typeof(Color), typeof(OfficeShellIconView), Colors.Gray,
        propertyChanged: (b, _, _) => ((OfficeShellIconView)b).Refresh());


    public OfficeShellIconView()
    {
        this.Drawable = this.drawable;
        this.WidthRequest = 16;
        this.HeightRequest = 16;
        this.InputTransparent = true;
        this.BackgroundColor = Colors.Transparent;
        this.SetDynamicResource(ColorProperty, ShinyThemeKeys.Color.OnSurface);
        this.Refresh();
    }


    public OfficeShellIcon Icon
    {
        get => (OfficeShellIcon)this.GetValue(IconProperty);
        set => this.SetValue(IconProperty, value);
    }

    public Color Color
    {
        get => (Color)this.GetValue(ColorProperty);
        set => this.SetValue(ColorProperty, value);
    }


    void Refresh()
    {
        if (this.drawable is null)
            return;

        this.drawable.Shapes = OfficeIcons.Shapes(this.Icon);
        this.drawable.Color = this.Color ?? Colors.Gray;
        this.Invalidate();
    }
}

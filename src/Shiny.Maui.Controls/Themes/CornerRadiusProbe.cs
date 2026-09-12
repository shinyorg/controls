using Microsoft.Maui.Controls.Shapes;

namespace Shiny.Maui.Controls.Themes;

/// <summary>
/// Resolves a <c>ShinyThemeKeys.Shape.…Radius</c> token to a <see cref="double"/>, for a control that
/// needs the theme's radius on <em>some</em> of its corners.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ThemeTokens.WithCornerRadius"/> covers the ordinary case, where all four corners take
/// the token. It cannot help a tab that is rounded on top and square where it meets the strip, or a
/// segment that is rounded only on the end of the row: zeroing two corners means doing arithmetic on
/// the value, and a <c>DynamicResource</c> is not a value you can read until it has resolved.
/// </para>
/// <para>
/// So a hidden, zero-sized <see cref="Border"/> takes the token instead and the control reads the
/// number off it. It has to be a real child - that is what puts it in the resource chain, and it is
/// also what makes a live theme swap re-run the geometry rather than freezing it at startup.
/// </para>
/// </remarks>
sealed class CornerRadiusProbe
{
    readonly RoundRectangle shape;

    /// <param name="radiusToken">A <c>ShinyThemeKeys.Shape.…Radius</c> key.</param>
    /// <param name="changed">Raised whenever the resolved radius moves, including on a theme swap.</param>
    public CornerRadiusProbe(string radiusToken, Action changed)
    {
        this.shape = new RoundRectangle();
        this.shape.SetDynamicResource(RoundRectangle.CornerRadiusProperty, radiusToken);

        this.View = new Border
        {
            IsVisible = false,
            WidthRequest = 0,
            HeightRequest = 0,
            InputTransparent = true,
            StrokeThickness = 0,
            StrokeShape = this.shape
        };

        this.shape.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RoundRectangle.CornerRadius))
                changed();
        };
    }


    /// <summary>Add this to a layout inside the control, so the probe sits in the resource chain.</summary>
    public Border View { get; }

    /// <summary>The token's current value. Zero until the resource has resolved.</summary>
    public double Radius => this.shape.CornerRadius.TopLeft;
}

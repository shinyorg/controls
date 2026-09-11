namespace Shiny.Maui.Controls.Infrastructure;

/// <summary>
/// Lets a <c>Brush</c>-typed property follow a <c>Color</c> theme token.
/// </summary>
/// <remarks>
/// The theme dictionaries hold <see cref="Color"/> values, but <c>Border.Stroke</c>,
/// <c>Border.Background</c> and <c>Shadow.Brush</c> take a <c>Brush</c>. Two things that look like they
/// should bridge that gap silently do not:
/// <list type="number">
/// <item><c>border.SetDynamicResource(Border.StrokeProperty, token)</c> hands a Color to a Brush
/// property. The types never match, so the resource is dropped and the stroke stays null — which reads
/// as "the outline just is not drawn". It looks correct in source because <c>Stroke = someColor</c>
/// compiles via Brush's implicit conversion, making the two forms appear equivalent.</item>
/// <item><c>new SolidColorBrush().SetDynamicResource(SolidColorBrush.ColorProperty, token)</c> puts the
/// lookup on an object that is not in the visual tree, so it has no resource chain to search and keeps
/// its seed colour forever.</item>
/// </list>
/// What does work is resolving the token on something that <em>is</em> parented — a zero-sized hidden
/// <see cref="BoxView"/> — and binding the brush to it. Add the probe to a layout in the control, and
/// the brush tracks live theme swaps like everything else.
/// </remarks>
static class ThemeProbe
{
    /// <summary>
    /// A hidden probe plus a brush bound to its colour. Add the probe to a layout inside the control,
    /// then drive it with <see cref="Tint"/>.
    /// </summary>
    public static (SolidColorBrush Brush, BoxView Probe) Create()
    {
        var probe = new BoxView
        {
            // Exists only to resolve a resource: it must never be measured or paint anything.
            IsVisible = false,
            WidthRequest = 0,
            HeightRequest = 0,
            InputTransparent = true
        };

        // Seeded non-null: a brush handed to a handler with a null Color throws inside MAUI's Windows
        // stroke mapper, and a token stays unresolved until the theme dictionary merges.
        var brush = new SolidColorBrush(Colors.Transparent);
        brush.SetBinding(SolidColorBrush.ColorProperty, static (BoxView b) => b.Color, source: probe);

        return (brush, probe);
    }


    /// <summary>
    /// Points a property at an explicit colour, or back at a theme token when the consumer has not set
    /// one. This is the "leave it unset and it follows the theme" behaviour every Shiny control has.
    /// </summary>
    /// <remarks>
    /// <see cref="BindableObject.SetValue(BindableProperty, object)"/> writes a <em>local</em> value,
    /// and a local value outranks a dynamic resource. So going explicit-colour → token has to clear
    /// the local value first: without the <see cref="BindableObject.ClearValue(BindableProperty)"/>
    /// the <c>SetDynamicResource</c> below is accepted, resolves, and is then quietly outranked by the
    /// colour still sitting in the local slot. Nothing errors — the control simply keeps the old
    /// colour, which is how a walkthrough callout that followed a spotlight step (transparent fill)
    /// stayed transparent and rendered its card-less text on the dim.
    /// </remarks>
    public static void Tint(Element target, BindableProperty property, Color? value, string token)
    {
        if (value is null)
        {
            target.ClearValue(property);
            target.SetDynamicResource(property, token);
        }
        else
        {
            target.RemoveDynamicResource(property);
            target.SetValue(property, value);
        }
    }
}

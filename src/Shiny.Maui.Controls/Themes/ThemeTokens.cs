using Microsoft.Maui.Controls.Shapes;

namespace Shiny.Maui.Controls.Themes;

/// <summary>
/// Helpers for the "theme unless the consumer said otherwise" pattern.
/// </summary>
/// <remarks>
/// A control's internal children are painted from the theme with <c>SetDynamicResource</c>, which is
/// what lets a theme swap restyle them live. Writing a literal to the same property clears that
/// binding, so an explicit value from the consumer keeps winning — but a <em>default</em> literal
/// would silently win too, which is why the numeric appearance properties use a negative sentinel
/// for "unset" rather than baking their old default into the property.
/// </remarks>
static class ThemeTokens
{
    /// <summary>The value a numeric appearance property carries when the consumer has not set one.</summary>
    public const double Unset = -1d;

    public static bool IsSet(double value) => value >= 0d && !double.IsNaN(value);

    /// <summary>
    /// The <c>Brush</c> twin of a <c>Color</c> token key — <c>Shiny.Color.Outline</c> becomes
    /// <c>Shiny.Brush.Outline</c>.
    /// </summary>
    /// <remarks>
    /// Both families are emitted from one list of roles, so there is a brush for every colour and the
    /// two differ only in the group segment. It is a rewrite rather than a second lookup table because
    /// a table would be a second thing to keep in step with the generator.
    /// </remarks>
    public static string AsBrush(this string colorKey) => colorKey.Replace(".Color.", ".Brush.");

    /// <summary>Apply an explicit value, or fall back to the theme token when unset.</summary>
    public static void SetTokenOrValue(this Element element, BindableProperty property, double value, string themeKey)
    {
        if (IsSet(value))
            element.SetValue(property, value);
        else
            element.SetDynamicResource(property, themeKey);
    }

    /// <summary>
    /// Corner radius twin of <see cref="SetTokenOrValue"/>. <see cref="RoundRectangle.CornerRadius"/>
    /// is typed <see cref="CornerRadius"/> and a dynamic resource is assigned with no conversion, so
    /// callers pass a <c>ShinyThemeKeys.Shape.…Radius</c> key, not the plain double one.
    /// </summary>
    public static void SetCornerTokenOrValue(this RoundRectangle shape, double value, string radiusThemeKey)
    {
        if (IsSet(value))
            shape.CornerRadius = new CornerRadius(value);
        else
            shape.SetDynamicResource(RoundRectangle.CornerRadiusProperty, radiusThemeKey);
    }

    // ---------------------------------------------------------------------------------------------
    // Chainable forms, for the common case of an internal child built in an object initializer.
    // An object initializer cannot call SetDynamicResource, so setting a size inside one bakes it in
    // permanently; chaining a WithX(token) call after the initializer keeps it on the theme.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Bind a font size to a <c>ShinyThemeKeys.Type.…Size</c> token.</summary>
    public static T WithFontSize<T>(this T element, string themeKey) where T : Element
    {
        // Every MAUI type that has a font size shares FontElement's single BindableProperty instance,
        // so Label's is the right one to hand to any of them.
        element.SetDynamicResource(Label.FontSizeProperty, themeKey);
        return element;
    }

    /// <summary>Bind a corner radius to a <c>ShinyThemeKeys.Shape.…Radius</c> token.</summary>
    public static RoundRectangle WithCornerRadius(this RoundRectangle shape, string radiusThemeKey)
    {
        shape.SetDynamicResource(RoundRectangle.CornerRadiusProperty, radiusThemeKey);
        return shape;
    }

    /// <summary>Bind a stroke width to a <c>ShinyThemeKeys.Border.…</c> token.</summary>
    public static Border WithStrokeThickness(this Border border, string themeKey)
    {
        border.SetDynamicResource(Border.StrokeThicknessProperty, themeKey);
        return border;
    }

    /// <summary>Bind a drop shadow to a <c>ShinyThemeKeys.Elevation.Level…</c> token.</summary>
    public static T WithElevation<T>(this T element, string themeKey) where T : VisualElement
    {
        // ClearValue first, and not defensively: WithoutElevation leaves an explicit null behind, and
        // an explicitly-set local value silently wins over a dynamic resource - so a shadow that had
        // been turned off could never be turned back on. Clearing a property that was never set is a
        // no-op, which is every other call site.
        element.ClearValue(VisualElement.ShadowProperty);
        element.SetDynamicResource(VisualElement.ShadowProperty, themeKey);
        return element;
    }

    /// <summary>Takes a token-driven drop shadow back off.</summary>
    /// <remarks>
    /// <para>Neither half of this is optional, and the obvious one does not work at all.
    /// <c>ClearValue</c> does <b>not</b> clear a property whose value came from a dynamic resource -
    /// the resolved <c>Shadow</c> is still there afterwards, and calling it here would put the shadow
    /// back. <c>RemoveDynamicResource</c> only stops a later theme swap re-applying it. The explicit
    /// null is the only thing that actually takes the shadow off.</para>
    /// <para>This is why <c>HasShadow="False"</c> read as a switch that did nothing. The null it
    /// leaves is a local value that would then outrank the next dynamic resource, which is what
    /// <see cref="WithElevation"/> clears before re-binding.</para>
    /// </remarks>
    public static T WithoutElevation<T>(this T element) where T : VisualElement
    {
        element.RemoveDynamicResource(VisualElement.ShadowProperty);
        element.Shadow = null;
        return element;
    }

    /// <summary>Drive a brush's colour from a token so theme swaps reach Stroke/Background properties.</summary>
    public static SolidColorBrush TokenBrush(string colorThemeKey)
    {
        var brush = new SolidColorBrush();
        brush.SetDynamicResource(SolidColorBrush.ColorProperty, colorThemeKey);
        return brush;
    }
}

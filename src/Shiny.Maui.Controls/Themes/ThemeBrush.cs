namespace Shiny.Maui.Controls.Themes;

/// <summary>
/// Points a <c>Brush</c>-typed property at the theme, or at an explicit colour when the consumer set one.
/// </summary>
/// <remarks>
/// <para>
/// Half the surfaces a theme has to reach are <c>Brush</c>-typed — <c>Border.Stroke</c>,
/// <c>Border.Background</c>, <c>Shadow.Brush</c> — and the theme's colour tokens are <c>Color</c>s.
/// Handing a Color token to a Brush property compiles (Brush has an implicit conversion from Color)
/// and then silently does nothing: the types never match at resolve time, the resource is dropped, and
/// the stroke stays null. That mismatch is why this library used to carry three different versions of
/// a workaround, and a hidden zero-sized <c>BoxView</c> inside every outlined control whose only job
/// was to resolve a resource on their behalf.
/// </para>
/// <para>
/// The theme dictionaries now ship a <c>Brush</c> twin of every colour — <c>Shiny.Brush.Outline</c>
/// beside <c>Shiny.Color.Outline</c> — so there is nothing left to work around. The brush does not
/// need to track its colour, either: a theme swap replaces the whole dictionary, and the brushes go
/// with it.
/// </para>
/// </remarks>
static class ThemeBrush
{
    /// <summary>
    /// An explicit colour wins; unset follows <paramref name="brushToken"/>.
    /// </summary>
    /// <remarks>
    /// The <c>ClearValue</c> is not defensive. A locally-set value outranks a dynamic resource, so
    /// going explicit-colour → back-to-theme has to clear the local slot first or the resource
    /// resolves and is then quietly outranked — the control simply keeps the old colour, which is the
    /// same trap <see cref="Infrastructure.ThemeProbe.Tint"/> documents for Color properties.
    /// </remarks>
    public static void Apply(Element target, BindableProperty property, Color? value, string brushToken)
    {
        if (value is null)
        {
            target.ClearValue(property);
            target.SetDynamicResource(property, brushToken);
        }
        else
        {
            target.RemoveDynamicResource(property);
            target.SetValue(property, new SolidColorBrush(value));
        }
    }


    /// <summary>Follows a token unconditionally — for a surface the consumer cannot colour itself.</summary>
    public static void Apply(Element target, BindableProperty property, string brushToken)
        => Apply(target, property, null, brushToken);
}

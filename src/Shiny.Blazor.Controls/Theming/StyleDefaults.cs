namespace Shiny.Blazor.Controls.Theming;

/// <summary>
/// Helpers for emitting a styling parameter as an inline declaration <em>only</em> when the caller
/// actually chose a value.
/// </summary>
/// <remarks>
/// <para>
/// A component that builds <c>style="background: {BarColor}"</c> from a parameter whose default is a
/// token wins against every stylesheet a consumer can write, because an inline declaration outranks
/// any selector. The default is therefore not a default at all - it is a permanent decision, and
/// <c>.shiny-pb-fill { background: … }</c> in an app's own CSS silently does nothing.
/// </para>
/// <para>
/// The fix is for the component's <c>.razor.css</c> to carry the default and for the inline
/// declaration to appear only when the parameter differs from it. A caller who passes the default
/// string explicitly gets the same pixels either way, so nothing observable changes for them; what
/// changes is that a stylesheet now has something to beat.
/// </para>
/// </remarks>
public static class StyleDefaults
{
    /// <summary>
    /// <c>"property: value;"</c> when <paramref name="value"/> differs from <paramref name="fallback"/>,
    /// otherwise nothing - leaving the declaration to the component's stylesheet.
    /// </summary>
    /// <param name="property">The CSS property or custom property name.</param>
    /// <param name="value">The parameter's current value.</param>
    /// <param name="fallback">The parameter's declared default, which the stylesheet also carries.</param>
    public static string Override(string property, string? value, string fallback)
        => String.IsNullOrEmpty(value) || String.Equals(value, fallback, StringComparison.Ordinal)
            ? String.Empty
            : $"{property}: {value};";


    /// <summary>
    /// The same, for a parameter whose default is a number rather than a string.
    /// </summary>
    public static string Override(string property, double value, double fallback, string unit = "px")
        => value.Equals(fallback) ? String.Empty : $"{property}: {value.ToString(System.Globalization.CultureInfo.InvariantCulture)}{unit};";
}

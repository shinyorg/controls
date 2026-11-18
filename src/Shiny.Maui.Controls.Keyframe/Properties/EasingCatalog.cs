using System.ComponentModel;
using System.Globalization;

namespace Shiny.Maui.Controls.Keyframe;

/// <summary>
/// Resolves easing curves by name, so XAML can say <c>Easing="CubicOut"</c>.
/// </summary>
/// <remarks>
/// A MAUI-flavoured front for <see cref="EasingSyntax"/>, which the Blazor host shares, so a curve
/// written as text is the same curve on both. Also accepts CSS-style function syntax — <c>cubic-bezier(0.25, 0.1, 0.25, 1)</c>,
/// <c>steps(4)</c>, <c>spring(0.5, 10)</c> — because copying a curve straight out of a design tool
/// or a browser devtools panel is by far the most common way anyone arrives at one.
/// </remarks>
public static class EasingCatalog
{
    /// <summary>Adds or replaces a named curve.</summary>
    public static void Register(string name, EasingFunction easing) => EasingSyntax.Register(name, easing);

    /// <summary>Every registered curve name.</summary>
    public static IEnumerable<string> Names => EasingSyntax.Names;

    /// <summary>Resolves a curve, returning null when the text is not recognised.</summary>
    public static EasingFunction? Find(string? text) => EasingSyntax.Find(text);

    /// <summary>Resolves a curve, throwing a message that lists the alternatives.</summary>
    public static EasingFunction Get(string? text) => EasingSyntax.Get(text);
}

/// <summary>Lets XAML assign an easing curve from a plain string.</summary>
public sealed class EasingFunctionTypeConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string);

    /// <inheritdoc />
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        => value is string text ? EasingCatalog.Get(text) : base.ConvertFrom(context, culture, value);
}

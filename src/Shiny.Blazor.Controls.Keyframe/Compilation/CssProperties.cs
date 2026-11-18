using System.Globalization;
using System.Text;

namespace Shiny.Blazor.Controls.Keyframe.Compilation;

/// <summary>How a property's values are read, blended and written.</summary>
internal enum PropertyKind
{
    /// <summary>A plain number (opacity).</summary>
    Number,
    /// <summary>One axis of the CSS <c>scale</c> property; <see cref="Axis.Both"/> is MAUI's uniform Scale.</summary>
    Scale,
    /// <summary>One axis of the CSS <c>translate</c> property, in px unless a unit is given.</summary>
    Translate,
    /// <summary>The CSS <c>rotate</c> property, taking the shortest arc between keys.</summary>
    Rotation,
    /// <summary>The CSS <c>rotate</c> property, turning as far as the keys say.</summary>
    Spin,
    /// <summary>A 3D turn about X or Y, written as <c>transform</c>.</summary>
    Rotation3D,
    /// <summary>One axis of <c>transform-origin</c>, as a 0..1 fraction of the element's box.</summary>
    Anchor,
    /// <summary>A colour, blended in Oklab like the MAUI package.</summary>
    Color,
    /// <summary>A length, in px unless a unit is given.</summary>
    Length,
    /// <summary>A MAUI Thickness (<c>10</c>, <c>10,20</c> or <c>l,t,r,b</c>) written as CSS edges.</summary>
    Thickness,
    /// <summary>Any other name: a CSS property, passed through untouched.</summary>
    Raw
}

/// <summary>Which axis of a two-axis CSS property a MAUI property drives.</summary>
internal enum Axis
{
    None,
    X,
    Y,
    Both
}

/// <summary>Where a MAUI property name lands in CSS.</summary>
internal sealed record CssProperty(string Name, string Css, PropertyKind Kind, Axis Axis)
{
    /// <summary>Two-axis CSS properties fed by more than one MAUI property, so they are merged per offset.</summary>
    public bool IsMerged => this.Kind is PropertyKind.Scale or PropertyKind.Translate or PropertyKind.Rotation3D or PropertyKind.Anchor;
}

/// <summary>
/// The MAUI property names the Keyframe package animates, mapped to CSS. Any other name is a raw CSS
/// property, so <c>Property="filter"</c> or <c>Property="border-radius"</c> work as written.
/// </summary>
internal static class CssProperties
{
    static readonly Dictionary<string, CssProperty> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Opacity"] = new("Opacity", "opacity", PropertyKind.Number, Axis.None),
        ["Scale"] = new("Scale", "scale", PropertyKind.Scale, Axis.Both),
        ["ScaleX"] = new("ScaleX", "scale", PropertyKind.Scale, Axis.X),
        ["ScaleY"] = new("ScaleY", "scale", PropertyKind.Scale, Axis.Y),
        ["TranslationX"] = new("TranslationX", "translate", PropertyKind.Translate, Axis.X),
        ["TranslationY"] = new("TranslationY", "translate", PropertyKind.Translate, Axis.Y),
        ["Rotation"] = new("Rotation", "rotate", PropertyKind.Rotation, Axis.None),
        ["Spin"] = new("Spin", "rotate", PropertyKind.Spin, Axis.None),
        ["RotationX"] = new("RotationX", "transform", PropertyKind.Rotation3D, Axis.X),
        ["RotationY"] = new("RotationY", "transform", PropertyKind.Rotation3D, Axis.Y),
        ["AnchorX"] = new("AnchorX", "transform-origin", PropertyKind.Anchor, Axis.X),
        ["AnchorY"] = new("AnchorY", "transform-origin", PropertyKind.Anchor, Axis.Y),
        ["BackgroundColor"] = new("BackgroundColor", "background-color", PropertyKind.Color, Axis.None),
        ["Background"] = new("Background", "background-color", PropertyKind.Color, Axis.None),
        ["TextColor"] = new("TextColor", "color", PropertyKind.Color, Axis.None),
        ["WidthRequest"] = new("WidthRequest", "width", PropertyKind.Length, Axis.None),
        ["HeightRequest"] = new("HeightRequest", "height", PropertyKind.Length, Axis.None),
        ["Margin"] = new("Margin", "margin", PropertyKind.Thickness, Axis.None),
        ["Padding"] = new("Padding", "padding", PropertyKind.Thickness, Axis.None)
    };

    /// <summary>Every MAUI name with a mapping.</summary>
    public static IEnumerable<string> Names => Known.Keys;

    /// <summary>Resolves a MAUI name, or treats the text as a raw CSS property.</summary>
    public static CssProperty Resolve(string name)
    {
        var trimmed = name.Trim();
        if (Known.TryGetValue(trimmed, out var known))
            return known;

        return new CssProperty(trimmed, ToKebab(trimmed), RawKindFor(trimmed), Axis.None);
    }

    /// <summary>
    /// The CSS properties an implicit transition on this name has to list. Transform parts list the
    /// individual property and <c>transform</c>, because either may be what changes.
    /// </summary>
    public static IReadOnlyList<string> TransitionTargets(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Equals("All", StringComparison.OrdinalIgnoreCase))
            return ["all"];

        var property = Resolve(trimmed);
        return property.Kind switch
        {
            PropertyKind.Scale or PropertyKind.Translate or PropertyKind.Rotation or PropertyKind.Spin => [property.Css, "transform"],
            _ => [property.Css]
        };
    }

    // A raw name with "color" in it still blends in Oklab; everything else passes through.
    static PropertyKind RawKindFor(string name) => name.Contains("color", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("--", StringComparison.Ordinal)
        ? PropertyKind.Color
        : PropertyKind.Raw;

    /// <summary><c>BorderRadius</c> → <c>border-radius</c>; kebab-case and custom properties are kept.</summary>
    public static string ToKebab(string name)
    {
        if (name.StartsWith("--", StringComparison.Ordinal))
            return name;

        if (name.Contains('-'))
            return name.ToLowerInvariant();

        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
                builder.Append('-');
            builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }

    /// <summary><c>border-radius</c> → <c>borderRadius</c>, the spelling WAAPI keyframe objects need. Custom properties are kept.</summary>
    public static string ToCamel(string css)
    {
        if (css.StartsWith("--", StringComparison.Ordinal) || !css.Contains('-'))
            return css == "float" ? "cssFloat" : css;

        var builder = new StringBuilder(css.Length);
        var upper = false;
        foreach (var c in css)
        {
            if (c == '-')
            {
                upper = true;
            }
            else
            {
                builder.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
        }
        return builder.ToString();
    }


    /// <summary>Formats a number the way CSS reads it: invariant, no exponent, at most 4 decimals.</summary>
    public static string Number(double value)
    {
        var rounded = Math.Round(value, 4);
        if (rounded == 0d)
            rounded = 0d; // no "-0"
        return rounded.ToString("0.####", CultureInfo.InvariantCulture);
    }
}

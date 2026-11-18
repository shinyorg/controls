using System.Globalization;
using Microsoft.Maui.Graphics;

namespace Shiny.Blazor.Controls.Keyframe.Compilation;

/// <summary>Parsing and formatting of the values a key can carry.</summary>
internal static class CssValues
{
    /// <summary>Formats a parameter value invariantly, so <c>Value="@(1.5)"</c> is "1.5" under any culture.</summary>
    public static string? Format(object? value) => value switch
    {
        null => null,
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    /// <summary>A number with an optional unit suffix: <c>12</c>, <c>12px</c>, <c>-50%</c>, <c>1.5turn</c>.</summary>
    public static bool TryParseQuantity(string? text, out double value, out string unit)
    {
        value = 0d;
        unit = "";
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        var end = 0;
        while (end < trimmed.Length && (char.IsDigit(trimmed[end]) || trimmed[end] is '.' or '-' or '+' or 'e' or 'E'))
        {
            // An 'e' only belongs to the number when digits follow it ("1e3"); "1em" is a unit.
            var isExponent = trimmed[end] is 'e' or 'E';
            var exponentFollows = end + 1 < trimmed.Length && (char.IsDigit(trimmed[end + 1]) || trimmed[end + 1] is '-' or '+');
            if (isExponent && !exponentFollows)
                break;
            end++;
        }

        if (end == 0 || !double.TryParse(trimmed[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return false;

        unit = trimmed[end..].Trim();
        return unit.Length == 0 || unit.All(c => char.IsLetter(c) || c == '%');
    }

    /// <summary>An angle in degrees: a bare number is degrees, and deg/rad/grad/turn are converted.</summary>
    public static bool TryParseDegrees(string? text, out double degrees)
    {
        degrees = 0d;
        if (!TryParseQuantity(text, out var value, out var unit))
            return false;

        double? converted = unit.ToLowerInvariant() switch
        {
            "" or "deg" => value,
            "rad" => value * 180d / Math.PI,
            "grad" => value * 0.9d,
            "turn" => value * 360d,
            _ => null
        };

        if (converted is not { } result)
            return false;

        degrees = result;
        return true;
    }

    /// <summary>A length: a bare number is px, anything else is kept as written.</summary>
    public static string Length(string text)
        => TryParseQuantity(text, out var value, out var unit) && unit.Length == 0
            ? CssProperties.Number(value) + "px"
            : text.Trim();

    /// <summary>
    /// A MAUI Thickness - <c>10</c> (uniform), <c>10,20</c> (horizontal, vertical) or <c>l,t,r,b</c> -
    /// written as CSS edges (top right bottom left). Text that is already CSS (spaces, units) is kept.
    /// </summary>
    public static string Thickness(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.Contains(','))
            return trimmed.Contains(' ') ? trimmed : Length(trimmed);

        var parts = trimmed.Split(',', StringSplitOptions.TrimEntries).Select(Length).ToArray();
        return parts.Length switch
        {
            2 => $"{parts[1]} {parts[0]} {parts[1]} {parts[0]}",
            4 => $"{parts[1]} {parts[2]} {parts[3]} {parts[0]}",
            _ => trimmed.Replace(',', ' ')
        };
    }

    /// <summary>
    /// A colour as <c>oklab()</c>, so the browser blends it in Oklab like the MAUI package does rather
    /// than through the grey midpoint sRGB gives. Text that is not a parseable colour (a
    /// <c>var()</c>, <c>currentColor</c>) is kept as written.
    /// </summary>
    public static string Colour(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("var(", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("oklab(", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("oklch(", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("color", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        Microsoft.Maui.Graphics.Color? parsed;
        try
        {
            parsed = Microsoft.Maui.Graphics.Color.Parse(trimmed);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or IndexOutOfRangeException)
        {
            parsed = null;
        }

        if (parsed is null)
            return trimmed;

        // Color.Parse returns null-ish defaults for text it does not recognise rather than throwing.
        var named = trimmed.Equals("transparent", StringComparison.OrdinalIgnoreCase);
        var looksLikeColour = named || trimmed.StartsWith('#') || trimmed.StartsWith("rgb", StringComparison.OrdinalIgnoreCase) ||
                              trimmed.StartsWith("hsl", StringComparison.OrdinalIgnoreCase) || trimmed.All(char.IsLetter);
        if (!looksLikeColour)
            return trimmed;

        return ToOklab(parsed.Red, parsed.Green, parsed.Blue, parsed.Alpha);
    }

    /// <summary>sRGB (0..1 channels) to a CSS <c>oklab()</c> string.</summary>
    public static string ToOklab(double red, double green, double blue, double alpha)
    {
        var r = Linearize(red);
        var g = Linearize(green);
        var b = Linearize(blue);

        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);

        var lightness = 0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s;
        var a = 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s;
        var bb = 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s;

        return $"oklab({CssProperties.Number(lightness)} {CssProperties.Number(a)} {CssProperties.Number(bb)} / {CssProperties.Number(alpha)})";
    }

    static double Linearize(double channel)
        => channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
}

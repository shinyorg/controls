using System.Globalization;
using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

/// <summary>
/// A colour, as bytes.
/// </summary>
/// <remarks>
/// Not an <c>SKColor</c>, even though this package draws with Skia: a host builds a
/// <see cref="FloorPlanTheme"/> out of MAUI resources or CSS custom properties, and neither host
/// should have to reach for Skia's colour type to say what its own theme is.
/// </remarks>
public readonly record struct PlanColor(byte A, byte R, byte G, byte B)
{
    public static PlanColor Rgb(byte r, byte g, byte b) => new(255, r, g, b);

    /// <summary>The same colour at a different alpha, 0-255.</summary>
    public PlanColor WithAlpha(byte alpha) => this with { A = alpha };

    /// <summary>The same colour at a fraction of its alpha.</summary>
    public PlanColor Fade(float factor) => this with { A = (byte)Math.Clamp(this.A * factor, 0, 255) };

    public SKColor ToSKColor() => new(this.R, this.G, this.B, this.A);

    /// <summary>
    /// Parses <c>#RGB</c>, <c>#RRGGBB</c> or <c>#AARRGGBB</c> (with or without the hash).
    /// </summary>
    /// <remarks>
    /// Returns false rather than throwing. Element colours come out of a saved document, and one bad
    /// string in a file someone hand-edited should cost that element its custom colour, not the
    /// whole plan.
    /// </remarks>
    public static bool TryParse(string? value, out PlanColor color)
    {
        color = default;
        if (String.IsNullOrWhiteSpace(value))
            return false;

        var text = value.AsSpan().Trim();
        if (text.Length > 0 && text[0] == '#')
            text = text[1..];

        switch (text.Length)
        {
            case 3:
            {
                if (!TryNibble(text[0], out var r) || !TryNibble(text[1], out var g) || !TryNibble(text[2], out var b))
                    return false;

                // #ABC means #AABBCC, so each nibble is doubled rather than shifted.
                color = new PlanColor(255, (byte)(r * 17), (byte)(g * 17), (byte)(b * 17));
                return true;
            }
            case 6:
            {
                if (!TryByte(text[..2], out var r) || !TryByte(text[2..4], out var g) || !TryByte(text[4..6], out var b))
                    return false;

                color = new PlanColor(255, r, g, b);
                return true;
            }
            case 8:
            {
                if (!TryByte(text[..2], out var a) || !TryByte(text[2..4], out var r) ||
                    !TryByte(text[4..6], out var g) || !TryByte(text[6..8], out var b))
                    return false;

                color = new PlanColor(a, r, g, b);
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>The parsed colour, or <paramref name="fallback"/> when the string is missing or malformed.</summary>
    public static PlanColor ParseOr(string? value, PlanColor fallback) =>
        TryParse(value, out var parsed) ? parsed : fallback;

    static bool TryByte(ReadOnlySpan<char> text, out byte value) =>
        Byte.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

    static bool TryNibble(char c, out int value)
    {
        if (TryByte(stackalloc[] { '0', c }, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = 0;
        return false;
    }
}

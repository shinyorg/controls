using Shiny.Controls.Office.Spreadsheet;

namespace Shiny.Controls.Office.Skia;

/// <summary>
/// Keeps ink readable against the ground it is actually painted on.
/// </summary>
/// <remarks>
/// <para>
/// The ground is whatever sits directly under the glyphs — a table cell's shading, a paragraph's
/// shading, a highlight, a shape's fill — and only the page when there is nothing in between. Measuring
/// against the page alone is what produced a dark-mode table header with a light authored fill and
/// white text on it: the text was lifted to read against the dark page it was no longer sitting on.
/// </para>
/// <para>
/// Authored fills are deliberately left as authored. A fill carries meaning (a header band, a status
/// colour, a highlighter), and the page itself is a picture of paper; only the ink moves, and only
/// when it does not already contrast with its ground.
/// </para>
/// </remarks>
static class InkContrast
{
    /// <summary>How far apart two perceived lightnesses must be before the ink is left alone.</summary>
    const double MinimumSeparation = 0.34;

    /// <summary>
    /// Adjusts <paramref name="color"/> so it reads against <paramref name="ground"/>, keeping its hue.
    /// </summary>
    public static ArgbColor Legible(ArgbColor color, ArgbColor ground)
    {
        var groundLight = Luminance(ground);
        var textLight = Luminance(color);

        // Comfortably clear of the ground: nothing to do.
        if (Math.Abs(groundLight - textLight) >= MinimumSeparation)
            return color;

        // The page grounds (near-black, white) land on the same 0.72 / 0.34 targets they always had;
        // the extra term only matters for a mid-tone fill, where a fixed target could sit on top of it.
        var target = groundLight < 0.5
            ? Math.Min(1, Math.Max(textLight, Math.Max(0.72, groundLight + 0.45)))   // dark ground: lift
            : Math.Max(0, Math.Min(textLight, Math.Min(0.34, groundLight - 0.45)));  // light ground: push down

        return WithLightness(color, target);
    }

    /// <summary>
    /// The colour actually showing when <paramref name="top"/> is painted over <paramref name="under"/>.
    /// </summary>
    public static ArgbColor Over(ArgbColor top, ArgbColor under)
    {
        if (top.A == 255)
            return top;

        if (top.A == 0)
            return under;

        var a = top.A / 255d;
        return new ArgbColor(
            255,
            (byte)Math.Round(top.R * a + under.R * (1 - a)),
            (byte)Math.Round(top.G * a + under.G * (1 - a)),
            (byte)Math.Round(top.B * a + under.B * (1 - a)));
    }

    /// <summary>Perceived brightness, weighted the way the eye responds rather than by raw average.</summary>
    public static double Luminance(ArgbColor color)
        => (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255d;

    static ArgbColor WithLightness(ArgbColor color, double target)
    {
        var current = Luminance(color);

        if (current <= 0.001)
        {
            // Pure black carries no hue to preserve, so it simply becomes the corresponding grey.
            var grey = (byte)Math.Clamp(Math.Round(target * 255), 0, 255);
            return color with { R = grey, G = grey, B = grey };
        }

        // Scaling all three channels keeps the ratios between them, and therefore the hue.
        var factor = target / current;
        return color with
        {
            R = (byte)Math.Clamp(Math.Round(color.R * factor), 0, 255),
            G = (byte)Math.Clamp(Math.Round(color.G * factor), 0, 255),
            B = (byte)Math.Clamp(Math.Round(color.B * factor), 0, 255)
        };
    }
}

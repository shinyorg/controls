namespace Shiny.ThemeGen;

/// <summary>
/// Minimal CIELAB-based color science used to derive Material-3 style tonal palettes
/// from a single seed color. This is an approximation of Material's HCT model: we keep
/// the seed's hue and chroma (in Lab) and vary L* to produce tones 0..100.
/// </summary>
static class ColorMath
{
    public static (double R, double G, double B) ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        var r = Convert.ToInt32(hex.Substring(0, 2), 16);
        var g = Convert.ToInt32(hex.Substring(2, 2), 16);
        var b = Convert.ToInt32(hex.Substring(4, 2), 16);
        return (r, g, b);
    }

    public static string ToHex(double r, double g, double b)
    {
        int Clamp(double v) => (int)Math.Round(Math.Clamp(v, 0, 255));
        return $"#{Clamp(r):X2}{Clamp(g):X2}{Clamp(b):X2}";
    }

    static double SrgbToLinear(double c)
    {
        c /= 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    static double LinearToSrgb(double c)
    {
        var v = c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;
        return v * 255.0;
    }

    // D65 reference white
    const double Xn = 0.95047, Yn = 1.0, Zn = 1.08883;

    public static (double L, double A, double B) RgbToLab(double r, double g, double b)
    {
        var rl = SrgbToLinear(r);
        var gl = SrgbToLinear(g);
        var bl = SrgbToLinear(b);

        var x = (rl * 0.4124564 + gl * 0.3575761 + bl * 0.1804375) / Xn;
        var y = (rl * 0.2126729 + gl * 0.7151522 + bl * 0.0721750) / Yn;
        var z = (rl * 0.0193339 + gl * 0.1191920 + bl * 0.9503041) / Zn;

        double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116.0;
        var fx = F(x);
        var fy = F(y);
        var fz = F(z);

        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    // ---------------------------------------------------------------------------------------------
    // Oklab — the space CSS `color-mix(in oklab, …)` mixes in.
    //
    // The derived half of the token contract is expressed as mixes over the authoring tokens, and the
    // web gets those as literal `color-mix()` so that overriding one token ripples through every role
    // that leans on it. MAUI has no such thing: a dictionary holds resolved Colors, so the same mixes
    // have to be computed here. Doing them in oklab rather than in sRGB is what keeps the two hosts
    // landing on the same colour — an sRGB midpoint of two saturated colours is visibly darker and
    // muddier than the oklab one, and the divergence would show up as "the container tints are wrong
    // on Android".
    // ---------------------------------------------------------------------------------------------

    public static (double L, double A, double B) RgbToOklab(double r, double g, double b)
    {
        var rl = SrgbToLinear(r);
        var gl = SrgbToLinear(g);
        var bl = SrgbToLinear(b);

        var l = Math.Cbrt(0.4122214708 * rl + 0.5363325363 * gl + 0.0514459929 * bl);
        var m = Math.Cbrt(0.2119034982 * rl + 0.6806995451 * gl + 0.1073969566 * bl);
        var s = Math.Cbrt(0.0883024619 * rl + 0.2817188376 * gl + 0.6299787005 * bl);

        return (
            0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s
        );
    }


    public static string OklabToHex(double L, double A, double B)
    {
        var l = Math.Pow(L + 0.3963377774 * A + 0.2158037573 * B, 3);
        var m = Math.Pow(L - 0.1055613458 * A - 0.0638541728 * B, 3);
        var s = Math.Pow(L - 0.0894841775 * A - 1.2914855480 * B, 3);

        var rl = +4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
        var gl = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
        var bl = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;

        return ToHex(LinearToSrgb(rl), LinearToSrgb(gl), LinearToSrgb(bl));
    }


    /// <summary>
    /// The C# twin of <c>color-mix(in oklab, {a} {percent}%, {b})</c> — <paramref name="percent"/> of
    /// <paramref name="a"/> against the remainder of <paramref name="b"/>.
    /// </summary>
    public static string Mix(string a, double percent, string b)
    {
        var (ar, ag, ab) = ParseHex(a);
        var (br, bg, bb) = ParseHex(b);

        var (al, aa, aB) = RgbToOklab(ar, ag, ab);
        var (bl, ba, bB) = RgbToOklab(br, bg, bb);

        var w = Math.Clamp(percent, 0d, 1d);
        return OklabToHex(
            al * w + bl * (1 - w),
            aa * w + ba * (1 - w),
            aB * w + bB * (1 - w)
        );
    }


    public static string LabToHex(double l, double a, double bb)
    {
        var fy = (l + 16) / 116.0;
        var fx = fy + a / 500.0;
        var fz = fy - bb / 200.0;

        double Inv(double t)
        {
            var t3 = t * t * t;
            return t3 > 0.008856 ? t3 : (t - 16.0 / 116.0) / 7.787;
        }

        var x = Inv(fx) * Xn;
        var y = Inv(fy) * Yn;
        var z = Inv(fz) * Zn;

        var rl = x * 3.2404542 + y * -1.5371385 + z * -0.4985314;
        var gl = x * -0.9692660 + y * 1.8760108 + z * 0.0415560;
        var bl = x * 0.0556434 + y * -0.2040259 + z * 1.0572252;

        return ToHex(LinearToSrgb(rl), LinearToSrgb(gl), LinearToSrgb(bl));
    }
}

/// <summary>A tonal palette derived from a seed: keeps hue/chroma, varies tone (L*).</summary>
sealed class TonalPalette
{
    readonly double hueRad;
    readonly double chroma;

    TonalPalette(double hueRad, double chroma)
    {
        this.hueRad = hueRad;
        this.chroma = chroma;
    }

    public static TonalPalette FromSeed(string hex)
    {
        var (r, g, b) = ColorMath.ParseHex(hex);
        var (_, a, bb) = ColorMath.RgbToLab(r, g, b);
        var hue = Math.Atan2(bb, a);
        var chroma = Math.Sqrt(a * a + bb * bb);
        return new TonalPalette(hue, chroma);
    }

    /// <summary>Returns the hex for the given tone (0 = black .. 100 = white).</summary>
    public string Tone(double tone)
    {
        // Taper chroma toward the lightness extremes so tone 100 -> white and tone 0 -> black,
        // approximating how Material's HCT gamut collapses chroma near black/white. Tones in the
        // 10..90 band (accents and containers) keep full chroma.
        //
        // The curve is sqrt rather than linear: a linear taper left tone 98 (Surface) at only 20%
        // chroma, which on a low-chroma neutral seed is indistinguishable from pure grey, so every
        // pack's surfaces came out the same off-white. sqrt still reaches exactly 0 at tone 100/0
        // but keeps ~45% at tone 98, enough for a surface to carry the pack's hue.
        double factor;
        if (tone >= 90) factor = Math.Sqrt(Math.Max(0, (100 - tone) / 10.0));
        else if (tone <= 10) factor = Math.Sqrt(Math.Max(0, tone / 10.0));
        else factor = 1.0;

        var c = chroma * factor;
        var a = c * Math.Cos(hueRad);
        var b = c * Math.Sin(hueRad);
        return ColorMath.LabToHex(tone, a, b);
    }
}

using Shiny.Controls.Office.Text;
using SkiaSharp;

namespace Shiny.Controls.Office.Skia;

/// <summary>
/// Font metrics from SkiaSharp, with substitution for fonts the system does not have.
/// </summary>
/// <remarks>
/// <para>
/// Documents reference fonts nobody outside Office installs — Calibri and Cambria above all. Without a
/// metric-compatible substitute the measured widths differ from the ones the document was written
/// against, and every line breaks in the wrong place. Carlito and Caladea are metric-compatible with
/// Calibri and Cambria respectively.
/// </para>
/// <para>
/// Resolution order is: a face registered in <see cref="OfficeFontRegistry"/> under the requested
/// family, then the same for each substitute, then the platform's own fonts. The registry comes first
/// because on WebAssembly there are no platform fonts at all, and asking for one there returns a
/// wrong-but-non-null fallback rather than failing.
/// </para>
/// </remarks>
public sealed class SkiaTextMeasurer : ITextMeasurer, IDisposable
{
    readonly Dictionary<FontKey, SKFont> fonts = new();
    readonly Dictionary<string, string> resolvedFamilies = new(StringComparer.OrdinalIgnoreCase);
    int registryGeneration = -1;

    public SkiaTextMeasurer(OfficeFontRegistry? registry = null)
        => this.Registry = registry ?? OfficeFontRegistry.Default;

    /// <summary>Application-supplied faces, consulted before the platform's fonts.</summary>
    public OfficeFontRegistry Registry { get; }

    /// <inheritdoc />
    public int FontGeneration => this.Registry.Count;

    readonly record struct FontKey(string Family, float Size, bool Bold, bool Italic);

    /// <summary>Substitutions tried in order before falling back to the platform default.</summary>
    static readonly Dictionary<string, string[]> Substitutes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Calibri"] = ["Carlito", "Helvetica Neue", "Arial", "Helvetica"],
        ["Aptos"] = ["Carlito", "Helvetica Neue", "Arial", "Helvetica"],
        ["Aptos Display"] = ["Carlito", "Helvetica Neue", "Arial"],
        ["Calibri Light"] = ["Carlito", "Helvetica Neue", "Arial"],
        ["Cambria"] = ["Caladea", "Georgia", "Times New Roman", "Times"],
        ["Cambria Math"] = ["Caladea", "Georgia", "Times New Roman"],
        ["Candara"] = ["Optima", "Gill Sans", "Arial"],
        ["Constantia"] = ["Caladea", "Georgia", "Times New Roman"],
        ["Corbel"] = ["Carlito", "Helvetica Neue", "Arial"],
        ["Segoe UI"] = ["Carlito", "Helvetica Neue", "Arial", "Helvetica"],

        // The slide themes' faces. Each ends on the bundled Carlito or Caladea, so a browser (which
        // has no system fonts) still sets a theme in a proportional face of the right kind rather than
        // the monospace fallback.
        ["Arial"] = ["Helvetica", "Helvetica Neue", "Carlito"],
        ["Helvetica"] = ["Arial", "Helvetica Neue", "Carlito"],
        ["Verdana"] = ["Arial", "Helvetica Neue", "Carlito"],
        ["Tahoma"] = ["Verdana", "Arial", "Carlito"],
        ["Trebuchet MS"] = ["Arial", "Helvetica Neue", "Carlito"],
        ["Century Gothic"] = ["Futura", "Avenir Next", "Arial", "Carlito"],
        ["Gill Sans MT"] = ["Gill Sans", "Arial", "Carlito"],
        ["Georgia"] = ["Times New Roman", "Times", "Caladea"],
        ["Times New Roman"] = ["Times", "Georgia", "Caladea"],
        ["Garamond"] = ["EB Garamond", "Georgia", "Times New Roman", "Caladea"],
        ["Palatino Linotype"] = ["Palatino", "Georgia", "Caladea"],
        ["Book Antiqua"] = ["Palatino", "Georgia", "Caladea"],
        ["Wingdings"] = ["Apple Symbols", "Segoe UI Symbol", "Arial Unicode MS"],
        ["Symbol"] = ["Apple Symbols", "Segoe UI Symbol", "Arial Unicode MS"]
    };

    public TextMetrics Measure(ReadOnlySpan<char> text, TextStyle style)
    {
        var font = this.GetFont(style);
        var metrics = font.Metrics;

        if (text.IsEmpty)
            return new TextMetrics(0, -metrics.Ascent, metrics.Descent);

        return new TextMetrics(font.MeasureText(text), -metrics.Ascent, metrics.Descent);
    }

    public TextMetrics LineMetrics(TextStyle style)
    {
        var metrics = this.GetFont(style).Metrics;
        return new TextMetrics(0, -metrics.Ascent, metrics.Descent);
    }

    /// <summary>The font for a style, cached — resolving a typeface hits the system font manager.</summary>
    public SKFont GetFont(TextStyle style)
    {
        // Fonts registered after something was already measured must invalidate the cache, or the
        // first frame's fallback metrics are kept forever and the newly loaded faces never appear.
        if (this.registryGeneration != this.Registry.Count)
        {
            this.ClearCache();
            this.registryGeneration = this.Registry.Count;
        }

        var size = (float)Math.Max(1, style.EffectiveFontSize);
        var key = new FontKey(style.FontFamily, size, style.Bold, style.Italic);

        if (this.fonts.TryGetValue(key, out var cached))
            return cached;

        var typeface = this.ResolveTypeface(style.FontFamily, style.Bold, style.Italic);
        var font = new SKFont(typeface, size) { Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };

        this.fonts[key] = font;
        return font;
    }

    SKTypeface ResolveTypeface(string requested, bool bold, bool italic)
    {
        if (this.Registry.Find(requested, bold, italic) is { } exact)
            return exact;

        // The substitution table is consulted against the registry first: on WebAssembly the platform
        // has nothing, so a bundled Carlito is the only way a Calibri request resolves correctly.
        if (Substitutes.TryGetValue(requested, out var candidates))
        {
            foreach (var candidate in candidates)
            {
                if (this.Registry.Find(candidate, bold, italic) is { } substituted)
                    return substituted;
            }
        }

        var fontStyle = new SKFontStyle(
            bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

        var family = this.ResolveFamily(requested);
        return SKTypeface.FromFamilyName(family, fontStyle) ?? SKTypeface.Default;
    }

    void ClearCache()
    {
        foreach (var font in this.fonts.Values)
            font.Dispose();

        this.fonts.Clear();
        this.resolvedFamilies.Clear();
    }

    string ResolveFamily(string requested)
    {
        if (this.resolvedFamilies.TryGetValue(requested, out var cached))
            return cached;

        var result = requested;

        // SKTypeface.FromFamilyName never returns null for a missing family - it returns the default -
        // so availability has to be checked by asking whether the name came back.
        if (!IsAvailable(requested) && Substitutes.TryGetValue(requested, out var candidates))
        {
            foreach (var candidate in candidates)
            {
                if (!IsAvailable(candidate))
                    continue;

                result = candidate;
                break;
            }
        }

        this.resolvedFamilies[requested] = result;
        return result;
    }

    static bool IsAvailable(string family)
    {
        using var typeface = SKTypeface.FromFamilyName(family);
        return typeface is not null &&
               string.Equals(typeface.FamilyName, family, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => this.ClearCache();
}

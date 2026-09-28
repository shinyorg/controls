using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Drawing;
using DocumentFormat.OpenXml.Packaging;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using D = DocumentFormat.OpenXml.Drawing;

namespace Shiny.Controls.Office.Presentation;

/// <summary>
/// Reads the DrawingML vocabulary shared by shapes, fills, outlines and text bodies.
/// </summary>
sealed class DrawingReader(ThemeColors theme)
{
    public ThemeColors Theme { get; } = theme;

    public static ShapeGeometry MapGeometry(string? preset) => preset switch
    {
        null or "rect" => ShapeGeometry.Rectangle,
        "roundRect" or "round1Rect" or "round2SameRect" or "round2DiagRect" => ShapeGeometry.RoundedRectangle,
        "ellipse" or "circle" => ShapeGeometry.Ellipse,
        "triangle" or "isoscelesTriangle" => ShapeGeometry.Triangle,
        "rtTriangle" => ShapeGeometry.RightTriangle,
        "diamond" => ShapeGeometry.Diamond,
        "line" or "straightConnector1" or "bentConnector2" or "bentConnector3" => ShapeGeometry.Line,
        "rightArrow" => ShapeGeometry.RightArrow,
        "leftArrow" => ShapeGeometry.LeftArrow,
        "upArrow" => ShapeGeometry.UpArrow,
        "downArrow" => ShapeGeometry.DownArrow,
        "pentagon" or "homePlate" => ShapeGeometry.Pentagon,
        "hexagon" => ShapeGeometry.Hexagon,
        "star5" => ShapeGeometry.Star5,
        "chevron" => ShapeGeometry.Chevron,
        "parallelogram" => ShapeGeometry.Parallelogram,
        "trapezoid" => ShapeGeometry.Trapezoid,
        "plus" or "mathPlus" => ShapeGeometry.Plus,
        "can" or "cylinder" => ShapeGeometry.Can,
        "cloud" => ShapeGeometry.Cloud,
        _ => ShapeGeometry.Rectangle
    };

    /// <summary>True when the preset is one the viewer draws faithfully rather than approximating.</summary>
    public static bool IsKnownGeometry(string? preset) => preset switch
    {
        null or "rect" or "roundRect" or "ellipse" or "circle" or "triangle" or "isoscelesTriangle"
            or "rtTriangle" or "diamond" or "line" or "straightConnector1" or "rightArrow" or "leftArrow"
            or "upArrow" or "downArrow" or "pentagon" or "homePlate" or "hexagon" or "star5" or "chevron"
            or "parallelogram" or "trapezoid" or "plus" or "mathPlus" or "can" or "cylinder" or "cloud" => true,
        _ => false
    };

    public ShapeFill ReadFill(OpenXmlElement? properties)
    {
        if (properties is null)
            return ShapeFill.None;

        foreach (var child in properties.ChildElements)
        {
            switch (child)
            {
                case NoFill:
                    return ShapeFill.None;

                case SolidFill solid:
                    var color = this.ReadColor(solid);
                    return color is null ? ShapeFill.None : new ShapeFill { Solid = color };

                case GradientFill gradient:
                    var stops = new List<(double, ArgbColor)>();
                    foreach (var stop in gradient.GradientStopList?.Elements<GradientStop>() ?? [])
                    {
                        var stopColor = this.ReadColor(stop);
                        if (stopColor is not null)
                            stops.Add((stop.Position?.Value / 100000d ?? 0, stopColor.Value));
                    }

                    if (stops.Count == 0)
                        return ShapeFill.None;

                    var angle = gradient.GetFirstChild<LinearGradientFill>()?.Angle?.Value is { } a
                        ? OoxmlUnits.AngleToDegrees(a)
                        : 90;

                    return new ShapeFill { GradientStops = stops.OrderBy(x => x.Item1).ToList(), GradientAngle = angle };
            }
        }

        return ShapeFill.None;
    }

    public ShapeOutline? ReadOutline(OpenXmlElement? properties)
    {
        var line = properties?.GetFirstChild<Outline>();
        if (line is null)
            return null;

        if (line.GetFirstChild<NoFill>() is not null)
            return null;

        var color = this.ReadColor(line.GetFirstChild<SolidFill>());
        if (color is null)
            return null;

        // Width is in EMU; a line with no width is a hairline, which PowerPoint draws at 1px.
        var width = line.Width?.Value is { } w ? OoxmlUnits.EmuToPixels(w) : 1;
        var dash = MapDash(OoxmlUnits.EnumAttribute(line.GetFirstChild<PresetDash>(), "val"));

        return new ShapeOutline(color.Value, Math.Max(0.5, width), dash != LineDash.Solid) { Dash = dash };
    }

    /// <summary>The dash pattern for an <c>a:prstDash val</c>, read as the spec's token.</summary>
    public static LineDash MapDash(string? token) => token switch
    {
        "dot" => LineDash.Dot,
        "dash" => LineDash.Dash,
        "lgDash" => LineDash.LargeDash,
        "dashDot" => LineDash.DashDot,
        "lgDashDot" => LineDash.LongDashDot,
        "lgDashDotDot" => LineDash.LongDashDotDot,
        "sysDash" => LineDash.SystemDash,
        "sysDot" or "sysDashDot" or "sysDashDotDot" => LineDash.SystemDot,
        _ => LineDash.Solid
    };

    /// <summary>The <c>a:prstDash val</c> token for a pattern.</summary>
    public static string DashToken(LineDash dash) => dash switch
    {
        LineDash.Dot => "dot",
        LineDash.Dash => "dash",
        LineDash.LargeDash => "lgDash",
        LineDash.DashDot => "dashDot",
        LineDash.LongDashDot => "lgDashDot",
        LineDash.LongDashDotDot => "lgDashDotDot",
        LineDash.SystemDash => "sysDash",
        LineDash.SystemDot => "sysDot",
        _ => "solid"
    };

    /// <summary>
    /// The outer shadow in a shape's effect list, or null.
    /// </summary>
    /// <remarks>
    /// Only <c>a:outerShdw</c>: it is what every Shadow preset in PowerPoint's Shape Effects gallery
    /// writes, and the inner and perspective kinds would each need a painter of their own.
    /// </remarks>
    public ShapeShadow? ReadShadow(OpenXmlElement? properties)
    {
        var shadow = properties?.GetFirstChild<EffectList>()?.GetFirstChild<OuterShadow>();
        if (shadow is null)
            return null;

        var color = this.ReadColor(shadow) ?? new ArgbColor(102, 0, 0, 0);

        return new ShapeShadow(
            color,
            shadow.BlurRadius?.Value is { } blur ? OoxmlUnits.EmuToPixels(blur) : 0,
            shadow.Distance?.Value is { } distance ? OoxmlUnits.EmuToPixels(distance) : 0,
            shadow.Direction?.Value is { } direction ? OoxmlUnits.AngleToDegrees(direction) : 0);
    }

    /// <summary>
    /// Resolves a colour element, following theme references and applying the modifiers that sit
    /// underneath them.
    /// </summary>
    /// <remarks>
    /// A theme colour is almost never used raw — <c>lumMod</c>, <c>lumOff</c>, <c>shade</c>, <c>tint</c>
    /// and <c>alpha</c> are how "Accent 1, Lighter 40%" is stored. Ignoring them renders every themed
    /// shape at full saturation, which is the single most obvious way a deck looks wrong.
    /// </remarks>
    public ArgbColor? ReadColor(OpenXmlElement? container)
    {
        if (container is null)
            return null;

        foreach (var child in container.ChildElements)
        {
            switch (child)
            {
                case RgbColorModelHex hex when hex.Val?.Value is { } value:
                    return ApplyModifiers(ParseHex(value), hex);

                case SchemeColor scheme when OoxmlUnits.EnumAttribute(scheme, "val") is { } name:
                    var resolved = this.Theme.Resolve(name);
                    return resolved is null ? null : ApplyModifiers(resolved.Value, scheme);

                case SystemColor system:
                    var systemColor = system.LastColor?.Value is { } last
                        ? ParseHex(last)
                        : new ArgbColor(255, 0, 0, 0);

                    return ApplyModifiers(systemColor, system);

                case PresetColor preset when OoxmlUnits.EnumAttribute(preset, "val") is { } presetName:
                    return ApplyModifiers(NamedColor(presetName), preset);
            }
        }

        return null;
    }

    static ArgbColor ApplyModifiers(ArgbColor color, OpenXmlElement element)
    {
        foreach (var child in element.ChildElements)
        {
            switch (child)
            {
                case LuminanceModulation lumMod when lumMod.Val?.Value is { } value:
                    color = ScaleLuminance(color, value / 100000d);
                    break;

                case LuminanceOffset lumOff when lumOff.Val?.Value is { } value:
                    color = OffsetLuminance(color, value / 100000d);
                    break;

                case Shade shade when shade.Val?.Value is { } value:
                    color = Multiply(color, value / 100000d);
                    break;

                case Tint tint when tint.Val?.Value is { } value:
                    var amount = value / 100000d;
                    color = color with
                    {
                        R = Lerp(color.R, 255, 1 - amount),
                        G = Lerp(color.G, 255, 1 - amount),
                        B = Lerp(color.B, 255, 1 - amount)
                    };

                    break;

                case Alpha alpha when alpha.Val?.Value is { } value:
                    color = color with { A = (byte)Math.Clamp(Math.Round(value / 100000d * 255), 0, 255) };
                    break;
            }
        }

        return color;
    }

    static byte Lerp(byte from, byte to, double amount)
        => (byte)Math.Clamp(Math.Round(from + (to - from) * amount), 0, 255);

    static ArgbColor Multiply(ArgbColor color, double factor) => color with
    {
        R = (byte)Math.Clamp(Math.Round(color.R * factor), 0, 255),
        G = (byte)Math.Clamp(Math.Round(color.G * factor), 0, 255),
        B = (byte)Math.Clamp(Math.Round(color.B * factor), 0, 255)
    };

    static ArgbColor ScaleLuminance(ArgbColor color, double factor)
    {
        var (h, l, s) = ToHsl(color);
        return FromHsl(h, Math.Clamp(l * factor, 0, 1), s, color.A);
    }

    static ArgbColor OffsetLuminance(ArgbColor color, double offset)
    {
        var (h, l, s) = ToHsl(color);
        return FromHsl(h, Math.Clamp(l + offset, 0, 1), s, color.A);
    }

    static (double H, double L, double S) ToHsl(ArgbColor color)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;

        if (Math.Abs(max - min) < 1e-9)
            return (0, l, 0);

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);

        double h;
        if (Math.Abs(max - r) < 1e-9)
            h = (g - b) / d + (g < b ? 6 : 0);
        else if (Math.Abs(max - g) < 1e-9)
            h = (b - r) / d + 2;
        else
            h = (r - g) / d + 4;

        return (h / 6, l, s);
    }

    static ArgbColor FromHsl(double h, double l, double s, byte alpha)
    {
        if (s <= 0)
        {
            var grey = (byte)Math.Clamp(Math.Round(l * 255), 0, 255);
            return new ArgbColor(alpha, grey, grey, grey);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;

        return new ArgbColor(
            alpha,
            Channel(p, q, h + 1d / 3),
            Channel(p, q, h),
            Channel(p, q, h - 1d / 3));

        static byte Channel(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;

            var value = t switch
            {
                < 1d / 6 => p + (q - p) * 6 * t,
                < 1d / 2 => q,
                < 2d / 3 => p + (q - p) * (2d / 3 - t) * 6,
                _ => p
            };

            return (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
        }
    }

    public static ArgbColor ParseHex(string hex)
    {
        var span = hex.AsSpan().TrimStart('#');
        return span.Length == 6 && uint.TryParse(span, System.Globalization.NumberStyles.HexNumber, null, out var rgb)
            ? ArgbColor.FromUInt32(0xFF000000u | rgb)
            : new ArgbColor(255, 0, 0, 0);
    }

    static ArgbColor NamedColor(string name) => name.ToLowerInvariant() switch
    {
        "white" => new ArgbColor(255, 255, 255, 255),
        "black" => new ArgbColor(255, 0, 0, 0),
        "red" => new ArgbColor(255, 255, 0, 0),
        "green" => new ArgbColor(255, 0, 128, 0),
        "blue" => new ArgbColor(255, 0, 0, 255),
        "yellow" => new ArgbColor(255, 255, 255, 0),
        "gray" or "grey" => new ArgbColor(255, 128, 128, 128),
        "orange" => new ArgbColor(255, 255, 165, 0),
        _ => new ArgbColor(255, 128, 128, 128)
    };
}

/// <summary>
/// A theme's font scheme: the heading (major) and body (minor) Latin faces.
/// </summary>
/// <remarks>
/// A run whose typeface is <c>+mj-lt</c> or <c>+mn-lt</c> names one of these rather than a family, which
/// is how a deck's text follows a theme switch — every master text style is written that way.
/// </remarks>
sealed class ThemeFonts
{
    public string? Major { get; private init; }

    public string? Minor { get; private init; }

    public static ThemeFonts From(ThemePart? part)
    {
        var scheme = part?.Theme?.ThemeElements?.FontScheme;
        return new ThemeFonts
        {
            Major = Clean(scheme?.MajorFont?.LatinFont?.Typeface?.Value),
            Minor = Clean(scheme?.MinorFont?.LatinFont?.Typeface?.Value)
        };

        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>The family a <c>+mj-*</c>/<c>+mn-*</c> reference means, or null when it is neither.</summary>
    public string? Resolve(string typeface) => typeface switch
    {
        _ when typeface.StartsWith("+mj", StringComparison.Ordinal) => this.Major,
        _ when typeface.StartsWith("+mn", StringComparison.Ordinal) => this.Minor,
        _ => null
    };
}

/// <summary>A theme's colour scheme, with PowerPoint's index quirk applied.</summary>
sealed class ThemeColors
{
    readonly Dictionary<string, ArgbColor> colors = new(StringComparer.OrdinalIgnoreCase);

    public static ThemeColors From(ThemePart? part)
    {
        var result = new ThemeColors();
        var scheme = part?.Theme?.ThemeElements?.ColorScheme;
        if (scheme is null)
            return result;

        void Add(string name, Color2Type? color)
        {
            if (color?.RgbColorModelHex?.Val?.Value is { } hex)
                result.colors[name] = DrawingReader.ParseHex(hex);
            else if (color?.SystemColor?.LastColor?.Value is { } system)
                result.colors[name] = DrawingReader.ParseHex(system);
        }

        Add("dk1", scheme.Dark1Color);
        Add("lt1", scheme.Light1Color);
        Add("dk2", scheme.Dark2Color);
        Add("lt2", scheme.Light2Color);
        Add("accent1", scheme.Accent1Color);
        Add("accent2", scheme.Accent2Color);
        Add("accent3", scheme.Accent3Color);
        Add("accent4", scheme.Accent4Color);
        Add("accent5", scheme.Accent5Color);
        Add("accent6", scheme.Accent6Color);
        Add("hlink", scheme.Hyperlink);
        Add("folHlink", scheme.FollowedHyperlinkColor);

        return result;
    }

    public ArgbColor? Resolve(string name)
    {
        // tx1/bg1 are aliases for dk1/lt1, and are what slide content actually references.
        var key = name.ToLowerInvariant() switch
        {
            "tx1" => "dk1",
            "bg1" => "lt1",
            "tx2" => "dk2",
            "bg2" => "lt2",
            "phclr" => "accent1",
            var other => other
        };

        return this.colors.TryGetValue(key, out var color) ? color : null;
    }
}

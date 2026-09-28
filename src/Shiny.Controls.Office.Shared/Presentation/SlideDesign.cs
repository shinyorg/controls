using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using D = DocumentFormat.OpenXml.Drawing;

namespace Shiny.Controls.Office.Presentation;

/// <summary>The twelve colours of a theme's colour scheme, in DrawingML's order.</summary>
public sealed record SlideColorScheme(
    string Name,
    ArgbColor Dark1,
    ArgbColor Light1,
    ArgbColor Dark2,
    ArgbColor Light2,
    ArgbColor Accent1,
    ArgbColor Accent2,
    ArgbColor Accent3,
    ArgbColor Accent4,
    ArgbColor Accent5,
    ArgbColor Accent6,
    ArgbColor Hyperlink,
    ArgbColor FollowedHyperlink)
{
    public IReadOnlyList<ArgbColor> Accents => [this.Accent1, this.Accent2, this.Accent3, this.Accent4, this.Accent5, this.Accent6];

    /// <summary>The colour for a scheme token (<c>accent1</c>, <c>tx1</c>, <c>bg1</c>...).</summary>
    public ArgbColor? Resolve(string token) => token switch
    {
        "dk1" or "tx1" => this.Dark1,
        "lt1" or "bg1" => this.Light1,
        "dk2" or "tx2" => this.Dark2,
        "lt2" or "bg2" => this.Light2,
        "accent1" => this.Accent1,
        "accent2" => this.Accent2,
        "accent3" => this.Accent3,
        "accent4" => this.Accent4,
        "accent5" => this.Accent5,
        "accent6" => this.Accent6,
        "hlink" => this.Hyperlink,
        "folHlink" => this.FollowedHyperlink,
        _ => null
    };

    /// <summary>
    /// PowerPoint's colour picker grid: each theme colour down a column, then its lighter and darker
    /// shades — Lighter 80/60/40%, Darker 25/50% — which is what "Accent 1, Lighter 40%" means.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<ArgbColor>> PickerGrid()
    {
        ArgbColor[] columns = [this.Light1, this.Dark1, this.Light2, this.Dark2, this.Accent1, this.Accent2, this.Accent3, this.Accent4, this.Accent5, this.Accent6];
        return columns.Select(c => (IReadOnlyList<ArgbColor>)[c, Mix(c, 0.8, true), Mix(c, 0.6, true), Mix(c, 0.4, true), Mix(c, 0.25, false), Mix(c, 0.5, false)]).ToList();

        static ArgbColor Mix(ArgbColor c, double amount, bool lighter) => lighter
            ? new ArgbColor(255, (byte)(c.R + (255 - c.R) * amount), (byte)(c.G + (255 - c.G) * amount), (byte)(c.B + (255 - c.B) * amount))
            : new ArgbColor(255, (byte)(c.R * (1 - amount)), (byte)(c.G * (1 - amount)), (byte)(c.B * (1 - amount)));
    }
}

/// <summary>A theme the Design tab can apply: a colour scheme, a font scheme, and colour variants.</summary>
public sealed record SlideThemeDefinition(string Name, SlideColorScheme Colors, string MajorFont, string MinorFont)
{
    /// <summary>
    /// The Variants gallery: the theme's own colours, two with the accents rotated, and a dark
    /// version — the light and dark backgrounds swapped.
    /// </summary>
    public IReadOnlyList<SlideColorScheme> Variants
    {
        get
        {
            var c = this.Colors;
            var accents = c.Accents;

            SlideColorScheme Rotated(int by, string name) => c with
            {
                Name = name,
                Accent1 = accents[(0 + by) % 6],
                Accent2 = accents[(1 + by) % 6],
                Accent3 = accents[(2 + by) % 6],
                Accent4 = accents[(3 + by) % 6],
                Accent5 = accents[(4 + by) % 6],
                Accent6 = accents[(5 + by) % 6]
            };

            return
            [
                c,
                Rotated(2, $"{c.Name} 2"),
                Rotated(4, $"{c.Name} 3"),
                c with
                {
                    Name = $"{c.Name} Dark",
                    Dark1 = c.Light1,
                    Light1 = c.Dark2,
                    Dark2 = c.Light2,
                    Light2 = c.Dark1
                }
            ];
        }
    }

    static ArgbColor Hex(string value) => DrawingReader.ParseHex(value);

    static SlideColorScheme Scheme(string name, string dk1, string lt1, string dk2, string lt2, string a1, string a2, string a3, string a4, string a5, string a6, string hlink, string folHlink)
        => new(name, Hex(dk1), Hex(lt1), Hex(dk2), Hex(lt2), Hex(a1), Hex(a2), Hex(a3), Hex(a4), Hex(a5), Hex(a6), Hex(hlink), Hex(folHlink));

    /// <summary>The built-in themes, the default first.</summary>
    public static IReadOnlyList<SlideThemeDefinition> BuiltIn { get; } =
    [
        new("Office Theme",
            Scheme("Office", "000000", "FFFFFF", "44546A", "E7E6E6", "4472C4", "ED7D31", "A5A5A5", "FFC000", "5B9BD5", "70AD47", "0563C1", "954F72"),
            "Calibri Light", "Calibri"),
        new("Meadow",
            Scheme("Meadow", "000000", "FFFFFF", "2C3C43", "EBEBEB", "90C226", "54A021", "E6B91E", "E76618", "C42F1A", "918655", "99CA3C", "B9D181"),
            "Trebuchet MS", "Trebuchet MS"),
        new("Midnight",
            Scheme("Midnight", "FFFFFF", "1B2A41", "E7E6E6", "243B55", "4CC9F0", "F72585", "B983FF", "FFD166", "06D6A0", "FF8C42", "7FDBFF", "C3A6FF"),
            "Century Gothic", "Century Gothic"),
        new("Terracotta",
            Scheme("Terracotta", "000000", "FFFFFF", "637052", "CCDDEA", "E48312", "BD582C", "865640", "9B8357", "C2BC80", "94A088", "6B9F25", "8C8C8C"),
            "Calibri Light", "Calibri"),
        new("Slate",
            Scheme("Slate", "000000", "FFFFFF", "212745", "B4DCFA", "4E67C8", "5ECCF3", "A7EA52", "5DCEAF", "FF8021", "F14124", "56C7AA", "59A8D1"),
            "Georgia", "Verdana"),
        new("Botanical",
            Scheme("Botanical", "000000", "FFFFFF", "212121", "F1ECE3", "83992A", "3C9770", "44709D", "A23C33", "D97828", "DEB340", "44709D", "A23C33"),
            "Garamond", "Garamond"),
        new("Mono",
            Scheme("Mono", "000000", "FFFFFF", "262626", "F2F2F2", "404040", "595959", "7F7F7F", "A5A5A5", "BFBFBF", "262626", "0563C1", "7F7F7F"),
            "Arial", "Arial")
    ];
}

/// <summary>
/// Applies a theme's colours and fonts to every slide master's theme part.
/// </summary>
/// <remarks>
/// <para>
/// The theme part is rewritten in place — <c>a:clrScheme</c>'s twelve colours and <c>a:fontScheme</c>'s
/// Latin faces — rather than a new theme part related, so PowerPoint opens the file on the new theme and
/// every <c>a:schemeClr</c> and <c>+mn-lt</c> in the deck follows it. Undo puts each theme element back
/// exactly.
/// </para>
/// <para>
/// A variant is the same command with only a colour scheme: fonts stay as they are.
/// </para>
/// </remarks>
public sealed record ApplySlideThemeCommand(SlideColorScheme? Colors, string? MajorFont = null, string? MinorFont = null, string? ThemeName = null) : SlideCommand
{
    public override string Name => "Theme";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var parts = context.PresentationPart.SlideMasterParts.Select(x => x.ThemePart).OfType<ThemePart>().Distinct().ToList();
        if (parts.Count == 0)
            return new NoOpSlideCommand();

        var inverse = new RestoreThemesCommand(parts.Select(x => (x, (D.Theme?)x.Theme?.CloneNode(true))).ToList());

        foreach (var part in parts)
        {
            if (part.Theme is not { } theme)
                continue;

            if (this.ThemeName is { } name)
                theme.Name = name;

            var elements = theme.ThemeElements ??= new D.ThemeElements();

            if (this.Colors is { } colors)
                WriteColors(elements.ColorScheme ??= new D.ColorScheme(), colors);

            if (this.MajorFont is not null || this.MinorFont is not null)
            {
                var fonts = elements.FontScheme ??= new D.FontScheme(new D.MajorFont(new D.LatinFont(), new D.EastAsianFont(), new D.ComplexScriptFont()), new D.MinorFont(new D.LatinFont(), new D.EastAsianFont(), new D.ComplexScriptFont()));
                if (this.ThemeName is { } fontName)
                    fonts.Name = fontName;

                if (this.MajorFont is { } major && fonts.MajorFont is { } majorFont)
                    (majorFont.LatinFont ??= new D.LatinFont()).Typeface = major;

                if (this.MinorFont is { } minor && fonts.MinorFont is { } minorFont)
                    (minorFont.LatinFont ??= new D.LatinFont()).Typeface = minor;
            }

            context.MarkPartDirty(part);
        }

        context.ReprojectAll();
        return inverse;
    }

    static void WriteColors(D.ColorScheme scheme, SlideColorScheme colors)
    {
        scheme.Name = colors.Name;
        scheme.RemoveAllChildren();

        // The twelve in schema order; srgbClr throughout, since a system colour would follow the
        // viewer's Windows palette rather than the theme.
        scheme.Append(
            new D.Dark1Color(Rgb(colors.Dark1)),
            new D.Light1Color(Rgb(colors.Light1)),
            new D.Dark2Color(Rgb(colors.Dark2)),
            new D.Light2Color(Rgb(colors.Light2)),
            new D.Accent1Color(Rgb(colors.Accent1)),
            new D.Accent2Color(Rgb(colors.Accent2)),
            new D.Accent3Color(Rgb(colors.Accent3)),
            new D.Accent4Color(Rgb(colors.Accent4)),
            new D.Accent5Color(Rgb(colors.Accent5)),
            new D.Accent6Color(Rgb(colors.Accent6)),
            new D.Hyperlink(Rgb(colors.Hyperlink)),
            new D.FollowedHyperlinkColor(Rgb(colors.FollowedHyperlink)));

        static D.RgbColorModelHex Rgb(ArgbColor color) => new() { Val = SetShapeFillCommand.Hex(color) };
    }
}

/// <summary>Puts theme parts back exactly as they were captured.</summary>
sealed record RestoreThemesCommand(IReadOnlyList<(ThemePart Part, D.Theme? Theme)> Themes) : SlideCommand
{
    public override string Name => "Theme";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var inverse = new RestoreThemesCommand(this.Themes.Select(x => (x.Part, (D.Theme?)x.Part.Theme?.CloneNode(true))).ToList());

        foreach (var (part, theme) in this.Themes)
        {
            if (theme is not null)
                part.Theme = (D.Theme)theme.CloneNode(true);

            context.MarkPartDirty(part);
        }

        context.ReprojectAll();
        return inverse;
    }
}

/// <summary>A slide background the Format Background pane can set.</summary>
public sealed record SlideBackgroundSpec
{
    public SlideFillSpec? Fill { get; init; }

    /// <summary>An encoded picture to stretch over the slide.</summary>
    public byte[]? Picture { get; init; }

    public string PictureContentType { get; init; } = "image/png";

    public static SlideBackgroundSpec Solid(ArgbColor color) => new() { Fill = SlideFillSpec.Color(color) };

    public static SlideBackgroundSpec Gradient(ArgbColor from, ArgbColor to, double angle = 90) => new() { Fill = SlideFillSpec.LinearGradient(from, to, angle) };

    public static SlideBackgroundSpec Image(byte[] data, string contentType) => new() { Picture = data, PictureContentType = contentType };
}

/// <summary>
/// Writes <c>p:bg</c> on one slide, on several, or on a master or layout — or removes it so the slide
/// inherits its layout's again ("Reset Background").
/// </summary>
/// <remarks>
/// <c>p:bg</c> is the first child of <c>p:cSld</c>, and its <c>p:bgPr</c> must carry an effect list
/// after the fill even when it is empty — PowerPoint writes <c>&lt;a:effectLst/&gt;</c> and refuses a
/// background without one.
/// </remarks>
public sealed record SetSlideBackgroundCommand(IReadOnlyList<int> Slides, SlideBackgroundSpec? Background) : SlideCommand
{
    public override string Name => this.Background is null ? "Reset background" : "Format background";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var captured = new List<(OpenXmlPart, OpenXmlElement?)>();

        foreach (var index in this.Slides.Distinct())
        {
            if (context.PartAt(index) is not { Slide.CommonSlideData: { } data } part)
                continue;

            captured.Add((part, data.Background?.CloneNode(true)));
            Write(part, data, this.Background);
            context.Reproject(index);
        }

        return captured.Count == 0 ? new NoOpSlideCommand() : new RestoreBackgroundsCommand(captured);
    }

    internal static void Write(OpenXmlPart part, CommonSlideData data, SlideBackgroundSpec? spec)
    {
        data.Background?.Remove();

        if (spec is null)
            return;

        OpenXmlElement fill;
        if (spec.Picture is { Length: > 0 } picture)
        {
            var image = part switch
            {
                SlidePart slide => slide.AddImagePart(spec.PictureContentType),
                SlideLayoutPart layout => layout.AddImagePart(spec.PictureContentType),
                SlideMasterPart master => master.AddImagePart(spec.PictureContentType),
                _ => null
            };

            if (image is null)
                return;

            using (var stream = new MemoryStream(picture, writable: false))
                image.FeedData(stream);

            fill = new D.BlipFill(
                new D.Blip { Embed = part.GetIdOfPart(image) },
                new D.SourceRectangle(),
                new D.Stretch(new D.FillRectangle()))
            {
                Dpi = 0,
                RotateWithShape = true
            };
        }
        else
        {
            fill = (spec.Fill ?? SlideFillSpec.None).Build();
        }

        data.PrependChild(new Background(new BackgroundProperties(fill, new D.EffectList())));
    }
}

/// <summary>Puts each captured <c>p:bg</c> back — or removes it where there was none.</summary>
sealed record RestoreBackgroundsCommand(IReadOnlyList<(OpenXmlPart Part, OpenXmlElement? Background)> Backgrounds) : SlideCommand
{
    public override string Name => "Format background";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var inverse = new List<(OpenXmlPart, OpenXmlElement?)>();

        foreach (var (part, background) in this.Backgrounds)
        {
            var data = part switch
            {
                SlidePart slide => slide.Slide?.CommonSlideData,
                SlideLayoutPart layout => layout.SlideLayout?.CommonSlideData,
                SlideMasterPart master => master.SlideMaster?.CommonSlideData,
                _ => null
            };

            if (data is null)
                continue;

            inverse.Add((part, data.Background?.CloneNode(true)));
            data.Background?.Remove();
            if (background?.CloneNode(true) is { } restored)
                data.PrependChild(restored);

            context.MarkPartDirty(part);
        }

        context.ReprojectAll();
        return new RestoreBackgroundsCommand(inverse);
    }
}

/// <summary>
/// Changes the slide size — Design ▸ Slide Size — optionally scaling everything on every slide, layout
/// and master to the new size ("Ensure Fit").
/// </summary>
/// <remarks>
/// Scaling touches every transform in the deck, so the inverse is a snapshot of every part's root
/// rather than a reverse scale: EMU rounding would otherwise leave shapes a unit off after an undo.
/// </remarks>
public sealed record SetSlideSizeCommand(double Width, double Height, bool ScaleContent = true) : SlideCommand
{
    public override string Name => "Slide size";

    /// <summary>16:9 widescreen, PowerPoint's default: 13.333 x 7.5 inches.</summary>
    public static (double Width, double Height) Widescreen => (1280, 720);

    /// <summary>4:3 standard: 10 x 7.5 inches.</summary>
    public static (double Width, double Height) Standard => (960, 720);

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (context.PresentationRoot is not { } presentation || this.Width < 10 || this.Height < 10)
            return new NoOpSlideCommand();

        var oldWidth = context.SlideWidth;
        var oldHeight = context.SlideHeight;
        if (Math.Abs(oldWidth - this.Width) < 0.5 && Math.Abs(oldHeight - this.Height) < 0.5)
            return new NoOpSlideCommand();

        var snapshot = SlideDeckSnapshot.Capture(context);

        var size = presentation.SlideSize ??= new SlideSize();
        size.Cx = (int)OoxmlUnits.PixelsToEmu(this.Width);
        size.Cy = (int)OoxmlUnits.PixelsToEmu(this.Height);

        // The type names a preset; one left over from the old size would contradict the numbers.
        size.Type = Math.Abs(this.Width / this.Height - 4d / 3) < 0.01 ? SlideSizeValues.Screen4x3 : null;

        if (this.ScaleContent)
        {
            var sx = this.Width / oldWidth;
            var sy = this.Height / oldHeight;

            foreach (var root in SlideDeckSnapshot.Roots(context))
                Scale(root, sx, sy);
        }

        context.MarkStructureDirty();
        context.ApplySlideSize(this.Width, this.Height);
        return snapshot;
    }

    static void Scale(OpenXmlElement root, double sx, double sy)
    {
        foreach (var offset in root.Descendants<D.Offset>().ToList())
        {
            offset.X = (long)Math.Round((offset.X?.Value ?? 0) * sx);
            offset.Y = (long)Math.Round((offset.Y?.Value ?? 0) * sy);
        }

        foreach (var extents in root.Descendants<D.Extents>().ToList())
        {
            extents.Cx = (long)Math.Round((extents.Cx?.Value ?? 0) * sx);
            extents.Cy = (long)Math.Round((extents.Cy?.Value ?? 0) * sy);
        }

        // A group's child space scales with it, or its members would be scaled twice.
        foreach (var offset in root.Descendants<D.ChildOffset>().ToList())
        {
            offset.X = (long)Math.Round((offset.X?.Value ?? 0) * sx);
            offset.Y = (long)Math.Round((offset.Y?.Value ?? 0) * sy);
        }

        foreach (var extents in root.Descendants<D.ChildExtents>().ToList())
        {
            extents.Cx = (long)Math.Round((extents.Cx?.Value ?? 0) * sx);
            extents.Cy = (long)Math.Round((extents.Cy?.Value ?? 0) * sy);
        }
    }
}

/// <summary>Every slide, layout and master root plus the slide size, captured so a deck-wide edit undoes exactly.</summary>
sealed record SlideDeckSnapshot(IReadOnlyList<(OpenXmlPart Part, OpenXmlElement Root)> Parts, double Width, double Height, OpenXmlElement? Size) : SlideCommand
{
    public override string Name => "Slide size";

    public static IEnumerable<OpenXmlElement> Roots(SlideDeck deck)
    {
        foreach (var master in deck.PresentationPart.SlideMasterParts)
        {
            if (master.SlideMaster is { } m)
                yield return m;

            foreach (var layout in master.SlideLayoutParts)
            {
                if (layout.SlideLayout is { } l)
                    yield return l;
            }
        }

        foreach (var slide in deck.PresentationPart.SlideParts)
        {
            if (slide.Slide is { } s)
                yield return s;
        }
    }

    public static SlideDeckSnapshot Capture(SlideDeck deck)
    {
        var parts = new List<(OpenXmlPart, OpenXmlElement)>();

        foreach (var master in deck.PresentationPart.SlideMasterParts)
        {
            if (master.SlideMaster is { } m)
                parts.Add((master, m.CloneNode(true)));

            foreach (var layout in master.SlideLayoutParts)
            {
                if (layout.SlideLayout is { } l)
                    parts.Add((layout, l.CloneNode(true)));
            }
        }

        foreach (var slide in deck.PresentationPart.SlideParts)
        {
            if (slide.Slide is { } s)
                parts.Add((slide, s.CloneNode(true)));
        }

        return new SlideDeckSnapshot(parts, deck.SlideWidth, deck.SlideHeight, deck.PresentationRoot?.SlideSize?.CloneNode(true));
    }

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var inverse = Capture(context);

        foreach (var (part, root) in this.Parts)
        {
            switch (part)
            {
                case SlidePart slide: slide.Slide = (DocumentFormat.OpenXml.Presentation.Slide)root.CloneNode(true); break;
                case SlideLayoutPart layout: layout.SlideLayout = (SlideLayout)root.CloneNode(true); break;
                case SlideMasterPart master: master.SlideMaster = (SlideMaster)root.CloneNode(true); break;
            }

            context.MarkPartDirty(part);
        }

        if (context.PresentationRoot is { } presentation && this.Size?.CloneNode(true) is SlideSize size)
        {
            if (presentation.SlideSize is { } existing)
                presentation.ReplaceChild(size, existing);
            else
                presentation.SlideSize = size;
        }

        context.MarkStructureDirty();
        context.ApplySlideSize(this.Width, this.Height);
        return inverse;
    }
}

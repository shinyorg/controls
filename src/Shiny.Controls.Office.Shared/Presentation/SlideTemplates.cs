using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Shiny.Controls.Office.Shell;
using Shiny.Controls.Office.Spreadsheet;
using D = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;

namespace Shiny.Controls.Office.Presentation;

/// <summary>
/// The decks the backstage's New page offers — a blank presentation and three built in code on the
/// built-in themes — so the package ships no binary assets.
/// </summary>
/// <remarks>
/// Each is a real <c>.pptx</c>: a master carrying the theme, PowerPoint's own five layouts (Title
/// Slide, Title and Content, Section Header, Title Only, Blank) and slides whose placeholders carry
/// only text, so position and style come from the layout — New Slide, Layout and Reset all behave as
/// they do in a deck made by PowerPoint.
/// </remarks>
public static class SlideTemplates
{
    public const string BlankId = "blank";
    public const string ProjectId = "ppt-project";
    public const string PitchId = "ppt-pitch";
    public const string LessonId = "ppt-lesson";

    /// <summary>Blank, Project update, Pitch deck and Lesson, each with an <see cref="OfficeTemplate.Open"/> that writes the file.</summary>
    public static IReadOnlyList<OfficeTemplate> All { get; } =
    [
        new OfficeTemplate(BlankId, "Blank presentation")
        {
            IsBlank = true,
            Category = "Featured",
            Open = _ => Task.FromResult<Stream>(Create(BlankId))
        },
        new OfficeTemplate(ProjectId, "Project update")
        {
            Description = "Title and content slides for a status review, on the Slate theme",
            Category = "Featured",
            Open = _ => Task.FromResult<Stream>(Create(ProjectId))
        },
        new OfficeTemplate(PitchId, "Pitch deck")
        {
            Description = "Problem, solution, market, model, team and the ask, on the Midnight theme",
            Category = "Featured",
            Open = _ => Task.FromResult<Stream>(Create(PitchId))
        },
        new OfficeTemplate(LessonId, "Lesson")
        {
            Description = "Objectives, a concept, an example and practice, on the Botanical theme",
            Category = "Featured",
            Open = _ => Task.FromResult<Stream>(Create(LessonId))
        }
    ];


    /// <summary>Opens a template as an editable deck — the template's own <see cref="OfficeTemplate.Open"/>, or blank.</summary>
    public static async Task<SlideDeck> OpenAsync(OfficeTemplate? template, CancellationToken cancellationToken = default)
    {
        Stream stream = template?.Open is { } open
            ? await open(cancellationToken).ConfigureAwait(false)
            : Create(BlankId);

        await using (stream.ConfigureAwait(false))
            return await SlideDeck.OpenAsync(stream, editable: true, cancellationToken: cancellationToken).ConfigureAwait(false);
    }


    /// <summary>Writes the template with this id (blank for an unknown id) to a new, rewound stream.</summary>
    public static MemoryStream Create(string id)
    {
        var (theme, slides) = id switch
        {
            ProjectId => (Theme("Slate"), ProjectSlides()),
            PitchId => (Theme("Midnight"), PitchSlides()),
            LessonId => (Theme("Botanical"), LessonSlides()),
            _ => (SlideThemeDefinition.BuiltIn[0], BlankSlides())
        };

        return Write(theme, slides);
    }

    static SlideThemeDefinition Theme(string name)
        => SlideThemeDefinition.BuiltIn.FirstOrDefault(x => x.Name == name) ?? SlideThemeDefinition.BuiltIn[0];


    // ---- content ----

    enum Kind { Title, Content, Section, TitleOnly, Blank }

    /// <summary>One slide: its layout, its title, and its body lines (a leading tab per outline level).</summary>
    sealed record TemplateSlide(Kind Layout, string? Title, string[] Body, string? Notes = null);

    static IEnumerable<TemplateSlide> BlankSlides()
    {
        yield return new(Kind.Title, null, []);
    }

    static IEnumerable<TemplateSlide> ProjectSlides()
    {
        yield return new(Kind.Title, "Project Update", ["Status, milestones and next steps"],
            "Welcome everyone. This update covers where the project stands and what we need from this group.");
        yield return new(Kind.Content, "Agenda", ["Where we are", "What we delivered", "Risks and issues", "Next steps"]);
        yield return new(Kind.Section, "Progress", ["What changed since the last update"]);
        yield return new(Kind.Content, "Milestones",
            ["Discovery complete", "Design signed off", "\tTwo review rounds with stakeholders", "Build 60% complete", "\tOn track for the beta date"],
            "Call out the design sign-off - it unblocked the build team two weeks early.");
        yield return new(Kind.Content, "Risks and issues",
            ["Vendor API changes", "\tMitigation: integration tests against the sandbox", "Two open roles on the team", "Scope requests after the freeze"]);
        yield return new(Kind.Content, "Next steps",
            ["Finish the build", "Start the beta with ten customers", "Review again in four weeks"]);
    }

    static IEnumerable<TemplateSlide> PitchSlides()
    {
        yield return new(Kind.Title, "Company Name", ["One line that says what you do and for whom"],
            "Open with the one line. If they remember nothing else, they remember this.");
        yield return new(Kind.Content, "The problem",
            ["Who has it, and how often", "What it costs them today", "Why the current answers fall short"]);
        yield return new(Kind.Content, "Our solution",
            ["What the product does", "\tThe moment a customer gets it", "Why now"]);
        yield return new(Kind.Content, "Market",
            ["Total addressable market", "Serviceable market", "Who we win first"]);
        yield return new(Kind.Content, "Business model",
            ["How we charge", "Unit economics", "\tAcquisition cost and lifetime value"]);
        yield return new(Kind.Content, "Traction",
            ["Customers and revenue", "Growth month over month", "Proof points and logos"]);
        yield return new(Kind.Content, "Team",
            ["Founders and why they are the ones to do this", "Key hires", "Advisors"]);
        yield return new(Kind.Section, "The ask", ["How much we are raising, and what it buys"],
            "Be specific: the amount, the runway it gives us, and the milestones it reaches.");
    }

    static IEnumerable<TemplateSlide> LessonSlides()
    {
        yield return new(Kind.Title, "Lesson Title", ["Course name - Week 1"]);
        yield return new(Kind.Content, "Learning objectives",
            ["By the end of this lesson you will be able to:", "\tExplain the key concept", "\tApply it to an example", "\tCheck your own work"]);
        yield return new(Kind.Section, "Key concept", ["The one idea this lesson is built around"]);
        yield return new(Kind.Content, "Worked example",
            ["Start from the question", "Take it one step at a time", "\tSay why each step follows", "Check the answer"],
            "Work through this live rather than reading the slide.");
        yield return new(Kind.Content, "Practice", ["Try the three exercises on the handout", "Compare answers with a partner"]);
        yield return new(Kind.Content, "Summary", ["What we covered", "What comes next week", "Questions"]);
    }


    // ---- writing ----

    const long SlideCx = 12192000;
    const long SlideCy = 6858000;

    static MemoryStream Write(SlideThemeDefinition theme, IEnumerable<TemplateSlide> slides)
    {
        var output = new MemoryStream();

        using (var document = PresentationDocument.Create(output, PresentationDocumentType.Presentation, autoSave: false))
        {
            var presentationPart = document.AddPresentationPart();
            presentationPart.Presentation = new P.Presentation();

            var masterPart = presentationPart.AddNewPart<SlideMasterPart>();
            masterPart.SlideMaster = BuildMaster();

            var themePart = masterPart.AddNewPart<ThemePart>();
            themePart.Theme = BuildTheme(theme);

            var layouts = new Dictionary<Kind, SlideLayoutPart>();
            var layoutIds = new List<P.SlideLayoutId>();
            uint layoutId = 2147483649U;

            foreach (var kind in new[] { Kind.Title, Kind.Content, Kind.Section, Kind.TitleOnly, Kind.Blank })
            {
                var layoutPart = masterPart.AddNewPart<SlideLayoutPart>();
                layoutPart.SlideLayout = BuildLayout(kind);
                layoutPart.AddPart(masterPart);
                layouts[kind] = layoutPart;
                layoutIds.Add(new P.SlideLayoutId { Id = layoutId++, RelationshipId = masterPart.GetIdOfPart(layoutPart) });
            }

            masterPart.SlideMaster.SlideLayoutIdList = new P.SlideLayoutIdList(layoutIds.Cast<OpenXmlElement>().ToArray());

            var ids = new List<P.SlideId>();
            uint id = 256;

            foreach (var slide in slides)
            {
                var slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.Slide = BuildSlide(slide);
                slidePart.AddPart(layouts[slide.Layout]);

                if (!string.IsNullOrWhiteSpace(slide.Notes))
                    slidePart.AddNewPart<NotesSlidePart>().NotesSlide = BuildNotes(slide.Notes);

                ids.Add(new P.SlideId { Id = id++, RelationshipId = presentationPart.GetIdOfPart(slidePart) });
            }

            presentationPart.Presentation.SlideMasterIdList = new P.SlideMasterIdList(
                new P.SlideMasterId { Id = 2147483648U, RelationshipId = presentationPart.GetIdOfPart(masterPart) });
            presentationPart.Presentation.SlideIdList = new P.SlideIdList(ids.Cast<OpenXmlElement>().ToArray());
            presentationPart.Presentation.SlideSize = new P.SlideSize { Cx = (int)SlideCx, Cy = (int)SlideCy };
            presentationPart.Presentation.NotesSize = new P.NotesSize { Cx = 6858000, Cy = 9144000 };

            presentationPart.Presentation.Save();
            document.Save();
        }

        output.Position = 0;
        return output;
    }


    static string Hex(ArgbColor color) => $"{color.R:X2}{color.G:X2}{color.B:X2}";

    static D.Theme BuildTheme(SlideThemeDefinition theme)
    {
        var c = theme.Colors;

        D.SolidFill Scheme() => new(new D.SchemeColor { Val = D.SchemeColorValues.PhColor });

        return new D.Theme(
            new D.ThemeElements(
                new D.ColorScheme(
                    new D.Dark1Color(new D.RgbColorModelHex { Val = Hex(c.Dark1) }),
                    new D.Light1Color(new D.RgbColorModelHex { Val = Hex(c.Light1) }),
                    new D.Dark2Color(new D.RgbColorModelHex { Val = Hex(c.Dark2) }),
                    new D.Light2Color(new D.RgbColorModelHex { Val = Hex(c.Light2) }),
                    new D.Accent1Color(new D.RgbColorModelHex { Val = Hex(c.Accent1) }),
                    new D.Accent2Color(new D.RgbColorModelHex { Val = Hex(c.Accent2) }),
                    new D.Accent3Color(new D.RgbColorModelHex { Val = Hex(c.Accent3) }),
                    new D.Accent4Color(new D.RgbColorModelHex { Val = Hex(c.Accent4) }),
                    new D.Accent5Color(new D.RgbColorModelHex { Val = Hex(c.Accent5) }),
                    new D.Accent6Color(new D.RgbColorModelHex { Val = Hex(c.Accent6) }),
                    new D.Hyperlink(new D.RgbColorModelHex { Val = Hex(c.Hyperlink) }),
                    new D.FollowedHyperlinkColor(new D.RgbColorModelHex { Val = Hex(c.FollowedHyperlink) }))
                { Name = c.Name },
                new D.FontScheme(
                    new D.MajorFont(new D.LatinFont { Typeface = theme.MajorFont }, new D.EastAsianFont { Typeface = string.Empty }, new D.ComplexScriptFont { Typeface = string.Empty }),
                    new D.MinorFont(new D.LatinFont { Typeface = theme.MinorFont }, new D.EastAsianFont { Typeface = string.Empty }, new D.ComplexScriptFont { Typeface = string.Empty }))
                { Name = theme.Name },

                // PowerPoint wants three of each style; every one is the placeholder colour, which is
                // all the templates' shapes need.
                new D.FormatScheme(
                    new D.FillStyleList(Scheme(), Scheme(), Scheme()),
                    new D.LineStyleList(
                        new D.Outline(Scheme()) { Width = 6350 },
                        new D.Outline(Scheme()) { Width = 12700 },
                        new D.Outline(Scheme()) { Width = 19050 }),
                    new D.EffectStyleList(new D.EffectStyle(new D.EffectList()), new D.EffectStyle(new D.EffectList()), new D.EffectStyle(new D.EffectList())),
                    new D.BackgroundFillStyleList(Scheme(), Scheme(), Scheme()))
                { Name = theme.Name }))
        { Name = theme.Name };
    }

    static OpenXmlElement[] TreeHeader() =>
    [
        new P.NonVisualGroupShapeProperties(
            new P.NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
            new P.NonVisualGroupShapeDrawingProperties(),
            new P.ApplicationNonVisualDrawingProperties()),
        new P.GroupShapeProperties(new D.TransformGroup(
            new D.Offset { X = 0, Y = 0 }, new D.Extents { Cx = 0, Cy = 0 },
            new D.ChildOffset { X = 0, Y = 0 }, new D.ChildExtents { Cx = 0, Cy = 0 }))
    ];

    static D.SolidFill SchemeFill(D.SchemeColorValues value) => new(new D.SchemeColor { Val = value });

    static D.LatinFont ThemeFont(bool major) => new() { Typeface = major ? "+mj-lt" : "+mn-lt" };

    static P.SlideMaster BuildMaster() => new(
        new P.CommonSlideData(
            new P.Background(new P.BackgroundProperties(SchemeFill(D.SchemeColorValues.Background1), new D.EffectList())),
            new P.ShapeTree(
                [
                    .. TreeHeader(),
                    Placeholder(2, "Title Placeholder 1", P.PlaceholderValues.Title, null, 838200, 365125, 10515600, 1325563, D.TextAnchoringTypeValues.Center, null),
                    Placeholder(3, "Text Placeholder 2", P.PlaceholderValues.Body, 1U, 838200, 1825625, 10515600, 4351338, D.TextAnchoringTypeValues.Top, null),

                    // The theme's signature: a thin accent band along the foot of every slide.
                    Band(4, "Accent band", 0, SlideCy - 91440, SlideCx, 91440, D.SchemeColorValues.Accent1)
                ])),
        new P.ColorMap
        {
            Background1 = D.ColorSchemeIndexValues.Light1,
            Text1 = D.ColorSchemeIndexValues.Dark1,
            Background2 = D.ColorSchemeIndexValues.Light2,
            Text2 = D.ColorSchemeIndexValues.Dark2,
            Accent1 = D.ColorSchemeIndexValues.Accent1,
            Accent2 = D.ColorSchemeIndexValues.Accent2,
            Accent3 = D.ColorSchemeIndexValues.Accent3,
            Accent4 = D.ColorSchemeIndexValues.Accent4,
            Accent5 = D.ColorSchemeIndexValues.Accent5,
            Accent6 = D.ColorSchemeIndexValues.Accent6,
            Hyperlink = D.ColorSchemeIndexValues.Hyperlink,
            FollowedHyperlink = D.ColorSchemeIndexValues.FollowedHyperlink
        },
        new P.TextStyles(
            new P.TitleStyle(new D.Level1ParagraphProperties(
                new D.LineSpacing(new D.SpacingPercent { Val = 90000 }),
                new D.NoBullet(),
                new D.DefaultRunProperties(SchemeFill(D.SchemeColorValues.Text1), ThemeFont(major: true)) { FontSize = 4400 })
            { Alignment = D.TextAlignmentTypeValues.Left }),
            new P.BodyStyle(
                new D.Level1ParagraphProperties(
                    new D.SpaceBefore(new D.SpacingPoints { Val = 1000 }),
                    new D.CharacterBullet { Char = "•" },
                    new D.DefaultRunProperties(SchemeFill(D.SchemeColorValues.Text1), ThemeFont(major: false)) { FontSize = 2800 })
                { LeftMargin = 228600, Indent = -228600 },
                new D.Level2ParagraphProperties(
                    new D.SpaceBefore(new D.SpacingPoints { Val = 500 }),
                    new D.CharacterBullet { Char = "•" },
                    new D.DefaultRunProperties(SchemeFill(D.SchemeColorValues.Text1), ThemeFont(major: false)) { FontSize = 2400 })
                { LeftMargin = 685800, Indent = -228600 },
                new D.Level3ParagraphProperties(
                    new D.SpaceBefore(new D.SpacingPoints { Val = 500 }),
                    new D.CharacterBullet { Char = "•" },
                    new D.DefaultRunProperties(SchemeFill(D.SchemeColorValues.Text1), ThemeFont(major: false)) { FontSize = 2000 })
                { LeftMargin = 1143000, Indent = -228600 }),
            new P.OtherStyle(new D.Level1ParagraphProperties(
                new D.DefaultRunProperties(SchemeFill(D.SchemeColorValues.Text1), ThemeFont(major: false)) { FontSize = 1800 }))));

    static P.SlideLayout BuildLayout(Kind kind)
    {
        var (name, type) = kind switch
        {
            Kind.Title => ("Title Slide", P.SlideLayoutValues.Title),
            Kind.Content => ("Title and Content", P.SlideLayoutValues.Object),
            Kind.Section => ("Section Header", P.SlideLayoutValues.SectionHeader),
            Kind.TitleOnly => ("Title Only", P.SlideLayoutValues.TitleOnly),
            _ => ("Blank", P.SlideLayoutValues.Blank)
        };

        var shapes = new List<OpenXmlElement>(TreeHeader());
        switch (kind)
        {
            case Kind.Title:
                shapes.Add(Placeholder(2, "Title 1", P.PlaceholderValues.CenteredTitle, null, 1524000, 1122363, 9144000, 2387600,
                    D.TextAnchoringTypeValues.Bottom, Centered(6000, noBullet: true)));
                shapes.Add(Placeholder(3, "Subtitle 2", P.PlaceholderValues.SubTitle, 1U, 1524000, 3602038, 9144000, 1655762,
                    D.TextAnchoringTypeValues.Top, Centered(2400, noBullet: true)));
                break;

            case Kind.Content:
                shapes.Add(Placeholder(2, "Title 1", P.PlaceholderValues.Title, null, 838200, 365125, 10515600, 1325563, D.TextAnchoringTypeValues.Center, null));
                shapes.Add(Placeholder(3, "Content Placeholder 2", null, 1U, 838200, 1825625, 10515600, 4351338, D.TextAnchoringTypeValues.Top, null));
                break;

            case Kind.Section:
                shapes.Add(Placeholder(2, "Title 1", P.PlaceholderValues.Title, null, 831850, 1709738, 10515600, 2852737,
                    D.TextAnchoringTypeValues.Bottom, Sized(6000)));
                shapes.Add(Placeholder(3, "Text Placeholder 2", P.PlaceholderValues.Body, 1U, 831850, 4589463, 10515600, 1500187,
                    D.TextAnchoringTypeValues.Top, Sized(2400, noBullet: true)));
                break;

            case Kind.TitleOnly:
                shapes.Add(Placeholder(2, "Title 1", P.PlaceholderValues.Title, null, 838200, 365125, 10515600, 1325563, D.TextAnchoringTypeValues.Center, null));
                break;
        }

        return new P.SlideLayout(
            new P.CommonSlideData(new P.ShapeTree(shapes)) { Name = name },
            new P.ColorMapOverride(new D.MasterColorMapping()))
        { Type = type, Preserve = true };
    }

    /// <summary>
    /// The text colour, stated on the placeholder as well as the master's text styles: a reader that
    /// stops at the layout would otherwise draw black text on Midnight's navy. Ours walks the whole
    /// chain to the master now, but other readers of the saved file may not.
    /// </summary>
    /// <remarks>A body placeholder states its bullets here too, for the same reason.</remarks>
    static D.ListStyle Inked(bool bulleted)
    {
        // Sizes too: a placeholder's own list style replaces the master's for the levels it names.
        OpenXmlElement[] Run(int size) => bulleted
            ? [new D.CharacterBullet { Char = "•" }, new D.DefaultRunProperties(SchemeFill(D.SchemeColorValues.Text1)) { FontSize = size }]
            : [new D.DefaultRunProperties(SchemeFill(D.SchemeColorValues.Text1)) { FontSize = size }];

        return bulleted
            ? new D.ListStyle(
                new D.Level1ParagraphProperties(Run(2800)) { LeftMargin = 228600, Indent = -228600 },
                new D.Level2ParagraphProperties(Run(2400)) { LeftMargin = 685800, Indent = -228600 },
                new D.Level3ParagraphProperties(Run(2000)) { LeftMargin = 1143000, Indent = -228600 })
            : new D.ListStyle(new D.Level1ParagraphProperties(Run(4400)));
    }

    static D.ListStyle Centered(int size, bool noBullet)
    {
        var level = new D.Level1ParagraphProperties { Alignment = D.TextAlignmentTypeValues.Center, LeftMargin = 0, Indent = 0 };
        if (noBullet)
            level.AppendChild(new D.NoBullet());

        level.AppendChild(new D.DefaultRunProperties(SchemeFill(D.SchemeColorValues.Text1)) { FontSize = size });
        return new D.ListStyle(level);
    }

    static D.ListStyle Sized(int size, bool noBullet = false)
    {
        var level = new D.Level1ParagraphProperties();
        if (noBullet)
        {
            level.LeftMargin = 0;
            level.Indent = 0;
            level.AppendChild(new D.NoBullet());
        }

        level.AppendChild(new D.DefaultRunProperties(SchemeFill(D.SchemeColorValues.Text1)) { FontSize = size });
        return new D.ListStyle(level);
    }

    static P.Shape Placeholder(uint id, string name, P.PlaceholderValues? type, uint? index, long x, long y, long cx, long cy, D.TextAnchoringTypeValues anchor, D.ListStyle? listStyle)
    {
        var placeholder = new P.PlaceholderShape();
        if (type is { } t)
            placeholder.Type = t;

        if (index is { } i)
            placeholder.Index = i;

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(new D.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties(placeholder)),
            new P.ShapeProperties(
                new D.Transform2D(new D.Offset { X = x, Y = y }, new D.Extents { Cx = cx, Cy = cy }),
                new D.PresetGeometry(new D.AdjustValueList()) { Preset = D.ShapeTypeValues.Rectangle }),
            new P.TextBody(
                new D.BodyProperties { Anchor = anchor, Wrap = D.TextWrappingValues.Square },
                listStyle ?? Inked(bulleted: type is null || type == P.PlaceholderValues.Body),
                new D.Paragraph(new D.EndParagraphRunProperties { Language = "en-US" })));
    }

    static P.Shape Band(uint id, string name, long x, long y, long cx, long cy, D.SchemeColorValues color) => new(
        new P.NonVisualShapeProperties(
            new P.NonVisualDrawingProperties { Id = id, Name = name },
            new P.NonVisualShapeDrawingProperties(),
            new P.ApplicationNonVisualDrawingProperties()),
        new P.ShapeProperties(
            new D.Transform2D(new D.Offset { X = x, Y = y }, new D.Extents { Cx = cx, Cy = cy }),
            new D.PresetGeometry(new D.AdjustValueList()) { Preset = D.ShapeTypeValues.Rectangle },
            SchemeFill(color),
            new D.Outline(new D.NoFill())),
        new P.TextBody(new D.BodyProperties(), new D.ListStyle(), new D.Paragraph()));

    static P.Slide BuildSlide(TemplateSlide slide)
    {
        var shapes = new List<OpenXmlElement>(TreeHeader());

        switch (slide.Layout)
        {
            case Kind.Title:
                shapes.Add(Text(2, "Title 1", P.PlaceholderValues.CenteredTitle, null, slide.Title is null ? [] : [slide.Title]));
                shapes.Add(Text(3, "Subtitle 2", P.PlaceholderValues.SubTitle, 1U, slide.Body));
                break;

            case Kind.Content:
                shapes.Add(Text(2, "Title 1", P.PlaceholderValues.Title, null, slide.Title is null ? [] : [slide.Title]));
                shapes.Add(Text(3, "Content Placeholder 2", null, 1U, slide.Body));
                break;

            case Kind.Section:
                shapes.Add(Text(2, "Title 1", P.PlaceholderValues.Title, null, slide.Title is null ? [] : [slide.Title]));
                shapes.Add(Text(3, "Text Placeholder 2", P.PlaceholderValues.Body, 1U, slide.Body));
                break;

            case Kind.TitleOnly:
                shapes.Add(Text(2, "Title 1", P.PlaceholderValues.Title, null, slide.Title is null ? [] : [slide.Title]));
                break;
        }

        return new P.Slide(
            new P.CommonSlideData(new P.ShapeTree(shapes)),
            new P.ColorMapOverride(new D.MasterColorMapping()));
    }

    /// <summary>A placeholder carrying only text — geometry and style come from the layout.</summary>
    static P.Shape Text(uint id, string name, P.PlaceholderValues? type, uint? index, IReadOnlyList<string> lines)
    {
        var placeholder = new P.PlaceholderShape();
        if (type is { } t)
            placeholder.Type = t;

        if (index is { } i)
            placeholder.Index = i;

        var body = new P.TextBody(new D.BodyProperties(), new D.ListStyle());

        if (lines.Count == 0)
            body.AppendChild(new D.Paragraph(new D.EndParagraphRunProperties { Language = "en-US" }));

        foreach (var line in lines)
        {
            var level = line.TakeWhile(x => x == '\t').Count();
            var paragraph = new D.Paragraph();
            if (level > 0)
                paragraph.AppendChild(new D.ParagraphProperties { Level = level });

            paragraph.AppendChild(new D.Run(new D.RunProperties { Language = "en-US", Dirty = false }, new D.Text(line[level..])));
            body.AppendChild(paragraph);
        }

        return new P.Shape(
            new P.NonVisualShapeProperties(
                new P.NonVisualDrawingProperties { Id = id, Name = name },
                new P.NonVisualShapeDrawingProperties(new D.ShapeLocks { NoGrouping = true }),
                new P.ApplicationNonVisualDrawingProperties(placeholder)),
            new P.ShapeProperties(),
            body);
    }

    static P.NotesSlide BuildNotes(string text) => new(
        new P.CommonSlideData(
            new P.ShapeTree(
                [
                    .. TreeHeader(),
                    new P.Shape(
                        new P.NonVisualShapeProperties(
                            new P.NonVisualDrawingProperties { Id = 2U, Name = "Notes Placeholder 1" },
                            new P.NonVisualShapeDrawingProperties(),
                            new P.ApplicationNonVisualDrawingProperties(new P.PlaceholderShape { Type = P.PlaceholderValues.Body, Index = 1U })),
                        new P.ShapeProperties(),
                        new P.TextBody(
                            new D.BodyProperties(),
                            new D.ListStyle(),
                            new D.Paragraph(new D.Run(new D.RunProperties { Language = "en-US" }, new D.Text(text)))))
                ])),
        new P.ColorMapOverride(new D.MasterColorMapping()));
}

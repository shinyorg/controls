using Shiny.Controls.Office.Notebook;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;

// MAUI has a TextAlignment of its own and the sample sees both.
using TextAlignment = Shiny.Controls.Office.Text.TextAlignment;

namespace Sample.Features.Office;

/// <summary>
/// A notebook to open the sample on, built in code.
/// </summary>
/// <remarks>
/// Built rather than shipped as a <c>.shinynote</c> file for the same reason the sample deck and
/// workbook are: a binary fixture in the repo is one nobody can review, and a page whose contents are
/// written out here is also a worked example of the model's own API.
/// </remarks>
public static class SampleNotebook
{
    static readonly ArgbColor Purple = new(255, 0x7A, 0x33, 0x8C);
    static readonly ArgbColor Teal = new(255, 0x0F, 0x76, 0x6E);

    /// <summary>
    /// Blue pen.
    /// </summary>
    /// <remarks>
    /// A colour rather than near-black, because ink is content and the painter never recolours it —
    /// a stroke laid down in black stays black when the app flips to dark, where it vanishes. Blue is
    /// both what somebody would actually pick up and a value that reads on either ground.
    /// </remarks>
    static readonly ArgbColor PenInk = new(255, 0x2F, 0x6F, 0xED);

    /// <summary>
    /// A caption grey chosen to read on both grounds.
    /// </summary>
    /// <remarks>
    /// A colour set here is a colour the author chose, and the painter honours those as-is — only text
    /// left at the model's default follows the theme. So a subtitle wanting to be quieter than the
    /// body has to pick a value that works on paper and on a dark page alike.
    /// </remarks>
    static readonly ArgbColor Muted = new(255, 0x8A, 0x93, 0xA3);

    public static NotebookDocument Build()
    {
        var document = NotebookDocument.Create("Field notebook");

        var first = document.Sections[0];
        first.Title = "Kick-off";
        first.Color = Purple;

        var meeting = first.Pages[0];
        meeting.Title = "Project kick-off";
        meeting.Rule = PageRule.Lines;
        BuildMeetingPage(meeting);

        var sketch = new NotebookPage(NotebookDocument.NewId(), "Whiteboard sketch")
        {
            Rule = PageRule.Grid,
            RuleSpacing = 28
        };

        BuildSketchPage(sketch);
        first.Pages.Add(sketch);

        var research = new NotebookSection(NotebookDocument.NewId(), "Research") { Color = Teal };
        var reading = new NotebookPage(NotebookDocument.NewId(), "Reading list") { Rule = PageRule.Dots };
        BuildReadingPage(reading);
        research.Pages.Add(reading);
        document.Sections.Add(research);

        return document;
    }

    static void BuildMeetingPage(NotebookPage page)
    {
        page.Items.Add(Text(60, 60, 520, "Project kick-off", 26, bold: true, color: Purple));

        page.Items.Add(Text(60, 110, 520, "Tuesday · Room 3 · everyone in", 13, color: Muted));

        page.Items.Add(Bulleted(60, 160, 480,
        [
            "Agree the shape of the first milestone",
            "Pick the two risks worth tracking",
            "Book the follow-up before anyone leaves"
        ]));

        page.Items.Add(Numbered(60, 300, 480,
        [
            "Draft the scope note",
            "Circulate for comment",
            "Lock it on Friday"
        ]));

        // A callout, off to the side where a note actually gets written.
        page.Items.Add(NotebookDocument.NewShapeItem(
            ShapeGeometry.RoundedRectangle, 620, 160, 260, 120,
            new ArgbColor(40, 0x7A, 0x33, 0x8C), Purple) with
        {
            Text = new ShapeTextBody([Run("Decision: ship the narrow version first.", 14)])
            {
                Anchor = TextAnchor.Middle,
                InsetLeft = 14,
                InsetRight = 14
            }
        });

        // Highlighter over the first bullet, and a pen circle round the deadline - the two things ink
        // is actually used for on a page like this.
        page.Items.Add(Stroke(Highlighter(64, 176, 420), new ArgbColor(110, 0xFF, 0xE0, 0x3B), 16, InkTool.Highlighter));
        page.Items.Add(Stroke(Ellipse(600, 140, 300, 160, 44), new ArgbColor(255, 0xD1, 0x3A, 0x3A), 2.4, InkTool.Pen));
    }

    static void BuildSketchPage(NotebookPage page)
    {
        page.Items.Add(Text(60, 50, 480, "Whiteboard sketch", 22, bold: true));

        page.Items.Add(NotebookDocument.NewShapeItem(
            ShapeGeometry.RoundedRectangle, 80, 140, 200, 90, new ArgbColor(255, 0xE8, 0xF2, 0xFF), new ArgbColor(255, 0x2F, 0x6F, 0xED)) with
        {
            Text = new ShapeTextBody([Run("Capture", 15, alignment: TextAlignment.Center)]) { Anchor = TextAnchor.Middle }
        });

        page.Items.Add(NotebookDocument.NewShapeItem(
            ShapeGeometry.RoundedRectangle, 360, 140, 200, 90, new ArgbColor(255, 0xE9, 0xF7, 0xEF), Teal) with
        {
            Text = new ShapeTextBody([Run("Review", 15, alignment: TextAlignment.Center)]) { Anchor = TextAnchor.Middle }
        });

        page.Items.Add(NotebookDocument.NewShapeItem(
            ShapeGeometry.Diamond, 640, 130, 180, 110, new ArgbColor(255, 0xFD, 0xF2, 0xE3), new ArgbColor(255, 0xC4, 0x7A, 0x1C)) with
        {
            Text = new ShapeTextBody([Run("Ship?", 15, alignment: TextAlignment.Center)]) { Anchor = TextAnchor.Middle }
        });

        page.Items.Add(NotebookDocument.NewShapeItem(ShapeGeometry.RightArrow, 288, 172, 64, 26, new ArgbColor(255, 0x9C, 0xA3, 0xAF), null));
        page.Items.Add(NotebookDocument.NewShapeItem(ShapeGeometry.RightArrow, 568, 172, 64, 26, new ArgbColor(255, 0x9C, 0xA3, 0xAF), null));

        page.Items.Add(Text(80, 290, 420, "…and the bit nobody has drawn yet:", 14));
        page.Items.Add(Stroke(Squiggle(90, 330, 420, 34), PenInk, 2.2, InkTool.Pen));
    }

    static void BuildReadingPage(NotebookPage page)
    {
        page.Items.Add(Text(60, 60, 520, "Reading list", 24, bold: true, color: Teal));

        page.Items.Add(Bulleted(60, 120, 520,
        [
            "Tufte — Visual Display of Quantitative Information",
            "Norman — The Design of Everyday Things",
            "Wroblewski — Mobile First"
        ]));

        page.Items.Add(Text(60, 270, 520, "Annotate as you go; a lasso will pick a stroke back up.", 13,
            color: Muted));
    }

    // ---- builders ----

    static NoteItem Text(
        double x,
        double y,
        double width,
        string text,
        double size,
        bool bold = false,
        ArgbColor? color = null,
        TextAlignment alignment = TextAlignment.Left)
        => NotebookDocument.NewTextItem(x, y, width) with
        {
            Text = new ShapeTextBody([Run(text, size, bold, color, alignment)])
            {
                InsetLeft = 4,
                InsetRight = 4,
                InsetTop = 3,
                InsetBottom = 3
            },

            // Tall enough to hold one line at this size; the editor re-measures it the moment anyone
            // types into it, so this only has to be close.
            Height = size * 1.6
        };

    static NoteItem Bulleted(double x, double y, double width, string[] lines)
        => ListItem(x, y, width, lines, ListStyle.Bullet);

    static NoteItem Numbered(double x, double y, double width, string[] lines)
        => ListItem(x, y, width, lines, ListStyle.Numbered);

    static NoteItem ListItem(double x, double y, double width, string[] lines, ListStyle style)
    {
        var body = new ShapeTextBody(
            [.. lines.Select(line => new ShapeParagraph([new StyledRun(line, Face(14, false, null))])
            {
                List = style,
                SpaceAfter = 6
            })]);

        // Through the same renumbering the editor uses, so a numbered list built here carries exactly
        // the markers it would grow if it had been typed.
        return NotebookDocument.NewTextItem(x, y, width) with
        {
            Text = NotebookEditorController.Renumber(body) with { InsetLeft = 4, InsetRight = 4 },
            Height = lines.Length * 26 + 8
        };
    }

    static ShapeParagraph Run(
        string text,
        double size,
        bool bold = false,
        ArgbColor? color = null,
        TextAlignment alignment = TextAlignment.Left)
        => new([new StyledRun(text, Face(size, bold, color))]) { Alignment = alignment };

    static TextStyle Face(double size, bool bold, ArgbColor? color)
        => NotebookDocument.DefaultTextStyle with
        {
            FontSize = size,
            Bold = bold,
            Color = color ?? NotebookDocument.DefaultTextStyle.Color
        };

    static NoteItem Stroke(IReadOnlyList<InkPoint> points, ArgbColor color, double width, InkTool tool)
        => NotebookDocument.NewInkItem(new InkStroke
        {
            Points = points,
            Color = color,
            Width = width,
            Tool = tool
        });

    /// <summary>A flat sweep, which is what a highlighter drawn across a line of text looks like.</summary>
    static IReadOnlyList<InkPoint> Highlighter(double x, double y, double length)
    {
        var points = new List<InkPoint>();

        for (var i = 0; i <= 24; i++)
        {
            var t = i / 24d;

            // A little sag in the middle: a hand-drawn sweep is never level, and a perfectly straight
            // one reads as a drawn rectangle rather than as ink.
            points.Add(new InkPoint(x + length * t, y + Math.Sin(t * Math.PI) * 1.6));
        }

        return points;
    }

    static IReadOnlyList<InkPoint> Ellipse(double x, double y, double width, double height, double wobble)
    {
        var points = new List<InkPoint>();
        var cx = x + width / 2;
        var cy = y + height / 2;

        // Slightly past a full turn, so the ends overlap the way a circled note does.
        for (var i = 0; i <= 74; i++)
        {
            var angle = i / 72d * Math.PI * 2.1;
            var jitter = Math.Sin(angle * 3.3) * (wobble / 18);

            points.Add(new InkPoint(
                cx + (width / 2 + jitter) * Math.Cos(angle),
                cy + (height / 2 + jitter) * Math.Sin(angle),

                // Pressure eases off towards the end of the stroke, which is what a real pen does as
                // the hand lifts - and what makes the tapering worth having.
                Math.Clamp(0.7 - i / 74d * 0.35, 0.1, 1)));
        }

        return points;
    }

    static IReadOnlyList<InkPoint> Squiggle(double x, double y, double length, double amplitude)
    {
        var points = new List<InkPoint>();

        for (var i = 0; i <= 90; i++)
        {
            var t = i / 90d;

            points.Add(new InkPoint(
                x + length * t,
                y + Math.Sin(t * Math.PI * 5) * amplitude * (1 - t * 0.35),
                Math.Clamp(0.35 + Math.Abs(Math.Cos(t * Math.PI * 5)) * 0.5, 0.1, 1)));
        }

        return points;
    }
}

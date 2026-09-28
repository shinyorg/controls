using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using D = DocumentFormat.OpenXml.Drawing;

namespace Shiny.Controls.Office.Presentation;

/// <summary>
/// The Shape Format tab and the text commands beyond bold and italic: fill, outline, effects, quick
/// styles, size, rotation; soft breaks, case, sub/superscript, spacing, text layout and links.
/// </summary>
public sealed partial class SlideEditorController
{
    /// <summary>A shape is selected (or its text is being edited), so Shape Format has something to act on.</summary>
    public bool HasShapeSelection => !this.IsReadOnly && this.SelectedShapes.Count > 0 && this.Selection is { Element: not null };

    /// <summary>The selection holds a drawn shape rather than only pictures or tables — Shape Fill applies.</summary>
    public bool CanFormatShape
        => this.HasShapeSelection && this.SelectedShapes.Any(i => this.Current!.Shapes[i] is { Table: null, IsGroup: false, Chart: null });

    /// <summary>The selected table, when the selection is one table — for the Table Design and Layout tabs.</summary>
    public SlideTable? SelectedTable => this.others.Count == 0 ? this.Selection?.Table : null;

    /// <summary>The selected chart, when the selection is one chart.</summary>
    public SlideChart? SelectedChart => this.others.Count == 0 ? this.Selection?.Chart : null;

    /// <summary>The selected picture, when the selection is one picture.</summary>
    public bool IsPictureSelected => this.others.Count == 0 && this.Selection is { Image: not null };

    /// <summary>
    /// The theme's colours, for a picker's "Theme Colors" rows. Read from the current slide's master.
    /// </summary>
    public SlideColorScheme? ThemeColorScheme
    {
        get
        {
            var part = this.deck.PartAt(this.Index)?.SlideLayoutPart?.SlideMasterPart?.ThemePart
                ?? this.deck.PresentationPart.SlideMasterParts.FirstOrDefault()?.ThemePart;

            if (part?.Theme?.ThemeElements?.ColorScheme is not { } scheme)
                return null;

            var colors = ThemeColors.From(part);
            ArgbColor C(string token) => colors.Resolve(token) ?? new ArgbColor(255, 0, 0, 0);

            return new SlideColorScheme(
                scheme.Name?.Value ?? "Theme",
                C("dk1"), C("lt1"), C("dk2"), C("lt2"),
                C("accent1"), C("accent2"), C("accent3"), C("accent4"), C("accent5"), C("accent6"),
                C("hlink"), C("folHlink"));
        }
    }

    /// <summary>The name of the theme the current slide's master uses.</summary>
    public string? ThemeName
        => (this.deck.PartAt(this.Index)?.SlideLayoutPart?.SlideMasterPart?.ThemePart
            ?? this.deck.PresentationPart.SlideMasterParts.FirstOrDefault()?.ThemePart)?.Theme?.Name?.Value;

    /// <summary>
    /// Runs one command per selected shape as a single undo step.
    /// </summary>
    void ForEachSelected(string name, Func<int, IEditCommand<SlideDeck>?> build)
    {
        if (this.IsReadOnly)
            return;

        var commands = this.SelectedShapes.Select(build).OfType<IEditCommand<SlideDeck>>().ToList();
        if (commands.Count == 0)
            return;

        this.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand<SlideDeck>(name, commands));
        this.deck.Undo.BreakCoalescing();
    }

    // ---- fill, outline, effects ----

    /// <summary>Shape Fill: a colour, a theme colour, a gradient, or <see cref="SlideFillSpec.None"/>.</summary>
    public void SetShapeFill(SlideFillSpec fill)
        => this.ForEachSelected("Shape fill", i => this.Current!.Shapes[i] is { Table: null, IsGroup: false, Chart: null }
            ? new FormatShapeFillCommand(this.Index, i, fill)
            : null);

    /// <summary>Shape Fill with a plain colour, or no fill for null.</summary>
    public void SetShapeFill(ArgbColor? color)
        => this.SetShapeFill(color is { } c ? SlideFillSpec.Color(c) : SlideFillSpec.None);

    /// <summary>Shape Outline: the colour, keeping the weight and dash.</summary>
    public void SetShapeOutlineColor(ArgbColor color)
        => this.ForEachSelected("Shape outline", i => new FormatShapeLineCommand(this.Index, i, Color: color));

    /// <summary>Shape Outline ▸ Weight, in points.</summary>
    public void SetShapeOutlineWeight(double points)
        => this.ForEachSelected("Shape outline", i => new FormatShapeLineCommand(this.Index, i, Width: OoxmlUnits.PointsToPixels(points)));

    /// <summary>Shape Outline ▸ Dashes.</summary>
    public void SetShapeOutlineDash(LineDash dash)
        => this.ForEachSelected("Shape outline", i => new FormatShapeLineCommand(this.Index, i, Dash: dash));

    /// <summary>Shape Outline ▸ No Outline.</summary>
    public void RemoveShapeOutline()
        => this.ForEachSelected("Shape outline", i => new FormatShapeLineCommand(this.Index, i, Remove: true));

    /// <summary>Shape Effects ▸ Shadow: PowerPoint's "Offset: Bottom Right" on, or off.</summary>
    public void SetShapeShadow(bool on)
        => this.ForEachSelected("Shadow", i => new SetShapeShadowCommand(this.Index, i, on ? SetShapeShadowCommand.Default : null));

    /// <summary>Whether the primary selection has a shadow, so the toggle can show it.</summary>
    public bool SelectionHasShadow => this.Selection?.Shadow is not null;

    /// <summary>A Quick Styles gallery entry.</summary>
    public void ApplyQuickStyle(SlideQuickStyle style)
        => this.ForEachSelected("Shape style", i => this.Current!.Shapes[i] is { Table: null, IsGroup: false, Chart: null }
            ? new ApplyQuickStyleCommand(this.Index, i, style)
            : null);

    // ---- size and rotation ----

    /// <summary>The Size group's Height and Width boxes, in slide pixels. Either may be left as it is.</summary>
    public void SetShapeSize(double? width, double? height, bool lockAspect = false)
    {
        if (this.IsReadOnly)
            return;

        this.ForEachSelected("Size", i =>
        {
            var shape = this.Current!.Shapes[i];
            var w = width ?? shape.Width;
            var h = height ?? shape.Height;

            if (lockAspect && shape.Width > 0 && shape.Height > 0)
            {
                if (width is not null && height is null)
                    h = w * shape.Height / shape.Width;
                else if (height is not null && width is null)
                    w = h * shape.Width / shape.Height;
            }

            // Resized about the shape's top-left, as the ribbon's boxes do.
            return new SetShapeBoundsCommand(this.Index, i, shape.X, shape.Y, Math.Max(1, w), Math.Max(1, h));
        });
    }

    /// <summary>Rotate ▸ Right 90° (positive) or Left 90° (negative).</summary>
    public void RotateBy(double degrees)
        => this.ForEachSelected("Rotate", i => this.Current!.Shapes[i] is { Table: null, Chart: null } shape
            ? new SetShapeRotationCommand(this.Index, i, shape.Rotation + degrees)
            : null);

    /// <summary>An exact rotation, in degrees clockwise.</summary>
    public void SetRotation(double degrees)
        => this.ForEachSelected("Rotate", i => this.Current!.Shapes[i] is { Table: null, Chart: null }
            ? new SetShapeRotationCommand(this.Index, i, degrees)
            : null);

    /// <summary>Rotate ▸ Flip Horizontal / Flip Vertical.</summary>
    public void Flip(bool horizontal)
        => this.ForEachSelected("Flip", i => this.Current!.Shapes[i] is { Table: null, Chart: null } shape
            ? new SetShapeRotationCommand(this.Index, i, null, horizontal ? !shape.FlipHorizontal : null, horizontal ? null : !shape.FlipVertical)
            : null);

    // ---- text ----

    /// <summary>
    /// A line break inside the paragraph — Shift+Enter. The paragraph, and so its bullet, carries on.
    /// </summary>
    public void InsertLineBreak()
    {
        if (!this.CanEditText())
            return;

        if (this.TextSelection.IsEmpty)
        {
            this.Execute(new InsertSlideLineBreakCommand(this.caret));
            this.MoveCaret(this.caret);
            return;
        }

        using (this.deck.Undo.BeginTransaction("Line break"))
        {
            var at = this.DeleteSelectionCore();
            this.Execute(new InsertSlideLineBreakCommand(at));
            this.MoveCaret(at);
        }
    }

    /// <summary>A soft line break sits right before the caret, so Backspace takes the break rather than a character.</summary>
    bool TryDeleteBreakAtCaret()
    {
        if (!this.TextSelection.IsEmpty ||
            this.ActiveText?.Paragraphs.ElementAtOrDefault(this.caret.Paragraph)?.Element is not { } paragraph ||
            ShapeTextEditor.BreaksAt(paragraph, this.caret.Offset).Count == 0)
        {
            return false;
        }

        this.Execute(new DeleteSlideLineBreakCommand(this.caret));
        this.MoveCaret(this.caret);
        return true;
    }

    /// <summary>The standard size steps Increase/Decrease Font Size walk.</summary>
    static readonly double[] FontSteps = [8, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 40, 44, 48, 54, 60, 66, 72, 80, 88, 96];

    /// <summary>Increase Font Size — Ctrl+Shift+&gt; — or Decrease with a negative direction.</summary>
    public void GrowFont(int direction)
    {
        var current = this.CaretFormat.FontSize;
        var next = direction > 0
            ? FontSteps.FirstOrDefault(x => x > current + 0.01, Math.Min(400, current + 4))
            : FontSteps.LastOrDefault(x => x < current - 0.01, Math.Max(1, current - 1));

        if (this.IsEditingText)
        {
            this.SetFontSize(next);
            return;
        }

        // A selected shape rather than its text: every run in it steps, as PowerPoint's button does.
        this.FormatWholeShapes(ShapeTextEditor.SetFontSize(next), direction > 0 ? "Increase font size" : "Decrease font size");
    }

    /// <summary>Change Case.</summary>
    public void ChangeCase(TextCase textCase)
    {
        if (!this.CanEditText())
            return;

        var range = this.TextSelection.Normalized();
        if (range.IsEmpty && this.WordAroundCaret() is { } word)
            range = word;

        if (range.IsEmpty)
            return;

        this.Execute(new ChangeSlideTextCaseCommand(range, textCase));
    }

    /// <summary>Clear All Formatting on the selected text (or the whole shape when it is only selected).</summary>
    public void ClearFormatting()
    {
        if (this.IsEditingText)
        {
            this.FormatRuns(ShapeTextEditor.ClearFormatting(), "Clear formatting");
            return;
        }

        this.FormatWholeShapes(ShapeTextEditor.ClearFormatting(), "Clear formatting");
    }

    /// <summary>Superscript, subscript, or back to the baseline — toggled against what the caret shows.</summary>
    public void SetBaseline(int percent)
        => this.FormatRuns(ShapeTextEditor.SetBaseline(percent), percent > 0 ? "Superscript" : percent < 0 ? "Subscript" : "Baseline");

    public void ToggleSuperscript() => this.SetBaseline(this.CaretFormat.Baseline > 0 ? 0 : 30);

    public void ToggleSubscript() => this.SetBaseline(this.CaretFormat.Baseline < 0 ? 0 : -25);

    /// <summary>Character Spacing, in points: negative tightens, positive loosens.</summary>
    public void SetCharacterSpacing(double points) => this.FormatRuns(ShapeTextEditor.SetCharacterSpacing(points), "Character spacing");

    /// <summary>Line Spacing as a multiple — 1.0, 1.5, 2.0 on PowerPoint's menu.</summary>
    public void SetLineSpacing(double multiple)
    {
        if (this.IsEditingText)
            this.FormatParagraphs(ShapeTextEditor.SetLineSpacing(multiple), "Line spacing");
        else
            this.FormatWholeShapeParagraphs(ShapeTextEditor.SetLineSpacing(multiple), "Line spacing");
    }

    /// <summary>Justify, which the Paragraph group also offers.</summary>
    public void SetParagraphAlignment(TextAlignment alignment)
    {
        if (this.IsEditingText)
            this.SetAlignment(alignment);
        else
            this.FormatWholeShapeParagraphs(ShapeTextEditor.SetAlignment(alignment), "Alignment");
    }

    /// <summary>Align Text: where the text sits vertically in its shape.</summary>
    public void SetTextAnchor(TextAnchor anchor)
        => this.ForEachSelected("Align text", i => this.Current!.Shapes[i].Text is not null && this.Current.Shapes[i].Element is DocumentFormat.OpenXml.Presentation.Shape
            ? new SetTextBodyCommand(this.Index, i) { Anchor = anchor }
            : null);

    /// <summary>Text Direction: horizontal, or turned a quarter either way.</summary>
    public void SetTextDirection(ShapeTextDirection direction)
        => this.ForEachSelected("Text direction", i => this.Current!.Shapes[i].Element is DocumentFormat.OpenXml.Presentation.Shape
            ? new SetTextBodyCommand(this.Index, i) { Direction = direction }
            : null);

    /// <summary>
    /// Autofit: do nothing, shrink the text, or grow the shape.
    /// </summary>
    /// <remarks>
    /// The shrink factor and the grown height are measured here and written into the file, as PowerPoint
    /// does — a reader recomputing them would need PowerPoint's own font metrics, so it honours what was
    /// recorded instead.
    /// </remarks>
    public void SetAutofit(TextAutofit autofit)
        => this.ForEachSelected("Autofit", i =>
        {
            if (this.Current!.Shapes[i] is not { Element: DocumentFormat.OpenXml.Presentation.Shape, Text: { } text } shape)
                return null;

            var body = text with { FontScale = 1, LineSpaceReduction = 0 };
            var command = new SetTextBodyCommand(this.Index, i) { Autofit = autofit };

            return autofit switch
            {
                TextAutofit.ShrinkOnOverflow => command with { FontScale = this.FitScale(body, shape.Width, shape.Height) },
                TextAutofit.ResizeShape => command with { FitHeight = Math.Max(8, this.TextHeight(body, shape.Width) + body.InsetTop + body.InsetBottom) },
                _ => command
            };
        });

    double TextHeight(ShapeTextBody body, double width)
    {
        var layout = ShapeTextLayout.Layout(body, width, 100000, this.measurer);
        return layout.Paragraphs.Count == 0 ? 0 : layout.Paragraphs.Max(x => x.Y + x.Height);
    }

    /// <summary>The largest font scale (to 25%, in 2.5% steps) at which the text fits the shape.</summary>
    double FitScale(ShapeTextBody body, double width, double height)
    {
        var available = height - body.InsetTop - body.InsetBottom;
        for (var scale = 1.0; scale > 0.25; scale -= 0.025)
        {
            if (this.TextHeight(body with { FontScale = scale }, width) <= available)
                return scale;
        }

        return 0.25;
    }

    /// <summary>Applies a run change to every paragraph of every selected shape — formatting a shape rather than a selection of its text.</summary>
    void FormatWholeShapes(Action<D.RunProperties> apply, string label)
        => this.ForEachSelected(label, i =>
        {
            if (this.Current!.Shapes[i].Text is not { Paragraphs.Count: > 0 } text)
                return null;

            var last = text.Paragraphs.Count - 1;
            var start = new SlidePosition(this.Index, i, 0, 0);
            var end = start with { Paragraph = last, Offset = text.Paragraphs[last].PlainText.Length };
            return new FormatSlideRunsCommand(new SlideTextRange(start, end), apply, label);
        });

    void FormatWholeShapeParagraphs(Action<D.ParagraphProperties> apply, string label)
        => this.ForEachSelected(label, i =>
        {
            if (this.Current!.Shapes[i].Text is not { Paragraphs.Count: > 0 } text)
                return null;

            var start = new SlidePosition(this.Index, i, 0, 0);
            return new FormatSlideParagraphsCommand(new SlideTextRange(start, start with { Paragraph = text.Paragraphs.Count - 1 }), apply, label);
        });

    // ---- links ----

    /// <summary>
    /// The link under the caret or on the selected shape, for filling in the Link dialog.
    /// </summary>
    public SlideHyperlink? CurrentHyperlink
        => this.IsEditingText
            ? SlideHyperlinkCodec.Decode(this.CaretFormat.Link)
            : this.Selection?.Hyperlink;

    /// <summary>
    /// Insert ▸ Link. With text selected it links the text; with only a caret it inserts
    /// <paramref name="display"/> (or the address) as linked text; with a shape selected it links the shape.
    /// Null removes the link.
    /// </summary>
    public void SetHyperlink(SlideHyperlink? link, string? display = null)
    {
        if (this.IsReadOnly || this.Selection is null)
            return;

        if (this.IsEditingText && this.ActiveText is not null)
        {
            var range = this.TextSelection.Normalized();
            if (range.IsEmpty && this.WordAroundCaret() is { } word)
                range = word;

            if (!range.IsEmpty)
            {
                this.Execute(new SetTextHyperlinkCommand(range, link));
                return;
            }

            if (link is null)
                return;

            var text = string.IsNullOrEmpty(display) ? link.ToString() : display;
            this.Execute(new InsertLinkedTextCommand(this.caret, text, link));
            this.MoveCaret(this.caret with { Offset = this.caret.Offset + text.Length });
            return;
        }

        this.ForEachSelected(link is null ? "Remove link" : "Link", i => new SetShapeHyperlinkCommand(this.Index, i, link));
    }

    public void RemoveHyperlink() => this.SetHyperlink(null);

    // ---- fields ----

    /// <summary>
    /// Insert ▸ Slide Number or Date &amp; Time at the caret. Returns false with no caret, so a host can
    /// open the Header &amp; Footer dialog instead, which is what PowerPoint does.
    /// </summary>
    public bool InsertField(SlideFieldKind kind, string? dateFormat = null)
    {
        if (!this.CanEditText() || this.activeCell is not null)
            return false;

        var at = this.TextSelection.IsEmpty ? this.caret : this.DeleteSelectionCore();
        var before = this.LengthOf(at.Paragraph);
        this.Execute(new InsertSlideFieldCommand(at, kind, dateFormat));
        this.MoveCaret(at with { Offset = at.Offset + Math.Max(0, this.LengthOf(at.Paragraph) - before) });
        return true;
    }
}

/// <summary>The Change Case menu.</summary>
public enum TextCase
{
    /// <summary>Capital at the start of each sentence.</summary>
    Sentence,
    Lower,
    Upper,

    /// <summary>Capital at the start of each word.</summary>
    Capitalize,

    /// <summary>Every letter's case flipped.</summary>
    Toggle
}

/// <summary>Inserts a soft line break (<c>a:br</c>) at a position.</summary>
public sealed record InsertSlideLineBreakCommand(SlidePosition At) : SlideCommand
{
    public override string Name => "Line break";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var restore = CaptureShape(context, this.At.Slide, this.At.Shape);
        if (TextAt(context, this.At)?.Paragraphs.ElementAtOrDefault(this.At.Paragraph)?.Element is not { } paragraph)
            return new NoOpSlideCommand();

        ShapeTextEditor.InsertBreak(paragraph, this.At.Offset);
        context.Reproject(this.At.Slide);
        return restore;
    }
}

/// <summary>Removes the (last) soft line break sitting at a position — Backspace just after one.</summary>
public sealed record DeleteSlideLineBreakCommand(SlidePosition At) : SlideCommand
{
    public override string Name => "Delete";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var restore = CaptureShape(context, this.At.Slide, this.At.Shape);
        if (TextAt(context, this.At)?.Paragraphs.ElementAtOrDefault(this.At.Paragraph)?.Element is not { } paragraph ||
            ShapeTextEditor.BreaksAt(paragraph, this.At.Offset) is not { Count: > 0 } breaks)
        {
            return new NoOpSlideCommand();
        }

        breaks[^1].Remove();
        context.Reproject(this.At.Slide);
        return restore;
    }
}

/// <summary>Rewrites the case of a span, keeping every run's formatting.</summary>
public sealed record ChangeSlideTextCaseCommand(SlideTextRange Range, TextCase Case) : SlideCommand
{
    public override string Name => "Change case";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var range = this.Range.Normalized();
        if (range.IsEmpty || !range.Start.SameShape(range.End))
            return new NoOpSlideCommand();

        var restore = CaptureShape(context, range.Start.Slide, range.Start.Shape);
        var culture = System.Globalization.CultureInfo.CurrentCulture;

        for (var i = range.Start.Paragraph; i <= range.End.Paragraph; i++)
        {
            if (TextAt(context, range.Start with { Paragraph = i })?.Paragraphs.ElementAtOrDefault(i)?.Element is not { } paragraph)
                continue;

            var whole = ShapeTextEditor.TextOf(paragraph);
            var from = i == range.Start.Paragraph ? range.Start.Offset : 0;
            var to = i == range.End.Paragraph ? range.End.Offset : whole.Length;

            ShapeTextEditor.ChangeCase(paragraph, from, to, (text, offset) => Transform(text, offset, whole));
        }

        context.Reproject(range.Start.Slide);
        return restore;

        string Transform(string text, int offset, string whole)
        {
            var chars = text.ToCharArray();
            for (var c = 0; c < chars.Length; c++)
            {
                var position = offset + c;
                var previous = position > 0 ? whole[position - 1] : ' ';

                chars[c] = this.Case switch
                {
                    TextCase.Lower => char.ToLower(chars[c], culture),
                    TextCase.Upper => char.ToUpper(chars[c], culture),
                    TextCase.Toggle => char.IsUpper(chars[c]) ? char.ToLower(chars[c], culture) : char.ToUpper(chars[c], culture),
                    TextCase.Capitalize => char.IsWhiteSpace(previous) || position == 0 ? char.ToUpper(chars[c], culture) : char.ToLower(chars[c], culture),
                    _ => IsSentenceStart(whole, position) ? char.ToUpper(chars[c], culture) : char.ToLower(chars[c], culture)
                };
            }

            return new string(chars);
        }

        static bool IsSentenceStart(string whole, int position)
        {
            for (var p = position - 1; p >= 0; p--)
            {
                if (char.IsWhiteSpace(whole[p]))
                    continue;

                return whole[p] is '.' or '!' or '?';
            }

            return true;
        }
    }
}

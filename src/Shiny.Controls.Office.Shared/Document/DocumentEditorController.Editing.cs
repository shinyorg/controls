using DocumentFormat.OpenXml.Wordprocessing;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Document;

/// <summary>
/// Home tab editing: the clipboard, the format painter, the Font group's extras and the Paragraph group.
/// </summary>
public sealed partial class DocumentEditorController
{
    // ---- clipboard ----

    /// <summary>
    /// Copies the selection: the rich fragment is kept here, and the plain text is returned for the host
    /// to put on the system clipboard.
    /// </summary>
    /// <returns>The selection as plain text, paragraphs separated by newlines; empty when nothing is selected.</returns>
    /// <remarks>
    /// The two go together deliberately. Paste compares what the system clipboard holds with the text
    /// copied here: when they match the copy came from this editor and pastes with its formatting; when
    /// they differ something else was copied since, and it pastes as the plain text it is.
    /// </remarks>
    public string Copy()
    {
        if (this.Selection.IsEmpty)
            return string.Empty;

        var fragment = this.CaptureFragment(this.Selection.Range);
        var text = string.Join(Environment.NewLine, fragment.Select(p => WordParagraphEditor.TextOf(p).Replace(WordParagraphEditor.ObjectPlaceholder, string.Empty)));

        DocumentClipboard.Set(this.document, fragment, text);
        return text;
    }

    /// <summary>Copies the selection and removes it. Returns the plain text for the system clipboard.</summary>
    public string Cut()
    {
        if (this.Selection.IsEmpty || this.IsReadOnlyDocument)
            return this.Copy();

        var text = this.Copy();
        this.DeleteSelectionIfAny();
        this.AfterEdit();
        return text;
    }

    /// <summary>
    /// Pastes at the caret, with formatting when the clipboard holds what this editor copied.
    /// </summary>
    /// <param name="systemText">
    /// What the system clipboard holds, or null when the host cannot read it. Null trusts the internal
    /// clipboard.
    /// </param>
    public void Paste(string? systemText = null)
    {
        if (this.IsReadOnlyDocument)
            return;

        var rich = DocumentClipboard.Current;
        var fromHere = rich is not null && (systemText is null || Normalize(systemText) == Normalize(rich.PlainText));

        if (!fromHere)
        {
            if (!string.IsNullOrEmpty(systemText))
                this.PasteText(systemText);

            return;
        }

        var fragment = rich!.FragmentFor(this.document);

        using (this.document.Undo.BeginTransaction("Paste"))
        {
            var at = this.DeleteSelectionIfAny();
            this.document.Execute(new PasteFragmentCommand(at, fragment));

            var lastLength = WordParagraphEditor.LengthOf(fragment[^1]);
            var end = fragment.Count == 1
                ? at with { Offset = at.Offset + lastLength }
                : new DocumentPosition(at.Block + fragment.Count - 1, lastLength);

            this.Selection.MoveTo(end);
        }

        this.AfterEdit();

        static string Normalize(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
    }

    /// <summary>
    /// Pastes text without formatting — Paste Special ▸ Keep Text Only. Newlines become paragraphs.
    /// </summary>
    public void PasteText(string text)
    {
        if (this.IsReadOnlyDocument || string.IsNullOrEmpty(text))
            return;

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        using (this.document.Undo.BeginTransaction("Paste"))
        {
            var at = this.DeleteSelectionIfAny();

            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    this.document.Execute(new SplitParagraphCommand(at));
                    at = new DocumentPosition(at.Block + 1, 0);
                }

                if (lines[i].Length > 0)
                {
                    this.document.Execute(new InsertTextCommand(at, lines[i]));
                    at = at with { Offset = at.Offset + lines[i].Length };
                }
            }

            this.Selection.MoveTo(at);
        }

        this.AfterEdit();
    }

    /// <summary>True when there is something this editor copied to paste with formatting.</summary>
    public bool HasRichClipboard => DocumentClipboard.Current is not null;

    /// <summary>
    /// The selected paragraphs, cloned and trimmed to the selection.
    /// </summary>
    /// <remarks>
    /// Anything that is unique by id in a document — comment anchors, bookmarks, footnote references —
    /// is left out of the copy. Pasting it would create a second anchor for the same comment or a
    /// second reference to the same note, which Word treats as corruption.
    /// </remarks>
    List<Paragraph> CaptureFragment(DocumentRange range)
    {
        var fragment = new List<Paragraph>();

        for (var block = range.Start.Block; block <= range.End.Block; block++)
        {
            if (this.document.ParagraphElementAt(block) is not { } source)
                continue;

            var clone = (Paragraph)source.CloneNode(true);
            clone.ParagraphProperties?.RemoveAllChildren<SectionProperties>();

            var length = WordParagraphEditor.LengthOf(clone);
            var from = block == range.Start.Block ? range.Start.Offset : 0;
            var to = block == range.End.Block ? Math.Min(range.End.Offset, length) : length;

            WordParagraphEditor.Delete(clone, to, length);
            WordParagraphEditor.Delete(clone, 0, from);

            foreach (var unique in clone.Descendants().Where(x =>
                         x is CommentRangeStart or CommentRangeEnd or CommentReference or BookmarkStart or BookmarkEnd
                             or FootnoteReference or EndnoteReference).ToList())
            {
                if (unique is CommentReference or FootnoteReference or EndnoteReference && unique.Parent is Run run)
                    run.Remove();
                else
                    unique.Remove();
            }

            fragment.Add(clone);
        }

        return fragment;
    }

    // ---- format painter ----

    RunProperties? painterRun;
    ParagraphProperties? painterParagraph;

    /// <summary>True while the format painter is loaded and waiting for a selection to paint.</summary>
    public bool IsFormatPainterActive { get; private set; }

    /// <summary>True when the painter stays loaded after painting — a double-click on the brush.</summary>
    public bool IsFormatPainterSticky { get; private set; }

    /// <summary>
    /// Picks up the formatting at the caret for the format painter.
    /// </summary>
    /// <param name="sticky">Keep painting until cancelled, rather than for one selection.</param>
    /// <remarks>
    /// Calling it again while it is loaded puts it down, which is what clicking the brush a second time
    /// does in Word.
    /// </remarks>
    public void CopyFormatting(bool sticky = false)
    {
        if (this.IsFormatPainterActive)
        {
            this.CancelFormatPainter();
            return;
        }

        var paragraph = this.document.ParagraphElementAt(this.Selection.Range.Start.Block);
        if (paragraph is null)
            return;

        var offset = this.Selection.IsEmpty
            ? Math.Max(0, this.Selection.Focus.Offset - 1)
            : this.Selection.Range.Start.Offset;

        var run = WordParagraphEditor.RunAt(paragraph, offset);
        this.painterRun = run?.RunProperties?.CloneNode(true) as RunProperties;
        this.painterParagraph = paragraph.ParagraphProperties?.CloneNode(true) as ParagraphProperties;
        this.painterParagraph?.RemoveAllChildren<SectionProperties>();

        this.IsFormatPainterActive = true;
        this.IsFormatPainterSticky = sticky;
        this.RaiseChanged();
    }

    /// <summary>Puts the format painter down without painting.</summary>
    public void CancelFormatPainter()
    {
        if (!this.IsFormatPainterActive)
            return;

        this.IsFormatPainterActive = false;
        this.IsFormatPainterSticky = false;
        this.painterRun = null;
        this.painterParagraph = null;
        this.RaiseChanged();
    }

    /// <summary>
    /// Paints the picked-up formatting onto the selection, when the painter is loaded.
    /// </summary>
    /// <returns>True when something was painted.</returns>
    /// <remarks>
    /// Hosts call this when a pointer gesture that made a selection ends. A selection that covers a
    /// whole paragraph takes the paragraph formatting too — alignment, spacing, style — which is Word's
    /// rule: the painter carries paragraph formatting when the paragraph mark is part of what it copies
    /// onto.
    /// </remarks>
    public bool ApplyFormatPainter()
    {
        if (!this.IsFormatPainterActive || this.IsReadOnlyDocument)
            return false;

        var range = this.Selection.IsEmpty && this.WordAroundCaret() is { } word ? word : this.Selection.Range;
        if (range.IsEmpty)
            return false;

        var run = this.painterRun;
        var paragraphFormat = this.painterParagraph;
        var wholeParagraphs = range.Start.Offset == 0 && range.End.Offset >= this.LengthOf(range.End.Block);

        using (this.document.Undo.BeginTransaction("Format Painter"))
        {
            this.document.Execute(new FormatRunsCommand(range, RunFormatChange.CopyFrom(run)));

            if (wholeParagraphs && paragraphFormat is not null)
                this.document.Execute(new FormatParagraphsCommand(range, ParagraphFormatChange.CopyFrom(paragraphFormat)));
        }

        if (!this.IsFormatPainterSticky)
        {
            this.IsFormatPainterActive = false;
            this.painterRun = null;
            this.painterParagraph = null;
        }

        this.AfterEdit();
        return true;
    }

    /// <summary>
    /// Tells the controller a pointer gesture that may have made a selection has ended.
    /// </summary>
    /// <remarks>The format painter's trigger. Safe to call after every pointer release.</remarks>
    public void CompletePointerGesture()
    {
        if (this.IsFormatPainterActive)
            this.ApplyFormatPainter();
    }

    // ---- font ----

    /// <summary>The sizes Grow Font and Shrink Font step through — Word's own list.</summary>
    public static IReadOnlyList<double> FontSizeSteps { get; } = [8, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72];

    /// <summary>Grow Font (Ctrl+Shift+&gt;): the next size up the list, then by tens past 72.</summary>
    public void GrowFont()
    {
        var current = this.CaretFormat.FontSize;
        var next = FontSizeSteps.FirstOrDefault(x => x > current + 0.01);
        this.SetFontSize(next > 0 ? next : Math.Min(1638, Math.Floor(current / 10) * 10 + 10));
    }

    /// <summary>Shrink Font (Ctrl+Shift+&lt;): the next size down the list.</summary>
    public void ShrinkFont()
    {
        var current = this.CaretFormat.FontSize;
        var next = FontSizeSteps.LastOrDefault(x => x < current - 0.01);
        this.SetFontSize(next > 0 ? next : Math.Max(1, current - 1));
    }

    /// <summary>Superscript on or off (Ctrl+Shift+=).</summary>
    public void ToggleSuperscript()
        => this.ApplyRunFormat(RunFormatChange.Vertical(this.CaretFormat.Superscript ? VerticalPosition.Baseline : VerticalPosition.Superscript));

    /// <summary>Subscript on or off (Ctrl+=).</summary>
    public void ToggleSubscript()
        => this.ApplyRunFormat(RunFormatChange.Vertical(this.CaretFormat.Subscript ? VerticalPosition.Baseline : VerticalPosition.Subscript));

    /// <summary>
    /// Clear All Formatting: the selection's character formatting goes and its paragraphs return to
    /// Normal, keeping their list membership.
    /// </summary>
    public void ClearFormatting()
    {
        if (this.IsReadOnlyDocument)
            return;

        var range = this.Selection.IsEmpty && this.WordAroundCaret() is { } word ? word : this.Selection.Range;

        using (this.document.Undo.BeginTransaction("Clear Formatting"))
        {
            if (!range.IsEmpty)
                this.document.Execute(new FormatRunsCommand(range, RunFormatChange.Clear()));

            this.document.Execute(new FormatParagraphsCommand(this.Selection.Range, ParagraphFormatChange.ClearDirect()));
        }

        this.ClearPending();
        this.AfterEdit();
    }

    /// <summary>Change Case on the selection, or on the word at the caret.</summary>
    public void ChangeCase(TextCase textCase)
    {
        if (this.IsReadOnlyDocument)
            return;

        var range = this.Selection.IsEmpty && this.WordAroundCaret() is { } word ? word : this.Selection.Range;
        if (range.IsEmpty)
            return;

        this.document.Execute(new ChangeCaseCommand(range, textCase));
        this.AfterEdit();
    }

    /// <summary>
    /// Shift+F3: cycles lower → UPPER → Capitalize Each Word, the way Word's key does.
    /// </summary>
    public void CycleCase()
    {
        var range = this.Selection.IsEmpty && this.WordAroundCaret() is { } word ? word : this.Selection.Range;
        if (range.IsEmpty)
            return;

        var text = this.TextOf(range.Start.Block);
        var from = Math.Min(range.Start.Offset, text.Length);
        var to = range.IsWithinOneBlock ? Math.Min(range.End.Offset, text.Length) : text.Length;
        var letters = text[from..to].Where(char.IsLetter).ToList();

        if (letters.Count == 0)
            return;

        var next = letters.All(char.IsLower) ? TextCase.Upper
            : letters.All(char.IsUpper) ? TextCase.Capitalize
            : TextCase.Lower;

        this.ChangeCase(next);
    }

    // ---- paragraph ----

    /// <summary>The line-spacing presets the Paragraph group offers.</summary>
    public static IReadOnlyList<double> LineSpacingPresets { get; } = [1.0, 1.15, 1.5, 2.0, 2.5, 3.0];

    /// <summary>Sets line spacing as a multiple of single — 1.0, 1.15, 1.5, 2.0.</summary>
    public void SetLineSpacing(double multiple)
        => this.ApplyParagraphFormat(ParagraphFormatChange.Spacing(Math.Clamp(multiple, 0.5, 10), null, null));

    /// <summary>Sets the space above and below the selected paragraphs, in points. Null leaves one alone.</summary>
    public void SetParagraphSpacing(double? beforePoints, double? afterPoints)
        => this.ApplyParagraphFormat(ParagraphFormatChange.Spacing(null, beforePoints, afterPoints));

    /// <summary>Add Space Before Paragraph (12pt), or Remove it when there already is some.</summary>
    public void ToggleSpaceBefore()
        => this.SetParagraphSpacing(this.CaretFormat.SpaceBefore > 0 ? 0 : 12, null);

    /// <summary>Add Space After Paragraph (8pt), or Remove it when there already is some.</summary>
    public void ToggleSpaceAfter()
        => this.SetParagraphSpacing(null, this.CaretFormat.SpaceAfter > 0 ? 0 : 8);

    /// <summary>How far one press of Increase Indent moves a paragraph: half an inch.</summary>
    public const double IndentStep = 48;

    /// <summary>
    /// Increase or Decrease Indent. A list item moves a level; any other paragraph moves half an inch.
    /// </summary>
    public void ChangeIndent(int direction)
    {
        if (this.IsReadOnlyDocument || direction == 0)
            return;

        if (this.SelectionTouchesAList())
        {
            this.ChangeListLevel(Math.Sign(direction));
            return;
        }

        var current = this.CaretFormat.IndentLeft;
        var target = direction > 0
            ? (Math.Floor(current / IndentStep) + 1) * IndentStep
            : Math.Max(0, (Math.Ceiling(current / IndentStep) - 1) * IndentStep);

        this.ApplyParagraphFormat(ParagraphFormatChange.Indent(target, null, null));
    }

    /// <summary>Sets the paragraph indents in pixels. A negative first line is a hanging indent; null leaves one alone.</summary>
    public void SetIndents(double? left, double? right, double? firstLine)
        => this.ApplyParagraphFormat(ParagraphFormatChange.Indent(left, right, firstLine));

    /// <summary>Shading behind the selected paragraphs; null removes it.</summary>
    public void SetParagraphShading(ArgbColor? color)
        => this.ApplyParagraphFormat(ParagraphFormatChange.Shading(color));

    /// <summary>Paragraph borders from the Borders menu.</summary>
    public void SetParagraphBorders(ParagraphBorderPreset preset, ArgbColor? color = null)
    {
        var ink = color ?? new ArgbColor(255, 0, 0, 0);

        var borders = preset switch
        {
            ParagraphBorderPreset.Bottom => new ParagraphBorders { Bottom = true, Color = ink },
            ParagraphBorderPreset.Top => new ParagraphBorders { Top = true, Color = ink },
            ParagraphBorderPreset.Outside or ParagraphBorderPreset.All => new ParagraphBorders { Top = true, Bottom = true, Left = true, Right = true, Color = ink },
            _ => null
        };

        this.ApplyParagraphFormat(ParagraphFormatChange.Border(borders));
    }

    /// <summary>
    /// Show/Hide ¶: draws paragraph marks, spaces and tabs. A view setting — nothing is written.
    /// </summary>
    public bool ShowFormattingMarks
    {
        get => this.showFormattingMarks;
        set
        {
            if (this.showFormattingMarks == value)
                return;

            this.showFormattingMarks = value;
            this.RaiseChanged();
        }
    }

    bool showFormattingMarks;
}


/// <summary>
/// What the editor last copied, kept with formatting for a paste back into a document.
/// </summary>
/// <remarks>
/// Process-wide, so a copy from one open document pastes into another with its formatting. Pictures
/// are the exception: a picture is a reference to a part of the document it came from, so pasted into
/// a different one it would point at nothing — those runs are dropped on the way across.
/// </remarks>
public sealed class DocumentClipboard
{
    readonly WeakReference<WordDocument> source;
    readonly IReadOnlyList<Paragraph> fragment;

    DocumentClipboard(WordDocument source, IReadOnlyList<Paragraph> fragment, string plainText)
    {
        this.source = new WeakReference<WordDocument>(source);
        this.fragment = fragment;
        this.PlainText = plainText;
    }

    /// <summary>The last copy, or null when nothing has been copied.</summary>
    public static DocumentClipboard? Current { get; private set; }

    /// <summary>The copy as plain text.</summary>
    public string PlainText { get; }

    /// <summary>How many paragraphs the copy spans.</summary>
    public int ParagraphCount => this.fragment.Count;

    internal static void Set(WordDocument source, IReadOnlyList<Paragraph> fragment, string text)
        => Current = new DocumentClipboard(source, fragment, text);

    /// <summary>Forgets the last copy.</summary>
    public static void Clear() => Current = null;

    /// <summary>The copied paragraphs, cloned for pasting into <paramref name="target"/>.</summary>
    internal IReadOnlyList<Paragraph> FragmentFor(WordDocument target)
    {
        var sameDocument = this.source.TryGetTarget(out var from) && ReferenceEquals(from, target);
        var copies = this.fragment.Select(x => (Paragraph)x.CloneNode(true)).ToList();

        if (!sameDocument)
        {
            foreach (var paragraph in copies)
            {
                foreach (var drawing in paragraph.Descendants<DocumentFormat.OpenXml.Wordprocessing.Drawing>().ToList())
                    drawing.Remove();

                // A hyperlink relationship id belongs to the source's part too; the text stays.
                foreach (var link in paragraph.Descendants<Hyperlink>().Where(x => x.Id is not null).ToList())
                {
                    foreach (var child in link.ChildElements.ToList())
                    {
                        child.Remove();
                        link.InsertBeforeSelf(child);
                    }

                    link.Remove();
                }
            }
        }

        return copies;
    }
}

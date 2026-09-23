using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spelling;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using TextAlignment = Shiny.Controls.Office.Text.TextAlignment;

namespace Shiny.Controls.Office.Document;

/// <summary>Formatting of the text at the caret, for a toolbar to reflect.</summary>
public sealed record CaretFormat
{
    public static readonly CaretFormat Default = new();

    public string FontFamily { get; init; } = "Calibri";

    /// <summary>Font size in points, which is what a size picker shows.</summary>
    public double FontSize { get; init; } = 11;

    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Strike { get; init; }
    public ArgbColor Color { get; init; } = new(255, 0, 0, 0);

    /// <summary>The highlight behind the caret, or null when the text is not highlighted.</summary>
    public ArgbColor? Highlight { get; init; }

    public TextAlignment Alignment { get; init; } = TextAlignment.Left;
    public string? StyleName { get; init; }

    /// <summary>Which list the caret's paragraph is in, so a toolbar can light the right button.</summary>
    public ListStyle List { get; init; } = ListStyle.None;

    /// <summary>How deeply nested that list item is, zero-based. Zero when it is not in a list.</summary>
    public int ListLevel { get; init; }
}

public enum CaretMove
{
    Left,
    Right,
    Up,
    Down,
    LineStart,
    LineEnd,
    DocumentStart,
    DocumentEnd,
    WordLeft,
    WordRight
}

/// <summary>
/// Host-independent editing behaviour for a document: caret, selection, typing and formatting.
/// </summary>
/// <remarks>
/// Both hosts forward raw input here rather than implementing behaviour themselves. A host owns only
/// two things: painting, and giving the platform somewhere to send text — a hidden contenteditable on
/// the web, a hidden entry on MAUI.
/// </remarks>
public sealed class DocumentEditorController : DocumentController
{
    readonly WordDocument document;

    /// <summary>Remembered x for vertical movement, so up/down through short lines does not drift left.</summary>
    double? desiredX;

    /// <summary>
    /// The context the controller was created on - the UI thread on both hosts.
    /// </summary>
    /// <remarks>
    /// Captured rather than injected because the one thing every host has in common is that the
    /// control is constructed on the thread that is allowed to repaint it.
    /// </remarks>
    readonly SynchronizationContext? sync = SynchronizationContext.Current;

    CancellationTokenSource? spellCheckDebounce;

    /// <summary>
    /// Formatting chosen with nothing selected, waiting for text to apply itself to.
    /// </summary>
    /// <remarks>
    /// This is what Word does and what every editor is expected to do: put the caret somewhere, pick
    /// 24pt, type, and what you type is 24pt. Without it a format chosen at a bare caret has no range
    /// to act on, so the click does nothing at all — which reads as a broken toolbar rather than as a
    /// missing feature.
    /// </remarks>
    readonly List<RunFormatChange> pending = new();

    /// <summary>Where <see cref="pending"/> was chosen. Moving the caret off it abandons the choice.</summary>
    DocumentPosition? pendingAt;

    public DocumentEditorController(WordDocument document, ITextMeasurer measurer, ISpellChecker? spellChecker = null)
        : base(document, measurer)
    {
        this.document = document;
        this.Spelling = new DocumentSpellCheck(spellChecker ?? SpellCheckers.Default);
        this.Find = new DocumentFinder(this);

        // A check finishes on whatever thread the platform answered on - a binder thread on Android -
        // and repainting is the host's reaction to Changed, so the hop back has to happen here rather
        // than in each host.
        this.Spelling.Updated += (_, _) => this.Post(this.RaiseChanged);
        this.Selection.Changed += (_, _) =>
        {
            this.DropPendingIfCaretMoved();
            this.RefreshCaretFormat();
            this.RaiseChanged();
        };

        // Weakly: the document is the app's and outlives the view this controller paints. See WeakEvent.
        document.ContentChanged += WeakEvent.Forward(this, document, static c => c.OnDocumentContentChanged(), static (d, h) => d.ContentChanged -= h);

        this.RefreshCaretFormat();
    }

    void OnDocumentContentChanged()
    {
        this.InvalidateLayout();

        // The matches were collected from text that has just changed underneath them. Dropped
        // rather than recollected: an edit is somebody typing, and re-running a search on every
        // keystroke would walk the whole document per character.
        this.Find.Invalidate();
        this.RaiseChanged();
    }

    public DocumentSelection Selection { get; } = new();

    /// <summary>Spell checking over the document. Replace <c>Spelling.Checker</c> to override it.</summary>
    public DocumentSpellCheck Spelling { get; }

    /// <summary>
    /// Text search over the document, which is what the toolbar's find box drives.
    /// </summary>
    /// <remarks>
    /// Created with the controller rather than on demand, so a host can bind a find bar to it before
    /// anything has been searched for and the bar's readout is live from the first keystroke.
    /// </remarks>
    public DocumentFinder Find { get; }

    /// <summary>
    /// The checker in use. Assigning one discards what the previous checker found and re-checks.
    /// </summary>
    /// <remarks>
    /// Cached results belong to the checker that produced them - a different dictionary, or a
    /// different language, disagrees about which words are wrong - so swapping one in has to clear
    /// the cache rather than leave the old squiggles on screen.
    /// </remarks>
    public ISpellChecker SpellChecker
    {
        get => this.Spelling.Checker;
        set
        {
            if (ReferenceEquals(this.Spelling.Checker, value))
                return;

            this.Spelling.Checker = value ?? NullSpellChecker.Instance;
            this.Spelling.Invalidate();
            this.ScheduleSpellCheck(0);
        }
    }

    /// <summary>Whether misspellings are checked and underlined at all.</summary>
    public bool IsSpellCheckEnabled
    {
        get => this.Spelling.IsEnabled;
        set
        {
            if (this.Spelling.IsEnabled == value)
                return;

            this.Spelling.IsEnabled = value;
            this.Spelling.Invalidate();

            if (value)
                this.ScheduleSpellCheck(0);
        }
    }

    protected override void OnViewportChanged() => this.ScheduleSpellCheck();

    /// <summary>Formatting at the caret. A toolbar binds to this to show what is active.</summary>
    public CaretFormat CaretFormat { get; private set; } = CaretFormat.Default;

    public bool CanUndo => this.document.Undo.CanUndo;
    public bool CanRedo => this.document.Undo.CanRedo;

    /// <summary>Raised when the caret moves, so a host can scroll it into view.</summary>
    public event EventHandler? CaretMoved;

    // ---- spelling ----

    /// <summary>
    /// Re-checks the paragraphs currently on screen.
    /// </summary>
    /// <remarks>
    /// Called by the host when the view settles rather than on every keystroke: a platform checker is
    /// an interop call (a service round trip on Android), and running one per character typed would
    /// both stutter and flag every half-finished word as the user types it.
    /// </remarks>
    public Task RefreshSpellingAsync(CancellationToken cancellationToken = default)
    {
        var (first, last) = this.VisibleBlockRange();
        return this.Spelling.RefreshAsync(this.Document.Blocks, first, last, cancellationToken);
    }

    /// <summary>
    /// Queues a re-check of the visible paragraphs, coalescing bursts of edits and scrolls.
    /// </summary>
    /// <remarks>
    /// Hosts call this freely - on every keystroke, every scroll - and the debounce is what makes that
    /// safe. The delay also serves the user: a word is only half-typed while they are typing it, and
    /// flagging it mid-word is noise.
    /// </remarks>
    public void ScheduleSpellCheck(int delayMilliseconds = 500)
    {
        if (!this.Spelling.IsEnabled || !this.Spelling.Checker.IsAvailable)
            return;

        var previous = this.spellCheckDebounce;
        var cts = new CancellationTokenSource();
        this.spellCheckDebounce = cts;

        previous?.Cancel();
        previous?.Dispose();

        _ = RunAsync(cts.Token);

        async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
                await this.RefreshSpellingAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer request, which is the normal case while typing.
            }
        }
    }

    /// <summary>Runs an action on the thread the controller was created on.</summary>
    void Post(Action action)
    {
        if (this.sync is null || this.sync == SynchronizationContext.Current)
            action();
        else
            this.sync.Post(_ => action(), null);
    }

    (int First, int Last) VisibleBlockRange()
    {
        var blocks = this.Blocks;

        // Blocks are in flow coordinates and the scroll offset is in the paginated view's, so the
        // band has to be converted. Skipping this would spell-check the wrong paragraphs — off by
        // one page's worth of margins and gaps, and growing with every page scrolled past.
        var pagination = this.Pagination;
        var top = pagination.ViewToFlow(this.Viewport.ScrollY);
        var bottom = pagination.ViewToFlow(this.Viewport.ScrollY + this.Viewport.Height);

        var first = -1;
        var last = -1;

        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i].Y + blocks[i].Height < top)
                continue;

            if (blocks[i].Y > bottom)
                break;

            if (first < 0)
                first = i;

            last = i;
        }

        return first < 0 ? (0, -1) : (first, last);
    }

    /// <summary>Underline rectangles for misspelled words, in document coordinates.</summary>
    public IEnumerable<GridRectLike> SpellingRects()
    {
        var blocks = this.Blocks;
        var (first, last) = this.VisibleBlockRange();

        for (var i = first; i <= last && i < blocks.Count; i++)
        {
            if (blocks[i] is not LaidOutParagraph paragraph)
                continue;

            if (this.Document.Blocks.ElementAtOrDefault(i) is not DocumentParagraph source)
                continue;

            foreach (var error in this.Spelling.ErrorsFor(i, source.PlainText))
            {
                // A misspelled word can wrap, so the underline is emitted per line rather than as one
                // rectangle spanning from start to end - which would otherwise stretch across the page.
                foreach (var line in paragraph.Lines)
                {
                    var from = Math.Max(error.Start, line.SourceOffset);
                    var to = Math.Min(error.End, line.SourceEnd);
                    if (to <= from)
                        continue;

                    var start = this.CaretRect(new DocumentPosition(i, from));
                    var end = this.CaretRect(new DocumentPosition(i, to));

                    yield return new GridRectLike(
                        start.X,
                        paragraph.Y + line.Y + line.Ascent + 2,
                        Math.Max(2, end.X - start.X),
                        3);
                }
            }
        }
    }

    /// <summary>
    /// Moves the caret to the next misspelling after the current one, wrapping at the end.
    /// </summary>
    /// <returns>The error landed on, or null when the document has none.</returns>
    /// <remarks>
    /// <para>
    /// Wrapping rather than stopping: a review pass starts wherever the caret happens to be, and a
    /// "next" that goes quiet at the last paragraph looks identical to one that has finished the
    /// document - the user has no way to tell whether the words above were checked.
    /// </para>
    /// <para>
    /// The word is selected, not just landed on. The point of stepping to an error is to do something
    /// about it, and every one of those things - accept a suggestion, ignore it, learn it - operates on
    /// the word rather than on a caret inside it.
    /// </para>
    /// </remarks>
    public async Task<SpellingError?> GoToNextSpellingErrorAsync(
        bool backwards = false,
        CancellationToken cancellationToken = default)
    {
        var blocks = this.Document.Blocks;
        if (blocks.Count == 0)
            return null;

        var from = this.Selection.Focus;

        // One full lap, so a document whose only error is the one already under the caret still finds
        // it rather than reporting none.
        for (var step = 0; step <= blocks.Count; step++)
        {
            if (cancellationToken.IsCancellationRequested)
                return null;

            var index = backwards
                ? from.Block - step
                : from.Block + step;

            index = ((index % blocks.Count) + blocks.Count) % blocks.Count;

            if (blocks.ElementAtOrDefault(index) is not DocumentParagraph paragraph)
                continue;

            // Checked here, one paragraph at a time, because the spelling pass only ever runs over
            // what is on screen - nothing off screen can show a squiggle, so checking the whole
            // document up front would stall a long one for no benefit. That makes the cache empty
            // for every block below the fold, and a walk that only read it would step through a
            // document full of misspellings and report that it had none.
            await this.Spelling
                .RefreshAsync(blocks, index, index, cancellationToken)
                .ConfigureAwait(false);

            var errors = this.Spelling.ErrorsFor(index, paragraph.PlainText);
            if (errors.Count == 0)
                continue;

            // Only the block the search started in is partially consumed; every other one is taken
            // whole, which is what makes the wrap come back round to the errors above the caret.
            var candidates = step == 0
                ? errors.Where(e => backwards ? e.End < from.Offset : e.Start > from.Offset)
                : errors;

            var found = backwards
                ? candidates.LastOrDefault()
                : candidates.FirstOrDefault();

            if (found.Length == 0)
                continue;

            this.Selection.MoveTo(new DocumentPosition(index, found.Start));
            this.Selection.ExtendTo(new DocumentPosition(index, found.End));
            this.ScrollCaretIntoView();
            this.RaiseChanged();
            return found;
        }

        return null;
    }

    /// <summary>The misspelling under a position, or null.</summary>
    public SpellingError? SpellingErrorAt(DocumentPosition position)
    {
        if (this.Document.Blocks.ElementAtOrDefault(position.Block) is not DocumentParagraph paragraph)
            return null;

        foreach (var error in this.Spelling.ErrorsFor(position.Block, paragraph.PlainText))
        {
            if (error.Contains(position.Offset) || error.End == position.Offset)
                return error;
        }

        return null;
    }

    /// <summary>Replacement candidates for the misspelling under a position.</summary>
    public async Task<IReadOnlyList<string>> SuggestAtAsync(DocumentPosition position, CancellationToken cancellationToken = default)
    {
        if (this.SpellingErrorAt(position) is not { } error)
            return [];

        return await this.Spelling.Checker.SuggestAsync(error.Word, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Replaces the misspelling under a position with a suggestion, as one undo step.</summary>
    public bool ApplySuggestion(DocumentPosition position, string replacement)
    {
        if (this.SpellingErrorAt(position) is not { } error || string.IsNullOrEmpty(replacement))
            return false;

        var start = new DocumentPosition(position.Block, error.Start);
        var end = new DocumentPosition(position.Block, error.End);

        using (this.document.Undo.BeginTransaction("Correct Spelling"))
        {
            this.document.Execute(new DeleteRangeCommand(new DocumentRange(start, end)));
            this.document.Execute(new InsertTextCommand(start, replacement));
        }

        this.Selection.MoveTo(start with { Offset = error.Start + replacement.Length });
        this.Spelling.InvalidateFrom(position.Block);
        this.AfterEdit();
        return true;
    }

    /// <summary>Stops reporting a word for the rest of the session.</summary>
    public void IgnoreSpelling(string word)
    {
        this.Spelling.Checker.Ignore(word);
        this.Spelling.Invalidate();
    }

    /// <summary>Adds a word to the user's dictionary, where the platform supports it.</summary>
    public void LearnSpelling(string word)
    {
        this.Spelling.Checker.Learn(word);
        this.Spelling.Invalidate();
    }

    // ---- text input ----

    /// <summary>Inserts text at the caret, replacing the selection first.</summary>
    public void InsertText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        // A space is the trigger Word uses for list autoformat, and it is checked before the space is
        // inserted so the marker and the space both disappear into the list rather than surviving as
        // the first characters of the item.
        if (text == " " && this.TryAutoFormatList())
            return;

        var at = this.DeleteSelectionIfAny();
        var end = at with { Offset = at.Offset + text.Length };

        if (this.pending.Count == 0)
        {
            // The common path stays a single command. A transaction closes as a composite, which ends
            // the coalescing run - so wrapping every keystroke in one would make each character its
            // own undo step.
            this.document.Execute(new InsertTextCommand(at, text));
        }
        else
        {
            // Taken and cleared before anything runs: applying the format moves through the document,
            // and a half-applied queue left in place would be applied again by the next keystroke.
            var formats = this.pending.ToList();
            this.ClearPending();

            // One undo step. A Ctrl+Z that took the characters away but left the formatting behind
            // would leave the caret carrying a format nobody chose.
            using (this.document.Undo.BeginTransaction("Typing"))
            {
                this.document.Execute(new InsertTextCommand(at, text));

                foreach (var change in formats)
                    this.document.Execute(new FormatRunsCommand(new DocumentRange(at, end), change));
            }
        }

        this.Selection.MoveTo(end);
        this.AfterEdit();
    }

    /// <summary>
    /// Enter: splits the paragraph at the caret.
    /// </summary>
    /// <remarks>
    /// With one exception, which every editor has and which people rely on without noticing: Enter on
    /// an <em>empty</em> list item ends the list rather than making another empty one. A nested item
    /// comes out one level first, so repeated Enter walks back up the nesting and then leaves.
    /// </remarks>
    public void InsertParagraph()
    {
        if (this.Selection.IsEmpty && this.EmptyListItemAt(this.Selection.Focus.Block) is { } level)
        {
            var range = new DocumentRange(this.Selection.Focus, this.Selection.Focus);

            this.document.Execute(level > 0
                ? new ShiftListLevelCommand(range, -1)
                : new SetListCommand(range, ListStyle.None));

            this.AfterEdit();
            return;
        }

        var at = this.DeleteSelectionIfAny();
        this.document.Execute(new SplitParagraphCommand(at));
        this.Selection.MoveTo(new DocumentPosition(at.Block + 1, 0));
        this.AfterEdit();
    }

    /// <summary>The level of the list item at a block, when that item has no text. Null otherwise.</summary>
    int? EmptyListItemAt(int block)
        => this.Document.Blocks.ElementAtOrDefault(block) is DocumentParagraph { List: { } label } paragraph
            && paragraph.PlainText.Length == 0
            ? label.Numbering?.Level ?? 0
            : null;

    /// <summary>Backspace.</summary>
    public void DeleteBackward()
    {
        if (!this.Selection.IsEmpty)
        {
            this.DeleteSelectionIfAny();
            this.AfterEdit();
            return;
        }

        var caret = this.Selection.Focus;

        if (caret.Offset > 0)
        {
            var from = caret with { Offset = caret.Offset - 1 };
            this.document.Execute(new DeleteRangeCommand(new DocumentRange(from, caret)));
            this.Selection.MoveTo(from);
            this.AfterEdit();
            return;
        }

        // At the very start of a paragraph, backspace joins it to the one above.
        if (caret.Block == 0)
            return;

        var previousLength = this.LengthOf(caret.Block - 1);
        this.document.Execute(new MergeParagraphCommand(caret.Block - 1));
        this.Selection.MoveTo(new DocumentPosition(caret.Block - 1, previousLength));
        this.AfterEdit();
    }

    /// <summary>Delete.</summary>
    public void DeleteForward()
    {
        if (!this.Selection.IsEmpty)
        {
            this.DeleteSelectionIfAny();
            this.AfterEdit();
            return;
        }

        var caret = this.Selection.Focus;
        var length = this.LengthOf(caret.Block);

        if (caret.Offset < length)
        {
            this.document.Execute(new DeleteRangeCommand(new DocumentRange(caret, caret with { Offset = caret.Offset + 1 })));
            this.AfterEdit();
            return;
        }

        if (caret.Block + 1 >= this.Document.Blocks.Count)
            return;

        this.document.Execute(new MergeParagraphCommand(caret.Block));
        this.AfterEdit();
    }

    DocumentPosition DeleteSelectionIfAny()
    {
        if (this.Selection.IsEmpty)
            return this.Selection.Focus;

        var range = this.Selection.Range;
        this.document.Execute(new DeleteRangeCommand(range));
        this.Selection.MoveTo(range.Start);
        return range.Start;
    }

    // ---- formatting ----

    public void ToggleBold() => this.ApplyRunFormat(RunFormatChange.Bold(!this.CaretFormat.Bold));

    public void ToggleItalic() => this.ApplyRunFormat(RunFormatChange.Italic(!this.CaretFormat.Italic));

    public void ToggleUnderline() => this.ApplyRunFormat(RunFormatChange.Underline(!this.CaretFormat.Underline));

    public void ToggleStrikethrough() => this.ApplyRunFormat(RunFormatChange.Strike(!this.CaretFormat.Strike));

    public void SetFontFamily(string family) => this.ApplyRunFormat(RunFormatChange.FontFamily(family));

    public void SetFontSize(double points) => this.ApplyRunFormat(RunFormatChange.FontSize(points));

    public void SetTextColor(ArgbColor color) => this.ApplyRunFormat(RunFormatChange.Color(color));

    /// <summary>Highlights the selection, or clears it when passed null.</summary>
    public void SetHighlight(ArgbColor? color) => this.ApplyRunFormat(RunFormatChange.Highlight(color));

    /// <summary>Highlights with <paramref name="color"/>, or clears when that colour is already on.</summary>
    /// <remarks>
    /// What the toolbar button does, as opposed to what the swatch gallery does: pressing the
    /// highlight button on already-yellow text is a request to remove it, not to re-apply it.
    /// </remarks>
    public void ToggleHighlight(ArgbColor color)
        => this.SetHighlight(this.CaretFormat.Highlight == color ? null : color);

    public void SetAlignment(TextAlignment alignment)
        => this.ApplyParagraphFormat(ParagraphFormatChange.Alignment(alignment));

    public void SetParagraphStyle(string? styleId)
        => this.ApplyParagraphFormat(ParagraphFormatChange.Style(styleId));

    // ---- lists ----

    /// <summary>Turns the selected paragraphs into a bulleted list, or out of one when they already are.</summary>
    public void ToggleBulletList()
        => this.SetListStyle(this.CaretFormat.List == ListStyle.Bullet ? ListStyle.None : ListStyle.Bullet);

    /// <summary>Turns the selected paragraphs into a numbered list, or out of one when they already are.</summary>
    public void ToggleNumberedList()
        => this.SetListStyle(this.CaretFormat.List == ListStyle.Numbered ? ListStyle.None : ListStyle.Numbered);

    /// <summary>
    /// Puts every paragraph the selection touches into a list of this style, or takes them out of one.
    /// </summary>
    /// <remarks>
    /// The definitions are created on demand, so this works on a document that has never had a list in
    /// it and has no <c>numbering.xml</c> at all.
    /// </remarks>
    public void SetListStyle(ListStyle style)
    {
        if (this.IsReadOnlyDocument)
            return;

        this.document.Execute(new SetListCommand(this.Selection.Range, style));
        this.AfterEdit();
    }

    /// <summary>
    /// Nests or un-nests the list items the selection touches.
    /// </summary>
    /// <remarks>
    /// Only list items move. Indenting ordinary paragraphs is a different operation on a different
    /// property, and doing both from one call would make Tab mean two things at once.
    /// </remarks>
    public void ChangeListLevel(int delta)
    {
        if (this.IsReadOnlyDocument || delta == 0)
            return;

        this.document.Execute(new ShiftListLevelCommand(this.Selection.Range, delta));
        this.AfterEdit();
    }

    /// <summary>
    /// What the Tab key does, which depends on where the caret is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Inside a list it nests: Tab moves the item in a level, Shift+Tab moves it out. That is how the
    /// second level of a list gets created at all — there is no other gesture for it, and it is what
    /// every word processor does.
    /// </para>
    /// <para>
    /// Anywhere else Tab is still a tab character, so the key is not silently swallowed outside a
    /// list. Shift+Tab outside a list does nothing, matching Word: there is no character to remove.
    /// </para>
    /// </remarks>
    /// <returns>True when the key was consumed.</returns>
    public bool HandleTab(bool shift = false)
    {
        if (this.IsReadOnlyDocument)
            return false;

        if (this.SelectionTouchesAList())
        {
            this.ChangeListLevel(shift ? -1 : 1);
            return true;
        }

        if (shift)
            return false;

        var at = this.DeleteSelectionIfAny();
        this.document.Execute(new InsertTabCommand(at));
        this.Selection.MoveTo(at with { Offset = at.Offset + InsertTabCommand.Width });
        this.AfterEdit();
        return true;
    }

    /// <summary>True when any paragraph the selection touches is a list item.</summary>
    bool SelectionTouchesAList()
    {
        var range = this.Selection.Range;

        for (var block = range.Start.Block; block <= range.End.Block; block++)
        {
            if (this.Document.Blocks.ElementAtOrDefault(block) is DocumentParagraph { List: not null })
                return true;
        }

        return false;
    }

    /// <summary>
    /// Turns a marker the user typed by hand into a real list, if that is what they typed.
    /// </summary>
    /// <remarks>
    /// Runs on the space that follows the marker, and only when the marker is the whole paragraph so
    /// far — a hyphen part-way through a sentence is a hyphen. The marker and the space that triggered
    /// it are both removed, and the two edits share one undo step so a single Ctrl+Z puts the typed
    /// characters back rather than leaving a bulleted empty paragraph behind.
    /// </remarks>
    /// <returns>True when a list was created and the space should not be inserted.</returns>
    bool TryAutoFormatList()
    {
        if (!this.Selection.IsEmpty || !this.IsAutoFormatListEnabled)
            return false;

        var at = this.Selection.Focus;
        if (at.Offset == 0)
            return false;

        // Already a list item: the marker is just text the user meant to type.
        if (this.Document.Blocks.ElementAtOrDefault(at.Block) is not DocumentParagraph { List: null } paragraph)
            return false;

        var text = paragraph.PlainText;
        if (at.Offset > text.Length)
            return false;

        // Everything before the caret, which has to be the marker and nothing else. Text after the
        // caret is left alone — it becomes the item's text, which is what happens when someone
        // marks up a paragraph they have already written.
        var style = ListAutoFormat.Detect(text[..at.Offset]);
        if (style == ListStyle.None)
            return false;

        var start = at with { Offset = 0 };

        using (this.document.Undo.BeginTransaction(style == ListStyle.Bullet ? "Bulleted List" : "Numbered List"))
        {
            this.document.Execute(new DeleteRangeCommand(new DocumentRange(start, at)));
            this.document.Execute(new SetListCommand(new DocumentRange(start, start), style));
        }

        this.Selection.MoveTo(start);
        this.AfterEdit();
        return true;
    }

    /// <summary>
    /// Whether typing <c>-</c>, <c>*</c> or <c>1.</c> followed by a space starts a list. On by default.
    /// </summary>
    /// <remarks>
    /// Offered as a switch because autoformat is the one editing behaviour that acts without being
    /// asked, and a document of shell transcripts is a real reason to want it off.
    /// </remarks>
    public bool IsAutoFormatListEnabled { get; set; } = true;

    /// <summary>
    /// Applies run formatting to the selection, or holds it for the next thing typed.
    /// </summary>
    /// <remarks>
    /// With something selected this is immediate. With a bare caret there is no range to format, so the
    /// change is held in <see cref="pending"/> and applied by the next <see cref="InsertText"/> — which
    /// is what Word does. The toolbar shows it in the meantime, so the choice is visible before there
    /// is any text carrying it.
    /// <para>
    /// The exception, also Word's: a bare caret <em>inside</em> a word formats that whole word. Without
    /// it, clicking into a word and pressing Bold or picking a colour changed nothing on screen and
    /// read as a broken button. At a word's edge — the end of a line, between words — there is no word
    /// to mean, so the change is held for typing as before.
    /// </para>
    /// </remarks>
    void ApplyRunFormat(RunFormatChange change)
    {
        if (this.Selection.IsEmpty && this.WordAroundCaret() is { } word)
        {
            this.ClearPending();
            this.document.Execute(new FormatRunsCommand(word, change));
            this.AfterEdit();
            return;
        }

        if (this.Selection.IsEmpty)
        {
            this.RememberPending(change);
            this.RefreshCaretFormat();
            this.RaiseChanged();
            return;
        }

        this.ClearPending();
        this.document.Execute(new FormatRunsCommand(this.Selection.Range, change));
        this.AfterEdit();
    }

    /// <summary>The word the caret sits strictly inside, or null at a word's edge or in whitespace.</summary>
    DocumentRange? WordAroundCaret()
    {
        var caret = this.Selection.Focus;
        var word = this.WordRangeAt(caret);

        if (word.IsEmpty || caret.Offset <= word.Start.Offset || caret.Offset >= word.End.Offset)
            return null;

        // WordBoundaries also groups runs of punctuation and spaces; only letters and digits are a word.
        var text = this.TextOf(caret.Block);
        return char.IsLetterOrDigit(text[caret.Offset - 1]) && char.IsLetterOrDigit(text[caret.Offset])
            ? word
            : null;
    }

    /// <summary>Queues a change for the next insertion, replacing any earlier one of the same kind.</summary>
    void RememberPending(RunFormatChange change)
    {
        this.pendingAt = this.Selection.Focus;

        // Kind rather than Name: "Bold" and "Remove Bold" are the same attribute, and the second has to
        // replace the first rather than being applied after it.
        if (change.Kind != RunFormatKind.Other)
            this.pending.RemoveAll(x => x.Kind == change.Kind);

        this.pending.Add(change);
    }

    void ClearPending()
    {
        this.pending.Clear();
        this.pendingAt = null;
    }

    /// <summary>
    /// Abandons a pending format when the caret is no longer where it was chosen.
    /// </summary>
    /// <remarks>
    /// Anything else would be a trap: pick a colour, change your mind and click elsewhere, and the next
    /// thing typed - somewhere unrelated, possibly much later - would come out in that colour.
    /// </remarks>
    void DropPendingIfCaretMoved()
    {
        if (this.pending.Count == 0)
            return;

        if (this.Selection.IsEmpty && this.pendingAt == this.Selection.Focus)
            return;

        this.ClearPending();
    }

    void ApplyParagraphFormat(ParagraphFormatChange change)
    {
        this.document.Execute(new FormatParagraphsCommand(this.Selection.Range, change));
        this.AfterEdit();
    }

    // ---- inserting objects ----

    /// <summary>
    /// Inserts a preset-geometry shape at the caret.
    /// </summary>
    /// <remarks>
    /// Inline, in the text flow. The document view is a reflow engine with no float layer, so a shape
    /// behaves like a very large character: it wraps with the line it is on and moves as text is typed
    /// before it. That is a real limitation next to Word's anchored shapes, and it is also the only
    /// honest thing to draw in a view that has no fixed page positions to anchor against.
    /// </remarks>
    public void InsertShape(
        ShapeGeometry geometry,
        double width = 160,
        double height = 120,
        ArgbColor? fill = null,
        ArgbColor? outline = null,
        string? text = null)
    {
        if (this.IsReadOnlyDocument)
            return;

        var run = WordContentFactory.Shape(
            this.document.NextDrawingId(),
            geometry,
            width,
            height,
            fill ?? new ArgbColor(255, 0x44, 0x72, 0xC4),
            outline,
            text);

        this.InsertObjectRun(run);
    }

    /// <summary>
    /// Inserts a picture at the caret, adding its bytes to the package.
    /// </summary>
    /// <param name="data">The encoded image, in whatever format <paramref name="contentType"/> names.</param>
    /// <param name="contentType">The MIME type, e.g. <c>image/png</c>.</param>
    /// <param name="width">Display width in pixels. Null keeps the aspect ratio against <paramref name="height"/>.</param>
    /// <param name="height">Display height in pixels. Null keeps the aspect ratio against <paramref name="width"/>.</param>
    /// <remarks>
    /// The bytes go in as they arrived — never re-encoded. A drop of a 12-megapixel JPEG produces a
    /// large document, and the alternative is deciding on the user's behalf what quality their picture
    /// should be, which is not a renderer's call to make.
    /// </remarks>
    public void InsertImage(
        byte[] data,
        string contentType,
        double? width = null,
        double? height = null,
        string name = "Picture")
    {
        ArgumentNullException.ThrowIfNull(data);

        if (this.IsReadOnlyDocument || data.Length == 0)
            return;

        if (this.document.AddImagePart(data, contentType) is not { } relationshipId)
            return;

        var (w, h) = ResolveSize(width, height);

        this.InsertObjectRun(WordContentFactory.Picture(
            this.document.NextDrawingId(), relationshipId, w, h, name));
    }

    /// <summary>
    /// Inserts a page break at the caret.
    /// </summary>
    /// <remarks>
    /// Visible only in <see cref="DocumentPageLayout.Print"/>. In reflow it is still written to the
    /// document — and still saved — but a continuous column has nowhere to show it, so it falls back
    /// to a line break.
    /// </remarks>
    public void InsertPageBreak()
    {
        if (this.IsReadOnlyDocument)
            return;

        var at = this.DeleteSelectionIfAny();
        this.document.Execute(new InsertPageBreakCommand(at));
        this.AfterEdit();
    }

    /// <summary>
    /// Replaces the document's header with a single line of text, or clears it when passed null.
    /// </summary>
    /// <param name="text">The header text, or null to remove the header entirely.</param>
    /// <param name="alignment">Where the text sits across the page.</param>
    /// <param name="kind">Which header — the default one, the first page's, or the even pages'.</param>
    /// <summary>
    /// The plain text of the running head or foot, or null when there is none.
    /// </summary>
    /// <remarks>
    /// For seeding an editor with what is already there. Paragraphs are joined with newlines and any
    /// page-number field reads as whatever it last resolved to — a lossy view of the part, and
    /// deliberately so: it exists to be shown to someone about to retype the line, not to round-trip.
    /// </remarks>
    public string? ChromeText(bool header, DocumentPageKind kind = DocumentPageKind.Default)
    {
        var chrome = header
            ? this.Document.HeadersFooters.Header(kind)
            : this.Document.HeadersFooters.Footer(kind);

        if (chrome is null || chrome.IsEmpty)
            return null;

        var lines = chrome.Blocks
            .OfType<DocumentParagraph>()
            .Select(x => x.PlainText)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    public void SetHeaderText(string? text, PageNumberPosition alignment = PageNumberPosition.Left, DocumentPageKind kind = DocumentPageKind.Default)
        => this.SetChrome(header: true, text, alignment, kind);

    /// <summary>
    /// Replaces the document's footer with a single line of text, or clears it when passed null.
    /// </summary>
    public void SetFooterText(string? text, PageNumberPosition alignment = PageNumberPosition.Left, DocumentPageKind kind = DocumentPageKind.Default)
        => this.SetChrome(header: false, text, alignment, kind);

    void SetChrome(bool header, string? text, PageNumberPosition alignment, DocumentPageKind kind)
    {
        if (this.IsReadOnlyDocument)
            return;

        var content = String.IsNullOrEmpty(text)
            ? null
            : new[] { WordPageChrome.TextParagraph(text, alignment) };

        this.document.Execute(new SetHeaderFooterCommand(header, kind, content));
        this.AfterChromeEdit();
    }

    /// <summary>The document's current page margins.</summary>
    public PageMargins PageMargins => this.Document.Page.Margins;

    /// <summary>
    /// Sets the page margins for the whole document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Undoable in one step, and total: a document that had no <c>w:pgMar</c> of its own goes back to
    /// having none rather than to whatever the defaults were.
    /// </para>
    /// <para>
    /// Only <see cref="DocumentPageLayout.Print"/> shows it. A reflowed column has no paper to inset
    /// content from, so it insets by a cosmetic gutter instead — the change is still written to the
    /// document and still saved, exactly as a page break is, it simply has nowhere to appear until the
    /// view is showing pages.
    /// </para>
    /// </remarks>
    /// <param name="margins">The new margins, in pixels at 96 dpi. See <see cref="PageMargins.FromInches"/>.</param>
    public void SetPageMargins(PageMargins margins)
    {
        ArgumentNullException.ThrowIfNull(margins);

        if (this.IsReadOnlyDocument)
            return;

        this.document.Execute(new SetPageMarginsCommand(margins));
        this.AfterPageSetupEdit();
    }

    /// <summary>The way round the paper currently is.</summary>
    public PageOrientation PageOrientation => this.Document.Page.Orientation;

    /// <summary>Turns the paper, swapping its two dimensions with it.</summary>
    public void SetPageOrientation(PageOrientation orientation)
    {
        if (this.IsReadOnlyDocument || orientation == this.PageOrientation)
            return;

        this.document.Execute(new SetPageOrientationCommand(orientation));
        this.AfterPageSetupEdit();
    }

    /// <summary>Sets the four margins, in pixels at 96 dpi, keeping the header and footer distances.</summary>
    public void SetPageMargins(double left, double top, double right, double bottom)
        => this.SetPageMargins(this.PageMargins with
        {
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom
        });

    /// <summary>
    /// Settles the view after a page-geometry change.
    /// </summary>
    /// <remarks>
    /// The layout has to be thrown away even though no text changed. Margins move the content box, so
    /// every line re-wraps and every page break moves — and a top-or-bottom-only change does not alter
    /// the measure at all, which is the one the layout cache keys on and would otherwise keep.
    /// </remarks>
    void AfterPageSetupEdit()
    {
        this.InvalidateLayout();
        this.RaiseChanged();
    }

    /// <summary>
    /// Adds a page number to the header or footer, creating it if the document has none.
    /// </summary>
    /// <remarks>
    /// Appended rather than replacing, so a footer that already says something keeps saying it with
    /// the number underneath. Replacing was the other option and it silently discards a footer the
    /// document already had.
    /// </remarks>
    public void InsertPageNumber(
        PageNumberPlacement placement = PageNumberPlacement.Footer,
        PageNumberPosition position = PageNumberPosition.Center,
        PageNumberFormat format = PageNumberFormat.Plain,
        DocumentPageKind kind = DocumentPageKind.Default)
    {
        if (this.IsReadOnlyDocument)
            return;

        var header = placement == PageNumberPlacement.Header;
        var existing = header
            ? this.Document.HeadersFooters.Header(kind)
            : this.Document.HeadersFooters.Footer(kind);

        var content = new List<DocumentFormat.OpenXml.OpenXmlElement>();

        if (existing is { IsEmpty: false })
            content.AddRange(this.Document.ChromeElements(header, kind));

        content.Add(WordPageChrome.PageNumberParagraph(position, format));
        this.document.Execute(new SetHeaderFooterCommand(header, kind, content));
        this.AfterChromeEdit();
    }

    /// <summary>
    /// Settles the view after a header or footer change.
    /// </summary>
    /// <remarks>
    /// Lighter than <c>AfterEdit</c>, which re-checks spelling and scrolls the caret into view — the
    /// body did not change and the caret has not moved. The layout still has to go, because the
    /// per-page header and footer layouts are cached alongside it.
    /// </remarks>
    void AfterChromeEdit()
    {
        this.InvalidateLayout();
        this.RaiseChanged();
    }

    /// <summary>
    /// Inserts an empty table after the block the caret is in.
    /// </summary>
    /// <remarks>
    /// After the paragraph rather than inside it: a table is a block, and OOXML has no way to put one
    /// in the middle of a paragraph. Word splits the paragraph in that case; this places the table on
    /// the boundary below, which loses nothing and never divides a sentence the user was in the middle
    /// of writing.
    /// </remarks>
    public void InsertTable(int rows = 3, int columns = 3)
    {
        if (this.IsReadOnlyDocument)
            return;

        var block = Math.Clamp(this.Selection.Focus.Block, 0, Math.Max(0, this.Document.Blocks.Count - 1));

        this.document.Execute(new InsertTableCommand(block, rows, columns));

        // Into the first cell, which is where someone who just made a table wants to be typing.
        this.Selection.MoveTo(new DocumentPosition(block + 1, 0));
        this.AfterEdit();
    }

    void InsertObjectRun(DocumentFormat.OpenXml.Wordprocessing.Run run)
    {
        // An insertion replaces the selection, exactly as typing a character does.
        if (!this.Selection.Range.IsEmpty)
            this.document.Execute(new DeleteRangeCommand(this.Selection.Range));

        var at = this.Selection.Range.Start;

        this.document.Execute(new InsertInlineObjectCommand(at, run));
        this.Selection.MoveTo(at with { Offset = at.Offset + 1 });
        this.AfterEdit();
    }

    /// <summary>Fills in whichever of width and height was not given, keeping a 4:3 default.</summary>
    static (double Width, double Height) ResolveSize(double? width, double? height) => (width, height) switch
    {
        ({ } w, { } h) => (Math.Max(1, w), Math.Max(1, h)),
        ({ } w, null) => (Math.Max(1, w), Math.Max(1, w * 0.75)),
        (null, { } h) => (Math.Max(1, h * 4 / 3), Math.Max(1, h)),
        _ => (240d, 180d)
    };

    bool IsReadOnlyDocument => !this.document.IsEditable;

    // ---- inline object selection, drag and resize ----

    DocumentPosition? selectedObject;
    ShapeHandle dragging = ShapeHandle.None;
    double dragOriginX;
    double dragOriginY;
    double dragStartWidth;
    double dragStartHeight;

    /// <summary>Size of an object's resize handle, in document pixels.</summary>
    public double HandleSize { get; set; } = 8;

    /// <summary>The position of the selected inline object, or null when none is selected.</summary>
    public DocumentPosition? SelectedObject => this.selectedObject;

    /// <summary>The selected inline object itself, or null.</summary>
    public InlineObject? SelectedInline
        => this.selectedObject is { } at && this.Document.Blocks.ElementAtOrDefault(at.Block) is DocumentParagraph paragraph
            ? InlineAt(paragraph, at.Offset)
            : null;

    /// <summary>The inline object at an offset within a projected paragraph, or null.</summary>
    static InlineObject? InlineAt(DocumentParagraph paragraph, int offset)
    {
        var cursor = 0;

        foreach (var run in paragraph.Runs)
        {
            if (run.IsBreak)
                continue;

            var length = run.Inline is null ? run.Text.Length : 1;

            if (run.Inline is { } inline && offset >= cursor && offset < cursor + length)
                return inline;

            cursor += length;
        }

        return null;
    }

    public bool IsDraggingObject => this.dragging != ShapeHandle.None;

    /// <summary>Selects an inline object, or clears the selection when passed null.</summary>
    public void SelectObject(DocumentPosition? at)
    {
        if (this.selectedObject == at)
            return;

        this.selectedObject = at;

        // A selected object and a text caret are alternatives, not companions: leaving the caret
        // blinking somewhere else while an image is framed reads as two selections at once.
        if (at is { } position)
            this.Selection.MoveTo(position);

        this.CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    public void ClearObjectSelection() => this.SelectObject(null);

    /// <summary>The inline object under a point in viewport coordinates, or null.</summary>
    public DocumentPosition? ObjectAt(double x, double y)
    {
        var (documentX, documentY) = this.ToFlow(x, y);
        var blocks = this.Blocks;

        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is not LaidOutParagraph paragraph)
                continue;

            if (documentY < paragraph.Y || documentY > paragraph.Y + paragraph.Height)
                continue;

            foreach (var line in paragraph.Lines)
            {
                foreach (var run in line.Runs)
                {
                    if (run.Inline is null)
                        continue;

                    var rect = RunBounds(paragraph, line, run);
                    if (rect.Contains(documentX, documentY))
                        return new DocumentPosition(i, run.SourceOffset);
                }
            }
        }

        return null;
    }

    /// <summary>The selected object's rectangle in document coordinates, or null.</summary>
    public GridRectLike? SelectedObjectBounds()
    {
        if (this.selectedObject is not { } at)
            return null;

        if (this.Blocks.ElementAtOrDefault(at.Block) is not LaidOutParagraph paragraph)
            return null;

        foreach (var line in paragraph.Lines)
        {
            foreach (var run in line.Runs)
            {
                if (run.Inline is not null && run.SourceOffset == at.Offset)
                    return RunBounds(paragraph, line, run);
            }
        }

        return null;
    }

    /// <summary>
    /// The eight resize handles around the selected object, in document coordinates.
    /// </summary>
    /// <remarks>
    /// Emitted even for an object too small to hold them, for the same reason the slide editor does:
    /// a handle a user cannot grab is an object they cannot resize back.
    /// </remarks>
    public IEnumerable<(ShapeHandle Handle, GridRectLike Rect)> SelectedObjectHandles()
    {
        if (this.SelectedObjectBounds() is not { } bounds)
            yield break;

        var half = this.HandleSize / 2;

        GridRectLike At(double x, double y)
            => new(x - half, y - half, this.HandleSize, this.HandleSize);

        yield return (ShapeHandle.TopLeft, At(bounds.X, bounds.Y));
        yield return (ShapeHandle.Top, At(bounds.X + (bounds.Width / 2), bounds.Y));
        yield return (ShapeHandle.TopRight, At(bounds.Right, bounds.Y));
        yield return (ShapeHandle.Right, At(bounds.Right, bounds.Y + (bounds.Height / 2)));
        yield return (ShapeHandle.BottomRight, At(bounds.Right, bounds.Bottom));
        yield return (ShapeHandle.Bottom, At(bounds.X + (bounds.Width / 2), bounds.Bottom));
        yield return (ShapeHandle.BottomLeft, At(bounds.X, bounds.Bottom));
        yield return (ShapeHandle.Left, At(bounds.X, bounds.Y + (bounds.Height / 2)));
    }

    /// <summary>The handle under a point in viewport coordinates.</summary>
    public ShapeHandle HandleAt(double x, double y)
    {
        var (documentX, documentY) = this.ToFlow(x, y);

        foreach (var (handle, rect) in this.SelectedObjectHandles())
        {
            if (rect.Contains(documentX, documentY))
                return handle;
        }

        return ShapeHandle.None;
    }

    /// <summary>
    /// Starts a pointer interaction on an inline object, returning true when one was taken.
    /// </summary>
    /// <remarks>
    /// Returning false is the signal that the gesture belongs to the text caret instead, which is what
    /// lets a host call this first and fall through to ordinary click-and-drag selection without
    /// having to know anything about objects.
    /// </remarks>
    public bool BeginObjectDrag(double x, double y)
    {
        if (this.IsReadOnlyDocument)
            return false;

        // A handle on the current selection takes priority over whatever is underneath it: the
        // handles sit on the object's own edge, so the two always overlap.
        var handle = this.HandleAt(x, y);

        if (handle == ShapeHandle.None)
        {
            if (this.ObjectAt(x, y) is not { } hit)
            {
                this.ClearObjectSelection();
                return false;
            }

            this.SelectObject(hit);

            // Selecting is the whole gesture; a drag only starts from a handle, because an inline
            // object has no free position to be dragged to.
            return true;
        }

        if (this.SelectedInline is not { } inline)
            return false;

        this.dragging = handle;
        this.dragOriginX = x;
        this.dragOriginY = y;
        this.dragStartWidth = inline.Width;
        this.dragStartHeight = inline.Height;

        return true;
    }

    /// <summary>Extends a resize drag.</summary>
    /// <remarks>
    /// The size is always computed from where the drag <em>started</em> rather than from the previous
    /// move, so rounding cannot accumulate across a long drag and the object cannot creep.
    /// </remarks>
    public void DragObject(double x, double y)
    {
        if (this.dragging == ShapeHandle.None || this.selectedObject is not { } at)
            return;

        var dx = x - this.dragOriginX;
        var dy = y - this.dragOriginY;

        var width = this.dragStartWidth;
        var height = this.dragStartHeight;

        switch (this.dragging)
        {
            case ShapeHandle.Right or ShapeHandle.TopRight or ShapeHandle.BottomRight:
                width += dx;
                break;

            case ShapeHandle.Left or ShapeHandle.TopLeft or ShapeHandle.BottomLeft:
                width -= dx;
                break;
        }

        switch (this.dragging)
        {
            case ShapeHandle.Bottom or ShapeHandle.BottomLeft or ShapeHandle.BottomRight:
                height += dy;
                break;

            case ShapeHandle.Top or ShapeHandle.TopLeft or ShapeHandle.TopRight:
                height -= dy;
                break;
        }

        // A corner keeps the aspect ratio, which is what stops a dragged picture from being squashed.
        // An edge handle is the deliberate way to change one dimension alone.
        if (this.dragging is ShapeHandle.TopLeft or ShapeHandle.TopRight or ShapeHandle.BottomLeft or ShapeHandle.BottomRight
            && this.dragStartHeight > 0)
        {
            height = width * (this.dragStartHeight / this.dragStartWidth);
        }

        this.document.Execute(new ResizeInlineObjectCommand(
            at,
            Math.Max(MinimumObjectSize, width),
            Math.Max(MinimumObjectSize, height)));

        this.AfterEdit();
    }

    public void EndObjectDrag() => this.dragging = ShapeHandle.None;

    /// <summary>Removes the selected inline object.</summary>
    public void DeleteSelectedObject()
    {
        if (this.IsReadOnlyDocument || this.selectedObject is not { } at)
            return;

        this.document.Execute(new DeleteRangeCommand(new DocumentRange(at, at with { Offset = at.Offset + 1 })));
        this.ClearObjectSelection();
        this.Selection.MoveTo(at);
        this.AfterEdit();
    }

    /// <summary>How small a drag is allowed to make an object before it stops shrinking.</summary>
    /// <remarks>
    /// Not zero, and not one: an object dragged to nothing has no handles left to drag it back out by,
    /// so the floor is the size of a handle.
    /// </remarks>
    const double MinimumObjectSize = 12;

    /// <summary>A laid-out run's box in document coordinates.</summary>
    /// <remarks>
    /// An inline object sits on the baseline and runs upward from it, which is where the layout engine
    /// reserved its space and where the painter draws it.
    /// </remarks>
    static GridRectLike RunBounds(LaidOutParagraph paragraph, LaidOutLine line, LaidOutRun run)
    {
        var baseline = paragraph.Y + line.Y + line.Ascent;
        var height = run.Inline?.Height ?? run.Height;

        return new GridRectLike(paragraph.X + run.X, baseline - height, run.Width, height);
    }

    public void Undo()
    {
        this.document.Undo.Undo();
        this.ClampSelection();
        this.AfterEdit();
    }

    public void Redo()
    {
        this.document.Undo.Redo();
        this.ClampSelection();
        this.AfterEdit();
    }

    // ---- caret movement ----

    public void Move(CaretMove move, bool extend = false)
    {
        var target = this.Resolve(move);

        if (extend)
            this.Selection.ExtendTo(target);
        else
            this.Selection.MoveTo(target);

        // Vertical movement keeps its column; anything else resets it.
        if (move is not (CaretMove.Up or CaretMove.Down))
            this.desiredX = null;

        this.ScrollCaretIntoView();
        this.CaretMoved?.Invoke(this, EventArgs.Empty);
    }

    public void SelectAll()
    {
        var last = this.Document.Blocks.Count - 1;
        this.Selection.Select(DocumentPosition.Start, new DocumentPosition(last, this.LengthOf(last)));
    }

    /// <summary>
    /// Selects the word under <paramref name="position"/> — what a double-click does.
    /// </summary>
    /// <remarks>
    /// The gesture matters more than it looks. Every formatting command needs a range to act on, so
    /// without a way to select a word by pointing at it the only route to bolding one is a careful
    /// drag from one edge of it to the other. Selecting a word is the ordinary way to say which word.
    /// </remarks>
    public void SelectWordAt(DocumentPosition position)
    {
        var range = this.WordRangeAt(position);
        this.Selection.Select(range.Start, range.End);
    }

    /// <summary>Selects the whole paragraph — what a triple-click does.</summary>
    public void SelectParagraphAt(DocumentPosition position)
        => this.Selection.Select(
            position with { Offset = 0 },
            position with { Offset = this.LengthOf(position.Block) });

    /// <summary>The span of the word at <paramref name="position"/>, without changing the selection.</summary>
    public DocumentRange WordRangeAt(DocumentPosition position)
    {
        var text = this.TextOf(position.Block);
        if (text.Length == 0)
            return new DocumentRange(position, position);

        var (start, end) = WordBoundaries.RangeAt(text, position.Offset);
        return new DocumentRange(position with { Offset = start }, position with { Offset = end });
    }

    DocumentPosition Resolve(CaretMove move)
    {
        var caret = this.Selection.Focus;
        var length = this.LengthOf(caret.Block);

        switch (move)
        {
            case CaretMove.Left:
                if (caret.Offset > 0)
                    return caret with { Offset = caret.Offset - 1 };

                return caret.Block > 0
                    ? new DocumentPosition(caret.Block - 1, this.LengthOf(caret.Block - 1))
                    : caret;

            case CaretMove.Right:
                if (caret.Offset < length)
                    return caret with { Offset = caret.Offset + 1 };

                return caret.Block + 1 < this.Document.Blocks.Count
                    ? new DocumentPosition(caret.Block + 1, 0)
                    : caret;

            case CaretMove.LineStart:
                return caret with { Offset = this.LineBoundsAt(caret).Start };

            case CaretMove.LineEnd:
                return caret with { Offset = this.LineBoundsAt(caret).End };

            case CaretMove.DocumentStart:
                return DocumentPosition.Start;

            case CaretMove.DocumentEnd:
                var last = this.Document.Blocks.Count - 1;
                return new DocumentPosition(last, this.LengthOf(last));

            case CaretMove.WordLeft:
                return this.WordBoundary(caret, forward: false);

            case CaretMove.WordRight:
                return this.WordBoundary(caret, forward: true);

            case CaretMove.Up:
            case CaretMove.Down:
                return this.VerticalMove(caret, move == CaretMove.Down);

            default:
                return caret;
        }
    }

    /// <summary>
    /// Moves the caret one visual line, not one paragraph.
    /// </summary>
    /// <remarks>
    /// A wrapped paragraph is many lines, so moving by block would jump past all of them. The column
    /// is remembered across consecutive vertical moves, which is what stops the caret drifting left
    /// when it passes through a short line.
    /// </remarks>
    DocumentPosition VerticalMove(DocumentPosition caret, bool down)
    {
        var caretRect = this.CaretRect(caret);
        this.desiredX ??= caretRect.X;

        var probeY = down
            ? caretRect.Y + caretRect.Height + 1
            : caretRect.Y - 1;

        var target = this.PositionAt(this.desiredX.Value, probeY);
        return target ?? caret;
    }

    DocumentPosition WordBoundary(DocumentPosition caret, bool forward)
    {
        var text = this.TextOf(caret.Block);
        var offset = caret.Offset;

        if (forward)
        {
            while (offset < text.Length && char.IsWhiteSpace(text[offset]))
                offset++;

            while (offset < text.Length && !char.IsWhiteSpace(text[offset]))
                offset++;

            return offset == caret.Offset && caret.Block + 1 < this.Document.Blocks.Count
                ? new DocumentPosition(caret.Block + 1, 0)
                : caret with { Offset = offset };
        }

        while (offset > 0 && char.IsWhiteSpace(text[offset - 1]))
            offset--;

        while (offset > 0 && !char.IsWhiteSpace(text[offset - 1]))
            offset--;

        return offset == caret.Offset && caret.Block > 0
            ? new DocumentPosition(caret.Block - 1, this.LengthOf(caret.Block - 1))
            : caret with { Offset = offset };
    }

    // ---- hit testing and geometry ----

    /// <summary>The document position under a point in viewport coordinates, or null when there is none.</summary>
    public DocumentPosition? PositionAt(double x, double y)
    {
        var (documentX, documentY) = this.ToFlow(x, y);

        LaidOutParagraph? best = null;
        var index = -1;
        var blocks = this.Blocks;

        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is not LaidOutParagraph block)
                continue;

            if (documentY >= block.Y && documentY <= block.Y + block.Height)
            {
                best = block;
                index = i;
                break;
            }

            // Remember the last block above the point, so clicking in the gap below the document
            // still lands somewhere sensible rather than nowhere.
            if (block.Y <= documentY)
            {
                best = block;
                index = i;
            }
        }

        if (best is null || index < 0)
            return null;

        var localY = documentY - best.Y;
        var line = best.Lines.FirstOrDefault(l => localY >= l.Y && localY <= l.Y + l.Height)
            ?? (localY < 0 ? best.Lines[0] : best.Lines[^1]);

        return new DocumentPosition(index, this.OffsetInLine(line, documentX - best.X));
    }

    /// <summary>Finds the character boundary nearest an x within a line.</summary>
    int OffsetInLine(LaidOutLine line, double x)
    {
        if (line.Runs.Count == 0)
            return line.SourceOffset;

        foreach (var run in line.Runs)
        {
            if (x > run.X + run.Width)
                continue;

            if (x < run.X)
                return run.SourceOffset;

            // An object is one character and cannot be clicked into, only on one side or the other.
            if (run.Inline is not null)
                return run.SourceOffset + (x > run.X + (run.Width / 2) ? 1 : 0);

            // Walk the run's characters and take the boundary the click is closest to, so clicking the
            // right half of a glyph puts the caret after it rather than before.
            var local = 0;
            var previous = 0d;
            for (var i = 1; i <= run.Text.Length; i++)
            {
                var width = this.Measurer.Measure(run.Text.AsSpan(0, i), run.Style).Width;
                if (run.X + width >= x)
                {
                    local = x - (run.X + previous) < (run.X + width) - x ? i - 1 : i;
                    break;
                }

                previous = width;
                local = i;
            }

            return run.SourceOffset + local;
        }

        return line.SourceEnd;
    }

    /// <summary>The caret's rectangle in document coordinates.</summary>
    public GridRectLike CaretRect(DocumentPosition position)
    {
        var blocks = this.Blocks;
        if (position.Block < 0 || position.Block >= blocks.Count || blocks[position.Block] is not LaidOutParagraph paragraph)
            return new GridRectLike(0, 0, 1, 16);

        var line = paragraph.Lines.LastOrDefault(l => position.Offset >= l.SourceOffset) ?? paragraph.Lines[0];
        var x = paragraph.X;

        foreach (var run in line.Runs)
        {
            if (position.Offset < run.SourceOffset)
                break;

            // An inline object has no text to measure through: the caret is either at its left edge
            // or, once the offset has passed it, at its right.
            if (run.Inline is not null)
            {
                x = paragraph.X + run.X + (position.Offset > run.SourceOffset ? run.Width : 0);
                continue;
            }

            var local = Math.Min(position.Offset - run.SourceOffset, run.Text.Length);
            x = paragraph.X + run.X + this.Measurer.Measure(run.Text.AsSpan(0, local), run.Style).Width;
        }

        if (line.Runs.Count == 0)
            x = paragraph.X;

        return new GridRectLike(x, paragraph.Y + line.Y, 1.5, line.Height);
    }

    (int Start, int End) LineBoundsAt(DocumentPosition position)
    {
        if (this.Blocks.ElementAtOrDefault(position.Block) is not LaidOutParagraph paragraph)
            return (0, 0);

        var line = paragraph.Lines.LastOrDefault(l => position.Offset >= l.SourceOffset) ?? paragraph.Lines[0];
        return (line.SourceOffset, line.SourceEnd);
    }

    /// <summary>Rectangles covering the selection, for the painter to fill.</summary>
    public IEnumerable<GridRectLike> SelectionRects() => this.RangeRects(this.Selection.Range);

    /// <summary>
    /// Rectangles covering every find match on screen, so the painter can wash them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the blocks currently visible are measured. A search over a long document can turn up
    /// hundreds of hits, and laying out every one of them per frame would put the cost of the whole
    /// document into each repaint for rectangles that are nowhere near the viewport.
    /// </para>
    /// <para>
    /// The match the selection is sitting on is left out, so it is drawn in the selection's colour
    /// alone rather than in both. Stacking the two washes made the current hit a muddy blend of them
    /// and the hardest one on the page to pick out, which is the opposite of what it is for. The test
    /// is what the selection actually covers rather than which match is active, so clicking away from
    /// a hit brings its wash back instead of leaving a gap in the highlights.
    /// </para>
    /// </remarks>
    public IEnumerable<GridRectLike> FindMatchRects()
    {
        if (!this.Find.IsSearching)
            yield break;

        var (first, last) = this.VisibleBlockRange();
        var selection = this.Selection.Range;

        foreach (var match in this.Find.Matches)
        {
            if (match.Block < first)
                continue;

            // The match list is in block order, so the first one below the fold ends the walk.
            if (match.Block > last)
                yield break;

            if (!selection.IsEmpty && selection == match.Range)
                continue;

            foreach (var rect in this.RangeRects(match.Range))
                yield return rect;
        }
    }

    /// <summary>
    /// One rectangle per line the range covers, in document coordinates.
    /// </summary>
    /// <remarks>
    /// Per line rather than one box from start to end: a range that wraps would otherwise be drawn as
    /// a rectangle spanning everything between the two points, including the text to the left of the
    /// start and to the right of the end.
    /// </remarks>
    IEnumerable<GridRectLike> RangeRects(DocumentRange range)
    {
        if (range.IsEmpty)
            yield break;

        for (var block = range.Start.Block; block <= range.End.Block; block++)
        {
            if (this.Blocks.ElementAtOrDefault(block) is not LaidOutParagraph paragraph)
                continue;

            var from = block == range.Start.Block ? range.Start.Offset : 0;
            var to = block == range.End.Block ? range.End.Offset : int.MaxValue;

            foreach (var line in paragraph.Lines)
            {
                var lineFrom = Math.Max(from, line.SourceOffset);
                var lineTo = Math.Min(to, line.SourceEnd);
                if (lineTo <= lineFrom)
                    continue;

                var start = this.CaretRect(new DocumentPosition(block, lineFrom));
                var end = this.CaretRect(new DocumentPosition(block, lineTo));

                yield return new GridRectLike(start.X, paragraph.Y + line.Y, Math.Max(2, end.X - start.X), line.Height);
            }
        }
    }

    /// <summary>
    /// Selects a find match and brings it on screen.
    /// </summary>
    /// <remarks>
    /// Selected rather than landed beside, for the same reason stepping through misspellings selects
    /// the word: what a person does with a hit they have found is act on it.
    /// </remarks>
    internal void SelectFindMatch(DocumentFindMatch match)
    {
        // Any object selection has to go, or the caret is drawn suppressed and the hit looks unfound.
        this.ClearObjectSelection();

        this.Selection.Select(
            new DocumentPosition(match.Block, match.Start),
            new DocumentPosition(match.Block, match.End));

        this.ScrollCaretIntoView();
        this.RaiseChanged();
    }

    // ---- touch ----

    /// <summary>
    /// How far from a handle's centre still counts as grabbing it.
    /// </summary>
    /// <remarks>
    /// Larger than the handle is drawn. A caret is one pixel wide and its handle marks that edge
    /// exactly, which is what makes it usable for placing a selection boundary between two letters -
    /// but it is also the hardest thing on the page to land a fingertip on, and under touch a miss
    /// pans the page out from under the selection being adjusted.
    /// </remarks>
    public const double TouchHandleGripPixels = 22;

    /// <summary>True once a finger has been seen, so the surface should draw touch affordances.</summary>
    /// <remarks>
    /// Both kinds of pointer turn up in one session - an iPad with a keyboard, a laptop with a
    /// touchscreen - so this follows whatever was used last rather than being decided per platform.
    /// </remarks>
    public bool UsesTouch { get; set; }

    /// <summary>
    /// The grab handles for the current selection, in flow coordinates. Empty when nothing is selected.
    /// </summary>
    /// <remarks>
    /// One per end, sitting under the caret that marks it, which is where a text handle goes on both
    /// mobile platforms - over the text it would cover the letters the user is trying to select up to.
    /// </remarks>
    public IReadOnlyList<GridRectLike> TouchHandleRects()
    {
        if (this.Selection.IsEmpty)
            return [];

        return
        [
            HandleAt(this.CaretRect(this.Selection.Anchor)),
            HandleAt(this.CaretRect(this.Selection.Focus))
        ];

        static GridRectLike HandleAt(GridRectLike caret)
            => new(
                caret.X - (TouchHandleGripPixels / 2),
                caret.Bottom - (TouchHandleGripPixels / 2),
                TouchHandleGripPixels,
                TouchHandleGripPixels);
    }

    /// <summary>Which handle, if either, a control-space point grabs.</summary>
    /// <remarks>
    /// Focus is tested first: the two coincide while the selection is empty, and on a selection just
    /// made by dragging it is the end the user was last moving.
    /// </remarks>
    public TextHandle? TouchHandleAt(double x, double y)
    {
        var handles = this.TouchHandleRects();
        if (handles.Count != 2)
            return null;

        var (flowX, flowY) = this.ToFlow(x, y);

        if (handles[1].Contains(flowX, flowY))
            return TextHandle.Focus;

        return handles[0].Contains(flowX, flowY) ? TextHandle.Anchor : null;
    }

    /// <summary>Moves one end of the selection to the position under a control-space point.</summary>
    public void DragTouchHandle(TextHandle handle, double x, double y)
    {
        if (this.PositionAt(x, y) is not { } position)
            return;

        if (handle == TextHandle.Focus)
        {
            this.Selection.ExtendTo(position);
        }
        else
        {
            // Moving the anchor is the same operation with the ends swapped: re-anchor on the fixed
            // end and extend back to the finger, which is also what lets a drag past the other end
            // flip the selection rather than collapsing it.
            var fixedEnd = this.Selection.Focus;
            this.Selection.MoveTo(fixedEnd);
            this.Selection.ExtendTo(position);
        }
    }

    void ScrollCaretIntoView()
    {
        var caret = this.CaretRect(this.Selection.Focus);
        var pagination = this.Pagination;

        // The caret is in flow coordinates; scrolling happens in the view's.
        var caretTop = pagination.FlowToView(caret.Y);
        var caretBottom = pagination.FlowToView(caret.Y + caret.Height);

        var top = this.Viewport.ScrollY;
        var bottom = top + this.Viewport.Height;

        if (caretTop < top)
            this.ScrollTo(caretTop - 8);
        else if (caretBottom > bottom)
            this.ScrollTo(caretBottom - this.Viewport.Height + 8);
    }

    // ---- state ----

    string TextOf(int block)
        => this.Document.Blocks.ElementAtOrDefault(block) is DocumentParagraph paragraph ? paragraph.PlainText : string.Empty;

    int LengthOf(int block) => this.TextOf(block).Length;

    void ClampSelection()
    {
        var blocks = this.Document.Blocks.Count;
        if (blocks == 0)
            return;

        var block = Math.Clamp(this.Selection.Focus.Block, 0, blocks - 1);
        var offset = Math.Clamp(this.Selection.Focus.Offset, 0, this.LengthOf(block));
        this.Selection.MoveTo(new DocumentPosition(block, offset));
    }

    void AfterEdit()
    {
        // Paragraph indices below an edit can shift, so their cached spelling no longer belongs to the
        // text it was computed from.
        this.Spelling.InvalidateFrom(this.Selection.Focus.Block);
        this.ScheduleSpellCheck();

        this.InvalidateLayout();
        this.Find.Invalidate();
        this.RefreshCaretFormat();
        this.ScrollCaretIntoView();
        this.RaiseChanged();
    }

    /// <summary>Reads the formatting under the caret so a toolbar can show what is active.</summary>
    void RefreshCaretFormat()
    {
        if (this.Document.Blocks.ElementAtOrDefault(this.Selection.Focus.Block) is not DocumentParagraph paragraph)
            return;

        // The character *before* the caret is what Word reports, so typing continues the run the caret
        // just left rather than the one it is about to enter.
        var offset = Math.Max(0, this.Selection.Focus.Offset - 1);
        var cursor = 0;
        var style = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Style : TextStyle.Default;

        foreach (var run in paragraph.Runs)
        {
            if (run.IsBreak)
                continue;

            if (offset < cursor + run.Text.Length || run.Text.Length == 0)
            {
                style = run.Style;
                break;
            }

            cursor += run.Text.Length;
            style = run.Style;
        }

        var format = new CaretFormat
        {
            FontFamily = style.FontFamily,
            FontSize = OoxmlUnits.PixelsToPointsApprox(style.FontSize),
            Bold = style.Bold,
            Italic = style.Italic,
            Underline = style.Underline != UnderlineStyle.None,
            Strike = style.Strike,
            Color = style.Color,
            Highlight = style.Highlight,
            Alignment = paragraph.Format.Alignment,
            StyleName = paragraph.StyleName,

            List = paragraph.List switch
            {
                null => ListStyle.None,
                { IsBullet: true } => ListStyle.Bullet,
                _ => ListStyle.Numbered
            },

            ListLevel = paragraph.List?.Numbering?.Level ?? 0
        };

        // Layered over what the document says, in the order they were chosen. Without this the toolbar
        // would snap back to the run under the caret the moment anything raised Changed - which the
        // pending change itself does - and the choice would look like it had been ignored.
        foreach (var change in this.pending)
            format = change.PreviewCaret?.Invoke(format) ?? format;

        this.CaretFormat = format;
    }
}

/// <summary>A rectangle in document coordinates. Named to avoid clashing with the spreadsheet's.</summary>
public readonly record struct GridRectLike(double X, double Y, double Width, double Height)
{
    public double Right => this.X + this.Width;
    public double Bottom => this.Y + this.Height;

    public bool Contains(double x, double y)
        => x >= this.X && x <= this.Right && y >= this.Y && y <= this.Bottom;
}

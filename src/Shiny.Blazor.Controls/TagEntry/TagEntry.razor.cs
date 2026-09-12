using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Shiny.Blazor.Controls;

/// <summary>
/// A free-text tags field: type and press Enter — or a delimiter — to commit each value as a removable
/// chip, all inside one bordered box.
/// </summary>
/// <remarks>
/// <para>
/// The parameter surface mirrors the MAUI <c>TagEntry</c>, with one difference the platforms force: the
/// browser has a real <c>keydown</c>, so Backspace against empty text is read directly rather than
/// through the zero-width-space sentinel MAUI needs.
/// </para>
/// <para>
/// Every decision the field makes — committing, de-duplicating, capping, splitting a paste — is an
/// ordinary method on this class rather than logic buried in an event handler, so it can be driven
/// without a renderer.
/// </para>
/// </remarks>
public partial class TagEntry
{
    readonly List<string> current = new();

    ElementReference inputRef;
    IReadOnlyList<string>? lastTagsParameter;
    bool preventDefault;
    bool focused;
    bool autoFocusPending;
    bool hasRendered;

    /// <summary>The committed tags. Supports <c>@bind-Tags</c>.</summary>
    [Parameter] public IReadOnlyList<string>? Tags { get; set; }

    /// <inheritdoc cref="Tags"/>
    [Parameter] public EventCallback<IReadOnlyList<string>> TagsChanged { get; set; }

    /// <summary>
    /// Which strings commit a tag besides Enter, and what a pasted value splits on. A comma by default;
    /// an <em>empty</em> list means Enter only, which is how a tag gets to contain a comma.
    /// </summary>
    [Parameter] public IReadOnlyList<string> Delimiters { get; set; } = [","];

    /// <summary>
    /// How many tags may be committed, or 0 for no limit. At the cap further commits are ignored and the
    /// typed text stays put, so nothing the user wrote is thrown away without them seeing it.
    /// </summary>
    [Parameter] public int MaxTags { get; set; }

    /// <summary>Whether the same tag may be committed twice. Off by default — duplicates are dropped quietly.</summary>
    [Parameter] public bool AllowDuplicates { get; set; }

    /// <summary>Whether <c>Design</c> and <c>design</c> count as two tags. Off by default.</summary>
    [Parameter] public bool CaseSensitiveDuplicates { get; set; }

    /// <summary>Whether a committed tag has its surrounding whitespace removed. On by default.</summary>
    [Parameter] public bool TrimWhitespace { get; set; } = true;

    /// <summary>Whether Backspace against empty text removes the newest chip. On by default.</summary>
    [Parameter] public bool BackspaceRemovesTag { get; set; } = true;

    /// <summary>
    /// Whether leaving the field commits what was typed. On by default: a half-typed tag left behind on
    /// submit is a value the user believed they had entered.
    /// </summary>
    [Parameter] public bool CommitOnBlur { get; set; } = true;

    /// <summary>
    /// Keeps the chips visible and crisp but blocks adding and removing. Unlike <see cref="Disabled"/>,
    /// which also dims the field — read-only is "these are the values", disabled is "not right now".
    /// </summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Dims the whole field and makes it inert.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Prompt shown in the typing area while it is empty.</summary>
    [Parameter] public string? Placeholder { get; set; }

    /// <summary>Focuses the typing area as soon as the field appears — for one revealed by an action.</summary>
    [Parameter] public bool AutoFocus { get; set; }

    /// <summary>
    /// Replaces a chip's label — the tag string is the context. The remove button is not part of the
    /// template: every chip keeps the same way out, however it is drawn.
    /// </summary>
    [Parameter] public RenderFragment<string>? ChipContent { get; set; }

    /// <summary>
    /// Vets a tag before it is committed — return false to refuse it. It runs after trimming and after
    /// the duplicate and <see cref="MaxTags"/> rules, so it only ever sees a tag that would otherwise
    /// have been added, and a refusal leaves the typed text where it is to be corrected.
    /// </summary>
    [Parameter] public Func<string, bool>? TagValidator { get; set; }

    /// <summary>Raised after a tag is committed.</summary>
    [Parameter] public EventCallback<string> TagAdded { get; set; }

    /// <summary>Raised after a tag is removed.</summary>
    [Parameter] public EventCallback<string> TagRemoved { get; set; }

    /// <summary>Chip fill. Unset follows the theme's secondary container.</summary>
    [Parameter] public string? ChipBackgroundColor { get; set; }

    /// <summary>Chip ink. Unset follows the theme.</summary>
    [Parameter] public string? ChipTextColor { get; set; }

    /// <summary>The box's outline. Unset follows the theme, and takes the primary colour on focus.</summary>
    [Parameter] public string? BorderColor { get; set; }

    /// <summary>The box's corner radius in px. The default, <c>-1</c>, follows the theme.</summary>
    [Parameter] public double CornerRadius { get; set; } = -1d;

    /// <summary>Extra classes on the field.</summary>
    [Parameter] public string? CssClass { get; set; }

    /// <summary>
    /// Anything else lands on the inner <c>input</c> — so <c>aria-invalid</c>, a name, or a Field pairing
    /// behaves exactly as it would on any other input.
    /// </summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }


    // ---------------------------------------------------------------------------------------------
    // State
    // ---------------------------------------------------------------------------------------------

    /// <summary>The committed tags as the field currently holds them.</summary>
    public IReadOnlyList<string> Current => this.current;

    /// <summary>The text currently being typed, before it is committed.</summary>
    public string PendingText { get; private set; } = String.Empty;


    protected override void OnInitialized()
    {
        this.autoFocusPending = this.AutoFocus;
        this.TakeTagsParameter();
    }


    protected override void OnParametersSet() => this.TakeTagsParameter();


    /// <summary>
    /// Adopts the bound list when the parent actually changed it. The shadow is what makes "did the
    /// parent change it" answerable: without it, a parent re-supplying the list it already had would
    /// undo a tag the moment the field re-rendered.
    /// </summary>
    internal void TakeTagsParameter()
    {
        if (this.Tags is null || ReferenceEquals(this.Tags, this.lastTagsParameter))
            return;

        this.lastTagsParameter = this.Tags;
        this.current.Clear();
        this.current.AddRange(this.Tags);
    }


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        this.hasRendered = true;

        if (this.autoFocusPending)
        {
            this.autoFocusPending = false;
            await this.FocusAsync();
        }
    }


    /// <summary>
    /// Re-render, but only once there is something to re-render: asking for a repaint before the render
    /// handle exists throws, and every commit path runs through here.
    /// </summary>
    void Repaint()
    {
        if (this.hasRendered)
            this.StateHasChanged();
    }


    // ---------------------------------------------------------------------------------------------
    // Public surface
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Moves the keyboard to the typing area — never to a chip or its remove button. Clearing the tags
    /// and calling this hands the field straight back, ready for the replacement list.
    /// </summary>
    public ValueTask FocusAsync() => this.inputRef.FocusAsync();


    /// <summary>Commits whatever is currently typed, exactly as pressing Enter would.</summary>
    public Task<bool> CommitPendingAsync() => this.ProcessInputAsync(this.PendingText, commitTrailing: true);


    /// <summary>Adds a tag as though it had been typed and committed. Returns false if it was refused.</summary>
    public async Task<bool> AddTagAsync(string tag)
    {
        if (!this.TryStage(tag, out var staged))
            return false;

        this.current.Add(staged);
        await this.PublishAsync();

        if (this.TagAdded.HasDelegate)
            await this.TagAdded.InvokeAsync(staged);

        return true;
    }


    /// <summary>Removes the first matching tag. Returns false when it was not there.</summary>
    public async Task<bool> RemoveTagAsync(string tag)
    {
        if (this.ReadOnly || this.Disabled)
            return false;

        var index = this.IndexOf(tag);
        if (index < 0)
            return false;

        var removed = this.current[index];
        this.current.RemoveAt(index);
        await this.PublishAsync();

        if (this.TagRemoved.HasDelegate)
            await this.TagRemoved.InvokeAsync(removed);

        return true;
    }


    /// <summary>Removes every tag.</summary>
    public async Task ClearAsync()
    {
        if (this.current.Count == 0)
            return;

        this.current.Clear();
        await this.PublishAsync();
    }


    // ---------------------------------------------------------------------------------------------
    // Committing
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Runs a piece of input through the delimiters.
    /// </summary>
    /// <param name="text">The whole typed text.</param>
    /// <param name="commitTrailing">
    /// Whether the fragment after the last delimiter is committed too. It is for Enter and for a paste —
    /// "a, b, c" is three tags, and holding "c" back would be an odd way to read that. It is not for
    /// ordinary typing, where the trailing fragment is what the user is still writing.
    /// </param>
    internal async Task<bool> ProcessInputAsync(string? text, bool commitTrailing)
    {
        if (this.ReadOnly || this.Disabled || String.IsNullOrEmpty(text))
            return false;

        // What is in the box right now, before any of it is turned into tags. Owning this here rather
        // than in the input handler is what lets the whole commit path be driven without a renderer.
        this.PendingText = text;

        var committed = false;

        if (this.Delimiters is null || this.Delimiters.Count == 0)
        {
            // Enter-only mode: commas and semicolons are part of the tag, which is the whole point of
            // handing this an empty delimiter list.
            if (!commitTrailing)
                return false;

            committed = await this.AddTagAsync(text);
            if (committed)
                this.PendingText = String.Empty;

            return committed;
        }

        var parts = text.Split(this.Delimiters.ToArray(), StringSplitOptions.None);
        var last = parts.Length - 1;

        for (var i = 0; i < parts.Length; i++)
        {
            if (i == last && !commitTrailing)
                break;

            committed |= await this.AddTagAsync(parts[i]);
        }

        // No tag begins with whitespace while TrimWhitespace is on, so neither does the text on its way to
        // becoming one: typing "a, b" would otherwise leave the caret behind a space that is about to be
        // trimmed off anyway.
        var remainder = commitTrailing
            ? String.Empty
            : this.TrimWhitespace ? parts[last].TrimStart() : parts[last];

        // A refused trailing fragment stays typed so it can be corrected; everything before a delimiter
        // has been dealt with either way.
        if (committed || parts.Length > 1 || (!commitTrailing && remainder != text))
            this.PendingText = remainder;

        return committed;
    }


    /// <summary>Applies every rule that can refuse a tag, and hands back the value that would be stored.</summary>
    bool TryStage(string? raw, out string staged)
    {
        staged = String.Empty;

        if (this.ReadOnly || this.Disabled)
            return false;

        var tag = this.TrimWhitespace ? raw?.Trim() : raw;
        if (String.IsNullOrEmpty(tag))
            return false;

        if (!this.AllowDuplicates && this.IndexOf(tag) >= 0)
            return false;

        if (this.MaxTags > 0 && this.current.Count >= this.MaxTags)
            return false;

        if (this.TagValidator is not null && !this.TagValidator(tag))
            return false;

        staged = tag;
        return true;
    }


    int IndexOf(string tag)
    {
        var comparison = this.CaseSensitiveDuplicates
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        for (var i = 0; i < this.current.Count; i++)
        {
            if (String.Equals(this.current[i], tag, comparison))
                return i;
        }

        return -1;
    }


    async Task PublishAsync()
    {
        var snapshot = this.current.ToList();

        // The shadow moves with it, or the next parameter pass would read the parent's older list as a
        // change and undo what was just committed.
        this.lastTagsParameter = snapshot;
        this.Tags = snapshot;

        if (this.TagsChanged.HasDelegate)
            await this.TagsChanged.InvokeAsync(snapshot);

        this.Repaint();
    }


    // ---------------------------------------------------------------------------------------------
    // Input
    // ---------------------------------------------------------------------------------------------

    async Task OnInputAsync(ChangeEventArgs e)
    {
        var text = e.Value?.ToString() ?? String.Empty;

        // More than one character arrived at once, so this is a paste rather than a keystroke - and a
        // pasted "a, b, c" is three tags, trailing fragment included. This is also why there is no
        // @onpaste handler: ClipboardEventArgs does not carry the pasted text anyway, and the input
        // event that follows it does.
        var inserted = text.Length - this.PendingText.Length;

        this.PendingText = text;
        await this.ProcessInputAsync(text, commitTrailing: inserted > 1);
        this.Repaint();
    }


    async Task OnKeyDownAsync(KeyboardEventArgs e)
    {
        // preventDefault is read at render time rather than when the handler runs, so it can only ever
        // be armed for the *next* keystroke. Enter is the only key worth arming - it submits a
        // surrounding form - and arming anything else would stop the character being typed at all.
        this.preventDefault = e.Key == "Enter";

        switch (e.Key)
        {
            case "Enter":
                await this.CommitPendingAsync();
                break;

            case "Backspace" when this.BackspaceRemovesTag && this.PendingText.Length == 0:
                if (this.current.Count > 0)
                    await this.RemoveTagAsync(this.current[^1]);
                break;
        }
    }


    void OnFocus() => this.focused = true;


    async Task OnBlurAsync()
    {
        this.focused = false;

        if (this.CommitOnBlur)
            await this.CommitPendingAsync();
    }


    /// <summary>
    /// A click anywhere in the box goes to the typing area. The box is mostly padding, and a field whose
    /// whitespace does nothing reads as a field that is not editable.
    /// </summary>
    async Task OnBoxClickedAsync()
    {
        if (this.Disabled)
            return;

        await this.FocusAsync();
    }


    // ---------------------------------------------------------------------------------------------
    // Rendering
    // ---------------------------------------------------------------------------------------------

    string CssClasses
    {
        get
        {
            var sb = new StringBuilder("shiny-tags");

            if (this.Disabled)
                sb.Append(" is-disabled");

            if (this.ReadOnly)
                sb.Append(" is-readonly");

            if (this.focused)
                sb.Append(" is-focused");

            if (!String.IsNullOrEmpty(this.CssClass))
                sb.Append(' ').Append(this.CssClass);

            return sb.ToString();
        }
    }


    string? InlineStyle
    {
        get
        {
            var sb = new StringBuilder();

            // Only what the caller explicitly set: an inline declaration beats the scoped stylesheet
            // outright, so writing these unconditionally would make every themed rule dead on arrival.
            if (this.CornerRadius >= 0)
                sb.Append(CultureInfo.InvariantCulture, $"--shiny-tags-radius:{this.CornerRadius}px;");

            if (!String.IsNullOrEmpty(this.BorderColor))
                sb.Append("--shiny-tags-stroke:").Append(this.BorderColor).Append(';');

            if (!String.IsNullOrEmpty(this.ChipBackgroundColor))
                sb.Append("--shiny-tags-chip-bg:").Append(this.ChipBackgroundColor).Append(';');

            if (!String.IsNullOrEmpty(this.ChipTextColor))
                sb.Append("--shiny-tags-chip-fg:").Append(this.ChipTextColor).Append(';');

            return sb.Length == 0 ? null : sb.ToString();
        }
    }
}

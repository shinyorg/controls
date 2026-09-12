using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>
/// A free-text tags field: type and press Enter — or a delimiter — to commit each value as a removable
/// chip, all inside one bordered box.
/// </summary>
/// <remarks>
/// <para>
/// Reach for it when the values are the user's own words. When they have to come from a known list, an
/// <see cref="AutoCompleteEntry"/> is the control that offers one; this deliberately has no suggestion
/// popup, because a field that both accepts anything and suggests something is a field nobody can tell
/// the rules of.
/// </para>
/// <para>
/// Pasting <c>"a, b, c"</c> commits three tags. Typing a delimiter commits what is in front of it and
/// leaves the caret where it was. Backspace against empty text removes the newest chip — see
/// <see cref="BackspaceRemovesTag"/>, which is not as simple as it sounds on MAUI.
/// </para>
/// </remarks>
/// <example>
/// <code language="xaml">
/// &lt;shiny:TagEntry Tags="{Binding Topics}"
///                 Placeholder="Add a topic..."
///                 MaxTags="5" /&gt;
/// </code>
/// </example>
public partial class TagEntry : ContentView
{
    /// <summary>
    /// The zero-width space that makes Backspace detectable. See <see cref="BackspaceRemovesTag"/>.
    /// </summary>
    internal const string Sentinel = "​";

    const double DefaultMinimumHeight = 48;

    readonly Border border;
    readonly TagWrapLayout wrap;
    readonly BorderlessEntry entry;
    readonly SolidColorBrush strokeBrush;
    readonly BoxView strokeProbe;
    readonly List<TagChipView> chips = new();

    INotifyCollectionChanged? tagsNotifier;
    bool suppressTextChanged;
    bool rebuilding;
    bool isFocused;

    public TagEntry()
    {
        this.entry = new BorderlessEntry
        {
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            ReturnType = ReturnType.Done
        }.WithFontSize(ShinyThemeKeys.Type.BodyLargeSize);

        this.entry.SetDynamicResource(Entry.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
        this.entry.SetDynamicResource(Entry.PlaceholderColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        this.entry.TextChanged += this.OnEntryTextChanged;
        this.entry.Completed += this.OnEntryCompleted;
        this.entry.Focused += this.OnEntryFocused;
        this.entry.Unfocused += this.OnEntryUnfocused;

        (this.strokeBrush, this.strokeProbe) = ThemeProbe.Create();

        this.wrap = new TagWrapLayout
        {
            Padding = new Thickness(8, 6)
        };
        this.wrap.Add(this.strokeProbe);
        this.wrap.Add(this.entry);

        this.border = new Border
        {
            Content = this.wrap,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center
        }.WithStrokeThickness(ShinyThemeKeys.Border.Thin);

        this.border.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);
        this.border.Stroke = this.strokeBrush;

        this.Content = this.border;
        this.MinimumHeightRequest = DefaultMinimumHeight;

        // Assigned after the children exist, for the reason FabMenu gives: the property-changed handler
        // rebuilds the chips, which needs the wrap layout to be there. Created here rather than in a
        // defaultValueCreator, because a lazily-created default never raises propertyChanged and the
        // CollectionChanged hook wired there would never run.
        this.Tags = new ObservableCollection<string>();
        this.Delimiters = new ObservableCollection<string> { "," };

        this.ApplyCornerRadius();
        this.ApplyStroke();
        this.ApplyEntryState();

        // Last line: replays any styled property that was applied before the children existed.
        // See StyleGuard.
        StyleGuard.MarkReady(this, typeof(TagEntry));
    }


    // ---------------------------------------------------------------------------------------------
    // Public surface
    // ---------------------------------------------------------------------------------------------

    /// <summary>Raised before a tag is committed. Cancel it to refuse the value.</summary>
    public event EventHandler<TagAddingEventArgs>? TagAdding;

    /// <summary>Raised after a tag is committed.</summary>
    public event EventHandler<TagEventArgs>? TagAdded;

    /// <summary>Raised after a tag is removed.</summary>
    public event EventHandler<TagEventArgs>? TagRemoved;

    /// <summary>Raised whenever the list changes, however it changed.</summary>
    public event EventHandler? TagsChanged;

    /// <summary>
    /// Moves the keyboard to the typing area — never to a chip or its remove button. Clearing the tags
    /// and calling this hands the field straight back, ready for the replacement list.
    /// </summary>
    public new bool Focus() => this.entry.Focus();

    /// <summary>The text currently being typed, before it is committed.</summary>
    public string PendingText => Strip(this.entry.Text);

    /// <summary>
    /// The inner editor. Internal, so a test can type into the control the way a user does rather than
    /// through a seam that only exists for tests.
    /// </summary>
    internal Entry Editor => this.entry;

    /// <summary>The chips currently on screen, in order. Internal, for the same reason.</summary>
    internal IReadOnlyList<string> ChipTags => this.chips.Select(x => x.Tag).ToList();

    /// <summary>
    /// Commits whatever is currently typed, exactly as pressing Enter would. Returns false if there was
    /// nothing to commit or the value was refused.
    /// </summary>
    public bool CommitPending() => this.ProcessInput(this.PendingText, commitTrailing: true);

    /// <summary>Adds a tag as though it had been typed and committed. Returns false if it was refused.</summary>
    public bool AddTag(string tag) => this.TryCommit(tag);

    /// <summary>Removes the first matching tag. Returns false when it was not there.</summary>
    public bool RemoveTag(string tag)
    {
        var tags = this.Tags;
        if (tags is null)
            return false;

        var index = this.IndexOf(tags, tag);
        if (index < 0)
            return false;

        return this.RemoveAt(index);
    }


    // ---------------------------------------------------------------------------------------------
    // Committing
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Runs a piece of input through the delimiters.
    /// </summary>
    /// <param name="text">The whole typed text, sentinel already stripped.</param>
    /// <param name="commitTrailing">
    /// Whether the fragment after the last delimiter is committed too. It is for Enter and for a paste —
    /// "a, b, c" is three tags, and holding "c" back would be an odd way to read that. It is not for
    /// ordinary typing, where the trailing fragment is simply what the user is still writing.
    /// </param>
    internal bool ProcessInput(string? text, bool commitTrailing)
    {
        if (this.IsReadOnly || String.IsNullOrEmpty(text))
            return false;

        var delimiters = this.Delimiters;
        var committed = false;

        if (delimiters is null || delimiters.Count == 0)
        {
            // Enter-only mode: commas and semicolons are part of the tag, which is the whole point of
            // handing this an empty delimiter list.
            if (!commitTrailing)
                return false;

            committed = this.TryCommit(text);
            if (committed)
                this.SetPendingText(String.Empty);

            return committed;
        }

        var parts = text.Split(delimiters.ToArray(), StringSplitOptions.None);
        var last = parts.Length - 1;

        for (var i = 0; i < parts.Length; i++)
        {
            if (i == last && !commitTrailing)
                break;

            committed |= this.TryCommit(parts[i]);
        }

        // No tag begins with whitespace while TrimWhitespace is on, so neither does the text on its way
        // to becoming one: typing "a, b" would otherwise leave the caret behind a space that is about to
        // be trimmed off anyway, and the field would look like it had lost the comma rather than eaten it.
        var remainder = commitTrailing
            ? String.Empty
            : this.TrimWhitespace ? parts[last].TrimStart() : parts[last];

        // A refused trailing fragment stays typed so it can be corrected; everything before a delimiter
        // has been dealt with either way.
        if (committed || parts.Length > 1 || (!commitTrailing && remainder != text))
            this.SetPendingText(remainder);

        return committed;
    }


    bool TryCommit(string? raw)
    {
        if (this.IsReadOnly)
            return false;

        var tags = this.Tags;
        if (tags is null)
            return false;

        var tag = this.TrimWhitespace ? raw?.Trim() : raw;
        tag = Strip(tag);

        if (String.IsNullOrEmpty(tag))
            return false;

        if (!this.AllowDuplicates && this.IndexOf(tags, tag) >= 0)
            return false;

        // A cap stops further commits and leaves the typed text alone rather than silently eating it.
        if (this.MaxTags > 0 && tags.Count >= this.MaxTags)
            return false;

        var adding = new TagAddingEventArgs(tag);
        this.TagAdding?.Invoke(this, adding);
        if (adding.Cancel)
            return false;

        tags.Add(tag);

        var index = tags.Count - 1;
        var args = new TagEventArgs(tag, index);
        this.TagAdded?.Invoke(this, args);

        var command = this.TagAddedCommand;
        if (command?.CanExecute(tag) == true)
            command.Execute(tag);

        this.TagsChanged?.Invoke(this, EventArgs.Empty);

        // A plain IList raises nothing, so the chips are rebuilt here; an ObservableCollection has
        // already done it through CollectionChanged and this is a no-op second pass.
        this.RebuildChips();

        // The first tag is what makes Backspace mean something, so it is also what puts the sentinel in.
        this.SetPendingText(this.PendingText);
        return true;
    }


    bool RemoveAt(int index)
    {
        var tags = this.Tags;
        if (this.IsReadOnly || tags is null || index < 0 || index >= tags.Count)
            return false;

        var tag = tags[index];
        tags.RemoveAt(index);

        var args = new TagEventArgs(tag, index);
        this.TagRemoved?.Invoke(this, args);

        var command = this.TagRemovedCommand;
        if (command?.CanExecute(tag) == true)
            command.Execute(tag);

        this.TagsChanged?.Invoke(this, EventArgs.Empty);
        this.RebuildChips();

        // Removing the last tag takes the sentinel back out, and the placeholder comes back with it.
        this.SetPendingText(this.PendingText);
        return true;
    }


    int IndexOf(IList<string> tags, string tag)
    {
        var comparison = this.CaseSensitiveDuplicates
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        for (var i = 0; i < tags.Count; i++)
        {
            if (String.Equals(tags[i], tag, comparison))
                return i;
        }

        return -1;
    }


    // ---------------------------------------------------------------------------------------------
    // The entry
    // ---------------------------------------------------------------------------------------------

    void OnEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (this.suppressTextChanged)
            return;

        var oldText = e.OldTextValue ?? String.Empty;
        var newText = e.NewTextValue ?? String.Empty;

        if (this.BackspaceRemovesTag)
        {
            // The sentinel has gone and nothing else has: the caret was at the very start and the key
            // pressed was Backspace. That is the only signal MAUI gives - there is no portable key-down
            // on an Entry - and it is why the sentinel is seeded at all.
            var lostSentinel = oldText.StartsWith(Sentinel, StringComparison.Ordinal)
                && !newText.StartsWith(Sentinel, StringComparison.Ordinal);

            if (lostSentinel)
            {
                var remainder = Strip(newText);
                this.SetPendingText(remainder);

                if (remainder.Length == 0 && !this.IsReadOnly)
                {
                    var tags = this.Tags;
                    if (tags is { Count: > 0 })
                        this.RemoveAt(tags.Count - 1);
                }

                return;
            }
        }

        var typed = Strip(newText);
        if (typed.Length == 0)
            return;

        // More than one character arrived at once, so this is a paste rather than a keystroke - and a
        // pasted "a, b, c" is three tags, trailing fragment included.
        var inserted = typed.Length - Strip(oldText).Length;
        this.ProcessInput(typed, commitTrailing: inserted > 1);
    }


    void OnEntryCompleted(object? sender, EventArgs e) => this.CommitPending();


    void OnEntryFocused(object? sender, FocusEventArgs e) => this.SetFocused(true);

    void OnEntryUnfocused(object? sender, FocusEventArgs e)
    {
        this.SetFocused(false);

        if (this.CommitOnUnfocus)
            this.CommitPending();
    }


    /// <summary>
    /// Tracks whether the typing area has the keyboard, which decides whether the sentinel is in place.
    /// Internal because a headless test cannot focus a control the platform never realised, and the
    /// backspace behaviour is exactly what needs testing.
    /// </summary>
    internal void SetFocused(bool value)
    {
        this.isFocused = value;

        // The sentinel is an implementation detail of typing, so it is only in the box while the box is
        // being typed into. Outside that it comes straight out - a placeholder does not show while any
        // text is present, invisible or not, and a field that quietly lost its prompt after the first
        // tag would look broken.
        this.SetPendingText(this.PendingText);
        this.ApplyStroke();
    }


    /// <summary>
    /// Whether the zero-width space should currently be in front of the typed text: only while the field
    /// is being typed into, and only while there is a tag for Backspace to remove. With no tags there is
    /// nothing the key could do, so the placeholder is worth more than the signal.
    /// </summary>
    bool WantsSentinel
        => this.BackspaceRemovesTag
            && !this.IsReadOnly
            && this.isFocused
            && this.Tags is { Count: > 0 };


    /// <summary>Writes the typing area's text, with the sentinel in front of it when it is wanted.</summary>
    void SetPendingText(string text, bool seedSentinel = true)
    {
        var value = seedSentinel && this.WantsSentinel
            ? Sentinel + text
            : text;

        if (this.entry.Text == value)
            return;

        this.suppressTextChanged = true;
        try
        {
            this.entry.Text = value;
            this.entry.CursorPosition = value.Length;
        }
        finally
        {
            this.suppressTextChanged = false;
        }
    }


    static string Strip(string? text)
        => String.IsNullOrEmpty(text) ? String.Empty : text.Replace(Sentinel, String.Empty);


    // ---------------------------------------------------------------------------------------------
    // Chips
    // ---------------------------------------------------------------------------------------------

    internal void OnTagsSourceChanged(IList<string>? oldValue, IList<string>? newValue)
    {
        if (this.tagsNotifier is not null)
            this.tagsNotifier.CollectionChanged -= this.OnTagsCollectionChanged;

        this.tagsNotifier = newValue as INotifyCollectionChanged;
        if (this.tagsNotifier is not null)
            this.tagsNotifier.CollectionChanged += this.OnTagsCollectionChanged;

        this.RebuildChips();
    }


    void OnTagsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        this.RebuildChips();
        this.TagsChanged?.Invoke(this, EventArgs.Empty);
    }


    internal void RebuildChips()
    {
        if (this.rebuilding)
            return;

        this.rebuilding = true;
        try
        {
            foreach (var chip in this.chips)
            {
                chip.RemoveRequested -= this.OnChipRemoveRequested;
                this.wrap.Remove(chip);
            }
            this.chips.Clear();

            var tags = this.Tags;
            if (tags is null)
                return;

            var insertAt = 0;

            foreach (var tag in tags)
            {
                var chip = new TagChipView
                {
                    ChipBackgroundColor = this.ChipBackgroundColor,
                    ChipTextColor = this.ChipTextColor,
                    ChipCornerRadius = this.ChipCornerRadius,
                    CanRemove = !this.IsReadOnly
                };

                chip.Bind(tag);
                chip.Refresh();

                if (this.ChipTemplate?.CreateContent() is View templated)
                {
                    templated.BindingContext = tag;
                    chip.SetCustomContent(templated);
                }

                chip.RemoveRequested += this.OnChipRemoveRequested;
                this.chips.Add(chip);

                // Chips run in front of the editor, which always stays last so the caret is at the end
                // of the list rather than in the middle of it.
                this.wrap.Insert(insertAt++, chip);
            }
        }
        finally
        {
            this.rebuilding = false;
        }
    }


    void OnChipRemoveRequested(object? sender, EventArgs e)
    {
        if (this.IsReadOnly || sender is not TagChipView chip)
            return;

        var tags = this.Tags;
        if (tags is null)
            return;

        var index = this.chips.IndexOf(chip);
        if (index >= 0 && index < tags.Count)
            this.RemoveAt(index);
    }


    // ---------------------------------------------------------------------------------------------
    // Chrome
    // ---------------------------------------------------------------------------------------------

    void ApplyCornerRadius()
    {
        var shape = new RoundRectangle();
        shape.SetCornerTokenOrValue(this.CornerRadius, ShinyThemeKeys.Shape.CornerSmallRadius);
        this.border.StrokeShape = shape;
    }


    void ApplyStroke()
    {
        if (this.BorderColor is Color explicitColor)
        {
            ThemeProbe.Tint(this.strokeProbe, BoxView.ColorProperty, explicitColor, ShinyThemeKeys.Color.Outline);
        }
        else
        {
            // Focus is expressed as the outline taking the primary colour rather than as a glow: a
            // Shadow has to be built rather than tokenised, and swapping one while the user is
            // interacting unfocuses the content inside it on Android.
            var token = this.entry.IsFocused ? ShinyThemeKeys.Color.Primary : ShinyThemeKeys.Color.Outline;
            ThemeProbe.Tint(this.strokeProbe, BoxView.ColorProperty, null, token);
        }

        this.border.SetTokenOrValue(Border.StrokeThicknessProperty, this.BorderThickness, ShinyThemeKeys.Border.Thin);
    }


    internal void ApplyEntryState()
    {
        this.entry.IsReadOnly = this.IsReadOnly;
        this.entry.Placeholder = this.Placeholder;

        if (this.PlaceholderColor is Color placeholder)
            this.entry.PlaceholderColor = placeholder;
        else
            this.entry.SetDynamicResource(Entry.PlaceholderColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        ThemeProbe.Tint(this.entry, Entry.TextColorProperty, this.TextColor, ShinyThemeKeys.Color.OnSurface);
        this.entry.SetTokenOrValue(Entry.FontSizeProperty, this.FontSize, ShinyThemeKeys.Type.BodyLargeSize);

        foreach (var chip in this.chips)
            chip.CanRemove = !this.IsReadOnly;

        if (this.IsReadOnly)
            this.SetPendingText(this.PendingText, seedSentinel: false);
    }


    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName == nameof(this.IsEnabled))
            StyleGuard.WhenReady<TagEntry>(this, static x => x.border.Opacity = x.IsEnabled ? 1 : x.DisabledOpacity);
    }


    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (this.Handler is not null && this.AutoFocus)
            this.Dispatcher.Dispatch(() => this.Focus());
    }
}

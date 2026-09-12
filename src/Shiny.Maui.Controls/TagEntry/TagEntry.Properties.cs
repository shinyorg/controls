using System.Windows.Input;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

public partial class TagEntry
{
    static void Ready(BindableObject b, Action<TagEntry> apply)
        => StyleGuard.WhenReady(b, typeof(TagEntry), () => apply((TagEntry)b));


    // -- values ------------------------------------------------------------------------------------

    public static readonly BindableProperty TagsProperty = BindableProperty.Create(
        nameof(Tags), typeof(IList<string>), typeof(TagEntry), null, BindingMode.TwoWay,
        propertyChanged: (b, o, n) => Ready(b, x => x.OnTagsSourceChanged(o as IList<string>, n as IList<string>)));
    /// <summary>
    /// The committed tags. Two-way, and an <c>ObservableCollection</c> is watched live — a view model
    /// that clears the list gets an empty field back without touching the control.
    /// </summary>
    public IList<string>? Tags
    {
        get => (IList<string>?)this.GetValue(TagsProperty);
        set => this.SetValue(TagsProperty, value);
    }

    public static readonly BindableProperty DelimitersProperty = BindableProperty.Create(
        nameof(Delimiters), typeof(IList<string>), typeof(TagEntry), null);
    /// <summary>
    /// Which strings commit a tag besides Enter, and what a pasted value splits on. A comma by default;
    /// an <em>empty</em> list means Enter only, which is how a tag gets to contain a comma.
    /// </summary>
    public IList<string>? Delimiters
    {
        get => (IList<string>?)this.GetValue(DelimitersProperty);
        set => this.SetValue(DelimitersProperty, value);
    }

    public static readonly BindableProperty MaxTagsProperty = BindableProperty.Create(
        nameof(MaxTags), typeof(int), typeof(TagEntry), 0);
    /// <summary>
    /// How many tags may be committed, or 0 for no limit. At the cap further commits are ignored and the
    /// typed text stays put, so nothing the user wrote is thrown away without them seeing it.
    /// </summary>
    public int MaxTags
    {
        get => (int)this.GetValue(MaxTagsProperty);
        set => this.SetValue(MaxTagsProperty, value);
    }

    public static readonly BindableProperty AllowDuplicatesProperty = BindableProperty.Create(
        nameof(AllowDuplicates), typeof(bool), typeof(TagEntry), false);
    /// <summary>Whether the same tag may be committed twice. Off by default — duplicates are dropped quietly.</summary>
    public bool AllowDuplicates
    {
        get => (bool)this.GetValue(AllowDuplicatesProperty);
        set => this.SetValue(AllowDuplicatesProperty, value);
    }

    public static readonly BindableProperty CaseSensitiveDuplicatesProperty = BindableProperty.Create(
        nameof(CaseSensitiveDuplicates), typeof(bool), typeof(TagEntry), false);
    /// <summary>
    /// Whether <c>Design</c> and <c>design</c> count as two tags. Off by default, because to the person
    /// typing them they are one.
    /// </summary>
    public bool CaseSensitiveDuplicates
    {
        get => (bool)this.GetValue(CaseSensitiveDuplicatesProperty);
        set => this.SetValue(CaseSensitiveDuplicatesProperty, value);
    }

    public static readonly BindableProperty TrimWhitespaceProperty = BindableProperty.Create(
        nameof(TrimWhitespace), typeof(bool), typeof(TagEntry), true);
    /// <summary>Whether a committed tag has its surrounding whitespace removed. On by default.</summary>
    public bool TrimWhitespace
    {
        get => (bool)this.GetValue(TrimWhitespaceProperty);
        set => this.SetValue(TrimWhitespaceProperty, value);
    }

    public static readonly BindableProperty BackspaceRemovesTagProperty = BindableProperty.Create(
        nameof(BackspaceRemovesTag), typeof(bool), typeof(TagEntry), true,
        propertyChanged: (b, _, _) => Ready(b, x => x.ApplyEntryState()));
    /// <summary>
    /// Whether Backspace against empty text removes the newest chip. On by default.
    /// </summary>
    /// <remarks>
    /// MAUI has no portable key-down on an <c>Entry</c>, so there is nothing to listen to: backspacing
    /// text that is already empty changes nothing and raises nothing. The field therefore keeps a
    /// zero-width space in front of whatever is typed — deleting <em>that</em> does raise
    /// <c>TextChanged</c>, and is the signal. The sentinel never reaches a committed tag and is taken
    /// out the moment the field loses focus. Turn this off if the platform's predictive text is fighting
    /// it; everything else about the control is unaffected.
    /// </remarks>
    public bool BackspaceRemovesTag
    {
        get => (bool)this.GetValue(BackspaceRemovesTagProperty);
        set => this.SetValue(BackspaceRemovesTagProperty, value);
    }

    public static readonly BindableProperty CommitOnUnfocusProperty = BindableProperty.Create(
        nameof(CommitOnUnfocus), typeof(bool), typeof(TagEntry), true);
    /// <summary>
    /// Whether leaving the field commits what was typed. On by default: a half-typed tag left behind on
    /// submit is a value the user believed they had entered.
    /// </summary>
    public bool CommitOnUnfocus
    {
        get => (bool)this.GetValue(CommitOnUnfocusProperty);
        set => this.SetValue(CommitOnUnfocusProperty, value);
    }

    public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
        nameof(IsReadOnly), typeof(bool), typeof(TagEntry), false,
        propertyChanged: (b, _, _) => Ready(b, x => x.ApplyEntryState()));
    /// <summary>
    /// Keeps the chips visible and crisp but blocks adding and removing. Unlike disabling the field,
    /// which also dims it — read-only is "these are the values", disabled is "not right now".
    /// </summary>
    public bool IsReadOnly
    {
        get => (bool)this.GetValue(IsReadOnlyProperty);
        set => this.SetValue(IsReadOnlyProperty, value);
    }

    public static readonly BindableProperty AutoFocusProperty = BindableProperty.Create(
        nameof(AutoFocus), typeof(bool), typeof(TagEntry), false);
    /// <summary>Focuses the typing area as soon as the field appears — for one revealed by an action.</summary>
    public bool AutoFocus
    {
        get => (bool)this.GetValue(AutoFocusProperty);
        set => this.SetValue(AutoFocusProperty, value);
    }


    // -- text --------------------------------------------------------------------------------------

    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder), typeof(string), typeof(TagEntry), String.Empty,
        propertyChanged: (b, _, _) => Ready(b, x => x.ApplyEntryState()));
    /// <summary>Prompt shown in the typing area while it is empty.</summary>
    public string Placeholder
    {
        get => (string)this.GetValue(PlaceholderProperty);
        set => this.SetValue(PlaceholderProperty, value);
    }

    public static readonly BindableProperty PlaceholderColorProperty = BindableProperty.Create(
        nameof(PlaceholderColor), typeof(Color), typeof(TagEntry), null,
        propertyChanged: (b, _, _) => Ready(b, x => x.ApplyEntryState()));
    /// <summary>Placeholder colour. Unset follows the theme.</summary>
    public Color? PlaceholderColor
    {
        get => (Color?)this.GetValue(PlaceholderColorProperty);
        set => this.SetValue(PlaceholderColorProperty, value);
    }

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor), typeof(Color), typeof(TagEntry), null,
        propertyChanged: (b, _, _) => Ready(b, x => x.ApplyEntryState()));
    /// <summary>Typed-text colour. Unset follows the theme.</summary>
    public Color? TextColor
    {
        get => (Color?)this.GetValue(TextColorProperty);
        set => this.SetValue(TextColorProperty, value);
    }

    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize), typeof(double), typeof(TagEntry), ThemeTokens.Unset,
        propertyChanged: (b, _, _) => Ready(b, x => x.ApplyEntryState()));
    /// <summary>Typed-text size. Unset follows the theme.</summary>
    public double FontSize
    {
        get => (double)this.GetValue(FontSizeProperty);
        set => this.SetValue(FontSizeProperty, value);
    }


    // -- chrome ------------------------------------------------------------------------------------

    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(
        nameof(CornerRadius), typeof(double), typeof(TagEntry), ThemeTokens.Unset,
        propertyChanged: (b, _, _) => Ready(b, x => x.ApplyCornerRadius()));
    /// <summary>The box's corner radius. Unset follows the theme.</summary>
    public double CornerRadius
    {
        get => (double)this.GetValue(CornerRadiusProperty);
        set => this.SetValue(CornerRadiusProperty, value);
    }

    public static readonly BindableProperty BorderColorProperty = BindableProperty.Create(
        nameof(BorderColor), typeof(Color), typeof(TagEntry), null,
        propertyChanged: (b, _, _) => Ready(b, x => x.ApplyStroke()));
    /// <summary>
    /// The box's outline. Unset follows the theme — and takes the primary colour while the field has
    /// focus, which an explicit colour turns off.
    /// </summary>
    public Color? BorderColor
    {
        get => (Color?)this.GetValue(BorderColorProperty);
        set => this.SetValue(BorderColorProperty, value);
    }

    public static readonly BindableProperty BorderThicknessProperty = BindableProperty.Create(
        nameof(BorderThickness), typeof(double), typeof(TagEntry), ThemeTokens.Unset,
        propertyChanged: (b, _, _) => Ready(b, x => x.ApplyStroke()));
    /// <summary>Outline thickness. Unset follows the theme's thin border token.</summary>
    public double BorderThickness
    {
        get => (double)this.GetValue(BorderThicknessProperty);
        set => this.SetValue(BorderThicknessProperty, value);
    }

    public static readonly BindableProperty DisabledOpacityProperty = BindableProperty.Create(
        nameof(DisabledOpacity), typeof(double), typeof(TagEntry), 0.38);
    /// <summary>How far the whole field dims when it is disabled.</summary>
    public double DisabledOpacity
    {
        get => (double)this.GetValue(DisabledOpacityProperty);
        set => this.SetValue(DisabledOpacityProperty, value);
    }


    // -- chips -------------------------------------------------------------------------------------

    public static readonly BindableProperty ChipTemplateProperty = BindableProperty.Create(
        nameof(ChipTemplate), typeof(DataTemplate), typeof(TagEntry), null,
        propertyChanged: (b, _, _) => Ready(b, x => x.RebuildChips()));
    /// <summary>
    /// Replaces a chip's label — the tag string is the binding context. The remove affordance is not part
    /// of the template: every chip keeps the same way out, however it is drawn.
    /// </summary>
    public DataTemplate? ChipTemplate
    {
        get => (DataTemplate?)this.GetValue(ChipTemplateProperty);
        set => this.SetValue(ChipTemplateProperty, value);
    }

    public static readonly BindableProperty ChipBackgroundColorProperty = BindableProperty.Create(
        nameof(ChipBackgroundColor), typeof(Color), typeof(TagEntry), null,
        propertyChanged: (b, _, _) => Ready(b, x => x.RebuildChips()));
    /// <summary>
    /// Chip fill. Unset follows the theme's secondary container; set it and the chip's ink is computed
    /// from it by luminance, so a brand colour stays readable.
    /// </summary>
    public Color? ChipBackgroundColor
    {
        get => (Color?)this.GetValue(ChipBackgroundColorProperty);
        set => this.SetValue(ChipBackgroundColorProperty, value);
    }

    public static readonly BindableProperty ChipTextColorProperty = BindableProperty.Create(
        nameof(ChipTextColor), typeof(Color), typeof(TagEntry), null,
        propertyChanged: (b, _, _) => Ready(b, x => x.RebuildChips()));
    /// <summary>Chip ink. Unset is computed from the fill.</summary>
    public Color? ChipTextColor
    {
        get => (Color?)this.GetValue(ChipTextColorProperty);
        set => this.SetValue(ChipTextColorProperty, value);
    }

    public static readonly BindableProperty ChipCornerRadiusProperty = BindableProperty.Create(
        nameof(ChipCornerRadius), typeof(double), typeof(TagEntry), ThemeTokens.Unset,
        propertyChanged: (b, _, _) => Ready(b, x => x.RebuildChips()));
    /// <summary>Chip corner radius. Unset follows the theme's small corner token.</summary>
    public double ChipCornerRadius
    {
        get => (double)this.GetValue(ChipCornerRadiusProperty);
        set => this.SetValue(ChipCornerRadiusProperty, value);
    }


    // -- commands ----------------------------------------------------------------------------------

    public static readonly BindableProperty TagAddedCommandProperty = BindableProperty.Create(
        nameof(TagAddedCommand), typeof(ICommand), typeof(TagEntry), null);
    /// <summary>Invoked with the tag after <see cref="TagAdded"/>.</summary>
    public ICommand? TagAddedCommand
    {
        get => (ICommand?)this.GetValue(TagAddedCommandProperty);
        set => this.SetValue(TagAddedCommandProperty, value);
    }

    public static readonly BindableProperty TagRemovedCommandProperty = BindableProperty.Create(
        nameof(TagRemovedCommand), typeof(ICommand), typeof(TagEntry), null);
    /// <summary>Invoked with the tag after <see cref="TagRemoved"/>.</summary>
    public ICommand? TagRemovedCommand
    {
        get => (ICommand?)this.GetValue(TagRemovedCommandProperty);
        set => this.SetValue(TagRemovedCommandProperty, value);
    }
}

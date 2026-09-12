using System.Collections;
using System.Windows.Input;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

public partial class ChipGroup
{
    static void Ready(BindableObject b, Action<ChipGroup> apply)
        => StyleGuard.WhenReady(b, typeof(ChipGroup), () => apply((ChipGroup)b));


    // -- items -------------------------------------------------------------------------------------

    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource), typeof(IEnumerable), typeof(ChipGroup), null,
        propertyChanged: (b, o, n) => Ready(b, x => x.OnItemsSourceChanged(o as IEnumerable, n as IEnumerable)));
    /// <summary>
    /// What the chips stand for. Anything enumerable will do — strings, enum values, entities — and an
    /// <c>ObservableCollection</c> is watched live, so adding to it adds a chip without touching the
    /// control.
    /// </summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)this.GetValue(ItemsSourceProperty);
        set => this.SetValue(ItemsSourceProperty, value);
    }

    public static readonly BindableProperty ItemTemplateProperty = BindableProperty.Create(
        nameof(ItemTemplate), typeof(DataTemplate), typeof(ChipGroup), null,
        propertyChanged: (b, _, _) => Ready(b, x => x.RebuildChips()));
    /// <summary>
    /// Replaces a chip's label — the item is the binding context. The selection check and the remove
    /// affordance are not part of the template: every chip keeps the same state and the same way out,
    /// however it is drawn.
    /// </summary>
    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)this.GetValue(ItemTemplateProperty);
        set => this.SetValue(ItemTemplateProperty, value);
    }

    public static readonly BindableProperty ItemDisplayBindingProperty = BindableProperty.Create(
        nameof(ItemDisplayBinding), typeof(BindingBase), typeof(ChipGroup), null,
        propertyChanged: (b, _, _) => Ready(b, x => x.RebuildChips()));
    /// <summary>
    /// Which part of an item the chip shows, as a binding — <c>ItemDisplayBinding="{Binding Name}"</c>,
    /// exactly as a <c>Picker</c> takes one. Unset, a chip shows the item's <c>ToString()</c>.
    /// </summary>
    /// <remarks>
    /// A binding rather than a property name looked up by reflection: the path is compiled, so it
    /// survives trimming, and a converter or a <c>StringFormat</c> comes along for free.
    /// </remarks>
    public BindingBase? ItemDisplayBinding
    {
        get => (BindingBase?)this.GetValue(ItemDisplayBindingProperty);
        set => this.SetValue(ItemDisplayBindingProperty, value);
    }

    public static readonly BindableProperty DisplayMemberPathProperty = BindableProperty.Create(
        nameof(DisplayMemberPath), typeof(string), typeof(ChipGroup), null,
        propertyChanged: (b, _, _) => Ready(b, x => x.RebuildChips()));
    /// <summary>
    /// The property name a chip shows, for the common case where a whole
    /// <see cref="ItemDisplayBinding"/> is ceremony. Ignored when one is set.
    /// </summary>
    public string? DisplayMemberPath
    {
        get => (string?)this.GetValue(DisplayMemberPathProperty);
        set => this.SetValue(DisplayMemberPathProperty, value);
    }


    // -- selection ---------------------------------------------------------------------------------

    public static readonly BindableProperty SelectionModeProperty = BindableProperty.Create(
        nameof(SelectionMode), typeof(ChipSelectionMode), typeof(ChipGroup), ChipSelectionMode.Single,
        propertyChanged: (b, _, _) => Ready(b, x =>
        {
            if (x.SelectionMode == ChipSelectionMode.None)
                x.ClearSelection();
            else
                x.RefreshChips();
        }));
    /// <summary>
    /// How many chips may be selected at once. <see cref="ChipSelectionMode.Single"/> by default —
    /// selection is what a chip group is for, which is the opposite of <see cref="ButtonGroup"/>, a row
    /// of actions that only becomes a picker when asked.
    /// </summary>
    public ChipSelectionMode SelectionMode
    {
        get => (ChipSelectionMode)this.GetValue(SelectionModeProperty);
        set => this.SetValue(SelectionModeProperty, value);
    }

    public static readonly BindableProperty SelectedItemProperty = BindableProperty.Create(
        nameof(SelectedItem), typeof(object), typeof(ChipGroup), null, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => Ready(b, x => x.OnSelectedItemChanged(n)));
    /// <summary>
    /// The selected item, or <c>null</c> for none. Two-way, and in
    /// <see cref="ChipSelectionMode.Multiple"/> it reports the first selected item — read
    /// <see cref="SelectedItems"/> for the rest.
    /// </summary>
    public object? SelectedItem
    {
        get => this.GetValue(SelectedItemProperty);
        set => this.SetValue(SelectedItemProperty, value);
    }

    public static readonly BindableProperty SelectedItemsProperty = BindableProperty.Create(
        nameof(SelectedItems), typeof(IList), typeof(ChipGroup), null, BindingMode.TwoWay,
        propertyChanged: (b, o, n) => Ready(b, x => x.OnSelectedItemsChanged(o as IList, n as IList)));
    /// <summary>
    /// Everything selected. Two-way, and the control writes <em>into</em> the list you bind rather than
    /// replacing it — so an <c>ObservableCollection</c> on a view model stays the same instance and
    /// whatever is watching it goes on working.
    /// </summary>
    /// <remarks>
    /// Bind this in <see cref="ChipSelectionMode.Multiple"/>. It is kept accurate in
    /// <see cref="ChipSelectionMode.Single"/> too — one item, or none — so a view model can read the
    /// same property either way.
    /// </remarks>
    public IList? SelectedItems
    {
        get => (IList?)this.GetValue(SelectedItemsProperty);
        set => this.SetValue(SelectedItemsProperty, value);
    }

    public static readonly BindableProperty AllowDeselectProperty = BindableProperty.Create(
        nameof(AllowDeselect), typeof(bool), typeof(ChipGroup), false);
    /// <summary>
    /// Whether tapping the selected chip in <see cref="ChipSelectionMode.Single"/> clears the selection.
    /// Off by default — a picker that can be emptied by tapping its own answer again usually is not what
    /// was wanted. Ignored in multiple mode, where a second tap always toggles.
    /// </summary>
    public bool AllowDeselect
    {
        get => (bool)this.GetValue(AllowDeselectProperty);
        set => this.SetValue(AllowDeselectProperty, value);
    }

    public static readonly BindableProperty MaxSelectionCountProperty = BindableProperty.Create(
        nameof(MaxSelectionCount), typeof(int), typeof(ChipGroup), 0);
    /// <summary>
    /// How many chips may be selected in <see cref="ChipSelectionMode.Multiple"/>, or 0 for no limit.
    /// At the cap a tap on an unselected chip does nothing — the oldest selection is <em>not</em>
    /// dropped, because silently unpicking something the user chose is worse than refusing the new one.
    /// </summary>
    public int MaxSelectionCount
    {
        get => (int)this.GetValue(MaxSelectionCountProperty);
        set => this.SetValue(MaxSelectionCountProperty, value);
    }

    public static readonly BindableProperty ShowSelectionCheckProperty = BindableProperty.Create(
        nameof(ShowSelectionCheck), typeof(bool), typeof(ChipGroup), true,
        propertyChanged: (b, _, _) => Ready(b, x => x.RefreshChips()));
    /// <summary>
    /// Whether a selected chip draws a leading check. On by default: fill alone carries selection only
    /// for someone who can compare it with the chips beside it.
    /// </summary>
    public bool ShowSelectionCheck
    {
        get => (bool)this.GetValue(ShowSelectionCheckProperty);
        set => this.SetValue(ShowSelectionCheckProperty, value);
    }


    // -- behaviour ---------------------------------------------------------------------------------

    public static readonly BindableProperty AllowRemoveProperty = BindableProperty.Create(
        nameof(AllowRemove), typeof(bool), typeof(ChipGroup), false,
        propertyChanged: (b, _, _) => Ready(b, x => x.RefreshChips()));
    /// <summary>
    /// Whether each chip carries a remove affordance. Off by default — most chip groups are a fixed set
    /// of filters, and a ✕ on every one of them invites the user to destroy the picker.
    /// </summary>
    public bool AllowRemove
    {
        get => (bool)this.GetValue(AllowRemoveProperty);
        set => this.SetValue(AllowRemoveProperty, value);
    }

    public static readonly BindableProperty IsReadOnlyProperty = BindableProperty.Create(
        nameof(IsReadOnly), typeof(bool), typeof(ChipGroup), false,
        propertyChanged: (b, _, _) => Ready(b, x => x.RefreshChips()));
    /// <summary>
    /// Keeps the chips crisp and their selection visible but blocks changing it. Unlike disabling the
    /// group, which also dims it — read-only is "these are the values", disabled is "not right now".
    /// </summary>
    public bool IsReadOnly
    {
        get => (bool)this.GetValue(IsReadOnlyProperty);
        set => this.SetValue(IsReadOnlyProperty, value);
    }


    // -- layout ------------------------------------------------------------------------------------

    public static readonly BindableProperty WrapProperty = BindableProperty.Create(
        nameof(Wrap), typeof(bool), typeof(ChipGroup), true,
        propertyChanged: (b, _, _) => Ready(b, x => x.ApplyLayout()));
    /// <summary>
    /// Whether chips flow onto as many lines as they need. Off, they stay on one line however narrow the
    /// group gets — put it in a horizontal <c>ScrollView</c> when you turn this off, or the overflow is
    /// simply off the edge.
    /// </summary>
    public bool Wrap
    {
        get => (bool)this.GetValue(WrapProperty);
        set => this.SetValue(WrapProperty, value);
    }

    public static readonly BindableProperty HorizontalSpacingProperty = BindableProperty.Create(
        nameof(HorizontalSpacing), typeof(double), typeof(ChipGroup), 8d,
        propertyChanged: (b, _, _) => Ready(b, x => x.ApplyLayout()));
    /// <summary>The gap between chips on the same line.</summary>
    public double HorizontalSpacing
    {
        get => (double)this.GetValue(HorizontalSpacingProperty);
        set => this.SetValue(HorizontalSpacingProperty, value);
    }

    public static readonly BindableProperty VerticalSpacingProperty = BindableProperty.Create(
        nameof(VerticalSpacing), typeof(double), typeof(ChipGroup), 8d,
        propertyChanged: (b, _, _) => Ready(b, x => x.ApplyLayout()));
    /// <summary>The gap between lines of chips.</summary>
    public double VerticalSpacing
    {
        get => (double)this.GetValue(VerticalSpacingProperty);
        set => this.SetValue(VerticalSpacingProperty, value);
    }


    // -- chrome ------------------------------------------------------------------------------------

    public static readonly BindableProperty ChipBackgroundColorProperty = BindableProperty.Create(
        nameof(ChipBackgroundColor), typeof(Color), typeof(ChipGroup), null,
        propertyChanged: (b, _, _) => Ready(b, x => x.RefreshChips()));
    /// <summary>
    /// An unselected chip's fill. Unset it is transparent, so the page behind shows through and an
    /// outlined chip stays outlined; set it and the chip's ink is computed from it by luminance.
    /// </summary>
    public Color? ChipBackgroundColor
    {
        get => (Color?)this.GetValue(ChipBackgroundColorProperty);
        set => this.SetValue(ChipBackgroundColorProperty, value);
    }

    public static readonly BindableProperty ChipTextColorProperty = BindableProperty.Create(
        nameof(ChipTextColor), typeof(Color), typeof(ChipGroup), null,
        propertyChanged: (b, _, _) => Ready(b, x => x.RefreshChips()));
    /// <summary>An unselected chip's ink. Unset follows the theme, or is computed from an explicit fill.</summary>
    public Color? ChipTextColor
    {
        get => (Color?)this.GetValue(ChipTextColorProperty);
        set => this.SetValue(ChipTextColorProperty, value);
    }

    public static readonly BindableProperty SelectedChipBackgroundColorProperty = BindableProperty.Create(
        nameof(SelectedChipBackgroundColor), typeof(Color), typeof(ChipGroup), null,
        propertyChanged: (b, _, _) => Ready(b, x => x.RefreshChips()));
    /// <summary>A selected chip's fill. Unset follows the theme's secondary container.</summary>
    public Color? SelectedChipBackgroundColor
    {
        get => (Color?)this.GetValue(SelectedChipBackgroundColorProperty);
        set => this.SetValue(SelectedChipBackgroundColorProperty, value);
    }

    public static readonly BindableProperty SelectedChipTextColorProperty = BindableProperty.Create(
        nameof(SelectedChipTextColor), typeof(Color), typeof(ChipGroup), null,
        propertyChanged: (b, _, _) => Ready(b, x => x.RefreshChips()));
    /// <summary>A selected chip's ink. Unset follows the theme, or is computed from an explicit fill.</summary>
    public Color? SelectedChipTextColor
    {
        get => (Color?)this.GetValue(SelectedChipTextColorProperty);
        set => this.SetValue(SelectedChipTextColorProperty, value);
    }

    public static readonly BindableProperty ChipBorderColorProperty = BindableProperty.Create(
        nameof(ChipBorderColor), typeof(Color), typeof(ChipGroup), null,
        propertyChanged: (b, _, _) => Ready(b, x => x.RefreshChips()));
    /// <summary>An unselected chip's outline. Unset follows the theme. A selected chip draws none.</summary>
    public Color? ChipBorderColor
    {
        get => (Color?)this.GetValue(ChipBorderColorProperty);
        set => this.SetValue(ChipBorderColorProperty, value);
    }

    public static readonly BindableProperty ChipCornerRadiusProperty = BindableProperty.Create(
        nameof(ChipCornerRadius), typeof(double), typeof(ChipGroup), ThemeTokens.Unset,
        propertyChanged: (b, _, _) => Ready(b, x => x.RefreshChips()));
    /// <summary>Chip corner radius. Unset follows the theme's small corner token.</summary>
    public double ChipCornerRadius
    {
        get => (double)this.GetValue(ChipCornerRadiusProperty);
        set => this.SetValue(ChipCornerRadiusProperty, value);
    }

    public static readonly BindableProperty DisabledOpacityProperty = BindableProperty.Create(
        nameof(DisabledOpacity), typeof(double), typeof(ChipGroup), 0.38);
    /// <summary>How far the whole group dims when it is disabled.</summary>
    public double DisabledOpacity
    {
        get => (double)this.GetValue(DisabledOpacityProperty);
        set => this.SetValue(DisabledOpacityProperty, value);
    }


    // -- commands ----------------------------------------------------------------------------------

    public static readonly BindableProperty SelectionChangedCommandProperty = BindableProperty.Create(
        nameof(SelectionChangedCommand), typeof(ICommand), typeof(ChipGroup), null);
    /// <summary>Invoked after <see cref="SelectionChanged"/>, with <see cref="SelectedItem"/> unless a parameter is set.</summary>
    public ICommand? SelectionChangedCommand
    {
        get => (ICommand?)this.GetValue(SelectionChangedCommandProperty);
        set => this.SetValue(SelectionChangedCommandProperty, value);
    }

    public static readonly BindableProperty SelectionChangedCommandParameterProperty = BindableProperty.Create(
        nameof(SelectionChangedCommandParameter), typeof(object), typeof(ChipGroup), null);
    /// <inheritdoc cref="SelectionChangedCommandProperty"/>
    public object? SelectionChangedCommandParameter
    {
        get => this.GetValue(SelectionChangedCommandParameterProperty);
        set => this.SetValue(SelectionChangedCommandParameterProperty, value);
    }

    public static readonly BindableProperty ChipTappedCommandProperty = BindableProperty.Create(
        nameof(ChipTappedCommand), typeof(ICommand), typeof(ChipGroup), null);
    /// <summary>
    /// Invoked with the item after <see cref="ChipTapped"/> — for every tap, including one that changes
    /// no selection. This is the command a group in <see cref="ChipSelectionMode.None"/> binds.
    /// </summary>
    public ICommand? ChipTappedCommand
    {
        get => (ICommand?)this.GetValue(ChipTappedCommandProperty);
        set => this.SetValue(ChipTappedCommandProperty, value);
    }

    public static readonly BindableProperty ChipRemovedCommandProperty = BindableProperty.Create(
        nameof(ChipRemovedCommand), typeof(ICommand), typeof(ChipGroup), null);
    /// <summary>Invoked with the item after <see cref="ChipRemoved"/>.</summary>
    public ICommand? ChipRemovedCommand
    {
        get => (ICommand?)this.GetValue(ChipRemovedCommandProperty);
        set => this.SetValue(ChipRemovedCommandProperty, value);
    }
}

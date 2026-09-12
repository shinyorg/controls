using System.Windows.Input;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

public partial class ButtonGroup
{
    static void Ready(BindableObject b, Action<ButtonGroup> apply)
        => StyleGuard.WhenReady(b, typeof(ButtonGroup), () => apply((ButtonGroup)b));


    // -- geometry ----------------------------------------------------------------------------------

    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(
        nameof(CornerRadius), typeof(double), typeof(ButtonGroup), ThemeTokens.Unset,
        propertyChanged: (b, _, _) => Ready(b, x => x.Rebuild()));
    /// <summary>
    /// The radius of the group's four outer corners. Unset follows the theme's medium corner token, which
    /// is the same one an ungrouped <see cref="ShinyButton"/> uses, so a group looks like the buttons it
    /// is made of.
    /// </summary>
    public double CornerRadius
    {
        get => (double)this.GetValue(CornerRadiusProperty);
        set => this.SetValue(CornerRadiusProperty, value);
    }

    public static readonly BindableProperty CollapseBordersProperty = BindableProperty.Create(
        nameof(CollapseBorders), typeof(bool), typeof(ButtonGroup), true,
        propertyChanged: (b, _, _) => Ready(b, x => x.Rebuild()));
    /// <summary>
    /// Pulls each outlined segment a hairline into the one before it, so two adjacent outlines paint as
    /// one edge rather than two. Filled segments are left alone — they have no edge to double up.
    /// </summary>
    public bool CollapseBorders
    {
        get => (bool)this.GetValue(CollapseBordersProperty);
        set => this.SetValue(CollapseBordersProperty, value);
    }

    public static readonly BindableProperty BorderThicknessProperty = BindableProperty.Create(
        nameof(BorderThickness), typeof(double), typeof(ButtonGroup), ThemeTokens.Unset,
        propertyChanged: (b, _, _) => Ready(b, x => x.Rebuild()));
    /// <summary>
    /// How much of an overlap <see cref="CollapseBorders"/> takes. Unset follows the theme's thin border
    /// token, which is the width an outlined button draws.
    /// </summary>
    public double BorderThickness
    {
        get => (double)this.GetValue(BorderThicknessProperty);
        set => this.SetValue(BorderThicknessProperty, value);
    }

    public static readonly BindableProperty ClusterSpacingProperty = BindableProperty.Create(
        nameof(ClusterSpacing), typeof(double), typeof(ButtonGroup), 8d,
        propertyChanged: (b, _, _) => Ready(b, x => x.Rebuild()));
    /// <summary>
    /// The gap between nested groups when this group holds groups rather than buttons. Ignored otherwise —
    /// segments of one group are never spaced, that is what makes them one control.
    /// </summary>
    public double ClusterSpacing
    {
        get => (double)this.GetValue(ClusterSpacingProperty);
        set => this.SetValue(ClusterSpacingProperty, value);
    }


    // -- selection ---------------------------------------------------------------------------------

    public static readonly BindableProperty SelectionModeProperty = BindableProperty.Create(
        nameof(SelectionMode), typeof(ButtonGroupSelectionMode), typeof(ButtonGroup), ButtonGroupSelectionMode.None,
        propertyChanged: (b, _, _) => Ready(b, x =>
        {
            if (x.SelectionMode == ButtonGroupSelectionMode.None)
                x.ClearSelection();

            x.ApplySelectionVisuals();
        }));
    /// <summary>
    /// Whether the group carries selection state. <see cref="ButtonGroupSelectionMode.None"/> by default:
    /// a group of actions has nothing selected, and turning this on is what makes it a picker.
    /// </summary>
    public ButtonGroupSelectionMode SelectionMode
    {
        get => (ButtonGroupSelectionMode)this.GetValue(SelectionModeProperty);
        set => this.SetValue(SelectionModeProperty, value);
    }

    public static readonly BindableProperty SelectedIndexProperty = BindableProperty.Create(
        nameof(SelectedIndex), typeof(int), typeof(ButtonGroup), -1, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => Ready(b, x => x.OnSelectedIndexChanged((int)n)));
    /// <summary>
    /// The selected segment, or -1 for none. Two-way, and in
    /// <see cref="ButtonGroupSelectionMode.Multiple"/> it reports the first selected segment — read
    /// <see cref="SelectedIndexes"/> for the rest.
    /// </summary>
    public int SelectedIndex
    {
        get => (int)this.GetValue(SelectedIndexProperty);
        set => this.SetValue(SelectedIndexProperty, value);
    }

    public static readonly BindableProperty AllowDeselectProperty = BindableProperty.Create(
        nameof(AllowDeselect), typeof(bool), typeof(ButtonGroup), false);
    /// <summary>
    /// Whether tapping the selected segment in <see cref="ButtonGroupSelectionMode.Single"/> clears the
    /// selection. Off by default — a picker that can be emptied by tapping its own answer again usually
    /// is not what was wanted. Ignored in multiple mode, where a second tap always toggles.
    /// </summary>
    public bool AllowDeselect
    {
        get => (bool)this.GetValue(AllowDeselectProperty);
        set => this.SetValue(AllowDeselectProperty, value);
    }

    public static readonly BindableProperty SelectedAppearanceProperty = BindableProperty.Create(
        nameof(SelectedAppearance), typeof(ButtonAppearance), typeof(ButtonGroup), ButtonAppearance.Filled,
        propertyChanged: (b, _, _) => Ready(b, x => x.ApplySelectionVisuals()));
    /// <summary>The appearance a selected segment takes while the group is in a selection mode.</summary>
    public ButtonAppearance SelectedAppearance
    {
        get => (ButtonAppearance)this.GetValue(SelectedAppearanceProperty);
        set => this.SetValue(SelectedAppearanceProperty, value);
    }

    public static readonly BindableProperty UnselectedAppearanceProperty = BindableProperty.Create(
        nameof(UnselectedAppearance), typeof(ButtonAppearance), typeof(ButtonGroup), ButtonAppearance.Outlined,
        propertyChanged: (b, _, _) => Ready(b, x => x.ApplySelectionVisuals()));
    /// <summary>
    /// The appearance an unselected segment takes — unless the segment set an <c>Appearance</c> of its
    /// own, which wins, so one odd segment in a picker stays odd.
    /// </summary>
    public ButtonAppearance UnselectedAppearance
    {
        get => (ButtonAppearance)this.GetValue(UnselectedAppearanceProperty);
        set => this.SetValue(UnselectedAppearanceProperty, value);
    }

    public static readonly BindableProperty SelectionChangedCommandProperty = BindableProperty.Create(
        nameof(SelectionChangedCommand), typeof(ICommand), typeof(ButtonGroup), null);
    /// <summary>Invoked after <see cref="SelectionChanged"/>, with <see cref="SelectedIndex"/> unless a parameter is set.</summary>
    public ICommand? SelectionChangedCommand
    {
        get => (ICommand?)this.GetValue(SelectionChangedCommandProperty);
        set => this.SetValue(SelectionChangedCommandProperty, value);
    }

    public static readonly BindableProperty SelectionChangedCommandParameterProperty = BindableProperty.Create(
        nameof(SelectionChangedCommandParameter), typeof(object), typeof(ButtonGroup), null);
    /// <inheritdoc cref="SelectionChangedCommandProperty"/>
    public object? SelectionChangedCommandParameter
    {
        get => this.GetValue(SelectionChangedCommandParameterProperty);
        set => this.SetValue(SelectionChangedCommandParameterProperty, value);
    }
}

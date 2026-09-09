using System.Collections.ObjectModel;
using System.Windows.Input;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

public partial class FloatingToolbar
{
    static void Rewire(BindableObject b, object o, object n)
        => StyleGuard.WhenReady<FloatingToolbar>(b, t => t.RewireTriggers());

    static void Rebuild(BindableObject b, object o, object n)
        => StyleGuard.WhenReady<FloatingToolbar>(b, t => t.RebuildStrip());


    // ---------------------------------------------------------------------------------------------
    // Content
    // ---------------------------------------------------------------------------------------------

    /// <summary>The actions on the bar. Items with <c>Children</c> open a dropdown instead of acting.</summary>
    public static readonly BindableProperty ItemsProperty = BindableProperty.Create(
        nameof(Items),
        typeof(IList<ShinyToolbarItem>),
        typeof(FloatingToolbar),
        null,
        // A lazily-created default never fires propertyChanged, so the collection is made in the
        // constructor instead and this only has to handle a wholesale replacement.
        propertyChanged: (b, o, n) => ((FloatingToolbar)b).OnItemsChanged(o as IList<ShinyToolbarItem>, n as IList<ShinyToolbarItem>));

    /// <inheritdoc cref="ItemsProperty" />
    public IList<ShinyToolbarItem> Items
    {
        get => (IList<ShinyToolbarItem>)this.GetValue(ItemsProperty);
        set => this.SetValue(ItemsProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // Targeting
    // ---------------------------------------------------------------------------------------------

    /// <summary>The view the bar points at. Beats <see cref="TargetName"/> and <c>Content</c>.</summary>
    public static readonly BindableProperty TargetProperty = BindableProperty.Create(
        nameof(Target), typeof(View), typeof(FloatingToolbar), null, propertyChanged: Rewire);

    /// <inheritdoc cref="TargetProperty" />
    public View? Target
    {
        get => (View?)this.GetValue(TargetProperty);
        set => this.SetValue(TargetProperty, value);
    }


    /// <summary>Name of the target, for when <c>{x:Reference}</c> cannot see it.</summary>
    public static readonly BindableProperty TargetNameProperty = BindableProperty.Create(
        nameof(TargetName), typeof(string), typeof(FloatingToolbar), null, propertyChanged: Rewire);

    /// <inheritdoc cref="TargetNameProperty" />
    public string? TargetName
    {
        get => (string?)this.GetValue(TargetNameProperty);
        set => this.SetValue(TargetNameProperty, value);
    }


    /// <summary>What opens the bar.</summary>
    public static readonly BindableProperty TriggerProperty = BindableProperty.Create(
        nameof(Trigger), typeof(TooltipTrigger), typeof(FloatingToolbar), TooltipTrigger.Tap,
        propertyChanged: Rewire);

    /// <inheritdoc cref="TriggerProperty" />
    public TooltipTrigger Trigger
    {
        get => (TooltipTrigger)this.GetValue(TriggerProperty);
        set => this.SetValue(TriggerProperty, value);
    }


    /// <summary>Whether the bar is up. Two-way bindable.</summary>
    public static readonly BindableProperty IsOpenProperty = BindableProperty.Create(
        nameof(IsOpen), typeof(bool), typeof(FloatingToolbar), false, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => ((FloatingToolbar)b).OnIsOpenChanged((bool)n));

    /// <inheritdoc cref="IsOpenProperty" />
    public bool IsOpen
    {
        get => (bool)this.GetValue(IsOpenProperty);
        set => this.SetValue(IsOpenProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------------------

    /// <summary>Across or down. Also decides whether overflow is measured on width or on height.</summary>
    public static readonly BindableProperty OrientationProperty = BindableProperty.Create(
        nameof(Orientation), typeof(ToolbarOrientation), typeof(FloatingToolbar),
        ToolbarOrientation.Horizontal, propertyChanged: Rebuild);

    /// <inheritdoc cref="OrientationProperty" />
    public ToolbarOrientation Orientation
    {
        get => (ToolbarOrientation)this.GetValue(OrientationProperty);
        set => this.SetValue(OrientationProperty, value);
    }


    /// <summary>Which side of the target the bar sits on. <c>Auto</c> picks the side with room.</summary>
    public static readonly BindableProperty PlacementProperty = BindableProperty.Create(
        nameof(Placement), typeof(TooltipPlacement), typeof(FloatingToolbar), TooltipPlacement.Top);

    /// <inheritdoc cref="PlacementProperty" />
    public TooltipPlacement Placement
    {
        get => (TooltipPlacement)this.GetValue(PlacementProperty);
        set => this.SetValue(PlacementProperty, value);
    }


    /// <summary>Gap between the target and the bar.</summary>
    public static readonly BindableProperty OffsetProperty = BindableProperty.Create(
        nameof(Offset), typeof(double), typeof(FloatingToolbar), 8d);

    /// <inheritdoc cref="OffsetProperty" />
    public double Offset
    {
        get => (double)this.GetValue(OffsetProperty);
        set => this.SetValue(OffsetProperty, value);
    }


    /// <summary>Space kept clear at the screen edges.</summary>
    public static readonly BindableProperty ScreenMarginProperty = BindableProperty.Create(
        nameof(ScreenMargin), typeof(double), typeof(FloatingToolbar), 12d);

    /// <inheritdoc cref="ScreenMarginProperty" />
    public double ScreenMargin
    {
        get => (double)this.GetValue(ScreenMarginProperty);
        set => this.SetValue(ScreenMarginProperty, value);
    }


    /// <summary>Draw each item's text beside its icon.</summary>
    public static readonly BindableProperty ShowLabelsProperty = BindableProperty.Create(
        nameof(ShowLabels), typeof(bool), typeof(FloatingToolbar), false, propertyChanged: Rebuild);

    /// <inheritdoc cref="ShowLabelsProperty" />
    public bool ShowLabels
    {
        get => (bool)this.GetValue(ShowLabelsProperty);
        set => this.SetValue(ShowLabelsProperty, value);
    }


    /// <summary>Size of one icon-only item along the bar.</summary>
    public static readonly BindableProperty ItemSizeProperty = BindableProperty.Create(
        nameof(ItemSize), typeof(double), typeof(FloatingToolbar), 40d, propertyChanged: Rebuild);

    /// <inheritdoc cref="ItemSizeProperty" />
    public double ItemSize
    {
        get => (double)this.GetValue(ItemSizeProperty);
        set => this.SetValue(ItemSizeProperty, value);
    }


    /// <summary>Fold items that do not fit into a "⋯" dropdown rather than letting them run off.</summary>
    public static readonly BindableProperty OverflowEnabledProperty = BindableProperty.Create(
        nameof(OverflowEnabled), typeof(bool), typeof(FloatingToolbar), true, propertyChanged: Rebuild);

    /// <inheritdoc cref="OverflowEnabledProperty" />
    public bool OverflowEnabled
    {
        get => (bool)this.GetValue(OverflowEnabledProperty);
        set => this.SetValue(OverflowEnabledProperty, value);
    }


    /// <summary>
    /// Cap on items drawn on the bar. Zero works it out from the room the screen actually has along
    /// the orientation. The cap <b>counts the overflow button</b>.
    /// </summary>
    public static readonly BindableProperty MaxVisibleItemsProperty = BindableProperty.Create(
        nameof(MaxVisibleItems), typeof(int), typeof(FloatingToolbar), 0, propertyChanged: Rebuild);

    /// <inheritdoc cref="MaxVisibleItemsProperty" />
    public int MaxVisibleItems
    {
        get => (int)this.GetValue(MaxVisibleItemsProperty);
        set => this.SetValue(MaxVisibleItemsProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // Timing and dismissal
    // ---------------------------------------------------------------------------------------------

    /// <summary>Milliseconds a trigger has to persist before the bar opens.</summary>
    public static readonly BindableProperty ShowDelayProperty = BindableProperty.Create(
        nameof(ShowDelay), typeof(int), typeof(FloatingToolbar), 0);

    /// <inheritdoc cref="ShowDelayProperty" />
    public int ShowDelay
    {
        get => (int)this.GetValue(ShowDelayProperty);
        set => this.SetValue(ShowDelayProperty, value);
    }


    /// <summary>
    /// Milliseconds the bar lingers after the pointer leaves the target. This is what makes a hover
    /// toolbar usable at all: without a grace period the bar vanishes in the gap between the target
    /// and the buttons, and none of them can ever be reached.
    /// </summary>
    public static readonly BindableProperty HideDelayProperty = BindableProperty.Create(
        nameof(HideDelay), typeof(int), typeof(FloatingToolbar), 250);

    /// <inheritdoc cref="HideDelayProperty" />
    public int HideDelay
    {
        get => (int)this.GetValue(HideDelayProperty);
        set => this.SetValue(HideDelayProperty, value);
    }


    /// <summary>How long a press is held before <see cref="TooltipTrigger.LongPress"/> opens the bar.</summary>
    public static readonly BindableProperty LongPressDelayProperty = BindableProperty.Create(
        nameof(LongPressDelay), typeof(int), typeof(FloatingToolbar), 450, propertyChanged: Rewire);

    /// <inheritdoc cref="LongPressDelayProperty" />
    public int LongPressDelay
    {
        get => (int)this.GetValue(LongPressDelayProperty);
        set => this.SetValue(LongPressDelayProperty, value);
    }


    /// <summary>Milliseconds before the bar closes itself. Zero leaves it up.</summary>
    public static readonly BindableProperty AutoDismissDelayProperty = BindableProperty.Create(
        nameof(AutoDismissDelay), typeof(int), typeof(FloatingToolbar), 0);

    /// <inheritdoc cref="AutoDismissDelayProperty" />
    public int AutoDismissDelay
    {
        get => (int)this.GetValue(AutoDismissDelayProperty);
        set => this.SetValue(AutoDismissDelayProperty, value);
    }


    /// <summary>Close the bar once an item is invoked.</summary>
    public static readonly BindableProperty DismissOnItemClickProperty = BindableProperty.Create(
        nameof(DismissOnItemClick), typeof(bool), typeof(FloatingToolbar), true);

    /// <inheritdoc cref="DismissOnItemClickProperty" />
    public bool DismissOnItemClick
    {
        get => (bool)this.GetValue(DismissOnItemClickProperty);
        set => this.SetValue(DismissOnItemClickProperty, value);
    }


    /// <summary>Close the bar on a tap anywhere else.</summary>
    public static readonly BindableProperty DismissOnTapOutsideProperty = BindableProperty.Create(
        nameof(DismissOnTapOutside), typeof(bool), typeof(FloatingToolbar), true);

    /// <inheritdoc cref="DismissOnTapOutsideProperty" />
    public bool DismissOnTapOutside
    {
        get => (bool)this.GetValue(DismissOnTapOutsideProperty);
        set => this.SetValue(DismissOnTapOutsideProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // Chrome
    // ---------------------------------------------------------------------------------------------

    /// <summary>Foreground for items that do not override it.</summary>
    public static readonly BindableProperty ForegroundColorProperty = BindableProperty.Create(
        nameof(ForegroundColor), typeof(Color), typeof(FloatingToolbar), null, propertyChanged: Rebuild);

    /// <inheritdoc cref="ForegroundColorProperty" />
    public Color? ForegroundColor
    {
        get => (Color?)this.GetValue(ForegroundColorProperty);
        set => this.SetValue(ForegroundColorProperty, value);
    }


    /// <summary>The bar's surface. Unset follows the theme.</summary>
    public static readonly BindableProperty BarColorProperty = BindableProperty.Create(
        nameof(BarColor), typeof(Color), typeof(FloatingToolbar), null, propertyChanged: Rebuild);

    /// <inheritdoc cref="BarColorProperty" />
    public Color? BarColor
    {
        get => (Color?)this.GetValue(BarColorProperty);
        set => this.SetValue(BarColorProperty, value);
    }


    /// <summary>Corner radius of the bar and its menus. Unset follows the theme's shape scale.</summary>
    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(
        nameof(CornerRadius), typeof(double), typeof(FloatingToolbar), ThemeTokens.Unset, propertyChanged: Rebuild);

    /// <inheritdoc cref="CornerRadiusProperty" />
    public double CornerRadius
    {
        get => (double)this.GetValue(CornerRadiusProperty);
        set => this.SetValue(CornerRadiusProperty, value);
    }


    /// <summary>How the bar enters and leaves.</summary>
    public static readonly BindableProperty AnimationProperty = BindableProperty.Create(
        nameof(Animation), typeof(TooltipAnimation), typeof(FloatingToolbar), TooltipAnimation.Scale);

    /// <inheritdoc cref="AnimationProperty" />
    public TooltipAnimation Animation
    {
        get => (TooltipAnimation)this.GetValue(AnimationProperty);
        set => this.SetValue(AnimationProperty, value);
    }


    /// <summary>Length of the open/close animation in milliseconds. Zero snaps.</summary>
    public static readonly BindableProperty AnimationLengthProperty = BindableProperty.Create(
        nameof(AnimationLength), typeof(uint), typeof(FloatingToolbar), 140u);

    /// <inheritdoc cref="AnimationLengthProperty" />
    public uint AnimationLength
    {
        get => (uint)this.GetValue(AnimationLengthProperty);
        set => this.SetValue(AnimationLengthProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // Notifications
    // ---------------------------------------------------------------------------------------------

    /// <inheritdoc cref="ItemClicked" />
    public static readonly BindableProperty ItemClickedCommandProperty = BindableProperty.Create(
        nameof(ItemClickedCommand), typeof(ICommand), typeof(FloatingToolbar), null);

    /// <inheritdoc cref="ItemClickedCommandProperty" />
    public ICommand? ItemClickedCommand
    {
        get => (ICommand?)this.GetValue(ItemClickedCommandProperty);
        set => this.SetValue(ItemClickedCommandProperty, value);
    }


    /// <inheritdoc cref="Opened" />
    public static readonly BindableProperty OpenedCommandProperty = BindableProperty.Create(
        nameof(OpenedCommand), typeof(ICommand), typeof(FloatingToolbar), null);

    /// <inheritdoc cref="OpenedCommandProperty" />
    public ICommand? OpenedCommand
    {
        get => (ICommand?)this.GetValue(OpenedCommandProperty);
        set => this.SetValue(OpenedCommandProperty, value);
    }


    /// <inheritdoc cref="Closed" />
    public static readonly BindableProperty ClosedCommandProperty = BindableProperty.Create(
        nameof(ClosedCommand), typeof(ICommand), typeof(FloatingToolbar), null);

    /// <inheritdoc cref="ClosedCommandProperty" />
    public ICommand? ClosedCommand
    {
        get => (ICommand?)this.GetValue(ClosedCommandProperty);
        set => this.SetValue(ClosedCommandProperty, value);
    }
}

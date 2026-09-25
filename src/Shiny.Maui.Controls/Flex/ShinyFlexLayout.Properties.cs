using System.ComponentModel;
using Microsoft.Maui.Converters;
using Microsoft.Maui.Layouts;

namespace Shiny.Maui.Controls;

public partial class ShinyFlexLayout
{
    // Every property below funnels into OnFlexPropertyChanged, which drops the cached pass and asks
    // for a new one. Nothing is recomputed eagerly: a burst of changes costs one layout pass.

    public static readonly BindableProperty DirectionProperty = BindableProperty.Create(
        nameof(Direction),
        typeof(FlexDirection),
        typeof(ShinyFlexLayout),
        FlexDirection.Row,
        propertyChanged: OnFlexPropertyChanged);

    /// <summary>The main axis, and which end of it the first child starts from. Defaults to <see cref="FlexDirection.Row"/>.</summary>
    [TypeConverter(typeof(FlexDirectionTypeConverter))]
    public FlexDirection Direction
    {
        get => (FlexDirection)this.GetValue(DirectionProperty);
        set => this.SetValue(DirectionProperty, value);
    }

    public static readonly BindableProperty WrapProperty = BindableProperty.Create(
        nameof(Wrap),
        typeof(FlexWrap),
        typeof(ShinyFlexLayout),
        FlexWrap.NoWrap,
        propertyChanged: OnFlexPropertyChanged);

    /// <summary>Whether children that run out of main-axis room start a new line, and which way lines stack.</summary>
    [TypeConverter(typeof(FlexWrapTypeConverter))]
    public FlexWrap Wrap
    {
        get => (FlexWrap)this.GetValue(WrapProperty);
        set => this.SetValue(WrapProperty, value);
    }

    public static readonly BindableProperty JustifyContentProperty = BindableProperty.Create(
        nameof(JustifyContent),
        typeof(FlexJustify),
        typeof(ShinyFlexLayout),
        FlexJustify.Start,
        propertyChanged: OnFlexPropertyChanged);

    /// <summary>How the space left over on each line is shared out along the main axis.</summary>
    [TypeConverter(typeof(FlexJustifyTypeConverter))]
    public FlexJustify JustifyContent
    {
        get => (FlexJustify)this.GetValue(JustifyContentProperty);
        set => this.SetValue(JustifyContentProperty, value);
    }

    public static readonly BindableProperty AlignItemsProperty = BindableProperty.Create(
        nameof(AlignItems),
        typeof(FlexAlignItems),
        typeof(ShinyFlexLayout),
        FlexAlignItems.Stretch,
        propertyChanged: OnFlexPropertyChanged);

    /// <summary>Where each child sits across its line, unless it overrides it with <see cref="AlignSelfProperty"/>.</summary>
    [TypeConverter(typeof(FlexAlignItemsTypeConverter))]
    public FlexAlignItems AlignItems
    {
        get => (FlexAlignItems)this.GetValue(AlignItemsProperty);
        set => this.SetValue(AlignItemsProperty, value);
    }

    public static readonly BindableProperty AlignContentProperty = BindableProperty.Create(
        nameof(AlignContent),
        typeof(FlexAlignContent),
        typeof(ShinyFlexLayout),
        FlexAlignContent.Stretch,
        propertyChanged: OnFlexPropertyChanged);

    /// <summary>How wrapped lines share the cross-axis space. Has no effect unless <see cref="Wrap"/> is on.</summary>
    [TypeConverter(typeof(FlexAlignContentTypeConverter))]
    public FlexAlignContent AlignContent
    {
        get => (FlexAlignContent)this.GetValue(AlignContentProperty);
        set => this.SetValue(AlignContentProperty, value);
    }

    public static readonly BindableProperty RowSpacingProperty = BindableProperty.Create(
        nameof(RowSpacing),
        typeof(double),
        typeof(ShinyFlexLayout),
        0d,
        propertyChanged: OnFlexPropertyChanged);

    /// <summary>
    /// Gap between rows, as CSS <c>row-gap</c>: between wrapped lines in a row layout, and between
    /// children in a column layout. MAUI's own FlexLayout has no gap at all.
    /// </summary>
    public double RowSpacing
    {
        get => (double)this.GetValue(RowSpacingProperty);
        set => this.SetValue(RowSpacingProperty, value);
    }

    public static readonly BindableProperty ColumnSpacingProperty = BindableProperty.Create(
        nameof(ColumnSpacing),
        typeof(double),
        typeof(ShinyFlexLayout),
        0d,
        propertyChanged: OnFlexPropertyChanged);

    /// <summary>
    /// Gap between columns, as CSS <c>column-gap</c>: between children in a row layout, and between
    /// wrapped lines in a column layout.
    /// </summary>
    public double ColumnSpacing
    {
        get => (double)this.GetValue(ColumnSpacingProperty);
        set => this.SetValue(ColumnSpacingProperty, value);
    }

    public static readonly BindableProperty IsMeasureCacheEnabledProperty = BindableProperty.Create(
        nameof(IsMeasureCacheEnabled),
        typeof(bool),
        typeof(ShinyFlexLayout),
        true,
        propertyChanged: OnFlexPropertyChanged);

    /// <summary>
    /// Whether child measurements and whole layout passes are reused until something invalidates them.
    /// On by default — it is where most of the speed comes from. Turn it off only for a child that
    /// changes size natively without raising <see cref="VisualElement.MeasureInvalidated"/>; calling
    /// <c>InvalidateMeasure()</c> on that child is the better fix.
    /// </summary>
    public bool IsMeasureCacheEnabled
    {
        get => (bool)this.GetValue(IsMeasureCacheEnabledProperty);
        set => this.SetValue(IsMeasureCacheEnabledProperty, value);
    }

    static void OnFlexPropertyChanged(BindableObject bindable, object oldValue, object newValue)
        => ((ShinyFlexLayout)bindable).InvalidateFlex(clearChildCaches: false);


    // Attached properties. Their values are copied into the child's cached state when they change, so
    // a layout pass never calls GetValue on a child - see ChildState.

    public static readonly BindableProperty OrderProperty = BindableProperty.CreateAttached(
        "Order",
        typeof(int),
        typeof(ShinyFlexLayout),
        0,
        propertyChanged: OnChildFlexPropertyChanged);

    /// <summary>Visual position among its siblings; lower first, ties keep their child order.</summary>
    public static int GetOrder(BindableObject view) => (int)view.GetValue(OrderProperty);
    public static void SetOrder(BindableObject view, int value) => view.SetValue(OrderProperty, value);

    public static readonly BindableProperty GrowProperty = BindableProperty.CreateAttached(
        "Grow",
        typeof(float),
        typeof(ShinyFlexLayout),
        0f,
        propertyChanged: OnChildFlexPropertyChanged,
        validateValue: (_, v) => (float)v >= 0);

    /// <summary>Share of a line's spare room this child takes, relative to its siblings. 0 (the default) takes none.</summary>
    public static float GetGrow(BindableObject view) => (float)view.GetValue(GrowProperty);
    public static void SetGrow(BindableObject view, float value) => view.SetValue(GrowProperty, value);

    public static readonly BindableProperty ShrinkProperty = BindableProperty.CreateAttached(
        "Shrink",
        typeof(float),
        typeof(ShinyFlexLayout),
        1f,
        propertyChanged: OnChildFlexPropertyChanged,
        validateValue: (_, v) => (float)v >= 0);

    /// <summary>How readily this child gives up room when a line overflows, weighted by its size. 0 never shrinks.</summary>
    public static float GetShrink(BindableObject view) => (float)view.GetValue(ShrinkProperty);
    public static void SetShrink(BindableObject view, float value) => view.SetValue(ShrinkProperty, value);

    public static readonly BindableProperty BasisProperty = BindableProperty.CreateAttached(
        "Basis",
        typeof(FlexBasis),
        typeof(ShinyFlexLayout),
        FlexBasis.Auto,
        propertyChanged: OnChildFlexPropertyChanged);

    /// <summary>
    /// Starting main-axis size before growing or shrinking: <c>Auto</c> (measure the child), a length,
    /// or a percentage of the line such as <c>"25%"</c>.
    /// </summary>
    [TypeConverter(typeof(FlexBasisTypeConverter))]
    public static FlexBasis GetBasis(BindableObject view) => (FlexBasis)view.GetValue(BasisProperty);
    public static void SetBasis(BindableObject view, FlexBasis value) => view.SetValue(BasisProperty, value);

    public static readonly BindableProperty AlignSelfProperty = BindableProperty.CreateAttached(
        "AlignSelf",
        typeof(FlexAlignSelf),
        typeof(ShinyFlexLayout),
        FlexAlignSelf.Auto,
        propertyChanged: OnChildFlexPropertyChanged);

    /// <summary>Overrides <see cref="AlignItems"/> for one child. <c>Auto</c> (the default) follows the layout.</summary>
    [TypeConverter(typeof(FlexAlignSelfTypeConverter))]
    public static FlexAlignSelf GetAlignSelf(BindableObject view) => (FlexAlignSelf)view.GetValue(AlignSelfProperty);
    public static void SetAlignSelf(BindableObject view, FlexAlignSelf value) => view.SetValue(AlignSelfProperty, value);

    static void OnChildFlexPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        // Set before the child is added (the usual XAML order) there is no layout to tell yet; the value
        // is read when the child arrives.
        if (bindable is Element { Parent: ShinyFlexLayout flex } element)
            flex.OnChildFlexValuesChanged(element);
    }
}

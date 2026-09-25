namespace Shiny.Maui.Controls;

public partial class YogaLayout
{
    // ---------------------------------------------------------------- container

    public static readonly BindableProperty FlexDirectionProperty = BindableProperty.Create(
        nameof(FlexDirection), typeof(YogaFlexDirection), typeof(YogaLayout), YogaFlexDirection.Column,
        propertyChanged: OnLayoutPropertyChanged);

    public static readonly BindableProperty JustifyContentProperty = BindableProperty.Create(
        nameof(JustifyContent), typeof(YogaJustify), typeof(YogaLayout), YogaJustify.FlexStart,
        propertyChanged: OnLayoutPropertyChanged);

    public static readonly BindableProperty AlignItemsProperty = BindableProperty.Create(
        nameof(AlignItems), typeof(YogaAlign), typeof(YogaLayout), YogaAlign.Stretch,
        propertyChanged: OnLayoutPropertyChanged);

    public static readonly BindableProperty AlignContentProperty = BindableProperty.Create(
        nameof(AlignContent), typeof(YogaAlign), typeof(YogaLayout), YogaAlign.FlexStart,
        propertyChanged: OnLayoutPropertyChanged);

    public static readonly BindableProperty FlexWrapProperty = BindableProperty.Create(
        nameof(FlexWrap), typeof(YogaWrap), typeof(YogaLayout), YogaWrap.NoWrap,
        propertyChanged: OnLayoutPropertyChanged);

    public static readonly BindableProperty GapProperty = BindableProperty.Create(
        nameof(Gap), typeof(double), typeof(YogaLayout), 0d,
        propertyChanged: OnLayoutPropertyChanged);

    public static readonly BindableProperty RowGapProperty = BindableProperty.Create(
        nameof(RowGap), typeof(double), typeof(YogaLayout), double.NaN,
        propertyChanged: OnLayoutPropertyChanged);

    public static readonly BindableProperty ColumnGapProperty = BindableProperty.Create(
        nameof(ColumnGap), typeof(double), typeof(YogaLayout), double.NaN,
        propertyChanged: OnLayoutPropertyChanged);

    /// <summary>The main axis. Defaults to <see cref="YogaFlexDirection.Column"/>, as in Yoga.</summary>
    public YogaFlexDirection FlexDirection
    {
        get => (YogaFlexDirection)this.GetValue(FlexDirectionProperty);
        set => this.SetValue(FlexDirectionProperty, value);
    }

    /// <summary>How free space on each line is shared out along the main axis.</summary>
    public YogaJustify JustifyContent
    {
        get => (YogaJustify)this.GetValue(JustifyContentProperty);
        set => this.SetValue(JustifyContentProperty, value);
    }

    /// <summary>Cross-axis alignment of children within their line. Defaults to <see cref="YogaAlign.Stretch"/>.</summary>
    public YogaAlign AlignItems
    {
        get => (YogaAlign)this.GetValue(AlignItemsProperty);
        set => this.SetValue(AlignItemsProperty, value);
    }

    /// <summary>How wrapped lines are placed across the cross axis. Defaults to <see cref="YogaAlign.FlexStart"/>, as in Yoga.</summary>
    public YogaAlign AlignContent
    {
        get => (YogaAlign)this.GetValue(AlignContentProperty);
        set => this.SetValue(AlignContentProperty, value);
    }

    /// <summary>Whether children wrap onto more lines.</summary>
    public YogaWrap FlexWrap
    {
        get => (YogaWrap)this.GetValue(FlexWrapProperty);
        set => this.SetValue(FlexWrapProperty, value);
    }

    /// <summary>Space between children and between lines, unless <see cref="RowGap"/>/<see cref="ColumnGap"/> say otherwise.</summary>
    public double Gap
    {
        get => (double)this.GetValue(GapProperty);
        set => this.SetValue(GapProperty, value);
    }

    /// <summary>Vertical space between rows of children. NaN (the default) uses <see cref="Gap"/>.</summary>
    public double RowGap
    {
        get => (double)this.GetValue(RowGapProperty);
        set => this.SetValue(RowGapProperty, value);
    }

    /// <summary>Horizontal space between columns of children. NaN (the default) uses <see cref="Gap"/>.</summary>
    public double ColumnGap
    {
        get => (double)this.GetValue(ColumnGapProperty);
        set => this.SetValue(ColumnGapProperty, value);
    }

    static void OnLayoutPropertyChanged(BindableObject bindable, object oldValue, object newValue)
        => ((YogaLayout)bindable).Invalidate();


    // ---------------------------------------------------------------- children (attached)

    public static readonly BindableProperty FlexGrowProperty = Attached("FlexGrow", typeof(double), 0d);
    public static readonly BindableProperty FlexShrinkProperty = Attached("FlexShrink", typeof(double), 0d);
    public static readonly BindableProperty FlexBasisProperty = Attached("FlexBasis", typeof(YogaValue), YogaValue.Auto);
    public static readonly BindableProperty AlignSelfProperty = Attached("AlignSelf", typeof(YogaAlign), YogaAlign.Auto);
    public static readonly BindableProperty PositionTypeProperty = Attached("PositionType", typeof(YogaPositionType), YogaPositionType.Relative);
    public static readonly BindableProperty DisplayProperty = Attached("Display", typeof(YogaDisplay), YogaDisplay.Flex);
    public static readonly BindableProperty AspectRatioProperty = Attached("AspectRatio", typeof(double), double.NaN);
    public static readonly BindableProperty AutoMarginsProperty = Attached("AutoMargins", typeof(YogaEdges), YogaEdges.None);

    public static readonly BindableProperty LeftProperty = Attached("Left", typeof(YogaValue), YogaValue.Undefined);
    public static readonly BindableProperty TopProperty = Attached("Top", typeof(YogaValue), YogaValue.Undefined);
    public static readonly BindableProperty RightProperty = Attached("Right", typeof(YogaValue), YogaValue.Undefined);
    public static readonly BindableProperty BottomProperty = Attached("Bottom", typeof(YogaValue), YogaValue.Undefined);
    public static readonly BindableProperty StartProperty = Attached("Start", typeof(YogaValue), YogaValue.Undefined);
    public static readonly BindableProperty EndProperty = Attached("End", typeof(YogaValue), YogaValue.Undefined);

    // Yoga calls these width/height, but on a MAUI layout those names belong to VisualElement: XAML resolves
    // shiny:YogaLayout.Width to VisualElement.Width (a double) and "50%" fails to compile. Hence NodeWidth.
    public static readonly BindableProperty NodeWidthProperty = Attached("NodeWidth", typeof(YogaValue), YogaValue.Auto);
    public static readonly BindableProperty NodeHeightProperty = Attached("NodeHeight", typeof(YogaValue), YogaValue.Auto);
    public static readonly BindableProperty MinWidthProperty = Attached("MinWidth", typeof(YogaValue), YogaValue.Undefined);
    public static readonly BindableProperty MinHeightProperty = Attached("MinHeight", typeof(YogaValue), YogaValue.Undefined);
    public static readonly BindableProperty MaxWidthProperty = Attached("MaxWidth", typeof(YogaValue), YogaValue.Undefined);
    public static readonly BindableProperty MaxHeightProperty = Attached("MaxHeight", typeof(YogaValue), YogaValue.Undefined);

    static BindableProperty Attached(string name, Type type, object defaultValue)
        => BindableProperty.CreateAttached(name, type, typeof(YogaLayout), defaultValue, propertyChanged: OnChildPropertyChanged);

    static void OnChildPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is Element { Parent: YogaLayout yoga })
            yoga.Invalidate();
    }

    /// <summary>How much of the line's free space this child takes, relative to its siblings. Default 0.</summary>
    public static double GetFlexGrow(BindableObject view) => (double)view.GetValue(FlexGrowProperty);
    public static void SetFlexGrow(BindableObject view, double value) => view.SetValue(FlexGrowProperty, value);

    /// <summary>How much this child gives up when the line overflows. Default 0, as in Yoga (CSS uses 1).</summary>
    public static double GetFlexShrink(BindableObject view) => (double)view.GetValue(FlexShrinkProperty);
    public static void SetFlexShrink(BindableObject view, double value) => view.SetValue(FlexShrinkProperty, value);

    /// <summary>The main-axis size before growing or shrinking. <c>auto</c> uses the size, then the measured content.</summary>
    public static YogaValue GetFlexBasis(BindableObject view) => (YogaValue)view.GetValue(FlexBasisProperty);
    public static void SetFlexBasis(BindableObject view, YogaValue value) => view.SetValue(FlexBasisProperty, value);

    /// <summary>Overrides the container's <see cref="AlignItems"/> for this child.</summary>
    public static YogaAlign GetAlignSelf(BindableObject view) => (YogaAlign)view.GetValue(AlignSelfProperty);
    public static void SetAlignSelf(BindableObject view, YogaAlign value) => view.SetValue(AlignSelfProperty, value);

    public static YogaPositionType GetPositionType(BindableObject view) => (YogaPositionType)view.GetValue(PositionTypeProperty);
    public static void SetPositionType(BindableObject view, YogaPositionType value) => view.SetValue(PositionTypeProperty, value);

    public static YogaDisplay GetDisplay(BindableObject view) => (YogaDisplay)view.GetValue(DisplayProperty);
    public static void SetDisplay(BindableObject view, YogaDisplay value) => view.SetValue(DisplayProperty, value);

    /// <summary>Width divided by height. When only one of them is known, the other follows from it.</summary>
    public static double GetAspectRatio(BindableObject view) => (double)view.GetValue(AspectRatioProperty);
    public static void SetAspectRatio(BindableObject view, double value) => view.SetValue(AspectRatioProperty, value);

    /// <summary>Which of the child's margins are <c>auto</c> (see <see cref="YogaEdges"/>).</summary>
    public static YogaEdges GetAutoMargins(BindableObject view) => (YogaEdges)view.GetValue(AutoMarginsProperty);
    public static void SetAutoMargins(BindableObject view, YogaEdges value) => view.SetValue(AutoMarginsProperty, value);

    public static YogaValue GetLeft(BindableObject view) => (YogaValue)view.GetValue(LeftProperty);
    public static void SetLeft(BindableObject view, YogaValue value) => view.SetValue(LeftProperty, value);
    public static YogaValue GetTop(BindableObject view) => (YogaValue)view.GetValue(TopProperty);
    public static void SetTop(BindableObject view, YogaValue value) => view.SetValue(TopProperty, value);
    public static YogaValue GetRight(BindableObject view) => (YogaValue)view.GetValue(RightProperty);
    public static void SetRight(BindableObject view, YogaValue value) => view.SetValue(RightProperty, value);
    public static YogaValue GetBottom(BindableObject view) => (YogaValue)view.GetValue(BottomProperty);
    public static void SetBottom(BindableObject view, YogaValue value) => view.SetValue(BottomProperty, value);

    /// <summary>Left inset in left-to-right flow, right inset in right-to-left. Wins over Left/Right.</summary>
    public static YogaValue GetStart(BindableObject view) => (YogaValue)view.GetValue(StartProperty);
    public static void SetStart(BindableObject view, YogaValue value) => view.SetValue(StartProperty, value);
    public static YogaValue GetEnd(BindableObject view) => (YogaValue)view.GetValue(EndProperty);
    public static void SetEnd(BindableObject view, YogaValue value) => view.SetValue(EndProperty, value);

    /// <summary>The child's width; points or a percentage of the container. <c>auto</c> falls back to WidthRequest, then content.</summary>
    public static YogaValue GetNodeWidth(BindableObject view) => (YogaValue)view.GetValue(NodeWidthProperty);
    public static void SetNodeWidth(BindableObject view, YogaValue value) => view.SetValue(NodeWidthProperty, value);
    public static YogaValue GetNodeHeight(BindableObject view) => (YogaValue)view.GetValue(NodeHeightProperty);
    public static void SetNodeHeight(BindableObject view, YogaValue value) => view.SetValue(NodeHeightProperty, value);
    public static YogaValue GetMinWidth(BindableObject view) => (YogaValue)view.GetValue(MinWidthProperty);
    public static void SetMinWidth(BindableObject view, YogaValue value) => view.SetValue(MinWidthProperty, value);
    public static YogaValue GetMinHeight(BindableObject view) => (YogaValue)view.GetValue(MinHeightProperty);
    public static void SetMinHeight(BindableObject view, YogaValue value) => view.SetValue(MinHeightProperty, value);
    public static YogaValue GetMaxWidth(BindableObject view) => (YogaValue)view.GetValue(MaxWidthProperty);
    public static void SetMaxWidth(BindableObject view, YogaValue value) => view.SetValue(MaxWidthProperty, value);
    public static YogaValue GetMaxHeight(BindableObject view) => (YogaValue)view.GetValue(MaxHeightProperty);
    public static void SetMaxHeight(BindableObject view, YogaValue value) => view.SetValue(MaxHeightProperty, value);
}

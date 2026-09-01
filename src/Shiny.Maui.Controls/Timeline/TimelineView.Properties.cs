using System.Collections;

namespace Shiny.Maui.Controls;

public partial class TimelineView
{
    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource),
        typeof(IEnumerable),
        typeof(TimelineView),
        propertyChanged: (b, o, n) => ((TimelineView)b).OnItemsSourceChanged(o as IEnumerable, n as IEnumerable));

    public static readonly BindableProperty ItemTemplateProperty = BindableProperty.Create(
        nameof(ItemTemplate),
        typeof(DataTemplate),
        typeof(TimelineView),
        propertyChanged: (b, _, _) => ((TimelineView)b).Rebuild());

    public static readonly BindableProperty ItemTemplateSelectorProperty = BindableProperty.Create(
        nameof(ItemTemplateSelector),
        typeof(DataTemplateSelector),
        typeof(TimelineView),
        propertyChanged: (b, _, _) => ((TimelineView)b).Rebuild());

    public static readonly BindableProperty MarkerTemplateProperty = BindableProperty.Create(
        nameof(MarkerTemplate),
        typeof(DataTemplate),
        typeof(TimelineView),
        propertyChanged: (b, _, _) => ((TimelineView)b).Rebuild());

    public static readonly BindableProperty OppositeTemplateProperty = BindableProperty.Create(
        nameof(OppositeTemplate),
        typeof(DataTemplate),
        typeof(TimelineView),
        propertyChanged: (b, _, _) => ((TimelineView)b).Rebuild());

    /// <summary>
    /// How far along the timeline the active position is. -1, the default, makes every node pending.
    /// </summary>
    /// <remarks>
    /// Nodes before it are <see cref="TimelineNodeState.Complete"/>, the one at it is
    /// <see cref="TimelineNodeState.Current"/>, and everything after is
    /// <see cref="TimelineNodeState.Pending"/> — the same three states the wizard's markers use, so a
    /// timeline and a wizard showing the same progress read the same way.
    /// </remarks>
    public static readonly BindableProperty ActiveIndexProperty = BindableProperty.Create(
        nameof(ActiveIndex),
        typeof(int),
        typeof(TimelineView),
        -1,
        BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((TimelineView)b).RefreshStates());

    /// <summary>
    /// Treats every node as active, whatever <see cref="ActiveIndex"/> says.
    /// </summary>
    /// <remarks>
    /// For a timeline of things that have all already happened — an audit trail, a delivered order,
    /// a changelog — where a trailing "pending" tail would be saying something untrue. Wins over
    /// <see cref="ActiveIndex"/> rather than being merged with it, so switching it on does not need
    /// the index moved to the end and back again to switch it off.
    /// </remarks>
    public static readonly BindableProperty AllActiveProperty = BindableProperty.Create(
        nameof(AllActive),
        typeof(bool),
        typeof(TimelineView),
        false,
        propertyChanged: (b, _, _) => ((TimelineView)b).RefreshStates());

    public static readonly BindableProperty MarkerSizeProperty = BindableProperty.Create(
        nameof(MarkerSize),
        typeof(double),
        typeof(TimelineView),
        14d,
        propertyChanged: (b, _, _) => ((TimelineView)b).Rebuild());

    /// <summary>
    /// How far the marker's top sits below the top of its row.
    /// </summary>
    /// <remarks>
    /// Set so the marker lines up with the first line of text beside it rather than with the top of
    /// the content box. Centring it on the row instead would drift further out of alignment the taller
    /// the content got, which is the normal case here — content beside a timeline is arbitrary.
    /// </remarks>
    public static readonly BindableProperty MarkerOffsetProperty = BindableProperty.Create(
        nameof(MarkerOffset),
        typeof(double),
        typeof(TimelineView),
        4d,
        propertyChanged: (b, _, _) => ((TimelineView)b).Rebuild());

    public static readonly BindableProperty LineThicknessProperty = BindableProperty.Create(
        nameof(LineThickness),
        typeof(double),
        typeof(TimelineView),
        2d,
        propertyChanged: (b, _, _) => ((TimelineView)b).Rebuild());

    public static readonly BindableProperty ActiveColorProperty = BindableProperty.Create(
        nameof(ActiveColor),
        typeof(Color),
        typeof(TimelineView),
        propertyChanged: (b, _, _) => ((TimelineView)b).RefreshStates());

    public static readonly BindableProperty PendingColorProperty = BindableProperty.Create(
        nameof(PendingColor),
        typeof(Color),
        typeof(TimelineView),
        propertyChanged: (b, _, _) => ((TimelineView)b).RefreshStates());

    /// <summary>Gap between one node's content and the next. The rail runs through it unbroken.</summary>
    public static readonly BindableProperty ItemSpacingProperty = BindableProperty.Create(
        nameof(ItemSpacing),
        typeof(double),
        typeof(TimelineView),
        16d,
        propertyChanged: (b, _, _) => ((TimelineView)b).Rebuild());

    /// <summary>Gap between the rail and the content beside it.</summary>
    public static readonly BindableProperty RailSpacingProperty = BindableProperty.Create(
        nameof(RailSpacing),
        typeof(double),
        typeof(TimelineView),
        12d,
        propertyChanged: (b, _, _) => ((TimelineView)b).Rebuild());

    public static readonly BindableProperty RailPositionProperty = BindableProperty.Create(
        nameof(RailPosition),
        typeof(TimelineRailPosition),
        typeof(TimelineView),
        TimelineRailPosition.Left,
        propertyChanged: (b, _, _) => ((TimelineView)b).Rebuild());

    /// <summary>Whether the control scrolls itself. Off when it already sits inside a scroll view.</summary>
    public static readonly BindableProperty IsScrollableProperty = BindableProperty.Create(
        nameof(IsScrollable),
        typeof(bool),
        typeof(TimelineView),
        true,
        propertyChanged: (b, _, _) => ((TimelineView)b).BuildShell());

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)this.GetValue(ItemsSourceProperty);
        set => this.SetValue(ItemsSourceProperty, value);
    }

    /// <summary>The content beside each marker. Binds straight to the item.</summary>
    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)this.GetValue(ItemTemplateProperty);
        set => this.SetValue(ItemTemplateProperty, value);
    }

    public DataTemplateSelector? ItemTemplateSelector
    {
        get => (DataTemplateSelector?)this.GetValue(ItemTemplateSelectorProperty);
        set => this.SetValue(ItemTemplateSelectorProperty, value);
    }

    /// <summary>The marker on the rail. Binds to a <see cref="TimelineNode"/>, not to the item.</summary>
    public DataTemplate? MarkerTemplate
    {
        get => (DataTemplate?)this.GetValue(MarkerTemplateProperty);
        set => this.SetValue(MarkerTemplateProperty, value);
    }

    /// <summary>Optional content on the far side of the rail — a timestamp, a duration. Binds to the item.</summary>
    public DataTemplate? OppositeTemplate
    {
        get => (DataTemplate?)this.GetValue(OppositeTemplateProperty);
        set => this.SetValue(OppositeTemplateProperty, value);
    }

    /// <inheritdoc cref="ActiveIndexProperty"/>
    public int ActiveIndex
    {
        get => (int)this.GetValue(ActiveIndexProperty);
        set => this.SetValue(ActiveIndexProperty, value);
    }

    /// <inheritdoc cref="AllActiveProperty"/>
    public bool AllActive
    {
        get => (bool)this.GetValue(AllActiveProperty);
        set => this.SetValue(AllActiveProperty, value);
    }

    public double MarkerSize
    {
        get => (double)this.GetValue(MarkerSizeProperty);
        set => this.SetValue(MarkerSizeProperty, value);
    }

    /// <inheritdoc cref="MarkerOffsetProperty"/>
    public double MarkerOffset
    {
        get => (double)this.GetValue(MarkerOffsetProperty);
        set => this.SetValue(MarkerOffsetProperty, value);
    }

    public double LineThickness
    {
        get => (double)this.GetValue(LineThicknessProperty);
        set => this.SetValue(LineThicknessProperty, value);
    }

    public Color? ActiveColor
    {
        get => (Color?)this.GetValue(ActiveColorProperty);
        set => this.SetValue(ActiveColorProperty, value);
    }

    public Color? PendingColor
    {
        get => (Color?)this.GetValue(PendingColorProperty);
        set => this.SetValue(PendingColorProperty, value);
    }

    public double ItemSpacing
    {
        get => (double)this.GetValue(ItemSpacingProperty);
        set => this.SetValue(ItemSpacingProperty, value);
    }

    public double RailSpacing
    {
        get => (double)this.GetValue(RailSpacingProperty);
        set => this.SetValue(RailSpacingProperty, value);
    }

    public TimelineRailPosition RailPosition
    {
        get => (TimelineRailPosition)this.GetValue(RailPositionProperty);
        set => this.SetValue(RailPositionProperty, value);
    }

    /// <inheritdoc cref="IsScrollableProperty"/>
    public bool IsScrollable
    {
        get => (bool)this.GetValue(IsScrollableProperty);
        set => this.SetValue(IsScrollableProperty, value);
    }

    /// <summary>Raised when a node is tapped, with the item and its index.</summary>
    public event EventHandler<Collections.CollectionItemEventArgs>? NodeTapped;
}

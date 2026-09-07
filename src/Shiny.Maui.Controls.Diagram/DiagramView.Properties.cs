using System.Collections;
using Shiny.Controls.Diagramming;

namespace Shiny.Maui.Controls.Diagram;

public partial class DiagramView
{
    /// <summary>Backing store for <see cref="Nodes"/>.</summary>
    public static readonly BindableProperty NodesProperty = BindableProperty.Create(
        nameof(Nodes),
        typeof(IEnumerable),
        typeof(DiagramView),
        propertyChanged: (b, o, n) => ((DiagramView)b).OnSourceReplaced(o, n)
    );

    /// <summary>Backing store for <see cref="Connections"/>.</summary>
    public static readonly BindableProperty ConnectionsProperty = BindableProperty.Create(
        nameof(Connections),
        typeof(IEnumerable),
        typeof(DiagramView),
        propertyChanged: (b, o, n) => ((DiagramView)b).OnSourceReplaced(o, n)
    );

    /// <summary>Backing store for <see cref="Layout"/>.</summary>
    public static readonly BindableProperty LayoutKindProperty = BindableProperty.Create(
        nameof(LayoutKind),
        typeof(DiagramLayoutKind),
        typeof(DiagramView),
        DiagramLayoutKind.Tree,
        propertyChanged: OnLayoutChanged
    );

    /// <summary>Backing store for <see cref="Direction"/>.</summary>
    public static readonly BindableProperty DirectionProperty = BindableProperty.Create(
        nameof(Direction),
        typeof(DiagramDirection),
        typeof(DiagramView),
        DiagramDirection.TopToBottom,
        propertyChanged: OnLayoutChanged
    );

    /// <summary>Backing store for <see cref="TreeStyle"/>.</summary>
    public static readonly BindableProperty TreeStyleProperty = BindableProperty.Create(
        nameof(TreeStyle),
        typeof(DiagramTreeStyle),
        typeof(DiagramView),
        DiagramTreeStyle.Normal,
        propertyChanged: OnLayoutChanged
    );

    /// <summary>Backing store for <see cref="NodeSpacing"/>.</summary>
    public static readonly BindableProperty NodeSpacingProperty = BindableProperty.Create(
        nameof(NodeSpacing),
        typeof(double),
        typeof(DiagramView),
        32d,
        propertyChanged: OnLayoutChanged
    );

    /// <summary>Backing store for <see cref="LevelSpacing"/>.</summary>
    public static readonly BindableProperty LevelSpacingProperty = BindableProperty.Create(
        nameof(LevelSpacing),
        typeof(double),
        typeof(DiagramView),
        56d,
        propertyChanged: OnLayoutChanged
    );

    /// <summary>Backing store for <see cref="NodeWidth"/>.</summary>
    public static readonly BindableProperty NodeWidthProperty = BindableProperty.Create(
        nameof(NodeWidth),
        typeof(double),
        typeof(DiagramView),
        140d,
        propertyChanged: OnLayoutChanged
    );

    /// <summary>Backing store for <see cref="NodeHeight"/>.</summary>
    public static readonly BindableProperty NodeHeightProperty = BindableProperty.Create(
        nameof(NodeHeight),
        typeof(double),
        typeof(DiagramView),
        56d,
        propertyChanged: OnLayoutChanged
    );

    /// <summary>Backing store for <see cref="Router"/>.</summary>
    public static readonly BindableProperty RouterProperty = BindableProperty.Create(
        nameof(Router),
        typeof(DiagramConnectionRouter),
        typeof(DiagramView),
        DiagramConnectionRouter.Orthogonal,
        propertyChanged: OnLayoutChanged
    );

    /// <summary>Backing store for <see cref="Zoom"/>.</summary>
    public static readonly BindableProperty ZoomProperty = BindableProperty.Create(
        nameof(Zoom),
        typeof(double),
        typeof(DiagramView),
        1d,
        BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((DiagramView)b).Repaint(),
        coerceValue: CoerceZoom
    );

    /// <summary>Backing store for <see cref="MinZoom"/>.</summary>
    public static readonly BindableProperty MinZoomProperty = BindableProperty.Create(
        nameof(MinZoom),
        typeof(double),
        typeof(DiagramView),
        0.2d,
        propertyChanged: OnZoomRangeChanged
    );

    /// <summary>Backing store for <see cref="MaxZoom"/>.</summary>
    public static readonly BindableProperty MaxZoomProperty = BindableProperty.Create(
        nameof(MaxZoom),
        typeof(double),
        typeof(DiagramView),
        4d,
        propertyChanged: OnZoomRangeChanged
    );

    /// <summary>Backing store for <see cref="AllowPan"/>.</summary>
    public static readonly BindableProperty AllowPanProperty = BindableProperty.Create(
        nameof(AllowPan),
        typeof(bool),
        typeof(DiagramView),
        true
    );

    /// <summary>Backing store for <see cref="AllowZoom"/>.</summary>
    public static readonly BindableProperty AllowZoomProperty = BindableProperty.Create(
        nameof(AllowZoom),
        typeof(bool),
        typeof(DiagramView),
        true,
        propertyChanged: (b, _, _) => ((DiagramView)b).UpdateGestures()
    );

    /// <summary>Backing store for <see cref="AllowSelection"/>.</summary>
    public static readonly BindableProperty AllowSelectionProperty = BindableProperty.Create(
        nameof(AllowSelection),
        typeof(bool),
        typeof(DiagramView),
        true
    );

    /// <summary>Backing store for <see cref="AllowMultiSelect"/>.</summary>
    public static readonly BindableProperty AllowMultiSelectProperty = BindableProperty.Create(
        nameof(AllowMultiSelect),
        typeof(bool),
        typeof(DiagramView),
        true
    );

    /// <summary>Backing store for <see cref="AllowNodeDrag"/>.</summary>
    public static readonly BindableProperty AllowNodeDragProperty = BindableProperty.Create(
        nameof(AllowNodeDrag),
        typeof(bool),
        typeof(DiagramView),
        false
    );

    /// <summary>Backing store for <see cref="AllowConnectionEdit"/>.</summary>
    public static readonly BindableProperty AllowConnectionEditProperty = BindableProperty.Create(
        nameof(AllowConnectionEdit),
        typeof(bool),
        typeof(DiagramView),
        false,
        propertyChanged: (b, _, _) => ((DiagramView)b).Repaint()
    );

    /// <summary>Backing store for <see cref="ShowGrid"/>.</summary>
    public static readonly BindableProperty ShowGridProperty = BindableProperty.Create(
        nameof(ShowGrid),
        typeof(bool),
        typeof(DiagramView),
        false,
        propertyChanged: (b, _, _) => ((DiagramView)b).Repaint()
    );

    /// <summary>Backing store for <see cref="GridSize"/>.</summary>
    public static readonly BindableProperty GridSizeProperty = BindableProperty.Create(
        nameof(GridSize),
        typeof(double),
        typeof(DiagramView),
        20d,
        propertyChanged: (b, _, _) => ((DiagramView)b).Repaint()
    );

    /// <summary>Backing store for <see cref="SnapToGrid"/>.</summary>
    public static readonly BindableProperty SnapToGridProperty = BindableProperty.Create(
        nameof(SnapToGrid),
        typeof(bool),
        typeof(DiagramView),
        false
    );

    /// <summary>Backing store for <see cref="NodeFontSize"/>.</summary>
    public static readonly BindableProperty NodeFontSizeProperty = BindableProperty.Create(
        nameof(NodeFontSize),
        typeof(double),
        typeof(DiagramView),
        13d,
        propertyChanged: OnLayoutChanged
    );

    /// <summary>Backing store for <see cref="ConnectionFontSize"/>.</summary>
    public static readonly BindableProperty ConnectionFontSizeProperty = BindableProperty.Create(
        nameof(ConnectionFontSize),
        typeof(double),
        typeof(DiagramView),
        11d,
        propertyChanged: (b, _, _) => ((DiagramView)b).Repaint()
    );

    /// <summary>Backing store for <see cref="NodeTemplate"/>.</summary>
    public static readonly BindableProperty NodeTemplateProperty = BindableProperty.Create(
        nameof(NodeTemplate),
        typeof(DataTemplate),
        typeof(DiagramView),
        propertyChanged: (b, _, _) => ((DiagramView)b).RebuildTemplateLayer()
    );

    /// <summary>Backing store for <see cref="NodeTemplateSelector"/>.</summary>
    public static readonly BindableProperty NodeTemplateSelectorProperty = BindableProperty.Create(
        nameof(NodeTemplateSelector),
        typeof(DataTemplateSelector),
        typeof(DiagramView),
        propertyChanged: (b, _, _) => ((DiagramView)b).RebuildTemplateLayer()
    );

    /// <summary>Backing store for <see cref="SelectedNode"/>.</summary>
    public static readonly BindableProperty SelectedNodeProperty = BindableProperty.Create(
        nameof(SelectedNode),
        typeof(DiagramNode),
        typeof(DiagramView),
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: (b, _, n) => ((DiagramView)b).OnSelectedNodeSet(n as DiagramNode)
    );

    /// <summary>The shapes to draw. An observable collection redraws as it changes.</summary>
    public IEnumerable? Nodes
    {
        get => (IEnumerable?)this.GetValue(NodesProperty);
        set => this.SetValue(NodesProperty, value);
    }

    /// <summary>The lines between them.</summary>
    public IEnumerable? Connections
    {
        get => (IEnumerable?)this.GetValue(ConnectionsProperty);
        set => this.SetValue(ConnectionsProperty, value);
    }

    /// <summary>Which auto-layout arranges the graph.</summary>
    public DiagramLayoutKind LayoutKind
    {
        get => (DiagramLayoutKind)this.GetValue(LayoutKindProperty);
        set => this.SetValue(LayoutKindProperty, value);
    }

    /// <summary>Which way a hierarchy or layered layout grows.</summary>
    public DiagramDirection Direction
    {
        get => (DiagramDirection)this.GetValue(DirectionProperty);
        set => this.SetValue(DirectionProperty, value);
    }

    /// <summary>Whether tree children spread across the axis or stack and indent.</summary>
    public DiagramTreeStyle TreeStyle
    {
        get => (DiagramTreeStyle)this.GetValue(TreeStyleProperty);
        set => this.SetValue(TreeStyleProperty, value);
    }

    /// <summary>Gap between two nodes side by side within a level.</summary>
    public double NodeSpacing
    {
        get => (double)this.GetValue(NodeSpacingProperty);
        set => this.SetValue(NodeSpacingProperty, value);
    }

    /// <summary>Gap between one level of the hierarchy and the next.</summary>
    public double LevelSpacing
    {
        get => (double)this.GetValue(LevelSpacingProperty);
        set => this.SetValue(LevelSpacingProperty, value);
    }

    /// <summary>Default node width, for nodes that do not set their own.</summary>
    public double NodeWidth
    {
        get => (double)this.GetValue(NodeWidthProperty);
        set => this.SetValue(NodeWidthProperty, value);
    }

    /// <summary>Default node height, for nodes that do not set their own.</summary>
    public double NodeHeight
    {
        get => (double)this.GetValue(NodeHeightProperty);
        set => this.SetValue(NodeHeightProperty, value);
    }

    /// <summary>The router used by connections that do not name one.</summary>
    public DiagramConnectionRouter Router
    {
        get => (DiagramConnectionRouter)this.GetValue(RouterProperty);
        set => this.SetValue(RouterProperty, value);
    }

    /// <summary>Current zoom, clamped to <see cref="MinZoom"/> and <see cref="MaxZoom"/>.</summary>
    public double Zoom
    {
        get => (double)this.GetValue(ZoomProperty);
        set => this.SetValue(ZoomProperty, value);
    }

    /// <summary>The smallest zoom a gesture can reach.</summary>
    public double MinZoom
    {
        get => (double)this.GetValue(MinZoomProperty);
        set => this.SetValue(MinZoomProperty, value);
    }

    /// <summary>The largest zoom a gesture can reach.</summary>
    public double MaxZoom
    {
        get => (double)this.GetValue(MaxZoomProperty);
        set => this.SetValue(MaxZoomProperty, value);
    }

    /// <summary>Whether dragging the background pans the surface.</summary>
    public bool AllowPan
    {
        get => (bool)this.GetValue(AllowPanProperty);
        set => this.SetValue(AllowPanProperty, value);
    }

    /// <summary>Whether pinching zooms.</summary>
    public bool AllowZoom
    {
        get => (bool)this.GetValue(AllowZoomProperty);
        set => this.SetValue(AllowZoomProperty, value);
    }

    /// <summary>Whether tapping selects nodes and connections.</summary>
    public bool AllowSelection
    {
        get => (bool)this.GetValue(AllowSelectionProperty);
        set => this.SetValue(AllowSelectionProperty, value);
    }

    /// <summary>Whether dragging the background draws a selection marquee.</summary>
    /// <remarks>
    /// A finger has no shift key, so on touch the marquee is what a drag on the background does when
    /// <see cref="AllowPan"/> is off - the two gestures are the same one and cannot both be on.
    /// </remarks>
    public bool AllowMultiSelect
    {
        get => (bool)this.GetValue(AllowMultiSelectProperty);
        set => this.SetValue(AllowMultiSelectProperty, value);
    }

    /// <summary>Whether nodes can be dragged. Dragging a node pins it.</summary>
    public bool AllowNodeDrag
    {
        get => (bool)this.GetValue(AllowNodeDragProperty);
        set => this.SetValue(AllowNodeDragProperty, value);
    }

    /// <summary>Whether connections can be drawn from a selected node's connector handles.</summary>
    public bool AllowConnectionEdit
    {
        get => (bool)this.GetValue(AllowConnectionEditProperty);
        set => this.SetValue(AllowConnectionEditProperty, value);
    }

    /// <summary>Whether a grid is drawn behind the diagram.</summary>
    public bool ShowGrid
    {
        get => (bool)this.GetValue(ShowGridProperty);
        set => this.SetValue(ShowGridProperty, value);
    }

    /// <summary>The grid's pitch, and the step a dragged node snaps to when <see cref="SnapToGrid"/> is set.</summary>
    public double GridSize
    {
        get => (double)this.GetValue(GridSizeProperty);
        set => this.SetValue(GridSizeProperty, value);
    }

    /// <summary>Whether a dragged node snaps to <see cref="GridSize"/>.</summary>
    public bool SnapToGrid
    {
        get => (bool)this.GetValue(SnapToGridProperty);
        set => this.SetValue(SnapToGridProperty, value);
    }

    /// <summary>Label size inside a node.</summary>
    public double NodeFontSize
    {
        get => (double)this.GetValue(NodeFontSizeProperty);
        set => this.SetValue(NodeFontSizeProperty, value);
    }

    /// <summary>Label size on a connection.</summary>
    public double ConnectionFontSize
    {
        get => (double)this.GetValue(ConnectionFontSizeProperty);
        set => this.SetValue(ConnectionFontSizeProperty, value);
    }

    /// <summary>
    /// Replaces the drawn shape with a real view, bound to the <see cref="DiagramNode"/>.
    /// </summary>
    /// <remarks>
    /// One native view per node instead of one path, so a diagram of a few dozen nodes can hold
    /// buttons and images and a diagram of a few hundred should not. Unset - the default - every node
    /// is drawn.
    /// </remarks>
    public DataTemplate? NodeTemplate
    {
        get => (DataTemplate?)this.GetValue(NodeTemplateProperty);
        set => this.SetValue(NodeTemplateProperty, value);
    }

    /// <summary>Picks a template per node.</summary>
    public DataTemplateSelector? NodeTemplateSelector
    {
        get => (DataTemplateSelector?)this.GetValue(NodeTemplateSelectorProperty);
        set => this.SetValue(NodeTemplateSelectorProperty, value);
    }

    /// <summary>
    /// The primary selected node, two-way bindable.
    /// </summary>
    /// <remarks>
    /// The full selection - which can be several nodes and several connections - is
    /// <see cref="Selection"/>. This is the single-value form a view model usually wants to bind a
    /// detail pane to, and it holds the last node added to the selection.
    /// </remarks>
    public DiagramNode? SelectedNode
    {
        get => (DiagramNode?)this.GetValue(SelectedNodeProperty);
        set => this.SetValue(SelectedNodeProperty, value);
    }

    static void OnLayoutChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((DiagramView)bindable).Rebuild();

    static void OnZoomRangeChanged(BindableObject bindable, object oldValue, object newValue)
    {
        // Re-coerce: a range that moved may make the current zoom invalid, and coerceValue only runs
        // when the coerced property is itself set.
        var view = (DiagramView)bindable;
        view.Zoom = Math.Clamp(view.Zoom, view.MinZoom, view.MaxZoom);
    }

    static object CoerceZoom(BindableObject bindable, object value)
    {
        var view = (DiagramView)bindable;
        var zoom = (double)value;

        // A zero or negative zoom divides by zero in every coordinate conversion, and the floor is
        // deliberately below MinZoom's default so a consumer can legitimately allow a very wide view.
        return Math.Clamp(zoom <= 0 ? 1 : zoom, Math.Max(view.MinZoom, 0.01), Math.Max(view.MaxZoom, 0.01));
    }
}

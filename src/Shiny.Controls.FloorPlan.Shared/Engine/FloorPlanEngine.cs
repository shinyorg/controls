using SkiaSharp;

namespace Shiny.Controls.FloorPlan;

/// <summary>
/// The floor plan itself: the document, the camera, the renderers, the tools and the pointer
/// handling. Both host controls are a surface and an event pump around one of these.
/// </summary>
/// <remarks>
/// Everything that decides what a plan looks like or how it responds lives here rather than in the
/// hosts, which is what keeps a plan drawn in a MAUI page and the same plan drawn in a Blazor
/// component from being two different controls that happen to share a name.
/// </remarks>
public class FloorPlanEngine
{
    readonly Dictionary<Type, IFloorPlanElementRenderer> renderers = new();

    FloorPlanDocument document = new();
    FloorPlanTheme theme = FloorPlanTheme.Light;

    PanTool? viewPanTool;
    SKPoint viewPressPos;
    bool viewIsDragging;

    /// <summary>How far a press has to travel before a tap becomes a pan, in screen pixels.</summary>
    public const float DragThreshold = 5f;

    public FloorPlanEngine()
    {
        this.HitTester = new FloorPlanHitTester(this.renderers);

        this.RegisterRenderer(new RoomRenderer());
        this.RegisterRenderer(new WallRenderer());
        this.RegisterRenderer(new DoorRenderer());
        this.RegisterRenderer(new CubicleRenderer());
        this.RegisterRenderer(new OutletRenderer());
        this.RegisterRenderer(new FurnitureRenderer());

        // Resolved against whatever document is current, not the one present at construction.
        this.RegisterRenderer(new CustomElementRenderer(id =>
            this.document.CustomShapes.FirstOrDefault(x => x.Id == id)));

        this.State.SelectionChanged += x => this.SelectionChanged?.Invoke(x);
        this.State.HoverChanged += x => this.HoverChanged?.Invoke(x);
        this.State.ActiveTool = new SelectTool();
    }

    public FloorPlanEditorState State { get; } = new();

    public FloorPlanCamera Camera { get; } = new();

    public FloorPlanHitTester HitTester { get; }

    /// <summary>The plan being drawn. Setting it clears the selection and repaints.</summary>
    public FloorPlanDocument Document
    {
        get => this.document;
        set
        {
            this.document = value;
            this.State.ClearSelection();
            this.State.HoveredElement = null;
            this.Invalidate();
        }
    }

    /// <summary>The colours everything is drawn with. Hosts default this from the app's theme.</summary>
    public FloorPlanTheme Theme
    {
        get => this.theme;
        set
        {
            this.theme = value;
            this.Invalidate();
        }
    }

    /// <summary>Raised when the engine needs the surface repainted.</summary>
    public event Action? InvalidateRequested;

    public event Action<IReadOnlyList<FloorPlanElement>>? SelectionChanged;

    public event Action<FloorPlanElement?>? HoverChanged;

    /// <summary>
    /// Raised when an element is tapped in <see cref="FloorPlanEditorMode.View"/> - the seating-chart
    /// interaction. Not raised in Edit mode, where a click is a selection.
    /// </summary>
    public event Action<FloorPlanElement>? ElementTapped;

    /// <summary>Raised after a tool adds an element to the document.</summary>
    public event Action<FloorPlanElement>? ElementAdded;

    /// <summary>Raised after elements are removed from the document.</summary>
    public event Action<IReadOnlyList<FloorPlanElement>>? ElementsRemoved;

    /// <summary>
    /// Replaces the renderer for an element type, or adds one for a type of your own.
    /// </summary>
    /// <remarks>
    /// Keyed on <see cref="IFloorPlanElementRenderer.ElementType"/> exactly. A renderer registered for
    /// a base type will not pick up its subclasses - register each one you want drawn.
    /// </remarks>
    public void RegisterRenderer(IFloorPlanElementRenderer renderer) =>
        this.renderers[renderer.ElementType] = renderer;

    public void SetTool(IFloorPlanTool tool)
    {
        this.State.ActiveTool?.OnDeactivated();
        this.State.ActiveTool = tool;
        tool.OnActivated(this.CreateToolContext());
        this.Invalidate();
    }


    // ---------------------------------------------------------------------------------------------
    // Painting
    // ---------------------------------------------------------------------------------------------

    /// <summary>Paints the whole plan. <paramref name="width"/> and <paramref name="height"/> are in
    /// device pixels, matching the coordinates the pointer methods are given.</summary>
    public void Render(SKCanvas canvas, float width, float height)
    {
        canvas.Clear(this.Theme.Background.ToSKColor());
        canvas.Save();
        canvas.SetMatrix(this.Camera.GetTransformMatrix());

        if (this.State.IsGridVisible)
            FloorPlanGridRenderer.RenderGrid(canvas, this.document, this.Camera, this.Theme);

        // Always, even with the grid off: the boundary is where the document ends rather than part of
        // the grid, and without it a plan floats on a sheet with no edges.
        FloorPlanGridRenderer.RenderBoundary(canvas, this.document, this.Camera, this.Theme);

        foreach (var element in this.document.Elements.Where(x => x.IsVisible).OrderBy(x => x.ZIndex))
            this.DrawElement(canvas, element);

        if (this.State.Mode == FloorPlanEditorMode.Edit)
        {
            foreach (var selected in this.State.SelectedElements)
                FloorPlanSelectionRenderer.RenderHandles(canvas, selected, this.Camera, this.Theme);
        }

        this.State.ActiveTool?.Render(canvas, new FloorPlanToolRenderContext
        {
            Camera = this.Camera,
            Theme = this.Theme,
            DrawElement = this.DrawGhost
        });

        canvas.Restore();
    }

    void DrawElement(SKCanvas canvas, FloorPlanElement element)
    {
        if (!this.renderers.TryGetValue(element.GetType(), out var renderer))
            return;

        renderer.Render(canvas, element, new FloorPlanRenderContext
        {
            Camera = this.Camera,
            State = this.State,
            Theme = this.Theme,
            GridSize = this.document.GridSize,
            IsSelected = this.State.IsSelected(element),
            IsHovered = ReferenceEquals(this.State.HoveredElement, element)
        });
    }

    /// <summary>
    /// Draws an element as a tool's preview - never selected, never hovered, however the real one
    /// happens to be. A ghost that inherited the selection state would come out in selection blue.
    /// </summary>
    void DrawGhost(SKCanvas canvas, FloorPlanElement element)
    {
        if (!this.renderers.TryGetValue(element.GetType(), out var renderer))
            return;

        renderer.Render(canvas, element, new FloorPlanRenderContext
        {
            Camera = this.Camera,
            State = this.State,
            Theme = this.Theme,
            GridSize = this.document.GridSize,
            IsSelected = false,
            IsHovered = false
        });
    }


    // ---------------------------------------------------------------------------------------------
    // Pointer
    // ---------------------------------------------------------------------------------------------

    public void OnPointerPressed(
        float screenX,
        float screenY,
        FloorPlanPointerButton button = FloorPlanPointerButton.Left,
        bool shift = false,
        bool ctrl = false
    )
    {
        // The middle button pans from any tool, which is what every drawing application does and what
        // makes a long drawing session bearable.
        if (button == FloorPlanPointerButton.Middle)
        {
            var panTool = new PanTool();
            panTool.OnPointerPressed(this.CreateToolContext(), this.CreateArgs(screenX, screenY, button, shift, ctrl));
            this.State.ActiveTool = panTool;
            return;
        }

        if (this.State.Mode == FloorPlanEditorMode.View)
        {
            // A press in View mode is not yet either thing. It becomes a pan once it has moved far
            // enough and a tap if it never does - decided in Moved and Released.
            this.viewPressPos = new SKPoint(screenX, screenY);
            this.viewIsDragging = false;
            this.viewPanTool = new PanTool();
            this.viewPanTool.OnPointerPressed(this.CreateToolContext(), this.CreateArgs(screenX, screenY, button, shift, ctrl));
            return;
        }

        this.State.ActiveTool?.OnPointerPressed(this.CreateToolContext(), this.CreateArgs(screenX, screenY, button, shift, ctrl));
    }

    public void OnPointerMoved(float screenX, float screenY, bool shift = false, bool ctrl = false)
    {
        if (this.State.Mode == FloorPlanEditorMode.View && this.viewPanTool is not null)
        {
            var dx = screenX - this.viewPressPos.X;
            var dy = screenY - this.viewPressPos.Y;

            if (!this.viewIsDragging && MathF.Sqrt(dx * dx + dy * dy) > DragThreshold)
                this.viewIsDragging = true;

            if (this.viewIsDragging)
            {
                this.viewPanTool.OnPointerMoved(
                    this.CreateToolContext(),
                    this.CreateArgs(screenX, screenY, FloorPlanPointerButton.Left, shift, ctrl)
                );
            }

            return;
        }

        this.State.ActiveTool?.OnPointerMoved(
            this.CreateToolContext(),
            this.CreateArgs(screenX, screenY, FloorPlanPointerButton.Left, shift, ctrl)
        );
    }

    public void OnPointerReleased(
        float screenX,
        float screenY,
        FloorPlanPointerButton button = FloorPlanPointerButton.Left,
        bool shift = false,
        bool ctrl = false
    )
    {
        if (this.State.Mode == FloorPlanEditorMode.View && this.viewPanTool is not null)
        {
            this.viewPanTool.OnPointerReleased(this.CreateToolContext(), this.CreateArgs(screenX, screenY, button, shift, ctrl));
            this.viewPanTool = null;

            if (!this.viewIsDragging)
            {
                var world = this.Camera.ScreenToWorld(screenX, screenY);
                var hit = this.HitTester.HitTest(this.document, world.X, world.Y, this.State, this.Camera.Zoom);
                if (hit is { IsHandle: false })
                    this.ElementTapped?.Invoke(hit.Element);
            }

            this.viewIsDragging = false;
            return;
        }

        this.State.ActiveTool?.OnPointerReleased(this.CreateToolContext(), this.CreateArgs(screenX, screenY, button, shift, ctrl));
    }

    /// <summary>A wheel notch or a pinch, anchored at the pointer.</summary>
    public void OnScroll(float screenX, float screenY, float delta)
    {
        this.Camera.ZoomAt(screenX, screenY, delta * 0.1f);
        this.Invalidate();
    }


    // ---------------------------------------------------------------------------------------------
    // Commands
    // ---------------------------------------------------------------------------------------------

    public void DeleteSelected()
    {
        var removed = this.State.SelectedElements.Where(x => !x.IsLocked).ToList();
        if (removed.Count == 0)
            return;

        foreach (var el in removed)
            this.document.Elements.Remove(el);

        this.State.ClearSelection();
        this.ElementsRemoved?.Invoke(removed);
        this.Invalidate();
    }

    /// <summary>Adds an element, selects it and repaints - the programmatic equivalent of a place tool.</summary>
    public void AddElement(FloorPlanElement element)
    {
        this.document.Elements.Add(element);
        this.State.Select(element);
        this.ElementAdded?.Invoke(element);
        this.Invalidate();
    }

    public void BringToFront()
    {
        if (this.State.SelectedElements.Count == 0 || this.document.Elements.Count == 0)
            return;

        var maxZ = this.document.Elements.Max(x => x.ZIndex);
        foreach (var el in this.State.SelectedElements)
            el.ZIndex = ++maxZ;

        this.Invalidate();
    }

    public void SendToBack()
    {
        if (this.State.SelectedElements.Count == 0 || this.document.Elements.Count == 0)
            return;

        var minZ = this.document.Elements.Min(x => x.ZIndex);
        foreach (var el in this.State.SelectedElements)
            el.ZIndex = --minZ;

        this.Invalidate();
    }

    /// <summary>Fits the whole document into a viewport of the given size, in device pixels.</summary>
    public void ZoomToFit(float viewWidth, float viewHeight)
    {
        if (viewWidth <= 0 || viewHeight <= 0 || this.document.Width <= 0 || this.document.Height <= 0)
            return;

        var zoom = MathF.Min(viewWidth / this.document.Width, viewHeight / this.document.Height) * 0.9f;
        this.Camera.Zoom = Math.Clamp(zoom, this.Camera.MinZoom, this.Camera.MaxZoom);
        this.Camera.OffsetX = (viewWidth - this.document.Width * this.Camera.Zoom) / 2;
        this.Camera.OffsetY = (viewHeight - this.document.Height * this.Camera.Zoom) / 2;
        this.Invalidate();
    }

    /// <summary>
    /// Zooms in about the middle of a viewport of the given size.
    /// </summary>
    /// <remarks>
    /// The size is required rather than optional: zooming about (0, 0) instead - which is what a
    /// button wired straight to <see cref="OnScroll"/> does - walks the plan off the top-left corner
    /// of the surface, one click at a time.
    /// </remarks>
    public void ZoomIn(float viewWidth, float viewHeight, float step = 2f) =>
        this.OnScroll(viewWidth / 2, viewHeight / 2, step);

    public void ZoomOut(float viewWidth, float viewHeight, float step = 2f) =>
        this.OnScroll(viewWidth / 2, viewHeight / 2, -step);

    /// <summary>Centres the camera on an element without changing the zoom.</summary>
    public void ScrollTo(FloorPlanElement element, float viewWidth, float viewHeight)
    {
        var center = element.GetBounds().Center;
        this.Camera.OffsetX = viewWidth / 2 - center.X * this.Camera.Zoom;
        this.Camera.OffsetY = viewHeight / 2 - center.Y * this.Camera.Zoom;
        this.Invalidate();
    }

    public void Invalidate() => this.InvalidateRequested?.Invoke();

    FloorPlanToolContext CreateToolContext() => new()
    {
        Document = this.document,
        Camera = this.Camera,
        State = this.State,
        HitTester = this.HitTester,
        Invalidate = this.Invalidate,
        Added = x => this.ElementAdded?.Invoke(x)
    };

    FloorPlanPointerEventArgs CreateArgs(float sx, float sy, FloorPlanPointerButton button, bool shift, bool ctrl) => new()
    {
        ScreenPosition = new SKPoint(sx, sy),
        WorldPosition = this.Camera.ScreenToWorld(sx, sy),
        Button = button,
        Shift = shift,
        Ctrl = ctrl
    };
}

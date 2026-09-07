using Shiny.Controls.FloorPlan;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using PlanTool = Shiny.Controls.FloorPlan.IFloorPlanTool;

namespace Shiny.Maui.Controls.FloorPlan;

/// <summary>
/// An interactive floor plan: rooms, walls, doors, cubicles, outlets, furniture and custom stencils
/// on a pan and zoom canvas.
/// </summary>
/// <remarks>
/// <para>
/// Two modes. <see cref="FloorPlanEditorMode.Edit"/> gives the active tool the pointer and puts
/// resize handles on the selection; <see cref="FloorPlanEditorMode.View"/> turns the whole surface
/// into a pan surface where a tap raises <see cref="ElementTapped"/> - which is the seating-chart
/// case, and the reason a viewer needs no toolbar at all.
/// </para>
/// <para>
/// Everything that decides how the plan behaves lives on <see cref="Engine"/>, in
/// <c>Shiny.Controls.FloorPlan</c>, shared verbatim with the Blazor control. The properties here are
/// the XAML-facing surface over it, not a second implementation.
/// </para>
/// </remarks>
public class FloorPlanView : SKCanvasView
{
    bool suppressSelectionWrite;
    bool fitPending;

    public FloorPlanView()
    {
        this.EnableTouchEvents = true;
        this.IgnorePixelScaling = false;

        this.Engine.Theme = FloorPlanScheme.Default;
        this.FollowAppTheme(static view =>
        {
            if (view.Theme is null)
                view.Engine.Theme = FloorPlanScheme.Default;
        });

        this.Engine.InvalidateRequested += () => MainThread.BeginInvokeOnMainThread(this.InvalidateSurface);
        this.Engine.ElementTapped += element => this.ElementTapped?.Invoke(this, new FloorPlanElementEventArgs(element));
        this.Engine.ElementAdded += element => this.ElementAdded?.Invoke(this, new FloorPlanElementEventArgs(element));
        this.Engine.ElementsRemoved += elements => this.ElementsRemoved?.Invoke(this, new FloorPlanElementsEventArgs(elements));
        this.Engine.SelectionChanged += this.OnEngineSelectionChanged;

        // Desktop hover. Touch platforms never raise it, which is why hover is a highlight and never
        // the only way to find out what something is.
        var pointer = new PointerGestureRecognizer();
        pointer.PointerMoved += this.OnPointerMoved;
        pointer.PointerExited += this.OnPointerExited;
        this.GestureRecognizers.Add(pointer);
    }

    /// <summary>The engine behind the view. Register renderers and drive it directly through this.</summary>
    public FloorPlanEngine Engine { get; } = new();


    // ---------------------------------------------------------------------------------------------
    // Bindable properties
    // ---------------------------------------------------------------------------------------------

    public static readonly BindableProperty DocumentProperty = BindableProperty.Create(
        nameof(Document),
        typeof(FloorPlanDocument),
        typeof(FloorPlanView),
        propertyChanged: (bindable, _, value) =>
        {
            if (value is FloorPlanDocument doc)
                ((FloorPlanView)bindable).Engine.Document = doc;
        }
    );

    public static readonly BindableProperty ModeProperty = BindableProperty.Create(
        nameof(Mode),
        typeof(FloorPlanEditorMode),
        typeof(FloorPlanView),
        FloorPlanEditorMode.Edit,
        propertyChanged: (bindable, _, value) =>
        {
            var view = (FloorPlanView)bindable;
            view.Engine.State.Mode = (FloorPlanEditorMode)value;

            // Handles are Edit-mode chrome. Leaving them on screen after a switch to View is the sort
            // of thing that only shows up once someone flips modes on a live plan.
            view.Engine.State.ClearSelection();
            view.Engine.Invalidate();
        }
    );

    public static readonly BindableProperty ShowGridProperty = BindableProperty.Create(
        nameof(ShowGrid),
        typeof(bool),
        typeof(FloorPlanView),
        true,
        propertyChanged: (bindable, _, value) =>
        {
            var view = (FloorPlanView)bindable;
            view.Engine.State.IsGridVisible = (bool)value;
            view.Engine.Invalidate();
        }
    );

    public static readonly BindableProperty SnapToGridProperty = BindableProperty.Create(
        nameof(SnapToGrid),
        typeof(bool),
        typeof(FloorPlanView),
        true,
        propertyChanged: (bindable, _, value) => ((FloorPlanView)bindable).Engine.State.SnapToGrid = (bool)value
    );

    public static readonly BindableProperty ActiveToolProperty = BindableProperty.Create(
        nameof(ActiveTool),
        typeof(PlanTool),
        typeof(FloorPlanView),
        propertyChanged: (bindable, _, value) =>
        {
            if (value is PlanTool tool)
                ((FloorPlanView)bindable).Engine.SetTool(tool);
        }
    );

    public static readonly BindableProperty ThemeProperty = BindableProperty.Create(
        nameof(Theme),
        typeof(FloorPlanTheme),
        typeof(FloorPlanView),
        propertyChanged: (bindable, _, value) =>
        {
            var view = (FloorPlanView)bindable;
            view.Engine.Theme = (FloorPlanTheme?)value ?? FloorPlanScheme.Default;
        }
    );

    /// <remarks>
    /// Two-way by default: the point of it is to tell a view model what the user picked. Writing it
    /// selects that element in the plan, so a master list beside the plan drives it both ways.
    /// </remarks>
    public static readonly BindableProperty SelectedElementProperty = BindableProperty.Create(
        nameof(SelectedElement),
        typeof(FloorPlanElement),
        typeof(FloorPlanView),
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: OnSelectedElementChanged
    );

    public FloorPlanDocument? Document
    {
        get => (FloorPlanDocument?)this.GetValue(DocumentProperty);
        set => this.SetValue(DocumentProperty, value);
    }

    public FloorPlanEditorMode Mode
    {
        get => (FloorPlanEditorMode)this.GetValue(ModeProperty);
        set => this.SetValue(ModeProperty, value);
    }

    public bool ShowGrid
    {
        get => (bool)this.GetValue(ShowGridProperty);
        set => this.SetValue(ShowGridProperty, value);
    }

    public bool SnapToGrid
    {
        get => (bool)this.GetValue(SnapToGridProperty);
        set => this.SetValue(SnapToGridProperty, value);
    }

    /// <summary>The tool the pointer drives in Edit mode. Defaults to <see cref="SelectTool"/>.</summary>
    public PlanTool? ActiveTool
    {
        get => (PlanTool?)this.GetValue(ActiveToolProperty);
        set => this.SetValue(ActiveToolProperty, value);
    }

    /// <summary>Explicit colours. Leave it null to follow the app's theme, which is the default.</summary>
    public FloorPlanTheme? Theme
    {
        get => (FloorPlanTheme?)this.GetValue(ThemeProperty);
        set => this.SetValue(ThemeProperty, value);
    }

    /// <summary>The single selected element, or null when nothing or several things are selected.</summary>
    public FloorPlanElement? SelectedElement
    {
        get => (FloorPlanElement?)this.GetValue(SelectedElementProperty);
        set => this.SetValue(SelectedElementProperty, value);
    }


    // ---------------------------------------------------------------------------------------------
    // Events
    // ---------------------------------------------------------------------------------------------

    /// <summary>A tap on an element in View mode.</summary>
    public event EventHandler<FloorPlanElementEventArgs>? ElementTapped;

    public event EventHandler<FloorPlanSelectionChangedEventArgs>? SelectionChanged;

    public event EventHandler<FloorPlanElementEventArgs>? ElementAdded;

    public event EventHandler<FloorPlanElementsEventArgs>? ElementsRemoved;


    // ---------------------------------------------------------------------------------------------
    // Commands
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Fits the whole plan in the view.
    /// </summary>
    /// <remarks>
    /// Safe to call before the view has ever painted - which is the usual case, since the natural
    /// place to call it is the page's constructor or its first <c>SizeChanged</c>. The surface has no
    /// size until the first paint, and fitting to a zero-sized one is a no-op the engine deliberately
    /// ignores, so the request is held and applied on the next frame instead.
    /// </remarks>
    public void ZoomToFit()
    {
        if (this.CanvasSize is { Width: > 0, Height: > 0 })
            this.Engine.ZoomToFit(this.CanvasSize.Width, this.CanvasSize.Height);
        else
            this.fitPending = true;
    }

    /// <summary>Zooms in about the middle of the view.</summary>
    public void ZoomIn() => this.Engine.ZoomIn(this.CanvasSize.Width, this.CanvasSize.Height);

    /// <summary>Zooms out about the middle of the view.</summary>
    public void ZoomOut() => this.Engine.ZoomOut(this.CanvasSize.Width, this.CanvasSize.Height);

    /// <summary>Centres the view on an element without changing the zoom.</summary>
    public void ScrollTo(FloorPlanElement element) =>
        this.Engine.ScrollTo(element, this.CanvasSize.Width, this.CanvasSize.Height);

    public void DeleteSelected() => this.Engine.DeleteSelected();

    public void BringToFront() => this.Engine.BringToFront();

    public void SendToBack() => this.Engine.SendToBack();


    // ---------------------------------------------------------------------------------------------
    // Surface
    // ---------------------------------------------------------------------------------------------

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);

        // A fit asked for before the surface existed is applied here, where its real size is finally
        // known - and before the render, so the first frame is already fitted rather than showing a
        // thumbnail in the corner for one frame.
        if (this.fitPending && e.Info is { Width: > 0, Height: > 0 })
        {
            this.fitPending = false;
            this.Engine.ZoomToFit(e.Info.Width, e.Info.Height);
        }

        this.Engine.Render(e.Surface.Canvas, e.Info.Width, e.Info.Height);
    }

    protected override void OnTouch(SKTouchEventArgs e)
    {
        base.OnTouch(e);

        var x = e.Location.X;
        var y = e.Location.Y;
        var button = e.MouseButton switch
        {
            SKMouseButton.Middle => FloorPlanPointerButton.Middle,
            SKMouseButton.Right => FloorPlanPointerButton.Right,
            _ => FloorPlanPointerButton.Left
        };

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                this.Engine.OnPointerPressed(x, y, button);
                e.Handled = true;
                break;

            case SKTouchAction.Moved:
                this.Engine.OnPointerMoved(x, y);
                e.Handled = true;
                break;

            case SKTouchAction.Released:
                this.Engine.OnPointerReleased(x, y, button);
                e.Handled = true;
                break;

            case SKTouchAction.Cancelled:
                // A cancelled touch has to end the gesture too. Without this the tool keeps the drag
                // it thinks is still in hand, and the next tap continues a move from minutes ago.
                this.Engine.OnPointerReleased(x, y, button);
                e.Handled = true;
                break;

            case SKTouchAction.WheelChanged:
                this.Engine.OnScroll(x, y, e.WheelDelta > 0 ? 1 : -1);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Hover, from the desktop pointer.
    /// </summary>
    /// <remarks>
    /// The gesture reports layout units while the canvas works in device pixels, so the position has
    /// to be scaled by the display density - the same conversion <c>SKTouchEventArgs</c> has already
    /// done for the touch path. Skip it and hover highlights an element some distance from the
    /// pointer, further out the further right you go.
    /// </remarks>
    void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.GetPosition(this) is not { } position)
            return;

        var scale = (float)DeviceDisplay.Current.MainDisplayInfo.Density;
        this.Engine.OnPointerMoved((float)position.X * scale, (float)position.Y * scale);
    }

    void OnPointerExited(object? sender, PointerEventArgs e)
    {
        this.Engine.State.HoveredElement = null;
        this.Engine.Invalidate();
    }

    void OnEngineSelectionChanged(IReadOnlyList<FloorPlanElement> selection)
    {
        this.suppressSelectionWrite = true;
        try
        {
            this.SelectedElement = this.Engine.State.SelectedElement;
        }
        finally
        {
            this.suppressSelectionWrite = false;
        }

        this.SelectionChanged?.Invoke(this, new FloorPlanSelectionChangedEventArgs(selection));
    }

    /// <remarks>
    /// Guarded both ways. The engine writes this property when the user picks something, and without
    /// the flag that write comes straight back in here and re-selects what is already selected -
    /// which is harmless until a binding is attached, at which point it is a loop.
    /// </remarks>
    static void OnSelectedElementChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (FloorPlanView)bindable;
        if (view.suppressSelectionWrite)
            return;

        if (newValue is FloorPlanElement element)
            view.Engine.State.Select(element);
        else
            view.Engine.State.ClearSelection();

        view.Engine.Invalidate();
    }
}

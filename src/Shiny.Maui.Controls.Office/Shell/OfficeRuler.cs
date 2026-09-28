using Shiny.Controls.Office.Shell;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Office;

/// <summary>Which way a ruler runs.</summary>
public enum OfficeRulerOrientation
{
    Horizontal,
    Vertical
}


/// <summary>
/// Word's ruler: inch or centimetre ticks, shaded margins, and — horizontally — the first-line, hanging,
/// left and right indent markers and the tab stops, all draggable.
/// </summary>
/// <remarks>
/// <para>
/// A drawing over <see cref="OfficeRulerModel"/>, which owns every position and every drag rule; this
/// class converts pointer positions and paints. Vertically it is the same model with the page height and
/// the top/bottom margins in <see cref="PageWidth"/>/<see cref="LeftMargin"/>/<see cref="RightMargin"/>,
/// and no indents.
/// </para>
/// <para>
/// A pan gesture reports only deltas, so where a drag started is taken from the last pointer position a
/// <see cref="PointerGestureRecognizer"/> saw (desktop heads), falling back to the tap position. The
/// <see cref="BeginDrag"/> / <see cref="DragTo"/> / <see cref="EndDrag"/> / <see cref="TapAt"/> seams
/// drive the same code from a test.
/// </para>
/// </remarks>
public class OfficeRuler : GraphicsView, IDrawable
{
    const double Thickness = 22;

    readonly OfficeRulerModel model = new();
    OfficeRulerHit drag;
    Point lastPointer;
    double panStartX;
    double panStartY;
    Color ink = Colors.DimGray;
    Color shade = Color.FromArgb("#E3E3E3");
    Color paper = Colors.White;
    Color accent = Color.FromArgb("#185ABD");

    static BindableProperty Redraw(string name, Type type, object? value, BindingMode mode = BindingMode.OneWay)
        => BindableProperty.Create(name, type, typeof(OfficeRuler), value, mode, propertyChanged: (b, _, _) => ((OfficeRuler)b).Sync());

    public static readonly BindableProperty OrientationProperty = Redraw(nameof(Orientation), typeof(OfficeRulerOrientation), OfficeRulerOrientation.Horizontal);
    public static readonly BindableProperty PageWidthProperty = Redraw(nameof(PageWidth), typeof(double), 612d);
    public static readonly BindableProperty LeftMarginProperty = Redraw(nameof(LeftMargin), typeof(double), 72d, BindingMode.TwoWay);
    public static readonly BindableProperty RightMarginProperty = Redraw(nameof(RightMargin), typeof(double), 72d, BindingMode.TwoWay);
    public static readonly BindableProperty IndentsProperty = Redraw(nameof(Indents), typeof(OfficeIndents), default(OfficeIndents), BindingMode.TwoWay);
    public static readonly BindableProperty TabStopsProperty = Redraw(nameof(TabStops), typeof(IReadOnlyList<OfficeTabStop>), null, BindingMode.TwoWay);
    public static readonly BindableProperty UnitProperty = Redraw(nameof(Unit), typeof(OfficeRulerUnit), OfficeRulerUnit.Inches);
    public static readonly BindableProperty ZoomProperty = Redraw(nameof(Zoom), typeof(double), 1d);
    public static readonly BindableProperty PixelsPerPointProperty = Redraw(nameof(PixelsPerPoint), typeof(double), 96d / 72d);
    public static readonly BindableProperty PageOffsetProperty = Redraw(nameof(PageOffset), typeof(double), 0d);
    public static readonly BindableProperty ShowIndentsProperty = Redraw(nameof(ShowIndents), typeof(bool), true);
    public static readonly BindableProperty TabAlignmentProperty = Redraw(nameof(TabAlignment), typeof(OfficeTabAlignment), OfficeTabAlignment.Left);
    public static readonly BindableProperty AllowMarginDragProperty = Redraw(nameof(AllowMarginDrag), typeof(bool), true);
    public static readonly BindableProperty AccentColorProperty = Redraw(nameof(AccentColor), typeof(Color), null);


    public OfficeRuler()
    {
        this.Drawable = this;
        this.HeightRequest = Thickness;
        this.BackgroundColor = Colors.Transparent;

        // The colours are resolved values, not bindings, so an appearance flip has to re-read them;
        // otherwise a ruler built in dark mode stayed a dark band on a light window. Dispatched so the
        // theme manager has swapped the tokens first.
        this.FollowAppTheme(static ruler => ruler.Dispatcher.Dispatch(() =>
        {
            ruler.ResolveColors();
            ruler.Invalidate();
        }));

        var pointer = new PointerGestureRecognizer();
        pointer.PointerMoved += (_, e) => this.RememberPointer(e.GetPosition(this));
        pointer.PointerPressed += (_, e) => this.RememberPointer(e.GetPosition(this));
        this.GestureRecognizers.Add(pointer);

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += this.OnPan;
        this.GestureRecognizers.Add(pan);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, e) =>
        {
            if (e.GetPosition(this) is { } p)
                this.TapAt(this.Along(p), this.Across(p) / Thickness);
        };
        this.GestureRecognizers.Add(tap);

        this.Sync();
    }


    public OfficeRulerOrientation Orientation { get => (OfficeRulerOrientation)this.GetValue(OrientationProperty); set => this.SetValue(OrientationProperty, value); }

    /// <summary>The page's length along the ruler, in points. Page height for a vertical ruler.</summary>
    public double PageWidth { get => (double)this.GetValue(PageWidthProperty); set => this.SetValue(PageWidthProperty, value); }

    /// <summary>The leading margin in points (top margin vertically). Two-way.</summary>
    public double LeftMargin { get => (double)this.GetValue(LeftMarginProperty); set => this.SetValue(LeftMarginProperty, value); }

    /// <summary>The trailing margin in points (bottom margin vertically). Two-way.</summary>
    public double RightMargin { get => (double)this.GetValue(RightMarginProperty); set => this.SetValue(RightMarginProperty, value); }

    /// <summary>The caret paragraph's indents. Two-way; the markers write it.</summary>
    public OfficeIndents Indents { get => (OfficeIndents)this.GetValue(IndentsProperty); set => this.SetValue(IndentsProperty, value); }

    /// <summary>The caret paragraph's tab stops. Two-way.</summary>
    public IReadOnlyList<OfficeTabStop>? TabStops { get => (IReadOnlyList<OfficeTabStop>?)this.GetValue(TabStopsProperty); set => this.SetValue(TabStopsProperty, value); }

    public OfficeRulerUnit Unit { get => (OfficeRulerUnit)this.GetValue(UnitProperty); set => this.SetValue(UnitProperty, value); }

    /// <summary>The editor's zoom factor.</summary>
    public double Zoom { get => (double)this.GetValue(ZoomProperty); set => this.SetValue(ZoomProperty, value); }

    public double PixelsPerPoint { get => (double)this.GetValue(PixelsPerPointProperty); set => this.SetValue(PixelsPerPointProperty, value); }

    /// <summary>Where the page's leading edge sits in the ruler, in pixels — the editor's centring less its scroll.</summary>
    public double PageOffset { get => (double)this.GetValue(PageOffsetProperty); set => this.SetValue(PageOffsetProperty, value); }

    /// <summary>Draws and allows dragging the indent markers. False for a vertical ruler.</summary>
    public bool ShowIndents { get => (bool)this.GetValue(ShowIndentsProperty); set => this.SetValue(ShowIndentsProperty, value); }

    /// <summary>The alignment a tab added by tapping gets.</summary>
    public OfficeTabAlignment TabAlignment { get => (OfficeTabAlignment)this.GetValue(TabAlignmentProperty); set => this.SetValue(TabAlignmentProperty, value); }

    public bool AllowMarginDrag { get => (bool)this.GetValue(AllowMarginDragProperty); set => this.SetValue(AllowMarginDragProperty, value); }

    /// <summary>The markers' colour. Null uses Word blue.</summary>
    public Color? AccentColor { get => (Color?)this.GetValue(AccentColorProperty); set => this.SetValue(AccentColorProperty, value); }

    /// <summary>The geometry the ruler draws from — read-only use; set the properties instead.</summary>
    public OfficeRulerModel Model => this.model;

    public event EventHandler<OfficeIndents>? IndentsChanged;
    public event EventHandler<IReadOnlyList<OfficeTabStop>>? TabStopsChanged;
    public event EventHandler<(double Left, double Right)>? MarginsChanged;


    // ---------------------------------------------------------------------------------------------
    // Interaction (public seams)
    // ---------------------------------------------------------------------------------------------

    /// <summary>Starts a drag at a point along the ruler; <paramref name="y"/> is 0–1 across it.</summary>
    public OfficeRulerMarker BeginDrag(double x, double y)
    {
        this.Sync();
        this.drag = this.model.HitTest(x, y);

        if (!this.ShowIndents && this.drag.Marker is OfficeRulerMarker.FirstLineIndent or OfficeRulerMarker.HangingIndent or OfficeRulerMarker.LeftIndent or OfficeRulerMarker.RightIndent or OfficeRulerMarker.TabStop)
            this.drag = new(OfficeRulerMarker.None);

        if (!this.AllowMarginDrag && this.drag.Marker is OfficeRulerMarker.LeftMargin or OfficeRulerMarker.RightMargin)
            this.drag = new(OfficeRulerMarker.None);

        return this.drag.Marker;
    }


    public void DragTo(double x, double y)
    {
        switch (this.drag.Marker)
        {
            case OfficeRulerMarker.FirstLineIndent or OfficeRulerMarker.HangingIndent or OfficeRulerMarker.LeftIndent or OfficeRulerMarker.RightIndent:
                var indents = this.model.DragIndent(this.drag.Marker, x);
                if (indents != this.Indents)
                {
                    this.Indents = indents;
                    this.IndentsChanged?.Invoke(this, indents);
                }
                break;

            case OfficeRulerMarker.TabStop:
                var before = this.model.TabStops.Count;
                var tabs = this.model.DragTab(this.drag.TabIndex, x, y);
                this.TabStops = tabs;
                this.TabStopsChanged?.Invoke(this, tabs);

                // Follow the dragged stop through the re-sort, or stop dragging once it was dropped off.
                var landed = this.model.TabAt(x);
                var index = landed is { } t && tabs.Count == before
                    ? tabs.ToList().FindIndex(s => Math.Abs(s.Position - t.Position) < 0.01)
                    : -1;
                this.drag = index >= 0 ? new(OfficeRulerMarker.TabStop, index) : new(OfficeRulerMarker.None);
                this.Sync();
                break;

            case OfficeRulerMarker.LeftMargin or OfficeRulerMarker.RightMargin:
                var (left, right) = this.model.DragMargin(this.drag.Marker, x);
                if (left != this.LeftMargin || right != this.RightMargin)
                {
                    this.LeftMargin = left;
                    this.RightMargin = right;
                    this.MarginsChanged?.Invoke(this, (left, right));
                }
                break;
        }
    }


    public void EndDrag() => this.drag = new(OfficeRulerMarker.None);


    /// <summary>A tap along the ruler: on the text span away from any marker it adds a tab stop.</summary>
    public void TapAt(double x, double y = 0.5)
    {
        if (!this.ShowIndents)
            return;

        this.Sync();
        if (this.model.HitTest(x, y).Marker != OfficeRulerMarker.None)
            return;

        if (this.model.TabAt(x, this.TabAlignment) is { } tab)
        {
            var tabs = this.model.WithTab(tab);
            this.TabStops = tabs;
            this.TabStopsChanged?.Invoke(this, tabs);
        }
    }


    void RememberPointer(Point? p)
    {
        if (p is { } point)
            this.lastPointer = point;
    }


    double Along(Point p) => this.Orientation == OfficeRulerOrientation.Horizontal ? p.X : p.Y;

    double Across(Point p) => this.Orientation == OfficeRulerOrientation.Horizontal ? p.Y : p.X;


    void OnPan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                this.panStartX = this.Along(this.lastPointer);
                this.panStartY = this.Across(this.lastPointer) / Thickness;
                this.BeginDrag(this.panStartX, this.panStartY);
                break;

            case GestureStatus.Running:
                var dx = this.Orientation == OfficeRulerOrientation.Horizontal ? e.TotalX : e.TotalY;
                var dy = this.Orientation == OfficeRulerOrientation.Horizontal ? e.TotalY : e.TotalX;
                this.DragTo(this.panStartX + dx, this.panStartY + (dy / Thickness));
                break;

            default:
                this.EndDrag();
                break;
        }
    }


    // ---------------------------------------------------------------------------------------------
    // Drawing
    // ---------------------------------------------------------------------------------------------

    void Sync()
    {
        this.model.PageWidth = this.PageWidth;
        this.model.LeftMargin = this.LeftMargin;
        this.model.RightMargin = this.RightMargin;
        this.model.Indents = this.Indents;
        this.model.TabStops = this.TabStops ?? [];
        this.model.Unit = this.Unit;
        this.model.Zoom = this.Zoom;
        this.model.PixelsPerPoint = this.PixelsPerPoint;
        this.model.PageOffset = this.PageOffset;

        if (this.Orientation == OfficeRulerOrientation.Vertical)
        {
            this.WidthRequest = Thickness;
            this.HeightRequest = -1;
        }
        else
        {
            this.HeightRequest = Thickness;
            this.WidthRequest = -1;
        }

        this.Invalidate();
    }


    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        this.ResolveColors();
        this.Invalidate();
    }


    void ResolveColors()
    {
        if (Application.Current?.Resources is not { } resources)
            return;

        Color? Get(string key) => resources.TryGetValue(key, out var v) && v is Color c ? c : null;

        this.ink = Get(ShinyThemeKeys.Color.OnSurfaceVariant) ?? this.ink;
        this.shade = Get(ShinyThemeKeys.Color.SurfaceContainerHighest) ?? this.shade;
        this.paper = Get(ShinyThemeKeys.Color.Surface) ?? this.paper;
    }


    void IDrawable.Draw(ICanvas canvas, RectF rect)
    {
        var vertical = this.Orientation == OfficeRulerOrientation.Vertical;
        var accent = this.AccentColor ?? this.accent;
        canvas.SaveState();

        if (vertical)
        {
            // Draw the horizontal layout rotated a quarter turn, so one routine serves both.
            canvas.Translate(rect.Width, 0);
            canvas.Rotate(90);
            rect = new RectF(0, 0, rect.Height, rect.Width);
        }

        var h = rect.Height;
        var band = new RectF(0, h * 0.2f, rect.Width, h * 0.6f);

        // Margins shaded, text span on paper.
        var (ls, le) = this.model.LeftMarginSpan;
        var (rs, re) = this.model.RightMarginSpan;
        canvas.FillColor = this.shade;
        canvas.FillRectangle((float)ls, band.Y, (float)(le - ls), band.Height);
        canvas.FillRectangle((float)rs, band.Y, (float)(re - rs), band.Height);
        var (ts, te) = this.model.TextSpan;
        canvas.FillColor = this.paper;
        canvas.FillRectangle((float)ts, band.Y, (float)(te - ts), band.Height);

        // Ticks and numbers.
        canvas.StrokeColor = this.ink;
        canvas.StrokeSize = 1;
        canvas.FontColor = this.ink;
        canvas.FontSize = 9;
        var mid = band.Y + (band.Height / 2);

        foreach (var tick in this.model.Ticks())
        {
            var x = (float)tick.X;
            if (tick.Label is { } label)
            {
                canvas.DrawString(label, x - 10, band.Y, 20, band.Height, HorizontalAlignment.Center, VerticalAlignment.Center);
                continue;
            }

            var len = (float)(band.Height * 0.5 * tick.Height);
            canvas.DrawLine(x, mid - (len / 2), x, mid + (len / 2));
        }

        if (this.ShowIndents && !vertical)
            this.DrawMarkers(canvas, h, accent);

        canvas.RestoreState();
    }


    void DrawMarkers(ICanvas canvas, float h, Color accent)
    {
        canvas.FillColor = this.paper;
        canvas.StrokeColor = accent;
        canvas.StrokeSize = 1;

        // First line: a downward triangle hanging from the top.
        var first = (float)this.model.MarkerX(OfficeRulerMarker.FirstLineIndent);
        Triangle(canvas, first, 0, h * 0.4f, down: true);

        // Hanging: an upward triangle, with the left-indent box underneath.
        var left = (float)this.model.MarkerX(OfficeRulerMarker.LeftIndent);
        Triangle(canvas, left, h * 0.45f, h * 0.75f, down: false);
        canvas.FillRectangle(left - 4, h * 0.75f, 8, h * 0.22f);
        canvas.DrawRectangle(left - 4, h * 0.75f, 8, h * 0.22f);

        // Right indent: an upward triangle.
        var right = (float)this.model.MarkerX(OfficeRulerMarker.RightIndent);
        Triangle(canvas, right, h * 0.45f, h * 0.8f, down: false);

        // Tab stops.
        canvas.StrokeColor = this.ink;
        canvas.StrokeSize = 1.5f;
        var y0 = h * 0.55f;
        var y1 = h * 0.8f;
        foreach (var tab in this.model.TabStops)
        {
            var x = (float)this.model.MarginToView(tab.Position);
            switch (tab.Alignment)
            {
                case OfficeTabAlignment.Left:
                    canvas.DrawLine(x, y0, x, y1);
                    canvas.DrawLine(x, y1, x + 5, y1);
                    break;
                case OfficeTabAlignment.Right:
                    canvas.DrawLine(x, y0, x, y1);
                    canvas.DrawLine(x - 5, y1, x, y1);
                    break;
                case OfficeTabAlignment.Center:
                    canvas.DrawLine(x, y0, x, y1);
                    canvas.DrawLine(x - 4, y1, x + 4, y1);
                    break;
                default:
                    canvas.DrawLine(x, y0, x, y1);
                    canvas.DrawLine(x - 4, y1, x + 4, y1);
                    canvas.FillColor = this.ink;
                    canvas.FillCircle(x + 3, y0 + 3, 1);
                    break;
            }
        }
    }


    static void Triangle(ICanvas canvas, float x, float top, float bottom, bool down)
    {
        var path = new PathF();
        if (down)
        {
            path.MoveTo(x - 4.5f, top);
            path.LineTo(x + 4.5f, top);
            path.LineTo(x, bottom);
        }
        else
        {
            path.MoveTo(x, top);
            path.LineTo(x + 4.5f, bottom);
            path.LineTo(x - 4.5f, bottom);
        }

        path.Close();
        canvas.FillPath(path);
        canvas.DrawPath(path);
    }
}

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Shiny.Controls.Office.Shell;

namespace Shiny.Blazor.Controls.Office;

/// <summary>The direction a ruler runs.</summary>
public enum OfficeRulerOrientation
{
    Horizontal,
    Vertical
}


/// <summary>
/// Word's ruler: margin shading, ticks in inches or centimetres, draggable first-line / hanging /
/// left / right indent markers, tab stops (click to add, drag off to remove) and the tab-kind selector.
/// </summary>
/// <remarks>
/// A pure component: it holds no document. The editor feeds it the page width, margins, indents, zoom
/// and where the page sits in the view (<see cref="PageOffset"/>), and hears back through the two-way
/// callbacks when the user drags something. The geometry is <see cref="OfficeRulerModel"/>, shared with
/// the MAUI ruler.
/// </remarks>
public partial class OfficeRuler : ComponentBase, IAsyncDisposable
{
    [Inject] IJSRuntime Js { get; set; } = default!;

    ElementReference root;
    IJSObjectReference? module;
    bool disposed;

    // The drag in progress, and where the ruler was on screen when it began.
    OfficeRulerHit drag;
    double originLeft;
    double originTop;
    double originHeight;
    double pressAlong;
    bool moved;
    OfficeIndents? pendingIndents;
    IReadOnlyList<OfficeTabStop>? pendingTabs;
    (double Left, double Right)? pendingMargins;
    IReadOnlyList<OfficeTabStop> dragOriginTabs = [];

    [Parameter] public OfficeRulerOrientation Orientation { get; set; }

    /// <summary>The page's width (height, for a vertical ruler) in points. Default 612 — US Letter.</summary>
    [Parameter] public double PageWidth { get; set; } = 612;

    /// <summary>The left (or top) margin in points. Two-way bindable.</summary>
    [Parameter] public double LeftMargin { get; set; } = 72;

    [Parameter] public EventCallback<double> LeftMarginChanged { get; set; }

    /// <summary>The right (or bottom) margin in points. Two-way bindable.</summary>
    [Parameter] public double RightMargin { get; set; } = 72;

    [Parameter] public EventCallback<double> RightMarginChanged { get; set; }

    /// <summary>The paragraph's indents. Two-way bindable.</summary>
    [Parameter] public OfficeIndents Indents { get; set; }

    [Parameter] public EventCallback<OfficeIndents> IndentsChanged { get; set; }

    /// <summary>The paragraph's tab stops. Two-way bindable.</summary>
    [Parameter] public IReadOnlyList<OfficeTabStop>? TabStops { get; set; }

    [Parameter] public EventCallback<IReadOnlyList<OfficeTabStop>> TabStopsChanged { get; set; }

    [Parameter] public OfficeRulerUnit Unit { get; set; } = OfficeRulerUnit.Inches;

    /// <summary>The editor's zoom factor.</summary>
    [Parameter] public double Zoom { get; set; } = 1;

    /// <summary>CSS pixels per point at 100%. Default 96/72.</summary>
    [Parameter] public double PixelsPerPoint { get; set; } = 96d / 72d;

    /// <summary>Where the page's leading edge is, in pixels from the ruler's own leading edge.</summary>
    [Parameter] public double PageOffset { get; set; }

    /// <summary>Draws and allows dragging the indent markers and tab stops. Always off for a vertical ruler.</summary>
    [Parameter] public bool ShowIndents { get; set; } = true;

    /// <summary>Lets the margin boundaries be dragged.</summary>
    [Parameter] public bool AllowMarginDrag { get; set; } = true;

    /// <summary>Draws the tab-kind selector at the leading end.</summary>
    [Parameter] public bool ShowTabSelector { get; set; } = true;

    /// <summary>The kind of tab a click on the ruler adds. Two-way bindable; the selector cycles it.</summary>
    [Parameter] public OfficeTabAlignment TabAlignment { get; set; }

    [Parameter] public EventCallback<OfficeTabAlignment> TabAlignmentChanged { get; set; }

    /// <summary>Report every step of a drag rather than only where it ends. Off by default — relaying out a long document per pointer move is not cheap.</summary>
    [Parameter] public bool LiveUpdate { get; set; }

    [Parameter] public string? CssClass { get; set; }


    internal bool IsVertical => this.Orientation == OfficeRulerOrientation.Vertical;

    /// <summary>Ruler thickness in pixels.</summary>
    internal double Thickness => 22;

    /// <summary>The geometry for what is showing now, pending drag values included.</summary>
    internal OfficeRulerModel Model
    {
        get
        {
            var margins = this.pendingMargins ?? (this.LeftMargin, this.RightMargin);
            return new OfficeRulerModel
            {
                PageWidth = this.PageWidth,
                LeftMargin = margins.Left,
                RightMargin = margins.Right,
                Indents = this.pendingIndents ?? this.Indents,
                TabStops = this.pendingTabs ?? this.TabStops ?? [],
                Unit = this.Unit,
                Zoom = this.Zoom,
                PixelsPerPoint = this.PixelsPerPoint,
                PageOffset = this.PageOffset
            };
        }
    }

    string ValueText
    {
        get
        {
            var model = this.Model;
            return this.IsVertical
                ? $"Top margin {model.FormatLength(model.LeftMargin)}, bottom margin {model.FormatLength(model.RightMargin)}"
                : $"Left indent {model.FormatLength(model.Indents.Left)}, first line {model.FormatLength(model.Indents.FirstLine)}, right indent {model.FormatLength(model.Indents.Right)}";
        }
    }

    string RootCss
        => string.Join(' ', new[] { "office-ruler", this.IsVertical ? "is-vertical" : "is-horizontal", this.drag.Marker != OfficeRulerMarker.None ? "is-dragging" : null, this.CssClass }.Where(x => x is not null));

    string RootStyle => this.IsVertical ? $"width:{this.Thickness}px" : $"height:{this.Thickness}px";


    // ---- pointer -------------------------------------------------------------------------------

    async Task OnPointerDownAsync(PointerEventArgs e)
    {
        if (e.Button != 0)
            return;

        var box = await this.BoundsAsync();
        this.originLeft = box[0];
        this.originTop = box[1];
        this.originHeight = this.IsVertical ? box[2] : box[3];

        var (along, across) = this.Local(e);
        this.pressAlong = along;
        this.moved = false;
        this.Begin(along, across);

        if (this.drag.Marker != OfficeRulerMarker.None && this.module is not null)
            await this.module.InvokeVoidAsync("capture", this.root, e.PointerId);
    }

    async Task OnPointerMoveAsync(PointerEventArgs e)
    {
        if (this.drag.Marker == OfficeRulerMarker.None)
            return;

        var (along, across) = this.Local(e);
        if (!this.moved && Math.Abs(along - this.pressAlong) < 2)
            return;

        this.moved = true;
        this.Move(along, across);

        if (this.LiveUpdate)
            await this.FlushAsync(keepDragging: true);
    }

    async Task OnPointerUpAsync(PointerEventArgs e)
    {
        if (this.drag.Marker == OfficeRulerMarker.None)
        {
            // A click on the bare text column adds a tab stop there, the way Word does.
            if (e.Type == "pointerup" && this.ShowIndents && !this.IsVertical)
            {
                var (along, _) = this.Local(e);
                await this.AddTabAtAsync(along);
            }

            return;
        }

        if (this.module is not null)
            await this.module.InvokeVoidAsync("release", this.root, e.PointerId);

        await this.FlushAsync(keepDragging: false);
    }


    /// <summary>Starts a drag at a point in the ruler's own coordinates. The seam tests drive the ruler through.</summary>
    internal void Begin(double along, double across)
    {
        var fraction = this.originHeight > 0 ? across / this.originHeight : across / this.Thickness;
        var hit = this.Model.HitTest(along, fraction);

        hit = hit.Marker switch
        {
            OfficeRulerMarker.FirstLineIndent or OfficeRulerMarker.HangingIndent or OfficeRulerMarker.LeftIndent
                or OfficeRulerMarker.RightIndent or OfficeRulerMarker.TabStop when !this.ShowIndents || this.IsVertical
                => new OfficeRulerHit(OfficeRulerMarker.None),
            OfficeRulerMarker.LeftMargin or OfficeRulerMarker.RightMargin when !this.AllowMarginDrag
                => new OfficeRulerHit(OfficeRulerMarker.None),
            _ => hit
        };

        this.drag = hit;
        this.dragOriginTabs = this.TabStops ?? [];
    }

    /// <summary>Moves the drag in progress to a point.</summary>
    internal void Move(double along, double across)
    {
        var model = this.Model;
        var fraction = this.originHeight > 0 ? across / this.originHeight : across / this.Thickness;

        switch (this.drag.Marker)
        {
            case OfficeRulerMarker.FirstLineIndent or OfficeRulerMarker.HangingIndent
                or OfficeRulerMarker.LeftIndent or OfficeRulerMarker.RightIndent:
                this.pendingIndents = model.DragIndent(this.drag.Marker, along);
                break;

            case OfficeRulerMarker.TabStop:
                // Always from the tabs as they were when the drag began: DragTab removes and re-inserts,
                // so working from the pending list would lose track of which tab is being dragged.
                model.TabStops = this.dragOriginTabs;
                this.pendingTabs = model.DragTab(this.drag.TabIndex, along, fraction);
                break;

            case OfficeRulerMarker.LeftMargin or OfficeRulerMarker.RightMargin:
                this.pendingMargins = model.DragMargin(this.drag.Marker, along);
                break;
        }
    }

    /// <summary>Ends the drag and reports what changed.</summary>
    internal async Task FlushAsync(bool keepDragging)
    {
        var indents = this.pendingIndents;
        var tabs = this.pendingTabs;
        var margins = this.pendingMargins;

        if (!keepDragging)
        {
            this.drag = new OfficeRulerHit(OfficeRulerMarker.None);
            this.pendingIndents = null;
            this.pendingTabs = null;
            this.pendingMargins = null;
        }

        if (indents is { } i && i != this.Indents)
        {
            this.Indents = i;
            await this.IndentsChanged.InvokeAsync(i);
        }

        if (tabs is not null)
        {
            this.TabStops = tabs;
            await this.TabStopsChanged.InvokeAsync(tabs);
        }

        if (margins is { } m)
        {
            if (Math.Abs(m.Left - this.LeftMargin) > 0.001)
            {
                this.LeftMargin = m.Left;
                await this.LeftMarginChanged.InvokeAsync(m.Left);
            }

            if (Math.Abs(m.Right - this.RightMargin) > 0.001)
            {
                this.RightMargin = m.Right;
                await this.RightMarginChanged.InvokeAsync(m.Right);
            }
        }
    }

    /// <summary>Adds a tab of the selected kind at a point, when it lands in the text column.</summary>
    internal async Task AddTabAtAsync(double along)
    {
        var model = this.Model;
        if (model.TabAt(along, this.TabAlignment) is not { } tab)
            return;

        var tabs = model.WithTab(tab);
        this.TabStops = tabs;
        await this.TabStopsChanged.InvokeAsync(tabs);
    }

    async Task CycleTabAlignmentAsync()
    {
        this.TabAlignment = OfficeRulerModel.NextAlignment(this.TabAlignment);
        await this.TabAlignmentChanged.InvokeAsync(this.TabAlignment);
    }

    (double Along, double Across) Local(PointerEventArgs e)
    {
        var x = e.ClientX - this.originLeft;
        var y = e.ClientY - this.originTop;
        return this.IsVertical ? (y, x) : (x, y);
    }

    async Task<double[]> BoundsAsync()
    {
        try
        {
            this.module ??= await this.Js.InvokeAsync<IJSObjectReference>("import", "./_content/Shiny.Blazor.Controls.Office/officeShell.js");
            if (this.disposed)
                return [0, 0, 0, 0];

            return await this.module.InvokeAsync<double[]>("bounds", this.root);
        }
        catch (JSException)
        {
            return [0, 0, 0, 0];
        }
        catch (JSDisconnectedException)
        {
            return [0, 0, 0, 0];
        }
    }

    public async ValueTask DisposeAsync()
    {
        this.disposed = true;
        if (this.module is null)
            return;

        try
        {
            await this.module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
    }
}

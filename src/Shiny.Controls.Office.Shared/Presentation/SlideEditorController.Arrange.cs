using Shiny.Controls.Office.Shapes;

namespace Shiny.Controls.Office.Presentation;

/// <summary>Where an Align command lines shapes up.</summary>
public enum ShapeAlignment
{
    Left,
    Center,
    Right,
    Top,
    Middle,
    Bottom
}

/// <summary>A smart guide line drawn while dragging, in slide coordinates.</summary>
/// <param name="IsVertical">True for a vertical line at <see cref="Position"/> on the x axis.</param>
/// <param name="Position">Where the line is, on the axis it crosses.</param>
/// <param name="From">Where it starts on the other axis.</param>
/// <param name="To">Where it ends on the other axis.</param>
public readonly record struct SlideGuide(bool IsVertical, double Position, double From, double To);

/// <summary>
/// Multi-selection, the marquee, rotation, smart guides and snapping, alignment and grouping.
/// </summary>
public sealed partial class SlideEditorController
{
    /// <summary>Selected shapes besides <see cref="SelectedShape"/>, the primary one.</summary>
    readonly List<int> others = [];

    /// <summary>A marquee being dragged out, in viewport coordinates: where it started and where the pointer is.</summary>
    (double X1, double Y1, double X2, double Y2)? marquee;

    readonly List<SlideGuide> guides = [];

    /// <summary>Every selected shape's bounds when the drag began, in slide units.</summary>
    readonly Dictionary<int, SlideRect> dragStart = [];

    double rotateStartAngle;
    double rotateStartRotation;

    /// <summary>How far above the frame the rotation grip sits, in viewport pixels.</summary>
    public const double RotateHandleOffset = 22;

    /// <summary>How close, in viewport pixels, an edge or centre has to come to a guide to snap to it.</summary>
    public double SnapDistance { get; set; } = 6;

    /// <summary>Smart guides: snap to the slide's edges and centre and to other shapes while dragging. On by default.</summary>
    public bool SmartGuides { get; set; } = true;

    /// <summary>The gridlines are showing (View ▸ Gridlines), and a drag snaps to them.</summary>
    public bool ShowGridlines { get; set; }

    /// <summary>The static centre guides are showing (View ▸ Guides).</summary>
    public bool ShowGuides { get; set; }

    /// <summary>The rulers are showing (View ▸ Ruler).</summary>
    public bool ShowRuler { get; set; }

    /// <summary>Grid spacing in slide pixels — PowerPoint's default of 1/12 inch is too fine to draw, so 1/2 inch.</summary>
    public double GridSpacing { get; set; } = 48;

    /// <summary>
    /// Every selected shape — the primary one first. Empty with nothing selected.
    /// </summary>
    public IReadOnlyList<int> SelectedShapes
    {
        get
        {
            if (this.selected < 0)
                return [];

            var count = this.Current?.Shapes.Count ?? 0;
            return this.others.Count == 0
                ? [this.selected]
                : [this.selected, .. this.others.Where(x => x >= 0 && x < count && x != this.selected)];
        }
    }

    /// <summary>True when more than one shape is selected.</summary>
    public bool HasMultipleSelection => this.selected >= 0 && this.others.Count > 0;

    /// <summary>The marquee in viewport coordinates while one is being dragged out.</summary>
    public SlideRect? Marquee => this.marquee is { } m
        ? new SlideRect(Math.Min(m.X1, m.X2), Math.Min(m.Y1, m.Y2), Math.Abs(m.X2 - m.X1), Math.Abs(m.Y2 - m.Y1))
        : null;

    /// <summary>The smart guides the current drag has snapped to, in slide coordinates.</summary>
    public IReadOnlyList<SlideGuide> ActiveGuides => this.guides;

    /// <summary>A selected shape can be rotated: a drawn shape or a picture, not a table or a group being entered.</summary>
    public bool CanRotateSelection
        => !this.IsReadOnly && this.others.Count == 0 && !this.IsEditingText &&
           this.Selection is { Element: not null, Table: null, Chart: null, IsGroup: false };

    /// <summary>The rotation grip above the selection, in viewport coordinates — or null when it cannot turn.</summary>
    public SlideRect? RotationHandle()
    {
        if (!this.CanRotateSelection || this.SelectionBounds() is not { } bounds || this.Selection is not { } shape)
            return null;

        var midX = bounds.X + bounds.Width / 2;
        var midY = bounds.Y + bounds.Height / 2;
        var (x, y) = RotatePoint(midX, bounds.Y - RotateHandleOffset, midX, midY, shape.Rotation);
        var size = this.HandleSize + 3;
        return new SlideRect(x - size / 2, y - size / 2, size, size);
    }

    /// <summary>The frames of every selected shape besides the primary one, with their rotations.</summary>
    public IEnumerable<(SlideRect Bounds, double Rotation)> SecondarySelectionFrames()
    {
        foreach (var index in this.others)
        {
            if (this.Current?.Shapes.ElementAtOrDefault(index) is { } shape && this.BoundsOf(shape) is { } bounds)
                yield return (bounds, shape.Rotation);
        }
    }

    /// <summary>The primary selection's rotation, for a host drawing its frame.</summary>
    public double SelectionRotation => this.Selection?.Rotation ?? 0;

    internal static (double X, double Y) RotatePoint(double x, double y, double cx, double cy, double degrees)
    {
        if (degrees == 0)
            return (x, y);

        var radians = degrees * Math.PI / 180;
        var (sin, cos) = Math.SinCos(radians);
        var dx = x - cx;
        var dy = y - cy;
        return (cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
    }

    /// <summary>A point inside a rectangle turned about its own centre.</summary>
    static bool ContainsRotated(SlideRect bounds, double rotation, double x, double y)
    {
        if (rotation != 0)
            (x, y) = RotatePoint(x, y, bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2, -rotation);

        return bounds.Contains(x, y);
    }

    // ---- selection ----

    /// <summary>Adds a shape to the selection, or takes it out — Shift/Ctrl+click.</summary>
    public void ToggleSelected(int shape)
    {
        if (this.Current?.Shapes.ElementAtOrDefault(shape) is not { IsEditable: true })
            return;

        this.EndTextEditing();

        if (this.selected < 0)
        {
            this.Select(shape);
            return;
        }

        if (shape == this.selected)
        {
            // Taking the primary out promotes the next one.
            if (this.others.Count == 0)
            {
                this.Select(-1);
                return;
            }

            this.selected = this.others[0];
            this.others.RemoveAt(0);
        }
        else if (!this.others.Remove(shape))
        {
            this.others.Add(shape);
        }

        this.RaiseChanged();
    }

    /// <summary>Selects every shape of the slide's own — Ctrl+A with nothing being typed into.</summary>
    public void SelectAllShapes()
    {
        if (this.Current is not { } slide)
            return;

        this.EndTextEditing();
        this.enteredGroup = null;

        var all = Enumerable.Range(0, slide.Shapes.Count).Where(i => slide.Shapes[i] is { IsEditable: true, IsInGroup: false }).ToList();
        this.Select(all.FirstOrDefault(-1));
        this.others.AddRange(all.Skip(1));
        this.RaiseChanged();
    }

    /// <summary>Selects exactly these shapes.</summary>
    public void SelectShapes(IReadOnlyList<int> shapes)
    {
        this.Select(shapes.FirstOrDefault(-1));
        foreach (var shape in shapes.Skip(1))
        {
            if (shape != this.selected && this.Current?.Shapes.ElementAtOrDefault(shape) is { IsEditable: true } && !this.others.Contains(shape))
                this.others.Add(shape);
        }

        this.RaiseChanged();
    }

    void SelectInside((double X1, double Y1, double X2, double Y2) box)
    {
        if (this.Current is not { } slide)
            return;

        var area = new SlideRect(Math.Min(box.X1, box.X2), Math.Min(box.Y1, box.Y2), Math.Abs(box.X2 - box.X1), Math.Abs(box.Y2 - box.Y1));

        // A click with no drag is not a marquee.
        if (area.Width < 3 && area.Height < 3)
        {
            this.RaiseChanged();
            return;
        }

        var inside = new List<int>();
        for (var i = 0; i < slide.Shapes.Count; i++)
        {
            var shape = slide.Shapes[i];
            if (!shape.IsEditable || shape.IsInGroup || this.BoundsOf(shape) is not { } bounds)
                continue;

            if (bounds.X >= area.X && bounds.Y >= area.Y && bounds.Right <= area.Right && bounds.Bottom <= area.Bottom)
                inside.Add(i);
        }

        this.SelectShapes(inside);
    }

    // ---- dragging ----

    void BeginDrag(ShapeHandle handle, double x, double y, SlideRect bounds)
    {
        this.dragging = handle;
        this.dragStartX = x;
        this.dragStartY = y;
        this.dragOrigin = bounds;
        this.dragStart.Clear();

        foreach (var index in this.SelectedShapes)
        {
            if (this.Current?.Shapes.ElementAtOrDefault(index) is { } shape)
                this.dragStart[index] = new SlideRect(shape.X, shape.Y, shape.Width, shape.Height);
        }

        if (handle == ShapeHandle.Rotate && this.Selection is { } selection && this.SelectionBounds() is { } frame)
        {
            var cx = frame.X + frame.Width / 2;
            var cy = frame.Y + frame.Height / 2;
            this.rotateStartAngle = Math.Atan2(y - cy, x - cx) * 180 / Math.PI;
            this.rotateStartRotation = selection.Rotation;
        }
    }

    void DragMove(double dx, double dy, bool constrain)
    {
        if (this.dragStart.Count == 0)
            return;

        // Shift keeps a move on one axis, whichever the pointer has travelled further along.
        if (constrain)
        {
            if (Math.Abs(dx) >= Math.Abs(dy))
                dy = 0;
            else
                dx = 0;
        }

        var left = this.dragStart.Values.Min(x => x.X) + dx;
        var top = this.dragStart.Values.Min(x => x.Y) + dy;
        var right = this.dragStart.Values.Max(x => x.Right) + dx;
        var bottom = this.dragStart.Values.Max(x => x.Bottom) + dy;

        var (snapX, snapY) = this.Snap(new SlideRect(left, top, right - left, bottom - top));
        dx += snapX;
        dy += snapY;

        if (this.dragStart.Count == 1)
        {
            var (index, start) = this.dragStart.First();
            this.Execute(new SetShapeBoundsCommand(this.Index, index, start.X + dx, start.Y + dy, start.Width, start.Height));
            return;
        }

        this.Execute(new SetShapesBoundsCommand(
            this.Index,
            this.dragStart.Select(x => (x.Key, x.Value.X + dx, x.Value.Y + dy, x.Value.Width, x.Value.Height)).ToList()));
    }

    /// <summary>
    /// Resizes by a handle, in the shape's own rotated frame so the edge opposite the handle stays put.
    /// </summary>
    void DragResize(SlideShape shape, double dx, double dy, bool constrain)
    {
        if (!this.dragStart.TryGetValue(this.selected, out var start))
            return;

        // The pointer's travel in the shape's own axes.
        var (ldx, ldy) = RotatePoint(dx, dy, 0, 0, -shape.Rotation);

        double l = -start.Width / 2, t = -start.Height / 2, r = start.Width / 2, b = start.Height / 2;

        switch (this.dragging)
        {
            case ShapeHandle.Left: l += ldx; break;
            case ShapeHandle.Right: r += ldx; break;
            case ShapeHandle.Top: t += ldy; break;
            case ShapeHandle.Bottom: b += ldy; break;
            case ShapeHandle.TopLeft: l += ldx; t += ldy; break;
            case ShapeHandle.TopRight: r += ldx; t += ldy; break;
            case ShapeHandle.BottomLeft: l += ldx; b += ldy; break;
            case ShapeHandle.BottomRight: r += ldx; b += ldy; break;
        }

        // Shift on a corner keeps the proportions: the larger change wins and the other follows.
        if (constrain && this.dragging is ShapeHandle.TopLeft or ShapeHandle.TopRight or ShapeHandle.BottomLeft or ShapeHandle.BottomRight && start.Height > 0)
        {
            var ratio = start.Width / start.Height;
            var width = r - l;
            var height = b - t;

            if (Math.Abs(width / start.Width) > Math.Abs(height / start.Height))
                height = width / ratio;
            else
                width = height * ratio;

            if (this.dragging is ShapeHandle.TopLeft or ShapeHandle.BottomLeft)
                l = r - width;
            else
                r = l + width;

            if (this.dragging is ShapeHandle.TopLeft or ShapeHandle.TopRight)
                t = b - height;
            else
                b = t + height;
        }

        var newWidth = Math.Max(4, r - l);
        var newHeight = Math.Max(4, b - t);

        // The new centre, turned back into slide space about the old centre.
        var centreX = start.X + start.Width / 2;
        var centreY = start.Y + start.Height / 2;
        var (cx, cy) = RotatePoint(centreX + (l + r) / 2, centreY + (t + b) / 2, centreX, centreY, shape.Rotation);

        this.Execute(new SetShapeBoundsCommand(this.Index, this.selected, cx - newWidth / 2, cy - newHeight / 2, newWidth, newHeight));
    }

    void DragRotate(SlideShape shape, double x, double y, bool constrain)
    {
        if (this.SelectionBounds() is not { } frame)
            return;

        var cx = frame.X + frame.Width / 2;
        var cy = frame.Y + frame.Height / 2;
        var angle = Math.Atan2(y - cy, x - cx) * 180 / Math.PI;
        var rotation = this.rotateStartRotation + (angle - this.rotateStartAngle);

        // Shift snaps to 15 degrees, as PowerPoint's does.
        if (constrain)
            rotation = Math.Round(rotation / 15) * 15;

        rotation = ((rotation % 360) + 360) % 360;
        this.Execute(new SetShapeRotationCommand(this.Index, this.selected, rotation));
    }

    /// <summary>
    /// How far to nudge a moving box so an edge or its centre lands on a guide — the slide's edges and
    /// centre, every other shape's edges and centre, and the grid when it is showing.
    /// </summary>
    (double X, double Y) Snap(SlideRect box)
    {
        this.guides.Clear();

        if ((!this.SmartGuides && !this.ShowGridlines) || this.Scale <= 0)
            return (0, 0);

        var threshold = this.SnapDistance / this.Scale;
        var width = this.Deck.SlideWidth;
        var height = this.Deck.SlideHeight;

        var xs = new List<(double Value, double From, double To)>();
        var ys = new List<(double Value, double From, double To)>();

        if (this.SmartGuides)
        {
            xs.AddRange([(0, 0, height), (width / 2, 0, height), (width, 0, height)]);
            ys.AddRange([(0, 0, width), (height / 2, 0, width), (height, 0, width)]);

            if (this.Current is { } slide)
            {
                for (var i = 0; i < slide.Shapes.Count; i++)
                {
                    var other = slide.Shapes[i];
                    if (this.dragStart.ContainsKey(i) || !other.IsEditable || other.IsInGroup || other.Width <= 0)
                        continue;

                    var top = Math.Min(other.Y, box.Y);
                    var bottom = Math.Max(other.Y + other.Height, box.Bottom);
                    var left = Math.Min(other.X, box.X);
                    var right = Math.Max(other.X + other.Width, box.Right);

                    xs.AddRange([(other.X, top, bottom), (other.X + other.Width / 2, top, bottom), (other.X + other.Width, top, bottom)]);
                    ys.AddRange([(other.Y, left, right), (other.Y + other.Height / 2, left, right), (other.Y + other.Height, left, right)]);
                }
            }
        }

        var bestX = Best([box.X, box.X + box.Width / 2, box.Right], xs, threshold);
        var bestY = Best([box.Y, box.Y + box.Height / 2, box.Bottom], ys, threshold);

        // The grid, when nothing smarter is near.
        if (this.ShowGridlines && this.GridSpacing > 0)
        {
            bestX ??= GridSnap(box.X, threshold);
            bestY ??= GridSnap(box.Y, threshold);
        }

        if (bestX is { } sx && sx.Guide is { } gx)
            this.guides.Add(new SlideGuide(true, gx.Value, gx.From, gx.To));

        if (bestY is { } sy && sy.Guide is { } gy)
            this.guides.Add(new SlideGuide(false, gy.Value, gy.From, gy.To));

        return (bestX?.Delta ?? 0, bestY?.Delta ?? 0);

        static (double Delta, (double Value, double From, double To)? Guide)? Best(double[] edges, List<(double Value, double From, double To)> targets, double threshold)
        {
            (double Delta, (double Value, double From, double To)? Guide)? best = null;
            foreach (var edge in edges)
            {
                foreach (var target in targets)
                {
                    var delta = target.Value - edge;
                    if (Math.Abs(delta) <= threshold && (best is null || Math.Abs(delta) < Math.Abs(best.Value.Delta)))
                        best = (delta, target);
                }
            }

            return best;
        }

        (double Delta, (double Value, double From, double To)? Guide)? GridSnap(double edge, double threshold)
        {
            var nearest = Math.Round(edge / this.GridSpacing) * this.GridSpacing;
            return Math.Abs(nearest - edge) <= threshold ? (nearest - edge, null) : null;
        }
    }

    // ---- arrange ----

    /// <summary>Whether Align lines up to the slide (one shape selected, or asked for) or to the selection.</summary>
    public bool AlignToSlide { get; set; }

    /// <summary>
    /// Lines the selected shapes up — Arrange ▸ Align. One shape aligns to the slide; several align to
    /// the edge of the whole selection unless <see cref="AlignToSlide"/> is on.
    /// </summary>
    public void Align(ShapeAlignment alignment)
    {
        if (this.IsReadOnly || this.Current is not { } slide || this.SelectedShapes is not { Count: > 0 } selected)
            return;

        this.EndTextEditing();
        var bounds = SlideArrangement.Align(
            selected.Select(i => (i, new SlideRect(slide.Shapes[i].X, slide.Shapes[i].Y, slide.Shapes[i].Width, slide.Shapes[i].Height))).ToList(),
            alignment,
            this.AlignToSlide || selected.Count == 1 ? new SlideRect(0, 0, this.Deck.SlideWidth, this.Deck.SlideHeight) : null);

        this.Execute(new SetShapesBoundsCommand(this.Index, bounds.Select(x => (x.Shape, x.Bounds.X, x.Bounds.Y, x.Bounds.Width, x.Bounds.Height)).ToList(), "Align"));
        this.deck.Undo.BreakCoalescing();
    }

    /// <summary>Spaces the selected shapes evenly — Distribute Horizontally/Vertically.</summary>
    public void Distribute(bool horizontally)
    {
        if (this.IsReadOnly || this.Current is not { } slide || this.SelectedShapes is not { Count: > 0 } selected)
            return;

        // Two shapes cannot be distributed among themselves; with the slide they can.
        var toSlide = this.AlignToSlide || selected.Count < 3;
        this.EndTextEditing();

        var bounds = SlideArrangement.Distribute(
            selected.Select(i => (i, new SlideRect(slide.Shapes[i].X, slide.Shapes[i].Y, slide.Shapes[i].Width, slide.Shapes[i].Height))).ToList(),
            horizontally,
            toSlide ? new SlideRect(0, 0, this.Deck.SlideWidth, this.Deck.SlideHeight) : null);

        this.Execute(new SetShapesBoundsCommand(this.Index, bounds.Select(x => (x.Shape, x.Bounds.X, x.Bounds.Y, x.Bounds.Width, x.Bounds.Height)).ToList(), "Distribute"));
        this.deck.Undo.BreakCoalescing();
    }

    /// <summary>Two or more shapes sharing a parent are selected, so they can be grouped.</summary>
    public bool CanGroup
        => !this.IsReadOnly && this.Current is { } slide && this.SelectedShapes.Count > 1 &&
           this.SelectedShapes.Select(i => slide.Shapes[i].Element?.Parent).Distinct().Count() == 1;

    /// <summary>A group is selected, so it can be ungrouped.</summary>
    public bool CanUngroup => !this.IsReadOnly && this.others.Count == 0 && this.Selection is { IsGroup: true, Element: not null };

    /// <summary>Groups the selected shapes and selects the group — Ctrl+G.</summary>
    public void Group()
    {
        if (!this.CanGroup || this.deck.TreeAt(this.Index) is not { } tree)
            return;

        this.EndTextEditing();
        this.Execute(new GroupShapesCommand(this.Index, this.SelectedShapes));

        // The group is the one new p:grpSp; its entry in the model is what gets selected.
        var group = tree.Descendants<DocumentFormat.OpenXml.Presentation.GroupShape>()
            .OrderByDescending(x => x.NonVisualGroupShapeProperties?.NonVisualDrawingProperties?.Id?.Value ?? 0)
            .FirstOrDefault();

        this.selected = -1;
        this.others.Clear();
        this.enteredGroup = null;

        if (group is not null)
            this.Reselect(group);
        else
            this.RaiseChanged();
    }

    /// <summary>Takes the selected group apart and selects its members — Ctrl+Shift+G.</summary>
    public void Ungroup()
    {
        if (!this.CanUngroup || this.Selection?.Element is not { } group)
            return;

        var members = group.ChildElements.Where(SlideObjects.IsShapeElement).ToList();
        this.EndTextEditing();
        this.Execute(new UngroupShapeCommand(this.Index, this.selected));

        this.selected = -1;
        this.others.Clear();
        this.enteredGroup = null;

        if (this.Current is { } slide)
        {
            var indices = members.Select(m => slide.Shapes.ToList().FindIndex(x => ReferenceEquals(x.Element, m))).Where(x => x >= 0).ToList();
            this.SelectShapes(indices);
        }
    }
}

/// <summary>The arithmetic behind Align and Distribute, kept apart so it can be tested on its own.</summary>
public static class SlideArrangement
{
    /// <summary>
    /// Where each shape goes when aligned. Aligns to <paramref name="container"/> when given (the slide),
    /// otherwise to the bounds of the shapes themselves.
    /// </summary>
    public static IReadOnlyList<(int Shape, SlideRect Bounds)> Align(IReadOnlyList<(int Shape, SlideRect Bounds)> shapes, ShapeAlignment alignment, SlideRect? container = null)
    {
        if (shapes.Count == 0)
            return [];

        var area = container ?? Union(shapes.Select(x => x.Bounds));

        return shapes.Select(item =>
        {
            var b = item.Bounds;
            var moved = alignment switch
            {
                ShapeAlignment.Left => b with { X = area.X },
                ShapeAlignment.Center => b with { X = area.X + (area.Width - b.Width) / 2 },
                ShapeAlignment.Right => b with { X = area.Right - b.Width },
                ShapeAlignment.Top => b with { Y = area.Y },
                ShapeAlignment.Middle => b with { Y = area.Y + (area.Height - b.Height) / 2 },
                _ => b with { Y = area.Bottom - b.Height }
            };

            return (item.Shape, moved);
        }).ToList();
    }

    /// <summary>
    /// Spaces shapes so the gaps between them are equal. Within the outermost shapes' span, or across
    /// <paramref name="container"/> when one is given.
    /// </summary>
    public static IReadOnlyList<(int Shape, SlideRect Bounds)> Distribute(IReadOnlyList<(int Shape, SlideRect Bounds)> shapes, bool horizontally, SlideRect? container = null)
    {
        if (shapes.Count == 0)
            return [];

        var ordered = shapes.OrderBy(x => horizontally ? x.Bounds.X : x.Bounds.Y).ToList();
        var area = container ?? Union(ordered.Select(x => x.Bounds));

        // One shape distributed across the slide is centred on it, which is what PowerPoint does.
        if (ordered.Count == 1)
            return Align(ordered, horizontally ? ShapeAlignment.Center : ShapeAlignment.Middle, area);

        // The outermost shapes go to the ends of the span and the rest share the space between.
        var total = ordered.Sum(x => horizontally ? x.Bounds.Width : x.Bounds.Height);
        var span = horizontally ? area.Width : area.Height;
        var gap = (span - total) / (ordered.Count - 1);

        var cursor = horizontally ? area.X : area.Y;
        var result = new List<(int, SlideRect)>();

        foreach (var (shape, bounds) in ordered)
        {
            result.Add((shape, horizontally ? bounds with { X = cursor } : bounds with { Y = cursor }));
            cursor += (horizontally ? bounds.Width : bounds.Height) + gap;
        }

        return result;
    }

    static SlideRect Union(IEnumerable<SlideRect> rects)
    {
        var list = rects.ToList();
        var left = list.Min(x => x.X);
        var top = list.Min(x => x.Y);
        return new SlideRect(left, top, list.Max(x => x.Right) - left, list.Max(x => x.Bottom) - top);
    }
}

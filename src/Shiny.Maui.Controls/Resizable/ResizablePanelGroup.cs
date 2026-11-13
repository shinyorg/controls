using System.Windows.Input;
using Microsoft.Maui.Layouts;
using Shiny.Controls.Resizable;

namespace Shiny.Maui.Controls;

/// <summary>
/// Panels laid side by side (or stacked) with draggable handles between them, sized in percent of the
/// group.
/// </summary>
/// <example>
/// <code>
/// &lt;shiny:ResizablePanelGroup Direction="Horizontal"&gt;
///     &lt;shiny:ResizablePanel DefaultSize="25" MinSize="15" Collapsible="True"&gt;
///         &lt;local:Sidebar /&gt;
///     &lt;/shiny:ResizablePanel&gt;
///     &lt;shiny:ResizableHandle WithHandle="True" /&gt;
///     &lt;shiny:ResizablePanel&gt;
///         &lt;shiny:ResizablePanelGroup Direction="Vertical"&gt;
///             &lt;shiny:ResizablePanel DefaultSize="70"&gt;&lt;local:Editor /&gt;&lt;/shiny:ResizablePanel&gt;
///             &lt;shiny:ResizableHandle /&gt;
///             &lt;shiny:ResizablePanel&gt;&lt;local:Terminal /&gt;&lt;/shiny:ResizablePanel&gt;
///         &lt;/shiny:ResizablePanelGroup&gt;
///     &lt;/shiny:ResizablePanel&gt;
/// &lt;/shiny:ResizablePanelGroup&gt;
/// </code>
/// </example>
/// <remarks>
/// <para>
/// Children are <see cref="ResizablePanel"/>s with a <see cref="ResizableHandle"/> between each pair.
/// The group fills the space it is given and divides it - after the handles' one-unit lines - by
/// percentage, so give it a real size (a <c>*</c> grid row, a page) rather than a stack layout or a
/// scroller on the same axis, which has no length to divide.
/// </para>
/// <para>
/// Dragging a handle grows the panel on one side and shrinks the other side, nearest panel first, then
/// the next once that one reaches its <see cref="ResizablePanel.MinSize"/>. A collapsible panel snaps
/// shut when dragged more than halfway past its minimum. <see cref="Sizes"/> and
/// <see cref="LayoutChanged"/> update when a change settles (the end of a drag, a collapse, an expand),
/// which makes them the place to save and restore a layout.
/// </para>
/// </remarks>
public class ResizablePanelGroup : Layout
{
    sealed class GroupLayoutManager(ResizablePanelGroup group) : ILayoutManager
    {
        public Size Measure(double widthConstraint, double heightConstraint) => group.MeasureGroup(widthConstraint, heightConstraint);
        public Size ArrangeChildren(Rect bounds) => group.ArrangeGroup(bounds);
    }

    protected override ILayoutManager CreateLayoutManager() => new GroupLayoutManager(this);

    double[]? sizes;
    double[]? dragStart;
    double lastAvailable;
    bool syncingSizes;

    /// <summary>Set while panels are being told about a settled layout, so their IsCollapsed echo is ignored.</summary>
    internal bool Syncing { get; set; }


    // ---------------------------------------------------------------------------------------------
    // Properties
    // ---------------------------------------------------------------------------------------------

    public static readonly BindableProperty DirectionProperty = BindableProperty.Create(
        nameof(Direction), typeof(ResizableDirection), typeof(ResizablePanelGroup), ResizableDirection.Horizontal,
        propertyChanged: (b, _, _) => ((ResizablePanelGroup)b).OnDirectionChanged());

    /// <summary>Side by side (the default) or stacked.</summary>
    public ResizableDirection Direction
    {
        get => (ResizableDirection)this.GetValue(DirectionProperty);
        set => this.SetValue(DirectionProperty, value);
    }

    public static readonly BindableProperty SizesProperty = BindableProperty.Create(
        nameof(Sizes), typeof(IReadOnlyList<double>), typeof(ResizablePanelGroup), null, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => ((ResizablePanelGroup)b).OnSizesPropertyChanged((IReadOnlyList<double>?)n));

    /// <summary>
    /// Each panel's size in percent, in panel order. Two-way: updated when a resize settles, and setting
    /// it (a saved layout) resizes the panels - held to their limits and scaled to add up to 100. Wins
    /// over <see cref="ResizablePanel.DefaultSize"/> when its count matches the panels.
    /// </summary>
    public IReadOnlyList<double>? Sizes
    {
        get => (IReadOnlyList<double>?)this.GetValue(SizesProperty);
        set => this.SetValue(SizesProperty, value);
    }

    public static readonly BindableProperty LayoutChangedCommandProperty = BindableProperty.Create(
        nameof(LayoutChangedCommand), typeof(ICommand), typeof(ResizablePanelGroup));

    /// <summary>Executed alongside <see cref="LayoutChanged"/>, with the sizes as its parameter.</summary>
    public ICommand? LayoutChangedCommand
    {
        get => (ICommand?)this.GetValue(LayoutChangedCommandProperty);
        set => this.SetValue(LayoutChangedCommandProperty, value);
    }

    /// <summary>The panels settled on new sizes: the end of a drag, a collapse or expand, or <see cref="SetSizes"/>.</summary>
    public event EventHandler<ResizableLayoutChangedEventArgs>? LayoutChanged;


    // ---------------------------------------------------------------------------------------------
    // Methods
    // ---------------------------------------------------------------------------------------------

    /// <summary>The panels, in order.</summary>
    public IReadOnlyList<ResizablePanel> Panels => this.Children.OfType<ResizablePanel>().ToList();

    /// <summary>Resizes every panel at once (percent, in panel order), within their limits.</summary>
    public void SetSizes(params double[] values)
    {
        var limits = this.Limits();
        if (limits.Count == 0)
            return;

        this.Commit(ResizableLayout.Validate(values, limits), raise: true);
    }

    /// <summary>Collapses <paramref name="panel"/>. Needs <see cref="ResizablePanel.Collapsible"/>.</summary>
    public void Collapse(ResizablePanel panel)
    {
        var index = this.IndexOf(panel);
        if (index < 0 || !panel.Collapsible)
            return;

        var current = this.EnsureSizes();
        var next = ResizableLayout.Collapse(current, this.Limits(), index);
        if (ResizableLayout.IsCollapsed(next, this.Limits(), index))
            this.Commit(next, raise: true);
        else
            this.Settle(current);   // could not make room: put IsCollapsed back to the truth
    }

    /// <summary>Opens a collapsed <paramref name="panel"/> to the size it had before it collapsed.</summary>
    public void Expand(ResizablePanel panel)
    {
        var index = this.IndexOf(panel);
        if (index < 0)
            return;

        var current = this.EnsureSizes();
        this.Commit(ResizableLayout.Expand(current, this.Limits(), index, panel.ExpandedSize), raise: true);
    }


    // ---------------------------------------------------------------------------------------------
    // Children
    // ---------------------------------------------------------------------------------------------

    protected override void OnChildAdded(Element child)
    {
        base.OnChildAdded(child);
        switch (child)
        {
            case ResizablePanel panel:
                panel.Group = this;
                this.sizes = null;
                break;

            case ResizableHandle handle:
                handle.Group = this;
                handle.SetDirection(this.Direction);
                // The touch target overlaps the panel after it, which comes later in the tree and would
                // otherwise sit on top and take the touch.
                handle.ZIndex = 1;
                break;
        }
    }

    protected override void OnChildRemoved(Element child, int oldLogicalIndex)
    {
        base.OnChildRemoved(child, oldLogicalIndex);
        switch (child)
        {
            case ResizablePanel panel:
                panel.Group = null;
                this.sizes = null;
                break;

            case ResizableHandle handle:
                handle.Group = null;
                break;
        }
    }

    internal void OnPanelDefaultsChanged()
    {
        // Defaults only matter until the group has sizes of its own.
        if (this.Sizes is null && this.dragStart is null)
        {
            this.sizes = null;
            this.InvalidateMeasure();
        }
    }

    internal void OnPanelLimitsChanged()
    {
        if (this.sizes is null)
            return;

        this.Commit(ResizableLayout.Validate(this.sizes, this.Limits()), raise: false);
    }

    internal void OnPanelIsCollapsedChanged(ResizablePanel panel, bool collapsed)
    {
        if (this.Syncing)
            return;

        if (collapsed)
            this.Collapse(panel);
        else
            this.Expand(panel);
    }


    // ---------------------------------------------------------------------------------------------
    // Dragging
    // ---------------------------------------------------------------------------------------------

    internal void BeginDrag(ResizableHandle handle)
    {
        if (this.HandleIndex(handle) < 0)
            return;

        this.dragStart = this.EnsureSizes().ToArray();
    }

    internal void Drag(ResizableHandle handle, double distance)
    {
        var index = this.HandleIndex(handle);
        if (this.dragStart is null || index < 0 || this.lastAvailable <= 0)
            return;

        var delta = distance / this.lastAvailable * 100;
        this.sizes = ResizableLayout.Resize(this.dragStart, this.Limits(), index, delta);
        this.InvalidateMeasure();
    }

    internal void EndDrag(ResizableHandle handle)
    {
        if (this.dragStart is null)
            return;

        var start = this.dragStart;
        this.dragStart = null;
        if (this.sizes is { } settled && !settled.SequenceEqual(start))
            this.Commit(settled, raise: true);
    }


    // ---------------------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------------------

    bool IsHorizontal => this.Direction == ResizableDirection.Horizontal;

    Size MeasureGroup(double widthConstraint, double heightConstraint)
    {
        var padding = this.Padding;
        var innerWidth = Math.Max(0, widthConstraint - padding.HorizontalThickness);
        var innerHeight = Math.Max(0, heightConstraint - padding.VerticalThickness);
        var axis = this.IsHorizontal ? innerWidth : innerHeight;
        var cross = this.IsHorizontal ? innerHeight : innerWidth;

        var sizes = this.EnsureSizes();
        var panels = this.Children.OfType<ResizablePanel>().ToList();
        var handles = this.LaidOutHandles();
        var available = Math.Max(0, axis - handles.Count * ResizableHandle.LineThickness);

        double used = 0, crossNeeded = 0;
        for (var i = 0; i < panels.Count; i++)
        {
            var length = Double.IsInfinity(available) ? Double.PositiveInfinity : available * sizes[i] / 100;
            var measured = this.IsHorizontal
                ? panels[i].Measure(length, cross)
                : panels[i].Measure(cross, length);

            used += this.IsHorizontal ? measured.Width : measured.Height;
            crossNeeded = Math.Max(crossNeeded, this.IsHorizontal ? measured.Height : measured.Width);
        }

        foreach (var handle in this.Children.OfType<ResizableHandle>())
            handle.Measure(this.IsHorizontal ? ResizableHandle.HitSize : cross, this.IsHorizontal ? cross : ResizableHandle.HitSize);

        // A group fills its axis; only an unbounded one falls back to what the panels asked for.
        var axisResult = Double.IsInfinity(axis) ? used + handles.Count * ResizableHandle.LineThickness : axis;
        var crossResult = Double.IsInfinity(cross) ? crossNeeded : cross;

        return this.IsHorizontal
            ? new Size(axisResult + padding.HorizontalThickness, crossResult + padding.VerticalThickness)
            : new Size(crossResult + padding.HorizontalThickness, axisResult + padding.VerticalThickness);
    }

    Size ArrangeGroup(Rect bounds)
    {
        var padding = this.Padding;
        var inner = new Rect(
            bounds.X + padding.Left,
            bounds.Y + padding.Top,
            Math.Max(0, bounds.Width - padding.HorizontalThickness),
            Math.Max(0, bounds.Height - padding.VerticalThickness));

        var sizes = this.EnsureSizes();
        var laidOut = this.LaidOutHandles();
        var axis = this.IsHorizontal ? inner.Width : inner.Height;
        var available = Math.Max(0, axis - laidOut.Count * ResizableHandle.LineThickness);
        this.lastAvailable = available;

        var position = this.IsHorizontal ? inner.X : inner.Y;
        var panelIndex = 0;
        foreach (var child in this.Children)
        {
            switch (child)
            {
                case ResizablePanel panel:
                {
                    var length = available * sizes[panelIndex++] / 100;
                    panel.Arrange(this.IsHorizontal
                        ? new Rect(position, inner.Y, length, inner.Height)
                        : new Rect(inner.X, position, inner.Width, length));
                    position += length;
                    break;
                }

                case ResizableHandle handle when laidOut.Contains(handle):
                {
                    // The line takes one unit; the touch target spreads over the panels either side.
                    var start = position + (ResizableHandle.LineThickness - ResizableHandle.HitSize) / 2;
                    handle.Arrange(this.IsHorizontal
                        ? new Rect(start, inner.Y, ResizableHandle.HitSize, inner.Height)
                        : new Rect(inner.X, start, inner.Width, ResizableHandle.HitSize));
                    position += ResizableHandle.LineThickness;
                    break;
                }

                case IView other:
                    // A handle with no panel on one side, or anything that is not ours: out of the way.
                    other.Arrange(new Rect(inner.X, inner.Y, 0, 0));
                    break;
            }
        }

        return bounds.Size;
    }


    // ---------------------------------------------------------------------------------------------
    // Internals
    // ---------------------------------------------------------------------------------------------

    /// <summary>The handles that sit between two panels, and so are laid out.</summary>
    List<ResizableHandle> LaidOutHandles()
    {
        var result = new List<ResizableHandle>();
        var panelsSeen = 0;
        var total = this.Children.OfType<ResizablePanel>().Count();
        foreach (var child in this.Children)
        {
            if (child is ResizablePanel)
                panelsSeen++;
            else if (child is ResizableHandle handle && panelsSeen > 0 && panelsSeen < total)
                result.Add(handle);
        }
        return result;
    }

    /// <summary>The handle's position: handle <c>n</c> sits between panels <c>n</c> and <c>n + 1</c>. -1 when it is not between two.</summary>
    internal int HandleIndex(ResizableHandle handle)
    {
        var panelsSeen = 0;
        var total = this.Children.OfType<ResizablePanel>().Count();
        foreach (var child in this.Children)
        {
            if (child is ResizablePanel)
                panelsSeen++;
            else if (ReferenceEquals(child, handle))
                return panelsSeen > 0 && panelsSeen < total ? panelsSeen - 1 : -1;
        }
        return -1;
    }

    int IndexOf(ResizablePanel panel)
    {
        var index = 0;
        foreach (var child in this.Children)
        {
            if (ReferenceEquals(child, panel))
                return index;
            if (child is ResizablePanel)
                index++;
        }
        return -1;
    }

    List<ResizablePanelLimits> Limits()
        => this.Children.OfType<ResizablePanel>().Select(x => x.Limits).ToList();

    /// <summary>The current sizes, worked out from <see cref="Sizes"/> or the panels' defaults the first time they are needed.</summary>
    double[] EnsureSizes()
    {
        var panels = this.Children.OfType<ResizablePanel>().ToList();
        if (this.sizes is { } current && current.Length == panels.Count)
            return current;

        var limits = panels.Select(x => x.Limits).ToList();
        var initial = this.Sizes is { } given && given.Count == panels.Count
            ? ResizableLayout.Validate(given, limits)
            : ResizableLayout.Initial(panels.Select(x => x.DefaultSize).ToList(), limits);

        // Panels declared collapsed start that way.
        for (var i = 0; i < panels.Count; i++)
        {
            if (panels[i].IsCollapsed && panels[i].Collapsible && !limits[i].IsCollapsed(initial[i]))
            {
                panels[i].ExpandedSize = initial[i];
                initial = ResizableLayout.Collapse(initial, limits, i);
            }
        }

        this.sizes = initial;
        this.Settle(initial);
        return initial;
    }

    void Commit(double[] next, bool raise)
    {
        this.sizes = next;
        this.InvalidateMeasure();
        this.Settle(next);

        this.syncingSizes = true;
        try
        {
            if (this.Sizes is not { } bound || !bound.SequenceEqual(next))
                this.Sizes = next;
        }
        finally
        {
            this.syncingSizes = false;
        }

        if (!raise)
            return;

        var args = new ResizableLayoutChangedEventArgs(next);
        this.LayoutChanged?.Invoke(this, args);
        if (this.LayoutChangedCommand?.CanExecute(next) == true)
            this.LayoutChangedCommand.Execute(next);
    }

    void Settle(double[] values)
    {
        var panels = this.Children.OfType<ResizablePanel>().ToList();
        var limits = panels.Select(x => x.Limits).ToList();
        for (var i = 0; i < panels.Count && i < values.Length; i++)
            panels[i].Settle(values[i], limits[i].IsCollapsed(values[i]));
    }

    void OnSizesPropertyChanged(IReadOnlyList<double>? value)
    {
        // MAUI defers a set made inside this same callback until the outer set finishes, so Commit's
        // echo of Sizes can arrive here after syncingSizes is cleared: one already in place is ignored.
        if (this.syncingSizes || value is null || (this.sizes is { } current && value.SequenceEqual(current)))
            return;

        var limits = this.Limits();
        if (limits.Count == 0 || value.Count != limits.Count)
        {
            // Panels not added yet (XAML sets attributes before children): picked up by EnsureSizes.
            this.sizes = null;
            this.InvalidateMeasure();
            return;
        }

        this.Commit(ResizableLayout.Validate(value, limits), raise: false);
    }

    void OnDirectionChanged()
    {
        foreach (var handle in this.Children.OfType<ResizableHandle>())
            handle.SetDirection(this.Direction);

        this.InvalidateMeasure();
    }
}

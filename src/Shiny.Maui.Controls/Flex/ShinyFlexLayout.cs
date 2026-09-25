using Microsoft.Maui.Handlers;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Primitives;

namespace Shiny.Maui.Controls;

/// <summary>
/// A CSS-flexbox layout with the same properties as MAUI's <c>FlexLayout</c>, plus row/column gaps.
/// It is built to be fast: children are measured once and reused until they change, and a pass at
/// the same size costs no measuring at all.
/// </summary>
/// <remarks>
/// <para>
/// MAUI's <c>FlexLayout</c> is slow because it forgets everything between passes. It measures every
/// child in every measure pass and again in every arrange pass, through a port of a C engine that
/// allocates as it goes. A page with a hundred chips measures all of them each time the platform lays
/// the page out, even if nothing changed. And when one label changes its text, every sibling is
/// measured again.
/// </para>
/// <para>
/// This layout keeps three things instead:
/// </para>
/// <list type="bullet">
/// <item><b>Per-child measurements.</b> They are reused until that child raises
/// <see cref="VisualElement.MeasureInvalidated"/>. A text change measures one child, not all of them.
/// For leaf views (not layouts or content hosts) a measurement is also reused under a different
/// constraint when the child did not use all of the old one and still fits the new one. That is why
/// a resize or rotation measures almost nothing.</item>
/// <item><b>The last resolved pass.</b> Lines, flexed sizes and cross sizes are kept, keyed on the
/// available size. Measure followed by arrange at the same size, which is the normal case, resolves
/// the flex once. Arrange itself is only arithmetic.</item>
/// <item><b>Reusable buffers.</b> The pass reads from arrays that are only reallocated when the
/// child count grows. It does not allocate, use LINQ or box, and it only sorts when some child has a
/// non-zero <see cref="OrderProperty"/>.</item>
/// </list>
/// <para>
/// The API mirrors <c>FlexLayout</c> and uses the same <c>Microsoft.Maui.Layouts</c> enums and
/// <see cref="FlexBasis"/>. Porting a page means changing the element and attached-property prefix.
/// <c>FlexLayout.Position</c> (absolute positioning) is not carried over.
/// </para>
/// <para>
/// The manager is a nested class rather than the layout implementing <see cref="ILayoutManager"/>
/// itself: <c>ILayoutManager.Measure</c> would hide <c>VisualElement.Measure</c> (see TagWrapLayout).
/// </para>
/// </remarks>
public partial class ShinyFlexLayout : Layout
{
    const double Epsilon = 0.01;

    sealed class FlexManager(ShinyFlexLayout flex) : ILayoutManager
    {
        public Size Measure(double widthConstraint, double heightConstraint) => flex.MeasureFlex(widthConstraint, heightConstraint);
        public Size ArrangeChildren(Rect bounds) => flex.ArrangeFlex(bounds);
    }

    protected override ILayoutManager CreateLayoutManager() => new FlexManager(this);


    static ShinyFlexLayout()
    {
        // A child can be invalidated two ways, and only one of them is visible from here. The Controls
        // path (a Label's Text, a WidthRequest, IsVisible) raises MeasureInvalidated, and layouts pass
        // it up. The handler path, IView.InvalidateMeasure(), is what a handler calls when the native view
        // changes size (an image finishing loading, for example). It raises no event and never reaches any
        // parent on the Controls side: it goes to Handler.Invoke and then up the native tree. MAUI's own
        // layouts don't notice because they measure everything every pass. This layout does not, so it
        // listens for the command. It walks up from the view that changed, because an image deep inside a
        // Grid that is a flex child makes that Grid's cached size stale too. Every flex on the way forgets
        // only the one child the change came through.
        ViewHandler.ViewCommandMapper.AppendToMapping(nameof(IView.InvalidateMeasure), static (_, view, _) =>
        {
            var current = view as Element;
            while (current?.Parent is { } parent)
            {
                if (parent is ShinyFlexLayout flex)
                    flex.OnChildInvalidatedNatively(current);

                current = parent;
            }
        });
    }


    /// <summary>What the layout remembers about one child between passes.</summary>
    sealed class ChildState(IView view)
    {
        public readonly IView View = view;

        // Leaf views measure as min(natural, constraint), which is what lets a measurement taken under
        // one constraint answer for another. Containers do not: a Grid with star columns fills whatever
        // width it is given, so it only gets exact constraint matches.
        public readonly bool IsLeaf = view is not (Microsoft.Maui.ILayout or IContentView);

        public int Order;
        public float Grow;
        public float Shrink = 1;
        public FlexAlignSelf AlignSelf;

        // FlexBasis keeps IsAuto/IsRelative internal, so the kind is worked out once here by equality
        // rather than on every pass.
        public bool BasisAuto = true;
        public bool BasisRelative;
        public double BasisLength;

        // Two cache slots: the natural measurement and the one at the flexed size. Those are the two
        // constraints a child sees in a normal pass.
        double aW = double.NaN, aH;
        Size a;
        double bW = double.NaN, bH;
        Size b;

        public void ReadAttached(BindableObject bindable)
        {
            this.Order = GetOrder(bindable);
            this.Grow = GetGrow(bindable);
            this.Shrink = GetShrink(bindable);
            var basis = GetBasis(bindable);
            this.BasisAuto = basis == FlexBasis.Auto;
            this.BasisRelative = !this.BasisAuto && basis.Length is >= 0 and <= 1 && basis == new FlexBasis(basis.Length, isRelative: true);
            this.BasisLength = basis.Length;
            this.AlignSelf = GetAlignSelf(bindable);
        }

        public void Forget()
        {
            this.aW = double.NaN;
            this.bW = double.NaN;
        }

        public Size Measure(double width, double height, bool useCache)
        {
            if (useCache)
            {
                if (this.Hit(this.aW, this.aH, this.a, width, height))
                    return this.a;

                if (this.Hit(this.bW, this.bH, this.b, width, height))
                    return this.b;
            }

            var size = this.View.Measure(width, height);

            // An empty result is not cached. It usually means "not loaded yet", for example an image
            // before its source arrives. If that child resized natively without invalidating, a cached
            // zero would stick; measuring an empty view again next pass costs nothing.
            if (size.Width > 0 && size.Height > 0)
            {
                this.b = this.a;
                this.bW = this.aW;
                this.bH = this.aH;
                this.a = size;
                this.aW = width;
                this.aH = height;
            }
            return size;
        }

        bool Hit(double cachedW, double cachedH, Size cached, double width, double height)
            => !double.IsNaN(cachedW)
               && this.AxisReusable(cachedW, cached.Width, width)
               && this.AxisReusable(cachedH, cached.Height, height);

        bool AxisReusable(double cachedConstraint, double result, double constraint)
        {
            if (Same(cachedConstraint, constraint))
                return true;

            // The child did not use its whole constraint, so it was not limited by it. Any constraint
            // that still fits the result gives the same answer.
            return this.IsLeaf && result < cachedConstraint - Epsilon && result <= constraint + Epsilon;
        }
    }


    /// <summary>One visible child's working values for the current pass. All sizes are outer (margin included).</summary>
    struct Item
    {
        public ChildState State;
        public int Index;
        public double MarginMain;
        public double FlexBase;       // unclamped starting size
        public double Hyp;            // FlexBase clamped to min/max
        public double MinMain;
        public double MaxMain;
        public double MaxCross;
        public double MeasuredMain;   // main size the cross size was measured at; NaN = not measured
        public double Main;           // resolved main size
        public double Cross;          // measured cross size
        public bool ExplicitCross;
        public bool Frozen;

        // Arrange only
        public double MainPos;
        public double CrossPos;
        public double CrossSize;
    }

    struct Line
    {
        public int Start;
        public int Count;
        public double Used;   // resolved main sizes plus gaps
        public double Cross;  // tallest item
        public double Pos;
        public double Size;
    }

    readonly struct OrderComparer : IComparer<Item>
    {
        public int Compare(Item x, Item y)
        {
            var c = x.State.Order.CompareTo(y.State.Order);
            return c != 0 ? c : x.Index.CompareTo(y.Index);
        }
    }


    readonly Dictionary<Element, ChildState> states = new(ReferenceEqualityComparer.Instance);
    ChildState[] ordered = [];
    int orderedCount;
    bool childrenDirty = true;

    Item[] items = new Item[8];
    int itemCount;
    Line[] lines = new Line[4];
    int lineCount;

    // The resolved pass and what it was resolved for.
    bool passValid;
    double passMain = double.NaN;
    double passCross = double.NaN;
    double passMainExtent;
    double passCrossExtent;

    /// <summary>How many times the flex was resolved. Exposed for tests, which assert on caching.</summary>
    internal int ResolveCount { get; private set; }


    void InvalidateFlex(bool clearChildCaches)
    {
        this.passValid = false;
        if (clearChildCaches)
            foreach (var state in this.states.Values)
                state.Forget();

        ((IView)this).InvalidateMeasure();
    }


    void OnChildFlexValuesChanged(Element child)
    {
        if (this.states.TryGetValue(child, out var state))
            state.ReadAttached(child);

        this.InvalidateFlex(clearChildCaches: false);
    }


    protected override void OnChildAdded(Element child)
    {
        base.OnChildAdded(child);
        if (child is not IView view)
            return;

        var state = new ChildState(view);
        state.ReadAttached(child);
        this.states[child] = state;

        if (child is VisualElement visual)
            visual.MeasureInvalidated += this.OnChildMeasureInvalidated;

        child.PropertyChanged += this.OnChildPropertyChanged;
        child.HandlerChanged += this.OnChildHandlerChanged;

        this.childrenDirty = true;
        this.passValid = false;
    }


    protected override void OnChildRemoved(Element child, int oldLogicalIndex)
    {
        base.OnChildRemoved(child, oldLogicalIndex);

        if (child is VisualElement visual)
            visual.MeasureInvalidated -= this.OnChildMeasureInvalidated;

        child.PropertyChanged -= this.OnChildPropertyChanged;
        child.HandlerChanged -= this.OnChildHandlerChanged;

        this.states.Remove(child);
        this.childrenDirty = true;
        this.passValid = false;
    }


    void OnChildMeasureInvalidated(object? sender, EventArgs e)
    {
        // Only this child is measured again. Its siblings keep their measurements, which is the main
        // saving over FlexLayout, which measures all of them. MAUI already propagates the invalidation
        // to this layout, so there is nothing to request here.
        if (sender is Element element && this.states.TryGetValue(element, out var state))
            state.Forget();

        this.passValid = false;
    }


    void OnChildInvalidatedNatively(Element child)
    {
        if (this.states.TryGetValue(child, out var state))
            state.Forget();

        // Nothing is requested here: the native side is already re-laying out every ancestor, and each
        // ShinyFlexLayout above this one gets its own call from the same walk.
        this.passValid = false;
    }


    void OnChildHandlerChanged(object? sender, EventArgs e)
    {
        // A measurement taken before the native view existed is meaningless.
        if (sender is Element element && this.states.TryGetValue(element, out var state))
            state.Forget();

        this.passValid = false;
    }


    void OnChildPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Showing or hiding a child changes who is in the flow, which the cached pass cannot see.
        if (e.PropertyName == VisualElement.IsVisibleProperty.PropertyName)
            this.InvalidateFlex(clearChildCaches: false);
    }


    void EnsureOrderedChildren()
    {
        if (!this.childrenDirty)
            return;

        var count = this.Count;
        if (this.ordered.Length < count)
            this.ordered = new ChildState[Math.Max(count, this.ordered.Length * 2)];

        for (var i = 0; i < count; i++)
        {
            var view = this[i];
            if (view is not Element element || !this.states.TryGetValue(element, out var state))
            {
                // Not an Element, so OnChildAdded never saw it. It gets defaults and no invalidation hooks.
                state = new ChildState(view);
                if (view is Element e)
                    this.states[e] = state;
            }
            this.ordered[i] = state;
        }

        // Drop stale references so removed children are not kept alive by the buffer.
        Array.Clear(this.ordered, count, this.orderedCount > count ? this.orderedCount - count : 0);
        this.orderedCount = count;
        this.childrenDirty = false;
    }


    static bool Same(double a, double b) => a == b || Math.Abs(a - b) < Epsilon;

    static bool IsRow(FlexDirection direction) => direction is FlexDirection.Row or FlexDirection.RowReverse;


    Size MeasureFlex(double widthConstraint, double heightConstraint)
    {
        var padding = this.Padding;
        var view = (IView)this;

        // An explicit size is the box the children flow in, whatever the parent offered.
        var width = Dimension.IsExplicitSet(view.Width) ? view.Width : widthConstraint;
        var height = Dimension.IsExplicitSet(view.Height) ? view.Height : heightConstraint;
        var innerW = Math.Max(0, width - padding.HorizontalThickness);
        var innerH = Math.Max(0, height - padding.VerticalThickness);

        var row = IsRow(this.Direction);
        this.EnsurePass(row ? innerW : innerH, row ? innerH : innerW);

        var contentW = (row ? this.passMainExtent : this.passCrossExtent) + padding.HorizontalThickness;
        var contentH = (row ? this.passCrossExtent : this.passMainExtent) + padding.VerticalThickness;

        return new Size(
            LayoutManager.ResolveConstraints(widthConstraint, view.Width, contentW, view.MinimumWidth, view.MaximumWidth),
            LayoutManager.ResolveConstraints(heightConstraint, view.Height, contentH, view.MinimumHeight, view.MaximumHeight)
        );
    }


    Size ArrangeFlex(Rect bounds)
    {
        var padding = this.Padding;
        var innerW = Math.Max(0, bounds.Width - padding.HorizontalThickness);
        var innerH = Math.Max(0, bounds.Height - padding.VerticalThickness);

        var row = IsRow(this.Direction);
        var mainSize = row ? innerW : innerH;
        var crossSize = row ? innerH : innerW;

        // Arranging at the size the layout was measured at reuses the measure pass as it is.
        this.EnsurePass(mainSize, crossSize);
        this.Place(mainSize, crossSize);

        var rtl = ((IView)this).FlowDirection == FlowDirection.RightToLeft;
        var left = bounds.X + padding.Left;
        var top = bounds.Y + padding.Top;

        for (var i = 0; i < this.itemCount; i++)
        {
            ref var item = ref this.items[i];
            double x, y, w, h;
            if (row)
            {
                x = item.MainPos;
                y = item.CrossPos;
                w = item.Main;
                h = item.CrossSize;
            }
            else
            {
                x = item.CrossPos;
                y = item.MainPos;
                w = item.CrossSize;
                h = item.Main;
            }

            // The platform does not mirror a custom layout, so right-to-left is applied here. The cross
            // axis of a column layout runs along x, so it is mirrored too, as CSS does.
            if (rtl)
                x = innerW - x - w;

            item.State.View.Arrange(new Rect(left + x, top + y, Math.Max(0, w), Math.Max(0, h)));
        }

        return bounds.Size;
    }


    void EnsurePass(double mainAvail, double crossAvail)
    {
        if (this.passValid
            && this.IsMeasureCacheEnabled
            && !this.childrenDirty
            && Same(this.passMain, mainAvail)
            && Same(this.passCross, crossAvail))
            return;

        this.Resolve(mainAvail, crossAvail);
        this.passMain = mainAvail;
        this.passCross = crossAvail;
        this.passValid = true;
    }


    /// <summary>
    /// The part of the algorithm that measures children: basis, line breaking, grow/shrink, and cross
    /// sizes. It depends only on the available size, so its result is cached (see <see cref="EnsurePass"/>).
    /// </summary>
    void Resolve(double mainAvail, double crossAvail)
    {
        this.ResolveCount++;
        this.EnsureOrderedChildren();

        var row = IsRow(this.Direction);
        var mainGap = Math.Max(0, row ? this.ColumnSpacing : this.RowSpacing);
        var crossGap = Math.Max(0, row ? this.RowSpacing : this.ColumnSpacing);
        var bounded = !double.IsInfinity(mainAvail);
        var wrap = this.Wrap != FlexWrap.NoWrap && bounded;
        var useCache = this.IsMeasureCacheEnabled;

        // 1. Collect visible children.
        if (this.items.Length < this.orderedCount)
            this.items = new Item[Math.Max(this.orderedCount, this.items.Length * 2)];

        var count = 0;
        var anyOrder = false;
        for (var i = 0; i < this.orderedCount; i++)
        {
            var state = this.ordered[i];
            if (state.View.Visibility == Visibility.Collapsed)
                continue;

            anyOrder |= state.Order != 0;
            this.items[count++] = new Item { State = state, Index = i };
        }

        // Stale item slots past the new count would pin removed children, clear them
        if (count < this.itemCount)
            Array.Clear(this.items, count, this.itemCount - count);
        this.itemCount = count;

        if (anyOrder)
            this.items.AsSpan(0, count).Sort(new OrderComparer());

        // 2. Starting sizes. Auto-basis children are measured at the available size, as FlexLayout does.
        //    Fixed-basis children are measured once, after flexing, since their main size is known.
        for (var i = 0; i < count; i++)
        {
            ref var item = ref this.items[i];
            var view = item.State.View;
            var margin = view.Margin;
            item.MarginMain = row ? margin.HorizontalThickness : margin.VerticalThickness;

            var min = row ? view.MinimumWidth : view.MinimumHeight;
            var max = row ? view.MaximumWidth : view.MaximumHeight;
            var maxCross = row ? view.MaximumHeight : view.MaximumWidth;
            item.MinMain = (Dimension.IsMinimumSet(min) ? min : 0) + item.MarginMain;
            item.MaxMain = Dimension.IsMaximumSet(max) ? max + item.MarginMain : double.PositiveInfinity;
            item.MaxCross = Dimension.IsMaximumSet(maxCross)
                ? maxCross + (row ? margin.VerticalThickness : margin.HorizontalThickness)
                : double.PositiveInfinity;
            item.ExplicitCross = Dimension.IsExplicitSet(row ? view.Height : view.Width);

            var state = item.State;
            if (state.BasisAuto || (state.BasisRelative && !bounded))
            {
                var size = row
                    ? item.State.Measure(mainAvail, crossAvail, useCache)
                    : item.State.Measure(crossAvail, mainAvail, useCache);

                item.FlexBase = row ? size.Width : size.Height;
                item.Cross = row ? size.Height : size.Width;
                item.MeasuredMain = item.FlexBase;
            }
            else
            {
                var inner = state.BasisRelative ? state.BasisLength * mainAvail : state.BasisLength;
                item.FlexBase = Math.Max(0, inner) + item.MarginMain;
                item.MeasuredMain = double.NaN;
            }

            item.Hyp = Math.Clamp(item.FlexBase, item.MinMain, Math.Max(item.MinMain, item.MaxMain));
            item.Main = item.Hyp;
        }

        // 3. Break into lines.
        this.lineCount = 0;
        var start = 0;
        var used = 0d;
        for (var i = 0; i < count; i++)
        {
            var hyp = this.items[i].Hyp;
            if (i == start)
            {
                used = hyp;
            }
            else if (wrap && used + mainGap + hyp > mainAvail + Epsilon)
            {
                this.AddLine(start, i - start);
                start = i;
                used = hyp;
            }
            else
            {
                used += mainGap + hyp;
            }
        }
        if (count > 0)
            this.AddLine(start, count - start);

        // 4. Flex each line, then settle cross sizes.
        var mainExtent = 0d;
        var crossExtent = 0d;
        for (var l = 0; l < this.lineCount; l++)
        {
            ref var line = ref this.lines[l];
            if (bounded)
                this.FlexLine(ref line, mainAvail, mainGap);

            var lineUsed = mainGap * (line.Count - 1);
            var lineCross = 0d;
            for (var i = line.Start; i < line.Start + line.Count; i++)
            {
                ref var item = ref this.items[i];

                // Measure again only if flexing actually changed the main size. For leaves the cache
                // usually answers this anyway: a child that grew into room it did not need gets the same
                // answer back.
                if (double.IsNaN(item.MeasuredMain) || !Same(item.MeasuredMain, item.Main))
                {
                    var size = row
                        ? item.State.Measure(item.Main, crossAvail, useCache)
                        : item.State.Measure(crossAvail, item.Main, useCache);

                    item.Cross = row ? size.Height : size.Width;
                    item.MeasuredMain = item.Main;
                }

                item.Cross = Math.Min(item.Cross, item.MaxCross);
                lineUsed += item.Main;
                lineCross = Math.Max(lineCross, item.Cross);
            }

            line.Used = lineUsed;
            line.Cross = lineCross;
            mainExtent = Math.Max(mainExtent, lineUsed);
            crossExtent += lineCross;
        }

        if (this.lineCount > 1)
            crossExtent += crossGap * (this.lineCount - 1);

        this.passMainExtent = mainExtent;
        this.passCrossExtent = crossExtent;
    }


    void AddLine(int start, int count)
    {
        if (this.lineCount == this.lines.Length)
            Array.Resize(ref this.lines, this.lines.Length * 2);

        this.lines[this.lineCount++] = new Line { Start = start, Count = count };
    }


    /// <summary>
    /// CSS "resolve flexible lengths" (§9.7): share out the free space by grow or weighted shrink, clamp
    /// to min/max, freeze the children that hit a limit, and repeat. It settles in at most one round per
    /// child and usually in one.
    /// </summary>
    void FlexLine(ref Line line, double mainAvail, double mainGap)
    {
        var end = line.Start + line.Count;
        var available = mainAvail - mainGap * (line.Count - 1);

        var sumHyp = 0d;
        for (var i = line.Start; i < end; i++)
            sumHyp += this.items[i].Hyp;

        var initialFree = available - sumHyp;
        if (Math.Abs(initialFree) < Epsilon)
            return;

        var growing = initialFree > 0;
        var unfrozen = 0;
        for (var i = line.Start; i < end; i++)
        {
            ref var item = ref this.items[i];
            var factor = growing ? item.State.Grow : item.State.Shrink;
            item.Frozen = factor <= 0
                || (growing && item.FlexBase > item.Hyp)    // held down by max, can't grow
                || (!growing && item.FlexBase < item.Hyp);  // held up by min, can't shrink
            if (!item.Frozen)
                unfrozen++;
        }

        for (var round = 0; unfrozen > 0 && round <= line.Count; round++)
        {
            var remaining = available;
            var sumFactor = 0d;
            var sumScaled = 0d;
            for (var i = line.Start; i < end; i++)
            {
                ref var item = ref this.items[i];
                if (item.Frozen)
                {
                    remaining -= item.Main;
                    continue;
                }

                remaining -= item.FlexBase;
                if (growing)
                    sumFactor += item.State.Grow;
                else
                {
                    sumFactor += item.State.Shrink;
                    sumScaled += item.State.Shrink * Math.Max(0, item.FlexBase - item.MarginMain);
                }
            }

            // Factors that add up to less than 1 take only that fraction of the free space (CSS).
            if (sumFactor < 1)
            {
                var partial = initialFree * sumFactor;
                if (Math.Abs(partial) < Math.Abs(remaining))
                    remaining = partial;
            }

            var violation = 0d;
            for (var i = line.Start; i < end; i++)
            {
                ref var item = ref this.items[i];
                if (item.Frozen)
                    continue;

                double target;
                if (growing)
                {
                    target = item.FlexBase + remaining * (item.State.Grow / sumFactor);
                }
                else
                {
                    var scaled = item.State.Shrink * Math.Max(0, item.FlexBase - item.MarginMain);
                    target = sumScaled > 0
                        ? item.FlexBase + remaining * (scaled / sumScaled)
                        : item.FlexBase;
                }

                var clamped = Math.Clamp(target, item.MinMain, Math.Max(item.MinMain, item.MaxMain));
                violation += clamped - target;
                item.Main = clamped;
            }

            // No clamping, so the distribution is final.
            if (Math.Abs(violation) < Epsilon)
                break;

            // Freeze the children that went the way the total did, then share out the rest again.
            for (var i = line.Start; i < end; i++)
            {
                ref var item = ref this.items[i];
                if (item.Frozen)
                    continue;

                var atMin = item.Main <= item.MinMain + Epsilon;
                var atMax = item.Main >= item.MaxMain - Epsilon;
                if ((violation > 0 && atMin) || (violation < 0 && atMax))
                {
                    item.Frozen = true;
                    unfrozen--;
                }
            }
        }
    }


    /// <summary>
    /// The part of the algorithm that only positions children: line placement (AlignContent),
    /// JustifyContent, reversal and cross alignment. Arithmetic only, run on every arrange.
    /// </summary>
    void Place(double mainSize, double crossSize)
    {
        var direction = this.Direction;
        var row = IsRow(direction);
        var mainReverse = direction is FlexDirection.RowReverse or FlexDirection.ColumnReverse;
        var wrapMode = this.Wrap;
        var wrapReverse = wrapMode == FlexWrap.Reverse;
        var mainGap = Math.Max(0, row ? this.ColumnSpacing : this.RowSpacing);
        var crossGap = Math.Max(0, row ? this.RowSpacing : this.ColumnSpacing);
        var justify = this.JustifyContent;
        var alignItems = this.AlignItems;

        // Lines across the cross axis.
        if (wrapMode == FlexWrap.NoWrap)
        {
            // A single-line container's line is the container (CSS), which is what lets AlignItems center
            // children in the full height of a row.
            if (this.lineCount > 0)
            {
                this.lines[0].Pos = 0;
                this.lines[0].Size = crossSize;
            }
        }
        else
        {
            var total = crossGap * Math.Max(0, this.lineCount - 1);
            for (var l = 0; l < this.lineCount; l++)
                total += this.lines[l].Cross;

            var free = crossSize - total;
            var n = this.lineCount;
            double offset = 0, between = 0, stretch = 0;
            switch (this.AlignContent)
            {
                case FlexAlignContent.End:
                    offset = free;
                    break;
                case FlexAlignContent.Center:
                    offset = free / 2;
                    break;
                case FlexAlignContent.Stretch when free > 0:
                    stretch = free / n;
                    break;
                case FlexAlignContent.SpaceBetween when free > 0 && n > 1:
                    between = free / (n - 1);
                    break;
                case FlexAlignContent.SpaceAround:
                    if (free > 0)
                    {
                        between = free / n;
                        offset = between / 2;
                    }
                    else offset = free / 2;
                    break;
                case FlexAlignContent.SpaceEvenly:
                    if (free > 0)
                    {
                        between = free / (n + 1);
                        offset = between;
                    }
                    else offset = free / 2;
                    break;
            }

            var pos = offset;
            for (var l = 0; l < n; l++)
            {
                ref var line = ref this.lines[l];
                line.Size = line.Cross + stretch;
                line.Pos = wrapReverse ? crossSize - pos - line.Size : pos;
                pos += line.Size + crossGap + between;
            }
        }

        // Children along each line, and across it.
        for (var l = 0; l < this.lineCount; l++)
        {
            ref var line = ref this.lines[l];
            var end = line.Start + line.Count;
            var free = mainSize - line.Used;
            var n = line.Count;
            double offset = 0, between = 0;
            switch (justify)
            {
                case FlexJustify.End:
                    offset = free;
                    break;
                case FlexJustify.Center:
                    offset = free / 2;
                    break;
                case FlexJustify.SpaceBetween when free > 0 && n > 1:
                    between = free / (n - 1);
                    break;
                case FlexJustify.SpaceAround:
                    if (free > 0)
                    {
                        between = free / n;
                        offset = between / 2;
                    }
                    else offset = free / 2;
                    break;
                case FlexJustify.SpaceEvenly:
                    if (free > 0)
                    {
                        between = free / (n + 1);
                        offset = between;
                    }
                    else offset = free / 2;
                    break;
            }

            var pos = offset;
            for (var i = line.Start; i < end; i++)
            {
                ref var item = ref this.items[i];
                item.MainPos = mainReverse ? mainSize - pos - item.Main : pos;
                pos += item.Main + mainGap + between;

                var align = item.State.AlignSelf switch
                {
                    FlexAlignSelf.Stretch => FlexAlignItems.Stretch,
                    FlexAlignSelf.Start => FlexAlignItems.Start,
                    FlexAlignSelf.End => FlexAlignItems.End,
                    FlexAlignSelf.Center => FlexAlignItems.Center,
                    _ => alignItems
                };

                // Stretch never overrides a size the child asked for; it sits at the start instead (CSS).
                if (align == FlexAlignItems.Stretch && item.ExplicitCross)
                    align = FlexAlignItems.Start;

                // WrapReverse flips which edge of the line is "start".
                if (wrapReverse)
                {
                    if (align == FlexAlignItems.Start)
                        align = FlexAlignItems.End;
                    else if (align == FlexAlignItems.End)
                        align = FlexAlignItems.Start;
                }

                double size, within;
                switch (align)
                {
                    case FlexAlignItems.Stretch:
                        size = Math.Min(line.Size, item.MaxCross);
                        within = 0;
                        break;
                    case FlexAlignItems.End:
                        size = item.Cross;
                        within = line.Size - size;
                        break;
                    case FlexAlignItems.Center:
                        size = item.Cross;
                        within = (line.Size - size) / 2;
                        break;
                    default:
                        size = item.Cross;
                        within = 0;
                        break;
                }

                item.CrossSize = size;
                item.CrossPos = line.Pos + within;
            }
        }
    }
}

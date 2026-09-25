using Microsoft.Maui.Layouts;
using Microsoft.Maui.Primitives;

namespace Shiny.Maui.Controls;

/// <summary>
/// A layout that follows Yoga (<see href="https://www.yogalayout.dev/"/>), the flexbox engine behind
/// React Native. It is written in C# and needs no native library.
/// </summary>
/// <remarks>
/// <para>
/// Yoga is flexbox with different defaults and a few extras. Defaults: <see cref="FlexDirection"/> is
/// Column, <c>FlexShrink</c> is 0, <see cref="AlignContent"/> is FlexStart, and the minimum size is 0,
/// never "auto". Extras: absolute positioning with Left/Top/Right/Bottom/Start/End insets, percentage
/// sizes, <c>AspectRatio</c>, and auto margins. Every child setting is an attached property.
/// </para>
/// <para>
/// The same model ships as a Blazor component. There it is emitted as CSS with Yoga's defaults, so a
/// layout written once gives the same boxes on both hosts.
/// </para>
/// <para>
/// Each pass measures children once to find their basis and again only when flexing changed their
/// main size. A measure followed by an arrange at the same size, which is the usual order, resolves
/// the flex once. Arrange reuses the measure's result.
/// </para>
/// <para>
/// MAUI exposes no text baselines, so <see cref="YogaAlign.Baseline"/> uses each child's bottom edge.
/// That is what Yoga does for a node without a baseline function.
/// </para>
/// </remarks>
public partial class YogaLayout : Layout
{
    const double Epsilon = 0.01;

    sealed class YogaManager(YogaLayout yoga) : ILayoutManager
    {
        public Size Measure(double widthConstraint, double heightConstraint) => yoga.MeasureYoga(widthConstraint, heightConstraint);
        public Size ArrangeChildren(Rect bounds) => yoga.ArrangeYoga(bounds);
    }

    protected override ILayoutManager CreateLayoutManager() => new YogaManager(this);


    /// <summary>One child's working values for the current pass. Sizes exclude margins.</summary>
    struct Node
    {
        public IView View;
        public BindableObject? Bindable;
        public bool Absolute;
        public bool Relative;

        public double Grow;
        public double Shrink;
        public YogaAlign AlignSelf;
        public double Aspect;

        // Physical margins; auto edges are zero here and share out free space in Place.
        public Thickness Margin;
        public bool AutoLeft, AutoTop, AutoRight, AutoBottom;

        // Resolved insets (NaN = not set), Start/End already mapped to Left/Right.
        public double InsetLeft, InsetTop, InsetRight, InsetBottom;

        // Resolved sizes (NaN = auto) and limits.
        public double Width, Height;
        public double MinWidth, MaxWidth, MinHeight, MaxHeight;

        // Main/cross working values.
        public double Base;
        public double Hyp;
        public double Main;
        public double Cross;
        public double MeasuredMain;   // main size Cross was measured at; NaN = not measured
        public bool Frozen;

        // Output, as a physical inner box relative to the content box.
        public double X, Y, W, H;
    }

    struct Line
    {
        public int Start;
        public int Count;
        public double Used;       // outer main sizes plus gaps
        public double Cross;      // tallest outer cross size
        public double Ascent;     // baseline alignment
        public double Pos;
        public double Size;
    }


    Node[] nodes = new Node[8];
    int flowCount;
    int absoluteCount;            // stored after the flow nodes
    Line[] lines = new Line[4];
    int lineCount;

    double contentMain;
    double contentCross;

    // A measure resolves; an arrange straight after it at the same size reuses that. Anything else
    // resolves again, so a child that changed without telling anyone can never leave a stale layout.
    bool measureFresh;
    double measuredMainAvail = double.NaN;
    double measuredCrossAvail = double.NaN;

    /// <summary>How many times the flex was resolved. Exposed for tests.</summary>
    internal int ResolveCount { get; private set; }


    void Invalidate()
    {
        this.measureFresh = false;
        ((IView)this).InvalidateMeasure();
    }


    protected override void OnChildAdded(Element child)
    {
        base.OnChildAdded(child);
        this.measureFresh = false;
    }


    protected override void OnChildRemoved(Element child, int oldLogicalIndex)
    {
        base.OnChildRemoved(child, oldLogicalIndex);
        this.measureFresh = false;
    }


    static bool Same(double a, double b) => a == b || Math.Abs(a - b) < Epsilon;

    static bool IsRow(YogaFlexDirection direction) => direction is YogaFlexDirection.Row or YogaFlexDirection.RowReverse;

    static double Clamp(double value, double min, double max)
    {
        if (!double.IsNaN(max) && value > max)
            value = max;
        if (!double.IsNaN(min) && value < min)
            value = min;
        return Math.Max(0, value);
    }

    static double Or(double value, double fallback) => double.IsNaN(value) ? fallback : value;

    // The effective direction, so a layout inside a right-to-left page follows it without being told.
    // The platform does not mirror a custom layout, so right-to-left is applied here.
    bool IsRtl => (((IVisualElementController)this).EffectiveFlowDirection & EffectiveFlowDirection.RightToLeft) != 0;

    double MainGap(bool row) => Math.Max(0, Or(row ? this.ColumnGap : this.RowGap, this.Gap));

    double CrossGap(bool row) => Math.Max(0, Or(row ? this.RowGap : this.ColumnGap, this.Gap));


    Size MeasureYoga(double widthConstraint, double heightConstraint)
    {
        var padding = this.Padding;
        var view = (IView)this;

        var width = Dimension.IsExplicitSet(view.Width) ? view.Width : widthConstraint;
        var height = Dimension.IsExplicitSet(view.Height) ? view.Height : heightConstraint;
        var innerW = Math.Max(0, width - padding.HorizontalThickness);
        var innerH = Math.Max(0, height - padding.VerticalThickness);

        var row = IsRow(this.FlexDirection);
        var mainAvail = row ? innerW : innerH;
        var crossAvail = row ? innerH : innerW;
        this.Resolve(innerW, innerH);
        this.measureFresh = true;
        this.measuredMainAvail = mainAvail;
        this.measuredCrossAvail = crossAvail;

        var contentW = (row ? this.contentMain : this.contentCross) + padding.HorizontalThickness;
        var contentH = (row ? this.contentCross : this.contentMain) + padding.VerticalThickness;

        return new Size(
            LayoutManager.ResolveConstraints(widthConstraint, view.Width, contentW, view.MinimumWidth, view.MaximumWidth),
            LayoutManager.ResolveConstraints(heightConstraint, view.Height, contentH, view.MinimumHeight, view.MaximumHeight)
        );
    }


    Size ArrangeYoga(Rect bounds)
    {
        var padding = this.Padding;
        var innerW = Math.Max(0, bounds.Width - padding.HorizontalThickness);
        var innerH = Math.Max(0, bounds.Height - padding.VerticalThickness);

        var row = IsRow(this.FlexDirection);
        var mainSize = row ? innerW : innerH;
        var crossSize = row ? innerH : innerW;

        if (!this.measureFresh || !Same(this.measuredMainAvail, mainSize) || !Same(this.measuredCrossAvail, crossSize))
            this.Resolve(innerW, innerH);

        this.measureFresh = false;
        this.Place(innerW, innerH);
        this.PlaceAbsolute(bounds.Width, bounds.Height, innerW, innerH);

        var left = bounds.X + padding.Left;
        var top = bounds.Y + padding.Top;
        for (var i = 0; i < this.flowCount + this.absoluteCount; i++)
        {
            ref var node = ref this.nodes[i];
            var m = node.Margin;

            // Absolute children are placed against the padding box, flow children against the content box.
            var ox = node.Absolute ? bounds.X : left;
            var oy = node.Absolute ? bounds.Y : top;

            // MAUI takes the margin box and removes the margin itself.
            node.View.Arrange(new Rect(
                ox + node.X - m.Left,
                oy + node.Y - m.Top,
                Math.Max(0, node.W) + m.HorizontalThickness,
                Math.Max(0, node.H) + m.VerticalThickness
            ));
        }

        // Display="None" children still have a native view; give them no room.
        for (var i = 0; i < this.Count; i++)
        {
            var child = this[i];
            if (child is BindableObject b && GetDisplay(b) == YogaDisplay.None && child.Visibility != Visibility.Collapsed)
                child.Arrange(new Rect(bounds.X, bounds.Y, 0, 0));
        }

        return bounds.Size;
    }


    /// <summary>Reads every child's style and resolves percentages against the content box.</summary>
    void Collect(double innerW, double innerH, double boxW, double boxH)
    {
        var rtl = this.IsRtl;
        var count = this.Count;
        if (this.nodes.Length < count)
            this.nodes = new Node[Math.Max(count, this.nodes.Length * 2)];

        // Flow children fill from the front, absolute ones are collected behind them.
        var flow = 0;
        var absolute = 0;

        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < count; i++)
            {
                var view = this[i];
                if (view.Visibility == Visibility.Collapsed)
                    continue;

                var b = view as BindableObject;
                if (b is not null && GetDisplay(b) == YogaDisplay.None)
                    continue;

                var position = b is null ? YogaPositionType.Relative : GetPositionType(b);
                var isAbsolute = position == YogaPositionType.Absolute;
                if (isAbsolute != (pass == 1))
                    continue;

                // Percentages on an absolute child are taken of the padding box, which is what it is placed in.
                var refW = isAbsolute ? boxW : innerW;
                var refH = isAbsolute ? boxH : innerH;

                ref var node = ref this.nodes[isAbsolute ? flow + absolute : flow];
                node = new Node
                {
                    View = view,
                    Bindable = b,
                    Absolute = isAbsolute,
                    Relative = position == YogaPositionType.Relative,
                    Grow = 0,
                    Shrink = 0,
                    AlignSelf = YogaAlign.Auto,
                    Aspect = double.NaN,
                    Margin = view.Margin,
                    InsetLeft = double.NaN,
                    InsetTop = double.NaN,
                    InsetRight = double.NaN,
                    InsetBottom = double.NaN,
                    Width = Dimension.IsExplicitSet(view.Width) ? view.Width : double.NaN,
                    Height = Dimension.IsExplicitSet(view.Height) ? view.Height : double.NaN,
                    MinWidth = Dimension.IsMinimumSet(view.MinimumWidth) ? view.MinimumWidth : double.NaN,
                    MinHeight = Dimension.IsMinimumSet(view.MinimumHeight) ? view.MinimumHeight : double.NaN,
                    MaxWidth = Dimension.IsMaximumSet(view.MaximumWidth) ? view.MaximumWidth : double.NaN,
                    MaxHeight = Dimension.IsMaximumSet(view.MaximumHeight) ? view.MaximumHeight : double.NaN,
                    MeasuredMain = double.NaN
                };

                if (b is not null)
                {
                    node.Grow = Math.Max(0, GetFlexGrow(b));
                    node.Shrink = Math.Max(0, GetFlexShrink(b));
                    node.AlignSelf = GetAlignSelf(b);
                    var aspect = GetAspectRatio(b);
                    node.Aspect = aspect > 0 ? aspect : double.NaN;

                    var autos = GetAutoMargins(b);
                    var startEdge = rtl ? YogaEdges.Right : YogaEdges.Left;
                    var endEdge = rtl ? YogaEdges.Left : YogaEdges.Right;
                    if ((autos & YogaEdges.Start) != 0)
                        autos |= startEdge;
                    if ((autos & YogaEdges.End) != 0)
                        autos |= endEdge;
                    node.AutoLeft = (autos & YogaEdges.Left) != 0;
                    node.AutoTop = (autos & YogaEdges.Top) != 0;
                    node.AutoRight = (autos & YogaEdges.Right) != 0;
                    node.AutoBottom = (autos & YogaEdges.Bottom) != 0;
                    var physical = node.Margin;
                    node.Margin = new Thickness(
                        node.AutoLeft ? 0 : physical.Left,
                        node.AutoTop ? 0 : physical.Top,
                        node.AutoRight ? 0 : physical.Right,
                        node.AutoBottom ? 0 : physical.Bottom
                    );

                    var start = GetStart(b).Resolve(refW);
                    var end = GetEnd(b).Resolve(refW);
                    node.InsetLeft = Or(rtl ? end : start, GetLeft(b).Resolve(refW));
                    node.InsetRight = Or(rtl ? start : end, GetRight(b).Resolve(refW));
                    node.InsetTop = GetTop(b).Resolve(refH);
                    node.InsetBottom = GetBottom(b).Resolve(refH);

                    node.Width = Or(GetNodeWidth(b).Resolve(refW), node.Width);
                    node.Height = Or(GetNodeHeight(b).Resolve(refH), node.Height);
                    node.MinWidth = Or(GetMinWidth(b).Resolve(refW), node.MinWidth);
                    node.MinHeight = Or(GetMinHeight(b).Resolve(refH), node.MinHeight);
                    node.MaxWidth = Or(GetMaxWidth(b).Resolve(refW), node.MaxWidth);
                    node.MaxHeight = Or(GetMaxHeight(b).Resolve(refH), node.MaxHeight);
                }

                // One known dimension and an aspect ratio give the other.
                if (!double.IsNaN(node.Aspect))
                {
                    if (!double.IsNaN(node.Width) && double.IsNaN(node.Height))
                        node.Height = node.Width / node.Aspect;
                    else if (!double.IsNaN(node.Height) && double.IsNaN(node.Width))
                        node.Width = node.Height * node.Aspect;
                }

                if (!double.IsNaN(node.Width))
                    node.Width = Clamp(node.Width, node.MinWidth, node.MaxWidth);
                if (!double.IsNaN(node.Height))
                    node.Height = Clamp(node.Height, node.MinHeight, node.MaxHeight);

                if (isAbsolute)
                    absolute++;
                else
                    flow++;
            }
        }

        // Drop references to children that are gone.
        var used = flow + absolute;
        if (used < this.flowCount + this.absoluteCount)
            Array.Clear(this.nodes, used, this.flowCount + this.absoluteCount - used);

        this.flowCount = flow;
        this.absoluteCount = absolute;
    }


    /// <summary>Measures a child's inner box: constraints and result exclude its margin.</summary>
    static Size MeasureInner(ref Node node, double width, double height)
    {
        var m = node.Margin;
        var size = node.View.Measure(
            double.IsFinite(width) ? Math.Max(0, width) + m.HorizontalThickness : double.PositiveInfinity,
            double.IsFinite(height) ? Math.Max(0, height) + m.VerticalThickness : double.PositiveInfinity
        );
        return new Size(
            Math.Max(0, size.Width - m.HorizontalThickness),
            Math.Max(0, size.Height - m.VerticalThickness)
        );
    }


    /// <summary>
    /// Everything that needs measuring: basis, lines, flexible lengths and cross sizes. Depends only on
    /// the available content size; <see cref="Place"/> then positions from the result.
    /// </summary>
    void Resolve(double innerW, double innerH)
    {
        this.ResolveCount++;
        var padding = this.Padding;
        this.Collect(innerW, innerH, innerW + padding.HorizontalThickness, innerH + padding.VerticalThickness);

        var row = IsRow(this.FlexDirection);
        var mainAvail = row ? innerW : innerH;
        var crossAvail = row ? innerH : innerW;
        var mainGap = this.MainGap(row);
        var crossGap = this.CrossGap(row);
        var bounded = double.IsFinite(mainAvail);
        var wrap = this.FlexWrap != YogaWrap.NoWrap && bounded;
        var count = this.flowCount;

        // 1. Flex basis and hypothetical main size.
        for (var i = 0; i < count; i++)
        {
            ref var node = ref this.nodes[i];
            var marginMain = row ? node.Margin.HorizontalThickness : node.Margin.VerticalThickness;
            var marginCross = row ? node.Margin.VerticalThickness : node.Margin.HorizontalThickness;

            // A stretched child in a single line already knows its cross size: the container's. With an
            // aspect ratio that settles the main size too, so the image-card case needs no measuring.
            if (!double.IsNaN(node.Aspect)
                && double.IsNaN(node.Width) && double.IsNaN(node.Height)
                && this.FlexWrap == YogaWrap.NoWrap
                && double.IsFinite(crossAvail)
                && this.AlignOf(ref node) == YogaAlign.Stretch
                && !(row ? node.AutoTop || node.AutoBottom : node.AutoLeft || node.AutoRight))
            {
                var stretched = Math.Max(0, crossAvail - marginCross);
                if (row)
                {
                    node.Height = Clamp(stretched, node.MinHeight, node.MaxHeight);
                    node.Width = Clamp(node.Height * node.Aspect, node.MinWidth, node.MaxWidth);
                }
                else
                {
                    node.Width = Clamp(stretched, node.MinWidth, node.MaxWidth);
                    node.Height = Clamp(node.Width / node.Aspect, node.MinHeight, node.MaxHeight);
                }
            }

            var explicitMain = row ? node.Width : node.Height;
            var explicitCross = row ? node.Height : node.Width;

            var basis = node.Bindable is null ? double.NaN : GetFlexBasis(node.Bindable).Resolve(mainAvail);
            if (!double.IsNaN(basis))
            {
                node.Base = Math.Max(0, basis);
            }
            else if (!double.IsNaN(explicitMain))
            {
                node.Base = explicitMain;
            }
            else
            {
                var mainC = mainAvail - marginMain;
                var crossC = Or(explicitCross, crossAvail - marginCross);
                var size = row ? MeasureInner(ref node, mainC, crossC) : MeasureInner(ref node, crossC, mainC);
                node.Base = row ? size.Width : size.Height;
                node.Cross = row ? size.Height : size.Width;
                node.MeasuredMain = node.Base;
            }

            node.Hyp = Clamp(node.Base, row ? node.MinWidth : node.MinHeight, row ? node.MaxWidth : node.MaxHeight);
            node.Main = node.Hyp;
        }

        // 2. Lines.
        this.lineCount = 0;
        var start = 0;
        var used = 0d;
        for (var i = 0; i < count; i++)
        {
            ref var node = ref this.nodes[i];
            var outer = node.Hyp + (row ? node.Margin.HorizontalThickness : node.Margin.VerticalThickness);
            if (i == start)
            {
                used = outer;
            }
            else if (wrap && used + mainGap + outer > mainAvail + Epsilon)
            {
                this.AddLine(start, i - start);
                start = i;
                used = outer;
            }
            else
            {
                used += mainGap + outer;
            }
        }
        if (count > 0)
            this.AddLine(start, count - start);

        // 3. Flexible lengths, then cross sizes.
        var mainExtent = 0d;
        var crossExtent = 0d;
        for (var l = 0; l < this.lineCount; l++)
        {
            ref var line = ref this.lines[l];
            if (bounded)
                this.FlexLine(ref line, mainAvail, mainGap, row);

            var lineUsed = mainGap * (line.Count - 1);
            var lineCross = 0d;
            var ascent = 0d;
            var descent = 0d;
            for (var i = line.Start; i < line.Start + line.Count; i++)
            {
                ref var node = ref this.nodes[i];
                var marginCross = row ? node.Margin.VerticalThickness : node.Margin.HorizontalThickness;
                var explicitCross = row ? node.Height : node.Width;

                if (!double.IsNaN(explicitCross))
                {
                    node.Cross = explicitCross;
                }
                else if (!double.IsNaN(node.Aspect))
                {
                    node.Cross = row ? node.Main / node.Aspect : node.Main * node.Aspect;
                }
                else if (double.IsNaN(node.MeasuredMain) || !Same(node.MeasuredMain, node.Main))
                {
                    // The main size is settled; measure the cross size at it.
                    var crossC = crossAvail - marginCross;
                    var size = row ? MeasureInner(ref node, node.Main, crossC) : MeasureInner(ref node, crossC, node.Main);
                    node.Cross = row ? size.Height : size.Width;
                    node.MeasuredMain = node.Main;
                }

                node.Cross = Clamp(node.Cross, row ? node.MinHeight : node.MinWidth, row ? node.MaxHeight : node.MaxWidth);

                lineUsed += node.Main + (row ? node.Margin.HorizontalThickness : node.Margin.VerticalThickness);
                lineCross = Math.Max(lineCross, node.Cross + marginCross);

                if (row && this.AlignOf(ref node) == YogaAlign.Baseline)
                {
                    ascent = Math.Max(ascent, node.Margin.Top + node.Cross);
                    descent = Math.Max(descent, node.Margin.Bottom);
                }
            }

            line.Used = lineUsed;
            line.Ascent = ascent;
            line.Cross = Math.Max(lineCross, ascent + descent);
            mainExtent = Math.Max(mainExtent, lineUsed);
            crossExtent += line.Cross;
        }

        if (this.lineCount > 1)
            crossExtent += crossGap * (this.lineCount - 1);

        this.contentMain = mainExtent;
        this.contentCross = crossExtent;
    }


    YogaAlign AlignOf(ref Node node)
    {
        var align = node.AlignSelf == YogaAlign.Auto ? this.AlignItems : node.AlignSelf;
        return align is YogaAlign.SpaceBetween or YogaAlign.SpaceAround or YogaAlign.SpaceEvenly or YogaAlign.Auto
            ? YogaAlign.FlexStart
            : align;
    }


    void AddLine(int start, int count)
    {
        if (this.lineCount == this.lines.Length)
            Array.Resize(ref this.lines, this.lines.Length * 2);

        this.lines[this.lineCount++] = new Line { Start = start, Count = count };
    }


    /// <summary>
    /// Resolve flexible lengths (CSS §9.7): share out the free space by grow or weighted shrink, clamp to
    /// min/max, freeze whoever hit a limit, and go again. Settles in at most one round per child.
    /// </summary>
    void FlexLine(ref Line line, double mainAvail, double mainGap, bool row)
    {
        var end = line.Start + line.Count;
        var available = mainAvail - mainGap * (line.Count - 1);

        var sumHyp = 0d;
        for (var i = line.Start; i < end; i++)
        {
            ref var node = ref this.nodes[i];
            available -= row ? node.Margin.HorizontalThickness : node.Margin.VerticalThickness;
            sumHyp += node.Hyp;
        }

        var initialFree = available - sumHyp;
        if (Math.Abs(initialFree) < Epsilon)
            return;

        var growing = initialFree > 0;
        var unfrozen = 0;
        for (var i = line.Start; i < end; i++)
        {
            ref var node = ref this.nodes[i];
            var factor = growing ? node.Grow : node.Shrink;
            node.Frozen = factor <= 0
                || (growing && node.Base > node.Hyp)
                || (!growing && node.Base < node.Hyp);
            if (!node.Frozen)
                unfrozen++;
        }

        for (var round = 0; unfrozen > 0 && round <= line.Count; round++)
        {
            var remaining = available;
            var sumFactor = 0d;
            var sumScaled = 0d;
            for (var i = line.Start; i < end; i++)
            {
                ref var node = ref this.nodes[i];
                if (node.Frozen)
                {
                    remaining -= node.Main;
                    continue;
                }

                remaining -= node.Base;
                if (growing)
                {
                    sumFactor += node.Grow;
                }
                else
                {
                    sumFactor += node.Shrink;
                    sumScaled += node.Shrink * node.Base;
                }
            }

            // Factors adding up to less than 1 take only that fraction of the free space.
            if (sumFactor < 1)
            {
                var partial = initialFree * sumFactor;
                if (Math.Abs(partial) < Math.Abs(remaining))
                    remaining = partial;
            }

            var violation = 0d;
            for (var i = line.Start; i < end; i++)
            {
                ref var node = ref this.nodes[i];
                if (node.Frozen)
                    continue;

                var target = growing
                    ? node.Base + remaining * (node.Grow / sumFactor)
                    : sumScaled > 0 ? node.Base + remaining * (node.Shrink * node.Base / sumScaled) : node.Base;

                var clamped = Clamp(target, row ? node.MinWidth : node.MinHeight, row ? node.MaxWidth : node.MaxHeight);
                violation += clamped - target;
                node.Main = clamped;
            }

            if (Math.Abs(violation) < Epsilon)
                break;

            for (var i = line.Start; i < end; i++)
            {
                ref var node = ref this.nodes[i];
                if (node.Frozen)
                    continue;

                var target = growing
                    ? node.Base + remaining * (node.Grow / sumFactor)
                    : sumScaled > 0 ? node.Base + remaining * (node.Shrink * node.Base / sumScaled) : node.Base;

                // Freeze the children that were clamped the same way as the total.
                if ((violation > 0 && node.Main > target + Epsilon) || (violation < 0 && node.Main < target - Epsilon))
                {
                    node.Frozen = true;
                    unfrozen--;
                }
            }
        }
    }


    /// <summary>
    /// Positions flow children in the content box: lines (AlignContent), JustifyContent, auto margins,
    /// cross alignment, reversal, right-to-left and relative insets. Arithmetic only.
    /// </summary>
    void Place(double innerW, double innerH)
    {
        var direction = this.FlexDirection;
        var row = IsRow(direction);
        var rtl = this.IsRtl;
        var mainSize = row ? innerW : innerH;
        var crossSize = row ? innerH : innerW;
        var mainGap = this.MainGap(row);
        var crossGap = this.CrossGap(row);
        var wrapMode = this.FlexWrap;

        // Each axis runs forwards or backwards physically; everything below is logical until the flip.
        var mainReversed = (direction is YogaFlexDirection.RowReverse or YogaFlexDirection.ColumnReverse) ^ (row && rtl);
        var crossReversed = (wrapMode == YogaWrap.WrapReverse) ^ (!row && rtl);

        // Lines across the cross axis.
        if (wrapMode == YogaWrap.NoWrap)
        {
            // A single-line container's line is the container itself.
            if (this.lineCount > 0)
            {
                this.lines[0].Pos = 0;
                this.lines[0].Size = crossSize;
            }
        }
        else
        {
            var n = this.lineCount;
            var total = crossGap * Math.Max(0, n - 1);
            for (var l = 0; l < n; l++)
                total += this.lines[l].Cross;

            var free = crossSize - total;
            double offset = 0, between = 0, stretch = 0;
            switch (this.AlignContent)
            {
                case YogaAlign.FlexEnd:
                    offset = free;
                    break;
                case YogaAlign.Center:
                    offset = free / 2;
                    break;
                case YogaAlign.Stretch when free > 0 && n > 0:
                    stretch = free / n;
                    break;
                case YogaAlign.SpaceBetween when free > 0 && n > 1:
                    between = free / (n - 1);
                    break;
                case YogaAlign.SpaceAround when free > 0 && n > 0:
                    between = free / n;
                    offset = between / 2;
                    break;
                case YogaAlign.SpaceEvenly when free > 0:
                    between = free / (n + 1);
                    offset = between;
                    break;
            }

            var pos = offset;
            for (var l = 0; l < n; l++)
            {
                ref var line = ref this.lines[l];
                line.Size = line.Cross + stretch;
                line.Pos = pos;
                pos += line.Size + crossGap + between;
            }
        }

        var justify = this.JustifyContent;
        for (var l = 0; l < this.lineCount; l++)
        {
            ref var line = ref this.lines[l];
            var end = line.Start + line.Count;
            var n = line.Count;

            // Main axis: auto margins take the free space first; justify only gets what is left.
            var free = mainSize - line.Used;
            var autoCount = 0;
            for (var i = line.Start; i < end; i++)
            {
                ref var node = ref this.nodes[i];
                autoCount += (row ? (node.AutoLeft ? 1 : 0) + (node.AutoRight ? 1 : 0) : (node.AutoTop ? 1 : 0) + (node.AutoBottom ? 1 : 0));
            }

            double offset = 0, between = 0, autoShare = 0;
            if (autoCount > 0)
            {
                autoShare = Math.Max(0, free) / autoCount;
            }
            else
            {
                switch (justify)
                {
                    case YogaJustify.FlexEnd:
                        offset = free;
                        break;
                    case YogaJustify.Center:
                        offset = free / 2;
                        break;
                    case YogaJustify.SpaceBetween when free > 0 && n > 1:
                        between = free / (n - 1);
                        break;
                    case YogaJustify.SpaceAround when free > 0:
                        between = free / n;
                        offset = between / 2;
                        break;
                    case YogaJustify.SpaceEvenly when free > 0:
                        between = free / (n + 1);
                        offset = between;
                        break;
                }
            }

            var pos = offset;
            for (var i = line.Start; i < end; i++)
            {
                ref var node = ref this.nodes[i];

                // Leading/trailing are the logical start/end edges of this axis.
                var (leadMain, trailMain, leadAuto, trailAuto) = MainEdges(ref node, row, mainReversed);
                pos += leadMain + (leadAuto ? autoShare : 0);
                var mainPos = pos;
                pos += node.Main + trailMain + (trailAuto ? autoShare : 0) + mainGap + between;

                // Cross axis.
                var (leadCross, trailCross, leadCrossAuto, trailCrossAuto) = CrossEdges(ref node, row, crossReversed);
                var align = this.AlignOf(ref node);
                var explicitCross = !double.IsNaN(row ? node.Height : node.Width) || !double.IsNaN(node.Aspect);
                var crossExtent = node.Cross;
                double within;

                if (leadCrossAuto || trailCrossAuto)
                {
                    var room = Math.Max(0, line.Size - node.Cross - leadCross - trailCross);
                    within = leadCross + (leadCrossAuto && trailCrossAuto ? room / 2 : leadCrossAuto ? room : 0);
                }
                else
                {
                    switch (align)
                    {
                        case YogaAlign.Stretch when !explicitCross:
                            crossExtent = Clamp(line.Size - leadCross - trailCross,
                                row ? node.MinHeight : node.MinWidth,
                                row ? node.MaxHeight : node.MaxWidth);
                            within = leadCross;
                            break;
                        case YogaAlign.FlexEnd:
                            within = line.Size - trailCross - node.Cross;
                            break;
                        case YogaAlign.Center:
                            within = leadCross + (line.Size - node.Cross - leadCross - trailCross) / 2;
                            break;
                        case YogaAlign.Baseline when row && !crossReversed:
                            within = line.Ascent - node.Cross;
                            break;
                        default:
                            within = leadCross;
                            break;
                    }
                }

                var crossPos = line.Pos + within;

                // Physical box.
                var mainPhys = mainReversed ? mainSize - mainPos - node.Main : mainPos;
                var crossPhys = crossReversed ? crossSize - crossPos - crossExtent : crossPos;
                if (row)
                {
                    node.X = mainPhys;
                    node.Y = crossPhys;
                    node.W = node.Main;
                    node.H = crossExtent;
                }
                else
                {
                    node.X = crossPhys;
                    node.Y = mainPhys;
                    node.W = crossExtent;
                    node.H = node.Main;
                }

                if (node.Relative)
                {
                    if (!double.IsNaN(node.InsetLeft))
                        node.X += node.InsetLeft;
                    else if (!double.IsNaN(node.InsetRight))
                        node.X -= node.InsetRight;

                    if (!double.IsNaN(node.InsetTop))
                        node.Y += node.InsetTop;
                    else if (!double.IsNaN(node.InsetBottom))
                        node.Y -= node.InsetBottom;
                }
            }
        }
    }


    /// <summary>The margins on the logical start and end of the main axis, and whether they are auto.</summary>
    static (double Lead, double Trail, bool LeadAuto, bool TrailAuto) MainEdges(ref Node node, bool row, bool reversed)
    {
        var m = node.Margin;
        if (row)
            return reversed
                ? (m.Right, m.Left, node.AutoRight, node.AutoLeft)
                : (m.Left, m.Right, node.AutoLeft, node.AutoRight);

        return reversed
            ? (m.Bottom, m.Top, node.AutoBottom, node.AutoTop)
            : (m.Top, m.Bottom, node.AutoTop, node.AutoBottom);
    }


    static (double Lead, double Trail, bool LeadAuto, bool TrailAuto) CrossEdges(ref Node node, bool row, bool reversed)
    {
        var m = node.Margin;
        if (row)
            return reversed
                ? (m.Bottom, m.Top, node.AutoBottom, node.AutoTop)
                : (m.Top, m.Bottom, node.AutoTop, node.AutoBottom);

        return reversed
            ? (m.Right, m.Left, node.AutoRight, node.AutoLeft)
            : (m.Left, m.Right, node.AutoLeft, node.AutoRight);
    }


    /// <summary>
    /// Absolute children leave the flow and are placed by their insets against the padding box. An axis
    /// with no insets falls back to where JustifyContent/AlignItems would put a lone child.
    /// </summary>
    void PlaceAbsolute(double boxW, double boxH, double innerW, double innerH)
    {
        if (this.absoluteCount == 0)
            return;

        var padding = this.Padding;
        var direction = this.FlexDirection;
        var row = IsRow(direction);
        var rtl = this.IsRtl;
        var mainReversed = (direction is YogaFlexDirection.RowReverse or YogaFlexDirection.ColumnReverse) ^ (row && rtl);
        var crossReversed = (this.FlexWrap == YogaWrap.WrapReverse) ^ (!row && rtl);

        for (var i = this.flowCount; i < this.flowCount + this.absoluteCount; i++)
        {
            ref var node = ref this.nodes[i];
            var m = node.Margin;

            var w = node.Width;
            var h = node.Height;
            if (double.IsNaN(w) && !double.IsNaN(node.InsetLeft) && !double.IsNaN(node.InsetRight))
                w = Clamp(boxW - node.InsetLeft - node.InsetRight - m.HorizontalThickness, node.MinWidth, node.MaxWidth);
            if (double.IsNaN(h) && !double.IsNaN(node.InsetTop) && !double.IsNaN(node.InsetBottom))
                h = Clamp(boxH - node.InsetTop - node.InsetBottom - m.VerticalThickness, node.MinHeight, node.MaxHeight);

            if (!double.IsNaN(node.Aspect))
            {
                if (!double.IsNaN(w) && double.IsNaN(h))
                    h = w / node.Aspect;
                else if (!double.IsNaN(h) && double.IsNaN(w))
                    w = h * node.Aspect;
            }

            if (double.IsNaN(w) || double.IsNaN(h))
            {
                var availW = boxW - m.HorizontalThickness - Or(node.InsetLeft, 0) - Or(node.InsetRight, 0);
                var availH = boxH - m.VerticalThickness - Or(node.InsetTop, 0) - Or(node.InsetBottom, 0);
                var size = MeasureInner(ref node, Or(w, availW), Or(h, availH));
                if (double.IsNaN(w))
                    w = Clamp(size.Width, node.MinWidth, node.MaxWidth);
                if (double.IsNaN(h))
                    h = Clamp(size.Height, node.MinHeight, node.MaxHeight);
            }

            node.W = w;
            node.H = h;

            // Static position on an axis with no insets: where a lone flow child would sit, in the content box.
            var align = this.AlignOf(ref node);
            var staticX = row
                ? StaticOffset(this.JustifyContent, innerW, w + m.HorizontalThickness, mainReversed)
                : StaticOffset(align, innerW, w + m.HorizontalThickness, crossReversed);
            var staticY = row
                ? StaticOffset(align, innerH, h + m.VerticalThickness, crossReversed)
                : StaticOffset(this.JustifyContent, innerH, h + m.VerticalThickness, mainReversed);

            node.X = !double.IsNaN(node.InsetLeft)
                ? node.InsetLeft + m.Left
                : !double.IsNaN(node.InsetRight)
                    ? boxW - node.InsetRight - m.Right - w
                    : padding.Left + staticX + m.Left;

            node.Y = !double.IsNaN(node.InsetTop)
                ? node.InsetTop + m.Top
                : !double.IsNaN(node.InsetBottom)
                    ? boxH - node.InsetBottom - m.Bottom - h
                    : padding.Top + staticY + m.Top;
        }
    }


    static double StaticOffset(YogaJustify justify, double size, double outer, bool reversed)
    {
        var free = size - outer;
        var logical = justify switch
        {
            YogaJustify.Center or YogaJustify.SpaceAround or YogaJustify.SpaceEvenly => free / 2,
            YogaJustify.FlexEnd => free,
            _ => 0
        };
        return reversed ? free - logical : logical;
    }


    static double StaticOffset(YogaAlign align, double size, double outer, bool reversed)
    {
        var free = size - outer;
        var logical = align switch
        {
            YogaAlign.Center => free / 2,
            YogaAlign.FlexEnd => free,
            _ => 0
        };
        return reversed ? free - logical : logical;
    }
}

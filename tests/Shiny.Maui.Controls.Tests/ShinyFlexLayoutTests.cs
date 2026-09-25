using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Layouts;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// <see cref="ShinyFlexLayout"/> has to do two things: lay children out the way flexbox does (checked
/// against MAUI's own FlexLayout where they should agree), and avoid measuring. The second only shows
/// up as how many times each child's MeasureOverride ran, so the probes count it.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ShinyFlexLayoutTests
{
    public ShinyFlexLayoutTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }


    /// <summary>
    /// A view with a known natural size that counts its measures. With <see cref="Wraps"/> it behaves
    /// like text: narrower than natural and it gets taller, one <see cref="NaturalHeight"/> per line.
    /// </summary>
    sealed class Probe : View
    {
        public double NaturalWidth;
        public double NaturalHeight;
        public bool Wraps;
        public int Measures;

        public Probe(double width, double height)
        {
            this.NaturalWidth = width;
            this.NaturalHeight = height;
        }

        /// <summary>Changes size the way a Controls property does: MeasureInvalidated is raised.</summary>
        public void Resize(double width)
        {
            this.NaturalWidth = width;
            this.InvalidateMeasure();
        }

        /// <summary>
        /// Changes size the way a handler does, e.g. an image that finished loading. This path raises no
        /// MeasureInvalidated; it only reaches Handler.Invoke, so the probe needs a handler for it.
        /// </summary>
        public void ResizeNatively(double width)
        {
            this.NaturalWidth = width;
            ((IView)this).InvalidateMeasure();
        }

        protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
        {
            this.Measures++;
            var margin = this.Margin;
            var available = Math.Max(0, widthConstraint - margin.HorizontalThickness);
            var width = this.Wraps ? Math.Min(this.NaturalWidth, available) : this.NaturalWidth;
            var height = this.Wraps
                ? Math.Ceiling(this.NaturalWidth / Math.Max(1, width)) * this.NaturalHeight
                : this.NaturalHeight;

            var size = new Size(width + margin.HorizontalThickness, height + margin.VerticalThickness);
            this.DesiredSize = size;
            return size;
        }
    }


    /// <summary>Just enough of a handler for Handler.Invoke to reach ViewHandler.ViewCommandMapper.</summary>
    sealed class FakeHandler() : ViewHandler<IView, object>(ViewMapper, ViewCommandMapper)
    {
        protected override object CreatePlatformView() => new();
    }


    /// <summary>A stack that measures its children without a handler (a plain one measures zero headless).</summary>
    sealed class HeadlessStack : VerticalStackLayout
    {
        protected override Size MeasureOverride(double widthConstraint, double heightConstraint)
            => ((Microsoft.Maui.ILayout)this).CrossPlatformMeasure(widthConstraint, heightConstraint);
    }


    static Size Layout(Layout layout, double width, double height)
    {
        var l = (Microsoft.Maui.ILayout)layout;
        var size = l.CrossPlatformMeasure(width, height);
        l.CrossPlatformArrange(new Rect(0, 0, width, height));
        return size;
    }

    /// <summary>MAUI's FlexLayout builds its engine root in OnParentSet, so it needs a parent to lay out at all.</summary>
    static FlexLayout Maui(IEnumerable<View> children, Action<FlexLayout>? configure = null)
    {
        var flex = new FlexLayout();
        configure?.Invoke(flex);
        foreach (var child in children)
            flex.Children.Add(child);
        _ = new ContentPage { Content = flex };
        return flex;
    }

    static ShinyFlexLayout Flex(params View[] children)
    {
        var flex = new ShinyFlexLayout();
        foreach (var child in children)
            flex.Children.Add(child);
        return flex;
    }

    static void ShouldBe(Rect actual, double x, double y, double w, double h)
    {
        actual.X.ShouldBe(x, 0.01, $"X of {actual}");
        actual.Y.ShouldBe(y, 0.01, $"Y of {actual}");
        actual.Width.ShouldBe(w, 0.01, $"Width of {actual}");
        actual.Height.ShouldBe(h, 0.01, $"Height of {actual}");
    }


    [Fact]
    public void RowPlacesChildrenWithColumnSpacingAndStretchesToTheLine()
    {
        var a = new Probe(50, 20);
        var b = new Probe(30, 40);
        var flex = Flex(a, b);
        flex.ColumnSpacing = 10;

        Layout(flex, 300, 100);

        // A single-line row's line is the whole container height, so the default Stretch fills it.
        ShouldBe(a.Frame, 0, 0, 50, 100);
        ShouldBe(b.Frame, 60, 0, 30, 100);
    }


    [Fact]
    public void MeasureReportsContentSize()
    {
        var flex = Flex(new Probe(50, 20), new Probe(30, 40));
        flex.ColumnSpacing = 10;
        flex.Padding = new Thickness(5);

        var size = ((Microsoft.Maui.ILayout)flex).CrossPlatformMeasure(300, double.PositiveInfinity);

        size.Width.ShouldBe(50 + 10 + 30 + 10);
        size.Height.ShouldBe(40 + 10);
    }


    [Fact]
    public void WrapBreaksLinesAndAppliesRowSpacing()
    {
        var a = new Probe(60, 20);
        var b = new Probe(60, 30);
        var c = new Probe(60, 20);
        var flex = Flex(a, b, c);
        flex.Wrap = FlexWrap.Wrap;
        flex.AlignItems = FlexAlignItems.Start;
        flex.AlignContent = FlexAlignContent.Start;
        flex.ColumnSpacing = 10;
        flex.RowSpacing = 5;

        var size = ((Microsoft.Maui.ILayout)flex).CrossPlatformMeasure(140, double.PositiveInfinity);
        ((Microsoft.Maui.ILayout)flex).CrossPlatformArrange(new Rect(0, 0, 140, 200));

        size.Height.ShouldBe(30 + 5 + 20);
        ShouldBe(a.Frame, 0, 0, 60, 20);
        ShouldBe(b.Frame, 70, 0, 60, 30);
        ShouldBe(c.Frame, 0, 35, 60, 20);
    }


    [Fact]
    public void GrowSharesFreeSpaceByFactor()
    {
        var a = new Probe(50, 20);
        var b = new Probe(50, 20);
        var c = new Probe(50, 20);
        ShinyFlexLayout.SetGrow(a, 1);
        ShinyFlexLayout.SetGrow(b, 3);
        var flex = Flex(a, b, c);

        Layout(flex, 350, 20);

        // 200 spare: a gets 50, b gets 150, c none.
        ShouldBe(a.Frame, 0, 0, 100, 20);
        ShouldBe(b.Frame, 100, 0, 200, 20);
        ShouldBe(c.Frame, 300, 0, 50, 20);
    }


    [Fact]
    public void GrowStopsAtMaximumAndRedistributes()
    {
        var a = new Probe(50, 20) { MaximumWidthRequest = 80 };
        var b = new Probe(50, 20);
        ShinyFlexLayout.SetGrow(a, 1);
        ShinyFlexLayout.SetGrow(b, 1);

        Layout(Flex(a, b), 300, 20);

        a.Frame.Width.ShouldBe(80, 0.01);
        b.Frame.Width.ShouldBe(220, 0.01);
    }


    [Fact]
    public void ShrinkIsWeightedByBasis()
    {
        var a = new Probe(100, 20);
        var b = new Probe(300, 20);
        var flex = Flex(a, b);

        Layout(flex, 200, 20);

        // 200 over, taken 1:3 by size.
        a.Frame.Width.ShouldBe(50, 0.01);
        b.Frame.Width.ShouldBe(150, 0.01);
    }


    [Fact]
    public void ShrinkZeroHoldsItsSize()
    {
        var a = new Probe(100, 20);
        var b = new Probe(300, 20);
        ShinyFlexLayout.SetShrink(a, 0);

        Layout(Flex(a, b), 200, 20);

        a.Frame.Width.ShouldBe(100, 0.01);
        b.Frame.Width.ShouldBe(100, 0.01);
    }


    [Fact]
    public void ShrunkTextIsMeasuredAgainAtItsNewWidth()
    {
        var text = new Probe(300, 20) { Wraps = true };
        var flex = Flex(text);
        flex.AlignItems = FlexAlignItems.Start;

        var size = ((Microsoft.Maui.ILayout)flex).CrossPlatformMeasure(100, double.PositiveInfinity);

        size.Height.ShouldBe(60);
    }


    [Theory]
    [InlineData(FlexJustify.Start, 0, 60, 120)]
    [InlineData(FlexJustify.End, 150, 210, 270)]
    [InlineData(FlexJustify.Center, 75, 135, 195)]
    [InlineData(FlexJustify.SpaceBetween, 0, 135, 270)]
    [InlineData(FlexJustify.SpaceAround, 25, 135, 245)]
    [InlineData(FlexJustify.SpaceEvenly, 37.5, 135, 232.5)]
    public void JustifyContentDistributesTheLeftover(FlexJustify justify, double x1, double x2, double x3)
    {
        var a = new Probe(30, 20);
        var b = new Probe(30, 20);
        var c = new Probe(30, 20);
        var flex = Flex(a, b, c);
        flex.ColumnSpacing = 30;
        flex.JustifyContent = justify;

        Layout(flex, 300, 20);

        a.Frame.X.ShouldBe(x1, 0.01);
        b.Frame.X.ShouldBe(x2, 0.01);
        c.Frame.X.ShouldBe(x3, 0.01);
    }


    [Fact]
    public void AlignItemsAndAlignSelf()
    {
        var a = new Probe(10, 20);
        var b = new Probe(10, 20);
        var c = new Probe(10, 20);
        var d = new Probe(10, 20) { HeightRequest = 20 };
        ShinyFlexLayout.SetAlignSelf(b, FlexAlignSelf.End);
        ShinyFlexLayout.SetAlignSelf(c, FlexAlignSelf.Stretch);
        ShinyFlexLayout.SetAlignSelf(d, FlexAlignSelf.Stretch);
        var flex = Flex(a, b, c, d);
        flex.AlignItems = FlexAlignItems.Center;

        Layout(flex, 100, 100);

        ShouldBe(a.Frame, 0, 40, 10, 20);
        ShouldBe(b.Frame, 10, 80, 10, 20);
        ShouldBe(c.Frame, 20, 0, 10, 100);

        // Stretch leaves an explicit cross size alone.
        d.Frame.Height.ShouldBe(20, 0.01);
    }


    [Theory]
    [InlineData(FlexAlignContent.Start, 0, 20)]
    [InlineData(FlexAlignContent.End, 60, 80)]
    [InlineData(FlexAlignContent.Center, 30, 50)]
    [InlineData(FlexAlignContent.SpaceBetween, 0, 80)]
    [InlineData(FlexAlignContent.Stretch, 0, 50)]
    public void AlignContentPlacesLines(FlexAlignContent alignContent, double y1, double y2)
    {
        var a = new Probe(60, 20);
        var b = new Probe(60, 20);
        var flex = Flex(a, b);
        flex.Wrap = FlexWrap.Wrap;
        flex.AlignItems = FlexAlignItems.Start;
        flex.AlignContent = alignContent;

        Layout(flex, 100, 100);

        a.Frame.Y.ShouldBe(y1, 0.01);
        b.Frame.Y.ShouldBe(y2, 0.01);
    }


    [Fact]
    public void ReverseDirectionsAndWrapReverse()
    {
        var a = new Probe(30, 20);
        var b = new Probe(30, 20);
        var flex = Flex(a, b);
        flex.AlignItems = FlexAlignItems.Start;

        flex.Direction = FlexDirection.RowReverse;
        Layout(flex, 100, 50);
        a.Frame.X.ShouldBe(70, 0.01);
        b.Frame.X.ShouldBe(40, 0.01);

        flex.Direction = FlexDirection.Column;
        Layout(flex, 100, 50);
        ShouldBe(a.Frame, 0, 0, 30, 20);
        ShouldBe(b.Frame, 0, 20, 30, 20);

        flex.Direction = FlexDirection.ColumnReverse;
        Layout(flex, 100, 50);
        a.Frame.Y.ShouldBe(30, 0.01);
        b.Frame.Y.ShouldBe(10, 0.01);

        flex.Direction = FlexDirection.Row;
        flex.Wrap = FlexWrap.Reverse;
        flex.AlignContent = FlexAlignContent.Start;
        Layout(flex, 40, 100);
        a.Frame.Y.ShouldBe(80, 0.01);
        b.Frame.Y.ShouldBe(60, 0.01);
    }


    [Fact]
    public void OrderSortsStablyWithoutTouchingChildOrder()
    {
        var a = new Probe(10, 10);
        var b = new Probe(10, 10);
        var c = new Probe(10, 10);
        ShinyFlexLayout.SetOrder(a, 1);
        var flex = Flex(a, b, c);

        Layout(flex, 100, 10);

        b.Frame.X.ShouldBe(0);
        c.Frame.X.ShouldBe(10);
        a.Frame.X.ShouldBe(20);
        flex.Children[0].ShouldBeSameAs(a);
    }


    [Fact]
    public void BasisLengthAndPercentage()
    {
        var a = new Probe(10, 10);
        var b = new Probe(10, 10);
        ShinyFlexLayout.SetBasis(a, 40);
        ShinyFlexLayout.SetBasis(b, new FlexBasis(0.5f, isRelative: true));

        Layout(Flex(a, b), 200, 10);

        a.Frame.Width.ShouldBe(40, 0.01);
        b.Frame.Width.ShouldBe(100, 0.01);
    }


    [Fact]
    public void BasisFromXamlStringIsRelative()
    {
        var basis = (FlexBasis)new Microsoft.Maui.Converters.FlexBasisTypeConverter().ConvertFromInvariantString("25%")!;
        var a = new Probe(10, 10);
        ShinyFlexLayout.SetBasis(a, basis);

        Layout(Flex(a), 200, 10);

        a.Frame.Width.ShouldBe(50, 0.01);
    }


    [Fact]
    public void CollapsedChildrenTakeNoSpace()
    {
        var a = new Probe(30, 10) { IsVisible = false };
        var b = new Probe(30, 10);
        var flex = Flex(a, b);
        flex.ColumnSpacing = 10;

        Layout(flex, 100, 10);
        b.Frame.X.ShouldBe(0);

        a.IsVisible = true;
        Layout(flex, 100, 10);
        b.Frame.X.ShouldBe(40);
    }


    [Fact]
    public void RightToLeftMirrors()
    {
        var a = new Probe(30, 10);
        var b = new Probe(20, 10);
        var flex = Flex(a, b);
        flex.FlowDirection = FlowDirection.RightToLeft;

        Layout(flex, 100, 10);

        a.Frame.X.ShouldBe(70);
        b.Frame.X.ShouldBe(50);
    }


    [Fact]
    public void MarginsAreOutsideTheChildsFrame()
    {
        var a = new Probe(30, 10) { Margin = new Thickness(5) };
        var b = new Probe(30, 10);
        var flex = Flex(a, b);
        flex.AlignItems = FlexAlignItems.Start;

        Layout(flex, 100, 50);

        ShouldBe(a.Frame, 5, 5, 30, 10);
        b.Frame.X.ShouldBe(40);
    }


    // ---- caching ----

    [Fact]
    public void MeasureThenArrangeAtTheSameSizeResolvesOnceAndMeasuresEachChildOnce()
    {
        var children = Enumerable.Range(0, 50).Select(_ => new Probe(40, 20)).ToArray();
        var flex = Flex(children);
        flex.Wrap = FlexWrap.Wrap;

        var l = (Microsoft.Maui.ILayout)flex;
        var size = l.CrossPlatformMeasure(300, double.PositiveInfinity);
        l.CrossPlatformArrange(new Rect(0, 0, 300, size.Height));

        // The arrange height differs from the measured ∞, so the pass is resolved again, but every
        // child answers from its cache.
        children.Sum(c => c.Measures).ShouldBe(50);
    }


    [Fact]
    public void RepeatedPassesAtTheSameSizeMeasureNothing()
    {
        var children = Enumerable.Range(0, 20).Select(_ => new Probe(40, 20)).ToArray();
        var flex = Flex(children);
        Layout(flex, 300, 100);
        var resolves = flex.ResolveCount;
        foreach (var c in children) c.Measures = 0;

        Layout(flex, 300, 100);
        Layout(flex, 300, 100);

        flex.ResolveCount.ShouldBe(resolves);
        children.Sum(c => c.Measures).ShouldBe(0);
    }


    [Fact]
    public void InvalidatingOneChildMeasuresOnlyThatChild()
    {
        var children = Enumerable.Range(0, 20).Select(_ => new Probe(40, 20)).ToArray();
        var flex = Flex(children);
        flex.Wrap = FlexWrap.Wrap;
        flex.AlignItems = FlexAlignItems.Start;
        Layout(flex, 300, 400);
        foreach (var c in children) c.Measures = 0;

        children[3].Resize(100);
        Layout(flex, 300, 400);

        string.Join(",", children.Select(c => c.Measures)).ShouldBe("0,0,0,1," + string.Join(",", Enumerable.Repeat(0, 16)));
        children[3].Frame.Width.ShouldBe(100);
        children[4].Frame.X.ShouldBe(children[3].Frame.Right);
    }


    [Fact]
    public void HandlerSideInvalidationIsSeenToo()
    {
        var children = Enumerable.Range(0, 10).Select(_ => new Probe(40, 20)).ToArray();
        var flex = Flex(children);
        flex.AlignItems = FlexAlignItems.Start;
        children[2].Handler = new FakeHandler();
        Layout(flex, 500, 100);
        foreach (var c in children) c.Measures = 0;

        children[2].ResizeNatively(90);
        Layout(flex, 500, 100);

        children.Sum(c => c.Measures).ShouldBe(1);
        children[2].Frame.Width.ShouldBe(90);
        children[3].Frame.X.ShouldBe(children[2].Frame.Right);
    }


    [Fact]
    public void HandlerSideInvalidationDeepInsideAChildIsSeen()
    {
        var deep = new Probe(40, 20) { Handler = new FakeHandler() };
        var nested = new HeadlessStack { deep };
        var sibling = new Probe(40, 20);
        var flex = Flex(nested, sibling);
        flex.AlignItems = FlexAlignItems.Start;
        Layout(flex, 500, 100);
        nested.Frame.Width.ShouldBe(40);
        sibling.Measures = 0;

        deep.ResizeNatively(120);
        Layout(flex, 500, 100);

        nested.Frame.Width.ShouldBe(120);
        sibling.Measures.ShouldBe(0);
    }


    [Fact]
    public void ResizingReusesLeafMeasurements()
    {
        var children = Enumerable.Range(0, 30).Select(_ => new Probe(40, 20) { Wraps = true }).ToArray();
        var flex = Flex(children);
        flex.Wrap = FlexWrap.Wrap;
        Layout(flex, 300, 800);
        foreach (var c in children) c.Measures = 0;

        // Rotate: wider and shorter. Every child fitted before and still fits, so none is measured.
        Layout(flex, 600, 400);
        Layout(flex, 250, 900);

        children.Sum(c => c.Measures).ShouldBe(0);
        children[6].Frame.Y.ShouldBeGreaterThan(0); // 250 fits 6 per line
    }


    [Fact]
    public void AttachedPropertyChangeAfterLayoutIsPickedUp()
    {
        var a = new Probe(50, 20);
        var b = new Probe(50, 20);
        var flex = Flex(a, b);
        Layout(flex, 300, 20);
        a.Frame.Width.ShouldBe(50);

        ShinyFlexLayout.SetGrow(a, 1);
        Layout(flex, 300, 20);

        a.Frame.Width.ShouldBe(250);
    }


    [Fact]
    public void LayoutPropertyChangeAfterLayoutIsPickedUp()
    {
        var a = new Probe(50, 20);
        var flex = Flex(a);
        Layout(flex, 300, 20);

        flex.JustifyContent = FlexJustify.End;
        Layout(flex, 300, 20);

        a.Frame.X.ShouldBe(250);
    }


    [Fact]
    public void ChildrenAddedAndRemovedAfterLayout()
    {
        var a = new Probe(50, 20);
        var b = new Probe(50, 20);
        var flex = Flex(a);
        Layout(flex, 300, 20);

        flex.Children.Insert(0, b);
        Layout(flex, 300, 20);
        b.Frame.X.ShouldBe(0);
        a.Frame.X.ShouldBe(50);

        flex.Children.Remove(b);
        Layout(flex, 300, 20);
        a.Frame.X.ShouldBe(0);
    }


    [Fact]
    public void CacheCanBeTurnedOff()
    {
        var a = new Probe(50, 20);
        var flex = Flex(a);
        flex.IsMeasureCacheEnabled = false;
        Layout(flex, 300, 20);
        a.Measures = 0;

        Layout(flex, 300, 20);

        a.Measures.ShouldBeGreaterThan(0);
    }


    // ---- parity with MAUI's FlexLayout ----

    public static TheoryData<FlexDirection, FlexWrap, FlexJustify, FlexAlignItems, FlexAlignContent> ParityCases => new()
    {
        { FlexDirection.Row, FlexWrap.Wrap, FlexJustify.Start, FlexAlignItems.Start, FlexAlignContent.Start },
        { FlexDirection.Row, FlexWrap.Wrap, FlexJustify.SpaceBetween, FlexAlignItems.Center, FlexAlignContent.Start },
        { FlexDirection.Row, FlexWrap.Wrap, FlexJustify.Center, FlexAlignItems.End, FlexAlignContent.Center },
        { FlexDirection.Row, FlexWrap.NoWrap, FlexJustify.SpaceAround, FlexAlignItems.Center, FlexAlignContent.Start },
        { FlexDirection.Column, FlexWrap.Wrap, FlexJustify.Start, FlexAlignItems.Start, FlexAlignContent.Start },
        { FlexDirection.Column, FlexWrap.NoWrap, FlexJustify.SpaceEvenly, FlexAlignItems.Center, FlexAlignContent.Start },
        { FlexDirection.RowReverse, FlexWrap.Wrap, FlexJustify.Start, FlexAlignItems.Start, FlexAlignContent.Start },
    };

    [Theory]
    [MemberData(nameof(ParityCases))]
    public void MatchesMauiFlexLayout(FlexDirection direction, FlexWrap wrap, FlexJustify justify, FlexAlignItems alignItems, FlexAlignContent alignContent)
    {
        var sizes = new[] { (50d, 20d), (70d, 30d), (40d, 25d), (90d, 20d), (30d, 40d), (60d, 20d) };
        var ours = sizes.Select(s => new Probe(s.Item1, s.Item2)).ToArray();
        var theirs = sizes.Select(s => new Probe(s.Item1, s.Item2)).ToArray();
        ShinyFlexLayout.SetGrow(ours[5], 1);
        FlexLayout.SetGrow(theirs[5], 1);

        var shiny = Flex(ours);
        shiny.Direction = direction;
        shiny.Wrap = wrap;
        shiny.JustifyContent = justify;
        shiny.AlignItems = alignItems;
        shiny.AlignContent = alignContent;

        var maui = Maui(theirs, f =>
        {
            f.Direction = direction;
            f.Wrap = wrap;
            f.JustifyContent = justify;
            f.AlignItems = alignItems;
            f.AlignContent = alignContent;
        });

        // A no-wrap row is given room for all of its children. When one overflows, MAUI's FlexLayout
        // shrinks each child by the same amount and still spills past the container, while CSS weights
        // shrink by size and fits. That case is covered by ShrinkIsWeightedByBasis instead.
        var width = wrap == FlexWrap.NoWrap && direction is FlexDirection.Row ? 400 : 200;
        Layout(shiny, width, 160);
        Layout(maui, width, 160);

        for (var i = 0; i < sizes.Length; i++)
            ShouldBe(ours[i].Frame, theirs[i].Frame.X, theirs[i].Frame.Y, theirs[i].Frame.Width, theirs[i].Frame.Height);
    }


    [Fact]
    public void MeasuresFarLessThanMauiFlexLayout()
    {
        const int count = 200;
        var ours = Enumerable.Range(0, count).Select(i => new Probe(30 + i % 7 * 10, 20) { Wraps = true }).ToArray();
        var theirs = Enumerable.Range(0, count).Select(i => new Probe(30 + i % 7 * 10, 20) { Wraps = true }).ToArray();

        var shiny = Flex(ours);
        shiny.Wrap = FlexWrap.Wrap;
        var maui = Maui(theirs, f => f.Wrap = FlexWrap.Wrap);

        // Initial layout, a few same-size passes (what the platform does constantly), a rotation, and
        // one child changing.
        void Passes(Layout layout, Probe[] probes)
        {
            Layout(layout, 400, 2000);
            Layout(layout, 400, 2000);
            Layout(layout, 400, 2000);
            Layout(layout, 800, 1000);
            probes[10].Resize(55);
            Layout(layout, 800, 1000);
        }

        Passes(shiny, ours);
        Passes(maui, theirs);

        var ourMeasures = ours.Sum(p => p.Measures);
        var theirMeasures = theirs.Sum(p => p.Measures);

        ourMeasures.ShouldBeLessThanOrEqualTo(count + 1);
        (theirMeasures / (double)ourMeasures).ShouldBeGreaterThan(4, $"ours {ourMeasures} vs MAUI {theirMeasures}");
    }
}

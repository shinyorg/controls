namespace Shiny.Controls.Diagram.Tests;

public class ShapeGeometryTests
{
    static readonly DiagramRect Box = new(0, 0, 100, 60);

    static DiagramPoint Toward(double x, double y) => new(x, y);


    [Fact]
    public void ARectangleAnchorsOnTheEdgeFacingTheOtherEnd()
    {
        var right = ShapeGeometry.Intersect(DiagramNodeShape.Rectangle, Box, Toward(500, 30));
        right.X.ShouldBe(100, 0.01);
        right.Y.ShouldBe(30, 0.01);

        var below = ShapeGeometry.Intersect(DiagramNodeShape.Rectangle, Box, Toward(50, 500));
        below.X.ShouldBe(50, 0.01);
        below.Y.ShouldBe(60, 0.01);
    }


    [Fact]
    public void ADiamondAnchorsOnItsVertexNotItsBoundingBoxCorner()
    {
        // The detail the whole shape-geometry pass exists for. A box anchor would put this at
        // (100, 0) - the corner of an invisible rectangle, with nothing drawn anywhere near it.
        var east = ShapeGeometry.Intersect(DiagramNodeShape.Diamond, Box, Toward(500, 30));
        east.X.ShouldBe(100, 0.01);
        east.Y.ShouldBe(30, 0.01);

        // Heading for the top-right corner, a diamond's edge is met halfway up the slope.
        var corner = ShapeGeometry.Intersect(DiagramNodeShape.Diamond, Box, Toward(150, -20));
        corner.X.ShouldBeLessThan(100);
        corner.Y.ShouldBeGreaterThan(0);

        // On the diamond's own boundary: |x/hw| + |y/hh| == 1 measured from the centre.
        var nx = Math.Abs(corner.X - 50) / 50;
        var ny = Math.Abs(corner.Y - 30) / 30;
        (nx + ny).ShouldBe(1, 0.01);
    }


    [Fact]
    public void ACircleAnchorsOnItsRadius()
    {
        var square = new DiagramRect(0, 0, 80, 80);
        var point = ShapeGeometry.Intersect(DiagramNodeShape.Circle, square, Toward(500, 500));

        point.DistanceTo(square.Center).ShouldBe(40, 0.01);
    }


    [Fact]
    public void ADiamondsBoundingBoxCornerIsNotInsideIt()
    {
        // A click here selects the node if containment is tested against the box, and misses - which
        // is correct - if it is tested against the outline.
        ShapeGeometry.Contains(DiagramNodeShape.Diamond, Box, new DiagramPoint(2, 2)).ShouldBeFalse();
        ShapeGeometry.Contains(DiagramNodeShape.Diamond, Box, new DiagramPoint(50, 30)).ShouldBeTrue();

        // The same point is inside the rectangle, which is the difference being asserted.
        ShapeGeometry.Contains(DiagramNodeShape.Rectangle, Box, new DiagramPoint(2, 2)).ShouldBeTrue();
    }


    [Fact]
    public void AnEllipseExcludesItsCorners()
    {
        ShapeGeometry.Contains(DiagramNodeShape.Ellipse, Box, new DiagramPoint(1, 1)).ShouldBeFalse();
        ShapeGeometry.Contains(DiagramNodeShape.Ellipse, Box, new DiagramPoint(50, 30)).ShouldBeTrue();
    }


    [Theory]
    [InlineData(DiagramNodeShape.Rectangle)]
    [InlineData(DiagramNodeShape.RoundedRectangle)]
    [InlineData(DiagramNodeShape.Stadium)]
    [InlineData(DiagramNodeShape.Circle)]
    [InlineData(DiagramNodeShape.Ellipse)]
    [InlineData(DiagramNodeShape.Diamond)]
    [InlineData(DiagramNodeShape.Parallelogram)]
    [InlineData(DiagramNodeShape.Hexagon)]
    [InlineData(DiagramNodeShape.Triangle)]
    [InlineData(DiagramNodeShape.Cylinder)]
    [InlineData(DiagramNodeShape.Document)]
    public void EveryShapeAnchorsInsideItsOwnBox(DiagramNodeShape shape)
    {
        foreach (var target in new[] { Toward(500, 30), Toward(-500, 30), Toward(50, 500), Toward(50, -500) })
        {
            var point = ShapeGeometry.Intersect(shape, Box, target);

            // A circle is sized from the larger dimension, so it legitimately overhangs a wide, short
            // box on the vertical axis.
            var bounds = shape == DiagramNodeShape.Circle
                ? ShapeGeometry.CircleBounds(Box)
                : Box;

            bounds.Inflate(0.01).Contains(point).ShouldBeTrue($"{shape} anchored outside its box at {point}");
        }
    }


    [Theory]
    [InlineData(DiagramNodeShape.Rectangle)]
    [InlineData(DiagramNodeShape.Diamond)]
    [InlineData(DiagramNodeShape.Hexagon)]
    [InlineData(DiagramNodeShape.Stadium)]
    public void TheCentreIsInsideEveryShape(DiagramNodeShape shape) =>
        ShapeGeometry.Contains(shape, Box, Box.Center).ShouldBeTrue();


    [Fact]
    public void APortResolvesToTheOutlineNotTheBoxEdge()
    {
        var left = ShapeGeometry.PortPoint(DiagramNodeShape.Diamond, Box, DiagramPort.Left);

        left.X.ShouldBe(0, 0.01);
        left.Y.ShouldBe(30, 0.01);

        var top = ShapeGeometry.PortPoint(DiagramNodeShape.Diamond, Box, DiagramPort.Top);
        top.X.ShouldBe(50, 0.01);
        top.Y.ShouldBe(0, 0.01);
    }


    [Fact]
    public void TheFacingPortAccountsForTheBoxAspectRatio()
    {
        // A wide, short node hands off to its top edge sooner than a 45-degree split would say: the
        // corner is where the aspect ratio puts it, not at the diagonal.
        var wide = new DiagramRect(0, 0, 200, 40);

        ShapeGeometry.FacingPort(wide, new DiagramPoint(300, 30)).ShouldBe(DiagramPort.Right);
        ShapeGeometry.FacingPort(wide, new DiagramPoint(120, -200)).ShouldBe(DiagramPort.Top);
        ShapeGeometry.FacingPort(wide, new DiagramPoint(100, 300)).ShouldBe(DiagramPort.Bottom);
    }


    [Fact]
    public void AnEmptyBoxAnchorsAtItsCentreRatherThanThrowing()
    {
        var empty = new DiagramRect(10, 10, 0, 0);
        var point = ShapeGeometry.Intersect(DiagramNodeShape.Diamond, empty, Toward(100, 100));

        point.X.ShouldBe(10);
        point.Y.ShouldBe(10);
    }
}


public class ShapePathDataTests
{
    static readonly DiagramRect Box = new(0, 0, 100, 60);


    [Theory]
    [InlineData(DiagramNodeShape.Rectangle)]
    [InlineData(DiagramNodeShape.RoundedRectangle)]
    [InlineData(DiagramNodeShape.Stadium)]
    [InlineData(DiagramNodeShape.Circle)]
    [InlineData(DiagramNodeShape.Ellipse)]
    [InlineData(DiagramNodeShape.Diamond)]
    [InlineData(DiagramNodeShape.Parallelogram)]
    [InlineData(DiagramNodeShape.Hexagon)]
    [InlineData(DiagramNodeShape.Triangle)]
    [InlineData(DiagramNodeShape.Cylinder)]
    [InlineData(DiagramNodeShape.Document)]
    public void EveryShapeProducesAClosedPathStartingWithAMove(DiagramNodeShape shape)
    {
        var path = ShapePathData.For(shape, Box);

        path.ShouldStartWith("M ");
        path.ShouldEndWith("Z");
    }


    [Fact]
    public void NumbersAreFormattedInvariantly()
    {
        // A comma-decimal locale does not merely mis-scale a path: "12,5 30" is a valid path meaning
        // something else, so the shape silently draws wrong instead of failing.
        var original = System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var path = ShapePathData.For(DiagramNodeShape.Rectangle, new DiagramRect(0, 0, 12.5, 30));

            path.ShouldContain("12.5");
            path.ShouldNotContain("12,5");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }


    [Fact]
    public void ABezierRouteBecomesASingleCurveCommand()
    {
        var points = new List<DiagramPoint> { new(0, 0), new(10, 0), new(20, 10), new(30, 10) };
        var path = ShapePathData.ForRoute(points, DiagramConnectionRouter.Bezier);

        path.ShouldContain(" C ");
        path.ShouldNotContain(" L ");
    }


    [Fact]
    public void APolylineRouteBecomesLineCommands()
    {
        var points = new List<DiagramPoint> { new(0, 0), new(10, 0), new(10, 10) };
        var path = ShapePathData.ForRoute(points, DiagramConnectionRouter.Orthogonal);

        path.ShouldBe("M 0 0 L 10 0 L 10 10");
    }


    [Fact]
    public void OnlyACylinderHasALid()
    {
        ShapePathData.CylinderLid(DiagramNodeShape.Cylinder, Box).ShouldNotBeEmpty();
        ShapePathData.CylinderLid(DiagramNodeShape.Rectangle, Box).ShouldBeEmpty();
    }


    [Fact]
    public void AnEmptyBoxProducesNoPath() =>
        ShapePathData.For(DiagramNodeShape.Rectangle, DiagramRect.Empty).ShouldBeEmpty();
}

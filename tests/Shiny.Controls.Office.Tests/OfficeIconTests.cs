using Shiny.Controls.Office.Icons;
using Shouldly;
using Xunit;

namespace Shiny.Controls.Office.Tests;

/// <summary>
/// Guards on the toolbar icon set.
/// </summary>
/// <remarks>
/// Icon artwork fails silently: a path that starts without a move, or a coordinate that has drifted
/// off the grid, draws a stump or a clipped edge and throws nothing. Both hosts render this same data
/// — MAUI onto a canvas, Blazor into SVG — so a defect here is a defect on both, in the one place a
/// unit test can still see it.
/// </remarks>
public class OfficeIconTests
{
    public static TheoryData<OfficeIcon> AllIcons
    {
        get
        {
            var data = new TheoryData<OfficeIcon>();

            foreach (var icon in Enum.GetValues<OfficeIcon>())
                data.Add(icon);

            // The Word ribbon's set lives outside the enum; the same guards apply to it.
            foreach (var icon in WordIcons.All)
                data.Add(icon);

            return data;
        }
    }


    [Theory]
    [MemberData(nameof(AllIcons))]
    public void EveryIconDrawsSomething(OfficeIcon icon)
        => OfficeIcons.Shapes(icon).ShouldNotBeEmpty();


    [Theory]
    [MemberData(nameof(AllIcons))]
    public void EveryPathStartsWithAMove(OfficeIcon icon)
    {
        foreach (var shape in OfficeIcons.Shapes(icon).Where(x => x.Primitive == OfficeIconPrimitive.Path))
        {
            shape.Vertices.Count.ShouldBeGreaterThan(1);
            shape.Vertices[0].Verb.ShouldBe(OfficeIconVerb.Move);

            // Only the first: a second move inside one shape is the shape being two figures that
            // should have been declared as two, which is what a stray implicit command looks like.
            shape.Vertices.Skip(1).ShouldAllBe(v => v.Verb != OfficeIconVerb.Move);
        }
    }


    /// <summary>
    /// Everything stays inside the grid, with room for half a stroke.
    /// </summary>
    /// <remarks>
    /// The hosts scale the 24x24 grid to the button and centre it; they do not clip, so artwork that
    /// runs past the edge is simply drawn thin against the neighbouring button rather than reported.
    /// </remarks>
    [Theory]
    [MemberData(nameof(AllIcons))]
    public void EveryIconFitsTheGrid(OfficeIcon icon)
    {
        var inset = OfficeIcons.StrokeWidth / 2;

        foreach (var shape in OfficeIcons.Shapes(icon))
        {
            foreach (var (x, y) in Points(shape))
            {
                x.ShouldBeInRange(-0.01f, OfficeIcons.Grid + 0.01f);
                y.ShouldBeInRange(-0.01f, OfficeIcons.Grid + 0.01f);

                // Half a stroke of clearance, so a round cap is not sliced off by the icon's edge.
                x.ShouldBeInRange(inset - 0.01f, OfficeIcons.Grid - inset + 0.01f);
                y.ShouldBeInRange(inset - 0.01f, OfficeIcons.Grid - inset + 0.01f);
            }
        }
    }


    /// <summary>
    /// Every point a shape is defined by, control points included.
    /// </summary>
    /// <remarks>
    /// Control points are the conservative bound rather than the exact one — a curve stays inside the
    /// hull its controls describe — which is the right way round for a guard.
    /// </remarks>
    static IEnumerable<(float X, float Y)> Points(OfficeIconShape shape)
    {
        if (shape.Primitive is OfficeIconPrimitive.Rectangle or OfficeIconPrimitive.Ellipse)
        {
            yield return (shape.X, shape.Y);
            yield return (shape.X + shape.Width, shape.Y + shape.Height);
            yield break;
        }

        foreach (var vertex in shape.Vertices)
        {
            if (vertex.Verb == OfficeIconVerb.Close)
                continue;

            yield return (vertex.X, vertex.Y);

            if (vertex.Verb == OfficeIconVerb.Cubic)
            {
                yield return (vertex.C1X, vertex.C1Y);
                yield return (vertex.C2X, vertex.C2Y);
            }
        }
    }


    [Fact]
    public void OnlyTheHighlightBarIsFilled()
    {
        foreach (var icon in Enum.GetValues<OfficeIcon>())
        {
            var filled = OfficeIcons.Shapes(icon).Count(x => x.IsFilled);

            // A filled figure cannot be tinted by the toolbar the way a stroked one is, so the set
            // stays stroked throughout apart from the highlight bar, which exists to carry a colour.
            filled.ShouldBe(icon == OfficeIcon.Highlight ? 1 : 0);
        }
    }
}

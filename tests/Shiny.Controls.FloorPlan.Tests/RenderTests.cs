using SkiaSharp;

namespace Shiny.Controls.FloorPlan.Tests;

/// <summary>
/// Rendering is checked by painting into a raster surface and reading pixels back. Crude, but it is
/// the only thing that catches "the control drew nothing" - the failure every other kind of test here
/// is blind to.
/// </summary>
public class RenderTests
{
    static SKBitmap Paint(Action<FloorPlanEngine> arrange, int width = 200, int height = 160)
    {
        var (engine, _) = Plan.Engine(new FloorPlanDocument { Width = 180, Height = 140, GridSize = 20 });
        arrange(engine);

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        engine.Render(canvas, width, height);
        return bitmap;
    }

    /// <summary>Pixels differing from the theme's background - i.e. anything actually drawn.</summary>
    static int Painted(SKBitmap bitmap, FloorPlanTheme? theme = null)
    {
        var background = (theme ?? FloorPlanTheme.Light).Background.ToSKColor();
        var count = 0;

        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (bitmap.GetPixel(x, y) != background)
                    count++;
            }
        }

        return count;
    }


    [Fact]
    public void AnEmptyPlanStillDrawsItsGridAndBoundary()
    {
        using var bitmap = Paint(_ => { });
        Painted(bitmap).ShouldBeGreaterThan(0);
    }


    /// <summary>
    /// The boundary is where the document ends, not part of the snap grid. Hiding it along with the
    /// grid leaves a plan floating on a sheet with no edges and no way to tell how much room is left.
    /// </summary>
    [Fact]
    public void TurningTheGridOffKeepsTheBoundary()
    {
        using var withGrid = Paint(engine => engine.State.IsGridVisible = true);
        using var without = Paint(engine => engine.State.IsGridVisible = false);

        var bare = Painted(without);
        bare.ShouldBeGreaterThan(0);
        bare.ShouldBeLessThan(Painted(withGrid));
    }


    [Fact]
    public void AnElementIsDrawn()
    {
        using var empty = Paint(_ => { });
        using var withRoom = Paint(engine => engine.Document.Elements.Add(Plan.Room(20, 20, 100, 80, "Room")));

        Painted(withRoom).ShouldBeGreaterThan(Painted(empty));
    }


    [Fact]
    public void AHiddenElementIsNotDrawn()
    {
        using var empty = Paint(_ => { });
        using var hidden = Paint(engine =>
        {
            var room = Plan.Room(20, 20, 100, 80, "Room");
            room.IsVisible = false;
            engine.Document.Elements.Add(room);
        });

        Painted(hidden).ShouldBe(Painted(empty));
    }


    /// <summary>The canvas takes the theme's ground, which is what a dark app depends on.</summary>
    [Fact]
    public void TheCanvasIsClearedToTheThemeBackground()
    {
        var (engine, _) = Plan.Engine();
        engine.Theme = FloorPlanTheme.Dark;
        engine.Camera.Zoom = 0.01f;   // push the plan well away from the corner being sampled

        using var bitmap = new SKBitmap(new SKImageInfo(40, 40, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        engine.Render(canvas, 40, 40);

        bitmap.GetPixel(39, 39).ShouldBe(FloorPlanTheme.Dark.Background.ToSKColor());
    }
}

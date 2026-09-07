namespace Shiny.Controls.FloorPlan.Tests;

public class CameraTests
{
    [Fact]
    public void ScreenToWorld_InvertsWorldToScreen()
    {
        var camera = new FloorPlanCamera { Zoom = 2.5f, OffsetX = 40, OffsetY = -15 };

        var screen = camera.WorldToScreen(120, 80);
        var world = camera.ScreenToWorld(screen.X, screen.Y);

        world.X.ShouldBe(120, 0.001);
        world.Y.ShouldBe(80, 0.001);
    }


    /// <summary>
    /// The anchor is the whole contract. A zoom that does not hold the point under the pointer still
    /// "works" - it just walks the plan away from wherever you were looking.
    /// </summary>
    [Fact]
    public void ZoomAt_HoldsThePointUnderThePointer()
    {
        var camera = new FloorPlanCamera { Zoom = 1f };
        var before = camera.ScreenToWorld(300, 200);

        camera.ZoomAt(300, 200, 0.5f);
        var after = camera.ScreenToWorld(300, 200);

        camera.Zoom.ShouldBe(1.5f, 0.0001);
        after.X.ShouldBe(before.X, 0.01);
        after.Y.ShouldBe(before.Y, 0.01);
    }


    [Theory]
    [InlineData(100f, 10f)]
    [InlineData(-0.99f, 0.1f)]
    public void ZoomAt_ClampsToTheLimits(float delta, float expected)
    {
        var camera = new FloorPlanCamera();

        // Enough repetitions to run well past either stop.
        for (var i = 0; i < 20; i++)
            camera.ZoomAt(0, 0, delta);

        camera.Zoom.ShouldBe(expected, 0.0001);
    }


    [Fact]
    public void Pan_MovesTheOrigin()
    {
        var camera = new FloorPlanCamera();
        camera.Pan(25, -10);

        camera.OffsetX.ShouldBe(25);
        camera.OffsetY.ShouldBe(-10);
    }
}

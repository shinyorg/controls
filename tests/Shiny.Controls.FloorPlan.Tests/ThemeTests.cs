namespace Shiny.Controls.FloorPlan.Tests;

public class ThemeTests
{
    [Theory]
    [InlineData("#ABC", 255, 0xAA, 0xBB, 0xCC)]
    [InlineData("#123456", 255, 0x12, 0x34, 0x56)]
    [InlineData("#80123456", 0x80, 0x12, 0x34, 0x56)]
    [InlineData("123456", 255, 0x12, 0x34, 0x56)]
    public void ParsesTheHexForms(string text, int a, int r, int g, int b)
    {
        PlanColor.TryParse(text, out var color).ShouldBeTrue();
        color.ShouldBe(new PlanColor((byte)a, (byte)r, (byte)g, (byte)b));
    }


    /// <summary>
    /// Element colours come out of a saved document. One bad string in a file someone hand-edited
    /// should cost that element its custom colour, not throw the whole plan away.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rebeccapurple")]
    [InlineData("#12345")]
    [InlineData("#GGHHII")]
    public void FallsBackRatherThanThrowing(string? text)
    {
        var fallback = PlanColor.Rgb(1, 2, 3);
        PlanColor.ParseOr(text, fallback).ShouldBe(fallback);
    }


    /// <summary>
    /// Only the neutrals follow the app. The selection blue says "this is what you have hold of" and
    /// the furniture colours say "this is wood"; restating either in an app's accent is not theming.
    /// </summary>
    [Fact]
    public void ApplyingASurfaceLeavesMeaningAlone()
    {
        var surface = new FloorPlanSurface(
            PlanColor.Rgb(0x10, 0x10, 0x18),
            PlanColor.Rgb(0xEE, 0xEE, 0xF4),
            PlanColor.Rgb(0x20, 0x20, 0x2A),
            PlanColor.Rgb(0x18, 0x18, 0x22),
            PlanColor.Rgb(0xB0, 0xB0, 0xC0),
            PlanColor.Rgb(0x60, 0x60, 0x70),
            PlanColor.Rgb(0x40, 0x40, 0x50)
        );

        var themed = surface.Apply(FloorPlanTheme.Dark);

        themed.Background.ShouldBe(surface.Surface);
        themed.GridLine.ShouldBe(surface.OutlineVariant);
        themed.LabelText.ShouldBe(surface.OnSurface);

        // The door gap and the handle's inside are the ground showing through, not colours of their own.
        themed.DoorOpening.ShouldBe(surface.Surface);
        themed.HandleFill.ShouldBe(surface.Surface);

        themed.Selection.ShouldBe(FloorPlanTheme.Dark.Selection);
        themed.Sofa.ShouldBe(FloorPlanTheme.Dark.Sofa);
    }


    [Fact]
    public void EveryFurnitureKindHasAColour()
    {
        foreach (var kind in Enum.GetValues<FurnitureKind>())
            FloorPlanTheme.Light.ColorFor(kind).ShouldNotBe(FloorPlanTheme.Light.ElementFill);
    }
}

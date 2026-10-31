using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>What the component hands the script and the stylesheet. The strokes and the mask are script.</summary>
public class ScratchCardTests
{
    [Fact]
    public void TheRevealDurationReachesTheStylesheet()
        => new ScratchCard { RevealDuration = TimeSpan.FromMilliseconds(450) }
            .RootStyle.ShouldStartWith("--shiny-scratch-card-reveal-duration:450ms;");


    [Fact]
    public void ACallersStyleIsAppendedNotReplaced()
        => new ScratchCard { Style = "height:180px" }.RootStyle.ShouldEndWith(";height:180px");


    [Fact]
    public void TheOptionsCarryTheBrushAndWhetherItScratches()
    {
        new ScratchCard { StrokeWidth = 30 }.OptionsJson().ShouldBe("{\"width\":30,\"enabled\":true}");
        new ScratchCard { IsScratchEnabled = false }.OptionsJson().ShouldContain("\"enabled\":false");
    }


    [Fact]
    public void TheStrokeDefaultsToAFingertip()
        => new ScratchCard().OptionsJson().ShouldContain("\"width\":44");


    [Fact]
    public void TheStrokeNeverShrinksToNothing()
        => new ScratchCard { StrokeWidth = 0 }.OptionsJson().ShouldContain("\"width\":2");
}

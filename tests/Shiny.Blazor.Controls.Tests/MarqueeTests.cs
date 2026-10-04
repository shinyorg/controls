using System.Globalization;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// How an angle becomes a layout mode and a direction, and what reaches the stylesheet. The movement
/// itself is CSS, so this is everything the component decides.
/// </summary>
public class MarqueeTests
{
    [Theory]
    [InlineData(0, "Horizontal", false)]
    [InlineData(180, "Horizontal", true)]
    [InlineData(-180, "Horizontal", true)]
    [InlineData(90, "Vertical", false)]
    [InlineData(270, "Vertical", true)]
    [InlineData(-90, "Vertical", true)]
    [InlineData(-15, "Angled", false)]
    [InlineData(45, "Angled", false)]
    public void TheAnglePicksTheModeAndDirection(double angle, string mode, bool backwards)
    {
        var marquee = new Marquee { Angle = angle };

        marquee.LayoutMode.ToString().ShouldBe(mode);
        marquee.RunsBackwards.ShouldBe(backwards);
    }


    [Fact]
    public void ReverseFlipsTheDirectionAgain()
        => new Marquee { Angle = 180, Reverse = true }.RunsBackwards.ShouldBeFalse();


    [Fact]
    public void AnglesAreFoldedIntoOneTurn()
        => new Marquee { Angle = -15 }.NormalizedAngle.ShouldBe(345);


    [Fact]
    public void TheStyleIsWrittenInvariantly()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // A comma decimal separator would make every one of these custom properties invalid CSS.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var style = new Marquee { Duration = TimeSpan.FromSeconds(12.5), Gap = 8.5, Angle = 30 }.RootStyle;

            style.ShouldContain("--shiny-marquee-duration:12.5s;");
            style.ShouldContain("--shiny-marquee-gap:8.5px;");
            style.ShouldContain("--shiny-marquee-angle:30deg;");
            style.ShouldContain("--shiny-marquee-fade-angle:120deg;");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }


    [Fact]
    public void ACallersStyleIsAppendedNotReplaced()
        => new Marquee { Style = "height:200px" }.RootStyle.ShouldEndWith("height:200px");
}

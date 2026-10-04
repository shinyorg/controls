using System.Text.Json;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// confetti.js reads the options by canvas-confetti's camelCase names and lower-cases the shape
/// names; a rename on either side fails silently into "the defaults", so the wire shape is pinned.
/// </summary>
public class ConfettiOptionsTests
{
    [Fact]
    public void SerialisesWithTheNamesTheScriptReads()
    {
        var json = new ConfettiOptions
        {
            ParticleCount = 12,
            OriginX = 0.25,
            Shapes = [ConfettiShape.Star],
            Emoji = ["🦄"],
            DisableForReducedMotion = true
        }.ToJson();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("particleCount").GetInt32().ShouldBe(12);
        root.GetProperty("originX").GetDouble().ShouldBe(0.25);
        root.GetProperty("startVelocity").GetDouble().ShouldBe(45);
        root.GetProperty("shapes")[0].GetString().ShouldBe("Star");
        root.GetProperty("emoji")[0].GetString().ShouldBe("🦄");
        root.GetProperty("disableForReducedMotion").GetBoolean().ShouldBeTrue();
        root.GetProperty("colors").GetArrayLength().ShouldBe(ConfettiOptions.DefaultColors.Count);
    }
}

using System.Text.Json;

namespace Shiny.Blazor.Controls.Keyframe.Tests;

/// <summary>The JSON the keyframe script runs from.</summary>
public class ProgramWriterTests
{
    static CompiledAnimation Animation(double iterations = 1, params TrackSpec[] tracks)
        => KeyframeCompiler.Compile(new AnimationSpec(new TimingSpec { Duration = TimeSpan.FromMilliseconds(400), Iterations = iterations }, tracks));

    static TrackSpec Opacity => new("Opacity", [new KeySpec(0, "0", null), new KeySpec(1, "1", null)]);

    [Fact]
    public void WritesTargetsTimingAndCamelCasedKeyframes()
    {
        var animation = Animation(tracks: [Opacity, new TrackSpec("BorderRadius", [new KeySpec(0, "0px", null), new KeySpec(1, "8px", null)])]);
        var json = ProgramWriter.Write([new ProgramItem(new TargetSpec(0, null), animation, 250)], ReducedMotionBehavior.Instant, restoreOnStop: true, rate: -1);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("reduced").GetString().ShouldBe("instant");
        root.GetProperty("restore").GetBoolean().ShouldBeTrue();
        root.GetProperty("rate").GetDouble().ShouldBe(-1);

        var item = root.GetProperty("items")[0];
        item.GetProperty("t").GetProperty("e").GetInt32().ShouldBe(0);
        item.GetProperty("timing").GetProperty("delay").GetDouble().ShouldBe(250);
        item.GetProperty("timing").GetProperty("duration").GetDouble().ShouldBe(400);

        var effects = item.GetProperty("effects").EnumerateArray().ToList();
        effects.Count.ShouldBe(2);
        effects.Any(e => e[1].TryGetProperty("borderRadius", out var radius)).ShouldBeTrue();
        effects.Any(e => e[0].TryGetProperty("opacity", out var o) && o.GetString() == "0").ShouldBeTrue();
    }

    [Fact]
    public void ForeverIsWrittenAsMinusOne()
    {
        var json = ProgramWriter.Write([new ProgramItem(new TargetSpec(0, null), Animation(Keyframes.Infinite, Opacity), 0)], ReducedMotionBehavior.Auto, false, 1);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("items")[0].GetProperty("timing").GetProperty("iterations").GetDouble().ShouldBe(-1);
        doc.RootElement.GetProperty("reduced").GetString().ShouldBe("auto");
    }

    [Fact]
    public void AnIdTargetIsWrittenWhenThereIsNoElement()
    {
        var json = ProgramWriter.Write([new ProgramItem(new TargetSpec(-1, "hero", 2), Animation(tracks: Opacity), 0)], ReducedMotionBehavior.Play, false, 1);

        using var doc = JsonDocument.Parse(json);
        var target = doc.RootElement.GetProperty("items")[0].GetProperty("t");
        target.GetProperty("id").GetString().ShouldBe("hero");
        target.GetProperty("c").GetInt32().ShouldBe(2);
    }

    [Fact]
    public void ScrubLengthSpansTheLastFiniteEnd()
    {
        var items = new List<ProgramItem>
        {
            new(new TargetSpec(0, null), Animation(tracks: Opacity), 0),
            new(new TargetSpec(1, null), Animation(tracks: Opacity), 300),
            new(new TargetSpec(2, null), Animation(Keyframes.Infinite, Opacity), 0)
        };

        ProgramWriter.ScrubLength(items).ShouldBe(700);
    }

    [Fact]
    public void ScrubLengthOfOnlyLoopsIsTheFirstPass()
        => ProgramWriter.ScrubLength([new ProgramItem(new TargetSpec(0, null), Animation(Keyframes.Infinite, Opacity), 100)]).ShouldBe(500);

    [Fact]
    public void CssKeyframesCarryEachSegmentsCurve()
    {
        var effects = KeyframeCompiler.CompileForCss(new AnimationSpec(new TimingSpec(),
            [new TrackSpec("TranslationY", [new KeySpec(0, "40%", "EaseOut"), new KeySpec(1, "0", null)])]));

        var css = ProgramWriter.ToCssKeyframes("drop", effects.Single());
        css.ShouldBe("@keyframes drop { 0% { translate: 0px 40%; animation-timing-function: ease-out; } 100% { translate: 0px 0px; animation-timing-function: linear; } }");
    }
}

namespace Shiny.Blazor.Controls.Keyframe.Tests;

/// <summary>The decisions the components make on the .NET side: placement in time, state matching, transitions, cues.</summary>
public class ComponentLogicTests
{
    static NodeProgram Node(double durationMs, double iterations = 1)
    {
        var animation = KeyframeCompiler.Compile(new AnimationSpec(
            new TimingSpec { Duration = TimeSpan.FromMilliseconds(durationMs), Iterations = iterations },
            [new TrackSpec("Opacity", [new KeySpec(0, "0", null), new KeySpec(1, "1", null)])]));
        return new NodeProgram([new ProgramItem(new TargetSpec(0, null), animation, 0)]);
    }

    [Fact]
    public void ParallelStartsEachChildAStaggerAfterTheLast()
    {
        var items = Choreography.Place([Node(300), Node(300), Node(300)], ChoreographyMode.Parallel, 50);

        items.Select(i => i.OffsetMs).ShouldBe([0d, 50d, 100d]);
    }

    [Fact]
    public void SequenceStartsEachChildWhenTheLastEndsPlusTheGap()
    {
        var items = Choreography.Place([Node(300), Node(200), Node(100)], ChoreographyMode.Sequence, 50);

        items.Select(i => i.OffsetMs).ShouldBe([0d, 350d, 600d]);
    }

    [Fact]
    public void NothingInASequenceStartsAfterALoop()
    {
        var items = Choreography.Place([Node(300), Node(300, Keyframes.Infinite), Node(300)], ChoreographyMode.Sequence, 0);

        items.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("60%", 0.6)]
    [InlineData("0.6", 0.6)]
    [InlineData("from", 0)]
    [InlineData("to", 1)]
    [InlineData("150%", 1)]
    public void CuesReadPercentagesFractionsAndKeywords(string cue, double expected)
        => KeyFrame.ParseCue(cue).ShouldBe(expected, 1e-9);

    [Fact]
    public void ABadCueSaysWhatItWanted()
        => Should.Throw<FormatException>(() => KeyFrame.ParseCue("soon")).Message.ShouldContain("60%");


    static VisualTransition Transition(string? from, string? to, int ms = 100)
        => new() { From = from, To = to, Duration = TimeSpan.FromMilliseconds(ms) };

    [Fact]
    public void TheMostSpecificTransitionWins()
    {
        var any = Transition("*", "*", 1);
        var toPressed = Transition(null, "Pressed", 2);
        var fromNormal = Transition("Normal", null, 3);
        var exact = Transition("Normal", "Pressed", 4);
        var all = new[] { any, toPressed, fromNormal, exact };

        VisualStates.Match(all, "Normal", "Pressed").ShouldBe(exact);
        VisualStates.Match(all, "Focused", "Pressed").ShouldBe(toPressed);
        VisualStates.Match(all, "Normal", "Focused").ShouldBe(fromNormal);
        VisualStates.Match(all, "Focused", "Normal").ShouldBe(any);
    }

    [Fact]
    public void NoTransitionMeansTheChangeSnaps()
        => VisualStates.Match([Transition("A", "B")], "B", "A").ShouldBeNull();

    [Fact]
    public void TransitionsCoverThePropertiesEitherStateSets()
    {
        var from = new VisualState { Name = "Normal", Style = "opacity: 1" };
        var to = new VisualState { Name = "Pressed", Style = "scale: .96; opacity: .8;" };

        VisualStates.TransitionProperties(from, to).ShouldBe(["opacity", "scale"]);
        VisualStates.TransitionProperties(null, new VisualState { Name = "X", Class = "busy" }).ShouldBe(["all"]);
    }

    [Fact]
    public void ImplicitTransitionsListTransformPartsUnderBothSpellings()
    {
        var css = ImplicitTransitions.TransitionCss(
        [
            new PropertyTransition { Property = "Opacity", Duration = TimeSpan.FromMilliseconds(200), Easing = "EaseOut" },
            new PropertyTransition { Property = "Scale", Duration = TimeSpan.FromMilliseconds(300), Easing = "spring(0.5, 10)" }
        ]);

        css.ShouldStartWith("opacity 200ms ease-out 0ms, scale 300ms linear(");
        css.ShouldContain("transform 300ms linear(");
    }

    [Fact]
    public void AllTransitionsEverything()
        => ImplicitTransitions.TransitionCss([new PropertyTransition { Property = "All" }]).ShouldBe("all 250ms ease 0ms");

    [Fact]
    public void ItemExitsArePresetsRunBackwards()
    {
        var exit = ItemTransitionPresets.Exit(ItemTransition.FadeUp);

        var opacity = exit.Single(t => t.Property == "Opacity");
        opacity.Keys.Select(k => (k.Offset, k.Value)).ShouldBe([(0d, "1"), (1d, "0")]);

        var translate = exit.Single(t => t.Property == "TranslationY");
        translate.Keys.Select(k => k.Value).ShouldBe(["0", "16"]);
    }

    [Fact]
    public void CustomPageTransitionsBecomeNamedKeyframesRules()
    {
        var part = CustomPageTransition.Part("drop in", "enter", new AnimationSpec(new TimingSpec(),
        [
            new TrackSpec("Opacity", [new KeySpec(0, "0", null), new KeySpec(1, "1", null)]),
            new TrackSpec("TranslationY", [new KeySpec(0, "-40", null), new KeySpec(1, "0", null)])
        ]))!;

        part.Names.ShouldBe(["shiny-vt-c-drop-in-enter-0", "shiny-vt-c-drop-in-enter-1"]);
        part.Css.ShouldContain("@keyframes shiny-vt-c-drop-in-enter-0 {");
        part.Css.ShouldContain("translate: 0px -40px;");
    }
}

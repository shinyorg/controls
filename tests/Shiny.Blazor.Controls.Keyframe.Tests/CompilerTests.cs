namespace Shiny.Blazor.Controls.Keyframe.Tests;

/// <summary>Authored tracks → the keyframes and options the Web Animations API runs.</summary>
public class CompilerTests
{
    static KeySpec K(double offset, string? value, string? easing = null) => new(offset, value, easing);

    static TrackSpec T(string property, params KeySpec[] keys) => new(property, keys);

    static CompiledAnimation Compile(TimingSpec? timing = null, IReadOnlyDictionary<string, string>? live = null, params TrackSpec[] tracks)
        => KeyframeCompiler.Compile(new AnimationSpec(timing ?? new TimingSpec(), tracks), live);

    static CompiledEffect Effect(CompiledAnimation animation, string css) => animation.Effects.Single(e => e.CssProperty == css);

    static string[] Values(CompiledEffect effect) => effect.Frames.Select(f => f.Values[effect.CssProperty]).ToArray();


    [Fact]
    public void OpacityBecomesOneEffectWithAFramePerKey()
    {
        var effect = Effect(Compile(tracks: T("Opacity", K(0, "0"), K(1, "1"))), "opacity");

        effect.Frames.Select(f => f.Offset).ShouldBe([0d, 1d]);
        Values(effect).ShouldBe(["0", "1"]);
    }

    [Fact]
    public void EachCssPropertyGetsItsOwnEffect()
    {
        var animation = Compile(tracks:
        [
            T("Opacity", K(0, "0"), K(1, "1")),
            T("BackgroundColor", K(0, "#2563EB"), K(1, "#EC4899")),
            T("Rotation", K(0, "0"), K(1, "90"))
        ]);

        animation.Effects.Select(e => e.CssProperty).ShouldBe(["opacity", "background-color", "rotate"], ignoreOrder: true);
    }

    [Fact]
    public void AKeysEasingIsTheCurveOfTheSegmentItStarts()
    {
        var effect = Effect(Compile(tracks: T("Opacity", K(0, "0", "EaseOut"), K(1, "1", "BounceOut"))), "opacity");

        effect.Frames[0].EasingCss.ShouldBe("ease-out");
        effect.Frames[1].EasingCss.ShouldStartWith("linear(");
    }

    [Fact]
    public void SpringsAreSampledIntoCssLinear()
    {
        var effect = Effect(Compile(tracks: T("Scale", K(0, "0.5", "spring(0.3, 12)"), K(1, "1"))), "scale");

        effect.Frames[0].EasingCss.ShouldBe(EasingSyntax.ToCss("spring(0.3, 12)"));
        effect.Frames[0].EasingCss.ShouldStartWith("linear(");
    }

    [Fact]
    public void CubicBezierAndStepsStayNative()
    {
        var effect = Effect(Compile(tracks: T("Opacity", K(0, "0", "cubic-bezier(0.34, 1.56, 0.64, 1)"), K(0.5, "0.5", "steps(4)"), K(1, "1"))), "opacity");

        effect.Frames[0].EasingCss.ShouldBe("cubic-bezier(0.34, 1.56, 0.64, 1)");
        effect.Frames[1].EasingCss.ShouldBe("steps(4, jump-end)");
    }

    [Fact]
    public void AnUnknownCurveFailsWithTheAlternatives()
        => Should.Throw<FormatException>(() => Compile(tracks: T("Opacity", K(0, "0", "Wobbly"), K(1, "1"))))
            .Message.ShouldContain("CubicOut");

    [Fact]
    public void TheFirstAndLastValuesHoldOutToTheEnds()
    {
        var effect = Effect(Compile(tracks: T("Opacity", K(0.25, "0.2"), K(0.75, "0.8"))), "opacity");

        effect.Frames.Select(f => f.Offset).ShouldBe([0d, 0.25, 0.75, 1d]);
        Values(effect).ShouldBe(["0.2", "0.2", "0.8", "0.8"]);
    }

    [Fact]
    public void KeysAreSortedByOffset()
    {
        var effect = Effect(Compile(tracks: T("Opacity", K(1, "1"), K(0, "0"), K(0.5, "0.4"))), "opacity");

        Values(effect).ShouldBe(["0", "0.4", "1"]);
    }

    [Fact]
    public void RotationTakesTheShortestArc()
    {
        var effect = Effect(Compile(tracks: T("Rotation", K(0, "350"), K(1, "10"))), "rotate");

        Values(effect).ShouldBe(["350deg", "370deg"]);
    }

    [Fact]
    public void SpinTurnsAsFarAsTheKeysSay()
    {
        var effect = Effect(Compile(tracks: T("Spin", K(0, "0"), K(1, "720"))), "rotate");

        Values(effect).ShouldBe(["0deg", "720deg"]);
    }

    [Fact]
    public void ColoursBlendInOklab()
    {
        var effect = Effect(Compile(tracks: T("BackgroundColor", K(0, "#FF0000"), K(1, "white"))), "background-color");

        Values(effect)[0].ShouldStartWith("oklab(0.628");
        Values(effect)[1].ShouldBe("oklab(1 0 0 / 1)");
    }

    [Fact]
    public void AColourThatIsNotParseableIsKept()
    {
        var effect = Effect(Compile(tracks: T("BackgroundColor", K(0, "var(--accent)"), K(1, "currentColor"))), "background-color");

        Values(effect).ShouldBe(["var(--accent)", "currentColor"]);
    }

    [Fact]
    public void LengthsArePixelsUnlessAUnitIsGiven()
    {
        var effect = Effect(Compile(tracks: T("WidthRequest", K(0, "120"), K(1, "50%"))), "width");

        Values(effect).ShouldBe(["120px", "50%"]);
    }

    [Theory]
    [InlineData("10", "10px")]
    [InlineData("10,20", "20px 10px 20px 10px")]
    [InlineData("1,2,3,4", "2px 3px 4px 1px")]
    [InlineData("4px 8px", "4px 8px")]
    public void MarginTakesTheMauiThicknessSyntax(string maui, string css)
        => Values(Effect(Compile(tracks: T("Margin", K(0, maui), K(1, maui))), "margin"))[0].ShouldBe(css);

    [Fact]
    public void AnyOtherNameIsARawCssProperty()
    {
        var animation = Compile(tracks:
        [
            T("filter", K(0, "blur(0px)"), K(1, "blur(4px)")),
            T("BorderRadius", K(0, "0px"), K(1, "24px")),
            T("--glow", K(0, "0"), K(1, "1"))
        ]);

        animation.Effects.Select(e => e.CssProperty).ShouldBe(["filter", "border-radius", "--glow"], ignoreOrder: true);
    }


    // ---------------------------------------------------------------- merged transform parts

    [Fact]
    public void TranslationXAndYOnOneScheduleAreZipped()
    {
        var effect = Effect(Compile(tracks:
        [
            T("TranslationX", K(0, "0", "CubicOut"), K(1, "100")),
            T("TranslationY", K(0, "0", "CubicOut"), K(1, "50"))
        ]), "translate");

        Values(effect).ShouldBe(["0px 0px", "100px 50px"]);
        effect.Frames[0].EasingCss.ShouldBe(EasingSyntax.ToCss("CubicOut"));
    }

    [Fact]
    public void DifferentSchedulesAreSampledSoEachAxisKeepsItsCurve()
    {
        var effect = Effect(Compile(tracks:
        [
            T("TranslationX", K(0, "0"), K(1, "100")),
            T("TranslationY", K(0, "0", "CubicOut"), K(0.5, "40"), K(1, "0"))
        ]), "translate");

        effect.Frames.Count.ShouldBe(KeyframeCompiler.DenseSamples + 1);
        effect.Frames.ShouldAllBe(f => f.EasingCss == "linear");

        var half = effect.Frames.Single(f => Math.Abs(f.Offset - 0.5) < 1e-9);
        half.Values["translate"].ShouldBe("50px 40px");

        // A quarter of the way, X is linear (25) and Y is CubicOut-eased halfway to 40 (35).
        var quarter = effect.Frames.Single(f => Math.Abs(f.Offset - 0.25) < 1e-9);
        quarter.Values["translate"].ShouldBe("25px 35px");
    }

    [Fact]
    public void ALoneTranslationXKeepsTheElementsLiveY()
    {
        var needs = KeyframeCompiler.LiveNeeds([T("TranslationX", K(0, "0"), K(1, "100"))]);
        needs.ShouldBe(["TranslationY"]);

        var effect = Effect(Compile(live: new Dictionary<string, string> { ["TranslationY"] = "12" }, tracks: T("TranslationX", K(0, "0"), K(1, "100"))), "translate");
        Values(effect).ShouldBe(["0px 12px", "100px 12px"]);
    }

    [Fact]
    public void ScaleAloneIsUniform()
    {
        var effect = Effect(Compile(tracks: T("Scale", K(0, "1"), K(1, "1.5"))), "scale");

        Values(effect).ShouldBe(["1", "1.5"]);
        KeyframeCompiler.LiveNeeds([T("Scale", K(0, "1"), K(1, "1.5"))]).ShouldBeEmpty();
    }

    [Fact]
    public void ScaleMultipliesWithScaleXAsOnMaui()
    {
        var effect = Effect(Compile(tracks:
        [
            T("Scale", K(0, "2"), K(1, "2")),
            T("ScaleX", K(0, "1"), K(1, "3"))
        ]), "scale");

        Values(effect)[0].ShouldBe("2 2");
        Values(effect)[^1].ShouldBe("6 2");
    }

    [Fact]
    public void RotationXAndYBecomeA3DTransformWithPerspective()
    {
        var effect = Effect(Compile(tracks:
        [
            T("RotationX", K(0, "0"), K(1, "45")),
            T("RotationY", K(0, "0"), K(1, "90"))
        ]), "transform");

        Values(effect)[^1].ShouldBe($"{KeyframeCompiler.Perspective} rotateX(45deg) rotateY(90deg)");
    }

    [Fact]
    public void AnchorsBecomeATransformOriginInPercent()
    {
        var effect = Effect(Compile(tracks:
        [
            T("AnchorX", K(0, "0"), K(1, "0")),
            T("AnchorY", K(0, "1"), K(1, "1"))
        ]), "transform-origin");

        Values(effect)[0].ShouldBe("0% 100%");
    }

    [Fact]
    public void ScaleAndTranslateStaySeparateSoTheyCompose()
    {
        var animation = Compile(tracks:
        [
            T("Scale", K(0, "1"), K(1, "2")),
            T("TranslationY", K(0, "0"), K(1, "-20"))
        ]);

        animation.Effects.Select(e => e.CssProperty).ShouldBe(["scale", "translate"], ignoreOrder: true);
    }


    // ---------------------------------------------------------------- live values

    [Fact]
    public void AKeyWithNoValueStartsFromTheLiveValue()
    {
        var tracks = new[] { T("Opacity", K(0, null), K(1, "1")) };
        KeyframeCompiler.LiveNeeds(tracks).ShouldBe(["Opacity"]);

        var effect = Effect(Compile(live: new Dictionary<string, string> { ["Opacity"] = "0.35" }, tracks: tracks), "opacity");
        Values(effect).ShouldBe(["0.35", "1"]);
    }

    [Fact]
    public void WithNoReadingAMissingValueIsTheRestingValue()
    {
        var effect = Effect(Compile(tracks: T("Opacity", K(0, null), K(1, "0"))), "opacity");

        Values(effect).ShouldBe(["1", "0"]);
    }

    [Fact]
    public void InTemplateModeAMissingValueIsLeftToTheBrowser()
    {
        var animation = KeyframeCompiler.Compile(
            new AnimationSpec(new TimingSpec(), [T("Opacity", K(0, "0"), K(1, null))]), live: null, implicitEnds: true);

        var effect = Effect(animation, "opacity");
        effect.Frames.Single().Offset.ShouldBe(0d);
    }


    // ---------------------------------------------------------------- timing

    [Fact]
    public void TimingMapsOntoWebAnimationsOptions()
    {
        var animation = Compile(new TimingSpec
        {
            Duration = TimeSpan.FromMilliseconds(800),
            Delay = TimeSpan.FromMilliseconds(100),
            EndDelay = TimeSpan.FromMilliseconds(50),
            Iterations = 3,
            IterationStart = 0.5,
            Direction = PlaybackDirection.AlternateReverse,
            Fill = FillMode.Both,
            Easing = "EaseInOut"
        }, tracks: T("Opacity", K(0, "0"), K(1, "1")));

        animation.Timing.ShouldBe(new CompiledTiming(800, 100, 50, 3, 0.5, "alternate-reverse", "both", "ease-in-out"));
        animation.TotalMs.ShouldBe(100 + 800 * 3 + 50);
    }

    [Fact]
    public void ALoopHasNoEndButAFirstPassToScrub()
    {
        var animation = Compile(new TimingSpec { Duration = TimeSpan.FromSeconds(1), Delay = TimeSpan.FromMilliseconds(200), Iterations = Keyframes.Infinite },
            tracks: T("Opacity", K(0, "0"), K(1, "1")));

        animation.TotalMs.ShouldBe(double.PositiveInfinity);
        animation.FirstPassMs.ShouldBe(1200);
    }

    [Fact]
    public void IterationDelayIsBakedIntoTheKeyframes()
    {
        // 600ms passes with 400ms between them: each pass becomes 1000ms with 200ms held at each end.
        var animation = Compile(new TimingSpec
        {
            Duration = TimeSpan.FromMilliseconds(600),
            IterationDelay = TimeSpan.FromMilliseconds(400),
            Iterations = 3,
            Delay = TimeSpan.FromMilliseconds(100)
        }, tracks: T("Opacity", K(0, "0"), K(1, "1")));

        animation.Timing.DurationMs.ShouldBe(1000);
        animation.Timing.DelayMs.ShouldBe(100 - 200);
        animation.Timing.EndDelayMs.ShouldBe(-200);

        var effect = Effect(animation, "opacity");
        effect.Frames.Select(f => Math.Round(f.Offset, 6)).ShouldBe([0d, 0.2, 0.8, 1d]);
        Values(effect).ShouldBe(["0", "0", "1", "1"]);

        // The wall-clock length is the shared engine's: 3 passes and the 2 gaps between them.
        animation.TotalMs.ShouldBe(100 + 600 * 3 + 400 * 2);
    }

    [Fact]
    public void IterationDelayFoldsTheTimingEasingIntoTheSegments()
    {
        var animation = Compile(new TimingSpec
        {
            Duration = TimeSpan.FromMilliseconds(500),
            IterationDelay = TimeSpan.FromMilliseconds(500),
            Iterations = Keyframes.Infinite,
            Easing = "CubicOut"
        }, tracks: T("Opacity", K(0, "0"), K(1, "1")));

        animation.Timing.Easing.ShouldBe("linear");
        var motion = Effect(animation, "opacity").Frames[1];
        motion.EasingCss.ShouldStartWith("linear(");
        // The folded curve is the timing curve itself for a linear 0→1 track.
        motion.Easing(0.5).ShouldBe(Easings.CubicOut(0.5), 1e-9);
    }

    [Fact]
    public void IterationDelayIsIgnoredForASinglePass()
    {
        var animation = Compile(new TimingSpec { Duration = TimeSpan.FromMilliseconds(500), IterationDelay = TimeSpan.FromSeconds(1) },
            tracks: T("Opacity", K(0, "0"), K(1, "1")));

        animation.Timing.DurationMs.ShouldBe(500);
        Effect(animation, "opacity").Frames.Count.ShouldBe(2);
    }

    [Fact]
    public void IterationStartIsMappedIntoTheLengthenedPass()
    {
        var animation = Compile(new TimingSpec
        {
            Duration = TimeSpan.FromMilliseconds(600),
            IterationDelay = TimeSpan.FromMilliseconds(400),
            Iterations = 2,
            IterationStart = 0.5
        }, tracks: T("Opacity", K(0, "0"), K(1, "1")));

        // Halfway through the motion: 200ms hold + 300ms into 600ms, of a 1000ms pass.
        animation.Timing.IterationStart.ShouldBe(0.5, 1e-9);
    }

    [Fact]
    public void InverseFindsWhenACurveReachesAProgress()
    {
        var t = KeyframeCompiler.Inverse(Easings.CubicOut, 0.875);
        t.ShouldBe(0.5, 1e-6);
        KeyframeCompiler.Inverse(Easings.CubicOut, 0).ShouldBe(0);
        KeyframeCompiler.Inverse(Easings.CubicOut, 1).ShouldBe(1);
    }

    [Theory]
    [InlineData(350, 10, 20)]
    [InlineData(10, 350, -20)]
    [InlineData(0, 180, 180)]
    [InlineData(0, 540, 180)]
    public void ShortestDeltaNeverTurnsMoreThanHalfway(double from, double to, double expected)
        => KeyframeCompiler.ShortestDelta(from, to).ShouldBe(expected);
}

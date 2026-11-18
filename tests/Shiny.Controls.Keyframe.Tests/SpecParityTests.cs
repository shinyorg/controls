using Shiny.Controls.Keyframe;

namespace Shiny.Controls.Keyframe.Tests;

/// <summary>The engine additions that close the gaps with the MAUI animation spec (dotnet/maui#40382).</summary>
public class SpecParityTests
{
    sealed class Probe
    {
        public double Value { get; set; }
        public int Releases { get; set; }
    }

    static Timeline TimelineFor(Probe probe, Action<Timing>? configure = null)
    {
        var timing = new Timing { Duration = TimeSpan.FromSeconds(1) };
        configure?.Invoke(timing);

        return new Timeline(timing).Add(new Track<Probe, double>(
            probe,
            static (p, v) => p.Value = v,
            [new Key<double>(0d, 0d), new Key<double>(1d, 100d)],
            DoubleInterpolator.Instance,
            static p => p.Value,
            release: static p => p.Releases++));
    }

    // --- IterationDelay -----------------------------------------------------------------

    [Fact]
    public void IterationDelayAddsGapsBetweenIterationsOnly()
    {
        var timing = new Timing { Duration = TimeSpan.FromSeconds(1), Iterations = 3, IterationDelay = TimeSpan.FromMilliseconds(500) };

        Assert.Equal(TimeSpan.FromSeconds(4), timing.ActiveDuration);
    }

    [Fact]
    public void IterationDelayHoldsTheEndPoseDuringTheGap()
    {
        var timing = new Timing { Duration = TimeSpan.FromSeconds(1), Iterations = 2, IterationDelay = TimeSpan.FromMilliseconds(500) };

        var inGap = timing.Sample(TimeSpan.FromMilliseconds(1250));
        Assert.True(inGap.ShouldApply);
        Assert.Equal(1d, inGap.Progress, 6);
        Assert.Equal(0, inGap.Iteration);

        var second = timing.Sample(TimeSpan.FromMilliseconds(1750));
        Assert.Equal(0.25d, second.Progress, 6);
        Assert.Equal(1, second.Iteration);
    }

    [Fact]
    public void IterationDelayWithAlternateHoldsTheTurnaround()
    {
        var timing = new Timing
        {
            Duration = TimeSpan.FromSeconds(1),
            Iterations = 2,
            Direction = PlaybackDirection.Alternate,
            IterationDelay = TimeSpan.FromMilliseconds(500)
        };

        Assert.Equal(1d, timing.Sample(TimeSpan.FromMilliseconds(1400)).Progress, 6);
        Assert.Equal(0.75d, timing.Sample(TimeSpan.FromMilliseconds(1750)).Progress, 6);
    }

    [Fact]
    public void NegativeIterationDelayIsRejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new Timing { IterationDelay = TimeSpan.FromSeconds(-1) });

    // --- Release (fill none) ------------------------------------------------------------

    [Fact]
    public void LeavingTheActiveWindowWithoutFillReleasesOnce()
    {
        var probe = new Probe();
        var timeline = TimelineFor(probe);
        timeline.CaptureBaselines();

        timeline.Evaluate(TimeSpan.FromMilliseconds(500));
        timeline.Evaluate(TimeSpan.FromSeconds(2));
        timeline.Evaluate(TimeSpan.FromSeconds(3));

        Assert.Equal(1, probe.Releases);
    }

    [Fact]
    public void ForwardsFillNeverReleases()
    {
        var probe = new Probe();
        var timeline = TimelineFor(probe, t => t.Fill = FillMode.Forwards);
        timeline.CaptureBaselines();

        timeline.Evaluate(TimeSpan.FromMilliseconds(500));
        timeline.Evaluate(TimeSpan.FromSeconds(2));

        Assert.Equal(0, probe.Releases);
        Assert.Equal(100d, probe.Value, 6);
    }

    [Fact]
    public void StopWithReleaseWithdrawsAndHoldDoesNot()
    {
        var probe = new Probe();
        var clock = new ManualClock();
        using var player = new Player(TimelineFor(probe, t => t.Fill = FillMode.Forwards), clock);

        player.Play();
        clock.Advance(TimeSpan.FromMilliseconds(300));
        player.Stop(StopBehavior.Hold);
        Assert.Equal(0, probe.Releases);

        player.Play();
        clock.Advance(TimeSpan.FromMilliseconds(300));
        player.Stop(StopBehavior.Release);
        Assert.Equal(1, probe.Releases);
    }

    // --- Reduced motion -----------------------------------------------------------------

    [Fact]
    public void InstantReducedMotionJumpsToTheEndAndFinishes()
    {
        var probe = new Probe();
        var clock = new ManualClock();
        using var player = new Player(TimelineFor(probe, t => t.Fill = FillMode.Forwards), clock)
        {
            ReducedMotion = ReducedMotionBehavior.Instant
        };

        var finished = false;
        player.Finished += (_, _) => finished = true;
        player.Play();

        Assert.True(finished);
        Assert.Equal(PlaybackState.Finished, player.State);
        Assert.Equal(100d, probe.Value, 6);
    }

    [Fact]
    public async Task InstantReducedMotionCompletesPlayAsync()
    {
        var probe = new Probe();
        using var player = new Player(TimelineFor(probe, t => t.Fill = FillMode.Forwards), new ManualClock())
        {
            ReducedMotion = ReducedMotionBehavior.Instant
        };

        Assert.True(await player.PlayAsync());
    }

    [Fact]
    public void InstantReducedMotionHoldsALoopOnItsOpeningPose()
    {
        var probe = new Probe { Value = 42 };
        using var player = new Player(TimelineFor(probe, t => t.Iterations = double.PositiveInfinity), new ManualClock())
        {
            ReducedMotion = ReducedMotionBehavior.Instant
        };

        player.Play();

        Assert.Equal(0d, probe.Value, 6);
        Assert.Equal(PlaybackState.Finished, player.State);
    }

    [Fact]
    public void PlayBehaviorIgnoresReducedMotion()
    {
        Assert.False(MotionPolicy.ShouldSkip(ReducedMotionBehavior.Play));
        Assert.True(MotionPolicy.ShouldSkip(ReducedMotionBehavior.Instant));
    }

    [Fact]
    public void SeekingIsNeverAffectedByReducedMotion()
    {
        var probe = new Probe();
        using var player = new Player(TimelineFor(probe), new ManualClock()) { ReducedMotion = ReducedMotionBehavior.Instant };

        player.SeekProgress(0.25);

        Assert.Equal(25d, probe.Value, 6);
    }

    // --- Easing syntax ------------------------------------------------------------------

    [Theory]
    [InlineData("CubicOut")]
    [InlineData("Hold")]
    [InlineData("cubic-bezier(0.34, 1.56, 0.64, 1)")]
    [InlineData("CubicBezier(0.34, 1.56, 0.64, 1)")]
    [InlineData("Spring(0.4)")]
    [InlineData("spring(0.35, 14)")]
    [InlineData("bouncy(0.3)")]
    [InlineData("steps(4)")]
    [InlineData("steps(4, jump-start)")]
    [InlineData("ease-in-out")]
    public void EasingSyntaxReadsEveryForm(string text)
    {
        var curve = EasingSyntax.Get(text);

        Assert.Equal(1d, curve(1d), 3);
    }

    [Fact]
    public void HoldStaysUntilTheSegmentEnds()
    {
        Assert.Equal(0d, Easings.Hold(0.99d));
        Assert.Equal(1d, Easings.Hold(1d));
    }

    [Fact]
    public void BouncyZeroDoesNotOvershoot()
    {
        var curve = Easings.Bouncy(0d);
        var peak = Enumerable.Range(0, 101).Max(i => curve(i / 100d));

        Assert.True(peak <= 1.0001d);
    }

    [Theory]
    [InlineData(null, "linear")]
    [InlineData("Linear", "linear")]
    [InlineData("EaseInOut", "ease-in-out")]
    [InlineData("Hold", "step-end")]
    [InlineData("Emphasized", "cubic-bezier(0.2, 0, 0, 1)")]
    [InlineData("cubic-bezier(0.25, 0.1, 0.25, 1)", "cubic-bezier(0.25, 0.1, 0.25, 1)")]
    [InlineData("steps(3)", "steps(3, jump-end)")]
    public void ToCssPassesNativeCurvesThrough(string? text, string css)
        => Assert.Equal(css, EasingSyntax.ToCss(text));

    [Fact]
    public void ToCssSamplesSpringsIntoLinearWithOvershoot()
    {
        var css = EasingSyntax.ToCss("spring(0.3, 14)");

        Assert.StartsWith("linear(0 0%", css);
        Assert.EndsWith("1 100%)", css);

        // An underdamped spring overshoots; linear() must carry a stop above 1.
        var stops = css["linear(".Length..^1].Split(", ").Select(s => double.Parse(s.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains(stops, v => v > 1d);
    }

    [Fact]
    public void ToCssRejectsUnknownCurves()
        => Assert.Throws<FormatException>(() => EasingSyntax.ToCss("wobble"));
}

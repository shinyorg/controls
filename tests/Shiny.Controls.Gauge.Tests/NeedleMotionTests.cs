namespace Shiny.Controls.Gauge.Tests;

public class NeedleMotionTests
{
    const double Frame = 1.0 / 60;

    static int RunUntilSettled(NeedleMotion motion, int maxFrames = 2000, Action<NeedleMotion>? eachFrame = null)
    {
        var frames = 0;
        while (motion.IsMoving && frames < maxFrames)
        {
            motion.Step(Frame);
            eachFrame?.Invoke(motion);
            frames++;
        }
        return frames;
    }


    [Fact]
    public void Spring_ArrivesAndComesToRestOnTheTarget()
    {
        var motion = new NeedleMotion(0, 100);
        motion.SetTarget(60);

        var frames = RunUntilSettled(motion);

        motion.IsMoving.ShouldBeFalse();
        motion.Position.ShouldBe(60);
        motion.Velocity.ShouldBe(0);
        frames.ShouldBeLessThan(600);
    }


    [Fact]
    public void Spring_UnderdampedOvershoots()
    {
        var motion = new NeedleMotion(0, 100) { Damping = 0.4 };
        motion.SetTarget(50);

        var peak = 0.0;
        RunUntilSettled(motion, eachFrame: m => peak = Math.Max(peak, m.Position));

        peak.ShouldBeGreaterThan(50.5);
    }


    [Fact]
    public void Spring_CriticallyDampedDoesNotOvershoot()
    {
        var motion = new NeedleMotion(0, 100) { Damping = 1 };
        motion.SetTarget(50);

        var peak = 0.0;
        RunUntilSettled(motion, eachFrame: m => peak = Math.Max(peak, m.Position));

        peak.ShouldBeLessThanOrEqualTo(50.0001);
    }


    [Fact]
    public void Spring_NeverPassesThePegs()
    {
        var motion = new NeedleMotion(0, 100) { Damping = 0.2 };
        motion.SetTarget(100);

        RunUntilSettled(motion, eachFrame: m => m.Position.ShouldBeInRange(0, 100));
        motion.Position.ShouldBe(100);
    }


    [Fact]
    public void Spring_KeepsItsMomentumThroughARetarget()
    {
        var motion = new NeedleMotion(0, 100);
        motion.SetTarget(100);
        for (var i = 0; i < 10; i++)
            motion.Step(Frame);

        var velocity = motion.Velocity;
        velocity.ShouldBeGreaterThan(0);

        // A reading slightly lower than where it is heading must not stop the needle dead.
        motion.SetTarget(80);
        motion.Velocity.ShouldBe(velocity);
    }


    [Fact]
    public void Ease_TakesItsDuration()
    {
        var motion = new NeedleMotion(0, 100) { Mode = GaugeAnimation.Ease, Duration = 0.5 };
        motion.SetTarget(40);

        var frames = RunUntilSettled(motion);

        frames.ShouldBeInRange(29, 31);
        motion.Position.ShouldBe(40);
    }


    [Fact]
    public void None_Snaps()
    {
        var motion = new NeedleMotion(0, 100) { Mode = GaugeAnimation.None };
        motion.SetTarget(70);

        motion.Position.ShouldBe(70);
        motion.IsMoving.ShouldBeFalse();
    }


    [Fact]
    public void ZeroDuration_Snaps()
    {
        var motion = new NeedleMotion(0, 100) { Duration = 0 };
        motion.SetTarget(70);
        motion.Position.ShouldBe(70);
    }


    [Theory]
    [InlineData(GaugeAnimation.Spring)]
    [InlineData(GaugeAnimation.Ease)]
    public void Sweep_TouchesTheMaximumThenReturns(GaugeAnimation mode)
    {
        var motion = new NeedleMotion(0, 8) { Mode = mode };
        motion.Sweep(1);
        motion.IsSweeping.ShouldBeTrue();

        var peak = 0.0;
        RunUntilSettled(motion, eachFrame: m => peak = Math.Max(peak, m.Position));

        peak.ShouldBeGreaterThan(7.9);
        motion.Position.ShouldBe(1);
        motion.IsSweeping.ShouldBeFalse();
    }


    [Fact]
    public void SetTarget_DuringASweepChangesWhereItEnds()
    {
        var motion = new NeedleMotion(0, 100);
        motion.Sweep(10);
        motion.Step(Frame);

        motion.SetTarget(35);
        motion.Target.ShouldBe(100);
        motion.FinalTarget.ShouldBe(35);

        RunUntilSettled(motion);
        motion.Position.ShouldBe(35);
    }


    [Fact]
    public void Targets_AreClampedToThePegs()
    {
        var motion = new NeedleMotion(0, 100) { Mode = GaugeAnimation.None };
        motion.SetTarget(500);
        motion.Position.ShouldBe(100);
        motion.SetTarget(double.NaN);
        motion.Position.ShouldBe(0);
    }


    [Fact]
    public void SetBounds_PullsTheNeedleInside()
    {
        var motion = new NeedleMotion(0, 100);
        motion.Snap(90);
        motion.SetBounds(0, 50);
        motion.Position.ShouldBe(50);
    }


    [Fact]
    public void Step_IgnoresGarbageDeltas()
    {
        var motion = new NeedleMotion(0, 100);
        motion.SetTarget(50);
        motion.Step(double.NaN).ShouldBeFalse();
        motion.Step(-1).ShouldBeFalse();
        motion.Position.ShouldBe(0);
    }
}

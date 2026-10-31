namespace Shiny.Controls.Gauge;

/// <summary>How the needle travels to a new value.</summary>
public enum GaugeAnimation
{
    /// <summary>
    /// A damped spring, like the needle on a real instrument: it accelerates, carries its momentum
    /// through a change of target, and with damping under 1 overshoots and settles back.
    /// </summary>
    Spring,

    /// <summary>A fixed-length ease-out from wherever the needle is to the new value.</summary>
    Ease,

    /// <summary>The needle jumps straight to the value.</summary>
    None
}


/// <summary>The shape of the needle.</summary>
public enum GaugeNeedleStyle
{
    /// <summary>A pointer that tapers from the hub to the tip, with a short counterweight tail.</summary>
    Tapered,

    /// <summary>A single stroke from the hub to the tip.</summary>
    Line,

    /// <summary>No needle - the value arc alone shows the value, for a modern ring-style gauge.</summary>
    None
}


/// <summary>
/// The needle's motion: where it is, how fast it is moving and where it is heading, advanced one
/// frame at a time by the host's clock.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a simulation and not a tween.</b> A tween restarts from rest every time the target changes,
/// so a value that updates ten times a second - which is what a live RPM or speed feed does - makes
/// the needle stutter: it decelerates towards each reading and starts again from nothing. A spring
/// keeps its velocity, so the same feed produces one continuous sweep that lags the readings slightly
/// the way a physical needle lags the engine. <see cref="GaugeAnimation.Ease"/> exists for the cases
/// that want a predictable duration instead.
/// </para>
/// <para>
/// <b>Pegs.</b> A real needle cannot swing past its end stops. An overshoot that would carry it past
/// <see cref="Minimum"/> or <see cref="Maximum"/> hits the peg and bounces back a little, so revving
/// into the redline looks like it does in a car rather than wrapping off the end of the dial.
/// </para>
/// <para>
/// The Blazor host runs the same rules in <c>gauge.js</c>, because a needle animated from .NET would
/// cost a render batch per frame (and on Blazor Server, a network round trip). The constants below
/// are mirrored there; change one, change both.
/// </para>
/// </remarks>
public sealed class NeedleMotion
{
    /// <summary>The largest single integration step, in seconds. Smaller frames are taken whole.</summary>
    public const double MaximumSubstep = 1.0 / 240;

    /// <summary>How much speed survives a bounce off a peg.</summary>
    public const double PegRestitution = 0.3;

    /// <summary>
    /// Closer than this fraction of the range, and slower than this fraction of the range per second,
    /// the needle counts as settled and snaps onto its target.
    /// </summary>
    public const double SettleFraction = 0.0005;

    /// <summary>Closer than this fraction of the range, a sweep waypoint counts as reached.</summary>
    public const double WaypointFraction = 0.01;

    readonly List<double> waypoints = [];
    double easeFrom;
    double easeElapsed;


    /// <summary>Creates a needle resting at zero on a 0-100 dial.</summary>
    public NeedleMotion() : this(0, 100) { }


    /// <summary>Creates a needle resting at the minimum of a dial.</summary>
    public NeedleMotion(double minimum, double maximum)
    {
        this.SetBounds(minimum, maximum);
        this.Position = this.Minimum;
        this.Target = this.Minimum;
    }


    /// <summary>How the needle travels.</summary>
    public GaugeAnimation Mode { get; set; } = GaugeAnimation.Spring;

    /// <summary>
    /// Seconds. For <see cref="GaugeAnimation.Ease"/> the length of the move; for
    /// <see cref="GaugeAnimation.Spring"/> the spring's period, roughly how long a critically damped
    /// needle takes to arrive. Zero or less behaves like <see cref="GaugeAnimation.None"/>.
    /// </summary>
    public double Duration { get; set; } = 0.8;

    /// <summary>
    /// The spring's damping ratio. 1 arrives without overshooting; lower values overshoot and bounce
    /// (0.5-0.7 is a lively analogue needle); higher values creep in.
    /// </summary>
    public double Damping { get; set; } = 0.65;

    /// <summary>The low peg.</summary>
    public double Minimum { get; private set; }

    /// <summary>The high peg.</summary>
    public double Maximum { get; private set; }

    /// <summary>Where the needle is now.</summary>
    public double Position { get; private set; }

    /// <summary>The needle's speed, in value units per second.</summary>
    public double Velocity { get; private set; }

    /// <summary>The value the needle is currently heading for.</summary>
    public double Target { get; private set; }

    /// <summary>Where the needle will finally come to rest, after any sweep in progress.</summary>
    public double FinalTarget => this.waypoints.Count > 0 ? this.waypoints[^1] : this.Target;

    /// <summary>Whether the needle still has somewhere to go - the host keeps its clock running while this is true.</summary>
    public bool IsMoving { get; private set; }

    /// <summary>Whether a <see cref="Sweep"/> is still on its way out to the far peg.</summary>
    public bool IsSweeping => this.waypoints.Count > 0;


    /// <summary>Moves the pegs. The needle and its targets are pulled inside them.</summary>
    public void SetBounds(double minimum, double maximum)
    {
        this.Minimum = minimum;
        this.Maximum = maximum > minimum ? maximum : minimum + 1;

        this.Position = this.Clamp(this.Position);
        this.Target = this.Clamp(this.Target);
        for (var i = 0; i < this.waypoints.Count; i++)
            this.waypoints[i] = this.Clamp(this.waypoints[i]);

        this.IsMoving = this.Position != this.Target || this.waypoints.Count > 0;
    }


    /// <summary>
    /// Sends the needle to a value. During a <see cref="Sweep"/> this replaces where the sweep ends
    /// rather than cutting it short, so a reading that arrives while the dial is still doing its
    /// startup sweep is where it comes back to.
    /// </summary>
    public void SetTarget(double value)
    {
        value = this.Clamp(value);

        if (this.waypoints.Count > 0)
        {
            this.waypoints[^1] = value;
            return;
        }

        if (this.Mode == GaugeAnimation.None || this.Duration <= 0)
        {
            this.Snap(value);
            return;
        }

        this.HeadFor(value);
    }


    /// <summary>Puts the needle on a value at once, at rest, abandoning any sweep.</summary>
    public void Snap(double value)
    {
        value = this.Clamp(value);
        this.waypoints.Clear();
        this.Position = value;
        this.Target = value;
        this.Velocity = 0;
        this.IsMoving = false;
    }


    /// <summary>
    /// The startup self-test a car's instrument cluster does: the needle swings out to the maximum
    /// and falls back to <paramref name="finalValue"/>. Ignored when the needle does not animate.
    /// </summary>
    public void Sweep(double finalValue)
    {
        finalValue = this.Clamp(finalValue);

        if (this.Mode == GaugeAnimation.None || this.Duration <= 0)
        {
            this.Snap(finalValue);
            return;
        }

        this.waypoints.Clear();
        this.waypoints.Add(finalValue);
        this.HeadFor(this.Maximum);
    }


    /// <summary>
    /// Advances the needle by <paramref name="seconds"/>. Returns whether it moved, which is whether
    /// the host needs to redraw.
    /// </summary>
    public bool Step(double seconds)
    {
        if (!this.IsMoving)
            return false;

        if (!(seconds > 0) || !Double.IsFinite(seconds))
            return false;

        if (this.Mode == GaugeAnimation.None || this.Duration <= 0)
        {
            this.Snap(this.FinalTarget);
            return true;
        }

        if (this.Mode == GaugeAnimation.Ease)
            this.StepEase(seconds);
        else
            this.StepSpring(seconds);

        return true;
    }


    void HeadFor(double value)
    {
        this.Target = value;
        this.easeFrom = this.Position;
        this.easeElapsed = 0;
        this.IsMoving = true;
    }


    void StepEase(double seconds)
    {
        var previous = this.Position;
        this.easeElapsed += seconds;
        var t = Math.Min(1, this.easeElapsed / this.Duration);

        // Ease-out cubic: quick off the mark, gentle on arrival.
        var eased = 1 - Math.Pow(1 - t, 3);
        this.Position = this.easeFrom + (this.Target - this.easeFrom) * eased;
        this.Velocity = (this.Position - previous) / seconds;

        if (t >= 1)
            this.Arrive();
    }


    void StepSpring(double seconds)
    {
        var range = this.Maximum - this.Minimum;

        // Mass 1. Stiffness and damping from the period and ratio, so Duration means the same thing
        // whatever the scale: (2pi/T)^2 and 4pi*zeta/T.
        var omega = 2 * Math.PI / this.Duration;
        var stiffness = omega * omega;
        var damping = 4 * Math.PI * Math.Max(0, this.Damping) / this.Duration;

        var remaining = seconds;
        while (remaining > 0)
        {
            var h = Math.Min(remaining, MaximumSubstep);
            remaining -= h;

            // Semi-implicit Euler: velocity first, then position from the new velocity. Stable at
            // these step sizes where explicit Euler gains energy and never settles.
            var acceleration = stiffness * (this.Target - this.Position) - damping * this.Velocity;
            this.Velocity += acceleration * h;
            this.Position += this.Velocity * h;

            if (this.Position > this.Maximum)
            {
                this.Position = this.Maximum;
                if (this.Velocity > 0)
                    this.Velocity = -this.Velocity * PegRestitution;
            }
            else if (this.Position < this.Minimum)
            {
                this.Position = this.Minimum;
                if (this.Velocity < 0)
                    this.Velocity = -this.Velocity * PegRestitution;
            }
        }

        var distance = Math.Abs(this.Target - this.Position);
        if (this.waypoints.Count > 0)
        {
            // A sweep only has to touch the far end, not come to rest there.
            if (distance <= range * WaypointFraction)
                this.Arrive();
            return;
        }

        var settle = range * SettleFraction;
        if (distance <= settle && Math.Abs(this.Velocity) <= settle * 10)
            this.Arrive();
    }


    void Arrive()
    {
        if (this.waypoints.Count > 0)
        {
            var next = this.waypoints[0];
            this.waypoints.RemoveAt(0);

            // The spring keeps its velocity into the next leg - that is the swing back.
            if (this.Mode == GaugeAnimation.Ease)
                this.Position = this.Target;

            this.HeadFor(next);
            return;
        }

        this.Position = this.Target;
        this.Velocity = 0;
        this.IsMoving = false;
    }


    double Clamp(double value)
        => Double.IsNaN(value) ? this.Minimum : Math.Clamp(value, this.Minimum, this.Maximum);
}

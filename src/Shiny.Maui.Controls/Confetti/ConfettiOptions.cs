namespace Shiny.Maui.Controls;

/// <summary>The shape a confetti particle is drawn as.</summary>
public enum ConfettiShape
{
    /// <summary>A paper square that tumbles as it wobbles.</summary>
    Square,
    /// <summary>An oval that flattens and widens as it turns.</summary>
    Circle,
    /// <summary>A five-pointed star.</summary>
    Star
}


/// <summary>Ready-made bursts, matching the Magic UI confetti demos.</summary>
public enum ConfettiPreset
{
    /// <summary>One burst of 100 particles from the origin.</summary>
    Burst,
    /// <summary>A burst with a random angle, spread and count - no two taps look the same.</summary>
    Random,
    /// <summary>Five seconds of 360° bursts at random points across the top of the page. Ignores the origin.</summary>
    Fireworks,
    /// <summary>Three seconds of streams from both side edges. Ignores the origin.</summary>
    SideCannons,
    /// <summary>Three waves of gold stars and sparks from the origin, with no gravity.</summary>
    Stars
}


/// <summary>Which gesture fires the confetti attached with <see cref="Confetti"/>.</summary>
public enum ConfettiTrigger
{
    /// <summary>Not attached.</summary>
    None,
    /// <summary>A single tap (a click on a button).</summary>
    Tap,
    /// <summary>A double tap. Buttons cannot report one and fall back to <see cref="Tap"/>.</summary>
    DoubleTap
}


/// <summary>
/// One burst of confetti. The names and defaults are those of canvas-confetti (which Magic UI's
/// confetti wraps), so a recipe written for the web can be copied across value for value.
/// </summary>
public class ConfettiOptions
{
    /// <summary>The default palette.</summary>
    public static IReadOnlyList<Color> DefaultColors { get; } =
    [
        Color.FromArgb("#26CCFF"),
        Color.FromArgb("#A25AFD"),
        Color.FromArgb("#FF5E7E"),
        Color.FromArgb("#88FF5A"),
        Color.FromArgb("#FCFF42"),
        Color.FromArgb("#FFA62D"),
        Color.FromArgb("#FF36FF")
    ];

    /// <summary>How many particles to launch.</summary>
    public int ParticleCount { get; set; } = 50;

    /// <summary>Launch direction in degrees: 90 is straight up, 0 is right, 180 is left.</summary>
    public double Angle { get; set; } = 90;

    /// <summary>How far, in degrees, particles can stray either side of <see cref="Angle"/> in total.</summary>
    public double Spread { get; set; } = 45;

    /// <summary>Launch speed in points per frame. Each particle gets between half and one and a half of it.</summary>
    public double StartVelocity { get; set; } = 45;

    /// <summary>The share of its speed a particle keeps each frame. Keep it below 1.</summary>
    public double Decay { get; set; } = 0.9;

    /// <summary>How fast particles fall. 0 floats, 1 is the default pull.</summary>
    public double Gravity { get; set; } = 1;

    /// <summary>Sideways drift per frame - negative drifts left, positive right.</summary>
    public double Drift { get; set; }

    /// <summary>Stops particles tumbling, so they fall flat.</summary>
    public bool Flat { get; set; }

    /// <summary>How many frames (at 60 per second) a particle lives, fading as it goes.</summary>
    public int Ticks { get; set; } = 200;

    /// <summary>Horizontal launch point, 0 (left edge) to 1 (right edge) of the page.</summary>
    public double OriginX { get; set; } = 0.5;

    /// <summary>Vertical launch point, 0 (top edge) to 1 (bottom edge) of the page.</summary>
    public double OriginY { get; set; } = 0.5;

    /// <summary>The palette particles pick from at random.</summary>
    public IList<Color> Colors { get; set; } = [.. DefaultColors];

    /// <summary>The shapes particles pick from at random.</summary>
    public IList<ConfettiShape> Shapes { get; set; } = [ConfettiShape.Square, ConfettiShape.Circle];

    /// <summary>
    /// Text to throw instead of shapes - usually emoji (🦄, 🎉). When this has anything in it,
    /// <see cref="Shapes"/> and <see cref="Colors"/> are ignored.
    /// </summary>
    public IList<string> Emoji { get; set; } = [];

    /// <summary>Particle size multiplier.</summary>
    public double Scalar { get; set; } = 1;

    /// <summary>Skip the burst entirely when the device has reduced motion turned on.</summary>
    public bool DisableForReducedMotion { get; set; }

    /// <summary>A copy that can be changed without touching this one.</summary>
    public ConfettiOptions Clone()
    {
        var clone = (ConfettiOptions)this.MemberwiseClone();
        clone.Colors = [.. this.Colors];
        clone.Shapes = [.. this.Shapes];
        clone.Emoji = [.. this.Emoji];
        return clone;
    }
}

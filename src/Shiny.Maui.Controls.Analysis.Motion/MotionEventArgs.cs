using Microsoft.Maui.Graphics;

namespace Shiny.Maui.Controls.Analysis.Motion;

/// <summary>
/// Carries a motion state change to <see cref="MotionAnalyzer.MotionChanged"/> subscribers. Raised when
/// motion starts and again when it stops.
/// </summary>
/// <param name="inMotion"><c>true</c> when motion just started, <c>false</c> when it just stopped.</param>
/// <param name="region">Normalized bounds (0..1) of the overall changed region in upright image space, or null when stopped.</param>
/// <param name="intensity">Fraction of sampled pixels that changed this frame (0..1).</param>
/// <param name="regions">Normalized bounds (0..1) of each distinct motion cluster in upright image space.</param>
public class MotionEventArgs(bool inMotion, RectF? region, float intensity, IReadOnlyList<RectF>? regions = null) : EventArgs
{
    /// <summary><c>true</c> when motion just started, <c>false</c> when it just stopped.</summary>
    public bool InMotion { get; } = inMotion;

    /// <summary>
    /// Normalized bounds (0..1) of the overall changed region (the union of all <see cref="Regions"/>), or
    /// null when motion stopped.
    /// </summary>
    public RectF? Region { get; } = region;

    /// <summary>Fraction of sampled pixels that changed this frame (0..1).</summary>
    public float Intensity { get; } = intensity;

    /// <summary>
    /// Normalized bounds (0..1) of each distinct region where motion was detected this frame — localized so
    /// movement in two separate spots yields two boxes rather than one box spanning both. Empty when stopped.
    /// </summary>
    public IReadOnlyList<RectF> Regions { get; } = regions ?? [];
}

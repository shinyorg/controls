namespace Shiny.Controls.BarcodeReader;

/// <summary>
/// Decides when to zoom the camera in on a code that has been found but is too small to read, and when to
/// give the zoom back. Works on tracks in normalized upright space (0..1, centered at 0.5).
/// </summary>
/// <remarks>
/// <para>
/// Zoom magnifies around the center, so a code off to one side is pushed out of frame by zooming. The zoom
/// chosen is capped so the target's far edge stays inside the frame; codes that sit too far out get no
/// zoom at all, and the user has to aim.
/// </para>
/// <para>
/// It only ever steps in while there is something small and unread in view, holds while anything is in view
/// (zooming out the moment a code reads would shrink it back past readable and start the cycle again), and
/// steps back to where the user had it once the scene has been empty for <see cref="ReturnDelay"/>. If the
/// zoom moves without it — a pinch, a slider — it hands control back and does nothing until the scene is
/// empty again.
/// </para>
/// </remarks>
public sealed class AutoZoomController
{
    double? lastSet;
    double baseZoom = 1;
    bool engaged;
    bool suspended;
    TimeSpan lastActivity;

    /// <summary>The largest multiple of the user's own zoom it will go to. Default 4.</summary>
    public double MaxMultiplier { get; set; } = 4;

    /// <summary>Average element width (source pixels) a linear code should reach. Default 2.2.</summary>
    public float TargetElementWidth { get; set; } = 2.2f;

    /// <summary>Fraction of the frame's short side a 2D code should fill. Default 0.22.</summary>
    public float TargetMatrixSize { get; set; } = 0.22f;

    /// <summary>How long a small code must stay unread before zooming starts. Default 250 ms.</summary>
    public TimeSpan Dwell { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>How long the scene must be empty before the zoom is returned. Default 1.2 s.</summary>
    public TimeSpan ReturnDelay { get; set; } = TimeSpan.FromMilliseconds(1200);

    /// <summary>
    /// The zoom to apply now, or null to leave it alone. <paramref name="aspect"/> is the upright frame's
    /// width over height.
    /// </summary>
    public double? Update(IReadOnlyList<TrackedBarcode> tracks, double currentZoom, double minZoom, double maxZoom, float aspect, TimeSpan now)
    {
        // someone else moved the zoom: stand down until the scene clears
        if (this.lastSet is { } set && Math.Abs(set - currentZoom) > 0.02)
        {
            this.suspended = true;
            this.engaged = false;
            this.lastSet = null;
        }

        var inView = false;
        foreach (var t in tracks)
        {
            if (t.LastSeen == now)
            {
                inView = true;
                break;
            }
        }
        if (inView)
            this.lastActivity = now;

        if (this.suspended)
        {
            if (!inView && now - this.lastActivity > this.ReturnDelay)
                this.suspended = false;
            return null;
        }

        if (!this.engaged)
            this.baseZoom = currentZoom;

        var target = this.PickTarget(tracks, now, aspect, out var factor);
        if (target != null && factor > 1.05)
        {
            var limit = Math.Min(maxZoom, this.baseZoom * this.MaxMultiplier);
            var desired = Math.Min(limit, currentZoom * factor);
            if (desired > currentZoom + 0.02)
            {
                // ease in: half the remaining way per frame keeps the preview from lurching
                var next = currentZoom + (desired - currentZoom) * 0.5;
                this.engaged = true;
                return this.lastSet = next;
            }
            return null;
        }

        if (this.engaged && !inView && now - this.lastActivity > this.ReturnDelay)
        {
            var back = Math.Max(minZoom, this.baseZoom);
            if (Math.Abs(currentZoom - back) < 0.02)
            {
                this.engaged = false;
                this.lastSet = null;
                return null;
            }
            var next = currentZoom + (back - currentZoom) * 0.5;
            if (Math.Abs(next - back) < 0.02)
                next = back;
            return this.lastSet = next;
        }
        return null;
    }

    TrackedBarcode? PickTarget(IReadOnlyList<TrackedBarcode> tracks, TimeSpan now, float aspect, out double factor)
    {
        factor = 1;
        TrackedBarcode? best = null;
        foreach (var t in tracks)
        {
            if (t.IsConfirmed || t.LastSeen != now || now - t.FirstSeen < this.Dwell)
                continue;

            double want;
            if (t.Kind == CandidateKind.Linear)
            {
                if (t.ElementWidth <= 0 || t.ElementWidth >= this.TargetElementWidth)
                    continue;
                want = this.TargetElementWidth / t.ElementWidth;
            }
            else
            {
                // normalized sizes are per axis; compare on the short side
                var side = Math.Max(t.Box.Width * Math.Min(1f, aspect), t.Box.Height * Math.Min(1f, 1f / aspect));
                if (side <= 0 || side >= this.TargetMatrixSize)
                    continue;
                want = this.TargetMatrixSize / side;
            }

            // zooming by f pushes the far edge out to 0.5 + f * (offset + half size); keep it inside
            var reachX = Math.Abs(t.Box.CenterX - 0.5f) + t.Box.Width / 2f;
            var reachY = Math.Abs(t.Box.CenterY - 0.5f) + t.Box.Height / 2f;
            var room = 0.46 / Math.Max(1e-3, Math.Max(reachX, reachY));
            var f = Math.Min(Math.Min(want, room), 2.5);
            if (f > factor)
            {
                factor = f;
                best = t;
            }
        }
        return best;
    }
}

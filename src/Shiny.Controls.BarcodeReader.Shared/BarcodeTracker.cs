namespace Shiny.Controls.BarcodeReader;

/// <summary>One frame's sighting of a candidate or code, in whatever space the caller tracks in (normally normalized upright).</summary>
/// <param name="Box">Where it is.</param>
/// <param name="Kind">The texture it was located by.</param>
/// <param name="Text">The decoded payload, or null when it was located but not read.</param>
/// <param name="Symbology">The symbology when <paramref name="Text"/> is set.</param>
/// <param name="IsChecksummed">The read was verified by a check digit.</param>
/// <param name="ElementWidth">Average element width in source pixels, 0 when unknown.</param>
public readonly record struct TrackObservation(
    BoxF Box,
    CandidateKind Kind,
    string? Text = null,
    Symbology Symbology = Symbology.Unknown,
    bool IsChecksummed = false,
    float ElementWidth = 0f);

/// <summary>A code (or a candidate for one) followed across frames.</summary>
public sealed class TrackedBarcode
{
    readonly Dictionary<(string, Symbology), int> readings = new();

    internal TrackedBarcode(int id, TimeSpan now)
    {
        this.Id = id;
        this.FirstSeen = now;
        this.LastSeen = now;
        this.LastNativeAttempt = TimeSpan.FromDays(-1);
    }

    /// <summary>Stable for the life of the track.</summary>
    public int Id { get; }
    /// <summary>Where it was last seen.</summary>
    public BoxF Box { get; internal set; }
    /// <summary>The texture it was located by.</summary>
    public CandidateKind Kind { get; internal set; }

    /// <summary>The confirmed payload — null until enough readings agree.</summary>
    public string? Text { get; private set; }
    /// <summary>The confirmed symbology.</summary>
    public Symbology Symbology { get; private set; }
    /// <summary>Enough readings agree to believe <see cref="Text"/>.</summary>
    public bool IsConfirmed => this.Text != null;

    /// <summary>Average element width in source pixels from the latest sighting, 0 when unknown.</summary>
    public float ElementWidth { get; internal set; }

    /// <summary>When the track started.</summary>
    public TimeSpan FirstSeen { get; }
    /// <summary>The frame it was last seen in.</summary>
    public TimeSpan LastSeen { get; internal set; }

    /// <summary>When a native decoder was last given this track's crop; the scheduler visits the least recent first.</summary>
    public TimeSpan LastNativeAttempt { get; set; }

    /// <summary>Frames in which this track was located but nothing read it.</summary>
    public int Misses { get; internal set; }

    /// <summary>Anything the caller wants to carry with the track — the analyzer keeps the latest candidate here.</summary>
    public object? Tag { get; set; }

    internal void AddReading(string text, Symbology symbology, bool checksummed, int required, int requiredUnchecked)
    {
        var key = (text, symbology);
        var n = this.readings[key] = this.readings.GetValueOrDefault(key) + 1;
        if (this.Text == text && this.Symbology == symbology)
            return;

        // a second, different reading of the same place has to beat the one already accepted
        var needed = checksummed ? required : requiredUnchecked;
        var current = this.Text == null ? 0 : this.readings.GetValueOrDefault((this.Text, this.Symbology));
        if (n >= needed && n > current)
        {
            this.Text = text;
            this.Symbology = symbology;
        }
    }
}

/// <summary>
/// Follows candidates and reads from frame to frame. Two things come of it: a reading has to repeat before
/// it is believed (on top of the per-frame scanline vote), and a code that is already confirmed does not
/// need the expensive native pass again while it stays in view — which is most of what makes a steady
/// scan cheap.
/// </summary>
public sealed class BarcodeTracker
{
    readonly List<TrackedBarcode> tracks = new();
    int nextId;

    /// <summary>Frames a checksummed reading must be seen in before it is confirmed. Default 1.</summary>
    public int RequiredConfirmations { get; set; } = 1;

    /// <summary>Frames a reading without a check digit (Code 39, short ITF) must be seen in. Default 2.</summary>
    public int RequiredUncheckedConfirmations { get; set; } = 2;

    /// <summary>How long a track survives unseen — long enough to ride out a frame or two of motion blur.</summary>
    public TimeSpan Linger { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <summary>Tracks still alive.</summary>
    public IReadOnlyList<TrackedBarcode> Tracks => this.tracks;

    /// <summary>
    /// Fold one frame's observations in. Returns the track each observation landed on, by index. Can be
    /// called more than once per frame (for example once for the engine's reads and again for a native
    /// full-frame pass) with the same <paramref name="now"/>.
    /// </summary>
    public TrackedBarcode[] Update(IReadOnlyList<TrackObservation> observations, TimeSpan now)
    {
        this.tracks.RemoveAll(t => now - t.LastSeen > this.Linger);

        var result = new TrackedBarcode[observations.Count];
        var taken = new HashSet<TrackedBarcode>();
        for (var i = 0; i < observations.Count; i++)
        {
            var o = observations[i];
            var track = this.Match(o, taken) ?? this.Create(now);
            taken.Add(track);

            track.Box = o.Box;
            track.Kind = o.Kind;
            track.LastSeen = now;
            if (o.ElementWidth > 0)
                track.ElementWidth = o.ElementWidth;

            if (o.Text != null)
                track.AddReading(o.Text, o.Symbology, o.IsChecksummed, this.RequiredConfirmations, this.RequiredUncheckedConfirmations);
            else if (!track.IsConfirmed)
                track.Misses++;

            result[i] = track;
        }
        return result;
    }

    /// <summary>Record a read that came back for an existing track (from a native decoder on its crop).</summary>
    public void AddReading(TrackedBarcode track, string text, Symbology symbology, bool checksummed, BoxF? box = null)
    {
        track.AddReading(text, symbology, checksummed, this.RequiredConfirmations, this.RequiredUncheckedConfirmations);
        if (box is { } b)
            track.Box = b;
    }

    /// <summary>
    /// The unconfirmed tracks most in need of a native decode, least recently tried first, at most
    /// <paramref name="budget"/>. Matrix candidates qualify at once; a linear one only after the engine has
    /// failed it twice (a PDF417, a Code 93, or a code too damaged for the engine).
    /// </summary>
    public List<TrackedBarcode> SelectForNative(int budget, TimeSpan now, TimeSpan retryInterval)
    {
        var picks = new List<TrackedBarcode>();
        foreach (var t in this.tracks)
        {
            if (t.IsConfirmed || t.LastSeen != now || now - t.LastNativeAttempt < retryInterval)
                continue;
            if (t.Kind == CandidateKind.Linear && t.Misses < 2)
                continue;
            picks.Add(t);
        }
        picks.Sort(static (a, b) => a.LastNativeAttempt.CompareTo(b.LastNativeAttempt));
        if (picks.Count > budget)
            picks.RemoveRange(budget, picks.Count - budget);
        return picks;
    }

    /// <summary>Forget everything.</summary>
    public void Clear() => this.tracks.Clear();

    TrackedBarcode Create(TimeSpan now)
    {
        var t = new TrackedBarcode(++this.nextId, now);
        this.tracks.Add(t);
        return t;
    }

    TrackedBarcode? Match(TrackObservation o, HashSet<TrackedBarcode> taken)
    {
        TrackedBarcode? best = null;
        var bestScore = 0f;
        foreach (var t in this.tracks)
        {
            if (taken.Contains(t))
                continue;

            // the same payload in roughly the same place is the same code, however much the box moved
            if (o.Text != null && t.Text == o.Text)
            {
                var near = Distance(o.Box, t.Box) < 1.5f * Math.Max(Size(o.Box), Size(t.Box));
                if (near)
                    return t;
            }

            var iou = o.Box.IntersectionOverUnion(t.Box);
            var close = Distance(o.Box, t.Box) < 0.6f * Math.Max(Size(o.Box), Size(t.Box));
            var score = Math.Max(iou, close ? 0.2f : 0f);
            if (score > bestScore && score >= 0.15f)
            {
                best = t;
                bestScore = score;
            }
        }
        return best;
    }

    static float Size(BoxF b) => Math.Max(b.Width, b.Height);

    static float Distance(BoxF a, BoxF b)
    {
        var dx = a.CenterX - b.CenterX;
        var dy = a.CenterY - b.CenterY;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}

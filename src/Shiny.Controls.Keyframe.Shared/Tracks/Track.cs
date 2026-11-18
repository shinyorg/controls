namespace Shiny.Controls.Keyframe;

/// <summary>
/// Animates one property of one target across a list of keyframes.
/// </summary>
/// <typeparam name="TTarget">The object being animated.</typeparam>
/// <typeparam name="TValue">The property's value type.</typeparam>
/// <remarks>
/// <para><b>Why the target is a separate type parameter.</b> The setter is
/// <c>Action&lt;TTarget, TValue&gt;</c> rather than <c>Action&lt;TValue&gt;</c> so it never closes
/// over the target. That lets the track hold the target weakly — an infinitely looping animation
/// on a page that has since been popped goes inert and gets collected, instead of pinning the whole
/// visual tree. It is the single most common leak in hand-rolled animation code.</para>
/// <para><b>Evaluation is pure.</b> <see cref="Apply"/> depends only on the progress passed in,
/// never on the previous frame. That is what makes seeking, scrubbing and reverse playback exact
/// rather than approximate.</para>
/// </remarks>
public sealed class Track<TTarget, TValue> : ITrack
    where TTarget : class
{
    readonly WeakReference<TTarget> target;
    readonly Action<TTarget, TValue> setter;
    readonly Func<TTarget, TValue>? getter;
    readonly IInterpolator<TValue> interpolator;
    readonly Key<TValue>[] keys;
    readonly Action<TTarget>? release;

    TValue? baseline;
    bool hasBaseline;
    int lastSegment;

    /// <summary>Creates a track.</summary>
    /// <param name="target">The object to animate. Held weakly.</param>
    /// <param name="setter">Writes a value to the target.</param>
    /// <param name="keys">The keyframes. Sorted on construction; need not be supplied in order.</param>
    /// <param name="interpolator">Blends between keyframe values.</param>
    /// <param name="getter">Reads the target's current value. Required if any key is implicit or if
    /// <see cref="RestoreBaseline"/> will be used.</param>
    /// <param name="name">Optional label for diagnostics.</param>
    /// <param name="release">Withdraws the track's effect from the target; see <see cref="ITrack.Release"/>.
    /// Leave null for a setter that writes the property directly.</param>
    public Track(
        TTarget target,
        Action<TTarget, TValue> setter,
        IEnumerable<Key<TValue>> keys,
        IInterpolator<TValue> interpolator,
        Func<TTarget, TValue>? getter = null,
        string? name = null,
        Action<TTarget>? release = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(setter);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(interpolator);

        this.target = new WeakReference<TTarget>(target);
        this.setter = setter;
        this.getter = getter;
        this.interpolator = interpolator;
        this.release = release;
        this.Name = name;

        // A stable sort keeps duplicate offsets in author order, which is what makes a hard cut
        // (two keys at the same offset) land the way it reads in the source.
        this.keys = keys.OrderBy(k => k.Offset).ToArray();

        if (this.keys.Any(k => k.IsImplicit) && getter is null)
            throw new ArgumentException(
                "A track with implicit keyframes needs a getter so the current value can be read.",
                nameof(getter));
    }

    /// <inheritdoc />
    public string? Name { get; }

    /// <inheritdoc />
    public bool IsAlive => target.TryGetTarget(out _);

    /// <summary>The keyframes, in ascending offset order.</summary>
    public IReadOnlyList<Key<TValue>> Keys => keys;

    /// <inheritdoc />
    public void CaptureBaseline()
    {
        if (getter is null || !target.TryGetTarget(out var instance))
            return;

        baseline = getter(instance);
        hasBaseline = true;
    }

    /// <inheritdoc />
    public void RestoreBaseline()
    {
        if (!hasBaseline || !target.TryGetTarget(out var instance))
            return;

        setter(instance, baseline!);
    }

    /// <inheritdoc />
    public void Release()
    {
        if (release is not null && target.TryGetTarget(out var instance))
            release(instance);
    }

    /// <inheritdoc />
    public void Apply(double progress)
    {
        if (keys.Length == 0 || !target.TryGetTarget(out var instance))
            return;

        setter(instance, Evaluate(progress));
    }

    /// <summary>
    /// Computes the value at the given progress without writing it. Useful for tests, for the
    /// export pipeline, and for inspecting a timeline without side effects.
    /// </summary>
    public TValue Evaluate(double progress)
    {
        if (keys.Length == 0)
            return default!;

        if (keys.Length == 1)
            return ValueOf(keys[0]);

        var first = keys[0];
        var last = keys[^1];

        // Before the first key. Progress below zero only happens when a timeline-level easing
        // curve undershoots (anticipation), and extrapolating there is what makes that curve
        // actually visible rather than clipped flat.
        if (progress <= first.Offset)
            return progress < 0d ? Blend(0, progress) : ValueOf(first);

        // Past the last key — same reasoning for overshoot curves.
        if (progress >= last.Offset)
            return progress > 1d ? Blend(keys.Length - 2, progress) : ValueOf(last);

        return Blend(FindSegment(progress), progress);
    }

    /// <summary>
    /// Locates the segment containing <paramref name="progress"/>. Playback is overwhelmingly
    /// sequential, so we check the previously used segment and its successor before falling back
    /// to a binary search — that turns the common case into a couple of comparisons.
    /// </summary>
    int FindSegment(double progress)
    {
        var cached = lastSegment;
        if (cached >= 0 && cached < keys.Length - 1)
        {
            if (progress >= keys[cached].Offset && progress < keys[cached + 1].Offset)
                return cached;

            var next = cached + 1;
            if (next < keys.Length - 1 && progress >= keys[next].Offset && progress < keys[next + 1].Offset)
            {
                lastSegment = next;
                return next;
            }
        }

        // Binary search for the last key whose offset is <= progress.
        int lo = 0, hi = keys.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (keys[mid].Offset <= progress)
                lo = mid;
            else
                hi = mid - 1;
        }

        lastSegment = Math.Min(lo, keys.Length - 2);
        return lastSegment;
    }

    TValue Blend(int segment, double progress)
    {
        var from = keys[segment];
        var to = keys[segment + 1];

        var span = to.Offset - from.Offset;

        // Two keys at the same offset form a hard cut: there is no time to blend across, so the
        // later value wins the instant we reach it.
        var local = span <= 0d
            ? (progress >= to.Offset ? 1d : 0d)
            : (progress - from.Offset) / span;

        var eased = from.Easing is null ? local : from.Easing(local);
        return interpolator.Interpolate(ValueOf(from), ValueOf(to), eased);
    }

    TValue ValueOf(Key<TValue> key)
    {
        if (!key.IsImplicit)
            return key.Value!;

        // An implicit key resolves to the captured baseline. If playback somehow began without a
        // capture, read through to the live value so the animation still starts from a sane place.
        if (hasBaseline)
            return baseline!;

        return getter is not null && target.TryGetTarget(out var instance)
            ? getter(instance)
            : default!;
    }
}

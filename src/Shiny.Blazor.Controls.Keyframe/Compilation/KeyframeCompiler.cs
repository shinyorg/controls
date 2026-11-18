using System.Globalization;
using global::Shiny.Controls.Keyframe;

namespace Shiny.Blazor.Controls.Keyframe.Compilation;

/// <summary>
/// Turns authored tracks (MAUI property names, keys with offsets, values and curve text) into the
/// keyframes and options the Web Animations API runs.
/// </summary>
/// <remarks>
/// <para>Each CSS property becomes its own <c>element.animate()</c> effect, so tracks with different
/// offsets and curves never have to share keyframes. The exception is a CSS property that more than
/// one MAUI property feeds - <c>scale</c> (Scale, ScaleX, ScaleY), <c>translate</c> (TranslationX/Y),
/// the 3D <c>transform</c> (RotationX/Y) and <c>transform-origin</c> (AnchorX/Y). Those are merged
/// per offset: zipped when the tracks share a schedule, sampled densely when they do not, so each
/// axis still follows its own curve.</para>
/// <para>WAAPI has no iteration delay, so one is baked into the keyframes: each pass is lengthened by
/// the gap, half of it held at each end so the pause sits between passes whichever way they run, and
/// the start delay and end delay are pulled in by half a gap to compensate. A timing-level easing
/// would then be stretched over the holds, so it is folded into the per-segment curves first (each
/// becomes a sampled CSS <c>linear()</c>), which keeps the motion exact.</para>
/// </remarks>
internal static class KeyframeCompiler
{
    /// <summary>Samples per pass when tracks merged into one CSS property cannot share keyframes.</summary>
    public const int DenseSamples = 60;

    const double Epsilon = 1e-6;

    /// <summary>The 3D turns get a perspective, as MAUI's RotationX/RotationY do on device.</summary>
    public const string Perspective = "perspective(800px)";


    /// <summary>
    /// The property names whose live value has to be read off the element before compiling: tracks
    /// with a key that has no value, and the untouched axis of a merged CSS property (a lone
    /// TranslationX must keep the element's current Y).
    /// </summary>
    public static IReadOnlyList<string> LiveNeeds(IReadOnlyList<TrackSpec> tracks)
    {
        var needs = new List<string>();
        var resolved = tracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Property) && t.Keys.Count > 0)
            .Select(t => (Track: t, Property: CssProperties.Resolve(t.Property)))
            .ToList();

        foreach (var (track, property) in resolved.Where(r => r.Track.Keys.Any(k => k.Value is null)))
            Add(needs, property.Name);

        foreach (var group in resolved.Where(r => r.Property.IsMerged).GroupBy(r => r.Property.Css))
        {
            var axes = group.Select(g => g.Property.Axis).ToHashSet();
            if (axes.Contains(Axis.Both))
                continue;

            var (x, y) = AxisNames(group.First().Property.Kind);
            if (!axes.Contains(Axis.X))
                Add(needs, x);
            if (!axes.Contains(Axis.Y))
                Add(needs, y);
        }

        return needs;

        static void Add(List<string> list, string name)
        {
            if (!list.Contains(name, StringComparer.OrdinalIgnoreCase))
                list.Add(name);
        }
    }


    /// <summary>Compiles one animation.</summary>
    /// <param name="spec">The authored timing and tracks.</param>
    /// <param name="live">Live values by property name, from <see cref="LiveNeeds"/>; may be null.</param>
    /// <param name="implicitEnds">
    /// Template mode (item enter/exit, page transitions): a key with no value is left out of the
    /// keyframes, so the browser fills it from the element's own style - CSS semantics - instead of
    /// a live reading.
    /// </param>
    public static CompiledAnimation Compile(AnimationSpec spec, IReadOnlyDictionary<string, string>? live = null, bool implicitEnds = false)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var tracks = spec.Tracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Property) && t.Keys.Count > 0)
            .Select(t => Normalize(t, live, implicitEnds))
            .ToList();

        var effects = new List<CompiledEffect>();

        // Simple CSS properties: one effect each. Two tracks on the same one - Rotation and Spin, say -
        // cannot both win, so the last one authored does, as it would on MAUI.
        var simple = tracks
            .Where(t => !t.Property.IsMerged)
            .GroupBy(t => t.Property.Css)
            .Select(g => g.Last());

        foreach (var track in simple)
        {
            var effect = CompileSimple(track, implicitEnds);
            if (effect.Frames.Count > 0)
                effects.Add(effect);
        }

        foreach (var group in tracks.Where(t => t.Property.IsMerged).GroupBy(t => t.Property.Css))
            effects.Add(CompileMerged(group.ToList(), live));

        return ApplyTiming(effects, spec.Timing);
    }


    /// <summary>
    /// Compiles for a CSS <c>@keyframes</c> rule, where the animation itself runs linear: omitted values
    /// fall back to the element's own style, and the timing-level easing is folded into the keyframes.
    /// </summary>
    public static List<CompiledEffect> CompileForCss(AnimationSpec spec)
    {
        var compiled = Compile(spec with { Timing = spec.Timing with { IterationDelay = TimeSpan.Zero } }, live: null, implicitEnds: true);
        if (!string.IsNullOrWhiteSpace(spec.Timing.Easing) && EasingSyntax.ToCss(spec.Timing.Easing) != "linear")
            FoldTimingEasing(compiled.Effects, EasingSyntax.Get(spec.Timing.Easing));
        return compiled.Effects;
    }


    // ---------------------------------------------------------------- tracks

    sealed record NormalKey(double Offset, string? Value, string EasingCss, EasingFunction Easing);

    sealed record NormalTrack(CssProperty Property, List<NormalKey> Keys);

    static NormalTrack Normalize(TrackSpec track, IReadOnlyDictionary<string, string>? live, bool implicitEnds)
    {
        var property = CssProperties.Resolve(track.Property);

        string? liveValue = null;
        live?.TryGetValue(property.Name, out liveValue);

        var keys = track.Keys
            .Select((k, i) => (Key: k, Index: i))
            .OrderBy(k => Math.Clamp(k.Key.Offset, 0d, 1d))
            .ThenBy(k => k.Index) // stable: duplicate offsets are a hard cut, in author order
            .Select(k =>
            {
                var value = k.Key.Value ?? (implicitEnds ? null : liveValue ?? IdentityFor(property));
                return new NormalKey(
                    Math.Clamp(k.Key.Offset, 0d, 1d),
                    value,
                    EasingSyntax.ToCss(k.Key.Easing),
                    string.IsNullOrWhiteSpace(k.Key.Easing) ? Easings.Linear : EasingSyntax.Get(k.Key.Easing));
            })
            .ToList();

        return new NormalTrack(property, keys);
    }

    /// <summary>The value a property has when nothing has touched it.</summary>
    static string? IdentityFor(CssProperty property) => property.Kind switch
    {
        PropertyKind.Number when property.Css == "opacity" => "1",
        PropertyKind.Scale => "1",
        PropertyKind.Translate => "0",
        PropertyKind.Rotation or PropertyKind.Spin or PropertyKind.Rotation3D => "0",
        PropertyKind.Anchor => "0.5",
        _ => null
    };

    static (string X, string Y) AxisNames(PropertyKind kind) => kind switch
    {
        PropertyKind.Scale => ("ScaleX", "ScaleY"),
        PropertyKind.Translate => ("TranslationX", "TranslationY"),
        PropertyKind.Rotation3D => ("RotationX", "RotationY"),
        PropertyKind.Anchor => ("AnchorX", "AnchorY"),
        _ => ("", "")
    };


    // ---------------------------------------------------------------- simple properties

    static CompiledEffect CompileSimple(NormalTrack track, bool implicitEnds)
    {
        var keys = track.Keys.Where(k => k.Value is not null).ToList();
        var formatted = FormatSimple(track.Property, keys);
        var frames = new List<CompiledFrame>(keys.Count + 2);

        for (var i = 0; i < keys.Count; i++)
        {
            var values = new Dictionary<string, string> { [track.Property.Css] = formatted[i] };
            frames.Add(new CompiledFrame(keys[i].Offset, keys[i].EasingCss, keys[i].Easing, values));
        }

        if (!implicitEnds)
            HoldEnds(frames);

        return new CompiledEffect(track.Property.Css, frames);
    }

    static List<string> FormatSimple(CssProperty property, List<NormalKey> keys)
    {
        if (property.Kind is PropertyKind.Rotation or PropertyKind.Spin)
            return FormatAngles(property.Kind == PropertyKind.Rotation, keys);

        return keys.Select(k => FormatOne(property.Kind, k.Value!)).ToList();
    }

    static string FormatOne(PropertyKind kind, string value) => kind switch
    {
        PropertyKind.Number => CssValues.TryParseQuantity(value, out var n, out var unit) && unit.Length == 0 ? CssProperties.Number(n) : value.Trim(),
        PropertyKind.Color => CssValues.Colour(value),
        PropertyKind.Length => CssValues.Length(value),
        PropertyKind.Thickness => CssValues.Thickness(value),
        _ => value.Trim()
    };

    /// <summary>
    /// Degrees, unwrapped so each segment takes the shortest arc (Rotation: 350 → 10 turns forward 20)
    /// or turns as far as the keys say (Spin).
    /// </summary>
    static List<string> FormatAngles(bool shortestArc, List<NormalKey> keys)
    {
        var result = new List<string>(keys.Count);
        double? previous = null;

        foreach (var key in keys)
        {
            if (!CssValues.TryParseDegrees(key.Value, out var degrees))
            {
                result.Add(key.Value!.Trim());
                previous = null;
            }
            else
            {
                var unwrapped = shortestArc && previous is { } p ? p + ShortestDelta(p, degrees) : degrees;
                result.Add(CssProperties.Number(unwrapped) + "deg");
                previous = unwrapped;
            }
        }

        return result;
    }

    /// <summary>The signed turn from one angle to another that is at most half a turn.</summary>
    internal static double ShortestDelta(double from, double to)
    {
        var delta = (to - from) % 360d;
        if (delta > 180d)
            delta -= 360d;
        else if (delta < -180d)
            delta += 360d;
        return delta;
    }

    /// <summary>MAUI semantics: before the first key the first value holds, after the last the last does.</summary>
    static void HoldEnds(List<CompiledFrame> frames)
    {
        if (frames.Count == 0)
            return;

        if (frames[0].Offset > Epsilon)
            frames.Insert(0, frames[0].CopyAt(0d, "linear", Easings.Linear));

        if (frames[^1].Offset < 1d - Epsilon)
            frames.Add(frames[^1].CopyAt(1d, "linear", Easings.Linear));
    }


    // ---------------------------------------------------------------- merged properties

    sealed record NumberKey(double Offset, double Value, string Unit, string EasingCss, EasingFunction Easing);

    /// <summary>A track reduced to numbers, evaluated on the managed side when merging.</summary>
    sealed class NumberTrack
    {
        public NumberTrack(Axis axis, List<NumberKey> keys)
        {
            this.Axis = axis;
            this.Keys = keys;
        }

        public Axis Axis { get; }
        public List<NumberKey> Keys { get; }

        public double Number(double progress) => this.Sample(progress).Value;

        public string Css(double progress)
        {
            var (value, unit, mixed) = this.Sample(progress);
            return mixed ?? CssProperties.Number(value) + unit;
        }

        (double Value, string Unit, string? Mixed) Sample(double progress)
        {
            var keys = this.Keys;
            if (keys.Count == 1 || progress <= keys[0].Offset)
                return (keys[0].Value, keys[0].Unit, null);
            if (progress >= keys[^1].Offset)
                return (keys[^1].Value, keys[^1].Unit, null);

            var i = 0;
            while (i < keys.Count - 2 && progress >= keys[i + 1].Offset)
                i++;

            var a = keys[i];
            var b = keys[i + 1];
            var span = b.Offset - a.Offset;
            var t = span <= Epsilon ? 1d : (progress - a.Offset) / span;
            var e = a.Easing(t);

            if (a.Unit == b.Unit)
                return (a.Value + (b.Value - a.Value) * e, a.Unit, null);

            // Different units in one track (px to %): let the browser resolve the blend.
            var mixed = $"calc({CssProperties.Number(a.Value * (1d - e))}{a.Unit} + {CssProperties.Number(b.Value * e)}{b.Unit})";
            return (a.Value + (b.Value - a.Value) * e, a.Unit, mixed);
        }
    }

    static CompiledEffect CompileMerged(List<NormalTrack> group, IReadOnlyDictionary<string, string>? live)
    {
        var kind = group[0].Property.Kind;
        var css = group[0].Property.Css;

        // The last track on each axis wins, as on MAUI.
        var tracks = group
            .GroupBy(t => t.Property.Axis)
            .Select(g => ToNumbers(g.Last(), kind))
            .Where(t => t.Keys.Count > 0)
            .ToList();

        var both = tracks.FirstOrDefault(t => t.Axis == Axis.Both);
        var x = tracks.FirstOrDefault(t => t.Axis == Axis.X);
        var y = tracks.FirstOrDefault(t => t.Axis == Axis.Y);

        var (xName, yName) = AxisNames(kind);
        var constantX = Constant(kind, xName, live);
        var constantY = Constant(kind, yName, live);

        // Scale on MAUI multiplies with ScaleX/ScaleY; where an axis has no track of its own it is 1
        // once Scale is in play, and the element's live value otherwise.
        string ValueAt(double p)
        {
            if (kind == PropertyKind.Scale)
            {
                var sx = both is null && x is null ? Parse(constantX) : (both?.Number(p) ?? 1d) * (x?.Number(p) ?? 1d);
                var sy = both is null && y is null ? Parse(constantY) : (both?.Number(p) ?? 1d) * (y?.Number(p) ?? 1d);
                return both is not null && x is null && y is null
                    ? CssProperties.Number(sx)
                    : $"{CssProperties.Number(sx)} {CssProperties.Number(sy)}";
            }

            var ax = x?.Css(p) ?? constantX;
            var ay = y?.Css(p) ?? constantY;
            return Format(kind, ax, ay);
        }

        var multiply = kind == PropertyKind.Scale && both is not null && (x is not null || y is not null);
        var frames = new List<CompiledFrame>();

        if (tracks.Count == 1 && !multiply)
        {
            // One track drives the property: its own keys and curves, the other axis held.
            foreach (var key in tracks[0].Keys)
                frames.Add(Frame(key.Offset, key.EasingCss, key.Easing, css, ValueAt(key.Offset)));
        }
        else if (!multiply && SameSchedule(tracks))
        {
            foreach (var key in tracks[0].Keys)
                frames.Add(Frame(key.Offset, key.EasingCss, key.Easing, css, ValueAt(key.Offset)));
        }
        else
        {
            // Different schedules: sample so every axis follows its own curve.
            foreach (var offset in DenseOffsets(tracks))
                frames.Add(Frame(offset, "linear", Easings.Linear, css, ValueAt(offset)));
        }

        // Merged values are always resolved numbers, so the ends are held even in template mode
        // (where an omitted value has already become the identity value).
        HoldEnds(frames);
        return new CompiledEffect(css, frames);
    }

    static CompiledFrame Frame(double offset, string easingCss, EasingFunction easing, string css, string value)
        => new(offset, easingCss, easing, new Dictionary<string, string> { [css] = value });

    static NumberTrack ToNumbers(NormalTrack track, PropertyKind kind)
    {
        var keys = new List<NumberKey>(track.Keys.Count);
        var identity = IdentityFor(track.Property) ?? "0";

        foreach (var key in track.Keys)
        {
            var text = key.Value ?? identity;
            var (value, unit) = ParseNumber(kind, text);
            keys.Add(new NumberKey(key.Offset, value, unit, key.EasingCss, key.Easing));
        }

        return new NumberTrack(track.Property.Axis, keys);
    }

    static (double Value, string Unit) ParseNumber(PropertyKind kind, string text)
    {
        switch (kind)
        {
            case PropertyKind.Rotation3D:
                return (CssValues.TryParseDegrees(text, out var degrees) ? degrees : 0d, "deg");

            case PropertyKind.Anchor:
                if (CssValues.TryParseQuantity(text, out var anchor, out var anchorUnit))
                    return (anchorUnit == "%" ? anchor / 100d : anchor, "");
                return (0.5d, "");

            case PropertyKind.Translate:
                if (CssValues.TryParseQuantity(text, out var length, out var lengthUnit))
                    return (length, lengthUnit.Length == 0 ? "px" : lengthUnit);
                return (0d, "px");

            default:
                return (CssValues.TryParseQuantity(text, out var number, out _) ? number : 1d, "");
        }
    }

    /// <summary>The held value of an axis no track drives: the live reading, or the identity value.</summary>
    static string Constant(PropertyKind kind, string name, IReadOnlyDictionary<string, string>? live)
    {
        string? reading = null;
        live?.TryGetValue(name, out reading);

        var (value, unit) = ParseNumber(kind, reading ?? (kind switch
        {
            PropertyKind.Scale => "1",
            PropertyKind.Anchor => "0.5",
            _ => "0"
        }));

        return kind == PropertyKind.Translate ? CssProperties.Number(value) + unit : CssProperties.Number(value);
    }

    static double Parse(string text) => double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    static string Format(PropertyKind kind, string x, string y) => kind switch
    {
        PropertyKind.Translate => $"{x} {y}",
        PropertyKind.Rotation3D => $"{Perspective} rotateX({StripUnit(x)}deg) rotateY({StripUnit(y)}deg)",
        PropertyKind.Anchor => $"{CssProperties.Number(Parse(StripUnit(x)) * 100d)}% {CssProperties.Number(Parse(StripUnit(y)) * 100d)}%",
        _ => $"{x} {y}"
    };

    static string StripUnit(string text) => text.EndsWith("deg", StringComparison.Ordinal) ? text[..^3] : text;

    static bool SameSchedule(List<NumberTrack> tracks)
    {
        var first = tracks[0].Keys;
        return tracks.Skip(1).All(t =>
            t.Keys.Count == first.Count &&
            t.Keys.Select((k, i) => Math.Abs(k.Offset - first[i].Offset) < Epsilon && k.EasingCss == first[i].EasingCss).All(same => same));
    }

    static List<double> DenseOffsets(List<NumberTrack> tracks)
    {
        var sorted = Enumerable.Range(0, DenseSamples + 1)
            .Select(i => (double)i / DenseSamples)
            .Concat(tracks.SelectMany(t => t.Keys.Select(k => k.Offset)))
            .OrderBy(o => o);

        var result = new List<double>();
        foreach (var offset in sorted.Where(o => result.Count == 0 || o - result[^1] > Epsilon))
            result.Add(offset);

        return result;
    }


    // ---------------------------------------------------------------- timing

    static CompiledAnimation ApplyTiming(List<CompiledEffect> effects, TimingSpec spec)
    {
        var timing = spec.ToTiming();
        var duration = timing.Duration.TotalMilliseconds;
        var gap = timing.IterationDelay.TotalMilliseconds;

        var easingCss = EasingSyntax.ToCss(spec.Easing);
        var delay = timing.Delay.TotalMilliseconds;
        var endDelay = timing.EndDelay.TotalMilliseconds;
        var iterationStart = timing.IterationStart;
        var iterationDuration = duration;

        if (gap > 0d && timing.Iterations > 1d)
        {
            if (easingCss != "linear")
            {
                FoldTimingEasing(effects, EasingSyntax.Get(spec.Easing));
                easingCss = "linear";
            }

            var lengthened = duration + gap;
            var hold = gap / 2d / lengthened;
            var scale = duration / lengthened;

            foreach (var effect in effects)
                BakeGap(effect, hold, scale);

            iterationDuration = lengthened;
            delay -= gap / 2d;
            endDelay -= gap / 2d;

            var whole = Math.Floor(iterationStart);
            iterationStart = whole + hold + (iterationStart - whole) * scale;
        }

        var compiled = new CompiledTiming(
            iterationDuration,
            delay,
            endDelay,
            timing.Iterations,
            iterationStart,
            DirectionCss(timing.Direction),
            FillCss(timing.Fill),
            easingCss);

        var infinite = double.IsPositiveInfinity(timing.Iterations);
        var total = infinite ? double.PositiveInfinity : Math.Max(0d, timing.TotalDuration.TotalMilliseconds);
        var firstPass = Math.Max(0d, timing.Delay.TotalMilliseconds) + duration;

        return new CompiledAnimation(effects, compiled, total, firstPass);
    }

    /// <summary>
    /// Folds an iteration-level easing into the keyframes: each key moves to the time the curve
    /// reaches its offset, and each segment's curve becomes a sampled <c>linear()</c> of the timing
    /// curve composed with the segment's own.
    /// </summary>
    internal static void FoldTimingEasing(List<CompiledEffect> effects, EasingFunction timing)
    {
        foreach (var effect in effects.Where(e => e.Frames.Count >= 2))
        {
            var frames = effect.Frames;
            var offsets = frames.Select(f => f.Offset).ToArray();
            var times = offsets.Select(o => Inverse(timing, o)).ToArray();

            for (var i = 0; i < frames.Count - 1; i++)
            {
                var o0 = offsets[i];
                var o1 = offsets[i + 1];
                var t0 = times[i];
                var t1 = times[i + 1];
                var segment = frames[i].Easing;

                if (o1 - o0 > Epsilon && t1 - t0 > Epsilon)
                {
                    EasingFunction folded = s => segment((timing(t0 + s * (t1 - t0)) - o0) / (o1 - o0));
                    frames[i].Easing = folded;
                    frames[i].EasingCss = EasingSyntax.ToCssLinear(folded);
                }
            }

            for (var i = 0; i < frames.Count; i++)
                frames[i].Offset = times[i];
        }
    }

    /// <summary>The first time a curve reaches a progress value (0 and 1 map to themselves).</summary>
    internal static double Inverse(EasingFunction curve, double progress)
    {
        if (progress <= 0d)
            return 0d;
        if (progress >= 1d)
            return 1d;

        const int steps = 256;
        var lower = 0d;
        var upper = 1d;
        var found = false;

        for (var k = 1; k <= steps && !found; k++)
        {
            var t = (double)k / steps;
            if (curve(t) >= progress)
            {
                lower = (double)(k - 1) / steps;
                upper = t;
                found = true;
            }
        }

        if (!found)
            return 1d;

        for (var i = 0; i < 40; i++)
        {
            var mid = (lower + upper) / 2d;
            if (curve(mid) >= progress)
                upper = mid;
            else
                lower = mid;
        }

        return upper;
    }

    static void BakeGap(CompiledEffect effect, double hold, double scale)
    {
        var frames = effect.Frames;
        if (frames.Count == 0)
            return;

        var first = frames[0].CopyAt(0d, "linear", Easings.Linear);
        foreach (var frame in frames)
            frame.Offset = hold + frame.Offset * scale;

        var last = frames[^1].CopyAt(1d, "linear", Easings.Linear);
        frames.Insert(0, first);
        frames.Add(last);
    }

    static string DirectionCss(PlaybackDirection direction) => direction switch
    {
        PlaybackDirection.Reverse => "reverse",
        PlaybackDirection.Alternate => "alternate",
        PlaybackDirection.AlternateReverse => "alternate-reverse",
        _ => "normal"
    };

    static string FillCss(FillMode fill) => fill switch
    {
        FillMode.Forwards => "forwards",
        FillMode.Backwards => "backwards",
        FillMode.Both => "both",
        _ => "none"
    };
}

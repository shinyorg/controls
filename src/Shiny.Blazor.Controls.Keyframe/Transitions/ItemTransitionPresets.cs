using global::Shiny.Controls.Keyframe;
using Shiny.Blazor.Controls.Keyframe.Compilation;

namespace Shiny.Blazor.Controls.Keyframe;

/// <summary>The tracks behind each <see cref="ItemTransition"/>, written as an enter; an exit runs them backwards.</summary>
internal static class ItemTransitionPresets
{
    public static IReadOnlyList<TrackSpec> Enter(ItemTransition preset) => preset switch
    {
        ItemTransition.Fade => [Fade()],
        ItemTransition.FadeUp => [Fade(), Track("TranslationY", "16", "0")],
        ItemTransition.FadeDown => [Fade(), Track("TranslationY", "-16", "0")],
        ItemTransition.SlideLeft => [Fade(), Track("TranslationX", "40", "0")],
        ItemTransition.SlideRight => [Fade(), Track("TranslationX", "-40", "0")],
        ItemTransition.Scale => [Fade(), Track("Scale", "0.85", "1")],
        ItemTransition.Pop =>
        [
            new TrackSpec("Opacity", [new KeySpec(0, "0", null), new KeySpec(0.4, "1", null)]),
            new TrackSpec("Scale", [new KeySpec(0, "0.6", "spring(0.45, 14)"), new KeySpec(1, "1", null)])
        ],
        _ => []
    };

    /// <summary>
    /// The same tracks reversed in time: what was the end is now the start. Per-key curves are
    /// dropped (a spring run backwards reads as a wind-up); the group's Easing shapes the exit.
    /// </summary>
    public static IReadOnlyList<TrackSpec> Exit(ItemTransition preset)
        => Enter(preset)
            .Select(t => new TrackSpec(t.Property, t.Keys
                .Select(k => new KeySpec(1d - k.Offset, k.Value, null))
                .Reverse()
                .ToList()))
            .ToList();

    static TrackSpec Fade() => Track("Opacity", "0", "1");

    static TrackSpec Track(string property, string from, string to)
        => new(property, [new KeySpec(0, from, null), new KeySpec(1, to, null)]);

    /// <summary>The compiled program for one preset or template, as the script's item transitions read it.</summary>
    public static CompiledAnimation Compile(IReadOnlyList<TrackSpec> tracks, TimeSpan duration, string? easing)
        => KeyframeCompiler.Compile(
            new AnimationSpec(new TimingSpec { Duration = duration, Easing = easing, Fill = FillMode.Both }, tracks),
            live: null,
            implicitEnds: true);

    /// <summary>A template Keyframes, with fill forced to Both so an item never flashes its resting look before or after.</summary>
    public static CompiledAnimation Compile(Keyframes template)
    {
        var spec = template.Spec();
        return KeyframeCompiler.Compile(spec with { Timing = spec.Timing with { Fill = FillMode.Both } }, live: null, implicitEnds: true);
    }
}

using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using global::Shiny.Controls.Keyframe;

namespace Shiny.Blazor.Controls.Keyframe.Compilation;

/// <summary>Where an animation lands: an element passed alongside the program, an id, or a child of either.</summary>
/// <param name="ElementIndex">Index into the element array handed to the script, or -1.</param>
/// <param name="Id">An element id, used when there is no element reference.</param>
/// <param name="ChildIndex">When 0 or more, the animation targets that child element of the resolved element.</param>
internal readonly record struct TargetSpec(int ElementIndex, string? Id, int ChildIndex = -1);

/// <summary>One compiled animation placed on a target, with a start offset inside its group.</summary>
internal sealed record ProgramItem(TargetSpec Target, CompiledAnimation Animation, double OffsetMs);

/// <summary>
/// Writes the JSON a keyframe group's script runs from. Written by hand with
/// <see cref="Utf8JsonWriter"/> rather than serialised from types, so it stays trim and AOT safe and
/// the wire format is spelled out in one place.
/// </summary>
internal static class ProgramWriter
{
    /// <summary>The program for a group of animations played as one.</summary>
    public static string Write(IReadOnlyList<ProgramItem> items, ReducedMotionBehavior reducedMotion, bool restoreOnStop, double rate)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("reduced", reducedMotion switch
            {
                ReducedMotionBehavior.Play => "play",
                ReducedMotionBehavior.Instant => "instant",
                _ => "auto"
            });
            json.WriteBoolean("restore", restoreOnStop);
            json.WriteNumber("rate", double.IsFinite(rate) ? rate : 1d);
            json.WriteNumber("totalMs", ScrubLength(items));

            json.WriteStartArray("items");
            foreach (var item in items)
                WriteItem(json, item);
            json.WriteEndArray();

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// What <c>SeekProgress</c> spans: the end of the last finite animation, or, when everything
    /// loops, the first pass of the longest one.
    /// </summary>
    public static double ScrubLength(IReadOnlyList<ProgramItem> items)
    {
        var finite = items.Where(i => double.IsFinite(i.Animation.TotalMs)).Select(i => i.OffsetMs + i.Animation.TotalMs).ToList();
        if (finite.Count > 0)
            return Math.Max(finite.Max(), 0d);

        return items.Count == 0 ? 0d : Math.Max(items.Max(i => i.OffsetMs + i.Animation.FirstPassMs), 0d);
    }

    /// <summary>One animation with no target - an item transition the script applies to whichever element enters or leaves.</summary>
    public static string WriteSingle(CompiledAnimation animation)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            WriteTiming(json, animation.Timing);
            WriteEffects(json, animation.Effects);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    static void WriteEffects(Utf8JsonWriter json, IEnumerable<CompiledEffect> effects)
    {
        json.WriteStartArray("effects");
        foreach (var effect in effects)
        {
            json.WriteStartArray();
            foreach (var frame in effect.Frames)
                WriteFrame(json, frame);
            json.WriteEndArray();
        }
        json.WriteEndArray();
    }

    static void WriteItem(Utf8JsonWriter json, ProgramItem item)
    {
        json.WriteStartObject();

        json.WriteStartObject("t");
        json.WriteNumber("e", item.Target.ElementIndex);
        if (item.Target.Id is null)
            json.WriteNull("id");
        else
            json.WriteString("id", item.Target.Id);
        json.WriteNumber("c", item.Target.ChildIndex);
        json.WriteEndObject();

        WriteTiming(json, item.Animation.Timing.WithDelayOffset(item.OffsetMs));
        WriteEffects(json, item.Animation.Effects);

        json.WriteEndObject();
    }

    internal static void WriteTiming(Utf8JsonWriter json, CompiledTiming timing)
    {
        json.WriteStartObject("timing");
        json.WriteNumber("duration", Round(timing.DurationMs));
        json.WriteNumber("delay", Round(timing.DelayMs));
        json.WriteNumber("endDelay", Round(timing.EndDelayMs));
        // JSON has no Infinity; the script reads -1 as forever.
        json.WriteNumber("iterations", double.IsPositiveInfinity(timing.Iterations) ? -1d : timing.Iterations);
        json.WriteNumber("iterationStart", timing.IterationStart);
        json.WriteString("direction", timing.Direction);
        json.WriteString("fill", timing.Fill);
        json.WriteString("easing", timing.Easing);
        json.WriteEndObject();
    }

    static void WriteFrame(Utf8JsonWriter json, CompiledFrame frame)
    {
        json.WriteStartObject();
        json.WriteNumber("offset", Math.Round(Math.Clamp(frame.Offset, 0d, 1d), 6));
        json.WriteString("easing", frame.EasingCss);
        foreach (var (css, value) in frame.Values)
            json.WriteString(CssProperties.ToCamel(css), value);
        json.WriteEndObject();
    }

    static double Round(double ms) => Math.Round(ms, 3);


    /// <summary>
    /// One effect as a CSS <c>@keyframes</c> rule, for places the Web Animations API cannot reach -
    /// the <c>::view-transition</c> pseudo-elements a page transition animates.
    /// </summary>
    public static string ToCssKeyframes(string name, CompiledEffect effect)
    {
        var css = new StringBuilder();
        css.Append("@keyframes ").Append(name).Append(" {");

        foreach (var frame in effect.Frames)
        {
            css.Append(' ')
               .Append(Math.Round(Math.Clamp(frame.Offset, 0d, 1d) * 100d, 4).ToString("0.####", CultureInfo.InvariantCulture))
               .Append("% {");

            foreach (var (property, value) in frame.Values)
                css.Append(' ').Append(property).Append(": ").Append(value).Append(';');

            css.Append(" animation-timing-function: ").Append(frame.EasingCss).Append("; }");
        }

        return css.Append(" }").ToString();
    }
}

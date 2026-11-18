using System.Runtime.CompilerServices;

namespace Shiny.Maui.Controls.Keyframe;

/// <summary>
/// Runs one property from the value it showed to the value it has now — the engine behind visual-state
/// transitions and implicit property transitions.
/// </summary>
/// <remarks>
/// The property has already changed by the time a transition starts: the state, binding or setter that
/// changed it owns the new value. The runner only <i>shows</i> the way there, through the
/// <see cref="AnimationLayer"/>, and releases at the end, so what remains on screen is exactly the value
/// that source set — and if that source changes again mid-flight, the next transition starts from
/// wherever this one had got to.
/// </remarks>
static class TransitionRunner
{
    static readonly ConditionalWeakTable<VisualElement, Dictionary<BindableProperty, Player>> Running = [];

    public static void Start(
        VisualElement element,
        BindableProperty property,
        string? registeredName,
        object? from,
        object? to,
        TimeSpan duration,
        TimeSpan delay,
        EasingFunction? easing,
        ReducedMotionBehavior reducedMotion)
    {
        var running = Running.GetValue(element, static _ => []);
        if (running.Remove(property, out var previous))
        {
            // Hold: the new transition starts from the frame on screen, which the caller captured.
            previous.Stop(StopBehavior.Hold);
            previous.Dispose();
        }

        if (duration <= TimeSpan.Zero || Equals(from, to) || !TryFillNulls(property.ReturnType, ref from, ref to))
        {
            AnimationLayer.Release(element, property);
            return;
        }

        var descriptor = registeredName is null ? null : AnimatableProperties.Find(registeredName);
        if (descriptor?.GetBindableProperty(element) != property)
            descriptor = AnimatableProperty.For(property);

        ITrack track;
        try
        {
            track = descriptor.CreateTrack(element, (RawKey[])[new RawKey(0d, from, easing), new RawKey(1d, to, null)]);
        }
        catch (FormatException)
        {
            // A value the descriptor can't read (an exotic type behind a registered name): snap.
            AnimationLayer.Release(element, property);
            return;
        }

        // Backwards fill shows the starting value during the delay; no forwards fill means the
        // timeline releases at the end and the property shows its own value again.
        var timeline = new Timeline(new Timing
        {
            Duration = duration,
            Delay = delay,
            Fill = FillMode.Backwards
        }).Add(track);

        var player = new Player(timeline, MauiClock.For(element)) { ReducedMotion = reducedMotion };
        running[property] = player;
        player.Finished += (_, _) =>
        {
            if (running.TryGetValue(property, out var current) && ReferenceEquals(current, player))
                running.Remove(property);

            AnimationLayer.Release(element, property);
            player.Dispose();
        };

        player.Play();
    }

    /// <summary>Stops a property's running transition, leaving it on its own value.</summary>
    public static void Stop(VisualElement element, BindableProperty property)
    {
        if (!Running.TryGetValue(element, out var running) || !running.Remove(property, out var player))
            return;

        player.Stop(StopBehavior.Release);
        player.Dispose();
    }

    /// <summary>
    /// A transition to or from "no value" — a background colour a state sets and the normal state
    /// leaves unset — fades through the other end's transparent self rather than snapping.
    /// </summary>
    static bool TryFillNulls(Type type, ref object? from, ref object? to)
    {
        if (from is not null && to is not null)
            return true;

        if (from is null && to is null)
            return false;

        if (type == typeof(Color))
        {
            from ??= ((Color)to!).WithAlpha(0f);
            to ??= ((Color)from).WithAlpha(0f);
            return true;
        }

        if (typeof(Brush).IsAssignableFrom(type) && (from ?? to) is SolidColorBrush solid)
        {
            from ??= new SolidColorBrush(solid.Color.WithAlpha(0f));
            to ??= new SolidColorBrush(solid.Color.WithAlpha(0f));
            return true;
        }

        return false;
    }
}

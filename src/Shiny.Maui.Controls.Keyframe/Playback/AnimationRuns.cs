using System.Runtime.CompilerServices;

namespace Shiny.Maui.Controls.Keyframe;

/// <summary>
/// The players started on each element by definition, so trigger actions and code can reach a run
/// started somewhere else: a <c>StopAnimation</c> in a trigger's exit actions finds the run its
/// <c>BeginAnimation</c> started.
/// </summary>
static class AnimationRuns
{
    static readonly ConditionalWeakTable<VisualElement, Dictionary<IAnimationDefinition, Player>> Runs = [];

    public static Player Begin(VisualElement element, IAnimationDefinition definition, HandoffBehavior handoff = HandoffBehavior.Replace)
    {
        var runs = Runs.GetValue(element, static _ => new Dictionary<IAnimationDefinition, Player>(ReferenceEqualityComparer.Instance));

        if (runs.TryGetValue(definition, out var existing))
        {
            if (handoff == HandoffBehavior.Continue && existing.State == PlaybackState.Running)
                return existing;

            // Hold rather than release: the new run captures where the old one left off, so any
            // implicit keys continue from the current frame instead of snapping back first.
            existing.Stop(StopBehavior.Hold);
            existing.Dispose();
        }

        var node = definition.Build(element, name => NameScope.Find(element, name));
        var player = new Player(node, MauiClock.For(element))
        {
            Rate = definition.Speed,
            ReducedMotion = definition.ReducedMotion
        };

        runs[definition] = player;
        player.Play();
        return player;
    }

    public static Task<bool> BeginAsync(VisualElement element, IAnimationDefinition definition, CancellationToken cancellationToken)
    {
        var player = Begin(element, definition);
        if (player.State == PlaybackState.Finished)
            return Task.FromResult(true);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFinished(object? sender, EventArgs e) => tcs.TrySetResult(true);
        player.Finished += OnFinished;

        var registration = cancellationToken.Register(() =>
        {
            player.Stop(StopBehavior.Release);
            tcs.TrySetResult(false);
        });

        tcs.Task.ContinueWith(
            _ =>
            {
                player.Finished -= OnFinished;
                registration.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return tcs.Task;
    }

    public static Player? Find(VisualElement element, IAnimationDefinition definition)
        => Runs.TryGetValue(element, out var runs) && runs.TryGetValue(definition, out var player) ? player : null;

    public static IReadOnlyList<Player> FindAll(VisualElement element)
        => Runs.TryGetValue(element, out var runs) ? runs.Values.ToList() : [];

    public static void Stop(VisualElement element, IAnimationDefinition definition, StopBehavior behavior)
    {
        if (!Runs.TryGetValue(element, out var runs) || !runs.Remove(definition, out var player))
            return;

        player.Stop(behavior);
        player.Dispose();
    }

    public static void StopAll(VisualElement element, StopBehavior behavior)
    {
        if (!Runs.TryGetValue(element, out var runs))
            return;

        foreach (var player in runs.Values)
        {
            player.Stop(behavior);
            player.Dispose();
        }

        runs.Clear();
    }
}

/// <summary>What starting a definition that is already running on an element does.</summary>
public enum HandoffBehavior
{
    /// <summary>Restart it from the beginning, picking up from the current frame. The default.</summary>
    Replace,

    /// <summary>Leave the running one alone.</summary>
    Continue
}

/// <summary>Finds an <c>x:Name</c> in scope from an element.</summary>
static class NameScope
{
    public static VisualElement? Find(VisualElement scope, string name)
    {
        // Walk outward until something in the tree knows the name. Names are registered on the
        // page or view that declared them, which is usually an ancestor of the animated element.
        Element? current = scope;

        while (current is not null)
        {
            if (current is VisualElement candidate && candidate.FindByName(name) is VisualElement found)
                return found;

            current = current.Parent;
        }

        return null;
    }
}

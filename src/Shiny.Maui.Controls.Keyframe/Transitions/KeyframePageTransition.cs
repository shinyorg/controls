using Shiny.Maui.Controls;

namespace Shiny.Maui.Controls.Keyframe;

/// <summary>
/// A page transition written as Keyframes: <see cref="Enter"/> plays on the incoming page and
/// <see cref="Exit"/> on the outgoing one. Plugs into core's page-transition animator seam, so it works
/// wherever <see cref="PageTransition"/> does — <c>PageTransition.Animator</c> on a page,
/// <c>PageTransition.DefaultAnimator</c>, or the per-call navigation overloads.
/// </summary>
/// <example>
/// <code language="xml">
/// &lt;kf:KeyframePageTransition x:Key="Drop"&gt;
///   &lt;kf:KeyframePageTransition.Enter&gt;
///     &lt;kf:Keyframes Duration="0:0:0.4"&gt;
///       &lt;kf:Track Property="TranslationY"&gt;&lt;kf:Key Offset="0" Value="-600" Easing="BackOut" /&gt;&lt;kf:Key Offset="1" Value="0" /&gt;&lt;/kf:Track&gt;
///     &lt;/kf:Keyframes&gt;
///   &lt;/kf:KeyframePageTransition.Enter&gt;
///   &lt;kf:KeyframePageTransition.Exit&gt;
///     &lt;kf:Keyframes Duration="0:0:0.4"&gt;
///       &lt;kf:Track Property="Opacity"&gt;&lt;kf:Key Offset="0" Value="1" /&gt;&lt;kf:Key Offset="1" Value="0" /&gt;&lt;/kf:Track&gt;
///     &lt;/kf:Keyframes&gt;
///   &lt;/kf:KeyframePageTransition.Exit&gt;
/// &lt;/kf:KeyframePageTransition&gt;
///
/// &lt;ContentPage shiny:PageTransition.Animator="{StaticResource Drop}" /&gt;
/// </code>
/// </example>
/// <remarks>
/// <para>A pop uses <see cref="PopEnter"/> and <see cref="PopExit"/> when set. Without them it plays the
/// push backwards: the page coming back runs <see cref="Exit"/> in reverse, and the page leaving runs
/// <see cref="Enter"/> in reverse — so a page that dropped in rises back out.</para>
/// <para>The animations run for their own durations, not the navigation's <c>Duration</c>; the
/// navigation stays open until both have finished. Every timeline holds both of its edges, so the
/// incoming page never flashes in its final position. On heads that take the outgoing
/// page off screen before the transition starts (Windows, AppKit, GTK), only the incoming half plays.</para>
/// </remarks>
public sealed class KeyframePageTransition : BindableObject, IPageTransitionAnimator
{
    /// <summary>Played on the incoming page of a push.</summary>
    public IAnimationDefinition? Enter { get; set; }

    /// <summary>Played on the outgoing page of a push.</summary>
    public IAnimationDefinition? Exit { get; set; }

    /// <summary>Played on the page coming back on a pop. Falls back to <see cref="Exit"/> in reverse.</summary>
    public IAnimationDefinition? PopEnter { get; set; }

    /// <summary>Played on the page leaving on a pop. Falls back to <see cref="Enter"/> in reverse.</summary>
    public IAnimationDefinition? PopExit { get; set; }

    /// <summary>What happens under the OS reduced-motion setting. Core's page-transition reduced-motion
    /// rule (a short crossfade by default) normally applies before this animator is chosen.</summary>
    public ReducedMotionBehavior ReducedMotion { get; set; }

    /// <inheritdoc />
    public async Task AnimateAsync(PageTransitionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var parts = new List<(VisualElement Surface, IAnimationDefinition Definition, bool Reversed)>(2);

        if (context.IsPop)
        {
            AddPart(parts, context.ToSurface, PopEnter, Exit);
            AddPart(parts, context.FromSurface, PopExit, Enter);
        }
        else
        {
            AddPart(parts, context.ToSurface, Enter, null);
            AddPart(parts, context.FromSurface, Exit, null);
        }

        await Task.WhenAll(parts.Select(p => Run(p.Surface, p.Definition, p.Reversed, cancellationToken)));
    }

    static void AddPart(
        List<(VisualElement, IAnimationDefinition, bool)> parts,
        VisualElement? surface,
        IAnimationDefinition? own,
        IAnimationDefinition? reverseOf)
    {
        if (surface is null)
            return;

        if (own is not null)
            parts.Add((surface, own, false));
        else if (reverseOf is not null)
            parts.Add((surface, reverseOf, true));
    }

    Task Run(VisualElement surface, IAnimationDefinition definition, bool reversed, CancellationToken cancellationToken)
    {
        var node = definition.Build(surface, name => NameScope.Find(surface, name));

        // Hold both edges: the incoming page must show its opening pose from the first frame (which,
        // played backwards, is the end of the timeline), and nothing should snap before the host resets.
        NodeFill.Add(node, FillMode.Both);

        var player = new Player(node, MauiClock.For(surface))
        {
            Rate = reversed ? -Math.Abs(definition.Speed) : definition.Speed,
            ReducedMotion = ReducedMotion
        };

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration registration = default;

        // Cleanup runs where the run ends — on the UI thread, from the clock or from the caller's
        // cancellation — never on a thread-pool continuation.
        void Complete()
        {
            if (!tcs.TrySetResult())
                return;

            registration.Dispose();
            player.Stop(StopBehavior.Release);
            player.Dispose();
        }

        player.Finished += (_, _) => Complete();
        registration = cancellationToken.Register(() => surface.Dispatcher.Dispatch(Complete));

        player.Play();
        if (reversed && player.State == PlaybackState.Running && player.Duration != TimeSpan.MaxValue)
            player.Seek(player.Duration);

        return tcs.Task;
    }
}

/// <summary>Adds a fill to every timeline in a freshly built node.</summary>
static class NodeFill
{
    public static void Add(IAnimationNode node, FillMode fill)
    {
        if (node is Timeline timeline)
            timeline.Timing.Fill |= fill;

        if (node is Storyboard storyboard)
        {
            foreach (var (child, _) in storyboard.Children)
                Add(child, fill);
        }
    }
}

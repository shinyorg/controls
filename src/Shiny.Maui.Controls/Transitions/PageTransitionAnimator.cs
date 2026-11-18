namespace Shiny.Maui.Controls;

/// <summary>
/// A custom page transition. Set one on a page with <c>PageTransition.Animator</c>, app-wide with
/// <see cref="PageTransition.DefaultAnimator"/>, or for one navigation with the
/// <c>PushAsync(page, animator)</c> family of overloads.
/// </summary>
/// <remarks>
/// <para>Both pages are in the tree and laid out when <see cref="AnimateAsync"/> runs. Animate any
/// <see cref="VisualElement"/> properties on <see cref="PageTransitionContext.FromSurface"/> and
/// <see cref="PageTransitionContext.ToSurface"/> (or on their content) and complete the task when
/// done. The navigation stays open until it completes (on Android, for at most 30 seconds).</para>
/// <para>Afterwards the host resets <c>TranslationX/Y</c>, <c>Scale</c>, <c>ScaleX/Y</c>,
/// <c>Rotation</c>, <c>RotationX/Y</c>, <c>Opacity</c> and <c>AnchorX/Y</c> on both pages and both
/// surfaces, so leave them wherever the animation ends. The token is cancelled if the navigation is
/// torn down: an interactive back gesture, or a second navigation starting on the managed heads.
/// Stop promptly and let the task end (throwing <see cref="OperationCanceledException"/> is fine).</para>
/// <para><b>Animate the surfaces, not the pages.</b> A page's own transform moves its native view on
/// iOS, Android, Windows and macOS (AppKit), but not on Linux (GTK4), where a page sits in a
/// <c>Gtk.Stack</c> that ignores it; there the surface is the page's <c>Content</c>, which does
/// move (the page's background stays put). <see cref="PageTransitionContext.FromSurface"/> is the
/// right element on every head.</para>
/// <para>The incoming page is stacked above the outgoing one on a push and below it on a pop. Either
/// page can be <c>null</c>: <see cref="PageTransitionContext.From"/> is null on the managed heads
/// (Windows, macOS, Mac Catalyst, Linux) when the head has already taken the outgoing page off
/// screen, which is every push and pop except a modal presented over a presenter that stays visible.
/// An interactive back gesture never runs a custom animator; it falls back to the page's
/// <c>Push</c>/<c>Pop</c> type (or the platform's swipe).</para>
/// </remarks>
public interface IPageTransitionAnimator
{
    /// <summary>Runs the transition. See the interface remarks for what the host guarantees.</summary>
    Task AnimateAsync(PageTransitionContext context, CancellationToken cancellationToken);
}


/// <summary>What a custom <see cref="IPageTransitionAnimator"/> is animating.</summary>
/// <param name="From">The page being left, or null when it is not on screen (see <see cref="IPageTransitionAnimator"/>).</param>
/// <param name="To">The page arriving, or null when it is not on screen (a modal dismissed over a presenter the platform keeps out of the transition).</param>
/// <param name="IsPop">True for a pop or a modal dismiss.</param>
/// <param name="IsModal">True for a modal present or dismiss.</param>
/// <param name="Duration">How long the transition should take (the call's, the page's, or the app-wide duration).</param>
/// <param name="Bounds">The size of the area the pages fill, in device-independent units.</param>
public sealed record PageTransitionContext(Page? From, Page? To, bool IsPop, bool IsModal, TimeSpan Duration, Size Bounds)
{
    /// <summary>The direction resolved for this navigation (the call's, then the page's, then the transition's natural one).</summary>
    public PageTransitionDirection Direction { get; init; } = PageTransitionDirection.FromRight;

    /// <summary>The element to animate for <see cref="From"/> on this head: the page itself, or its content on Linux (GTK4).</summary>
    public VisualElement? FromSurface => PageTransition.GetAnimationSurface(this.From);

    /// <summary>The element to animate for <see cref="To"/> on this head: the page itself, or its content on Linux (GTK4).</summary>
    public VisualElement? ToSurface => PageTransition.GetAnimationSurface(this.To);
}


/// <summary>
/// What a navigation will run: a built-in <see cref="Plan"/>, or a custom <see cref="Animator"/>
/// (in which case <see cref="Plan"/> carries the duration and direction it gets, and the type an
/// interactive back gesture falls back to).
/// </summary>
public readonly record struct PageTransitionResolution(PageTransitionPlan Plan, IPageTransitionAnimator? Animator)
{
    /// <summary>True when anything other than the platform's own animation should run.</summary>
    public bool IsSet => this.Animator is not null || this.Plan.Type != PageTransitionType.Default;
}


/// <summary>What page transitions do when the user has asked the OS for less motion.</summary>
public enum PageTransitionReducedMotion
{
    /// <summary>Every transition (custom animators included) becomes a short crossfade, with no hero flights. The default.</summary>
    Crossfade,
    /// <summary>Every transition (custom animators included) becomes <see cref="PageTransitionType.None"/>.</summary>
    None,
    /// <summary>Transitions run as configured.</summary>
    Ignore
}


/// <summary>A transition handed to one navigation call, ahead of everything the page or app says.</summary>
sealed record PageTransitionOverride(
    PageTransitionType Type,
    PageTransitionDirection Direction,
    TimeSpan? Duration,
    IPageTransitionAnimator? Animator
);


/// <summary>
/// The one-shot transitions the per-call overloads hand to the resolver. A push or pop keys its
/// override on the page it moves (the incoming page on a push, the outgoing one on a pop); a Shell
/// <c>GoToAsync</c> can't know its pages up front, so its override is ambient for the length of the
/// call. Either way it is cleared once the navigation finishes, so it never outlives its call.
/// </summary>
static class PageTransitionOverrides
{
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Page, PageTransitionOverride> byPage = new();
    static PageTransitionOverride? ambient;

    internal static void Set(Page page, PageTransitionOverride value) => byPage.AddOrUpdate(page, value);
    internal static void Clear(Page page) => byPage.Remove(page);

    internal static void SetAmbient(PageTransitionOverride? value) => ambient = value;

    internal static PageTransitionOverride? Find(Page? page)
    {
        if (page is not null && byPage.TryGetValue(page, out var found))
            return found;

        return ambient;
    }
}


/// <summary>
/// The one transition in flight that a custom animator or managed run is part of. Starting another
/// cancels it, which is how a second navigation or an interactive back gesture tears it down.
/// </summary>
static class PageTransitionCancellation
{
    static CancellationTokenSource? current;

    internal static CancellationToken Begin()
    {
        Cancel();
        current = new CancellationTokenSource();
        return current.Token;
    }

    internal static void Cancel()
    {
        var running = current;
        current = null;
        if (running is null)
            return;

        running.Cancel();
        running.Dispose();
    }

    internal static void End(CancellationToken token)
    {
        if (current is not null && current.Token == token)
        {
            current.Dispose();
            current = null;
        }
    }
}


/// <summary>Runs a custom animator the way the interface promises, on any head.</summary>
static class PageTransitionHost
{
    internal static async Task RunAnimatorAsync(IPageTransitionAnimator animator, PageTransitionContext context, CancellationToken cancellationToken)
    {
        try
        {
            await animator.AnimateAsync(context, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            // A broken animator must never strand a navigation half way.
            System.Diagnostics.Debug.WriteLine($"[Shiny.PageTransition] Custom animator failed: {ex}");
        }
        finally
        {
            Reset(context.From);
            Reset(context.To);
        }
    }


    /// <summary>Puts a page (and, where it differs, its animation surface) back at rest.</summary>
    internal static void Reset(Page? page)
    {
        if (page is null)
            return;

        ResetElement(page);
        var surface = PageTransition.GetAnimationSurface(page);
        if (surface is not null && !ReferenceEquals(surface, page))
            ResetElement(surface);
    }


    internal static void ResetElement(VisualElement element)
    {
        element.AbortAnimation(ManagedTransitionRunner.AnimationName);
        element.TranslationX = 0;
        element.TranslationY = 0;
        element.Scale = 1;
        element.ScaleX = 1;
        element.ScaleY = 1;
        element.Rotation = 0;
        element.RotationX = 0;
        element.RotationY = 0;
        element.Opacity = 1;
        element.AnchorX = 0.5;
        element.AnchorY = 0.5;
    }
}

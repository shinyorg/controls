namespace Shiny.Maui.Controls;

/// <summary>
/// How a page arrives when it is pushed, or leaves when it is popped.
/// </summary>
public enum PageTransitionType
{
    /// <summary>Not set here - fall back to the next level (the page's Push for a pop, then the app default, then the platform's own).</summary>
    Default,
    /// <summary>No animation at all.</summary>
    None,
    /// <summary>The incoming page fades in over the outgoing one.</summary>
    Fade,
    /// <summary>Material "fade through": the outgoing page fades out, then the incoming one fades in while growing slightly. For screens with no relationship to each other.</summary>
    FadeThrough,
    /// <summary>The platform-style push: the incoming page slides in from <see cref="PageTransitionDirection"/> while the outgoing one drifts a little the other way.</summary>
    Slide,
    /// <summary>Both pages move together, edge to edge, like a carousel.</summary>
    Push,
    /// <summary>The incoming page slides in over the outgoing one, which stays put.</summary>
    Cover,
    /// <summary>The outgoing page slides away and uncovers the incoming one underneath.</summary>
    Reveal,
    /// <summary>Material shared axis on X: both pages shift a little horizontally while crossfading. For steps in a flow.</summary>
    SharedAxisX,
    /// <summary>Material shared axis on Y: both pages shift a little vertically while crossfading.</summary>
    SharedAxisY,
    /// <summary>Material shared axis on Z: the incoming page grows in while the outgoing one grows past the viewer and fades. For parent/child.</summary>
    SharedAxisZ,
    /// <summary>The incoming page grows in from slightly smaller while fading in.</summary>
    Zoom,
    /// <summary>The incoming page grows out of a circle from the point that was last tapped, and shrinks back into it on pop.</summary>
    CircularReveal,
    /// <summary>The page turns over in 3D around its vertical (or, for vertical directions, horizontal) axis.</summary>
    Flip,
    /// <summary>iOS 18's own zoom transition, growing the page out of its <see cref="PageTransition.HeroProperty">hero</see> source, with interactive swipe and pinch to dismiss. Shell and modal pages on iOS 18+; elsewhere it falls back to <see cref="Zoom"/> with the hero flown.</summary>
    NativeZoom
}


/// <summary>
/// The edge a page comes in from. A pop runs the same transition in reverse, so the page leaves
/// toward the edge it came in from.
/// </summary>
public enum PageTransitionDirection
{
    /// <summary>The transition's natural direction: from the right for slides and flips, from the bottom for <see cref="PageTransitionType.SharedAxisY"/>.</summary>
    Default,
    FromRight,
    FromLeft,
    FromBottom,
    FromTop
}


/// <summary>
/// How a layout's children enter when their page appears.
/// </summary>
public enum PageEntranceAnimation
{
    None,
    /// <summary>Each child fades in.</summary>
    Fade,
    /// <summary>Each child fades in while rising a short distance.</summary>
    FadeUp,
    /// <summary>Each child fades in while sliding in from the leading edge.</summary>
    SlideIn,
    /// <summary>Each child fades in while growing from slightly smaller.</summary>
    Scale
}


/// <summary>
/// One resolved transition: what to run, which way, and for how long.
/// </summary>
public readonly record struct PageTransitionPlan(
    PageTransitionType Type,
    PageTransitionDirection Direction,
    bool IsPop,
    TimeSpan Duration
)
{
    /// <summary>
    /// The unit vector toward the edge the incoming page comes from (+1 = right/bottom). On a pop
    /// it is the edge the outgoing page leaves toward.
    /// </summary>
    public (int X, int Y) Vector => this.Direction switch
    {
        PageTransitionDirection.FromLeft => (-1, 0),
        PageTransitionDirection.FromBottom => (0, 1),
        PageTransitionDirection.FromTop => (0, -1),
        _ => (1, 0)
    };

    /// <summary>True when the transition moves along the vertical axis.</summary>
    public bool IsVertical => this.Direction is PageTransitionDirection.FromBottom or PageTransitionDirection.FromTop;

    public bool IsAnimated => this.Type is not (PageTransitionType.None or PageTransitionType.Default);
}


/// <summary>
/// Page push/pop transitions for <see cref="Shell"/> and <see cref="NavigationPage"/> stacks and
/// modal pages, shared-element ("hero") transitions between pages, and entrance choreography for a
/// page's content.
/// </summary>
/// <remarks>
/// <para>Set on the page itself: <c>shiny:PageTransition.Push="Cover"</c> is how the page arrives,
/// <c>shiny:PageTransition.Direction="FromBottom"</c> which edge it comes from, and
/// <c>shiny:PageTransition.Pop</c> how it leaves if that should differ. A pop that is left at
/// <see cref="PageTransitionType.Default"/> runs the page's push in reverse.</para>
/// <para>Nothing navigates differently: <c>Navigation.PushAsync</c>, <c>PushModalAsync</c>,
/// <c>Shell.GoToAsync</c> and any navigation library built on them (Shiny MAUI Shell included)
/// pick the transition up from the page, so no navigation API needs to know these exist. Turned on
/// with <c>UseShinyControls(cfg =&gt; cfg.UsePageTransitions())</c>.</para>
/// </remarks>
public static class PageTransition
{
    #region Page properties

    public static readonly BindableProperty PushProperty = BindableProperty.CreateAttached(
        "Push", typeof(PageTransitionType), typeof(PageTransition), PageTransitionType.Default);

    public static readonly BindableProperty PopProperty = BindableProperty.CreateAttached(
        "Pop", typeof(PageTransitionType), typeof(PageTransition), PageTransitionType.Default);

    public static readonly BindableProperty DirectionProperty = BindableProperty.CreateAttached(
        "Direction", typeof(PageTransitionDirection), typeof(PageTransition), PageTransitionDirection.Default);

    /// <summary>Overrides <see cref="Duration"/> for this page's transitions.</summary>
    public static readonly BindableProperty DurationProperty = BindableProperty.CreateAttached(
        "Duration", typeof(TimeSpan?), typeof(PageTransition), null);

    public static PageTransitionType GetPush(BindableObject view) => (PageTransitionType)view.GetValue(PushProperty);
    public static void SetPush(BindableObject view, PageTransitionType value) => view.SetValue(PushProperty, value);

    public static PageTransitionType GetPop(BindableObject view) => (PageTransitionType)view.GetValue(PopProperty);
    public static void SetPop(BindableObject view, PageTransitionType value) => view.SetValue(PopProperty, value);

    public static PageTransitionDirection GetDirection(BindableObject view) => (PageTransitionDirection)view.GetValue(DirectionProperty);
    public static void SetDirection(BindableObject view, PageTransitionDirection value) => view.SetValue(DirectionProperty, value);

    public static TimeSpan? GetDuration(BindableObject view) => (TimeSpan?)view.GetValue(DurationProperty);
    public static void SetDuration(BindableObject view, TimeSpan? value) => view.SetValue(DurationProperty, value);

    /// <summary>
    /// How the page is presented and dismissed when it is shown modally (<c>PushModalAsync</c>), in
    /// place of <see cref="PushProperty"/>/<see cref="PopProperty"/>. Left at
    /// <see cref="PageTransitionType.Default"/>, a modal uses the page's Push (and Pop) as before.
    /// </summary>
    public static readonly BindableProperty ModalProperty = BindableProperty.CreateAttached(
        "Modal", typeof(PageTransitionType), typeof(PageTransition), PageTransitionType.Default);

    /// <summary>
    /// A custom transition for this page's push, pop, modal present and dismiss. It wins over the
    /// page's Push, Pop and Modal types.
    /// </summary>
    public static readonly BindableProperty AnimatorProperty = BindableProperty.CreateAttached(
        "Animator", typeof(IPageTransitionAnimator), typeof(PageTransition), null);

    /// <summary>
    /// Set on a <see cref="ShellContent"/>, <see cref="ShellSection"/> (Tab) or <see cref="ShellItem"/>:
    /// the transition used when switching to it, in place of <see cref="ShellItemTransition"/>. The
    /// most specific one set wins (content, then section, then item).
    /// </summary>
    public static readonly BindableProperty TabProperty = BindableProperty.CreateAttached(
        "Tab", typeof(PageTransitionType), typeof(PageTransition), PageTransitionType.Default);

    public static PageTransitionType GetModal(BindableObject view) => (PageTransitionType)view.GetValue(ModalProperty);
    public static void SetModal(BindableObject view, PageTransitionType value) => view.SetValue(ModalProperty, value);

    public static IPageTransitionAnimator? GetAnimator(BindableObject view) => (IPageTransitionAnimator?)view.GetValue(AnimatorProperty);
    public static void SetAnimator(BindableObject view, IPageTransitionAnimator? value) => view.SetValue(AnimatorProperty, value);

    public static PageTransitionType GetTab(BindableObject view) => (PageTransitionType)view.GetValue(TabProperty);
    public static void SetTab(BindableObject view, PageTransitionType value) => view.SetValue(TabProperty, value);

    #endregion

    #region Hero

    /// <summary>
    /// Pairs a view on one page with a view on the next: give both the same name and, when either
    /// page is pushed or popped, the view flies from where it is on the outgoing page to where it
    /// is on the incoming one while the rest of the page transitions.
    /// </summary>
    public static readonly BindableProperty HeroProperty = BindableProperty.CreateAttached(
        "Hero", typeof(string), typeof(PageTransition), null);

    public static string? GetHero(BindableObject view) => (string?)view.GetValue(HeroProperty);
    public static void SetHero(BindableObject view, string? value) => view.SetValue(HeroProperty, value);


    /// <summary>
    /// Every hero name the two pages share, as (view on <paramref name="from"/>, view on
    /// <paramref name="to"/>). Only views that are showing take part.
    /// </summary>
    public static IReadOnlyList<(VisualElement From, VisualElement To)> FindHeroPairs(Page? from, Page? to)
    {
        if (from is null || to is null)
            return [];

        var source = new Dictionary<string, VisualElement>();
        CollectHeroes(from, source);
        if (source.Count == 0)
            return [];

        var target = new Dictionary<string, VisualElement>();
        CollectHeroes(to, target);

        var pairs = new List<(VisualElement, VisualElement)>();
        foreach (var (name, view) in target)
        {
            if (source.TryGetValue(name, out var origin))
                pairs.Add((origin, view));
        }
        return pairs;
    }


    static void CollectHeroes(IVisualTreeElement element, Dictionary<string, VisualElement> found)
    {
        if (element is VisualElement { IsVisible: false })
            return;

        if (element is VisualElement view && GetHero(view) is { Length: > 0 } name)
            found.TryAdd(name, view);

        foreach (var child in element.GetVisualChildren())
            CollectHeroes(child, found);
    }

    #endregion

    #region Entrance

    /// <summary>
    /// Set on a layout: its children animate in, one after another, when the page appears.
    /// </summary>
    public static readonly BindableProperty EntranceProperty = BindableProperty.CreateAttached(
        "Entrance", typeof(PageEntranceAnimation), typeof(PageTransition), PageEntranceAnimation.None,
        propertyChanged: (b, _, n) => PageEntrance.OnEntranceChanged(b, (PageEntranceAnimation)n));

    /// <summary>Milliseconds between one child starting and the next. Default 50.</summary>
    public static readonly BindableProperty EntranceStaggerProperty = BindableProperty.CreateAttached(
        "EntranceStagger", typeof(int), typeof(PageTransition), 50);

    /// <summary>Milliseconds before the first child starts. Default 120, so the page transition leads.</summary>
    public static readonly BindableProperty EntranceDelayProperty = BindableProperty.CreateAttached(
        "EntranceDelay", typeof(int), typeof(PageTransition), 120);

    /// <summary>Play every time the page appears (true), or only the first time (false, the default) - so coming back to a page does not re-run it.</summary>
    public static readonly BindableProperty EntranceReplayProperty = BindableProperty.CreateAttached(
        "EntranceReplay", typeof(bool), typeof(PageTransition), false);

    public static PageEntranceAnimation GetEntrance(BindableObject view) => (PageEntranceAnimation)view.GetValue(EntranceProperty);
    public static void SetEntrance(BindableObject view, PageEntranceAnimation value) => view.SetValue(EntranceProperty, value);
    public static int GetEntranceStagger(BindableObject view) => (int)view.GetValue(EntranceStaggerProperty);
    public static void SetEntranceStagger(BindableObject view, int value) => view.SetValue(EntranceStaggerProperty, value);
    public static int GetEntranceDelay(BindableObject view) => (int)view.GetValue(EntranceDelayProperty);
    public static void SetEntranceDelay(BindableObject view, int value) => view.SetValue(EntranceDelayProperty, value);
    public static bool GetEntranceReplay(BindableObject view) => (bool)view.GetValue(EntranceReplayProperty);
    public static void SetEntranceReplay(BindableObject view, bool value) => view.SetValue(EntranceReplayProperty, value);

    #endregion

    #region App-wide settings

    /// <summary>
    /// The transition for any page that does not set its own. <see cref="PageTransitionType.Default"/>
    /// leaves those pages on the platform's own animation.
    /// </summary>
    public static PageTransitionType DefaultTransition { get; set; } = PageTransitionType.Default;

    /// <summary>
    /// The transition for modal pages that set neither <see cref="ModalProperty"/> nor a Push.
    /// <see cref="PageTransitionType.Default"/> uses <see cref="DefaultTransition"/>.
    /// </summary>
    public static PageTransitionType DefaultModalTransition { get; set; } = PageTransitionType.Default;

    /// <summary>
    /// A custom transition for every page that sets no transition of its own (no Animator, and no
    /// Push/Pop/Modal type). It wins over <see cref="DefaultTransition"/>.
    /// </summary>
    public static IPageTransitionAnimator? DefaultAnimator { get; set; }

    /// <summary>What transitions do when the OS asks for less motion. Default <see cref="PageTransitionReducedMotion.Crossfade"/>.</summary>
    public static PageTransitionReducedMotion ReducedMotion { get; set; } = PageTransitionReducedMotion.Crossfade;

    /// <summary>The OS reduced-motion setting; swapped in tests.</summary>
    internal static Func<bool> IsReducedMotionRequested { get; set; } = () => Shiny.Maui.Controls.ReducedMotion.IsEnabled;

    /// <summary>The crossfade a reduced-motion transition becomes never runs longer than this.</summary>
    internal static readonly TimeSpan ReducedMotionCrossfade = TimeSpan.FromMilliseconds(200);

    /// <summary>True while reduced motion is changing what transitions run.</summary>
    internal static bool IsMotionReduced => ReducedMotion != PageTransitionReducedMotion.Ignore && IsReducedMotionRequested();

    /// <summary>Hero flights are motion too; reduced motion drops them.</summary>
    internal static bool HeroesAllowed => !IsMotionReduced;

    /// <summary>How long a transition runs, unless the page sets <see cref="DurationProperty"/>.</summary>
    public static TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(320);

    /// <summary>
    /// The transition when switching Shell tabs and flyout items. Default keeps the platform's
    /// (iOS crossfades flyout items; Android switches tabs without animating).
    /// </summary>
    public static PageTransitionType ShellItemTransition { get; set; } = PageTransitionType.Default;

    /// <summary>iOS: swipe back from anywhere on the page, not only the left edge. Default false.</summary>
    public static bool FullScreenSwipeBack { get; set; }

    /// <summary>
    /// Android 14+: preview the page underneath while the back gesture is in progress. Default true.
    /// Needs <c>android:enableOnBackInvokedCallback="true"</c> on the application in the manifest.
    /// </summary>
    public static bool PredictiveBack { get; set; } = true;

    /// <summary>True once <c>UsePageTransitions</c> has installed the platform hooks.</summary>
    public static bool IsEnabled { get; internal set; }

    #endregion

    #region Resolution

    /// <summary>The transition <paramref name="incoming"/> arrives with.</summary>
    public static PageTransitionPlan ResolvePush(Page? incoming)
    {
        var type = incoming is null ? PageTransitionType.Default : GetPush(incoming);
        if (type == PageTransitionType.Default)
            type = DefaultTransition;

        return Plan(incoming, type, false);
    }


    /// <summary>The transition <paramref name="outgoing"/> leaves with when it is popped.</summary>
    public static PageTransitionPlan ResolvePop(Page? outgoing)
    {
        var type = outgoing is null ? PageTransitionType.Default : GetPop(outgoing);
        if (type == PageTransitionType.Default)
            type = outgoing is null ? DefaultTransition : ResolvePush(outgoing).Type;

        return Plan(outgoing, type, true);
    }


    /// <summary>
    /// The built-in transition for a navigation between <paramref name="from"/> and
    /// <paramref name="to"/>: the same as <see cref="ResolveTransition"/>, without a custom animator.
    /// When the pages share a hero and nothing else is set, the pages crossfade so the hero has a
    /// transition to fly over.
    /// </summary>
    public static PageTransitionPlan Resolve(Page? from, Page? to, bool isPop)
        => ResolveTransition(from, to, isPop).Plan;


    /// <summary>
    /// Everything a navigation between <paramref name="from"/> and <paramref name="to"/> will run.
    /// </summary>
    /// <remarks>
    /// <para>Precedence, highest first, for the page that moves (the incoming page on a push, the
    /// outgoing one on a pop):</para>
    /// <list type="number">
    /// <item>A one-shot transition from a per-call overload (<c>PushAsync(page, type)</c>, <c>GoToAsync(state, animator)</c>, ...).</item>
    /// <item>The page's <see cref="AnimatorProperty">Animator</see>.</item>
    /// <item>The page's own type: <see cref="ModalProperty">Modal</see> for a modal, then <see cref="PopProperty">Pop</see> for a pop, then <see cref="PushProperty">Push</see>.</item>
    /// <item><see cref="DefaultAnimator"/>.</item>
    /// <item><see cref="DefaultModalTransition"/> for a modal, then <see cref="DefaultTransition"/>.</item>
    /// <item>The platform's own animation (<see cref="PageTransitionType.Default"/>), or a crossfade when the pages share a hero.</item>
    /// </list>
    /// <para>Then reduced motion, unless <see cref="ReducedMotion"/> is <see cref="PageTransitionReducedMotion.Ignore"/>:
    /// anything set becomes a short crossfade or <see cref="PageTransitionType.None"/>. The platform's own
    /// animation is left to the platform.</para>
    /// </remarks>
    public static PageTransitionResolution ResolveTransition(Page? from, Page? to, bool isPop, bool isModal = false)
    {
        var subject = isPop ? from : to;
        var shot = PageTransitionOverrides.Find(subject);

        var ownType = OwnType(subject, isPop, isModal);
        var pageAnimator = subject is null ? null : GetAnimator(subject);
        var direction = shot?.Direction ?? PageTransitionDirection.Default;

        IPageTransitionAnimator? animator;
        PageTransitionType type;
        if (shot is not null && (shot.Animator is not null || shot.Type != PageTransitionType.Default))
        {
            animator = shot.Animator;
            type = shot.Type != PageTransitionType.Default ? shot.Type : FallbackType(ownType, isModal);
        }
        else if (pageAnimator is not null)
        {
            animator = pageAnimator;
            type = FallbackType(ownType, isModal);
        }
        else if (ownType != PageTransitionType.Default)
        {
            animator = null;
            type = ownType;
        }
        else
        {
            animator = DefaultAnimator;
            type = FallbackType(ownType, isModal);
        }

        if (animator is null && type == PageTransitionType.Default && FindHeroPairs(from, to).Count > 0)
            type = PageTransitionType.Fade;

        var plan = Plan(subject, type, isPop, direction, shot?.Duration);
        return ApplyReducedMotion(new PageTransitionResolution(plan, animator));
    }


    /// <summary>
    /// The transition for switching to a Shell tab or flyout item: the most specific
    /// <see cref="TabProperty">Tab</see> set on its content, section or item, else
    /// <see cref="ShellItemTransition"/>. <paramref name="forward"/> (moving to a later tab) slides in
    /// from the right, backward from the left.
    /// </summary>
    public static PageTransitionPlan ResolveTab(Element? incoming, bool forward)
    {
        var type = TabCandidates(incoming)
            .Where(x => x is not null)
            .Select(x => GetTab(x!))
            .FirstOrDefault(x => x != PageTransitionType.Default);

        if (type == PageTransitionType.Default)
            type = ShellItemTransition;

        var plan = new PageTransitionPlan(
            type,
            forward ? PageTransitionDirection.FromRight : PageTransitionDirection.FromLeft,
            false,
            Duration
        );
        return ApplyReducedMotion(new PageTransitionResolution(plan, null)).Plan;
    }


    static BindableObject?[] TabCandidates(Element? incoming) => incoming switch
    {
        ShellContent content => [content, content.Parent, content.Parent?.Parent],
        ShellSection section => [section.CurrentItem, section, section.Parent],
        ShellItem item => [item.CurrentItem?.CurrentItem, item.CurrentItem, item],
        _ => []
    };


    /// <summary>The page's own type for this kind of navigation, or Default when it sets none.</summary>
    static PageTransitionType OwnType(Page? page, bool isPop, bool isModal)
    {
        if (page is null)
            return PageTransitionType.Default;

        var type = isModal ? GetModal(page) : PageTransitionType.Default;
        if (type == PageTransitionType.Default && isPop)
            type = GetPop(page);
        if (type == PageTransitionType.Default)
            type = GetPush(page);

        return type;
    }


    /// <summary>The page's type, else the app's default for the kind of navigation.</summary>
    static PageTransitionType FallbackType(PageTransitionType ownType, bool isModal)
    {
        if (ownType != PageTransitionType.Default)
            return ownType;

        if (isModal && DefaultModalTransition != PageTransitionType.Default)
            return DefaultModalTransition;

        return DefaultTransition;
    }


    /// <summary>What reduced motion turns a resolved transition into.</summary>
    internal static PageTransitionResolution ApplyReducedMotion(PageTransitionResolution resolution)
    {
        // The platform's own animation follows the platform's own reduced-motion rules, and None is
        // already as still as it gets.
        if (!resolution.IsSet || resolution.Plan.Type == PageTransitionType.None || !IsMotionReduced)
            return resolution;

        var plan = resolution.Plan;
        plan = ReducedMotion == PageTransitionReducedMotion.None
            ? plan with { Type = PageTransitionType.None }
            : plan with
            {
                Type = PageTransitionType.Fade,
                Duration = plan.Duration < ReducedMotionCrossfade ? plan.Duration : ReducedMotionCrossfade
            };

        return new PageTransitionResolution(plan, null);
    }


    static PageTransitionPlan Plan(Page? page, PageTransitionType type, bool isPop, PageTransitionDirection direction = PageTransitionDirection.Default, TimeSpan? duration = null)
    {
        if (direction == PageTransitionDirection.Default && page is not null)
            direction = GetDirection(page);
        if (direction == PageTransitionDirection.Default)
            direction = type == PageTransitionType.SharedAxisY ? PageTransitionDirection.FromBottom : PageTransitionDirection.FromRight;

        duration ??= (page is null ? null : GetDuration(page)) ?? Duration;
        return new PageTransitionPlan(type, direction, isPop, duration.Value);
    }


    /// <summary>
    /// The element a transition animates for <paramref name="page"/> on this head: the page itself,
    /// except on Linux (GTK4), whose page views ignore transforms, where it is the page's content.
    /// </summary>
    public static VisualElement? GetAnimationSurface(Page? page)
    {
        if (page is ContentPage { Content: View content } && OperatingSystem.IsLinux())
            return content;

        return page;
    }


    /// <summary>The page actually showing inside a container page (a Shell's or NavigationPage's current page).</summary>
    internal static Page? VisiblePage(Page? page)
    {
        for (var depth = 0; depth < 6; depth++)
        {
            var inner = page switch
            {
                Shell shell => shell.CurrentPage,
                FlyoutPage flyout => flyout.Detail,
                IPageContainer<Page> container => container.CurrentPage,
                _ => null
            };
            if (inner is null || ReferenceEquals(inner, page))
                return page;

            page = inner;
        }
        return page;
    }


    /// <summary>
    /// Runs one of the built-in transitions as property animations on the context's pages, the way
    /// the managed heads do. A custom <see cref="IPageTransitionAnimator"/> can use it to build on a
    /// built-in (run a <see cref="PageTransitionType.Fade"/> alongside its own motion, say).
    /// The caller resets the pages afterwards, as the host does for an animator.
    /// </summary>
    public static Task AnimateAsync(PageTransitionContext context, PageTransitionType type, CancellationToken cancellationToken = default)
    {
        var plan = new PageTransitionPlan(type, context.Direction, context.IsPop, context.Duration);
        return ManagedTransitionRunner.RunAsync(plan, context.FromSurface, context.ToSurface, context.Bounds, [], cancellationToken);
    }

    #endregion

    #region Touch origin

    /// <summary>
    /// Where the last touch went down, in window coordinates (device-independent units). The
    /// circular reveal grows from here.
    /// </summary>
    public static Point? LastTouch { get; internal set; }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Page, object> revealOrigins = new();

    /// <summary>Remembers where a page's reveal grew from, so its pop shrinks back to the same point.</summary>
    internal static void SetRevealOrigin(Page page, Point origin)
    {
        revealOrigins.Remove(page);
        revealOrigins.Add(page, origin);
    }

    internal static Point? GetRevealOrigin(Page? page)
        => page is not null && revealOrigins.TryGetValue(page, out var o) ? (Point)o : null;

    #endregion
}

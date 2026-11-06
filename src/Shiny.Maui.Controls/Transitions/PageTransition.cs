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
    /// The transition for a navigation between <paramref name="from"/> and <paramref name="to"/>.
    /// When the pages share a hero and nothing else is set, the pages crossfade so the hero has a
    /// transition to fly over.
    /// </summary>
    public static PageTransitionPlan Resolve(Page? from, Page? to, bool isPop)
    {
        var plan = isPop ? ResolvePop(from) : ResolvePush(to);
        if (plan.Type == PageTransitionType.Default && FindHeroPairs(from, to).Count > 0)
            plan = plan with { Type = PageTransitionType.Fade };

        return plan;
    }


    static PageTransitionPlan Plan(Page? page, PageTransitionType type, bool isPop)
    {
        var direction = page is null ? PageTransitionDirection.Default : GetDirection(page);
        if (direction == PageTransitionDirection.Default)
            direction = type == PageTransitionType.SharedAxisY ? PageTransitionDirection.FromBottom : PageTransitionDirection.FromRight;

        var duration = (page is null ? null : GetDuration(page)) ?? Duration;
        return new PageTransitionPlan(type, direction, isPop, duration);
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

using System.Runtime.CompilerServices;
using Android.Animation;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Views.Accessibility;
using Android.Views.Animations;
using Android.Widget;
using AndroidX.Activity;
using AndroidX.Fragment.App;
using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.Maui.Platform;
using Shiny.Maui.Controls.Infrastructure;
using AAnimation = Android.Views.Animations.Animation;
using AnimRes = Shiny.Maui.Controls.Resource.Animation;
using AView = Android.Views.View;
using AWindow = Android.Views.Window;
using Point = Microsoft.Maui.Graphics.Point;

namespace Shiny.Maui.Controls;

static partial class PageTransitionInstaller
{
    static partial void InstallPlatform(MauiAppBuilder builder)
    {
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<Shell, ShinyShellRenderer>());

        // Where the last touch went down - the circular reveal grows from it.
        builder.ConfigureLifecycleEvents(events => events.AddAndroid(android => android
            .OnPostCreate((activity, _) => TouchOriginCallback.Install(activity))));
    }
}


/// <summary>
/// MAUI's Android Shell renderer with <see cref="PageTransition"/> support: page transitions, hero
/// flights, Shell tab transitions and the Android 14 predictive back preview. Registered as the Shell
/// handler by <c>UsePageTransitions()</c>; an app that needs its own Shell renderer derives from this
/// one rather than <see cref="ShellRenderer"/> to keep them.
/// </summary>
public class ShinyShellRenderer : ShellRenderer
{
    PredictiveBackCallback? predictiveBack;

    // Not a mapper: Android's ShellRenderer only runs its mapper for property changes, never on connect.
    protected override void OnElementSet(Shell shell)
    {
        base.OnElementSet(shell);
        this.InstallPredictiveBack(shell);
    }


    protected override IShellItemRenderer CreateShellItemRenderer(ShellItem shellItem)
        => new ShinyShellItemRenderer(this);

    protected override IShellObservableFragment CreateFragmentForPage(Page page)
        => new ShinyShellContentFragment(this, page);

    protected override IShellSectionRenderer CreateShellSectionRenderer(ShellSection shellSection)
        => new ShinyShellSectionRenderer(this);


    void InstallPredictiveBack(Shell shell)
    {
        if (this.predictiveBack is not null || !OperatingSystem.IsAndroidVersionAtLeast(34))
            return;

        // The renderer's own context: the mapper runs before Shell.Handler points back at it.
        if (((IElementHandler)this).MauiContext?.Context?.GetActivity() is not ComponentActivity activity)
            return;

        TouchOriginCallback.Install(activity);
        this.predictiveBack = new PredictiveBackCallback(shell, activity);
        activity.OnBackPressedDispatcher.AddCallback(activity, this.predictiveBack);
        shell.Navigated += (_, _) => this.predictiveBack.Refresh();
        this.predictiveBack.Refresh();
    }
}


/// <summary>
/// Chooses the transition for a Shell push, pop or tab switch, and hands it to the fragments, which
/// build the animations in code (see <see cref="AndroidTransitions"/>).
/// </summary>
public class ShinyShellItemRenderer(IShellContext shellContext) : ShellItemRenderer(shellContext)
{
    ShellSection? previousSection;
    ShellSection? lastSection;
    bool sectionChanging;


    // Only a real tab change comes through here (the first display does not), which is what makes
    // it the signal to animate rather than the navigation source alone.
    protected override void OnShellSectionChanged()
    {
        this.sectionChanging = true;
        try
        {
            base.OnShellSectionChanged();
        }
        finally
        {
            this.sectionChanging = false;
        }
    }


    protected override Task<bool> HandleFragmentUpdate(ShellNavigationSource navSource, ShellSection shellSection, Page page, bool animated)
    {
        this.previousSection = this.lastSection;
        this.lastSection = shellSection;

        // MAUI switches tabs without animating; with a Shell item transition set, it is asked to.
        if (navSource == ShellNavigationSource.ShellSectionChanged &&
            this.sectionChanging &&
            PageTransition.IsEnabled &&
            PageTransition.ShellItemTransition is not (PageTransitionType.Default or PageTransitionType.None))
            animated = true;

        return base.HandleFragmentUpdate(navSource, shellSection, page, animated);
    }


    protected override void SetupAnimation(ShellNavigationSource navSource, FragmentTransaction t, Page page)
    {
        if (!PageTransition.IsEnabled)
        {
            base.SetupAnimation(navSource, t, page);
            return;
        }

        var section = this.ShellSection;
        var stack = section.Stack;
        Page? from = null, to = null;
        PageTransitionPlan plan;

        // For a push, page is the one arriving; for a pop it is the one leaving. Shell's stack has
        // already moved by now, with a null standing in for the section's root page.
        switch (navSource)
        {
            case ShellNavigationSource.Push:
                to = page;
                from = stack.Count > 2 ? stack[^2] : RootPage(section);
                plan = PageTransition.Resolve(from, to, false);
                break;

            case ShellNavigationSource.Pop:
            case ShellNavigationSource.PopToRoot:
                from = page;
                to = navSource == ShellNavigationSource.PopToRoot || stack.Count <= 1 ? RootPage(section) : stack[^1];
                plan = PageTransition.Resolve(from, to, true);
                break;

            case ShellNavigationSource.ShellSectionChanged:
                var items = section.Parent is ShellItem item ? item.Items : null;
                var forward = items is null || this.previousSection is null || items.IndexOf(section) >= items.IndexOf(this.previousSection);
                plan = new PageTransitionPlan(
                    PageTransition.ShellItemTransition,
                    forward ? PageTransitionDirection.FromRight : PageTransitionDirection.FromLeft,
                    false,
                    PageTransition.Duration
                );
                break;

            default:
                base.SetupAnimation(navSource, t, page);
                return;
        }

        if (plan.Type == PageTransitionType.NativeZoom)
            plan = plan with { Type = PageTransitionType.Zoom };

        if (plan.Type == PageTransitionType.Default)
        {
            AndroidTransitions.Current = null;
            base.SetupAnimation(navSource, t, page);
            return;
        }

        if (plan.Type == PageTransitionType.None)
        {
            // No animation at all: the fragments report finishing straight away.
            AndroidTransitions.Current = null;
            t.SetCustomAnimations(0, 0);
            return;
        }

        AndroidTransitions.Current = new AndroidTransitions.Pending(plan, from, to, PageTransition.FindHeroPairs(from, to));
        t.SetCustomAnimations(AndroidTransitions.Marker, AndroidTransitions.Marker);
    }


    static Page? RootPage(ShellSection section)
        => (section.CurrentItem as IShellContentController)?.Page;
}


/// <summary>A pushed page's fragment, animated by <see cref="AndroidTransitions"/>.</summary>
public class ShinyShellContentFragment : ShellContentFragment
{
    static readonly ConditionalWeakTable<Page, ShinyShellContentFragment> byPage = new();

    public ShinyShellContentFragment(IShellContext shellContext, Page page) : base(shellContext, page)
    {
        byPage.AddOrUpdate(page, this);
    }

    internal static AView? ViewFor(Page page)
        => byPage.TryGetValue(page, out var f) ? f.View : null;


    public override AAnimation? OnCreateAnimation(int transit, bool enter, int nextAnim)
    {
        // Asking the base for the marker animation keeps MAUI's bookkeeping (the hardware layer,
        // the "animating" flag and the listener whose end completes the navigation task); the
        // transition is then added to the set it returns, behind that listener's animation.
        var result = base.OnCreateAnimation(transit, enter, nextAnim);
        if (nextAnim == AndroidTransitions.Marker && AndroidTransitions.Current is { } pending && result is AnimationSet set && this.View is { } view)
            AndroidTransitions.Decorate(set, view, pending, enter);

        return result;
    }
}


/// <summary>A Shell section's root fragment, animated by <see cref="AndroidTransitions"/>.</summary>
public class ShinyShellSectionRenderer : ShellSectionRenderer
{
    static readonly ConditionalWeakTable<ShellSection, ShinyShellSectionRenderer> bySection = new();

    public ShinyShellSectionRenderer(IShellContext shellContext) : base(shellContext) { }

    internal static AView? ViewFor(ShellSection section)
        => bySection.TryGetValue(section, out var r) ? r.View : null;


    public override AView? OnCreateView(LayoutInflater inflater, ViewGroup? container, Android.OS.Bundle? savedInstanceState)
    {
        if (this.ShellSection is { } section)
            bySection.AddOrUpdate(section, this);

        return base.OnCreateView(inflater, container, savedInstanceState);
    }


    public override AAnimation? OnCreateAnimation(int transit, bool enter, int nextAnim)
    {
        if (nextAnim == AndroidTransitions.Marker && AndroidTransitions.Current is { } pending && this.View is { } view)
        {
            var animation = AndroidTransitions.Create(view, pending, enter);

            // On a tab switch MAUI has already hidden the outgoing tab by the time its exit
            // animation is asked for, so it would animate while invisible. It stays on screen for
            // the run and goes back to hidden when it ends.
            if (!enter && this.IsHidden && view.Visibility == ViewStates.Gone)
            {
                view.Visibility = ViewStates.Visible;
                animation.AnimationEnd += (_, _) =>
                {
                    if (this.IsHidden)
                        view.Visibility = ViewStates.Gone;
                };
            }
            return animation;
        }

        return base.OnCreateAnimation(transit, enter, nextAnim);
    }
}


/// <summary>
/// The animations, built in code so every transition type, duration and origin is available - Shell
/// only accepts fixed animation resources, and view-animation XML has no 3D rotation, no circular
/// clip and no runtime origin.
/// </summary>
static class AndroidTransitions
{
    internal sealed record Pending(
        PageTransitionPlan Plan,
        Page? From,
        Page? To,
        IReadOnlyList<(VisualElement From, VisualElement To)> Heroes
    );

    /// <summary>The transition the fragment transaction being run should animate.</summary>
    internal static Pending? Current;

    /// <summary>
    /// The resource the transaction is given so the fragments know it is ours. It is a set whose
    /// first animation holds for the run: Shell's fragment only listens for the end of a set's
    /// first animation, so it is that animation's length that completes the navigation.
    /// </summary>
    internal static int Marker => AnimRes.shiny_transition_hold;

    static readonly IInterpolator Curve = new PathInterpolator(0.4f, 0f, 0.2f, 1f);


    internal static AnimationSet Create(AView view, Pending pending, bool enter)
    {
        var set = new AnimationSet(false);
        set.AddAnimation(new AlphaAnimation(1, 1));
        Decorate(set, view, pending, enter);
        return set;
    }


    internal static void Decorate(AnimationSet set, AView view, Pending pending, bool enter)
    {
        var plan = pending.Plan;
        var ms = (long)plan.Duration.TotalMilliseconds;
        if (set.Animations is { Count: > 0 } existing)
            existing[0].Duration = ms;

        var isPop = plan.IsPop;
        var (vx, vy) = plan.Vector;
        var density = view.Resources?.DisplayMetrics?.Density ?? 1f;

        // FillEnabled has to be on for FillBefore/FillAfter to mean anything: without it Android
        // clamps every animation to its end values outside its window, so a "hidden for the first
        // half" step stays hidden for the whole run.
        void Add(AAnimation a, double start = 0, double length = 1, bool fillAfter = true, bool fillBefore = true)
        {
            a.StartOffset = (long)(ms * start);
            a.Duration = Math.Max(1, (long)(ms * length));
            a.Interpolator = Curve;
            a.FillEnabled = true;
            a.FillAfter = fillAfter;
            a.FillBefore = fillBefore;
            set.AddAnimation(a);
        }
        static TranslateAnimation Tr(float x0, float x1, float y0, float y1)
            => new(Dimension.RelativeToSelf, x0, Dimension.RelativeToSelf, x1, Dimension.RelativeToSelf, y0, Dimension.RelativeToSelf, y1);
        static TranslateAnimation TrPx(float x0, float x1, float y0, float y1)
            => new(Dimension.Absolute, x0, Dimension.Absolute, x1, Dimension.Absolute, y0, Dimension.Absolute, y1);
        static ScaleAnimation Sc(float s0, float s1)
            => new(s0, s1, s0, s1, Dimension.RelativeToSelf, 0.5f, Dimension.RelativeToSelf, 0.5f);
        static AlphaAnimation Al(float a0, float a1) => new(a0, a1);

        var incoming = enter;
        switch (plan.Type)
        {
            case PageTransitionType.Fade:
                if (isPop && !incoming)
                    Add(Al(1, 0));
                else if (!isPop && incoming)
                    Add(Al(0, 1));
                break;

            case PageTransitionType.FadeThrough:
                if (incoming)
                {
                    Add(Al(0, 1), 0.35, 0.65);
                    Add(Sc(0.92f, 1), 0.35, 0.65);
                }
                else
                    Add(Al(1, 0), 0, 0.35);
                break;

            case PageTransitionType.Slide:
            case PageTransitionType.Push:
                var share = plan.Type == PageTransitionType.Push ? 1f : 0.3f;
                if (!isPop)
                    Add(incoming ? Tr(vx, 0, vy, 0) : Tr(0, -vx * share, 0, -vy * share));
                else
                    Add(incoming ? Tr(-vx * share, 0, -vy * share, 0) : Tr(0, vx, 0, vy));
                break;

            case PageTransitionType.Cover:
                if (!isPop && incoming)
                    Add(Tr(vx, 0, vy, 0));
                else if (isPop && !incoming)
                    Add(Tr(0, vx, 0, vy));
                break;

            case PageTransitionType.Reveal:
                if (!isPop && !incoming)
                {
                    // The outgoing page has to stay above the incoming one while it moves away;
                    // both are ordinary children here, so Z decides the drawing order.
                    Add(Tr(0, -vx, 0, -vy));
                    RaiseFor(view, ms);
                }
                else if (isPop && incoming)
                    Add(Tr(-vx, 0, -vy, 0));
                else if (isPop && !incoming)
                {
                    // A view being removed is always drawn last, whatever its Z, so the outgoing
                    // page cannot sit underneath. It goes invisible and its picture becomes the
                    // container's background (which MAUI clears when the navigation completes).
                    if (view.Parent is AView container && Snapshot(view) is { } picture)
                        container.Background = new BitmapDrawable(view.Resources, picture);
                    Add(Al(0, 0));
                }
                break;

            case PageTransitionType.SharedAxisX:
            case PageTransitionType.SharedAxisY:
                var d = 30 * density * (isPop ? -1 : 1);
                float ax, ay;
                if (plan.Type == PageTransitionType.SharedAxisX)
                    (ax, ay) = (plan.IsVertical ? d : vx * d, 0);
                else
                    (ax, ay) = (0, plan.IsVertical ? vy * d : d);

                if (incoming)
                {
                    Add(TrPx(ax, 0, ay, 0));
                    Add(Al(0, 1), 0.3, 0.7);
                }
                else
                {
                    Add(TrPx(0, -ax, 0, -ay));
                    Add(Al(1, 0), 0, 0.3);
                }
                break;

            case PageTransitionType.SharedAxisZ:
                if (incoming)
                {
                    Add(Sc(isPop ? 1.1f : 0.8f, 1));
                    Add(Al(0, 1), 0.3, 0.7);
                }
                else
                {
                    Add(Sc(1, isPop ? 0.8f : 1.1f));
                    Add(Al(1, 0), 0, 0.3);
                }
                break;

            case PageTransitionType.Zoom:
                if (!isPop && incoming)
                {
                    Add(Sc(0.85f, 1));
                    Add(Al(0, 1));
                }
                else if (isPop && !incoming)
                {
                    Add(Sc(1, 0.85f));
                    Add(Al(1, 0));
                }
                break;

            case PageTransitionType.CircularReveal:
                if (!isPop && incoming)
                    CircularReveal(view, pending, ms, grow: true);
                else if (isPop && !incoming)
                {
                    CircularReveal(view, pending, ms, grow: false);
                    // Gone at the last moment from inside the animation: a view whose animation is
                    // still running is drawn even when invisible, so hiding it from the reveal's
                    // end callback leaves one frame of the whole page.
                    Add(Al(0, 0), 0.97, 0.03, fillBefore: false);
                }
                break;

            case PageTransitionType.Flip:
                var horizontal = !plan.IsVertical;
                var turn = 90f * (horizontal ? vx : -vy) * (isPop ? -1 : 1);
                if (incoming)
                {
                    Add(new FlipAnimation(-turn, 0, horizontal), 0.5, 0.5);
                    // Hidden for the first half (an animation that does not fill after, so it stops
                    // applying once the second half starts).
                    Add(Al(0, 0), 0, 0.5, fillAfter: false);
                }
                else
                {
                    Add(new FlipAnimation(0, turn, horizontal), 0, 0.5);
                    Add(Al(0, 0), 0.5, 0.5, fillBefore: false);
                }
                break;
        }

        // MAUI paints the navigation container black for the length of a Shell animation, which
        // is what shows wherever neither page covers it - a fade through goes through black. The
        // page's own surface is what belongs there (MAUI clears it again when the navigation ends).
        if (incoming && plan.Type != PageTransitionType.Reveal && view.Parent is AView host)
            host.SetBackgroundColor(Surface(pending));

        if (incoming && pending.Heroes.Count > 0)
            HeroFlights.Start(view, pending.Heroes, ms);
    }


    static Android.Graphics.Color Surface(Pending pending)
    {
        // The page's own colour, else the theme's background, else whatever the window shows.
        var color = pending.To?.BackgroundColor ?? pending.From?.BackgroundColor;
        if (color is null && Application.Current?.Resources.TryGetValue(Shiny.Maui.Controls.Themes.ShinyThemeKeys.Color.Background, out var value) == true && value is Microsoft.Maui.Graphics.Color themed)
            color = themed;

        return (color ?? Colors.Transparent).ToPlatform();
    }


    static void RaiseFor(AView view, long ms)
    {
        view.TranslationZ = 100;
        view.PostDelayed(() => view.TranslationZ = 0, ms + 50);
    }


    static void CircularReveal(AView view, Pending pending, long ms, bool grow)
    {
        void Run()
        {
            var w = view.Width;
            var h = view.Height;
            if (w == 0 || h == 0)
                return;

            var density = view.Resources?.DisplayMetrics?.Density ?? 1f;
            var location = new int[2];
            view.GetLocationInWindow(location);

            Point? origin = grow ? PageTransition.LastTouch : PageTransition.GetRevealOrigin(pending.From);
            if (grow && origin is { } touch && pending.To is { } page)
                PageTransition.SetRevealOrigin(page, touch);

            var cx = origin is { } o ? (int)(o.X * density) - location[0] : w / 2;
            var cy = origin is { } p ? (int)(p.Y * density) - location[1] : h / 2;
            var radius = (float)Math.Sqrt(Math.Pow(Math.Max(cx, w - cx), 2) + Math.Pow(Math.Max(cy, h - cy), 2));

            var animator = ViewAnimationUtils.CreateCircularReveal(view, cx, cy, grow ? 0 : radius, grow ? radius : 0)!;
            animator.SetDuration(ms);
            animator.SetInterpolator(Curve);
            animator.Start();
        }

        if (grow && view.Width == 0)
        {
            // A new page is not laid out yet; start just before its first frame is drawn.
            EventHandler<ViewTreeObserver.PreDrawEventArgs>? handler = null;
            handler = (_, e) =>
            {
                view.ViewTreeObserver!.PreDraw -= handler;
                Run();
                e.Handled = true;
            };
            view.ViewTreeObserver!.PreDraw += handler;
        }
        else
        {
            Run();
        }
    }


    internal static Bitmap? Snapshot(AView view)
    {
        if (view.Width <= 0 || view.Height <= 0)
            return null;

        var bitmap = Bitmap.CreateBitmap(view.Width, view.Height, Bitmap.Config.Argb8888!);
        view.Draw(new Canvas(bitmap));
        return bitmap;
    }


    /// <summary>A view-animation 3D turn: android.graphics.Camera rotated about Y (or X).</summary>
    sealed class FlipAnimation(float from, float to, bool aboutY) : AAnimation
    {
        int cx, cy;

        public override void Initialize(int width, int height, int parentWidth, int parentHeight)
        {
            base.Initialize(width, height, parentWidth, parentHeight);
            this.cx = width / 2;
            this.cy = height / 2;
        }

        protected override void ApplyTransformation(float interpolatedTime, Transformation? t)
        {
            if (t?.Matrix is not { } matrix)
                return;

            var degrees = from + (to - from) * interpolatedTime;
            var camera = new Android.Graphics.Camera();
            camera.Save();
            camera.SetLocation(0, 0, -24);
            if (aboutY)
                camera.RotateY(degrees);
            else
                camera.RotateX(degrees);
            camera.GetMatrix(matrix);
            camera.Restore();
            matrix.PreTranslate(-this.cx, -this.cy);
            matrix.PostTranslate(this.cx, this.cy);
        }
    }
}


/// <summary>
/// Shared elements on Android: pictures of the view on both pages, flown in the window's overlay
/// from where it was to where it lands while the real views are hidden.
/// </summary>
static class HeroFlights
{
    internal static ViewGroupOverlay Overlay(ViewGroup group) => Android.Runtime.Extensions.JavaCast<ViewGroupOverlay>(group.Overlay!);

    static readonly IInterpolator Curve = new PathInterpolator(0.4f, 0f, 0.2f, 1f);

    static AView? PlatformView(VisualElement element)
        => element.Handler is IViewHandler handler ? (handler.ContainerView ?? handler.PlatformView) as AView : null;


    internal static void Start(AView incomingView, IReadOnlyList<(VisualElement From, VisualElement To)> heroes, long ms)
    {
        if (incomingView.Context?.GetActivity()?.Window?.DecorView is not ViewGroup decor)
            return;

        var flights = new List<(AView Source, VisualElement To, ImageView Picture, int[] Start, float SourceAlpha)>();
        foreach (var (from, to) in heroes)
        {
            if (PlatformView(from) is not { Width: > 0 } source || AndroidTransitions.Snapshot(source) is not { } bitmap)
                continue;

            var start = new int[2];
            source.GetLocationInWindow(start);
            var picture = Picture(decor, bitmap, start[0], start[1], source.Width, source.Height);
            flights.Add((source, to, picture, start, source.Alpha));
            source.Alpha = 0;
        }

        if (flights.Count == 0)
            return;

        // The incoming page is not laid out yet: measure the landing spots just before it draws.
        EventHandler<ViewTreeObserver.PreDrawEventArgs>? handler = null;
        handler = (_, e) =>
        {
            incomingView.ViewTreeObserver!.PreDraw -= handler;
            e.Handled = true;

            foreach (var (source, to, picture, start, sourceAlpha) in flights)
            {
                var target = PlatformView(to);
                if (target is not { Width: > 0 })
                {
                    HeroFlights.Overlay(decor).Remove(picture);
                    source.Alpha = sourceAlpha;
                    continue;
                }

                var end = new int[2];
                target.GetLocationInWindow(end);
                var targetAlpha = target.Alpha;
                var landing = AndroidTransitions.Snapshot(target) is { } b
                    ? Picture(decor, b, start[0], start[1], source.Width, source.Height)
                    : null;
                target.Alpha = 0;

                var sx = target.Width / (float)source.Width;
                var sy = target.Height / (float)source.Height;
                foreach (var p in new[] { picture, landing })
                {
                    if (p is null)
                        continue;
                    p.PivotX = 0;
                    p.PivotY = 0;
                }

                if (landing is not null)
                {
                    landing.Alpha = 0;
                    landing.Animate()!.TranslationX(end[0] - start[0]).TranslationY(end[1] - start[1]).ScaleX(sx).ScaleY(sy).Alpha(1)
                        .SetDuration(ms).SetInterpolator(Curve).Start();
                }

                picture.Animate()!.TranslationX(end[0] - start[0]).TranslationY(end[1] - start[1]).ScaleX(sx).ScaleY(sy).Alpha(0)
                    .SetDuration(ms).SetInterpolator(Curve)
                    .WithEndAction(new Java.Lang.Runnable(() =>
                    {
                        HeroFlights.Overlay(decor).Remove(picture);
                        if (landing is not null)
                            HeroFlights.Overlay(decor).Remove(landing);
                        source.Alpha = sourceAlpha;
                        target.Alpha = targetAlpha;
                    }))!
                    .Start();
            }
        };
        incomingView.ViewTreeObserver!.PreDraw += handler;
    }


    static ImageView Picture(ViewGroup decor, Bitmap bitmap, int x, int y, int w, int h)
    {
        var picture = new ImageView(decor.Context);
        picture.SetImageBitmap(bitmap);
        picture.SetScaleType(ImageView.ScaleType.FitXy!);
        picture.Measure(
            AView.MeasureSpec.MakeMeasureSpec(w, MeasureSpecMode.Exactly),
            AView.MeasureSpec.MakeMeasureSpec(h, MeasureSpecMode.Exactly));
        picture.Layout(x, y, x + w, y + h);
        HeroFlights.Overlay(decor).Add(picture);
        return picture;
    }
}


/// <summary>
/// The Android 14 predictive back preview for Shell: while the back gesture is held, the page
/// shrinks and follows the finger with the page underneath showing; letting go pops it, and
/// cancelling puts it back.
/// </summary>
/// <remarks>
/// Progress events need <c>android:enableOnBackInvokedCallback="true"</c> on the application in
/// the manifest; without it the system only ever reports the final press, which is handled like
/// any other back press.
/// </remarks>
sealed class PredictiveBackCallback(Shell shell, ComponentActivity activity) : OnBackPressedCallback(false)
{
    readonly WeakReference<Shell> shellRef = new(shell);
    AView? outgoing;
    AView? incoming;
    Page? outgoingPage;
    int edge;


    internal void Refresh()
    {
        var wasEnabled = this.Enabled;
        this.Enabled = PageTransition.IsEnabled &&
            PageTransition.PredictiveBack &&
            this.shellRef.TryGetTarget(out var s) &&
            s.Navigation.ModalStack.Count == 0 &&
            (s.CurrentItem?.CurrentItem?.Stack.Count ?? 0) > 1;
        // The dispatcher hands a back gesture to the most recently added enabled callback, and other
        // parts of the app (fragments, MAUI itself) add theirs after this one. Re-adding on every
        // switch-on keeps this one on top for exactly as long as it has a page to pop.
        if (this.Enabled && !wasEnabled)
        {
            this.Remove();
            activity.OnBackPressedDispatcher.AddCallback(activity, this);
        }
    }


    public override void HandleOnBackStarted(BackEventCompat backEvent)
    {
        this.Reset();
        if (AndroidBackButton.HasActive)
            return;
        if (!this.shellRef.TryGetTarget(out var s))
            return;

        var section = s.CurrentItem?.CurrentItem;
        var stack = section?.Stack;
        if (section is null || stack is null || stack.Count < 2 || stack[^1] is not { } top)
            return;

        var below = stack.Count > 2 ? stack[^2] : null;
        var outView = ShinyShellContentFragment.ViewFor(top);
        var inView = below is not null ? ShinyShellContentFragment.ViewFor(below) : ShinyShellSectionRenderer.ViewFor(section);
        if (outView is null || inView is null)
            return;

        this.outgoingPage = top;
        this.outgoing = outView;
        this.incoming = inView;
        this.edge = backEvent.SwipeEdge;

        // The page underneath is a hidden fragment; it shows for the length of the gesture.
        inView.Visibility = ViewStates.Visible;
        outView.PivotX = outView.Width * (this.edge == 0 ? 0.85f : 0.15f);
        outView.PivotY = outView.Height / 2f;
        this.Apply(0);
    }


    public override void HandleOnBackProgressed(BackEventCompat backEvent) => this.Apply(backEvent.Progress);


    public override void HandleOnBackCancelled()
    {
        if (this.outgoing is not { } outView)
            return;

        var inView = this.incoming;
        outView.Animate()!.ScaleX(1).ScaleY(1).TranslationX(0).SetDuration(180)
            .WithEndAction(new Java.Lang.Runnable(() =>
            {
                if (inView is not null)
                    inView.Visibility = ViewStates.Gone;
            }))!
            .Start();
        if (inView is not null)
            inView.Animate()!.TranslationX(0).SetDuration(180).Start();

        this.outgoing = null;
        this.incoming = null;
    }


    public override void HandleOnBackPressed()
    {
        if (!this.shellRef.TryGetTarget(out var s))
            return;

        var outView = this.outgoing;
        var inView = this.incoming;
        var page = this.outgoingPage;

        // An overlay that owns the back button, or a page that refuses the pop, gets the press as
        // usual and the preview goes back.
        if (outView is null || inView is null || page is null || AndroidBackButton.HasActive || page.SendBackButtonPressed())
        {
            this.HandleOnBackCancelled();
            if (AndroidBackButton.HasActive)
            {
                AndroidBackButton.HandleBack();
                return;
            }
            if (outView is null)
                this.PassThrough();
            return;
        }

        // The preview already did most of the move: the rest is a picture of the outgoing page
        // finishing it in the overlay while the real pop runs without its own animation.
        var decor = activity.Window?.DecorView as ViewGroup;
        ImageView? picture = null;
        if (decor is not null && AndroidTransitions.Snapshot(outView) is { } bitmap)
        {
            var at = new int[2];
            outView.GetLocationInWindow(at);
            picture = new ImageView(activity);
            picture.SetImageBitmap(bitmap);
            picture.Measure(
                AView.MeasureSpec.MakeMeasureSpec(outView.Width, MeasureSpecMode.Exactly),
                AView.MeasureSpec.MakeMeasureSpec(outView.Height, MeasureSpecMode.Exactly));
            // The window location already includes the preview's translation; lay out at the
            // untransformed spot and carry the transform over.
            var left = at[0] - (int)outView.TranslationX;
            picture.Layout(left, at[1], left + outView.Width, at[1] + outView.Height);
            picture.PivotX = outView.PivotX;
            picture.PivotY = outView.PivotY;
            picture.ScaleX = outView.ScaleX;
            picture.ScaleY = outView.ScaleY;
            picture.TranslationX = outView.TranslationX;
            HeroFlights.Overlay(decor).Add(picture);
        }

        outView.ScaleX = 1;
        outView.ScaleY = 1;
        outView.TranslationX = 0;
        inView.TranslationX = 0;
        this.outgoing = null;
        this.incoming = null;

        _ = s.Navigation.PopAsync(false);

        if (picture is not null && decor is not null)
        {
            var dir = this.edge == 0 ? 1 : -1;
            picture.Animate()!.TranslationX(picture.TranslationX + dir * outView.Width * 0.25f).Alpha(0).SetDuration(160)
                .WithEndAction(new Java.Lang.Runnable(() => HeroFlights.Overlay(decor).Remove(picture)))!
                .Start();
        }
    }


    void Apply(float progress)
    {
        if (this.outgoing is not { } outView)
            return;

        // Material's "back to previous screen": the page shrinks toward 90% and drifts with the
        // gesture, the page underneath eases in from a little behind.
        var dir = this.edge == 0 ? 1 : -1;
        var scale = 1f - 0.1f * progress;
        outView.ScaleX = scale;
        outView.ScaleY = scale;
        outView.TranslationX = dir * progress * outView.Width * 0.06f;
        if (this.incoming is { } inView)
            inView.TranslationX = -dir * (1 - progress) * inView.Width * 0.08f;
    }


    void Reset()
    {
        if (this.outgoing is { } o)
        {
            o.ScaleX = 1;
            o.ScaleY = 1;
            o.TranslationX = 0;
        }
        if (this.incoming is { } i)
            i.TranslationX = 0;
        this.outgoing = null;
        this.incoming = null;
        this.outgoingPage = null;
    }


    /// <summary>Hands a press this callback cannot use to the next one down (MAUI's own).</summary>
    void PassThrough()
    {
        this.Enabled = false;
        try
        {
            activity.OnBackPressedDispatcher.OnBackPressed();
        }
        finally
        {
            this.Refresh();
        }
    }
}


/// <summary>
/// Sits in front of the activity's window callback to note where each touch goes down, and passes
/// everything through untouched.
/// </summary>
#pragma warning disable CA1416 // forwarded as-is: the window only calls what its API level has
sealed class TouchOriginCallback : Java.Lang.Object, AWindow.ICallback
{
    readonly AWindow.ICallback inner;
    readonly float density;

    TouchOriginCallback(AWindow.ICallback inner, float density)
    {
        this.inner = inner;
        this.density = density;
    }


    internal static void Install(Android.App.Activity activity)
    {
        if (activity.Window is not { } window || window.Callback is null or TouchOriginCallback)
            return;

        window.Callback = new TouchOriginCallback(window.Callback, activity.Resources?.DisplayMetrics?.Density ?? 1f);
    }


    public bool DispatchTouchEvent(MotionEvent? e)
    {
        if (e is { ActionMasked: MotionEventActions.Down })
            PageTransition.LastTouch = new Point(e.RawX / this.density, e.RawY / this.density);

        return this.inner.DispatchTouchEvent(e);
    }

    public bool DispatchGenericMotionEvent(MotionEvent? e) => this.inner.DispatchGenericMotionEvent(e);
    public bool DispatchKeyEvent(KeyEvent? e) => this.inner.DispatchKeyEvent(e);
    public bool DispatchKeyShortcutEvent(KeyEvent? e) => this.inner.DispatchKeyShortcutEvent(e);
    public bool DispatchPopulateAccessibilityEvent(AccessibilityEvent? e) => this.inner.DispatchPopulateAccessibilityEvent(e);
    public bool DispatchTrackballEvent(MotionEvent? e) => this.inner.DispatchTrackballEvent(e);
    public void OnActionModeFinished(ActionMode? mode) => this.inner.OnActionModeFinished(mode);
    public void OnActionModeStarted(ActionMode? mode) => this.inner.OnActionModeStarted(mode);
    public void OnAttachedToWindow() => this.inner.OnAttachedToWindow();
    public void OnContentChanged() => this.inner.OnContentChanged();
    public bool OnCreatePanelMenu(int featureId, IMenu menu) => this.inner.OnCreatePanelMenu(featureId, menu);
    public AView? OnCreatePanelView(int featureId) => this.inner.OnCreatePanelView(featureId);
    public void OnDetachedFromWindow() => this.inner.OnDetachedFromWindow();
    public bool OnMenuItemSelected(int featureId, IMenuItem item) => this.inner.OnMenuItemSelected(featureId, item);
    public bool OnMenuOpened(int featureId, IMenu menu) => this.inner.OnMenuOpened(featureId, menu);
    public void OnPanelClosed(int featureId, IMenu menu) => this.inner.OnPanelClosed(featureId, menu);
    public void OnPointerCaptureChanged(bool hasCapture) => this.inner.OnPointerCaptureChanged(hasCapture);
    public bool OnPreparePanel(int featureId, AView? view, IMenu menu) => this.inner.OnPreparePanel(featureId, view, menu);
    public void OnProvideKeyboardShortcuts(IList<KeyboardShortcutGroup>? data, IMenu? menu, int deviceId) => this.inner.OnProvideKeyboardShortcuts(data, menu, deviceId);
    public bool OnSearchRequested() => this.inner.OnSearchRequested();
    public bool OnSearchRequested(SearchEvent? searchEvent) => this.inner.OnSearchRequested(searchEvent);
    public void OnWindowAttributesChanged(WindowManagerLayoutParams? attrs) => this.inner.OnWindowAttributesChanged(attrs);
    public void OnWindowFocusChanged(bool hasFocus) => this.inner.OnWindowFocusChanged(hasFocus);
    public ActionMode? OnWindowStartingActionMode(ActionMode.ICallback? callback) => this.inner.OnWindowStartingActionMode(callback);
    public ActionMode? OnWindowStartingActionMode(ActionMode.ICallback? callback, ActionModeType type) => this.inner.OnWindowStartingActionMode(callback, type);
}
#pragma warning restore CA1416

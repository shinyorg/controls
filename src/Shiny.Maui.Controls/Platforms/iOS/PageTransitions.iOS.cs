using System.Runtime.CompilerServices;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;
using iOSPage = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page;
using iOSModalStyle = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.UIModalPresentationStyle;

namespace Shiny.Maui.Controls;

static partial class PageTransitionInstaller
{
    static readonly ConditionalWeakTable<Shell, object> hookedShells = new();
    static readonly ConditionalWeakTable<UIWindow, object> hookedWindows = new();
    internal static readonly ModalTransitionDelegate ModalDelegate = new();

    static partial void InstallPlatform(MauiAppBuilder builder)
    {
        // The Shell handler is replaced for the one thing a mapper cannot reach: the transition
        // between Shell items (flyout items), which MAUI asks the renderer for.
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<Shell, ShinyShellRenderer>());

        // Shell: each ShellSection is its own UINavigationController (ShellSectionRenderer). They are
        // created lazily as sections are first shown, so they are picked up on every navigation
        // rather than once. Navigated catches the section that just appeared (so the first push on it
        // is already covered); Navigating catches one that appears with the navigation itself.
        ShellRenderer.Mapper.AppendToMapping("ShinyPageTransitions", (renderer, shell) =>
        {
            if (hookedShells.TryGetValue(shell, out _))
                return;

            hookedShells.Add(shell, new object());
            shell.Navigating += (s, _) => AttachShell((Shell)s!);
            shell.Navigated += (s, _) => AttachShell((Shell)s!);
            shell.Dispatcher?.Dispatch(() => AttachShell(shell));
        });

        // NavigationPage (ShinyNavigationPage included) is a single UINavigationController - and,
        // presented modally, also the controller a modal transition applies to.
        NavigationRenderer.Mapper.AppendToMapping("ShinyPageTransitions", (handler, page) =>
        {
            TransitionNavigationDelegate.Attach(handler);
            PrepareModal(page, handler);
        });

        PageHandler.Mapper.AppendToMapping("ShinyPageTransitions", (handler, view) =>
        {
            if (view is Page page && (handler as IPlatformViewHandler)?.ViewController is { } vc)
            {
                PrepareModal(page, vc);
                PrepareNativeZoom(page, vc);
            }
        });

        // Where the last touch went down - the circular reveal grows from it.
        WindowHandler.Mapper.AppendToMapping("ShinyPageTransitions", (handler, window) =>
        {
            if (handler.PlatformView is UIWindow uiWindow && !hookedWindows.TryGetValue(uiWindow, out _))
            {
                hookedWindows.Add(uiWindow, new object());
                uiWindow.AddGestureRecognizer(new TouchOriginRecognizer());
            }
        });
    }


    /// <summary>
    /// A page with a transition, presented modally, is shown full screen over its presenter (so
    /// fades and zooms show the page underneath) and animated by <see cref="ModalTransitionDelegate"/>.
    /// MAUI's modal wrapper copies both from the page's controller as it is created.
    /// </summary>
    static void PrepareModal(Page page, UIViewController vc)
    {
        var push = PageTransition.ResolvePush(page);
        if (!push.IsAnimated && !PageTransition.ResolvePop(page).IsAnimated)
            return;

        vc.TransitioningDelegate = ModalDelegate;
        if (!page.IsSet(iOSPage.ModalPresentationStyleProperty))
            page.SetValue(iOSPage.ModalPresentationStyleProperty, iOSModalStyle.OverFullScreen);
    }


    /// <summary>
    /// iOS 18's zoom transition is a property of the controller being pushed, so it has to be in
    /// place before the push. Shell pushes the page's own controller; NavigationPage wraps it in
    /// one of its own, so there <see cref="PageTransitionType.NativeZoom"/> falls back to ours.
    /// </summary>
    static void PrepareNativeZoom(Page page, UIViewController vc)
    {
        if (!OperatingSystem.IsIOSVersionAtLeast(18) || PageTransition.ResolvePush(page).Type != PageTransitionType.NativeZoom)
            return;

        var target = new WeakReference<Page>(page);
        vc.PreferredTransition = UIViewControllerTransition.Zoom(new UIZoomTransitionOptions(), context =>
        {
            if (!OperatingSystem.IsIOSVersionAtLeast(18) || !target.TryGetTarget(out var zoomed))
                return null!;

            var source = TransitionNavigationDelegate.FindPage(context.SourceViewController, 0);
            var pair = PageTransition.FindHeroPairs(source, zoomed).FirstOrDefault();
            return (pair.From?.Handler?.PlatformView as UIView)!;
        });
    }


    static void AttachShell(Shell shell)
    {
        if ((shell.Handler as IPlatformViewHandler)?.ViewController is { } root)
            AttachSections(root, 0);
    }


    static void AttachSections(UIViewController vc, int depth)
    {
        if (depth > 6)
            return;

        if (vc is ShellSectionRenderer section)
        {
            TransitionNavigationDelegate.Attach(section);
            return;
        }

        foreach (var child in vc.ChildViewControllers)
            AttachSections(child, depth + 1);
    }
}


/// <summary>
/// Records where each touch goes down without taking part in gesture recognition: it fails at once,
/// so nothing it is attached to ever loses a touch to it.
/// </summary>
sealed class TouchOriginRecognizer : UIGestureRecognizer
{
    public TouchOriginRecognizer()
    {
        this.CancelsTouchesInView = false;
        this.DelaysTouchesBegan = false;
        this.DelaysTouchesEnded = false;
    }

    public override void TouchesBegan(NSSet touches, UIEvent evt)
    {
        base.TouchesBegan(touches, evt);
        if (touches.AnyObject is UITouch touch && this.View is { } view)
        {
            var p = touch.LocationInView(view);
            PageTransition.LastTouch = new Point(p.X, p.Y);
        }
        this.State = UIGestureRecognizerState.Failed;
    }

    public override bool CanPreventGestureRecognizer(UIGestureRecognizer preventedGestureRecognizer) => false;
    public override bool CanBePreventedByGestureRecognizer(UIGestureRecognizer preventingGestureRecognizer) => false;
}


/// <summary>
/// The Shell renderer with <see cref="PageTransition"/> support for switching Shell items (flyout
/// items) and tabs. Registered as the Shell handler by <c>UsePageTransitions()</c>; an app that
/// needs its own Shell renderer derives from this one to keep transitions.
/// </summary>
public class ShinyShellRenderer : ShellRenderer
{
    protected override IShellItemTransition CreateShellItemTransition()
        => PageTransition.ShellItemTransition is PageTransitionType.Default
            ? base.CreateShellItemTransition()
            : new ShinyShellItemTransition();


    protected override IShellItemRenderer CreateShellItemRenderer(ShellItem item)
    {
        var renderer = base.CreateShellItemRenderer(item);
        if (renderer is UITabBarController tabs)
        {
            // A delegate *property*, so it shares the delegate ShellItemRenderer already uses for
            // ShouldSelectViewController rather than replacing it.
            tabs.GetAnimationControllerForTransition = (controller, from, to) =>
            {
                if (!PageTransition.IsEnabled || PageTransition.ShellItemTransition is PageTransitionType.Default or PageTransitionType.None)
                    return null!;

                var vcs = controller.ViewControllers ?? [];
                var forward = Array.IndexOf(vcs, to) >= Array.IndexOf(vcs, from);
                var plan = new PageTransitionPlan(
                    PageTransition.ShellItemTransition,
                    forward ? PageTransitionDirection.FromRight : PageTransitionDirection.FromLeft,
                    false,
                    PageTransition.Duration
                );
                return new PageTransitionAnimator(plan, false, null, null);
            };
        }
        return renderer;
    }
}


/// <summary>Animates the switch between two Shell items (flyout items).</summary>
sealed class ShinyShellItemTransition : IShellItemTransition
{
    public Task Transition(IShellItemRenderer oldRenderer, IShellItemRenderer newRenderer)
    {
        var tcs = new TaskCompletionSource<bool>();
        var oldView = oldRenderer.ViewController.View!;
        var newView = newRenderer.ViewController.View!;
        var container = oldView.Superview!;
        oldView.Layer.RemoveAllAnimations();

        var plan = new PageTransitionPlan(PageTransition.ShellItemTransition, PageTransitionDirection.FromRight, false, PageTransition.Duration);

        // iOS 26+: a tab bar inside a view whose alpha starts at 0 composites its Liquid Glass
        // icons at zero opacity and they never come back (why MAUI's own crossfade is a no-op
        // there). The incoming item is therefore never faded - the outgoing one is, from a snapshot
        // laid over it, which is the same picture.
        var fromView = oldView.SnapshotView(false) ?? oldView;
        if (fromView != oldView)
        {
            fromView.Frame = oldView.Frame;
            container.InsertSubviewAbove(newView, oldView);
            container.InsertSubviewAbove(fromView, newView);
        }
        else
        {
            container.InsertSubviewAbove(newView, oldView);
        }

        if (OperatingSystem.IsIOSVersionAtLeast(26) && TransitionRunner.FadesIncoming(plan.Type))
            plan = plan with { Type = PageTransitionType.Fade, IsPop = true };

        TransitionRunner.Run(container, fromView, newView, plan, false, [], null, () => false, _ =>
        {
            if (fromView != oldView)
                fromView.RemoveFromSuperview();
            tcs.TrySetResult(true);
        }, insertIncoming: false);

        return tcs.Task;
    }
}


/// <summary>
/// Wraps MAUI's own navigation delegate: everything it does still happens (its WillShow/DidShow
/// drive the nav bar, back-button titles and the push/pop completion tasks), and the one question it
/// always answers with "the system's" - which animator to use - is answered from the page instead.
/// </summary>
sealed class TransitionNavigationDelegate : UINavigationControllerDelegate
{
    readonly UINavigationControllerDelegate? inner;
    readonly WeakReference<UINavigationController> navigation;
    UIPercentDrivenInteractiveTransition? interaction;
    UIPanGestureRecognizer? drivingPan;
    UIPanGestureRecognizer? fullScreenPan;
    bool fullScreenPopPending;


    TransitionNavigationDelegate(UINavigationController navigation, UINavigationControllerDelegate? inner)
    {
        this.navigation = new(navigation);
        this.inner = inner;
    }


    internal static void Attach(UINavigationController navigation)
    {
        if (navigation.Delegate is TransitionNavigationDelegate)
            return;

        var wrapper = new TransitionNavigationDelegate(navigation, navigation.Delegate as UINavigationControllerDelegate);
        navigation.Delegate = wrapper;

        // Swipe-back. Returning our own animator switches UIKit's built-in interactive pop off, so
        // the edge pan's progress is fed to an interaction controller of ours instead. Listening to
        // the system recognizer (rather than adding a second one) keeps whatever ShouldBegin rules
        // MAUI / ShinyNavigationPage already put on it.
        if (navigation.InteractivePopGestureRecognizer is UIPanGestureRecognizer edge)
            edge.AddTarget(() => wrapper.OnPopPan(edge));

        if (PageTransition.FullScreenSwipeBack && navigation.View is { } view)
        {
            var pan = new UIPanGestureRecognizer();
            pan.AddTarget(() => wrapper.OnFullScreenPan(pan));
            pan.Delegate = new FullScreenPopDelegate(navigation);
            view.AddGestureRecognizer(pan);
        }
    }


    public override void WillShowViewController(UINavigationController navigationController, UIViewController viewController, bool animated)
        => this.inner?.WillShowViewController(navigationController, viewController, animated);


    public override void DidShowViewController(UINavigationController navigationController, UIViewController viewController, bool animated)
        => this.inner?.DidShowViewController(navigationController, viewController, animated);


    public override IUIViewControllerAnimatedTransitioning GetAnimationControllerForOperation(
        UINavigationController navigationController,
        UINavigationControllerOperation operation,
        UIViewController fromViewController,
        UIViewController toViewController
    )
    {
        var fullScreen = this.fullScreenPopPending;
        this.fullScreenPopPending = false;
        this.interaction = null;
        this.drivingPan = null;

        if (!PageTransition.IsEnabled || operation is not (UINavigationControllerOperation.Push or UINavigationControllerOperation.Pop))
            return null!;

        var isPop = operation == UINavigationControllerOperation.Pop;
        var fromPage = FindPage(fromViewController, 0);
        var toPage = FindPage(toViewController, 0);
        var plan = PageTransition.Resolve(fromPage, toPage, isPop);

        if (plan.Type == PageTransitionType.NativeZoom)
        {
            // Handled by UIKit when the pushed controller carries it (Shell); ours otherwise.
            var zoomed = isPop ? fromViewController : toViewController;
            if (OperatingSystem.IsIOSVersionAtLeast(18) && zoomed.PreferredTransition is not null)
                return null!;

            plan = plan with { Type = PageTransitionType.Zoom };
        }

        var edgeActive = navigationController.InteractivePopGestureRecognizer?.State == UIGestureRecognizerState.Began;
        var interactive = isPop && (edgeActive || fullScreen);

        // A full-screen swipe needs an animator of ours even when the page asks for the platform's.
        if (plan.Type == PageTransitionType.Default && fullScreen)
            plan = plan with { Type = PageTransitionType.Slide };

        if (plan.Type == PageTransitionType.Default)
            return null!;

        if (interactive)
        {
            this.interaction = new UIPercentDrivenInteractiveTransition();
            this.drivingPan = fullScreen ? this.fullScreenPan : navigationController.InteractivePopGestureRecognizer as UIPanGestureRecognizer;
        }

        return new PageTransitionAnimator(plan, interactive, fromPage, toPage);
    }


    public override IUIViewControllerInteractiveTransitioning GetInteractionControllerForAnimationController(
        UINavigationController navigationController,
        IUIViewControllerAnimatedTransitioning animationController
    ) => this.interaction!;


    void OnFullScreenPan(UIPanGestureRecognizer pan)
    {
        if (pan.State == UIGestureRecognizerState.Began && this.navigation.TryGetTarget(out var nav))
        {
            this.fullScreenPan = pan;
            this.fullScreenPopPending = true;
            nav.PopViewController(true);
            return;
        }
        this.OnPopPan(pan);
    }


    void OnPopPan(UIPanGestureRecognizer pan)
    {
        if (this.interaction is not { } interaction || pan != this.drivingPan)
            return;

        var view = pan.View!;
        var width = Math.Max(1, view.Bounds.Width);
        var progress = (nfloat)Math.Clamp(pan.TranslationInView(view).X / width, 0, 1);

        switch (pan.State)
        {
            case UIGestureRecognizerState.Changed:
                interaction.UpdateInteractiveTransition(progress);
                break;

            case UIGestureRecognizerState.Ended:
                if (progress > 0.5 || pan.VelocityInView(view).X > 600)
                    interaction.FinishInteractiveTransition();
                else
                    interaction.CancelInteractiveTransition();
                this.interaction = null;
                break;

            case UIGestureRecognizerState.Cancelled:
            case UIGestureRecognizerState.Failed:
                interaction.CancelInteractiveTransition();
                this.interaction = null;
                break;
        }
    }


    /// <summary>
    /// The MAUI page a pushed controller shows. Shell pushes the page's own controller; NavigationPage
    /// wraps it in a parenting controller, so children are searched a few levels down.
    /// </summary>
    internal static Page? FindPage(UIViewController? vc, int depth)
    {
        if (vc is null || depth > 3)
            return null;

        if (vc is ContainerViewController { CurrentView: Page page })
            return page;

        if (vc is IPlatformViewHandler { VirtualView: Page handled })
            return handled;

        foreach (var child in vc.ChildViewControllers)
        {
            if (FindPage(child, depth + 1) is { } found)
                return found;
        }

        return null;
    }


    /// <summary>
    /// Lets the full-screen swipe start only where the edge swipe would be allowed to, and never
    /// on top of something that scrolls sideways itself.
    /// </summary>
    sealed class FullScreenPopDelegate(UINavigationController navigation) : UIGestureRecognizerDelegate
    {
        readonly WeakReference<UINavigationController> nav = new(navigation);

        public override bool ShouldBegin(UIGestureRecognizer recognizer)
        {
            if (!this.nav.TryGetTarget(out var nav) || recognizer is not UIPanGestureRecognizer pan)
                return false;

            if (nav.ViewControllers is not { Length: > 1 } || nav.GetTransitionCoordinator() is not null)
                return false;

            var velocity = pan.VelocityInView(pan.View);
            if (velocity.X <= 0 || Math.Abs(velocity.X) < Math.Abs(velocity.Y))
                return false;

            // The same rules the edge swipe obeys (MAUI's back-button behaviour, ShinyNavigationPage's root check).
            if (nav.InteractivePopGestureRecognizer is { } edge && edge.Delegate is UIGestureRecognizerDelegate rules)
                return rules.ShouldBegin(edge);

            return true;
        }

        public override bool ShouldReceiveTouch(UIGestureRecognizer recognizer, UITouch touch)
        {
            for (var v = touch.View; v is not null; v = v.Superview)
            {
                if (v is UIScrollView scroll && scroll.ContentSize.Width > scroll.Bounds.Width + 1)
                    return false;

                if (v is UISlider or UISwitch)
                    return false;
            }
            return true;
        }
    }
}


/// <summary>Modal present/dismiss for pages that set a transition.</summary>
sealed class ModalTransitionDelegate : UIViewControllerTransitioningDelegate
{
    public override IUIViewControllerAnimatedTransitioning GetAnimationControllerForPresentedController(
        UIViewController presented,
        UIViewController presenting,
        UIViewController source
    )
    {
        var page = TransitionNavigationDelegate.FindPage(presented, 0);
        return Animator(PageTransition.ResolvePush(page), null, page);
    }


    public override IUIViewControllerAnimatedTransitioning GetAnimationControllerForDismissedController(UIViewController dismissed)
    {
        var page = TransitionNavigationDelegate.FindPage(dismissed, 0);
        return Animator(PageTransition.ResolvePop(page), page, null);
    }


    static IUIViewControllerAnimatedTransitioning Animator(PageTransitionPlan plan, Page? from, Page? to)
    {
        if (plan.Type == PageTransitionType.NativeZoom)
            plan = plan with { Type = PageTransitionType.Zoom };

        return plan.Type == PageTransitionType.Default ? null! : new PageTransitionAnimator(plan, false, from, to);
    }
}


sealed class PageTransitionAnimator(PageTransitionPlan plan, bool interactive, Page? fromPage, Page? toPage)
    : UIViewControllerAnimatedTransitioning
{
    public override double TransitionDuration(IUIViewControllerContextTransitioning transitionContext)
        => plan.Type == PageTransitionType.None ? 0 : plan.Duration.TotalSeconds;


    public override void AnimateTransition(IUIViewControllerContextTransitioning context)
    {
        var container = context.ContainerView;
        var fromVc = context.GetViewControllerForKey(UITransitionContext.FromViewControllerKey);
        var toVc = context.GetViewControllerForKey(UITransitionContext.ToViewControllerKey);

        // A modal shown over its presenter leaves the presenter out of the container, so one side
        // can be missing: there is no from-view on present, no to-view on dismiss.
        var fromView = context.GetViewFor(UITransitionContext.FromViewKey);
        var toView = context.GetViewFor(UITransitionContext.ToViewKey);
        if (fromView is null && toView is null)
        {
            fromView = fromVc?.View;
            toView = toVc?.View;
        }

        if (toView is not null && toVc is not null)
            toView.Frame = context.GetFinalFrameForViewController(toVc);

        var heroes = PageTransition.FindHeroPairs(fromPage, toPage);

        // The reveal grows from the touch that started the navigation and remembers that point,
        // so the pop shrinks back into the same place.
        CGPoint? origin = null;
        if (plan.Type == PageTransitionType.CircularReveal)
        {
            if (plan.IsPop && PageTransition.GetRevealOrigin(fromPage) is { } back)
                origin = new CGPoint(back.X, back.Y);
            else if (!plan.IsPop && PageTransition.LastTouch is { } touch)
            {
                var window = container.Window;
                var p = new CGPoint(touch.X, touch.Y);
                origin = window is null ? p : container.ConvertPointFromView(p, window);
                if (toPage is not null)
                    PageTransition.SetRevealOrigin(toPage, new Point(origin.Value.X, origin.Value.Y));
            }
        }

        TransitionRunner.Run(container, fromView, toView, plan, interactive, heroes, origin, () => context.TransitionWasCancelled, cancelled =>
        {
            if (cancelled && toView is not null)
                toView.RemoveFromSuperview();

            context.CompleteTransition(!cancelled);
        });
    }
}


/// <summary>
/// The animations themselves, for any pair of views in a container: navigation pushes and pops,
/// modals, tab switches and Shell item switches all run through here.
/// </summary>
static class TransitionRunner
{
    internal static bool FadesIncoming(PageTransitionType type) => type is
        PageTransitionType.Fade or PageTransitionType.FadeThrough or PageTransitionType.Zoom or
        PageTransitionType.SharedAxisX or PageTransitionType.SharedAxisY or PageTransitionType.SharedAxisZ or
        PageTransitionType.Flip;


    internal static void Run(
        UIView container,
        UIView? fromView,
        UIView? toView,
        PageTransitionPlan plan,
        bool interactive,
        IReadOnlyList<(VisualElement From, VisualElement To)> heroes,
        CGPoint? origin,
        Func<bool> wasCancelled,
        Action<bool> completed,
        bool insertIncoming = true
    )
    {
        var type = plan.Type;
        var isPop = plan.IsPop;

        // A reveal uncovers the incoming page from under the outgoing one; with no outgoing page in
        // the container (a modal over its presenter) there is nothing to uncover.
        if (type == PageTransitionType.Reveal && (fromView is null || toView is null))
            type = PageTransitionType.Fade;

        if (insertIncoming && toView is not null)
        {
            var incomingOnTop = isPop ? type == PageTransitionType.Reveal : type != PageTransitionType.Reveal;
            if (fromView is null || fromView.Superview != container)
                container.AddSubview(toView);
            else if (incomingOnTop)
                container.InsertSubviewAbove(toView, fromView);
            else
                container.InsertSubviewBelow(toView, fromView);
        }

        // Hero frames are measured before any starting transform moves the pages.
        toView?.LayoutIfNeeded();
        var flights = HeroFlight.Prepare(container, heroes);

        var w = (double)container.Bounds.Width;
        var h = (double)container.Bounds.Height;
        var (vx, vy) = plan.Vector;
        var fullX = vx * w;
        var fullY = vy * h;

        var keyframes = new List<(double Start, double Length, Action Change)>();
        void Frame(double start, double length, Action change) => keyframes.Add((start, length, change));
        static CGAffineTransform T(double x, double y) => CGAffineTransform.MakeTranslation((nfloat)x, (nfloat)y);
        static CGAffineTransform S(double s) => CGAffineTransform.MakeScale((nfloat)s, (nfloat)s);
        var identity = CGAffineTransform.MakeIdentity();

        switch (type)
        {
            case PageTransitionType.Fade:
                if (isPop && fromView is not null)
                    Frame(0, 1, () => fromView.Alpha = 0);
                else if (toView is not null)
                {
                    toView.Alpha = 0;
                    Frame(0, 1, () => toView.Alpha = 1);
                }
                break;

            case PageTransitionType.FadeThrough:
                if (fromView is not null)
                    Frame(0, 0.35, () => fromView.Alpha = 0);
                if (toView is not null)
                {
                    toView.Alpha = 0;
                    toView.Transform = S(0.92);
                    Frame(0.35, 0.65, () => { toView.Alpha = 1; toView.Transform = identity; });
                }
                break;

            case PageTransitionType.Slide:
            case PageTransitionType.Push:
                var share = type == PageTransitionType.Push ? 1.0 : 0.3;
                if (isPop)
                {
                    if (toView is not null)
                    {
                        toView.Transform = T(-fullX * share, -fullY * share);
                        Frame(0, 1, () => toView.Transform = identity);
                    }
                    if (fromView is not null)
                        Frame(0, 1, () => fromView.Transform = T(fullX, fullY));
                }
                else
                {
                    if (toView is not null)
                    {
                        toView.Transform = T(fullX, fullY);
                        Frame(0, 1, () => toView.Transform = identity);
                    }
                    if (fromView is not null)
                        Frame(0, 1, () => fromView.Transform = T(-fullX * share, -fullY * share));
                }
                break;

            case PageTransitionType.Cover:
                if (isPop && fromView is not null)
                    Frame(0, 1, () => fromView.Transform = T(fullX, fullY));
                else if (!isPop && toView is not null)
                {
                    toView.Transform = T(fullX, fullY);
                    Frame(0, 1, () => toView.Transform = identity);
                }
                break;

            case PageTransitionType.Reveal:
                if (isPop && toView is not null)
                {
                    toView.Transform = T(-fullX, -fullY);
                    Frame(0, 1, () => toView.Transform = identity);
                }
                else if (!isPop && fromView is not null)
                    Frame(0, 1, () => fromView.Transform = T(-fullX, -fullY));
                break;

            case PageTransitionType.SharedAxisX:
            case PageTransitionType.SharedAxisY:
                double ax, ay;
                if (type == PageTransitionType.SharedAxisX)
                    (ax, ay) = (plan.IsVertical ? 30 : vx * 30, 0);
                else
                    (ax, ay) = (0, plan.IsVertical ? vy * 30 : 30);
                var sign = isPop ? -1 : 1;

                if (toView is not null)
                {
                    toView.Alpha = 0;
                    toView.Transform = T(ax * sign, ay * sign);
                    Frame(0, 1, () => toView.Transform = identity);
                    Frame(0.3, 0.7, () => toView.Alpha = 1);
                }
                if (fromView is not null)
                {
                    Frame(0, 1, () => fromView.Transform = T(-ax * sign, -ay * sign));
                    Frame(0, 0.3, () => fromView.Alpha = 0);
                }
                break;

            case PageTransitionType.SharedAxisZ:
                if (toView is not null)
                {
                    toView.Alpha = 0;
                    toView.Transform = S(isPop ? 1.1 : 0.8);
                    Frame(0, 1, () => toView.Transform = identity);
                    Frame(0.3, 0.7, () => toView.Alpha = 1);
                }
                if (fromView is not null)
                {
                    Frame(0, 1, () => fromView.Transform = S(isPop ? 0.8 : 1.1));
                    Frame(0, 0.3, () => fromView.Alpha = 0);
                }
                break;

            case PageTransitionType.Zoom:
                if (isPop && fromView is not null)
                    Frame(0, 1, () => { fromView.Alpha = 0; fromView.Transform = S(0.85); });
                else if (!isPop && toView is not null)
                {
                    toView.Alpha = 0;
                    toView.Transform = S(0.85);
                    Frame(0, 1, () => { toView.Alpha = 1; toView.Transform = identity; });
                }
                break;

            case PageTransitionType.CircularReveal:
                var masked = isPop ? fromView : toView;
                if (masked is not null)
                {
                    // A round mask view scaled up from nothing (or down to it) - a plain view
                    // transform, so an interactive pop scrubs it with the finger like everything else.
                    var center = origin ?? new CGPoint(w / 2, h / 2);
                    var cx = (double)center.X;
                    var cy = (double)center.Y;
                    var radius = Math.Sqrt(Math.Pow(Math.Max(cx, w - cx), 2) + Math.Pow(Math.Max(cy, h - cy), 2));
                    var mask = new UIView(masked.ConvertRectFromView(new CGRect(cx - radius, cy - radius, radius * 2, radius * 2), container))
                    {
                        BackgroundColor = UIColor.Black
                    };
                    mask.Layer.CornerRadius = (nfloat)radius;
                    mask.Transform = isPop ? identity : S(0.001);
                    masked.MaskView = mask;
                    Frame(0, 1, () => mask.Transform = isPop ? S(0.001) : identity);
                }
                break;

            case PageTransitionType.Flip:
                var horizontal = !plan.IsVertical;
                var turn = (nfloat)(Math.PI / 2 * (horizontal ? vx : -vy) * (isPop ? -1 : 1));
                CATransform3D Rotate(nfloat angle)
                {
                    var t = CATransform3D.Identity;
                    t.M34 = -1f / 900f;
                    return horizontal ? t.Rotate(angle, 0, 1, 0) : t.Rotate(angle, 1, 0, 0);
                }

                if (toView is not null)
                {
                    toView.Alpha = 0;
                    toView.Transform3D = Rotate(-turn);
                }
                if (fromView is not null)
                    Frame(0, 0.5, () => fromView.Transform3D = Rotate(turn));
                Frame(0.5, 0.001, () =>
                {
                    if (fromView is not null)
                        fromView.Alpha = 0;
                    if (toView is not null)
                        toView.Alpha = 1;
                });
                if (toView is not null)
                    Frame(0.5, 0.5, () => toView.Transform3D = CATransform3D.Identity);
                break;
        }

        foreach (var flight in flights)
            Frame(0, 1, flight.Fly);

        void Finish()
        {
            foreach (var view in new[] { fromView, toView })
            {
                if (view is null)
                    continue;
                view.Transform = identity;
                view.Transform3D = CATransform3D.Identity;
                view.Alpha = 1;
                view.MaskView = null;
            }
            foreach (var flight in flights)
                flight.Land();

            completed(wasCancelled());
        }

        if (type == PageTransitionType.None || keyframes.Count == 0)
        {
            Finish();
            return;
        }

        // A finger drives an interactive pop, so the curve has to be linear or the page lags and
        // then races the finger.
        var curve = interactive ? UIViewAnimationOptions.CurveLinear : UIViewAnimationOptions.CurveEaseInOut;
        UIView.AnimateKeyframes(
            plan.Duration.TotalSeconds,
            0,
            UIViewKeyframeAnimationOptions.CalculationModeLinear | (UIViewKeyframeAnimationOptions)(ulong)curve,
            () =>
            {
                foreach (var (start, length, change) in keyframes)
                    UIView.AddKeyframeWithRelativeStartTime(start, length, change);
            },
            _ => Finish()
        );
    }
}


/// <summary>
/// One shared element: snapshots of the view on both pages, flown from the outgoing position to the
/// incoming one and crossfaded on the way, while the real views are hidden underneath.
/// </summary>
sealed class HeroFlight
{
    readonly UIView source;
    readonly UIView target;
    readonly UIView fromSnapshot;
    readonly UIView toSnapshot;
    readonly CGRect end;
    readonly nfloat sourceAlpha;
    readonly nfloat targetAlpha;

    HeroFlight(UIView source, UIView target, UIView fromSnapshot, UIView toSnapshot, CGRect end)
    {
        this.source = source;
        this.target = target;
        this.fromSnapshot = fromSnapshot;
        this.toSnapshot = toSnapshot;
        this.end = end;
        this.sourceAlpha = source.Alpha;
        this.targetAlpha = target.Alpha;
    }


    internal static List<HeroFlight> Prepare(UIView container, IReadOnlyList<(VisualElement From, VisualElement To)> heroes)
    {
        var flights = new List<HeroFlight>();
        foreach (var (from, to) in heroes)
        {
            if (from.Handler?.PlatformView is not UIView source || to.Handler?.PlatformView is not UIView target)
                continue;

            if (source.Window is null || target.Bounds.IsEmpty)
                continue;

            var start = source.ConvertRectToView(source.Bounds, container);
            var end = target.ConvertRectToView(target.Bounds, container);
            var fromSnapshot = source.SnapshotView(false);
            var toSnapshot = target.SnapshotView(true);
            if (fromSnapshot is null || toSnapshot is null)
                continue;

            fromSnapshot.Frame = start;
            toSnapshot.Frame = start;
            toSnapshot.Alpha = 0;
            container.AddSubview(fromSnapshot);
            container.AddSubview(toSnapshot);

            flights.Add(new HeroFlight(source, target, fromSnapshot, toSnapshot, end));
            source.Alpha = 0;
            target.Alpha = 0;
        }
        return flights;
    }


    internal void Fly()
    {
        this.fromSnapshot.Frame = this.end;
        this.toSnapshot.Frame = this.end;
        this.fromSnapshot.Alpha = 0;
        this.toSnapshot.Alpha = 1;
    }


    internal void Land()
    {
        this.source.Alpha = this.sourceAlpha;
        this.target.Alpha = this.targetAlpha;
        this.fromSnapshot.RemoveFromSuperview();
        this.toSnapshot.RemoveFromSuperview();
    }
}

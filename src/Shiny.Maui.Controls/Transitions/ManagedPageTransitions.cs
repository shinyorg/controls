using System.Runtime.CompilerServices;
using Microsoft.Maui.Handlers;

namespace Shiny.Maui.Controls;

/// <summary>
/// Page transitions on the heads without a native navigation hook: Windows, macOS (AppKit),
/// Mac Catalyst and Linux (GTK4), plus the per-call overloads on Android's NavigationPage and modals.
/// The transitions run as property animations on the pages (see <see cref="ManagedTransitionRunner"/>).
/// </summary>
/// <remarks>
/// <para>Every navigation follows the same steps. Just before the head swaps pages (NavigationPage
/// adding or removing its top child, a window's ModalPushing/ModalPopping, Shell's Navigating and the
/// pushed page joining its section), the incoming page is put in the transition's starting state, so
/// it is never drawn at rest. Once the head has shown it (NavigationPage's Pushed/Popped, the
/// window's ModalPushed/ModalPopped, Shell's Navigated) the animation runs.</para>
/// <para>What these heads can't give is the outgoing page: they take it off screen as the new one
/// goes on, so only the incoming page's half of a transition plays (a Cover pop, whose only motion is
/// the outgoing page, fades the incoming page in instead). The exceptions are a modal presented over
/// a presenter that stays visible, and the per-call pop overloads on a modal, which play the exit on
/// the live modal before it is dismissed.</para>
/// </remarks>
static partial class ManagedPageTransitions
{
    internal enum Kind { Stack, Modal, Shell }

    sealed class Pending
    {
        public required Page To { get; init; }
        public Page? From { get; init; }
        public bool FromVisible { get; init; }
        public required bool IsPop { get; init; }
        public required Kind Kind { get; init; }
        public required PageTransitionResolution Resolution { get; init; }
        public required bool FromOverride { get; init; }
        public required Size Bounds { get; init; }
        public required IReadOnlyList<(VisualElement Target, Rect Source)> Heroes { get; init; }
        public ManagedTransitionRunner.Choreography? Choreography { get; set; }
        public bool Started { get; set; }
    }

    static Pending? pending;
    static bool installed;
    static readonly ConditionalWeakTable<Element, object> attached = new();

    /// <summary>Set while a per-call overload hands the head a navigation it has already animated.</summary>
    internal static bool Suppressed { get; set; }

    /// <summary>True while a managed transition is waiting for the head to swap pages.</summary>
    internal static bool IsPending => pending is not null;


    internal static void Install()
    {
        if (installed)
            return;

        installed = true;
        ElementHandler.ElementMapper.AppendToMapping("ShinyManagedPageTransitions", static (_, element) =>
        {
            if (element is Window window)
                AttachWindow(window);
            else if (element is Application app)
                foreach (var w in app.Windows)
                    AttachWindow(w);
        });
        InstallHeadHooks();
    }

    /// <summary>Head-specific hooks (Windows strips its own navigation animation while one of ours runs).</summary>
    static partial void InstallHeadHooks();


    /// <summary>
    /// Whether this head runs managed transitions for this kind of navigation. Plain navigations are
    /// only animated where the head's own animation is absent or can be switched off; the per-call
    /// overloads also ask the head not to animate, so they reach a few more.
    /// </summary>
    internal static bool Handles(Kind kind, bool fromOverride)
    {
        if (!PageTransition.IsEnabled)
            return false;
        if (OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst())
            return false;
        if (OperatingSystem.IsAndroid())
            return fromOverride && kind != Kind.Shell; // Shell is hooked natively
        if (OperatingSystem.IsWindows())
            return true;
        if (OperatingSystem.IsMacCatalyst())
            return fromOverride; // UIKit's own push runs otherwise
        if (OperatingSystem.IsMacOS())
            return kind != Kind.Modal; // modals are native sheets/windows
        if (OperatingSystem.IsLinux())
            return kind == Kind.Shell; // NavigationPage is a Gtk.Stack that always slides; modals are dialogs

        return fromOverride;
    }


    #region Discovery

    static bool TryAttach(Element element)
    {
        if (attached.TryGetValue(element, out _))
            return false;

        attached.Add(element, new object());
        return true;
    }


    static void AttachWindow(Window window)
    {
        if (TryAttach(window))
        {
            window.ModalPushing += OnModalPushing;
            window.ModalPushed += OnModalPushed;
            window.ModalPopping += OnModalPopping;
            window.ModalPopped += OnModalPopped;
            window.DescendantAdded += (_, e) => AttachTree(e.Element);
            window.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Window.Page) && s is Window { Page: { } page })
                    AttachTree(page);
            };
        }

        if (window.Page is { } root)
            AttachTree(root);
    }


    static void AttachTree(Element element)
    {
        if (element is NavigationPage nav)
            AttachNavigationPage(nav);
        else if (element is Shell shell)
            AttachShell(shell);
        else if (element is Page { Parent: ShellSection section } page)
            OnShellPageAdded(section, page);

        // A NavigationPage can sit inside a TabbedPage or FlyoutPage; content pages and Shell (which
        // can't host one) hold nothing more to find.
        if (element is not Page || element is ContentPage or Shell)
            return;

        foreach (var child in ((IElementController)element).LogicalChildren.Where(x => x is Page))
            AttachTree(child);
    }


    static void AttachNavigationPage(NavigationPage nav)
    {
        if (!TryAttach(nav))
            return;

        nav.ChildAdded += OnNavigationChildAdded;
        nav.ChildRemoved += OnNavigationChildRemoved;
        nav.Pushed += (s, _) => StartFor(((NavigationPage)s!).CurrentPage);
        nav.Popped += (s, _) => StartFor(((NavigationPage)s!).CurrentPage);
        nav.PoppedToRoot += (s, _) => StartFor(((NavigationPage)s!).CurrentPage);
    }


    static void AttachShell(Shell shell)
    {
        if (!TryAttach(shell))
            return;

        shell.Navigating += OnShellNavigating;
        shell.Navigated += (s, _) =>
        {
            shellPushFrom = null;
            StartFor(((Shell)s!).CurrentPage);
        };
    }

    #endregion

    #region NavigationPage

    static void OnNavigationChildAdded(object? sender, ElementEventArgs e)
    {
        // A push adds the page on top (an insert puts it lower down and isn't a navigation).
        if (sender is not NavigationPage nav || e.Element is not Page page || Suppressed)
            return;

        var stack = nav.Navigation.NavigationStack;
        if (stack.Count < 2 || !ReferenceEquals(stack[^1], page))
            return;

        Prepare(Kind.Stack, stack[^2], page, isPop: false, MeasureContainer(nav), fromVisible: false);
    }


    static void OnNavigationChildRemoved(object? sender, ElementEventArgs e)
    {
        // A pop removes the top page (RemovePage of a lower one isn't a navigation). A pop to root
        // removes each page in turn, so the last one prepared is the root.
        if (sender is not NavigationPage nav || e.Element is not Page page || Suppressed)
            return;

        var stack = nav.Navigation.NavigationStack;
        var current = nav.CurrentPage;
        var popping = ReferenceEquals(current, page) ||
            (pending is { Kind: Kind.Stack, IsPop: true } p && ReferenceEquals(p.From, current));
        if (stack.Count == 0 || current is null || !popping)
            return;

        Prepare(Kind.Stack, current, stack[^1], isPop: true, MeasureContainer(nav), fromVisible: false);
    }

    #endregion

    #region Modals

    static void OnModalPushing(object? sender, ModalPushingEventArgs e)
    {
        if (sender is not Window window || e.Modal is not { } modal || Suppressed)
            return;

        var presenter = window.Navigation.ModalStack.LastOrDefault() ?? window.Page;
        Prepare(Kind.Modal, presenter, modal, isPop: false, MeasureContainer(window.Page), PresenterStaysVisible(modal));
    }


    static void OnModalPopping(object? sender, ModalPoppingEventArgs e)
    {
        if (sender is not Window window || e.Modal is not { } modal || Suppressed)
            return;

        // A presenter that stayed visible under the modal has nothing to animate in.
        if (PresenterStaysVisible(modal))
            return;

        var stack = window.Navigation.ModalStack;
        var below = stack.Count > 1 ? stack[^2] : window.Page;
        if (below is not null)
            Prepare(Kind.Modal, modal, below, isPop: true, MeasureContainer(window.Page), fromVisible: false);
    }


    static void OnModalPushed(object? sender, ModalPushedEventArgs e) => StartFor(e.Modal);

    static void OnModalPopped(object? sender, ModalPoppedEventArgs e)
    {
        if (sender is Window window)
            StartFor(window.Navigation.ModalStack.LastOrDefault() ?? window.Page);
    }


    /// <summary>
    /// Whether the page underneath stays on screen while a modal shows. Android's modal is a dialog
    /// over it; Windows keeps it only under a modal that paints its own background (it assumes the
    /// modal may be see-through).
    /// </summary>
    internal static bool PresenterStaysVisible(Page modal)
    {
        if (OperatingSystem.IsAndroid())
            return true;

        if (OperatingSystem.IsWindows())
            return modal.BackgroundColor is not null || modal.Background is { IsEmpty: false };

        return false;
    }

    #endregion

    #region Shell

    static void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
    {
        if (sender is not Shell shell || Suppressed)
            return;

        if (e.Source == ShellNavigationSource.Push)
        {
            // The page isn't known until it joins its section, just before the head shows it.
            shellPushFrom = new WeakReference<Page>(shell.CurrentPage);
            return;
        }

        if (e.Source is not (ShellNavigationSource.Pop or ShellNavigationSource.PopToRoot))
            return;

        var section = shell.CurrentItem?.CurrentItem;
        var stack = section?.Stack;
        if (section is null || stack is null || stack.Count < 2 || shell.CurrentPage is not { } from)
            return;

        var to = e.Source == ShellNavigationSource.PopToRoot || stack.Count == 2
            ? (section.CurrentItem as IShellContentController)?.Page
            : stack[^2];

        if (to is not null)
            Prepare(Kind.Shell, from, to, isPop: true, MeasureContainer(from), fromVisible: false);
    }

    static WeakReference<Page>? shellPushFrom;


    static void OnShellPageAdded(ShellSection section, Page page)
    {
        if (shellPushFrom is null || Suppressed)
            return;

        shellPushFrom.TryGetTarget(out var from);
        Prepare(Kind.Shell, from, page, isPop: false, MeasureContainer(from), fromVisible: false);
    }

    #endregion

    #region Running

    static void Prepare(Kind kind, Page? from, Page to, bool isPop, Size bounds, bool fromVisible)
    {
        Abandon();

        var subject = isPop ? from : to;
        var fromOverride = PageTransitionOverrides.Find(subject) is not null;
        if (!Handles(kind, fromOverride))
            return;

        var resolution = PageTransition.ResolveTransition(from, to, isPop, kind == Kind.Modal);
        if (!resolution.IsSet)
            return;

        var heroes = resolution.Plan.IsAnimated && PageTransition.HeroesAllowed && resolution.Animator is null
            ? PageTransition.FindHeroPairs(from, to).Select(x => (x.To, ManagedTransitionRunner.BoundsInPage(x.From))).ToList()
            : [];

        var next = new Pending
        {
            From = from,
            To = to,
            FromVisible = fromVisible,
            IsPop = isPop,
            Kind = kind,
            Resolution = resolution,
            FromOverride = fromOverride,
            Bounds = bounds,
            Heroes = heroes
        };

        // A built-in type's starting state goes on now, before the head draws the page. A custom
        // animator sets its own starting state when it runs.
        if (resolution.Animator is null && resolution.Plan.IsAnimated)
        {
            next.Choreography = ManagedTransitionRunner.Prepare(
                resolution.Plan,
                fromVisible ? PageTransition.GetAnimationSurface(from) : null,
                PageTransition.GetAnimationSurface(to),
                bounds
            );
        }

        pending = next;

        // A navigation that is cancelled or never shows its page must not leave it hidden.
        to.Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(3), () =>
        {
            if (ReferenceEquals(pending, next) && !next.Started)
                Abandon();
        });
    }


    /// <summary>
    /// Called by a head's navigation handler as it is asked to swap pages: true when it should not
    /// run its own animation because one of ours is about to. A navigation the caller asked not to
    /// animate drops ours instead (unless a per-call overload asked for it).
    /// </summary>
    internal static bool OnHeadNavigation(bool animated)
    {
        if (pending is not { } p)
            return false;

        if (!animated && !p.FromOverride)
        {
            Abandon();
            return false;
        }
        return true;
    }


    /// <summary>Drops a prepared transition that won't run, putting its page back at rest.</summary>
    internal static void Abandon()
    {
        if (pending is not { } p)
            return;

        pending = null;
        if (!p.Started)
        {
            PageTransitionHost.Reset(p.To);
            if (p.FromVisible)
                PageTransitionHost.Reset(p.From);
        }
    }


    static void StartFor(Page? shown)
    {
        if (pending is not { } p || p.Started)
            return;

        if (!ReferenceEquals(p.To, shown))
        {
            Abandon();
            return;
        }

        p.Started = true;
        pending = null;
        _ = RunAsync(p);
    }


    static async Task RunAsync(Pending p)
    {
        var token = PageTransitionCancellation.Begin();
        var from = p.FromVisible ? p.From : null;
        var bounds = p.To.Width > 0 && p.To.Height > 0 ? new Size(p.To.Width, p.To.Height) : p.Bounds;
        try
        {
            if (p.Resolution.Animator is { } animator)
            {
                var context = new PageTransitionContext(from, p.To, p.IsPop, p.Kind == Kind.Modal, p.Resolution.Plan.Duration, bounds)
                {
                    Direction = p.Resolution.Plan.Direction
                };
                await PageTransitionHost.RunAnimatorAsync(animator, context, token);
            }
            else if (p.Choreography is { } choreography)
            {
                var heroes = p.Heroes.Select(x => new ManagedTransitionRunner.HeroFlight(x.Target, x.Source)).ToList();
                await choreography.RunAsync(p.Resolution.Plan.Duration, heroes, token);
            }
        }
        catch (Exception ex)
        {
            // The pages are already reset by the runner; the navigation itself is unaffected.
            System.Diagnostics.Debug.WriteLine($"[Shiny.PageTransition] Managed transition failed: {ex}");
        }
        finally
        {
            PageTransitionCancellation.End(token);
        }
    }


    /// <summary>
    /// The per-call modal pop on a managed head: the modal is still on screen before it is
    /// dismissed, so its exit (and, where the presenter shows underneath, the presenter's entrance)
    /// plays in full first. The dismiss that follows doesn't animate.
    /// </summary>
    internal static async Task RunModalExitAsync(Page modal, Page? presenter, PageTransitionResolution resolution, Size bounds)
    {
        Abandon();
        var to = presenter is not null && PresenterStaysVisible(modal) ? presenter : null;
        var token = PageTransitionCancellation.Begin();
        try
        {
            if (resolution.Animator is { } animator)
            {
                var context = new PageTransitionContext(modal, to, true, true, resolution.Plan.Duration, bounds)
                {
                    Direction = resolution.Plan.Direction
                };
                await PageTransitionHost.RunAnimatorAsync(animator, context, token);
            }
            else if (resolution.Plan.IsAnimated)
            {
                await ManagedTransitionRunner.RunAsync(
                    resolution.Plan,
                    PageTransition.GetAnimationSurface(modal),
                    PageTransition.GetAnimationSurface(to),
                    bounds,
                    [],
                    token
                );
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Shiny.PageTransition] Managed transition failed: {ex}");
        }
        finally
        {
            PageTransitionCancellation.End(token);
        }
    }


    static Size MeasureContainer(VisualElement? element)
    {
        for (Element? e = element; e is not null; e = e.Parent)
        {
            if (e is VisualElement { Width: > 0, Height: > 0 } sized)
                return new Size(sized.Width, sized.Height);

            if (e is Window { Width: > 0, Height: > 0 } window)
                return new Size(window.Width, window.Height);
        }
        return Microsoft.Maui.Graphics.Size.Zero;
    }

    #endregion
}

namespace Shiny.Maui.Controls;

/// <summary>
/// Navigation calls that carry a transition for that one navigation, ahead of anything the page or
/// app sets. They hand the transition to the resolver as a one-shot override and then call the
/// ordinary <see cref="INavigation"/> / <see cref="Shell"/> method, so <see cref="INavigation"/> and
/// <see cref="Shell"/> themselves are unchanged and everything else about the navigation (events,
/// lifecycle, Shell routing, the returned task) is MAUI's own.
/// </summary>
/// <remarks>
/// <c>animated: false</c>, or <see cref="PageTransitionType.None"/>, navigates without any animation.
/// <c>UsePageTransitions</c> must be on. Platform coverage follows the transitions themselves: on
/// iOS every call is covered; on Android a Shell navigation runs natively and a NavigationPage or
/// modal call runs as a managed animation; on Windows, macOS, Mac Catalyst and Linux they run as
/// managed animations, except a Linux NavigationPage (its Gtk.Stack always slides) and macOS/Linux
/// modals (native sheets and dialogs), which keep the head's own presentation.
/// </remarks>
public static class PageTransitionNavigationExtensions
{
    #region Push / pop

    /// <summary>Pushes <paramref name="page"/> with <paramref name="type"/>, whatever the page itself sets.</summary>
    public static Task PushAsync(
        this INavigation navigation,
        Page page,
        PageTransitionType type,
        PageTransitionDirection direction = PageTransitionDirection.Default,
        TimeSpan? duration = null,
        bool animated = true
    ) => PushCore(navigation, page, new PageTransitionOverride(type, direction, duration, null), animated, false);


    /// <summary>Pushes <paramref name="page"/> with a custom <paramref name="animator"/>.</summary>
    public static Task PushAsync(
        this INavigation navigation,
        Page page,
        IPageTransitionAnimator animator,
        TimeSpan? duration = null,
        bool animated = true
    ) => PushCore(navigation, page, new PageTransitionOverride(PageTransitionType.Default, PageTransitionDirection.Default, duration, animator), animated, false);


    /// <summary>Pops the top page with <paramref name="type"/>, whatever the page itself sets.</summary>
    public static Task<Page?> PopAsync(
        this INavigation navigation,
        PageTransitionType type,
        PageTransitionDirection direction = PageTransitionDirection.Default,
        TimeSpan? duration = null,
        bool animated = true
    ) => PopCore(navigation, new PageTransitionOverride(type, direction, duration, null), animated, false);


    /// <summary>Pops the top page with a custom <paramref name="animator"/>.</summary>
    public static Task<Page?> PopAsync(
        this INavigation navigation,
        IPageTransitionAnimator animator,
        TimeSpan? duration = null,
        bool animated = true
    ) => PopCore(navigation, new PageTransitionOverride(PageTransitionType.Default, PageTransitionDirection.Default, duration, animator), animated, false);

    #endregion

    #region Modals

    /// <summary>Presents <paramref name="page"/> modally with <paramref name="type"/>, whatever the page itself sets.</summary>
    public static Task PushModalAsync(
        this INavigation navigation,
        Page page,
        PageTransitionType type,
        PageTransitionDirection direction = PageTransitionDirection.Default,
        TimeSpan? duration = null,
        bool animated = true
    ) => PushCore(navigation, page, new PageTransitionOverride(type, direction, duration, null), animated, true);


    /// <summary>Presents <paramref name="page"/> modally with a custom <paramref name="animator"/>.</summary>
    public static Task PushModalAsync(
        this INavigation navigation,
        Page page,
        IPageTransitionAnimator animator,
        TimeSpan? duration = null,
        bool animated = true
    ) => PushCore(navigation, page, new PageTransitionOverride(PageTransitionType.Default, PageTransitionDirection.Default, duration, animator), animated, true);


    /// <summary>Dismisses the top modal page with <paramref name="type"/>, whatever the page itself sets.</summary>
    public static Task<Page?> PopModalAsync(
        this INavigation navigation,
        PageTransitionType type,
        PageTransitionDirection direction = PageTransitionDirection.Default,
        TimeSpan? duration = null,
        bool animated = true
    ) => PopCore(navigation, new PageTransitionOverride(type, direction, duration, null), animated, true);


    /// <summary>Dismisses the top modal page with a custom <paramref name="animator"/>.</summary>
    public static Task<Page?> PopModalAsync(
        this INavigation navigation,
        IPageTransitionAnimator animator,
        TimeSpan? duration = null,
        bool animated = true
    ) => PopCore(navigation, new PageTransitionOverride(PageTransitionType.Default, PageTransitionDirection.Default, duration, animator), animated, true);

    #endregion

    #region Shell

    /// <summary>
    /// <see cref="Shell.GoToAsync(ShellNavigationState, bool)"/> with <paramref name="type"/> for every
    /// page it pushes or pops, whatever those pages set.
    /// </summary>
    public static Task GoToAsync(
        this Shell shell,
        ShellNavigationState state,
        PageTransitionType type,
        PageTransitionDirection direction = PageTransitionDirection.Default,
        TimeSpan? duration = null,
        bool animated = true,
        IDictionary<string, object>? parameters = null
    ) => GoCore(shell, state, new PageTransitionOverride(type, direction, duration, null), animated, parameters);


    /// <summary>
    /// <see cref="Shell.GoToAsync(ShellNavigationState, bool)"/> with a custom
    /// <paramref name="animator"/> for every page it pushes or pops.
    /// </summary>
    public static Task GoToAsync(
        this Shell shell,
        ShellNavigationState state,
        IPageTransitionAnimator animator,
        TimeSpan? duration = null,
        bool animated = true,
        IDictionary<string, object>? parameters = null
    ) => GoCore(shell, state, new PageTransitionOverride(PageTransitionType.Default, PageTransitionDirection.Default, duration, animator), animated, parameters);

    #endregion

    #region Core

    static PageTransitionOverride Still(PageTransitionOverride shot)
        => shot with { Type = PageTransitionType.None, Animator = null };

    static bool IsStill(PageTransitionOverride shot)
        => shot.Animator is null && shot.Type == PageTransitionType.None;


    static async Task PushCore(INavigation navigation, Page page, PageTransitionOverride shot, bool animated, bool modal)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(page);

        if (!animated)
            shot = Still(shot);

        // Where the transition runs as a managed animation, the head is told not to animate so
        // its own push doesn't play underneath it.
        var kind = modal ? ManagedPageTransitions.Kind.Modal : KindOf(navigation);
        var headAnimated = !IsStill(shot) && !ManagedPageTransitions.Handles(kind, true);

        PageTransitionOverrides.Set(page, shot);
        try
        {
            if (modal)
                await navigation.PushModalAsync(page, headAnimated);
            else
                await navigation.PushAsync(page, headAnimated);
        }
        finally
        {
            PageTransitionOverrides.Clear(page);
        }
    }


    static async Task<Page?> PopCore(INavigation navigation, PageTransitionOverride shot, bool animated, bool modal)
    {
        ArgumentNullException.ThrowIfNull(navigation);

        var stack = modal ? navigation.ModalStack : navigation.NavigationStack;
        var top = stack.Count > 0 ? stack[^1] : null;
        if (top is null || (!modal && stack.Count < 2))
            return modal ? await navigation.PopModalAsync(animated) : await navigation.PopAsync(animated);

        if (!animated)
            shot = Still(shot);

        var kind = modal ? ManagedPageTransitions.Kind.Modal : KindOf(navigation);
        var managed = ManagedPageTransitions.Handles(kind, true);
        var headAnimated = !IsStill(shot) && !managed;

        PageTransitionOverrides.Set(top, shot);
        try
        {
            if (!modal)
                return await navigation.PopAsync(headAnimated);

            if (!managed || IsStill(shot))
                return await navigation.PopModalAsync(headAnimated);

            // The modal is still on screen, so its exit plays in full before it is dismissed.
            var presenter = stack.Count > 1 ? stack[^2] : top.Window?.Page;
            var resolution = PageTransition.ResolveTransition(top, presenter, true, true);
            var bounds = new Size(Math.Max(0, top.Width), Math.Max(0, top.Height));
            await ManagedPageTransitions.RunModalExitAsync(top, presenter, resolution, bounds);

            ManagedPageTransitions.Suppressed = true;
            try
            {
                return await navigation.PopModalAsync(false);
            }
            finally
            {
                ManagedPageTransitions.Suppressed = false;
            }
        }
        finally
        {
            PageTransitionOverrides.Clear(top);
        }
    }


    static async Task GoCore(Shell shell, ShellNavigationState state, PageTransitionOverride shot, bool animated, IDictionary<string, object>? parameters)
    {
        ArgumentNullException.ThrowIfNull(shell);

        if (!animated)
            shot = Still(shot);

        var headAnimated = !IsStill(shot) && !ManagedPageTransitions.Handles(ManagedPageTransitions.Kind.Shell, true);
        PageTransitionOverrides.SetAmbient(shot);
        try
        {
            if (parameters is null)
                await shell.GoToAsync(state, headAnimated);
            else
                await shell.GoToAsync(state, headAnimated, parameters);
        }
        finally
        {
            PageTransitionOverrides.SetAmbient(null);
        }
    }


    /// <summary>Whether <paramref name="navigation"/> belongs to a Shell or a NavigationPage.</summary>
    static ManagedPageTransitions.Kind KindOf(INavigation navigation)
    {
        var probe = navigation.NavigationStack.LastOrDefault(x => x is not null);
        for (Element? e = probe; e is not null; e = e.Parent)
        {
            if (e is ShellSection or Shell)
                return ManagedPageTransitions.Kind.Shell;
            if (e is NavigationPage)
                return ManagedPageTransitions.Kind.Stack;
        }
        return Shell.Current is not null ? ManagedPageTransitions.Kind.Shell : ManagedPageTransitions.Kind.Stack;
    }

    #endregion
}

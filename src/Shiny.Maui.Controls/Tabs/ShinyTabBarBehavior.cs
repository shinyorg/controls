using System.ComponentModel;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

/// <summary>
/// Replaces a <see cref="Shell"/>'s native bottom tab bar with a <see cref="ShinyTabBar"/>, without
/// changing a line of the Shell's structure or routing.
/// </summary>
/// <remarks>
/// <para>Shell keeps doing everything it is good at — routes, lazily built <c>ShellContent</c>,
/// deep links, the navigation stack inside each tab. The behavior only takes over the chrome: it
/// hides the platform bar, mirrors the Shell's own tabs into a <see cref="ShinyTabBar"/>, docks that
/// over whichever page is showing, and turns a tap back into a <c>CurrentItem</c> change.</para>
/// <para>Because the tabs are mirrored from the Shell, the bar's <see cref="ShinyTabBar.Items"/> are
/// managed here — anything set on them by hand is replaced. Per-tab chrome goes on the Shell
/// elements instead, with <see cref="ShinyTabs"/>: an icon and a badge on the <c>ShellContent</c>
/// (readable before its page exists, which is what a lazy tab needs), and the centre menu's actions
/// on the page or on its <c>ShellContent</c>.</para>
/// <para>The centre menu is resolved from the current page first, then the current
/// <c>ShellContent</c>, its <c>Tab</c> and its <c>ShellItem</c>: the first of those that declares a
/// <see cref="ShinyTabs.MenuContentTemplateProperty"/>/<see cref="ShinyTabs.MenuContentProperty"/>
/// wins for content, and the first with any <see cref="ShinyTabs.ActionsProperty"/> rows wins for
/// rows. A page's rows replace its shell element's rather than merging with them, the same way a
/// page's badge beats the tab's. Rows declared on a shell element bind against that element's
/// binding context - the Shell's, unless one was set on it.</para>
/// </remarks>
/// <example>
/// <code language="xaml">
/// &lt;Shell ...&gt;
///     &lt;Shell.Behaviors&gt;
///         &lt;shiny:ShinyTabBarBehavior&gt;
///             &lt;shiny:ShinyTabBar IndicatorStyle="Pill"&gt;
///                 &lt;shiny:ShinyTabBar.CenterButton&gt;
///                     &lt;shiny:TabCenterButton Icon="plus" /&gt;
///                 &lt;/shiny:ShinyTabBar.CenterButton&gt;
///             &lt;/shiny:ShinyTabBar&gt;
///         &lt;/shiny:ShinyTabBarBehavior&gt;
///     &lt;/Shell.Behaviors&gt;
///
///     &lt;TabBar&gt;
///         &lt;Tab Title="Home" shiny:ShinyTabs.Icon="home"&gt;
///             &lt;ShellContent ContentTemplate="{DataTemplate local:HomePage}" /&gt;
///         &lt;/Tab&gt;
///     &lt;/TabBar&gt;
/// &lt;/Shell&gt;
/// </code>
/// </example>
[ContentProperty(nameof(Bar))]
public class ShinyTabBarBehavior : Behavior<Shell>
{
    /// <summary>Backing store for <see cref="Bar"/>.</summary>
    public static readonly BindableProperty BarProperty = BindableProperty.Create(
        nameof(Bar), typeof(ShinyTabBar), typeof(ShinyTabBarBehavior), null,
        defaultValueCreator: _ => new ShinyTabBar(),
        propertyChanged: (b, o, n) => ((ShinyTabBarBehavior)b).OnBarChanged(o as ShinyTabBar, n as ShinyTabBar));

    /// <summary>Backing store for <see cref="TabSource"/>.</summary>
    public static readonly BindableProperty TabSourceProperty = BindableProperty.Create(
        nameof(TabSource), typeof(ShellTabSource), typeof(ShinyTabBarBehavior), ShellTabSource.Auto,
        propertyChanged: (b, _, _) => ((ShinyTabBarBehavior)b).Refresh());

    /// <summary>Backing store for <see cref="Transition"/>.</summary>
    public static readonly BindableProperty TransitionProperty = BindableProperty.Create(
        nameof(Transition), typeof(StateTransition), typeof(ShinyTabBarBehavior), StateTransition.Fade);

    /// <summary>Backing store for <see cref="TransitionDuration"/>.</summary>
    public static readonly BindableProperty TransitionDurationProperty = BindableProperty.Create(
        nameof(TransitionDuration), typeof(uint), typeof(ShinyTabBarBehavior), 200u);

    /// <summary>Backing store for <see cref="HideOnPush"/>.</summary>
    public static readonly BindableProperty HideOnPushProperty = BindableProperty.Create(
        nameof(HideOnPush), typeof(bool), typeof(ShinyTabBarBehavior), true,
        propertyChanged: (b, _, _) => ((ShinyTabBarBehavior)b).Refresh());

    Shell? shell;
    readonly List<BaseShellItem> sources = new();
    bool suppressSelectionSync;
    ContentPage? attachedPage;
    BaseShellItem? lastSelectedSource;

    /// <summary>
    /// The bar to dock. Defaults to a stock one; supply your own — as the behavior's content, so no
    /// property element is needed — to style it or give it a centre button.
    /// </summary>
    public ShinyTabBar Bar
    {
        get => (ShinyTabBar)this.GetValue(BarProperty);
        set => this.SetValue(BarProperty, value);
    }

    /// <summary>
    /// Which level of the Shell becomes the tabs. Defaults to <see cref="ShellTabSource.Auto"/>,
    /// which is where MAUI's own bottom bar takes them from.
    /// </summary>
    public ShellTabSource TabSource
    {
        get => (ShellTabSource)this.GetValue(TabSourceProperty);
        set => this.SetValue(TabSourceProperty, value);
    }

    /// <summary>
    /// How the incoming page animates in on a tab change. Shell owns the page swap itself, so only
    /// the entering half of the transition runs — there is no outgoing page left to animate by the
    /// time <c>Navigated</c> reports the change.
    /// </summary>
    public StateTransition Transition
    {
        get => (StateTransition)this.GetValue(TransitionProperty);
        set => this.SetValue(TransitionProperty, value);
    }

    /// <summary>Entry animation length in milliseconds. Zero shows the page with no animation.</summary>
    public uint TransitionDuration
    {
        get => (uint)this.GetValue(TransitionDurationProperty);
        set => this.SetValue(TransitionDurationProperty, value);
    }

    /// <summary>
    /// Hide the bar on pages pushed onto a tab's stack, which is what Shell does with its own. Turn
    /// it off for a bar that stays put through a whole flow.
    /// </summary>
    public bool HideOnPush
    {
        get => (bool)this.GetValue(HideOnPushProperty);
        set => this.SetValue(HideOnPushProperty, value);
    }


    /// <inheritdoc/>
    protected override void OnAttachedTo(Shell bindable)
    {
        base.OnAttachedTo(bindable);
        this.shell = bindable;

        // Behaviors do not inherit a binding context on their own, so bindings written on the bar
        // (a badge bound to a view model, say) would silently resolve against nothing.
        this.BindingContext = bindable.BindingContext;
        bindable.BindingContextChanged += this.OnShellBindingContextChanged;

        // Set on the Shell rather than per page: the attached property is inherited down the whole
        // hierarchy, so this kills the native bar once instead of racing each navigation to it.
        Shell.SetTabBarIsVisible(bindable, false);

        bindable.Navigated += this.OnNavigated;
        bindable.PropertyChanged += this.OnShellPropertyChanged;

        // Explicitly, because Bar's default comes from a defaultValueCreator and those never raise
        // propertyChanged - so OnBarChanged has not run and never will for the stock bar.
        this.HookBar(this.Bar);

        this.Refresh();
    }


    /// <inheritdoc/>
    protected override void OnDetachingFrom(Shell bindable)
    {
        bindable.Navigated -= this.OnNavigated;
        bindable.PropertyChanged -= this.OnShellPropertyChanged;
        bindable.BindingContextChanged -= this.OnShellBindingContextChanged;
        bindable.ClearValue(Shell.TabBarIsVisibleProperty);

        this.UnhookBar(this.Bar);
        this.Bar.SetMenuFallbackContexts([]);
        this.UnhookSources();
        this.Detach();

        this.shell = null;
        base.OnDetachingFrom(bindable);
    }


    void OnShellBindingContextChanged(object? sender, EventArgs e)
        => this.BindingContext = this.shell?.BindingContext;


    void OnBarChanged(ShinyTabBar? oldBar, ShinyTabBar? newBar)
    {
        if (oldBar is not null)
        {
            this.UnhookBar(oldBar);
            oldBar.SetMenuFallbackContexts([]);

            // The old one explicitly. propertyChanged runs after the value is set, so this.Bar is
            // already the replacement and detaching that would leave the outgoing bar on the page.
            Detach(oldBar);
            this.attachedPage = null;
        }
        this.HookBar(newBar);
        this.Refresh();
    }


    void HookBar(ShinyTabBar? bar)
    {
        if (bar is not null)
            bar.SelectionChanged += this.OnBarSelectionChanged;
    }


    void UnhookBar(ShinyTabBar? bar)
    {
        if (bar is not null)
            bar.SelectionChanged -= this.OnBarSelectionChanged;
    }


    void OnNavigated(object? sender, ShellNavigatedEventArgs e) => this.Refresh();


    void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Shell.CurrentItem) or nameof(Shell.CurrentState) or nameof(Shell.CurrentPage))
            this.Refresh();
    }


    // ---------------------------------------------------------------------------------------------
    // Mirroring the Shell's structure
    // ---------------------------------------------------------------------------------------------

    IReadOnlyList<BaseShellItem> ResolveSources()
    {
        if (this.shell is not { } shell)
            return [];

        var sections = shell.CurrentItem?.Items;

        return this.TabSource switch
        {
            ShellTabSource.Sections => sections?.Cast<BaseShellItem>().ToList() ?? [],
            ShellTabSource.Items => shell.Items.Cast<BaseShellItem>().ToList(),

            // Auto: sections when the current item has more than one, which is exactly when MAUI
            // would have drawn a bottom bar for them. A single-section item means the tabs the user
            // sees are the Shell's top-level items instead.
            _ => sections is { Count: > 1 }
                ? sections.Cast<BaseShellItem>().ToList()
                : shell.Items.Cast<BaseShellItem>().ToList()
        };
    }


    void SyncTabs()
    {
        var bar = this.Bar;
        var resolved = this.ResolveSources();

        if (this.sources.SequenceEqual(resolved))
        {
            // Same tabs, possibly different chrome. Rebuilding would drop the bar's realized cells
            // (and the selection) for nothing.
            for (var i = 0; i < resolved.Count && i < bar.Items.Count; i++)
                ApplySource(bar.Items[i], resolved[i]);
            return;
        }

        this.UnhookSources();
        this.sources.AddRange(resolved);

        this.suppressSelectionSync = true;
        try
        {
            bar.Items.Clear();
            foreach (var source in resolved)
            {
                source.PropertyChanged += this.OnSourcePropertyChanged;

                var item = new ShinyTabItem { Tag = source };
                ApplySource(item, source);
                bar.Items.Add(item);
            }
        }
        finally
        {
            this.suppressSelectionSync = false;
        }
    }


    void UnhookSources()
    {
        foreach (var source in this.sources)
            source.PropertyChanged -= this.OnSourcePropertyChanged;

        this.sources.Clear();
    }


    void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not BaseShellItem source)
            return;

        var index = this.sources.IndexOf(source);
        if (index >= 0 && index < this.Bar.Items.Count)
            ApplySource(this.Bar.Items[index], source);

        // A Tab switching between its ShellContents (or an item between its sections) changes whose
        // menu the centre button presents, even though the tabs themselves did not change.
        if (e.PropertyName == nameof(ShellItem.CurrentItem))
            this.SyncMenuContexts();
    }


    static void ApplySource(ShinyTabItem item, BaseShellItem source)
    {
        item.Title = ShinyTabs.GetTitle(source) ?? source.Title;
        item.Icon = ShinyTabs.GetIcon(source);
        item.IconImage = source.Icon;
        item.Badge = ShinyTabs.GetBadge(source);
        item.BadgeColor = ShinyTabs.GetBadgeColor(source);
        item.Route = source.Route;
        item.IsVisible = source.IsVisible;
        item.IsEnabled = source.IsEnabled;
        item.Tag = source;
    }


    // ---------------------------------------------------------------------------------------------
    // Docking + selection
    // ---------------------------------------------------------------------------------------------

    void Refresh()
    {
        if (this.shell is not { } shell)
            return;

        this.SyncTabs();

        // Ahead of the page checks: the shell elements are known before the page exists, and a menu
        // declared on them has to count from the first frame.
        this.SyncMenuContexts();

        var page = shell.CurrentPage is { } current ? PageOverlay.LeafPage(current) : null;
        if (page is null)
        {
            this.Detach();
            return;
        }

        var visible = ShinyTabs.GetIsTabBarVisible(page) && !(this.HideOnPush && IsPushed(shell));
        if (!visible)
        {
            this.Detach();
            return;
        }

        var moved = !ReferenceEquals(this.attachedPage, page);
        this.Attach(page);

        var selected = this.CurrentSource();
        var changed = !ReferenceEquals(selected, this.lastSelectedSource);
        this.lastSelectedSource = selected;

        var index = selected is null ? -1 : this.sources.IndexOf(selected);
        if (index >= 0 && index != this.Bar.SelectedIndex)
        {
            this.suppressSelectionSync = true;
            try
            {
                this.Bar.SelectedIndex = index;
            }
            finally
            {
                this.suppressSelectionSync = false;
            }
        }

        this.Bar.PageContext = page;

        if (moved && changed)
            this.AnimateIn(page);
    }


    /// <summary>
    /// Hands the bar the Shell elements behind the current page - its <c>ShellContent</c>, <c>Tab</c>
    /// and <c>ShellItem</c>, in that order - as the fallbacks for a menu the page does not declare.
    /// </summary>
    /// <remarks>
    /// Without this only the page's own <see cref="ShinyTabs.ActionsProperty"/> were ever read, so
    /// rows declared on a <c>ShellContent</c> - which the docs have always said works - were silently
    /// ignored and the centre button dimmed as if the tab had nothing to present.
    /// </remarks>
    void SyncMenuContexts()
    {
        if (this.shell is not { } shell)
            return;

        var item = shell.CurrentItem;
        var section = item?.CurrentItem;
        var content = section?.CurrentItem;

        var contexts = new List<BindableObject>(3);
        if (content is not null)
            contexts.Add(content);
        if (section is not null)
            contexts.Add(section);
        if (item is not null)
            contexts.Add(item);

        this.Bar.SetMenuFallbackContexts(contexts);
    }


    /// <summary>True when the current tab has pushed something on top of its root page.</summary>
    static bool IsPushed(Shell shell)
        => shell.CurrentItem?.CurrentItem?.Navigation?.NavigationStack.Count > 1;


    BaseShellItem? CurrentSource()
    {
        if (this.shell is not { } shell)
            return null;

        // Whichever level the tabs came from is the level the selection has to be read at. Reading
        // the wrong one gives -1 on every navigation and the bar never highlights anything.
        var section = (BaseShellItem?)shell.CurrentItem?.CurrentItem;
        var item = (BaseShellItem?)shell.CurrentItem;

        if (section is not null && this.sources.Contains(section))
            return section;

        return item is not null && this.sources.Contains(item) ? item : null;
    }


    void Attach(ContentPage page)
    {
        var bar = this.Bar;

        // The ContentPage overload rather than the Element one: it cannot fail, so there is no null
        // to explain away at the one call site that would have to handle it.
        var root = PageOverlay.GetOrCreateRoot(page);
        var layer = PageOverlay.GetOrCreateLayer<PageOverlay.TabBarLayer>(root, PageOverlay.Layers.TabBar);

        if (ReferenceEquals(bar.Parent, layer))
        {
            this.attachedPage = page;
            return;
        }

        // One bar instance follows the navigation rather than one per page: the selection, the open
        // menu and any in-flight animation live on it, and cloning it per page would reset all three
        // on every tab change.
        this.Detach();

        layer.Children.Add(bar);
        this.attachedPage = page;
    }


    void Detach()
    {
        Detach(this.Bar);
        this.attachedPage = null;
    }


    static void Detach(ShinyTabBar? bar)
    {
        if (bar?.Parent is Layout parent)
            parent.Children.Remove(bar);
    }


    void OnBarSelectionChanged(object? sender, TabSelectionChangedEventArgs e)
    {
        if (this.suppressSelectionSync || this.shell is not { } shell)
            return;

        switch (e.NewItem?.Tag)
        {
            // Assigning CurrentItem is what Shell's own bar does, so routes, the tab's saved
            // navigation stack and Navigated all behave exactly as they would have.
            case ShellSection section when shell.CurrentItem is { } item:
                item.CurrentItem = section;
                break;

            case ShellItem shellItem:
                shell.CurrentItem = shellItem;
                break;
        }

        // Navigated follows on a device and refreshes everything, but the bar has already re-decided
        // the centre button for the new tab by now - against the previous tab's ShellContent.
        this.SyncMenuContexts();
    }


    void AnimateIn(ContentPage page)
    {
        var duration = this.TransitionDuration;
        if (this.Transition == StateTransition.None || duration == 0)
            return;

        if (page.Content is not PageOverlay.ShinyOverlayRoot root || PageOverlay.ContentOf(root) is not { } content)
            return;

        // No handler means the page is not on screen yet, and in a headless host there is no
        // animation manager at all - so there is nothing to animate towards.
        if (content.Handler is null)
            return;

        var width = page.Width > 0 ? page.Width : 320d;
        var height = page.Height > 0 ? page.Height : 480d;

        var (dx, dy, scale) = this.Transition switch
        {
            StateTransition.Slide or StateTransition.SlideLeft => (width, 0d, 1d),
            StateTransition.SlideRight => (-width, 0d, 1d),
            StateTransition.SlideUp => (0d, height, 1d),
            StateTransition.SlideDown => (0d, -height, 1d),
            StateTransition.Scale => (0d, 0d, 0.94d),
            _ => (0d, 0d, 1d)
        };

        content.Opacity = 0;
        content.TranslationX = dx;
        content.TranslationY = dy;
        content.Scale = scale;

        _ = RunAsync();

        async Task RunAsync()
        {
            try
            {
                var animations = new List<Task> { content.FadeToAsync(1, duration, Easing.CubicOut) };

                if (dx != 0 || dy != 0)
                    animations.Add(content.TranslateToAsync(0, 0, duration, Easing.CubicOut));

                if (scale != 1d)
                    animations.Add(content.ScaleToAsync(1, duration, Easing.CubicOut));

                await Task.WhenAll(animations).ConfigureAwait(true);
            }
            catch (Exception)
            {
                // Navigated away mid-flight. The reset below still runs, so the page is never left
                // half-transitioned if it comes back.
            }

            content.Opacity = 1;
            content.TranslationX = 0;
            content.TranslationY = 0;
            content.Scale = 1;
        }
    }
}

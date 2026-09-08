using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

/// <summary>
/// A tabbed page built out of a <see cref="ShinyTabBar"/> and a transitioning content host, rather
/// than out of each platform's native tab controller.
/// </summary>
/// <remarks>
/// <para>What it keeps from MAUI's <c>TabbedPage</c>: tabs whose content is built the first time
/// they are selected and then kept, page lifecycle for content declared as a <see cref="ContentPage"/>,
/// and a title that follows the selection.</para>
/// <para>What it adds: animated icons, badges, an animated transition between tabs, a raised centre
/// button that presents the current page's actions, and a bar you can restyle without a handler.
/// Because none of it is native, it also renders on the heads MAUI's <c>TabbedPage</c> does not
/// reach — AppKit and GTK4.</para>
/// <para>What it is not: a navigation host. Each tab is one screen. To push inside a tab, put a
/// <c>NavigationPage</c>-shaped flow in your own content, or use a <see cref="Shell"/> with
/// <see cref="ShinyTabBarBehavior"/>, which is the same bar over real Shell navigation.</para>
/// </remarks>
/// <example>
/// <code language="xaml">
/// &lt;shiny:ShinyTabbedPage Transition="Slide"&gt;
///     &lt;shiny:ShinyTabItem Title="Home" Icon="home"&gt;
///         &lt;shiny:ShinyTabItem.ContentTemplate&gt;
///             &lt;DataTemplate&gt;&lt;local:HomePage /&gt;&lt;/DataTemplate&gt;
///         &lt;/shiny:ShinyTabItem.ContentTemplate&gt;
///     &lt;/shiny:ShinyTabItem&gt;
/// &lt;/shiny:ShinyTabbedPage&gt;
/// </code>
/// </example>
[ContentProperty(nameof(Tabs))]
public partial class ShinyTabbedPage : ContentPage, ShinyTabBar.ITabMenuHost
{
    readonly ObservableCollection<ShinyTabItem> tabs = new();
    readonly Grid rootGrid;
    readonly StateView contentHost;
    readonly ShinyTabBar tabBar;
    readonly Grid menuLayer;

    bool suppressSelectionSync;
    bool tabAppeared;
    ShinyTabItem? appearedTab;

    /// <summary>Creates the page.</summary>
    public ShinyTabbedPage()
    {
        this.contentHost = new StateView
        {
            // Direction-aware by default: a tab later in the list enters from the right and an
            // earlier one from the left, which is the only cue that says which way you just moved.
            Transition = StateTransition.Slide,
            TransitionDuration = 220
        };

        this.tabBar = new ShinyTabBar();
        this.tabBar.SelectionChanged += this.OnBarSelectionChanged;
        this.tabBar.TabReselected += (_, e) => this.TabReselected?.Invoke(this, e);

        // Between the content and the bar: the centre menu dims the page it belongs to, but the
        // button that opened it has to stay visible above its own backdrop or the rotate-to-close
        // affordance is hidden by the very thing it closes.
        this.menuLayer = new Grid
        {
            InputTransparent = true,
            CascadeInputTransparent = false,
            ZIndex = 10
        };
        this.tabBar.ZIndex = 20;

        this.rootGrid = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) },

            // Edge-to-edge. A Grid defaults to SafeAreaRegions.Container and it insets the area its
            // children are arranged into, so the first layout in the chain that insets is the one
            // that wins - and left at the default this one hands the bar a row that stops short of
            // the bottom of the screen, leaving a strip of page under it whatever the bar does with
            // its own inset. The bar takes the bottom inset inside its own background instead; the
            // tab content in row 0 keeps MAUI's default for its type and still insets itself.
            SafeAreaEdges = SafeAreaEdges.None
        };
        Grid.SetRow(this.contentHost, 0);
        Grid.SetRow(this.tabBar, 1);
        Grid.SetRow(this.menuLayer, 0);
        Grid.SetRowSpan(this.menuLayer, 2);

        this.rootGrid.Children.Add(this.contentHost);
        this.rootGrid.Children.Add(this.menuLayer);
        this.rootGrid.Children.Add(this.tabBar);

        this.ApplyBarPlacement();
        this.tabBar.BarStyleChanged += (_, _) => this.ApplyBarPlacement();

        this.tabs.CollectionChanged += this.OnTabsChanged;

        // Assigned through the base property on purpose. Hiding Content would not actually stop a
        // consumer setting it - an inaccessible member is skipped by C# lookup and the base one
        // found anyway - so the honest answer is that the tabs are the page's content and setting
        // Content replaces them.
        this.Content = this.rootGrid;

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(ShinyTabbedPage));
    }


    /// <summary>
    /// Decides whether the content stops above the bar or runs the full height of the page under it.
    /// </summary>
    /// <remarks>
    /// A floating bar is a capsule laid over the page, so content running underneath it is not a
    /// separate opt-in - it is what floating means. <see cref="ContentBehindTabBar"/> stays a
    /// property of its own because a docked bar can want the same thing (a translucent one over a
    /// photo, say), so the two are OR-ed rather than one driving the other.
    /// </remarks>
    internal void ApplyBarPlacement()
    {
        var behind = this.ContentBehindTabBar || this.tabBar.BarStyle == TabBarStyle.Floating;
        Grid.SetRowSpan(this.contentHost, behind ? 2 : 1);
    }


    /// <summary>The tabs, in bar order. The content property, so they can be listed inline in XAML.</summary>
    public IList<ShinyTabItem> Tabs => this.tabs;

    /// <summary>
    /// The bar itself, for the styling the pass-through properties do not cover — and for wiring
    /// <see cref="ShinyTabBar.CenterClicked"/> or <see cref="ShinyTabBar.ActionInvoked"/> in
    /// code-behind.
    /// </summary>
    public ShinyTabBar TabBar => this.tabBar;

    /// <summary>The content host, for the transition properties the page does not pass through.</summary>
    public StateView ContentHost => this.contentHost;

    /// <summary>Raised after the selected tab changes.</summary>
    public event EventHandler<TabSelectionChangedEventArgs>? SelectionChanged;

    /// <summary>Raised when the already-selected tab is tapped again.</summary>
    public event EventHandler<TabReselectedEventArgs>? TabReselected;

    Layout ShinyTabBar.ITabMenuHost.GetTabMenuLayer() => this.menuLayer;


    /// <summary>Selects the tab at <paramref name="index"/>. Returns false when out of range, hidden or disabled.</summary>
    public bool GoTo(int index) => this.tabBar.GoTo(index);

    /// <summary>Selects the tab with this <see cref="ShinyTabItem.Route"/>. Returns false when there is no such tab.</summary>
    public bool GoTo(string route) => this.tabBar.GoTo(route);


    // ---------------------------------------------------------------------------------------------
    // Tab collection
    // ---------------------------------------------------------------------------------------------

    void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // The bar and the content host each need their own copy of the list, so one collection is
        // mirrored into both rather than either of them owning it. Rebuilt wholesale on any change:
        // the lists are a handful of items long and an incremental sync of two collections against a
        // third is where ordering bugs live.
        StyleGuard.WhenReady<ShinyTabbedPage>(this, page => page.SyncTabs());
    }


    void SyncTabs()
    {
        foreach (var tab in this.tabs)
            tab.Host = this;

        this.suppressSelectionSync = true;
        try
        {
            var selected = this.tabBar.SelectedItem;

            this.tabBar.Items.Clear();
            foreach (var tab in this.tabs)
                this.tabBar.Items.Add(tab);

            this.contentHost.States.Clear();
            foreach (var tab in this.tabs)
                this.contentHost.States.Add(tab);

            // Rebuilding the bar's list resets its selection; put the caller's back if it survived.
            if (selected is not null && this.tabs.Contains(selected))
                this.tabBar.SelectedItem = selected;
        }
        finally
        {
            this.suppressSelectionSync = false;
        }

        this.ShowSelectedTab(animate: false);
    }


    // ---------------------------------------------------------------------------------------------
    // Selection
    // ---------------------------------------------------------------------------------------------

    void OnBarSelectionChanged(object? sender, TabSelectionChangedEventArgs e)
    {
        if (this.suppressSelectionSync)
            return;

        this.ShowSelectedTab(animate: true);
        this.SelectionChanged?.Invoke(this, e);
    }


    /// <summary>
    /// Brings the bar's selection through to everything downstream of it: the content host (which is
    /// where a tab is first built), the page's own two selection properties, the bar's page context,
    /// lifecycle, and the title.
    /// </summary>
    void ShowSelectedTab(bool animate)
    {
        var item = this.tabBar.SelectedItem;

        var transition = this.contentHost.Transition;
        if (!animate)
            this.contentHost.Transition = StateTransition.None;

        // Realizes the tab's content if this is its first time on screen - the lazy half of the
        // contract, and the reason PageContext is only read afterwards.
        this.contentHost.CurrentState = item?.Name;

        if (!animate)
            this.contentHost.Transition = transition;

        this.suppressSelectionSync = true;
        try
        {
            this.SelectedIndex = this.tabBar.SelectedIndex;
            this.SelectedItem = item;
        }
        finally
        {
            this.suppressSelectionSync = false;
        }

        this.EnterTab(item);

        this.tabBar.PageContext = item?.PageContext;

        if (this.SyncTitleWithTab && !String.IsNullOrEmpty(item?.Title))
            this.Title = item.Title;
    }


    /// <summary>
    /// Moves the <see cref="ITabAware"/> notifications onto <paramref name="item"/>: the tab being
    /// left is told first, then the tab being entered.
    /// </summary>
    /// <remarks>
    /// Entering announces immediately rather than waiting for the page's own <c>OnAppearing</c>.
    /// For the first tab that means the callback lands while the page is still being built, which is
    /// both what a view model wants (start loading) and the only behaviour that is the same on every
    /// head — MAUI raises page lifecycle from the platform, so there are hosts and moments where it
    /// never comes. The <c>tabAppeared</c> flag is what stops the page's own appearing from
    /// announcing a second time.
    /// </remarks>
    void EnterTab(ShinyTabItem? item)
    {
        if (ReferenceEquals(this.appearedTab, item) && this.tabAppeared)
            return;

        if (this.tabAppeared)
            this.appearedTab?.NotifyDisappearing();

        this.appearedTab = item;
        this.tabAppeared = item is not null;

        if (this.tabAppeared)
            item!.NotifyAppearing();
    }


    /// <summary>
    /// The page-level half of the lifecycle, kept separate from MAUI's <c>OnAppearing</c> so it can
    /// be driven directly. MAUI raises page lifecycle from the platform — a page with no handler
    /// never appears, however hard it is asked — so this is the only way a test host reaches it.
    /// </summary>
    internal void SendLifecycle(bool appearing)
    {
        if (appearing)
        {
            // The page can come back long after the tab was chosen - a push and a pop - and the tab
            // needs to hear about that. It has already been told if it never stopped being current.
            if (this.tabAppeared || this.appearedTab is null)
                return;

            this.tabAppeared = true;
            this.appearedTab.NotifyAppearing();
        }
        else
        {
            if (!this.tabAppeared)
                return;

            this.tabAppeared = false;
            this.appearedTab?.NotifyDisappearing();
        }
    }


    /// <inheritdoc/>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        this.SendLifecycle(appearing: true);
    }


    /// <inheritdoc/>
    protected override void OnDisappearing()
    {
        this.SendLifecycle(appearing: false);
        base.OnDisappearing();
    }
}

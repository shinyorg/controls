using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

/// <summary>
/// A <see cref="NavigationPage"/> whose bar has items on <b>both</b> sides of the title.
/// </summary>
/// <remarks>
/// <para>It <em>is</em> a <see cref="NavigationPage"/>, not a replacement for one, so everything the
/// stock page does still works and works the same way: <c>Navigation.PushAsync</c>/<c>PopAsync</c>/
/// <c>PopToRootAsync</c>/<c>InsertPageBefore</c>/<c>RemovePage</c>, the modal stack, page lifecycle,
/// Android's hardware back button, <c>Pushed</c>/<c>Popped</c>/<c>PoppedToRoot</c>, and every
/// attached property MAUI already gives it — <see cref="NavigationPage.SetHasNavigationBar(BindableObject, bool)"/>,
/// <see cref="NavigationPage.SetHasBackButton(Page, bool)"/>,
/// <see cref="NavigationPage.SetBackButtonTitle(BindableObject, string)"/>,
/// <see cref="NavigationPage.SetTitleView(BindableObject, View)"/>,
/// <see cref="NavigationPage.SetTitleIconImageSource(BindableObject, ImageSource)"/>,
/// <see cref="NavigationPage.SetIconColor(BindableObject, Color)"/>, and the <c>Bar*</c> colours.
/// A page's own <see cref="Page.ToolbarItems"/> render unchanged.</para>
/// <para><b>What is different:</b> the bar itself is drawn by <see cref="ShinyNavBar"/> rather than
/// handed to the platform, because no platform's native bar has a left slot to give you — that side
/// belongs to the back button on all of them, and AppKit and GTK4 have no bar at all. The native bar
/// is hidden and the drawn one takes its place, which is also what makes the overflow menu, the
/// badges, the motion icons and the collapsing large title possible on every head.</para>
/// <para>Pages that are not a <see cref="ContentPage"/> are left entirely alone — their own chrome is
/// not something to wrap.</para>
/// </remarks>
/// <example>
/// <code language="xaml">
/// &lt;shiny:ShinyNavigationPage xmlns:shiny="http://shiny.net/maui/controls"
///                            LargeTitleDisplay="Collapsing"&gt;
///     &lt;x:Arguments&gt;
///         &lt;local:InboxPage /&gt;
///     &lt;/x:Arguments&gt;
/// &lt;/shiny:ShinyNavigationPage&gt;
/// </code>
/// <code language="xaml">
/// &lt;!-- and on the page itself --&gt;
/// &lt;ContentPage Title="Inbox" shiny:ShinyNav.Subtitle="12 unread"&gt;
///     &lt;shiny:ShinyNav.LeftItems&gt;
///         &lt;shiny:NavBarItem Icon="menu" Command="{Binding OpenDrawerCommand}" /&gt;
///     &lt;/shiny:ShinyNav.LeftItems&gt;
///     &lt;shiny:ShinyNav.RightItems&gt;
///         &lt;shiny:NavBarItem Icon="search" Command="{Binding SearchCommand}" /&gt;
///         &lt;shiny:NavBarItem Icon="bell" Badge="3" Command="{Binding AlertsCommand}" /&gt;
///         &lt;shiny:NavBarItem Text="Settings" Order="Secondary" Command="{Binding SettingsCommand}" /&gt;
///     &lt;/shiny:ShinyNav.RightItems&gt;
/// &lt;/ContentPage&gt;
/// </code>
/// </example>
public partial class ShinyNavigationPage : NavigationPage
{
    static readonly ConditionalWeakTable<Page, ShinyNavBar> bars = new();

    readonly ConditionalWeakTable<ContentPage, Install> installs = new();

    /// <summary>Creates the page with no root. Push one before showing it, as with any navigation page.</summary>
    public ShinyNavigationPage() => this.Init();

    /// <summary>Creates the page and pushes <paramref name="root"/>.</summary>
    public ShinyNavigationPage(Page root) : base(root) => this.Init();


    /// <summary>Everything installed over one page, kept together so it can all be undone at once.</summary>
    sealed class Install
    {
        public required ShinyNavBar Bar { get; init; }
        public required NavHost Host { get; init; }
        public required PropertyChangedEventHandler PageChanged { get; init; }
        public required NotifyCollectionChangedEventHandler ToolbarChanged { get; init; }

        /// <summary>
        /// What the page asked for with <see cref="NavigationPage.SetHasNavigationBar(BindableObject, bool)"/>.
        /// The native bar is forced off to make room for the drawn one, so the page's own answer has
        /// to be remembered here or hiding the bar for one page would become impossible.
        /// </summary>
        public bool WantsBar { get; set; } = true;

    }


    /// <summary>The grid a page's content is wrapped in: the bar in row 0, the content in row 1.</summary>
    /// <remarks>
    /// Edge-to-edge, unlike every other layout: a <see cref="Grid"/> defaults to
    /// <see cref="SafeAreaRegions.Container"/>, which would inset the host and so stop the bar's
    /// background short of the status bar - leaving a strip of page above a bar that is supposed to
    /// run to the top of the screen. The bar takes the top inset itself, on its background surface,
    /// so the colour reaches the top edge while the title and items do not. The page's own content
    /// in row 1 keeps whatever MAUI's default for its type is, so a layout still insets itself out
    /// of the home indicator without this having to reach into it.
    /// </remarks>
    internal sealed class NavHost : Grid
    {
        public NavHost() => this.SafeAreaEdges = SafeAreaEdges.None;
    }


    void Init()
    {
        // OnChildAdded is the earliest a pushed page is reachable - before the platform has built a
        // thing for it, so wrapping its content costs nothing. The rest are belt and braces: a page
        // inserted with InsertPageBefore, and any host that changes CurrentPage without a push.
        this.Pushed += (_, e) => this.Sync(e.Page);
        this.Popped += (_, _) => this.SyncCurrent();
        this.PoppedToRoot += (_, _) => this.SyncCurrent();
        this.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(this.CurrentPage))
                this.SyncCurrent();
        };

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(ShinyNavigationPage));

        this.SyncCurrent();
    }


    /// <inheritdoc/>
    protected override void OnChildAdded(Element child)
    {
        base.OnChildAdded(child);

        // A root passed to the constructor is pushed by the base constructor, which runs before this
        // type's own is finished - so the install is queued and replayed by MarkReady above.
        if (child is Page page)
            StyleGuard.WhenReady<ShinyNavigationPage>(this, self => self.Sync(page));
    }


    /// <inheritdoc/>
    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        // BarBackground, BarBackgroundColor and BarTextColor are MAUI's own properties on this type,
        // so there is no propertyChanged callback of ours to hang the refresh on - and they are the
        // three most likely to be set from a style or a theme swap rather than in the constructor.
        if (propertyName is nameof(BarBackground) or nameof(BarBackgroundColor) or nameof(BarTextColor))
            StyleGuard.WhenReady<ShinyNavigationPage>(this, self => self.RefreshAll());
    }


    /// <inheritdoc/>
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        this.ApplySwipeBack();
    }


    /// <inheritdoc/>
    protected override void OnAppearing()
    {
        base.OnAppearing();

        // The status bar is global, so whatever was showing before this page owned it last. Coming
        // back from a modal, or forward onto one, is the moment to claim it again - a Refresh alone
        // does not fire on a page whose properties have not changed.
        if (this.CurrentPage is ContentPage current)
            this.SyncStatusBar(current);
    }


    /// <summary>
    /// Platform polish that only exists on one head. Implemented for iOS in
    /// <c>Platforms/iOS/ShinyNavigationPage.iOS.cs</c>; compiled away everywhere else.
    /// </summary>
    partial void ApplySwipeBack();


    /// <summary>
    /// Hands the resolved status bar appearance to the platform. Implemented for iOS and Android;
    /// compiled away on Windows, GTK4 and macOS AppKit, none of which has a status bar.
    /// </summary>
    /// <param name="background">
    /// What is painted behind the clock, or null when the bar cannot be reduced to one colour.
    /// Android below 15 fills the status bar with it; everywhere else it is only what
    /// <see cref="Shiny.Maui.Controls.StatusBarStyle.Auto"/> is derived from.
    /// </param>
    /// <param name="style">Already resolved — never <c>Inherit</c>, and never <c>Auto</c>.</param>
    partial void ApplyStatusBar(Color? background, StatusBarStyle style);


    /// <summary>
    /// Works out what the status bar should look like over <paramref name="page"/> and applies it.
    /// </summary>
    /// <remarks>
    /// Only for the page that is showing. Refreshing the whole stack is how every other setting is
    /// pushed - see <see cref="RefreshAll"/> - and doing the same here would leave the status bar
    /// wearing the colours of whichever page happened to be walked last.
    /// </remarks>
    internal void SyncStatusBar(ContentPage page)
    {
        if (!ReferenceEquals(this.CurrentPage, page))
            return;

        var style = this.ResolvedStatusBarStyle(page);
        if (style == StatusBarStyle.None)
            return;

        this.ApplyStatusBar(this.StatusBarColorFor(page), style);
    }


    /// <summary>
    /// What the status bar should actually be set to over <paramref name="page"/> — the page's answer
    /// over this one's, with <see cref="StatusBarStyle.Auto"/> already resolved against the bar.
    /// </summary>
    /// <remarks>
    /// <see cref="StatusBarStyle.None"/> comes back for "leave it alone", which covers both an
    /// explicit <c>None</c> and an <c>Auto</c> with nothing to derive from — a bar painted with an
    /// image brush, or a theme token that has not resolved yet. Guessing in that case is how a light
    /// bar ends up with a white clock on it.
    /// </remarks>
    internal StatusBarStyle ResolvedStatusBarStyle(ContentPage page)
    {
        var declared = ShinyNav.GetStatusBarStyle(page);
        var style = declared == StatusBarStyle.Inherit ? this.StatusBarStyle : declared;

        if (style != StatusBarStyle.Auto)
            return style;

        return this.StatusBarColorFor(page) switch
        {
            null => StatusBarStyle.None,
            { } background => IsDark(background) ? StatusBarStyle.LightContent : StatusBarStyle.DarkContent
        };
    }


    /// <summary>What is painted behind the clock: the pinned colour, the page's, then the bar's.</summary>
    Color? StatusBarColorFor(ContentPage page)
        => this.StatusBarColor
        ?? ShinyNav.GetBarBackgroundColor(page)
        ?? GetNavBar(page)?.EffectiveBarColor;


    /// <summary>
    /// Whether white text reads better on <paramref name="color"/> than black does.
    /// </summary>
    /// <remarks>
    /// Relative luminance, not <c>Color.GetLuminosity</c>: HSL lightness calls pure blue and pure
    /// yellow equally light, and the status bar over an indigo bar is exactly the case that gets
    /// wrong. Weighted the way WCAG weights the channels, against the 0.5 midpoint rather than the
    /// contrast ratio - there are only two answers to pick between.
    /// </remarks>
    internal static bool IsDark(Color color)
        => (0.2126 * color.Red) + (0.7152 * color.Green) + (0.0722 * color.Blue) < 0.5;


    /// <inheritdoc/>
    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        this.SyncCurrent();
    }


    /// <summary>The bar drawn over <paramref name="page"/>, or null if none was installed.</summary>
    /// <remarks>
    /// Everything the bar shows is derived from the page, so reach for this only for what the
    /// attached properties do not cover — wiring <see cref="ShinyNavBar.ItemInvoked"/> in code-behind,
    /// or opening the overflow menu yourself.
    /// </remarks>
    public static ShinyNavBar? GetNavBar(Page page) => bars.TryGetValue(page, out var bar) ? bar : null;


    /// <summary>Test seam: the grid a page's content was wrapped in, or null when none was.</summary>
    internal NavHost? HostFor(ContentPage page) => this.installs.TryGetValue(page, out var install) ? install.Host : null;

    /// <summary>Test seam: whether the page still wants a bar at all.</summary>
    internal bool WantsBar(ContentPage page) => this.installs.TryGetValue(page, out var install) && install.WantsBar;


    /// <summary>The bar over the page that is showing, or null.</summary>
    public ShinyNavBar? CurrentNavBar => this.CurrentPage is null ? null : GetNavBar(this.CurrentPage);


    /// <summary>Raised when an item on any of this page's bars is tapped.</summary>
    public event EventHandler<NavBarItemEventArgs>? NavBarItemInvoked;


    void SyncCurrent()
    {
        // Re-tried on every navigation, not only when the handler arrives: the iOS controller often
        // does not exist yet at the point this page first gets one.
        this.ApplySwipeBack();

        if (this.CurrentPage is { } page)
            this.Sync(page);

        // A pop leaves the page underneath showing, and its back button may have been the only thing
        // that changed - it is the root now. Refresh every installed page rather than tracking which.
        foreach (var stacked in this.Navigation.NavigationStack.OfType<ContentPage>())
        {
            if (this.installs.TryGetValue(stacked, out _))
                this.Refresh(stacked);
        }
    }


    void Sync(Page page)
    {
        // Only a ContentPage. A TabbedPage or a FlyoutPage pushed onto the stack brings its own
        // chrome and its content is not one view to wrap.
        if (page is not ContentPage content)
            return;

        if (this.installs.TryGetValue(content, out _))
        {
            this.Refresh(content);
            return;
        }

        this.InstallOn(content);
    }


    void InstallOn(ContentPage page)
    {
        // Declaring the bar's attached properties in markup puts them ahead of the page's content, and
        // XAML applies properties in document order - so at this point the content often does not
        // exist yet. Wrapping nothing and being overwritten a line later is the failure that makes.
        if (ContentOf(page) is null)
        {
            this.DeferUntilContent(page);
            return;
        }

        var bar = new ShinyNavBar();
        var host = new NavHost
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }
        };

        var existing = ContentOf(page)!;
        SetContentOf(page, null);

        Grid.SetRow(bar, 0);
        Grid.SetRow(existing, 1);
        host.Children.Add(existing);
        host.Children.Add(bar);
        SetContentOf(page, host);

        // The page handler needs the install and the install needs the handler, so the variable is
        // declared first and closed over - it is assigned before anything can raise the event.
        Install install = null!;
        var pageChanged = new PropertyChangedEventHandler((_, e) => this.OnPageChanged(page, install, e));
        var toolbarChanged = new NotifyCollectionChangedEventHandler((_, _) => this.Refresh(page));

        install = new Install
        {
            Bar = bar,
            Host = host,
            PageChanged = pageChanged,
            ToolbarChanged = toolbarChanged,
            WantsBar = NavigationPage.GetHasNavigationBar(page)
        };

        this.installs.Add(page, install);
        bars.Add(page, bar);

        // The native bar has to go: it is what would otherwise draw the title and the right-hand
        // toolbar items a second time, above the drawn one. Done before subscribing below, so this
        // write is not mistaken for the page talking.
        NavigationPage.SetHasNavigationBar(page, false);

        page.PropertyChanged += pageChanged;

        // Page.ToolbarItems is typed IList<T>; the instance behind it is observable, which is how the
        // native bar keeps up. Asked for rather than assumed, so a future change of backing type
        // costs a stale toolbar rather than a cast exception on every page.
        if (page.ToolbarItems is INotifyCollectionChanged observable)
            observable.CollectionChanged += toolbarChanged;

        ShinyNav.GetLeftItems(page).CollectionChanged += toolbarChanged;
        ShinyNav.GetRightItems(page).CollectionChanged += toolbarChanged;

        // Installed now rather than when the overflow menu first opens. The root is created by
        // re-parenting the page's content, which rebuilds every native view underneath it - fine
        // here, before the page has been rendered at all, and a visible hitch if it happened on the
        // tap that opens a menu.
        PageOverlay.GetOrCreateRoot(page);

        bar.BackAction = () => _ = this.PopAsync();
        bar.ItemInvoked += (_, e) => this.NavBarItemInvoked?.Invoke(this, e);

        // Auto is derived from the bar's background, and a theme token only becomes a colour once the
        // dictionary has merged - which is after every Refresh this install will run.
        bar.EffectiveBarColorChanged += (_, _) => this.SyncStatusBar(page);

        this.Refresh(page);
    }


    void OnPageChanged(ContentPage page, Install install, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "HasNavigationBar")
        {
            // Only one of the two transitions can ever be the page talking. It cannot set false: the
            // property already holds the false written at install time, so that write raises nothing.
            // A false arriving here is therefore always this class's own write coming back, and is
            // ignored - which is also why ShinyNav.IsNavBarVisible exists as the runtime switch.
            //
            // Deliberately no re-entrancy flag. MAUI defers a SetValue made for the property it is
            // already notifying, so the write below lands after this method has returned and any
            // guard released - a guard here would be reset before the write it was guarding arrived,
            // and the bar would hide itself the moment a page asked for it.
            if (!NavigationPage.GetHasNavigationBar(page))
                return;

            install.WantsBar = true;
            NavigationPage.SetHasNavigationBar(page, false);
        }

        this.Refresh(page);
    }


    readonly ConditionalWeakTable<ContentPage, PropertyChangedEventHandler> awaitingContent = new();

    void DeferUntilContent(ContentPage page)
    {
        if (this.awaitingContent.TryGetValue(page, out _))
            return;

        PropertyChangedEventHandler handler = null!;
        handler = (_, e) =>
        {
            if (e.PropertyName is not (nameof(ContentPage.Content) or nameof(ShinyContentPage.PageContent)))
                return;

            if (ContentOf(page) is null)
                return;

            page.PropertyChanged -= handler;
            this.awaitingContent.Remove(page);
            this.InstallOn(page);
        };

        this.awaitingContent.Add(page, handler);
        page.PropertyChanged += handler;
    }


    /// <summary>
    /// A <see cref="ShinyContentPage"/>'s body is its <c>PageContent</c>, not its <c>Content</c> - the
    /// page's own root grid carries the overlay host, and wrapping that instead would put the nav bar
    /// underneath every toast, dialog and floating panel on the page.
    /// </summary>
    static View? ContentOf(ContentPage page) => page is ShinyContentPage shiny ? shiny.PageContent : page.Content;

    static void SetContentOf(ContentPage page, View? view)
    {
        if (page is ShinyContentPage shiny)
            shiny.PageContent = view;
        else
            page.Content = view;
    }
}

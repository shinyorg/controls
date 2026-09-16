using System.ComponentModel;
using Shiny.Maui.Controls.FloatingPanel;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.QuickEntry;

/// <summary>
/// Presents the quick entry popup as an overlay on the current page. The only presentation
/// available on iOS, Android and Mac Catalyst, and an option on desktop for a popup that should stay
/// inside the app rather than float over the whole machine.
/// </summary>
/// <remarks>
/// <para>
/// Built on the library's own <see cref="Overlay"/> control rather than a hand-rolled scrim. That
/// brings the backdrop, the optional blur, close-on-backdrop-tap and the show/hide worker that
/// already survives a rapid open→close flip — none of which is worth reimplementing, and all of
/// which is already proven on iOS and Android.
/// </para>
/// <para>
/// It also means the popup shares the page's <see cref="OverlayHost"/> backdrop with everything else
/// that uses one, so a quick entry opened over a floating panel dims the page once rather than
/// twice. When the page has no host — a plain <c>ContentPage</c> — one is installed into the shared
/// page overlay so the control still works with no cooperation from the app.
/// </para>
/// <para>
/// Unlike the desktop presenter this does not have to size a window to its content: the overlay is
/// laid out by the page, so the content sizes itself and <see cref="QuickEntryOptions.MaxHeight"/>
/// is a maximum-height request. That is why none of the measuring machinery
/// <see cref="IQuickEntryAutoSize"/> exists for is needed here.
/// </para>
/// </remarks>
sealed class InAppQuickEntryPresenter : IQuickEntryPresenter
{
    // The popup never runs closer than this to either edge of the host when the requested width
    // does not fit — a phone is narrower than any sensible desktop popup width.
    const double SideMargin = 16d;

    // Hide-animation grace: past this the overlay is let go whether or not its fade reported in. A
    // page that has been navigated away from stops ticking animations, and a fade that never ends
    // would otherwise hold the page for the life of the app.
    static readonly TimeSpan DetachGrace = TimeSpan.FromMilliseconds(250);

    readonly Func<ContentPage?> currentPage;

    // Everything below points into the page the popup was last shown on. It is all released once the
    // popup has finished hiding: this presenter is an app-lifetime singleton, and anything it still
    // referenced would keep that page - and every view under it - alive until the next show.
    ContentPage? page;
    OverlayHost? host;
    Overlay? overlay;
    ContentView? contentHost;
    View? content;
    bool opened;
    int hideGeneration;

    public InAppQuickEntryPresenter() : this(PageOverlay.CurrentPage) { }

    /// <summary>Test seam: the page the popup draws on, in place of the application's current window.</summary>
    internal InAppQuickEntryPresenter(Func<ContentPage?> currentPage)
        => this.currentPage = currentPage;

    // The last layout arguments, replayed when the host finally reports a width. Show() can run
    // before the host has been measured, and the clamp needs a real width to work from.
    QuickEntryOptions? lastOptions;
    double lastWidth;

    public QuickEntryPresentation Kind => QuickEntryPresentation.InApp;

    /// <summary>Needs a <see cref="ContentPage"/> to draw on, which only exists once the app has a window.</summary>
    public bool IsSupported => this.currentPage() != null;

    public Action? Deactivated { get; set; }

    public Func<QuickEntryKey, bool>? KeyPressed { get; set; }

    /// <summary>Never raised: the overlay is laid out by the page, so nothing here has to size itself.</summary>
    public Action<double>? ContentHeightChanged { get; set; }

    public Task PrepareAsync(QuickEntryOptions options, View content)
    {
        this.content = content;
        this.Attach(options);
        return Task.CompletedTask;
    }

    public void SetContent(View content)
    {
        this.content = content;
        if (this.contentHost != null)
            this.contentHost.Content = content;
    }

    public void Show(QuickEntryOptions options, double width, double height)
    {
        // Re-attach every open. The page underneath changes as the user navigates, and an overlay
        // pinned to whichever page happened to be showing the first time is the classic way one of
        // these silently stops appearing.
        this.Attach(options);
        if (this.overlay == null)
            return;

        this.ApplyLayout(options, width);
        this.opened = true;
        this.overlay.IsShown = true;
    }

    public void Hide()
    {
        this.opened = false;
        var overlay = this.overlay;
        if (overlay == null)
            return;

        overlay.IsShown = false;

        // Nothing is animating out - it was never shown, or the fade finished synchronously (which
        // already released everything through OnOverlayPropertyChanged) - so let go now.
        if (ReferenceEquals(this.overlay, overlay) && !overlay.IsVisible)
        {
            this.Detach();
            return;
        }

        // Otherwise the fade releases it when IsVisible drops. This is the backstop for a fade that
        // never finishes, and is ignored if the popup was shown (and hidden) again in the meantime.
        var generation = ++this.hideGeneration;
        overlay.Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(overlay.AnimationDuration) + DetachGrace,
            () =>
            {
                if (!this.opened && generation == this.hideGeneration && ReferenceEquals(this.overlay, overlay))
                    this.Detach();
            }
        );
    }

    public void Resize(QuickEntryOptions options, double width, double height)
        => this.ApplyLayout(options, width);

    public void Teardown()
    {
        this.opened = false;
        if (this.overlay != null)
            this.overlay.IsShown = false;

        this.Detach();
    }

    // -------------------------------------------------------------------------------------

    void Attach(QuickEntryOptions options)
    {
        var current = this.currentPage();
        if (current == null)
            return;

        if (ReferenceEquals(current, this.page) && this.overlay != null)
            return;

        // Moving to a new page: let the old host go, and detach the content before it is re-parented.
        this.Detach();

        this.page = current;
        this.host = FindHost(current) ?? InstallHost(current);

        this.contentHost = new ContentView { Content = this.content };
        this.overlay = new Overlay
        {
            CloseOnBackdropTap = options.DismissOnScrimTap,
            OverlayContentTemplate = new DataTemplate(() => this.contentHost)
        };
        this.overlay.PropertyChanged += this.OnOverlayPropertyChanged;
        this.host.Children.Add(this.overlay);
        this.host.SizeChanged -= this.OnHostSizeChanged;
        this.host.SizeChanged += this.OnHostSizeChanged;
        this.ApplyLayout(options, options.Width);
    }

    /// <summary>
    /// Takes the popup back off the page it was on and forgets that page. Safe to call repeatedly.
    /// </summary>
    /// <remarks>
    /// The service owns the content view and keeps it across shows, so clearing
    /// <see cref="ContentView.Content"/> matters as much as dropping the fields: while the content
    /// is parented, its <c>Parent</c> chain runs host → layer → page and the service roots all of it.
    /// </remarks>
    void Detach()
    {
        var overlay = this.overlay;
        var host = this.host;

        if (overlay != null)
        {
            overlay.PropertyChanged -= this.OnOverlayPropertyChanged;

            // Cut off mid-fade: the overlay never reached the end of its hide, so it never released
            // the host's shared backdrop either, and the page would stay dimmed.
            if (overlay.IsVisible)
                host?.HideBackdrop(overlay, 0);

            host?.Children.Remove(overlay);
            overlay.OverlayContentTemplate = null;
        }

        // Release the content so it can be shown again elsewhere - MAUI refuses to add a view that
        // still has a parent, and switching presentation hands this same view across.
        if (this.contentHost != null)
            this.contentHost.Content = null;

        if (host != null)
            host.SizeChanged -= this.OnHostSizeChanged;

        this.overlay = null;
        this.contentHost = null;
        this.host = null;
        this.page = null;
        this.hideGeneration++;
    }

    /// <summary>
    /// The host is usually unmeasured on the first <c>Show</c>, so the clamp in
    /// <see cref="ApplyLayout"/> has no width to work from. Replaying the layout once the host has a
    /// size is what keeps the very first open from being the one that overflows — and it re-clamps
    /// on rotation for free.
    /// </summary>
    void OnHostSizeChanged(object? sender, EventArgs e)
    {
        if (this.lastOptions != null)
            this.ApplyLayout(this.lastOptions, this.lastWidth);
    }

    /// <summary>
    /// A backdrop tap sets <c>IsShown</c> false on the overlay itself, so watching the property is how
    /// the host hears about a dismissal it did not initiate.
    /// </summary>
    void OnOverlayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == VisualElement.IsVisibleProperty.PropertyName)
        {
            // The hide fade has finished. Unless a show landed during it, this is the end of this
            // page's popup.
            if (!this.opened && sender is Overlay { IsVisible: false, IsShown: false } done && ReferenceEquals(done, this.overlay))
                this.Detach();
            return;
        }

        if (e.PropertyName != Overlay.IsShownProperty.PropertyName)
            return;

        if (this.opened && this.overlay?.IsShown == false)
        {
            this.opened = false;
            this.Deactivated?.Invoke();

            // A scrim tap the service chose not to act on still took the popup down; let the page go
            // once the fade is over, same as an explicit hide.
            if (!this.opened && this.overlay != null && !this.overlay.IsVisible)
                this.Detach();
        }
    }

    static OverlayHost? FindHost(Element root)
    {
        if (root is OverlayHost host)
            return host;

        foreach (var child in root.LogicalChildren)
        {
            if (child is Element element && FindHost(element) is { } found)
                return found;
        }
        return null;
    }

    /// <summary>
    /// Installs a host for a page that has none, in the shared page overlay so it layers correctly
    /// against tooltips, walkthroughs and dialogs rather than fighting them.
    /// </summary>
    static OverlayHost InstallHost(ContentPage page)
    {
        var layer = PageOverlay.GetOrCreateLayer<PageOverlay.QuickEntryLayer>(page, PageOverlay.Layers.QuickEntry);
        if (layer.Children.OfType<OverlayHost>().FirstOrDefault() is { } existing)
            return existing;

        var host = new OverlayHost();
        layer.Children.Add(host);
        return host;
    }

    /// <summary>
    /// Places the popup with alignment and a margin. Height is never requested: the overlay is laid
    /// out by the page, so the content sizes itself and <c>MaxHeight</c> is simply a ceiling.
    /// </summary>
    void ApplyLayout(QuickEntryOptions options, double width)
    {
        if (this.contentHost == null || this.overlay == null)
            return;

        this.lastOptions = options;
        this.lastWidth = width;

        // Width in the options is a desktop *window* width. In-app the popup is laid out inside the
        // page, so a request wider than the host centres the card off both edges at once — on a
        // phone 720 against a 420 host puts it at x=-150 and almost all of it off-screen. Clamp to
        // what the host actually has. The content carries its own WidthRequest (the service sets it
        // when it builds the view), so both have to come down or the inner view overflows anyway.
        var hostWidth = this.host?.Width ?? 0;
        var effective = hostWidth > 0
            ? Math.Min(width, Math.Max(0, hostWidth - (SideMargin * 2)))
            : width;

        this.contentHost.WidthRequest = effective;
        this.contentHost.MaximumHeightRequest = options.MaxHeight;

        if (this.content != null)
            this.content.WidthRequest = effective;

        var available = this.host?.Height > 0 ? this.host.Height : 0;

        switch (options.Placement)
        {
            case QuickEntryPlacement.BottomCenter:
                this.overlay.ContentAlignment = LayoutOptions.End;
                this.overlay.ContentMargin = new Thickness(0, 0, 0, available * options.BottomMarginRatio);
                break;

            // A touch screen has no pointer to sit near, and on desktop the overlay is inside the
            // app rather than at the cursor, so NearCursor reads as centred in-app rather than as a
            // silently ignored setting.
            case QuickEntryPlacement.Center:
            case QuickEntryPlacement.NearCursor:
                this.overlay.ContentAlignment = LayoutOptions.Center;
                this.overlay.ContentMargin = new Thickness(0);
                break;

            case QuickEntryPlacement.Manual:
                this.overlay.ContentAlignment = LayoutOptions.Start;
                this.overlay.ContentMargin = new Thickness(options.X, options.Y, 0, 0);
                break;

            default:
                this.overlay.ContentAlignment = LayoutOptions.Start;
                this.overlay.ContentMargin = new Thickness(0, available * options.TopMarginRatio, 0, 0);
                break;
        }
    }
}

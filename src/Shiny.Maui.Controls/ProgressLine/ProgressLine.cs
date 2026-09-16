using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

/// <summary>
/// The thin determinate/indeterminate line that runs across the top or bottom of a page while
/// something is loading.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="ProgressBar"/>, which is an inline view that fills a slot you gave it in
/// a layout. This one is chrome: it has no slot, it moves itself onto the page edge, and it knows
/// about the navigation bar, the tab bar and the safe area so it lands against them rather than
/// under them. The drawing is <see cref="ProgressBar"/>'s — the gradient, the shimmer sweep, the
/// animated fill and the platform paint fixes are all shared rather than reimplemented.
/// </para>
/// <para>
/// It can be driven two ways: declared in markup with <see cref="Value"/> bound, or created for you
/// by <see cref="IProgressLineService"/> when the thing you are reporting on is a code path rather
/// than a view-model property.
/// </para>
/// </remarks>
public partial class ProgressLine : ContentView, IDisposable
{
    const string FadeAnimationName = "ShinyProgressLineFade";

    readonly ProgressBar bar;

    ContentPage? subscribedPage;
    Element? watchedAncestor;
    bool dockPending;
    bool disposed;

    /// <summary>
    /// The page this line belongs to, kept after the line loses its parent. See
    /// <see cref="OnHomePageContentChanged"/> for why.
    /// </summary>
    ContentPage? homePage;
    object? outgoingContent;
    bool docking;

    public ProgressLine()
    {
        this.bar = new ProgressBar
        {
            ShowText = false,
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill
        };

        // Bound rather than pushed by a propertyChanged handler on each of the fifteen passthroughs:
        // one source of truth, and a property added to ProgressBar later cannot silently go unwired
        // on this side.
        Forward(ProgressBar.ValueProperty, nameof(this.Value));
        Forward(ProgressBar.MinimumProperty, nameof(this.Minimum));
        Forward(ProgressBar.MaximumProperty, nameof(this.Maximum));
        Forward(ProgressBar.IsIndeterminateProperty, nameof(this.IsIndeterminate));
        Forward(ProgressBar.BarColorProperty, nameof(this.BarColor));
        Forward(ProgressBar.TrackColorProperty, nameof(this.TrackColor));
        Forward(ProgressBar.TrackHeightProperty, nameof(this.LineHeight));
        Forward(ProgressBar.CornerRadiusProperty, nameof(this.CornerRadius));
        Forward(ProgressBar.UseGradientProperty, nameof(this.UseGradient));
        Forward(ProgressBar.AnimateProgressProperty, nameof(this.AnimateProgress));
        Forward(ProgressBar.ProgressAnimationDurationProperty, nameof(this.ProgressAnimationDuration));
        Forward(ProgressBar.ProgressAnimationEasingProperty, nameof(this.ProgressAnimationEasing));
        Forward(ProgressBar.PulseEnabledProperty, nameof(this.PulseEnabled));
        Forward(ProgressBar.PulseColorProperty, nameof(this.PulseColor));
        Forward(ProgressBar.PulseLengthProperty, nameof(this.PulseLength));
        Forward(ProgressBar.PulseSpeedProperty, nameof(this.PulseSpeed));

        this.Content = this.bar;
        this.HorizontalOptions = LayoutOptions.Fill;

        // The line reports, it is never the thing being touched — and it spans the full width, so
        // leaving it hit-testable would swallow taps along a whole edge of the page.
        this.InputTransparent = true;

        this.ApplyGradientColors();

        StyleGuard.MarkReady(this, typeof(ProgressLine));

        void Forward(BindableProperty target, string source)
            => this.bar.SetBinding(target, new Binding(source, source: this));
    }


    /// <summary>The inner bar, for the styling <see cref="ProgressLine"/> does not surface.</summary>
    public ProgressBar Bar => this.bar;


    protected override void OnParentSet()
    {
        base.OnParentSet();

        this.Unsubscribe();

        if (this.Parent is null)
        {
            // The home page is only remembered so a *docking* line can find its way back after the
            // page's content is swapped. A line that does not dock - the one IProgressLineService owns,
            // chiefly - has nothing to return to, and holding the page (and its PropertyChanged
            // subscription) here kept the last page a run was shown on alive inside the singleton.
            if (!this.Dock)
                this.UntrackHomePage();
            return;
        }

        if (PageOverlay.FindPage(this) is { } page)
            this.TrackHomePage(page);

        if (!this.Dock || this.Parent is PageOverlay.ProgressLineLayer)
        {
            this.RefreshLayout();
            return;
        }

        this.ScheduleDock();
    }


    /// <summary>
    /// Docking is deferred a tick because XAML sets properties in document order: at the moment the
    /// line's parent is set, the page around it is often still being built and has no content to
    /// wrap into an overlay root.
    /// </summary>
    internal void ScheduleDock()
    {
        // Mid-dock, the move itself re-parents the line; the dock already running finishes the job.
        if (this.dockPending || this.docking)
            return;

        this.dockPending = true;
        this.Dispatcher.Dispatch(() =>
        {
            this.dockPending = false;
            this.TryDock();
        });
    }


    void TryDock()
    {
        if (this.disposed || !this.Dock)
            return;

        var page = PageOverlay.FindPage(this);

        // Off every page, but only because the page's content was swapped out from under the line —
        // so the page it belonged to is still the right one. A line moved somewhere else entirely has
        // a parent of its own and is left to find its new page.
        if (page is null && (this.Parent is null || this.Parent is PageOverlay.ProgressLineLayer))
            page = this.homePage;

        if (page is null)
        {
            this.WatchForPage();
            return;
        }

        if (this.IsDockedOn(page))
            return;

        this.UnwatchAncestor();
        this.TrackHomePage(page);

        this.docking = true;
        try
        {
            // Before reading Parent: creating the root re-parents the page's content, and this line
            // is currently somewhere inside it.
            var layer = PageOverlay.GetOrCreateLayer<PageOverlay.ProgressLineLayer>(page, PageOverlay.Layers.ProgressLine);

            if (!this.DetachFromParent())
                return;

            layer.Children.Add(this);
        }
        finally
        {
            this.docking = false;
        }

        this.RefreshLayout();

        // Second pass once the page has laid out: the nav/tab bar's measured height is what the inset
        // is taken from, and on the first pass it is still zero.
        this.Dispatcher.Dispatch(this.RefreshLayout);
    }


    bool IsDockedOn(ContentPage page)
        => this.Parent is PageOverlay.ProgressLineLayer { Parent: PageOverlay.ShinyOverlayRoot root }
            && ReferenceEquals(page.Content, root);


    void TrackHomePage(ContentPage page)
    {
        if (ReferenceEquals(page, this.homePage))
            return;

        this.UntrackHomePage();
        this.homePage = page;
        page.PropertyChanging += this.OnHomePageContentChanging;
        page.PropertyChanged += this.OnHomePageContentChanged;
    }


    void UntrackHomePage()
    {
        if (this.homePage is null)
            return;

        this.homePage.PropertyChanging -= this.OnHomePageContentChanging;
        this.homePage.PropertyChanged -= this.OnHomePageContentChanged;
        this.homePage = null;
        this.outgoingContent = null;
    }


    void OnHomePageContentChanging(object? sender, Microsoft.Maui.Controls.PropertyChangingEventArgs e)
    {
        if (e.PropertyName == ContentPage.ContentProperty.PropertyName)
            this.outgoingContent = this.homePage?.Content;
    }


    /// <summary>
    /// Re-docks a line the page's content was replaced out from under.
    /// </summary>
    /// <remarks>
    /// This is the ordinary shape of a page declaring the line in markup: XAML treats every direct
    /// child of a <see cref="ContentPage"/> as its <c>Content</c>, so the line is the content for a
    /// moment and the layout after it then replaces it. Docking is deferred a tick, so by the time it
    /// ran the line had no parent and no way back to the page — it was never added to the tree at all,
    /// and nothing reported it. The same happens to a line that had already docked when the page's
    /// content is later swapped: its overlay root goes with the old content.
    /// </remarks>
    void OnHomePageContentChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != ContentPage.ContentProperty.PropertyName)
            return;

        var outgoing = this.outgoingContent;
        this.outgoingContent = null;

        if (this.docking || this.disposed || !this.Dock || outgoing is null)
            return;

        if (ReferenceEquals(outgoing, this) || (outgoing is Element old && this.IsInside(old)))
            this.ScheduleDock();
    }


    bool IsInside(Element ancestor)
    {
        for (var element = this.Parent; element is not null; element = element.Parent)
        {
            if (ReferenceEquals(element, ancestor))
                return true;
        }
        return false;
    }


    /// <summary>
    /// Waits for the page to appear above the line, by watching the top of the chain it is currently
    /// in for a parent of its own.
    /// </summary>
    /// <remarks>
    /// <see cref="OnParentSet"/> alone is not enough, and the reason is the shape of every XAML page:
    /// the line is constructed and added to its layout first, and only then is that layout handed to
    /// the page. The line's own parent never changes at that second step, so nothing re-fires — the
    /// line sits inline forever and the control looks like it simply does not work.
    /// </remarks>
    void WatchForPage()
    {
        Element root = this;
        while (root.Parent is not null)
            root = root.Parent;

        if (ReferenceEquals(root, this.watchedAncestor))
            return;

        this.UnwatchAncestor();
        this.watchedAncestor = root;
        root.ParentChanged += this.OnAncestorParentChanged;
    }


    void OnAncestorParentChanged(object? sender, EventArgs e)
    {
        this.UnwatchAncestor();
        this.ScheduleDock();
    }


    void UnwatchAncestor()
    {
        if (this.watchedAncestor is null)
            return;

        this.watchedAncestor.ParentChanged -= this.OnAncestorParentChanged;
        this.watchedAncestor = null;
    }


    /// <summary>
    /// Removes the line from whatever it was declared in. False when the parent is not something a
    /// child can be pulled out of — a templated item, chiefly — in which case the line stays inline
    /// rather than throwing.
    /// </summary>
    bool DetachFromParent()
    {
        switch (this.Parent)
        {
            case null:
                return true;

            case Layout layout:
                return layout.Children.Remove(this);

            case ContentView view when ReferenceEquals(view.Content, this):
                view.Content = null;
                return true;

            case ScrollView scroll when ReferenceEquals(scroll.Content, this):
                scroll.Content = null;
                return true;

            case ContentPage page when ReferenceEquals(page.Content, this):
                page.Content = null;
                return true;

            default:
                return false;
        }
    }


    /// <summary>
    /// Re-resolves the edge, the inset and the fade state. Called automatically on docking, on a
    /// placement property change and when the page resizes; call it directly after changing the
    /// height of a bar the line is sitting against.
    /// </summary>
    public void RefreshLayout()
    {
        if (this.disposed)
            return;

        var page = PageOverlay.FindPage(this);
        this.Subscribe(page);

        var top = this.Position == ProgressLinePosition.Top;
        this.VerticalOptions = top ? LayoutOptions.Start : LayoutOptions.End;

        var inset = this.AutoInset && page is not null
            ? ProgressLineInsets.Resolve(page, this.OverlayRoot(), this.Position)
            : 0;

        this.Margin = top
            ? new Thickness(this.Offset.Left, inset + this.Offset.Top, this.Offset.Right, 0)
            : new Thickness(this.Offset.Left, 0, this.Offset.Right, inset + this.Offset.Bottom);

        this.HeightRequest = this.LineHeight;
    }


    /// <summary>
    /// The overlay root the line sits in — the coordinate space its margin is measured against, and
    /// the thing the inset rule is expressed in terms of. Null when the line is inline.
    /// </summary>
    Element? OverlayRoot()
    {
        for (Element? element = this; element is not null; element = element.Parent)
        {
            if (element is PageOverlay.ShinyOverlayRoot root)
                return root;
        }
        return null;
    }


    void Subscribe(ContentPage? page)
    {
        if (ReferenceEquals(page, this.subscribedPage))
            return;

        this.Unsubscribe();

        if (page is null)
            return;

        this.subscribedPage = page;
        page.SizeChanged += this.OnPageSizeChanged;
    }


    void Unsubscribe()
    {
        if (this.subscribedPage is null)
            return;

        this.subscribedPage.SizeChanged -= this.OnPageSizeChanged;
        this.subscribedPage = null;
    }


    // A rotation or a window resize changes the safe area and can change the chrome's height with it.
    void OnPageSizeChanged(object? sender, EventArgs e) => this.RefreshLayout();


    void OnActiveChanged(bool active)
    {
        this.AbortAnimation(FadeAnimationName);

        if (this.FadeDuration <= 0)
        {
            this.Opacity = active ? 1 : 0;
            this.IsVisible = active;
            return;
        }

        if (active)
            this.IsVisible = true;

        new Animation(v => this.Opacity = v, this.Opacity, active ? 1 : 0)
            .Commit(
                this,
                FadeAnimationName,
                length: (uint)this.FadeDuration,
                finished: (_, cancelled) =>
                {
                    if (!cancelled && !active)
                        this.IsVisible = false;
                }
            );
    }


    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        this.Unsubscribe();
        this.UnwatchAncestor();
        this.UntrackHomePage();
        this.AbortAnimation(FadeAnimationName);
        this.bar.Dispose();
        GC.SuppressFinalize(this);
    }
}

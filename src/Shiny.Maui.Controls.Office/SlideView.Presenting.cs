namespace Shiny.Maui.Controls.Office;

/// <summary>
/// Presenting mode: the deck full screen on a black surround, with the chrome faded out.
/// </summary>
public partial class SlideView
{
    /// <summary>The left edge band where a tap goes back instead of forward, as a fraction of the width.</summary>
    const double BackTapZone = 0.25;

    /// <summary>Set on the surface the show page carries, which must never push a page of its own.</summary>
    SlideView? mirrorOwner;

    SlideShowPage? showPage;
    bool presentingApplied;
    bool presentingTransitioning;

    /// <summary>
    /// The full-screen surface a <see cref="SlideShowPage"/> shows. Shares the owner's deck, not its
    /// controller.
    /// </summary>
    /// <remarks>
    /// A second controller rather than the owner's, because a controller owns a viewport: the show
    /// surface is the size of the screen and the inline viewer is whatever the page gave it, so sharing
    /// one would leave the inline viewer laid out for the projector after the show ended — and nothing
    /// resizes it back, because its own size never changed. The index is pushed to the owner instead,
    /// which is the only piece of that state a host cares about.
    /// </remarks>
    internal SlideView(SlideView owner) : this()
    {
        this.mirrorOwner = owner;
        this.presentingApplied = true;
        this.SetValue(IsPresentingProperty, true);

        this.Watermark = owner.Watermark;
        this.Deck = owner.Deck;
        this.SlideIndex = owner.SlideIndex;

        // The show surface runs the show engine: clicks through animations, transitions, timings,
        // hidden slides, black and white screens.
        if (this.Deck is { Slides.Count: > 0 } deck)
        {
            this.show = new SlideShowController(deck, this.SlideIndex);
            this.show.Changed += this.OnShowChanged;
            this.show.Ended += this.OnShowEnded;
        }
    }

    SlideShowController? show;
    SlideShowPainter? showPainter;
    IDispatcherTimer? ticker;

    /// <summary>The running show on the presenting surface, for the show page's counter and presenter view.</summary>
    internal SlideShowController? Show => this.show;

    /// <summary>
    /// Present in PowerPoint's Presenter View: the slide beside the next one, the notes, a timer and the
    /// controls. Off by default — the plain show is what a projector wants.
    /// </summary>
    public static readonly BindableProperty PresenterViewProperty = BindableProperty.Create(
        nameof(PresenterView),
        typeof(bool),
        typeof(SlideView),
        false);

    /// <inheritdoc cref="PresenterViewProperty"/>
    public bool PresenterView
    {
        get => (bool)this.GetValue(PresenterViewProperty);
        set => this.SetValue(PresenterViewProperty, value);
    }

    /// <summary>Raised on the show page's surface whenever the show's slide, click or screen changes.</summary>
    internal event EventHandler? ShowChanged;

    void OnShowChanged(object? sender, EventArgs e)
    {
        if (this.show is { } running && this.controller is { } c && c.Index != running.SlideIndex)
            c.Index = running.SlideIndex;

        this.StartTicking();
        this.Invalidate();
        this.ShowChanged?.Invoke(this, EventArgs.Empty);
    }

    void OnShowEnded(object? sender, EventArgs e) => this.mirrorOwner?.StopPresenting();

    /// <summary>A frame clock while something moves — a transition, an effect, a timed advance.</summary>
    void StartTicking()
    {
        if (this.ticker is not null || this.show is null || this.Dispatcher is null)
            return;

        var timer = this.Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (_, _) =>
        {
            if (this.show is not { } running)
            {
                this.StopTicking();
                return;
            }

            running.Tick();
            this.Invalidate();

            if (!running.IsAnimating)
                this.StopTicking();
        };

        this.ticker = timer;
        timer.Start();
    }

    void StopTicking()
    {
        this.ticker?.Stop();
        this.ticker = null;
    }

    /// <summary>A tap in a show: a clip or a link under the finger first, then forward or back.</summary>
    void OnShowTap(SlideShowController running, double x, double y)
    {
        if (this.Deck is { } deck && this.Width > 0)
        {
            var fit = Math.Min(this.Width / deck.SlideWidth, this.Height / deck.SlideHeight);
            var slideX = (x - (this.Width - deck.SlideWidth * fit) / 2) / fit;
            var slideY = (y - (this.Height - deck.SlideHeight * fit) / 2) / fit;

            if (running.MediaAt(slideX, slideY) is { Media: { } media })
            {
                _ = PlayMediaAsync(media);
                return;
            }

            if (running.HyperlinkAt(slideX, slideY, this.measurer) is { } link)
            {
                if (running.Follow(link) is { } url && Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    _ = OpenLinkAsync(uri);

                return;
            }
        }

        if (x < this.Width * BackTapZone)
            running.Previous();
        else
            running.Next(fromClick: true);
    }

    static async Task OpenLinkAsync(Uri uri)
    {
        try
        {
            await Launcher.Default.OpenAsync(uri);
        }
        catch (Exception)
        {
            // No browser registered (a kiosk, a test host): the link simply does nothing.
        }
    }

    /// <summary>
    /// Plays an embedded clip in the platform's own player: written to the cache and handed to the
    /// launcher. The show stays where it was underneath.
    /// </summary>
    static async Task PlayMediaAsync(SlideMedia media)
    {
        try
        {
            if (media.ReadAll() is not { } bytes)
                return;

            var extension = media.ContentType switch
            {
                "video/quicktime" => ".mov",
                "video/webm" => ".webm",
                "audio/mpeg" => ".mp3",
                "audio/mp4" or "audio/x-m4a" => ".m4a",
                "audio/wav" or "audio/x-wav" => ".wav",
                _ => media.IsVideo ? ".mp4" : ".mp3"
            };

            var path = Path.Combine(FileSystem.CacheDirectory, $"shiny-slide-media-{Guid.NewGuid():N}{extension}");
            await File.WriteAllBytesAsync(path, bytes);
            await Launcher.Default.OpenAsync(new OpenFileRequest("Play", new ReadOnlyFile(path, media.ContentType)));
        }
        catch (Exception)
        {
            // No player for the format on this head.
        }
    }

    /// <summary>
    /// Whether the deck is being presented full screen. Two-way: leaving the show, however it was left,
    /// writes <c>false</c> back.
    /// </summary>
    public static readonly BindableProperty IsPresentingProperty = BindableProperty.Create(
        nameof(IsPresenting),
        typeof(bool),
        typeof(SlideView),
        false,
        BindingMode.TwoWay,
        propertyChanged: (b, _, value) => ((SlideView)b).ApplyPresenting((bool)value));

    /// <summary>
    /// Show the auto-hiding control bar over a presented deck. Default <c>true</c>; turn it off for a
    /// kiosk or a second screen where nobody is meant to be driving it from the surface itself.
    /// </summary>
    public static readonly BindableProperty ShowPresenterControlsProperty = BindableProperty.Create(
        nameof(ShowPresenterControls),
        typeof(bool),
        typeof(SlideView),
        true);

    /// <summary>
    /// Keep the display awake for the length of the show. Default <c>true</c> — a deck left on one slide
    /// during a long question is exactly the case a screen timeout was built for, and exactly the wrong
    /// time for it. Ignored where the platform has no such notion.
    /// </summary>
    public static readonly BindableProperty KeepScreenOnWhilePresentingProperty = BindableProperty.Create(
        nameof(KeepScreenOnWhilePresenting),
        typeof(bool),
        typeof(SlideView),
        true);

    /// <inheritdoc cref="IsPresentingProperty"/>
    public bool IsPresenting
    {
        get => (bool)this.GetValue(IsPresentingProperty);
        set => this.SetValue(IsPresentingProperty, value);
    }

    /// <inheritdoc cref="ShowPresenterControlsProperty"/>
    public bool ShowPresenterControls
    {
        get => (bool)this.GetValue(ShowPresenterControlsProperty);
        set => this.SetValue(ShowPresenterControlsProperty, value);
    }

    /// <inheritdoc cref="KeepScreenOnWhilePresentingProperty"/>
    public bool KeepScreenOnWhilePresenting
    {
        get => (bool)this.GetValue(KeepScreenOnWhilePresentingProperty);
        set => this.SetValue(KeepScreenOnWhilePresentingProperty, value);
    }

    /// <summary>Raised when the show starts or ends, however it was triggered — including the back button.</summary>
    public event EventHandler<bool>? PresentingChanged;

    /// <summary>Any touch on the surface, so the show page can bring its chrome back.</summary>
    internal event EventHandler? Interacted;

    /// <summary>Start the show. A no-op when there is no deck, or when one is already running.</summary>
    public void StartPresenting()
    {
        if (this.Deck is not null)
            this.IsPresenting = true;
    }

    /// <summary>End the show and return to the inline viewer.</summary>
    public void StopPresenting() => this.IsPresenting = false;

    /// <summary>Flip <see cref="IsPresenting"/>.</summary>
    public void TogglePresenting() => this.IsPresenting = !this.IsPresenting;

    async void ApplyPresenting(bool value)
    {
        // The show page's own surface is born presenting; it must not push a second page.
        if (this.mirrorOwner is not null)
        {
            if (this.controller is not null)
                this.controller.IsPresenting = value;

            this.Invalidate();
            return;
        }

        if (value == this.presentingApplied || this.presentingTransitioning)
            return;

        var navigation = this.FindNavigation();
        if (navigation is null || (value && this.Deck is null))
        {
            // Nothing to push onto (a detached template, a unit test), or nothing to present. Revert
            // rather than report a show that is not on screen.
            this.SetValue(IsPresentingProperty, this.presentingApplied);
            return;
        }

        this.presentingTransitioning = true;
        try
        {
            if (value)
            {
                this.showPage = new SlideShowPage(this);
                this.presentingApplied = true;
                await navigation.PushModalAsync(this.showPage, false).ConfigureAwait(true);
            }
            else
            {
                this.presentingApplied = false;
                if (this.showPage is not null)
                    await navigation.PopModalAsync(false).ConfigureAwait(true);

                this.showPage = null;
            }

            this.PresentingChanged?.Invoke(this, value);
        }
        catch (Exception)
        {
            this.presentingApplied = !value;
            this.SetValue(IsPresentingProperty, this.presentingApplied);
        }
        finally
        {
            this.presentingTransitioning = false;
        }
    }

    /// <summary>
    /// Called by the show page when the platform dismissed it — the Android back gesture, a swipe-down
    /// modal — rather than our own exit button.
    /// </summary>
    internal void OnShowPageDismissed()
    {
        if (!this.presentingApplied)
            return;

        this.presentingApplied = false;
        this.showPage = null;
        this.SetValue(IsPresentingProperty, false);
        this.PresentingChanged?.Invoke(this, false);
    }

    /// <summary>The slide the show ended on, so the inline viewer is left where the presenter left it.</summary>
    internal void AdoptSlideIndex(int index) => this.SlideIndex = index;

    INavigation? FindNavigation()
    {
        Element? element = this;
        while (element is not null)
        {
            if (element is Page page)
                return page.Navigation;

            element = element.Parent;
        }

        return Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
    }
}

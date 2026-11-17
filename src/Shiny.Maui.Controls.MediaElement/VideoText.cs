namespace Shiny.Maui.Controls.Media;

/// <summary>
/// Display text whose letters are windows onto a looping video. The video plays at full strength inside the
/// glyphs; around them it sits under an <see cref="OverlayColor"/> sheet (black by default) at
/// <see cref="OverlayOpacity"/>. Clear the overlay colour and everything around the letters is transparent
/// instead, so whatever is behind the control shows through.
/// </summary>
/// <remarks>
/// <para>
/// Decorative by design: there is no transport bar, no seek, no fullscreen. The video starts as soon as it
/// opens and loops forever. Sound is off unless you set <see cref="IsMuted"/> to <c>false</c>.
/// </para>
/// <para>
/// The mask is drawn natively over the same <see cref="IMediaPlayerBackend"/> a <see cref="MediaElement"/>
/// uses: a layer mask on the <c>AVPlayerLayer</c> on iOS, Mac Catalyst and macOS, and a <c>DST_IN</c>
/// composite over a <c>TextureView</c> on Android. Windows and Linux have no masked output yet and show
/// the text in <see cref="FallbackTextColor"/> instead (see <see cref="IsMaskSupported"/>).
/// </para>
/// <para>
/// Leave <see cref="FontSize"/> at <c>0</c> to auto-fit: the text grows until its longest line (or the
/// whole block, if that is the tighter fit) fills 90% of the control. Only explicit line breaks split it.
/// With a <see cref="Marquee"/> the text scrolls across the control instead, so auto-fit only sizes it to
/// the cross axis (90% of the height for left/right, of the width for up/down).
/// </para>
/// </remarks>
/// <example>
/// <code language="xaml">
/// &lt;media:VideoText Source="https://example.com/ocean.mp4"
///                  Text="OCEAN"
///                  HeightRequest="200" /&gt;
/// </code>
/// </example>
public class VideoText : ContentView
{
    // ── properties ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The video playing inside the letters. Accepts a bare string in XAML, classified by
    /// <see cref="MediaSource.Parse"/> as a URI, a filesystem path, or a packaged resource.
    /// </summary>
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source), typeof(MediaSource), typeof(VideoText), null,
        propertyChanged: (b, _, _) => ((VideoText)b).LoadSource());

    /// <summary>The text cut out of the video. Use a line break (<c>&amp;#10;</c> in XAML) for several lines.</summary>
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(VideoText), null,
        propertyChanged: (b, _, _) => ((VideoText)b).ApplyText());

    /// <summary>The font family — a registered MAUI font alias or a platform font name. <c>null</c> uses the system font.</summary>
    public static readonly BindableProperty FontFamilyProperty = BindableProperty.Create(
        nameof(FontFamily), typeof(string), typeof(VideoText), null,
        propertyChanged: (b, _, _) => ((VideoText)b).ApplyText());

    /// <summary>The font size in device-independent units. <c>0</c> (the default) auto-fits the text to the control.</summary>
    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize), typeof(double), typeof(VideoText), 0d,
        coerceValue: (_, v) => Math.Max(0d, (double)v),
        propertyChanged: (b, _, _) => ((VideoText)b).ApplyText());

    /// <summary>Bold and/or italic. Default <see cref="FontAttributes.Bold"/> — thin strokes show very little video.</summary>
    public static readonly BindableProperty FontAttributesProperty = BindableProperty.Create(
        nameof(FontAttributes), typeof(FontAttributes), typeof(VideoText), FontAttributes.Bold,
        propertyChanged: (b, _, _) => ((VideoText)b).ApplyText());

    /// <summary>Silence the video's audio. Default <c>true</c>: VideoText is decoration, and decoration shouldn't make noise.</summary>
    public static readonly BindableProperty IsMutedProperty = BindableProperty.Create(
        nameof(IsMuted), typeof(bool), typeof(VideoText), true,
        propertyChanged: (b, _, v) => ((VideoText)b).backend?.SetMuted((bool)v));

    /// <summary>The colour of the plain text shown where no masked video output exists (Windows, Linux). <c>null</c> uses the label default.</summary>
    public static readonly BindableProperty FallbackTextColorProperty = BindableProperty.Create(
        nameof(FallbackTextColor), typeof(Color), typeof(VideoText), null,
        propertyChanged: (b, _, _) => ((VideoText)b).ApplyText());

    /// <summary>
    /// The colour of the sheet over the video around the letters. Default black. <c>null</c> (or a fully
    /// transparent colour) drops the sheet and clips the video to the letters, leaving the rest of the
    /// control transparent.
    /// </summary>
    public static readonly BindableProperty OverlayColorProperty = BindableProperty.Create(
        nameof(OverlayColor), typeof(Color), typeof(VideoText), Colors.Black,
        propertyChanged: (b, _, _) => ((VideoText)b).ApplyText());

    /// <summary>The overlay sheet's opacity, 0–1. Default 1 (solid). Lower it to let the video show faintly around the letters.</summary>
    public static readonly BindableProperty OverlayOpacityProperty = BindableProperty.Create(
        nameof(OverlayOpacity), typeof(double), typeof(VideoText), 1d,
        coerceValue: (_, v) => Math.Clamp((double)v, 0d, 1d),
        propertyChanged: (b, _, _) => ((VideoText)b).ApplyText());

    /// <summary>Scroll the text continuously across the control. Default <see cref="MarqueeDirection.None"/> (centred, still).</summary>
    public static readonly BindableProperty MarqueeProperty = BindableProperty.Create(
        nameof(Marquee), typeof(MarqueeDirection), typeof(VideoText), MarqueeDirection.None,
        propertyChanged: (b, _, _) => ((VideoText)b).ApplyText());

    /// <summary>How long one marquee pass takes, from entering one edge to leaving the other. Default 8 seconds.</summary>
    public static readonly BindableProperty MarqueeDurationProperty = BindableProperty.Create(
        nameof(MarqueeDuration), typeof(TimeSpan), typeof(VideoText), TimeSpan.FromSeconds(8),
        propertyChanged: (b, _, _) => ((VideoText)b).ApplyText());

    /// <inheritdoc cref="OverlayColorProperty"/>
    public Color? OverlayColor
    {
        get => (Color?)this.GetValue(OverlayColorProperty);
        set => this.SetValue(OverlayColorProperty, value);
    }

    /// <inheritdoc cref="OverlayOpacityProperty"/>
    public double OverlayOpacity
    {
        get => (double)this.GetValue(OverlayOpacityProperty);
        set => this.SetValue(OverlayOpacityProperty, value);
    }

    /// <inheritdoc cref="MarqueeProperty"/>
    public MarqueeDirection Marquee
    {
        get => (MarqueeDirection)this.GetValue(MarqueeProperty);
        set => this.SetValue(MarqueeProperty, value);
    }

    /// <inheritdoc cref="MarqueeDurationProperty"/>
    public TimeSpan MarqueeDuration
    {
        get => (TimeSpan)this.GetValue(MarqueeDurationProperty);
        set => this.SetValue(MarqueeDurationProperty, value);
    }

    /// <inheritdoc cref="SourceProperty"/>
    public MediaSource? Source
    {
        get => (MediaSource?)this.GetValue(SourceProperty);
        set => this.SetValue(SourceProperty, value);
    }

    /// <inheritdoc cref="TextProperty"/>
    public string? Text
    {
        get => (string?)this.GetValue(TextProperty);
        set => this.SetValue(TextProperty, value);
    }

    /// <inheritdoc cref="FontFamilyProperty"/>
    public string? FontFamily
    {
        get => (string?)this.GetValue(FontFamilyProperty);
        set => this.SetValue(FontFamilyProperty, value);
    }

    /// <inheritdoc cref="FontSizeProperty"/>
    public double FontSize
    {
        get => (double)this.GetValue(FontSizeProperty);
        set => this.SetValue(FontSizeProperty, value);
    }

    /// <inheritdoc cref="FontAttributesProperty"/>
    public FontAttributes FontAttributes
    {
        get => (FontAttributes)this.GetValue(FontAttributesProperty);
        set => this.SetValue(FontAttributesProperty, value);
    }

    /// <inheritdoc cref="IsMutedProperty"/>
    public bool IsMuted
    {
        get => (bool)this.GetValue(IsMutedProperty);
        set => this.SetValue(IsMutedProperty, value);
    }

    /// <inheritdoc cref="FallbackTextColorProperty"/>
    public Color? FallbackTextColor
    {
        get => (Color?)this.GetValue(FallbackTextColorProperty);
        set => this.SetValue(FallbackTextColorProperty, value);
    }

    /// <summary>
    /// Whether this build draws the text as a video mask. <c>false</c> on Windows and on the plain
    /// <c>net11.0</c> target the Linux head runs on, where the text is shown in <see cref="FallbackTextColor"/>.
    /// </summary>
    public static bool IsMaskSupported =>
#if ANDROID || IOS || MACCATALYST || MACOS
        true;
#else
        false;
#endif


    // ── state ────────────────────────────────────────────────────────────────────────────────────

    readonly VideoTextSurface? surface;
    readonly Label? fallback;
    IMediaPlayerBackend? backend;
    CancellationTokenSource? openCts;
    bool videoSizeHooked;
    bool hasOpenSource;

    // Set only by Unloaded, never assumed from IsLoaded: a head that never raises Loaded must keep playing
    // exactly as before, so the default is "showing".
    bool suspended;

    public VideoText()
    {
        if (IsMaskSupported)
        {
            this.surface = new VideoTextSurface();
            this.Content = this.surface;
        }
        else
        {
            this.fallback = new Label
            {
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill,
                LineBreakMode = LineBreakMode.NoWrap
            };
            this.fallback.SizeChanged += (_, _) => this.ApplyFallbackText();
            this.Content = this.fallback;
            this.IsClippedToBounds = true; // the marquee runs the label off the edges
        }

        // Off-screen, the player pauses and lets go of its output and of this control. Its process-wide
        // observers (PiP, app lifecycle, KVO) then hold only a paused player, never the page, if the
        // handler is never disconnected (removed from a layout, swapped out of a ContentView, recycled).
        this.Loaded += (_, _) => this.Resume();
        this.Unloaded += (_, _) => this.Suspend();

        this.ApplyText();
    }

    /// <summary>The playback state of the video, for diagnostics. VideoText drives playback itself.</summary>
    public MediaElementState State => this.backend?.State ?? MediaElementState.None;


    // ── backend lifecycle ────────────────────────────────────────────────────────────────────────
    //
    // Same rule as MediaElement: the player is created when a handler arrives, never in the constructor,
    // because a backend roots itself in process-wide sources the moment it exists and only a handler
    // disconnect ever tears it down. Unloaded only pauses it and detaches it from the page (see Suspend).

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        base.OnHandlerChanging(args);

        if (args.NewHandler is null)
            this.StopFallbackMarquee();

        if (this.surface is null)
            return;

        if (args.NewHandler is not null)
        {
            if (this.backend is not null)
                return;

            this.backend = MediaPlayerBackends.Create();
            if (this.backend is null)
                return;

            if (!this.suspended)
                this.HookVideoSize();
            this.surface.Backend = this.backend;
            return;
        }

        this.openCts?.Cancel();
        this.openCts?.Dispose();
        this.openCts = null;
        this.hasOpenSource = false;

        if (this.backend is null)
            return;

        this.UnhookVideoSize();
        this.surface.Backend = null;
        this.backend.Dispose();
        this.backend = null;
    }

    void Suspend()
    {
        this.suspended = true;
        this.StopFallbackMarquee();

        if (this.backend is null)
            return;

        this.UnhookVideoSize();
        this.backend.Pause();

        // Android's player holds the TextureView (and through its parents the page); AVPlayer the layer.
        this.backend.SetOutput(null);
    }

    void Resume()
    {
        if (!this.suspended)
            return;

        this.suspended = false;
        this.ApplyFallbackText();

        if (this.backend is null || this.surface is null)
            return;

        this.surface.RebindOutput();
        this.HookVideoSize();
        this.OnVideoSizeChanged(this, EventArgs.Empty); // whatever it reported while we weren't listening

        // the player kept its position while paused, so the loop carries on where it left off
        if (this.hasOpenSource)
            this.backend.Play();
    }

    void HookVideoSize()
    {
        if (this.videoSizeHooked || this.backend is null)
            return;

        this.backend.VideoSizeChanged += this.OnVideoSizeChanged;
        this.videoSizeHooked = true;
    }

    void UnhookVideoSize()
    {
        if (!this.videoSizeHooked || this.backend is null)
            return;

        this.backend.VideoSizeChanged -= this.OnVideoSizeChanged;
        this.videoSizeHooked = false;
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (this.Handler is not null && this.backend is not null)
            this.LoadSource();
    }

    void ApplyBackendSettings()
    {
        if (this.backend is null)
            return;

        this.backend.SetLooping(true);
        this.backend.SetMuted(this.IsMuted);
        this.backend.SetAspect(MediaAspect.AspectFill);
        this.backend.SetKeepScreenOn(false);
    }

    async void LoadSource()
    {
        if (this.backend is null)
            return;

        this.openCts?.Cancel();
        this.openCts?.Dispose();
        this.openCts = new CancellationTokenSource();
        var ct = this.openCts.Token;
        var source = this.Source;
        this.hasOpenSource = false;

        try
        {
            this.ApplyBackendSettings();
            await this.backend.OpenAsync(source, ct).ConfigureAwait(true);

            if (ct.IsCancellationRequested || this.backend is null)
                return;

            // Re-applied after open: AVPlayer settings live on the item, which OpenAsync just replaced.
            this.ApplyBackendSettings();

            // a Source set while off-screen opens now and starts playing when the control is shown again
            this.hasOpenSource = source is not null;
            if (this.hasOpenSource && !this.suspended)
                this.backend.Play();
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer Source
        }
        catch (Exception ex)
        {
            // Decorative control: a dead link leaves the letters empty rather than taking the page down.
            System.Diagnostics.Debug.WriteLine($"[VideoText] Could not open '{source}': {ex.Message}");
        }
    }

    void OnVideoSizeChanged(object? sender, EventArgs e)
    {
        if (this.surface is null || this.backend is null)
            return;

        this.surface.VideoSize = this.backend.VideoSize;
        this.surface.Handler?.UpdateValue(nameof(VideoTextSurface.VideoSize));
    }


    // ── text ─────────────────────────────────────────────────────────────────────────────────────

    void ApplyText()
    {
        if (this.surface is not null)
        {
            this.surface.Text = this.Text;
            this.surface.FontFamily = this.FontFamily;
            this.surface.FontSize = this.FontSize;
            this.surface.FontAttributes = this.FontAttributes;
            this.surface.OverlayColor = this.OverlayColor;
            this.surface.OverlayOpacity = this.OverlayOpacity;
            this.surface.Marquee = this.Marquee;
            this.surface.MarqueeDuration = this.MarqueeDuration;
            this.surface.Handler?.UpdateValue(nameof(VideoTextSurface.Text));
        }

        if (this.fallback is not null)
        {
            this.fallback.Text = this.Text;
            this.fallback.FontFamily = this.FontFamily;
            this.fallback.FontAttributes = this.FontAttributes;

            if (this.FallbackTextColor is not null)
                this.fallback.TextColor = this.FallbackTextColor;
            else
                this.fallback.ClearValue(Label.TextColorProperty);

            // no video to show through the letters here, so the overlay is just the label's backdrop
            if (this.OverlayColor is { Alpha: > 0 } overlay)
                this.fallback.BackgroundColor = overlay.WithAlpha(overlay.Alpha * (float)this.OverlayOpacity);
            else
                this.fallback.ClearValue(VisualElement.BackgroundColorProperty);

            this.ApplyFallbackText();
        }
    }

    // The fallback has no native measuring to auto-fit with, so it approximates with the same average
    // advance the Blazor prerender uses. Close enough for a placeholder.
    void ApplyFallbackText()
    {
        if (this.fallback is null)
            return;

        var lines = VideoTextLayout.SplitLines(this.Text);
        var longest = lines.Count == 0 ? 0 : lines.Max(l => l.Length);
        var marquee = this.MarqueeDuration > TimeSpan.Zero ? this.Marquee : MarqueeDirection.None;

        if (this.FontSize > 0)
        {
            this.fallback.FontSize = this.FontSize;
        }
        else
        {
            var fitted = VideoTextLayout.FitFontSize(
                longest * VideoTextLayout.EstimatedAdvance * 100,
                lines.Count * VideoTextLayout.LineHeight * 100,
                100,
                this.fallback.Width,
                this.fallback.Height,
                marquee);

            if (fitted > 0)
                this.fallback.FontSize = fitted;
        }

        this.ApplyFallbackMarquee(lines.Count, longest, marquee);
    }

    // A dispatcher timer rather than a repeating MAUI Animation: a committed animation's callback is held by
    // MAUI's static animation table and pins the label (and the page) until something aborts it. The timer
    // stops on Unloaded and on handler disconnect, and runs only while the control is showing.
    static readonly TimeSpan MarqueeFrame = TimeSpan.FromMilliseconds(16);

    IDispatcherTimer? marqueeTimer;
    long marqueeStart;
    MarqueeDirection marqueeDirection;
    double marqueeTextWidth;
    double marqueeTextHeight;

    /// <summary>Whether the fallback label's marquee timer is running - a test seam.</summary>
    internal bool IsFallbackMarqueeRunning => this.marqueeTimer?.IsRunning == true;

    void ApplyFallbackMarquee(int lineCount, int longest, MarqueeDirection marquee)
    {
        if (this.fallback is not { } label)
            return;

        this.StopFallbackMarquee();

        if (this.suspended || marquee == MarqueeDirection.None || lineCount == 0 || label.Width <= 0 || label.Height <= 0)
            return;

        this.marqueeDirection = marquee;
        this.marqueeTextWidth = longest * VideoTextLayout.EstimatedAdvance * label.FontSize;
        this.marqueeTextHeight = lineCount * VideoTextLayout.LineHeight * label.FontSize;
        this.marqueeStart = System.Diagnostics.Stopwatch.GetTimestamp();

        if (this.marqueeTimer is null)
        {
            if (this.Dispatcher?.CreateTimer() is not { } timer)
                return;

            timer.Interval = MarqueeFrame;
            timer.IsRepeating = true;
            timer.Tick += (_, _) => this.TickFallbackMarquee();
            this.marqueeTimer = timer;
        }

        this.marqueeTimer.Start();
        this.TickFallbackMarquee();
    }

    void TickFallbackMarquee()
    {
        if (this.fallback is not { } label)
            return;

        var duration = Math.Max(1, this.MarqueeDuration.TotalMilliseconds);
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(this.marqueeStart).TotalMilliseconds;
        var progress = elapsed % duration / duration;

        var (x, y) = VideoTextLayout.MarqueeOffset(
            this.marqueeDirection, progress, label.Width, label.Height, this.marqueeTextWidth, this.marqueeTextHeight);
        label.TranslationX = x;
        label.TranslationY = y;
    }

    void StopFallbackMarquee()
    {
        if (this.fallback is not { } label)
            return;

        this.marqueeTimer?.Stop();
        label.TranslationX = 0;
        label.TranslationY = 0;
    }
}

#if !ANDROID && !IOS && !MACCATALYST
using Microsoft.Extensions.Logging;

namespace Shiny.Maui.Controls.Desktop.Notch;

/// <summary>
/// Hosts a <see cref="NotchView"/> in a borderless OS window pinned to the top-centre of the screen.
/// </summary>
/// <remarks>
/// <para>
/// The notch's surface fills the window and the <em>window</em> does the morphing. On macOS that is a
/// native frame animation: a managed morph inside the window cannot work on the AppKit head, which runs
/// no layout when a realized view changes size, so the surface would sit still and then jump, whereas
/// every step of a window-frame animation is a resize and lays out. Windows and GTK have no such
/// animation, so <see cref="NotchFrameAnimator"/> resizes the window once per frame instead.
/// </para>
/// <para>
/// The window has to be the notch, not a larger transparent canvas the notch morphs inside: a WinUI
/// window cannot be made see-through, and a transparent region of a window still takes the clicks
/// meant for whatever is behind it.
/// </para>
/// <para>
/// A content swap builds a fresh page rather than handing the new view to the existing notch: AppKit
/// never creates a platform view for a child added to an already-realized layout, so swapped-in content
/// would be blank. A new page is realized whole.
/// </para>
/// <para>
/// Opening and closing are content swaps like any other, and animate as one: the new content fades in
/// as the notch morphs to its size, and on macOS the old content, frozen into an image, collapses (or
/// grows) with the notch and fades out, instead of vanishing the moment the new content arrives.
/// </para>
/// </remarks>
sealed class DesktopNotchWindowService : INotchWindowService, IDisposable
{
    readonly NotchWindowOptions options;
    readonly ILogger<DesktopNotchWindowService>? logger;
    readonly SemaphoreSlim gate = new(1, 1);
    readonly NotchFrameAnimator frames;

    /// <summary>One display frame: the new content's first layout pass happens in it.</summary>
    static readonly TimeSpan LayoutFrame = TimeSpan.FromMilliseconds(16);
    /// <summary>Waiting longer than this for new content to load, the morph goes ahead regardless.</summary>
    static readonly TimeSpan LoadTimeout = TimeSpan.FromMilliseconds(500);

    Window? window;
    object? platformWindow;
    NotchView? notch;
    NotchGeometry geometry = NotchGeometry.None;
    IDisposable? pointerTracking;
    /// <summary>A swap is waiting for its new content to be laid out before the window morphs.</summary>
    bool settling;

    public DesktopNotchWindowService(NotchWindowOptions options, ILogger<DesktopNotchWindowService>? logger = null)
    {
        this.options = options;
        this.logger = logger;
        this.frames = new NotchFrameAnimator(NotchView.DefaultEasing.Ease, size => this.PlaceFrame(size, 0));
    }

    public bool HasHardwareNotch => NotchWindowPlatform.GetGeometry().HasNotch;

    public Size HardwareNotchSize
    {
        get
        {
            var g = NotchWindowPlatform.GetGeometry();
            return g.HasNotch ? new Size(g.Width, g.Height) : Size.Zero;
        }
    }

    public bool IsPointerOver { get; private set; }

    public event EventHandler? PointerEntered;

    public event EventHandler? PointerExited;

    public bool IsVisible { get; private set; }

    public View? Content { get; private set; }

    public event EventHandler? Tapped;

    public async Task ShowAsync(View? content = null, Action<NotchView>? configure = null)
    {
        if (content != null || this.window == null)
            await this.SetContentAsync(content ?? this.Content, configure).ConfigureAwait(true);

        if (this.platformWindow == null || this.notch == null)
            return;

        this.Place(this.notch.NotchSize, animate: false);
        NotchWindowPlatform.Show(this.platformWindow);
        this.IsVisible = true;
        this.pointerTracking ??= NotchWindowPlatform.TrackPointer(this.platformWindow, this.OnPointerChanged);
    }

    public async Task SetContentAsync(View? content, Action<NotchView>? configure = null)
    {
        await this.gate.WaitAsync().ConfigureAwait(true);
        try
        {
            // A swap on screen: what's showing now is frozen (see NotchWindowPlatform.FreezeContent) to
            // collapse or grow with the notch while the new content fades in.
            var swapping = this.IsVisible && this.notch != null && this.platformWindow != null && this.options.MorphDuration > 0;
            if (swapping)
                NotchWindowPlatform.FreezeContent(this.platformWindow!);

            // Detach first: the view may be moving from the notch we are about to discard, and MAUI
            // refuses to parent a view that still has one.
            if (this.notch != null)
            {
                this.notch.NotchSizeChanged -= this.OnNotchSizeChanged;
                this.notch.Tapped -= this.OnNotchTapped;
                this.notch.Content = null;
            }

            this.Content = content;
            this.geometry = this.options.UseHardwareNotch ? NotchWindowPlatform.GetGeometry() : NotchGeometry.None;
            this.notch = this.CreateNotch(content, configure, fadeIn: swapping);

            var page = new ContentPage
            {
                // Where the window cannot be see-through (Windows), it is clipped to the notch's outline
                // instead, so whatever the page paints *is* the notch: the surface colour, not white.
                BackgroundColor = NotchWindowPlatform.IsTransparent ? Colors.Transparent : this.notch.SurfaceColor,
                Padding = 0,
                Content = this.notch
            };

            if (this.window == null)
            {
                await this.OpenWindowAsync(page).ConfigureAwait(true);
            }
            else
            {
                // Swapping the page does not tear down the old one's handlers, and every swap builds a
                // whole new page: without this each one leaves its native view tree behind.
                var previous = this.window.Page;
                this.window.Page = page;
                previous?.DisconnectHandlers();
            }

            // The morph waits for the new content to be built and laid out. Started straight away, it ran
            // while the main thread was busy realizing the new page, and for anything heavier than a
            // line of text that took the whole morph: the window jumped to its new size at the end.
            // Until it starts, the frozen old content is what shows.
            this.settling = this.IsVisible;
            this.notch.Refresh();
            if (this.IsVisible)
            {
                await SettleAsync(this.notch).ConfigureAwait(true);
                this.settling = false;
                this.Place(this.notch.NotchSize, animate: true);
                this.notch.FadeInContent((uint)Math.Max(0, this.options.MorphDuration));
            }
        }
        finally
        {
            this.gate.Release();
        }
    }

    public void Hide()
    {
        this.frames.Stop();
        if (this.platformWindow != null)
            NotchWindowPlatform.Hide(this.platformWindow);
        this.IsVisible = false;

        this.pointerTracking?.Dispose();
        this.pointerTracking = null;
        this.OnPointerChanged(false);
    }

    void OnPointerChanged(bool over)
    {
        if (over == this.IsPointerOver)
            return;

        this.IsPointerOver = over;
        (over ? this.PointerEntered : this.PointerExited)?.Invoke(this, EventArgs.Empty);
    }

    NotchView CreateNotch(View? content, Action<NotchView>? configure, bool fadeIn)
    {
        var view = new NotchView
        {
            FillsHost = true,
            FollowsOfferedWidth = false,
            MaxNotchWidth = this.options.MaxWidth,
            MaxNotchHeight = this.options.MaxHeight,
            HasShadow = false,
            // The content fades in over the window's morph, so in step with it.
            MorphDuration = (uint)Math.Max(0, this.options.MorphDuration),
            FadeInFirstContent = fadeIn
        };

        if (this.geometry.HasNotch)
        {
            // Empty, the notch is exactly the hardware cut-out and vanishes into it. Content sits below
            // the cut-out, since nothing drawn behind the camera housing can be seen.
            view.Shape = NotchShape.Notch;
            view.MinNotchWidth = this.geometry.Width;
            view.MinNotchHeight = this.geometry.Height;
            view.ContentPadding = new Thickness(16, this.geometry.Height + 6, 16, 14);
        }
        else
        {
            view.Shape = NotchShape.Island;
        }

        this.options.Configure?.Invoke(view);
        configure?.Invoke(view);

        view.NotchSizeChanged += this.OnNotchSizeChanged;
        view.Tapped += this.OnNotchTapped;
        view.Content = content;
        return view;
    }

    async Task OpenWindowAsync(Page page)
    {
        var app = Application.Current
            ?? throw new InvalidOperationException("The notch window needs a running MAUI application.");

        var size = this.geometry.HasNotch ? new Size(this.geometry.Width, this.geometry.Height) : new Size(120, 36);
        var w = new Window(page) { Title = "Notch", Width = size.Width, Height = size.Height };
        this.window = w;

        // As with the quick entry window: the native window arrives through HandlerChanged, and on some
        // heads only after OpenWindow has returned.
        var ready = new TaskCompletionSource();
        void OnHandlerChanged(object? sender, EventArgs e)
        {
            if (w.Handler?.PlatformView is { } native)
            {
                w.HandlerChanged -= OnHandlerChanged;
                this.platformWindow = native;
                ready.TrySetResult();
            }
        }
        w.HandlerChanged += OnHandlerChanged;

        app.OpenWindow(w);
        OnHandlerChanged(w, EventArgs.Empty);

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await ready.Task.WaitAsync(timeout.Token).ConfigureAwait(true);
            NotchWindowPlatform.Initialize(this.platformWindow!, this.options);
        }
        catch (OperationCanceledException)
        {
            this.logger?.LogError("Timed out waiting for the notch window handler. The notch will not show on this host.");

            // A handler arriving after this would otherwise hand a closed window to platformWindow.
            w.HandlerChanged -= OnHandlerChanged;
            this.platformWindow = null;
            this.window = null;
            try { app.CloseWindow(w); }
            catch (Exception ex) { this.logger?.LogDebug(ex, "Closing the notch window failed"); }
        }
    }

    void OnNotchSizeChanged(object? sender, NotchSizeChangedEventArgs e)
    {
        // A swap places the window itself once the new content is laid out.
        if (this.IsVisible && !this.settling)
            this.Place(e.NewSize, animate: true);
    }

    /// <summary>Until the view is loaded (its native view is in the window), then one frame for its layout.</summary>
    static async Task SettleAsync(View view)
    {
        if (!view.IsLoaded)
        {
            var loaded = new TaskCompletionSource();
            void OnLoaded(object? sender, EventArgs e) => loaded.TrySetResult();
            view.Loaded += OnLoaded;
            try
            {
                if (!view.IsLoaded)
                    await loaded.Task.WaitAsync(LoadTimeout).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
            }
            finally
            {
                view.Loaded -= OnLoaded;
            }
        }
        await Task.Delay(LayoutFrame).ConfigureAwait(true);
    }

    void OnNotchTapped(object? sender, EventArgs e) => this.Tapped?.Invoke(this, EventArgs.Empty);

    void Place(Size size, bool animate)
    {
        if (this.platformWindow == null || size.Width <= 0 || size.Height <= 0)
            return;

        var duration = animate ? this.options.MorphDuration : 0;
        if (NotchWindowPlatform.AnimatesFrame)
            this.PlaceFrame(size, duration);
        else if (duration <= 0)
            this.frames.Jump(size);
        else
            this.frames.AnimateTo(size, duration, Application.Current?.Dispatcher);
    }

    void PlaceFrame(Size size, double durationMs)
    {
        if (this.platformWindow == null)
            return;

        NotchWindowPlatform.Place(
            this.platformWindow,
            size.Width,
            size.Height,
            CornersFor(size, this.notch?.CornerRadius ?? -1, this.notch?.Shape ?? NotchShape.Island),
            this.geometry.HasNotch,
            this.options.TopMargin,
            this.options.CoverMenuBar,
            durationMs
        );
    }

    /// <summary>
    /// The outline at a given frame size, for a host that clips the window to it. Resolved per frame
    /// rather than read off the notch, which only knows its corners at the size it is heading to.
    /// </summary>
    internal static CornerRadius CornersFor(Size size, double requested, NotchShape shape)
    {
        var r = NotchView.ResolveCornerRadius(requested, size.Width, size.Height);
        return shape == NotchShape.Notch ? new CornerRadius(0, 0, r, r) : new CornerRadius(r);
    }

    public void Dispose()
    {
        this.frames.Dispose();
        this.pointerTracking?.Dispose();
        this.pointerTracking = null;

        var native = this.platformWindow;
        this.platformWindow = null;
        if (native != null)
            NotchWindowPlatform.Teardown(native);

        var w = this.window;
        this.window = null;
        if (w != null)
        {
            try { Application.Current?.CloseWindow(w); }
            catch (Exception ex) { this.logger?.LogDebug(ex, "Closing the notch window failed"); }
        }
        this.gate.Dispose();
    }
}
#endif

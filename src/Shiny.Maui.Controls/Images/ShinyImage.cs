using Microsoft.Extensions.DependencyInjection;
using Shiny.Maui.Controls.Images.Svg;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Images;

/// <summary>
/// An image that always shows something: placeholder artwork, a determinate or indeterminate
/// loading ring, the image itself, or error artwork.
/// </summary>
/// <remarks>
/// <para>Remote URIs go through <see cref="IImageService"/>, which caches to memory and disk, caps
/// how many downloads run at once, and collapses concurrent requests for the same URI into one.
/// Replace <see cref="IImageDownloader"/> to control how the bytes are fetched (auth headers, your
/// own <c>HttpClient</c>) without giving up any of that.</para>
///
/// <para>SVG is drawn as vectors rather than rasterized, so one file stays sharp at every size and
/// can be tinted per placement with <see cref="SvgTintColor"/>. Parsing is the expensive half of a
/// vector image, so parsed documents are shared through <see cref="Svg.SvgCache"/> - a list of a
/// hundred rows showing the same icon parses it once. Artwork can come from
/// <c>resource://</c> (an embedded resource), a file path or bundled asset, a <c>data:</c> URI, or
/// an <c>http</c>/<c>https</c> URL.</para>
/// </remarks>
/// <example>
/// <code>
/// &lt;shiny:ShinyImage Uri="{Binding AvatarUrl}"
///                   PlaceholderImage="avatar_placeholder.png"
///                   ErrorImage="broken_image.png"
///                   Aspect="AspectFill"
///                   HeightRequest="120" /&gt;
///
/// &lt;shiny:ShinyImage Uri="resource://MyApp.Assets.logo.svg"
///                   SvgTintColor="{StaticResource Primary}"
///                   HeightRequest="48" /&gt;
/// </code>
/// </example>
public partial class ShinyImage : ContentView, IDisposable
{
    readonly Grid root;
    readonly Image targetImage;
    readonly Image placeholderImage;
    readonly Image errorImage;
    readonly GraphicsView vectorImage;
    readonly SvgDrawable vectorDrawable;
    readonly ContentView loadingHost;
    readonly ContentView errorHost;
    readonly LoadingRing ring;

    CancellationTokenSource? cts;
    IImageService? resolvedService;
    SvgCache? resolvedSvgCache;
    bool showingVector;
    bool isDisposed;

    /// <summary>Creates the control.</summary>
    public ShinyImage()
    {
        this.targetImage = new Image
        {
            Aspect = Aspect.AspectFit,
            Opacity = 0,
            IsVisible = false
        };

        this.vectorDrawable = new SvgDrawable();

        // A second presenter rather than a rasterized ImageSource. A vector redrawn at the size it
        // is actually shown at is the whole reason to ship SVG, and there is no cross-platform way
        // to rasterize into an ImageSource without pulling a native imaging stack into a package
        // that currently has none.
        this.vectorImage = new GraphicsView
        {
            Drawable = this.vectorDrawable,
            Opacity = 0,
            IsVisible = false,
            InputTransparent = true
        };

        this.placeholderImage = new Image
        {
            Aspect = Aspect.AspectFit,
            IsVisible = false,
            InputTransparent = true
        };

        this.errorImage = new Image
        {
            Aspect = Aspect.AspectFit,
            InputTransparent = true
        };

        this.ring = new LoadingRing();

        this.loadingHost = new ContentView
        {
            Content = this.ring,
            IsVisible = false,
            InputTransparent = true,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        this.errorHost = new ContentView
        {
            Content = this.errorImage,
            IsVisible = false,
            InputTransparent = true
        };

        this.root = new Grid();
        this.root.Children.Add(this.placeholderImage);
        this.root.Children.Add(this.targetImage);
        this.root.Children.Add(this.vectorImage);
        this.root.Children.Add(this.errorHost);
        this.root.Children.Add(this.loadingHost);
        this.Content = this.root;

        // A recycled cell raises Unloaded then Loaded again. Cancelling on the way out stops work
        // for a row nobody is looking at; restarting on the way back in is what keeps a scrolled-away
        // and scrolled-back cell from showing a permanently empty frame. Cache hits make the retry
        // nearly free.
        this.Loaded += this.OnControlLoaded;
        this.Unloaded += this.OnControlUnloaded;

        StyleGuard.MarkReady(this, typeof(ShinyImage));
    }


    /// <summary>
    /// The service this control loads through. Assign to override the resolved one for this instance
    /// - handy in tests and for a screen that needs its own cache policy.
    /// </summary>
    public IImageService ImageService
    {
        get => this.resolvedService ??= Resolve(this, static () => DefaultService.Value);
        set => this.resolvedService = value;
    }


    /// <summary>
    /// Where parsed SVG documents are shared from. Assign to give one screen its own, or to isolate
    /// a test.
    /// </summary>
    public SvgCache SvgCache
    {
        get => this.resolvedSvgCache ??= Resolve(this, static () => DefaultSvgCache.Value);
        set => this.resolvedSvgCache = value;
    }


    /// <summary>
    /// Re-fetches the image, skipping both cache tiers - and, for SVG, re-parsing it. The caches are
    /// refreshed with what comes back.
    /// </summary>
    public Task ReloadAsync() => this.LoadAsync(true);


    void OnControlLoaded(object? sender, EventArgs e)
    {
        if (this.State is ImageLoadState.None or ImageLoadState.Failed || !this.HasContent)
            this.BeginLoad();
    }


    void OnControlUnloaded(object? sender, EventArgs e) => this.CancelPending();


    void BeginLoad() => _ = this.LoadAsync(false);


    bool HasContent => this.showingVector ? this.vectorDrawable.Document is not null : this.targetImage.Source is not null;


    async Task LoadAsync(bool bypassCache)
    {
        this.CancelPending();

        if (this.isDisposed)
            return;

        var source = this.Source;
        if (source is not null)
        {
            // An explicit ImageSource is already resolved as far as we are concerned - there is
            // nothing to download, queue, parse or cache.
            this.SetProgress(new ImageLoadProgress(ImageLoadState.Loaded));
            await this.ShowImageAsync(source).ConfigureAwait(true);
            this.RaiseLoaded(null, ImageOrigin.Memory, null);
            return;
        }

        var uri = this.Uri;
        if (String.IsNullOrWhiteSpace(uri))
        {
            this.ShowNothing();
            return;
        }

        var tokenSource = new CancellationTokenSource();
        this.cts = tokenSource;

        // Local files, bundled assets, embedded resources and data URIs never touch the service.
        // Sending them through the download path would mean a queue slot and a cache copy for bytes
        // already on the device.
        if (IsRemote(uri))
            await this.LoadRemoteAsync(uri, bypassCache, tokenSource.Token).ConfigureAwait(true);
        else
            await this.LoadLocalAsync(uri, bypassCache, tokenSource.Token).ConfigureAwait(true);
    }


    async Task LoadRemoteAsync(string uri, bool bypassCache, CancellationToken token)
    {
        this.SetProgress(ImageLoadProgress.Queued);

        // Progress<T> captures the sync context at construction. Building it here - on the UI thread,
        // where every load starts - is what makes the ring update without a manual dispatch.
        var progress = new Progress<ImageLoadProgress>(this.SetProgress);

        var request = new ImageRequest(uri)
        {
            BypassCache = bypassCache,
            CacheEnabled = this.CacheEnabled,
            CacheDuration = this.CacheDuration
        };

        ImageResult result;
        try
        {
            result = await this.ImageService.GetAsync(request, progress, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            result = ImageResult.Fail(ex);
        }

        if (!this.StillWanted(uri, token))
            return;

        if (!result.Success)
        {
            if (OperatingSystem.IsBrowser() && result.Error is HttpRequestException { StatusCode: null })
                await this.ShowCrossOriginAsync(uri, result.Error, token).ConfigureAwait(true);
            else
                this.ShowError(uri, result.Error ?? new InvalidOperationException("Image load failed"));
            return;
        }

        // The URL's extension is only a hint - plenty of endpoints serve /avatar/1234 and negotiate
        // the format - so the payload itself decides whether this is a vector.
        byte[]? vector;
        try
        {
            vector = await ReadVectorAsync(result, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            this.ShowError(uri, ex);
            return;
        }

        if (!this.StillWanted(uri, token))
            return;

        if (vector is not null)
        {
            await this.ShowVectorAsync(uri, vector, uri, bypassCache, result.Origin).ConfigureAwait(true);
            return;
        }

        var imageSource = BuildSource(result);
        if (imageSource is null)
        {
            this.ShowError(uri, new InvalidOperationException("Image loaded but produced no readable content"));
            return;
        }

        this.SetProgress(new ImageLoadProgress(ImageLoadState.Loaded, result.ContentLength ?? 0, result.ContentLength));
        await this.ShowImageAsync(imageSource).ConfigureAwait(true);
        this.RaiseLoaded(uri, result.Origin, result.ContentLength);
    }


    /// <summary>
    /// Hands a URL the browser would not let .NET read to the platform image instead.
    /// </summary>
    /// <remarks>
    /// In a browser the download is a <c>fetch</c>, and the page may only read the bytes when the
    /// server sends CORS headers - which plenty of image hosts (avatar services, CDNs) do not. An
    /// &lt;img&gt; needs no such permission to show the picture, so the platform image loads the URL
    /// itself, without the caches or a real percentage (both need the bytes). An unknown host or an
    /// offline browser fails the fetch the same way, so the image's own load decides: one that comes
    /// back with no size to show is the error it was.
    /// </remarks>
    async Task ShowCrossOriginAsync(string uri, Exception error, CancellationToken token)
    {
        this.SetProgress(new ImageLoadProgress(ImageLoadState.Downloading));

        var source = ImageSource.FromUri(new System.Uri(uri));
        // Asynchronous continuations, so the platform finishes handling its load (re-measuring the
        // image) before the size is read below.
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnImagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Image.IsLoading) && !this.targetImage.IsLoading)
                settled.TrySetResult();
        }

        this.targetImage.PropertyChanged += OnImagePropertyChanged;
        try
        {
            this.targetImage.Source = source;
            if (this.targetImage.IsLoading)
                await settled.Task.WaitAsync(token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Left set, a reloaded control would read this source as content it already has.
            if (this.targetImage.Source == source)
                this.targetImage.Source = null;
            return;
        }
        finally
        {
            this.targetImage.PropertyChanged -= OnImagePropertyChanged;
        }

        if (!this.StillWanted(uri, token))
            return;

        var natural = this.targetImage.Handler?.GetDesiredSize(double.PositiveInfinity, double.PositiveInfinity) ?? Size.Zero;
        if (natural.Width <= 0 || natural.Height <= 0)
        {
            this.ShowError(uri, error);
            return;
        }

        this.SetProgress(new ImageLoadProgress(ImageLoadState.Loaded));
        await this.ShowImageAsync(source).ConfigureAwait(true);
        this.RaiseLoaded(uri, ImageOrigin.Network, null);
    }


    /// <summary>
    /// Turns a service result into something MAUI can render.
    /// </summary>
    /// <remarks>
    /// Bytes are preferred over the file path when both are available - a stream over an in-memory
    /// array skips a file read per cell in a scrolling list. Crucially the stream is built from a
    /// <b>factory</b>, not a single stream instance: MAUI reads a stream source once and may re-read
    /// it after a handler is recycled, and a one-shot stream renders blank the second time.
    /// </remarks>
    static ImageSource? BuildSource(ImageResult result)
    {
        if (result.Bytes is { Length: > 0 } bytes)
            return ImageSource.FromStream(() => new MemoryStream(bytes));

        if (!String.IsNullOrWhiteSpace(result.FilePath) && File.Exists(result.FilePath))
            return ImageSource.FromFile(result.FilePath);

        return null;
    }


    async Task ShowImageAsync(ImageSource source)
    {
        this.showingVector = false;
        this.vectorDrawable.Document = null;
        this.vectorImage.Opacity = 0;

        this.targetImage.Source = source;
        this.ApplyState();

        await this.FadeInAsync(this.targetImage).ConfigureAwait(true);
    }


    async Task FadeInAsync(VisualElement element)
    {
        if (this.FadeInDuration == 0)
        {
            element.Opacity = 1;
            return;
        }

        element.Opacity = 0;
        await element.FadeToAsync(1, this.FadeInDuration).ConfigureAwait(true);
    }


    void ShowNothing()
    {
        this.showingVector = false;
        this.vectorDrawable.Document = null;
        this.vectorImage.Opacity = 0;
        this.targetImage.Source = null;
        this.targetImage.Opacity = 0;
        this.SetValue(LoadErrorPropertyKey, null);
        this.SetProgress(ImageLoadProgress.None);
    }


    void ShowError(string? uri, Exception error)
    {
        this.showingVector = false;
        this.vectorDrawable.Document = null;
        this.vectorImage.Opacity = 0;
        this.targetImage.Source = null;
        this.targetImage.Opacity = 0;
        this.SetValue(LoadErrorPropertyKey, error);
        this.SetProgress(new ImageLoadProgress(ImageLoadState.Failed));

        this.ImageFailed?.Invoke(this, new ImageFailedEventArgs(uri, error));

        var command = this.ImageFailedCommand;
        if (command?.CanExecute(error) == true)
            command.Execute(error);
    }


    void RaiseLoaded(string? uri, ImageOrigin origin, long? contentLength)
    {
        var args = new ImageLoadedEventArgs(uri, origin, contentLength);
        this.ImageLoaded?.Invoke(this, args);

        var command = this.ImageLoadedCommand;
        if (command?.CanExecute(args) == true)
            command.Execute(args);
    }


    /// <summary>
    /// Whether a load that has just come back is still the one this control wants shown.
    /// </summary>
    /// <remarks>
    /// The Uri check is the important one: it changed while this load was in flight (a recycled
    /// cell, or a fast rebind), and showing these bytes now would put the previous row's picture in
    /// the new row.
    /// </remarks>
    bool StillWanted(string uri, CancellationToken token)
        => !token.IsCancellationRequested &&
           !this.isDisposed &&
           String.Equals(this.Uri, uri, StringComparison.Ordinal);


    void SetProgress(ImageLoadProgress progress)
    {
        this.SetValue(ProgressPropertyKey, progress);
        this.SetValue(StatePropertyKey, progress.State);
        this.SetValue(IsLoadingPropertyKey, progress.State is ImageLoadState.Queued or ImageLoadState.Downloading);

        this.ring.Percent = progress.Percent;

        // A custom template binds against the progress record, and the record is replaced rather
        // than mutated, so the host's binding context has to be re-pointed each time.
        if (this.LoadingTemplate is not null)
            this.loadingHost.BindingContext = progress;

        if (this.ErrorTemplate is not null)
            this.errorHost.BindingContext = progress;

        this.ApplyState();
    }


    void ApplyState()
    {
        var state = this.State;
        var isLoading = state is ImageLoadState.Queued or ImageLoadState.Downloading;
        var isFailed = state == ImageLoadState.Failed;
        var isLoaded = state == ImageLoadState.Loaded;

        this.loadingHost.IsVisible = isLoading;
        this.errorHost.IsVisible = isFailed;

        // Exactly one of the two presenters is ever live: a raster image and a vector drawing occupy
        // the same cell of the grid.
        this.targetImage.IsVisible = isLoaded && !this.showingVector;
        this.vectorImage.IsVisible = isLoaded && this.showingVector;

        // Set on the ring itself, not just its host. Hiding a parent does not raise IsVisible on the
        // child, and the ring uses its own IsVisible to decide whether to hold the shared frame
        // clock - a loaded image reports a null Percent, which would otherwise read as "spinning
        // forever" and keep a 60fps timer awake on every page holding one.
        this.ring.IsVisible = isLoading;

        // The placeholder stays behind the ring rather than being swapped out for it, so a blurred
        // thumbnail with a progress ring over it is just two properties.
        this.placeholderImage.IsVisible = !isLoaded && !isFailed && this.PlaceholderImage is not null;
    }


    void RebuildLoadingContent()
    {
        var template = this.LoadingTemplate;
        if (template is null)
        {
            this.loadingHost.BindingContext = null;
            this.loadingHost.Content = this.ring;
            return;
        }

        this.loadingHost.Content = template.CreateContent() as View;
        this.loadingHost.BindingContext = this.Progress;
    }


    void RebuildErrorContent()
    {
        var template = this.ErrorTemplate;
        if (template is null)
        {
            this.errorHost.BindingContext = null;
            this.errorHost.Content = this.errorImage;
            return;
        }

        this.errorHost.Content = template.CreateContent() as View;
        this.errorHost.BindingContext = this.Progress;
    }


    void CancelPending()
    {
        var pending = Interlocked.Exchange(ref this.cts, null);
        if (pending is null)
            return;

        try
        {
            pending.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already torn down
        }
        pending.Dispose();
    }


    /// <summary>True for anything the image service should download.</summary>
    internal static bool IsRemote(string uri)
        => System.Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
           && (parsed.Scheme == System.Uri.UriSchemeHttp || parsed.Scheme == System.Uri.UriSchemeHttps);


    /// <summary>
    /// Finds a registered service, falling back to a self-built one.
    /// </summary>
    /// <remarks>
    /// The fallback is deliberate. A control constructed outside a running app - a headless test
    /// host, a design-time preview - has no MauiContext, and throwing there would make the control
    /// impossible to unit test and would crash the previewer over a service that has a perfectly
    /// good default.
    /// <para>
    /// The fallback is a factory rather than a <c>Lazy&lt;T&gt;</c>: CoreLib's <c>Lazy&lt;T&gt;</c> requires
    /// <c>T</c>'s parameterless constructor, so reading it through an open <c>T</c> is an IL2091 in every
    /// full-trim app - one the library build cannot see, because the reference assembly omits that
    /// annotation (issue #33).
    /// </para>
    /// </remarks>
    static T Resolve<T>(ShinyImage image, Func<T> fallback) where T : class
    {
        var services = image.Handler?.MauiContext?.Services
                       ?? Application.Current?.Handler?.MauiContext?.Services;

        return services?.GetService<T>() ?? fallback();
    }

    static readonly Lazy<IImageService> DefaultService = new(() => new ImageService());
    static readonly Lazy<SvgCache> DefaultSvgCache = new(() => new SvgCache());


    /// <summary>Cancels any in-flight load and releases the control's resources.</summary>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }


    /// <summary>Cancels any in-flight load and releases the control's resources.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (this.isDisposed || !disposing)
            return;

        this.isDisposed = true;
        this.Loaded -= this.OnControlLoaded;
        this.Unloaded -= this.OnControlUnloaded;
        this.CancelPending();
    }
}

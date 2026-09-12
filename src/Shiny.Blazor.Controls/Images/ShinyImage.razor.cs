using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls.Images;

/// <summary>
/// An image that always shows something: placeholder artwork, a loading ring - with a real download
/// percentage where the browser allows it - the image itself, or error artwork.
/// </summary>
/// <remarks>
/// <para>By default remote images are streamed through <c>fetch</c> so the ring can show a genuine
/// percentage, which a plain <c>&lt;img&gt;</c> can never do. When that fetch is blocked - almost
/// always a cross-origin server sending no CORS headers - the component falls back to letting the
/// browser load the URL directly and the ring simply stays indeterminate. The image still
/// appears.</para>
///
/// <para>Caching is left to the browser, which already does it well and shares it across tabs and
/// sessions. Register an <see cref="IImageDownloader"/> only when bytes must come through C# -
/// in practice, images behind a bearer token.</para>
/// </remarks>
public partial class ShinyImage : IAsyncDisposable
{
    /// <summary>
    /// What JS hands back from a streamed load.
    /// </summary>
    /// <remarks>
    /// A plain class with settable properties and no constructor, deliberately. Records and
    /// constructor-bound DTOs have bitten this repo before once trimming is on - the metadata the
    /// deserializer needs is exactly what the trimmer removes, and it fails only in a published
    /// build. Primitives only, no arrays.
    /// </remarks>
    public class JsLoadResult
    {
        /// <summary>The blob URL to display, or null.</summary>
        public string? Url { get; set; }

        /// <summary>Bytes received.</summary>
        public long ContentLength { get; set; }

        /// <summary>True when the caller should let the browser load the original URL itself.</summary>
        public bool DeferToBrowser { get; set; }

        /// <summary>Why the streamed load failed, if it did.</summary>
        public string? Error { get; set; }
    }

    IJSObjectReference? module;
    DotNetObjectReference<ShinyImage>? selfRef;
    CancellationTokenSource? cts;
    IImageDownloader? downloader;

    string? resolvedSrc;
    string? blobUrl;
    string? loadedUri;
    int requestId;
    bool firstRenderDone;
    ImageLoadState state = ImageLoadState.None;


    /// <summary>The image to load. Relative and absolute URLs both work.</summary>
    [Parameter] public string? Uri { get; set; }

    /// <summary>Artwork shown before and during the load, behind the loading ring.</summary>
    [Parameter] public string? PlaceholderUri { get; set; }

    /// <summary>Artwork shown when the load fails. A built-in glyph is used when neither this nor <see cref="ErrorContent"/> is set.</summary>
    [Parameter] public string? ErrorUri { get; set; }

    /// <summary>Alt text for the image.</summary>
    [Parameter] public string? Alt { get; set; }

    /// <summary>CSS <c>object-fit</c> for the image. <c>cover</c> and <c>contain</c> are the usual choices.</summary>
    [Parameter] public string ObjectFit { get; set; } = "contain";

    /// <summary>Milliseconds the image fades in over. Zero shows it instantly.</summary>
    [Parameter] public int FadeInDuration { get; set; } = 150;

    /// <summary>
    /// Replaces the built-in loading ring. The fragment's context is the live
    /// <see cref="ImageLoadProgress"/>, so <c>State</c>, <c>Percent</c> and <c>BytesRead</c> are all
    /// available.
    /// </summary>
    [Parameter] public RenderFragment<ImageLoadProgress>? LoadingContent { get; set; }

    /// <summary>Replaces the built-in error artwork.</summary>
    [Parameter] public RenderFragment<ImageLoadProgress>? ErrorContent { get; set; }

    /// <summary>Diameter of the loading ring in pixels.</summary>
    [Parameter] public double RingSize { get; set; } = 48;

    /// <summary>Whether the percentage is drawn inside the ring. Never shown when indeterminate.</summary>
    [Parameter] public bool ShowProgressText { get; set; } = true;

    /// <summary>Colour of the ring's progress arc.</summary>
    [Parameter] public string RingColor { get; set; } = "var(--shiny-color-primary, #0055D9)";

    /// <summary>Colour of the ring's unfilled track.</summary>
    [Parameter] public string RingTrackColor { get; set; } = "var(--shiny-color-surface-container-highest, #D7DCE1)";

    /// <summary>Colour of the ring's percentage label.</summary>
    [Parameter] public string ProgressTextColor { get; set; } = "var(--shiny-color-on-surface, #161C23)";

    /// <summary>
    /// Skips the streamed fetch and lets the browser load the URL directly. No percentage, but no
    /// CORS exposure and one less copy of the bytes - worth it for same-origin thumbnails.
    /// </summary>
    [Parameter] public bool DisableProgress { get; set; }

    /// <summary>The glyph shown when a load fails and no error artwork was supplied.</summary>
    [Parameter] public string ErrorGlyph { get; set; } = "🖼";

    /// <summary>Extra CSS classes for the wrapper.</summary>
    [Parameter] public string? CssClass { get; set; }

    /// <summary>Raised once the image is on screen.</summary>
    [Parameter] public EventCallback<string?> ImageLoaded { get; set; }

    /// <summary>Raised with a description when a load fails.</summary>
    [Parameter] public EventCallback<string> ImageFailed { get; set; }

    /// <summary>Anything else lands on the wrapper element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }


    /// <summary>The live progress snapshot - also the context of <see cref="LoadingContent"/>.</summary>
    public ImageLoadProgress Progress { get; private set; } = ImageLoadProgress.None;

    /// <summary>Where the current load is.</summary>
    public ImageLoadState State => this.state;

    bool IsLoading => this.state is ImageLoadState.Queued or ImageLoadState.Downloading;

    bool ShowPlaceholder =>
        !String.IsNullOrWhiteSpace(this.PlaceholderUri) &&
        this.state is not (ImageLoadState.Loaded or ImageLoadState.Failed);

    /// <summary>
    /// The wrapper's own style, with any caller-supplied <c>style</c> appended.
    /// </summary>
    /// <remarks>
    /// Blazor does not merge two <c>style</c> attributes on one element - the splatted one replaces
    /// the literal one. Leaving the caller's style in <c>AdditionalAttributes</c> therefore erased
    /// <c>--shiny-img-fit</c>, and every image silently fell back to <c>contain</c> the moment
    /// someone wrote <c>style="height:100%"</c>. Concatenating puts the caller's declarations last,
    /// so they still win on any property they actually set.
    /// </remarks>
    string RootStyle
    {
        get
        {
            var own = $"--shiny-img-fit: {this.ObjectFit};";

            if (this.AdditionalAttributes?.TryGetValue("style", out var caller) != true)
                return own;

            var text = caller?.ToString();
            return String.IsNullOrWhiteSpace(text) ? own : own + " " + text;
        }
    }


    /// <summary>Everything the caller passed except <c>style</c>, which <see cref="RootStyle"/> owns.</summary>
    IReadOnlyDictionary<string, object>? SplattedAttributes
    {
        get
        {
            if (this.AdditionalAttributes is null)
                return null;

            if (!this.AdditionalAttributes.ContainsKey("style"))
                return this.AdditionalAttributes.AsReadOnly();

            return this.AdditionalAttributes
                .Where(pair => !String.Equals(pair.Key, "style", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }
    }

    string FadeStyle => $"transition-duration: {Math.Max(0, this.FadeInDuration)}ms;";


    /// <inheritdoc />
    protected override void OnInitialized()
        // Resolved rather than injected so the component works with nothing registered - which is the
        // normal case, since the browser handles unauthenticated images perfectly well on its own.
        => this.downloader = this.Services.GetService<IImageDownloader>();


    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (!this.firstRenderDone || String.Equals(this.Uri, this.loadedUri, StringComparison.Ordinal))
            return;

        await this.StartLoadAsync().ConfigureAwait(true);
    }


    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        this.module = await this.JS
            .InvokeAsync<IJSObjectReference>("import", "./_content/Shiny.Blazor.Controls/shiny-image.js")
            .ConfigureAwait(true);

        this.selfRef = DotNetObjectReference.Create(this);
        this.firstRenderDone = true;

        await this.StartLoadAsync().ConfigureAwait(true);
    }


    /// <summary>Re-fetches the image, adding a cache-busting parameter so the browser really re-asks.</summary>
    public Task ReloadAsync() => this.StartLoadAsync(true);


    async Task StartLoadAsync(bool bustCache = false)
    {
        await this.CancelPendingAsync().ConfigureAwait(true);
        await this.ReleaseBlobAsync().ConfigureAwait(true);

        this.loadedUri = this.Uri;
        this.resolvedSrc = null;

        if (String.IsNullOrWhiteSpace(this.Uri))
        {
            this.SetProgress(ImageLoadProgress.None);
            return;
        }

        var uri = bustCache ? AppendCacheBuster(this.Uri) : this.Uri;
        var id = ++this.requestId;

        this.cts = new CancellationTokenSource();
        var token = this.cts.Token;

        this.SetProgress(ImageLoadProgress.Queued);

        try
        {
            var src = this.downloader is not null
                ? await this.LoadViaDownloaderAsync(uri, token).ConfigureAwait(true)
                : await this.LoadViaBrowserAsync(uri, id, token).ConfigureAwait(true);

            // A newer load started while this one was in flight - showing these bytes now would put
            // the previous URL's picture under the current one's alt text.
            if (id != this.requestId || token.IsCancellationRequested)
                return;

            if (src is null)
            {
                await this.FailAsync("Image could not be decoded").ConfigureAwait(true);
                return;
            }

            this.resolvedSrc = src;
            this.SetProgress(new ImageLoadProgress(ImageLoadState.Loaded, this.Progress.BytesRead, this.Progress.TotalBytes));
            await this.ImageLoaded.InvokeAsync(this.Uri).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // superseded or disposed
        }
        catch (Exception ex)
        {
            if (id == this.requestId)
                await this.FailAsync(ex.Message).ConfigureAwait(true);
        }
    }


    async Task<string?> LoadViaDownloaderAsync(string uri, CancellationToken token)
    {
        var progress = new Progress<ImageLoadProgress>(this.SetProgress);
        var result = await this.downloader!.DownloadAsync(uri, progress, token).ConfigureAwait(true);

        if (this.module is null || result.Bytes.Length == 0)
            return null;

        // The bytes exist only in .NET memory, so they need a URL the DOM can point at. A blob URL
        // beats a data: URI here - a base64 data URI is a third larger and lands in the DOM as one
        // enormous attribute string.
        this.blobUrl = await this.module
            .InvokeAsync<string>("createBlobUrl", token, result.Bytes, result.ContentType)
            .ConfigureAwait(true);

        return await this.DecodeAsync(this.blobUrl, token).ConfigureAwait(true);
    }


    async Task<string?> LoadViaBrowserAsync(string uri, int id, CancellationToken token)
    {
        if (this.module is null)
            return null;

        // Streaming is only worth it for something the browser would fetch over the network anyway,
        // and only when the caller wants a percentage.
        if (this.DisableProgress || !IsRemote(uri))
        {
            this.SetProgress(new ImageLoadProgress(ImageLoadState.Downloading));
            return await this.DecodeAsync(uri, token).ConfigureAwait(true);
        }

        this.SetProgress(new ImageLoadProgress(ImageLoadState.Downloading));

        var result = await this.module
            .InvokeAsync<JsLoadResult>("load", token, this.selfRef, uri, id)
            .ConfigureAwait(true);

        if (result.DeferToBrowser)
        {
            // CORS, almost always. The browser can still render this through a plain <img>; all that
            // is lost is the percentage, so the ring goes indeterminate and the image appears.
            this.SetProgress(new ImageLoadProgress(ImageLoadState.Downloading));
            return await this.DecodeAsync(uri, token).ConfigureAwait(true);
        }

        if (result.Url is null)
        {
            // Surface what the fetch actually said - "HTTP 404" is the difference between a broken
            // link and a broken decoder, and swallowing it into a generic message throws away the
            // only diagnostic the caller gets.
            throw new InvalidOperationException(result.Error ?? "Image request failed");
        }

        this.blobUrl = result.Url;
        return await this.DecodeAsync(result.Url, token).ConfigureAwait(true);
    }


    /// <summary>
    /// Confirms the URL actually decodes as an image before it is put on screen.
    /// </summary>
    /// <remarks>
    /// This is done in JS rather than with Blazor's <c>@onload</c>/<c>@onerror</c> on the element.
    /// The DOM <c>load</c> and <c>error</c> events do not bubble, so they do not survive Blazor's
    /// delegated event plumbing dependably - and the failure mode is a component that never learns
    /// its image arrived and shows a spinner forever. The second read is free: the URL is either a
    /// local blob or already in the HTTP cache.
    /// </remarks>
    async Task<string?> DecodeAsync(string? url, CancellationToken token)
    {
        if (url is null || this.module is null)
            return null;

        var ok = await this.module.InvokeAsync<bool>("preload", token, url).ConfigureAwait(true);
        return ok ? url : null;
    }


    /// <summary>Progress from the JS streaming reader.</summary>
    [JSInvokable]
    public void OnProgress(long bytesRead, long totalBytes)
    {
        // Primitives, not a DTO. Interop payloads in this repo have been broken by trimming before,
        // and two numbers cannot be.
        this.SetProgress(new ImageLoadProgress(
            ImageLoadState.Downloading,
            bytesRead,
            totalBytes > 0 ? totalBytes : null
        ));
    }


    void SetProgress(ImageLoadProgress progress)
    {
        this.Progress = progress;
        this.state = progress.State;
        this.InvokeAsync(this.StateHasChanged);
    }


    async Task FailAsync(string message)
    {
        this.resolvedSrc = null;
        this.SetProgress(new ImageLoadProgress(ImageLoadState.Failed));
        await this.ImageFailed.InvokeAsync(message).ConfigureAwait(true);
    }


    async Task CancelPendingAsync()
    {
        var pending = this.cts;
        this.cts = null;

        if (pending is null)
            return;

        await pending.CancelAsync().ConfigureAwait(true);
        pending.Dispose();

        if (this.module is not null)
        {
            try
            {
                await this.module.InvokeVoidAsync("abort", this.requestId).ConfigureAwait(true);
            }
            catch (JSDisconnectedException)
            {
                // circuit already gone
            }
        }
    }


    /// <summary>
    /// Hands a blob URL back to the browser.
    /// </summary>
    /// <remarks>
    /// Not optional. A blob URL keeps its bytes alive until it is explicitly revoked, so a list that
    /// creates one per image and never releases them grows the tab's memory by the full weight of
    /// every image ever scrolled past.
    /// </remarks>
    async Task ReleaseBlobAsync()
    {
        var url = this.blobUrl;
        this.blobUrl = null;

        if (url is null || this.module is null)
            return;

        try
        {
            await this.module.InvokeVoidAsync("revoke", url).ConfigureAwait(true);
        }
        catch (JSDisconnectedException)
        {
            // circuit already gone; the whole document is going with it
        }
    }


    static bool IsRemote(string uri)
        => System.Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
           && (parsed.Scheme == System.Uri.UriSchemeHttp || parsed.Scheme == System.Uri.UriSchemeHttps);


    static string AppendCacheBuster(string uri)
        => uri + (uri.Contains('?') ? '&' : '?') + "_shiny=" + DateTime.UtcNow.Ticks;


    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await this.CancelPendingAsync().ConfigureAwait(true);
        await this.ReleaseBlobAsync().ConfigureAwait(true);

        if (this.module is not null)
        {
            try
            {
                await this.module.DisposeAsync().ConfigureAwait(true);
            }
            catch (JSDisconnectedException)
            {
                // circuit already gone
            }
        }

        this.selfRef?.Dispose();
        GC.SuppressFinalize(this);
    }
}

# ShinyImage

[← All Shiny Controls](../../README.md)

A remote image that always shows *something* — placeholder artwork, a loading ring, the image, or
error artwork — with the download itself under your control on MAUI. On MAUI it also renders **SVG**,
as vectors rather than as a rasterized bitmap.

<!-- TODO: capture screenshots for shiny-image -->

```xml
<shiny:ShinyImage Uri="{Binding PhotoUrl}"
                  PlaceholderImage="placeholder.png"
                  ErrorImage="broken_image.png"
                  Aspect="AspectFill"
                  HeightRequest="220" />
```

```razor
<ShinyImage Uri="@PhotoUrl"
            PlaceholderUri="/images/placeholder.svg"
            Alt="Profile photo"
            ObjectFit="cover" />
```

**The loading ring picks its own mode.** When the response carries a `Content-Length` the ring fills
to a real percentage; when it does not — a chunked response, or a request still waiting for a
download slot — the same ring spins instead. There is nothing to configure: `Percent` is null in
exactly the cases where a percentage would be a lie.

| Property | Type | Description |
|---|---|---|
| Uri | string? | The image to load. `http`/`https` goes through `IImageService`; `resource://` reads an embedded resource; `data:` is decoded inline; anything else is a file path or bundled asset |
| Source | ImageSource? | An explicit source (MAUI). Takes precedence over `Uri` and skips the service entirely |
| PlaceholderImage / PlaceholderUri | ImageSource? / string? | Artwork shown before and during the load, **behind** the ring |
| ErrorImage / ErrorUri | ImageSource? / string? | Artwork shown when the load fails |
| LoadingTemplate / LoadingContent | DataTemplate? / RenderFragment&lt;ImageLoadProgress&gt;? | Replaces the ring entirely; context is the live progress |
| ErrorTemplate / ErrorContent | DataTemplate? / RenderFragment&lt;ImageLoadProgress&gt;? | Replaces the error artwork |
| Aspect / ObjectFit | Aspect / string | How the image scales (default `AspectFit` / `contain`) |
| FadeInDuration | uint / int | Fade-in milliseconds once loaded (default 150) |
| RingSize, RingColor, RingTrackColor, ProgressTextColor, ShowProgressText | — | Ring appearance; colours fall back to theme tokens |
| CacheEnabled, CacheDuration | bool, TimeSpan? | Per-image cache participation and expiry override (MAUI) |
| DisableProgress | bool | Blazor: skip the streamed fetch and let the browser load the URL directly |
| State, Progress, IsLoading, LoadError | read-only | The live load state. A progress report the service raised before it returned is dropped if it arrives after the load finished, so a loaded image never falls back to `Downloading` |
| ImageLoaded / ImageFailed | event / EventCallback | Completion callbacks (plus `ImageLoadedCommand` / `ImageFailedCommand` on MAUI) |
| SvgTintColor | Color? | MAUI: what `currentColor` resolves to in an SVG. Ignored by rasters and by artwork with its own colours |
| ReloadAsync() | method | Re-fetch, skipping the cache — and, for SVG, re-parse |

## SVG (MAUI)

SVG is drawn rather than rasterized: one file stays sharp at every size, and a single-colour icon can
be tinted per placement. The format is detected from the payload, not the file extension, so an
endpoint that serves vectors from a URL that does not say so still works.

```xml
<!-- an embedded resource, tinted from the theme -->
<shiny:ShinyImage Uri="resource://MyApp.Assets.logo.svg"
                  SvgTintColor="{StaticResource Primary}"
                  HeightRequest="48" />

<!-- a file on disk, a bundled asset, or a remote URL - all the same property -->
<shiny:ShinyImage Uri="/var/mobile/.../chart.svg" />
<shiny:ShinyImage Uri="art/logo.svg" />
<shiny:ShinyImage Uri="https://cdn.example.com/logo.svg" />
```

`resource://Name` searches the app assembly, then the entry assembly, then everything loaded, matching
the manifest name exactly and then by suffix — so `resource://Assets.logo.svg` finds
`MyApp.Assets.logo.svg`. Write `resource://MyLib/MyLib.Assets.logo.svg` to name the assembly outright.

**Parsing is cached, not just the bytes.** `IImageService` already stops the same URL being downloaded
twice, but turning bytes into geometry is an XML parse, a path-data parse per shape and a bounds
measurement per shape — pure CPU on the UI thread, repeated for every cell in a list showing the same
icon. Parsed documents are immutable and shared through an LRU `SvgCache`, so a hundred rows showing
one icon parse it once. `SvgTintColor` is applied at draw time rather than baked in, so placements
that disagree about the colour still share a parse.

```csharp
builder.UseShinyControls(cfg => cfg.ConfigureImages(o => o.SvgCacheEntryLimit = 32));
```

**What is drawn:** `path`, `rect`, `circle`, `ellipse`, `line`, `polyline`, `polygon`, `text`, `g`,
`use`, `symbol`, `defs`, `switch`, `clipPath`, linear and radial gradients, presentation attributes,
the `style` attribute, and the type/class/id rules inside a `<style>` element — which is how
Illustrator, Figma and Sketch export shared appearance. `.svgz` is decompressed transparently.

**What is not:** filters, masks, patterns, markers, embedded raster `<image>`, SMIL and CSS animation,
and external references of any kind (an image file never becomes a fetch). An unsupported element is
skipped rather than approximated, so it costs that element and nothing else. Gradient strokes fall
back to their first stop — `ICanvas` fills with a paint but strokes with a colour.

Blazor needs none of this: the browser renders SVG natively through `<img>`.

## ImageService (MAUI)

Downloads go through `IImageService`, which caches to memory and disk, caps concurrency, and
collapses concurrent requests for the same URI into a single download — the difference between a
scrolling list issuing one request per unique image and one per visible cell.

```csharp
builder.UseShinyControls(cfg => cfg
    .ConfigureImages(o =>
    {
        o.MaxConcurrentDownloads = 4;                 // requests past this report Queued
        o.DiskCacheDuration      = TimeSpan.FromDays(7);
        o.MaxDiskCacheBytes      = 100 * 1024 * 1024; // LRU-trimmed
        o.MemoryCacheEnabled     = true;
        o.MaxMemoryCacheBytes    = 32 * 1024 * 1024;
        o.MaxMemoryItemBytes     = 2 * 1024 * 1024;   // bigger images stay disk-only
        o.ServeStaleOnError      = true;              // offline? show the expired copy
        o.SvgCacheEntryLimit     = 32;                // parsed SVG documents kept
    })
);
```

**How the disk cache decides.** An entry lives as long as the server's `Cache-Control: max-age` or
`Expires` says, otherwise `DiskCacheDuration`. An expired entry is **revalidated, not thrown away**:
the next request carries its `ETag` / `Last-Modified` as `If-None-Match` / `If-Modified-Since`, and a
`304 Not Modified` extends the entry without downloading the image again. If the refresh fails
(offline, timeout, 5xx), the stale copy is shown instead of the error artwork; set
`ServeStaleOnError = false` to fail instead. A custom `IImageDownloader` can honour the same
validators: send `request.IfNoneMatch` / `request.IfModifiedSince` and return
`new ImageDownloadResult(Stream.Null, NotModified: true)` on a 304. One that ignores them still works
and just re-downloads.

**One request per URL.** Everything past the memory tier is shared per URI: a dozen cells asking for
one avatar make one disk read, or one request (download or revalidation), and every cell gets its
result and its progress. A forced reload (`ReloadAsync`) and an uncached image never share with a
normal load.

**Clock.** Expiry, last-access stamps and progress throttling read an injected `TimeProvider`.
Register one in DI (`builder.Services.AddSingleton<TimeProvider>(...)`) to control it in tests; the
default is `TimeProvider.System`.

```csharp
// Clear cached images — e.g. from a "free up space" setting
await imageService.ClearCacheAsync();
await imageService.ClearCacheAsync(oneUrl);
var bytes = await imageService.GetCacheSizeAsync();
await imageService.PrefetchAsync(nextPageUrls);
```

**Bring your own `HttpClient`.** For authenticated images — the one thing a plain `Image` or `<img>`
genuinely cannot do — replace `IImageDownloader`, not the whole service. Caching, queueing and
de-duplication stay where they are:

```csharp
class AuthenticatedDownloader(HttpClient client, ITokenStore tokens) : IImageDownloader
{
    public async Task<ImageDownloadResult> DownloadAsync(ImageRequest request, CancellationToken ct)
    {
        var msg = new HttpRequestMessage(HttpMethod.Get, request.Uri);
        msg.Headers.Authorization = new("Bearer", await tokens.GetAsync(ct));

        var response = await client.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        return new ImageDownloadResult(
            await response.Content.ReadAsStreamAsync(ct),
            response.Content.Headers.ContentLength   // this is what makes the ring determinate
        );
    }
}

// builder.UseShinyControls(cfg => cfg.SetCustomImageDownloader<AuthenticatedDownloader>());
// cfg.SetCustomImageService<T>() replaces the whole pipeline, caching included.
```

The memory tier caches **encoded bytes, not `ImageSource` objects** — a stream-backed `ImageSource`
is consumed once and platform image handles are bound to the view that realized them, so sharing one
across cells renders blank. Each control builds a fresh `ImageSource.FromStream` from the cached
bytes, which is free; the network round-trip is what was skipped.

## Blazor

There is no disk cache layer on Blazor and that is deliberate — the browser's HTTP cache already is
one: it lives on disk, is shared across tabs and sessions, and revalidates with `ETag` /
`Last-Modified` on its own. What Blazor *does* add:

- **One fetch per URL.** Every `ShinyImage` showing the same URL rides one `fetch` and one blob. A late
  arrival joins the fetch in flight (and is told how far it has got) or, once it has finished, gets the
  same blob straight away. The blob is released when the last image showing it goes. One image leaving
  the screen never cancels a fetch others are still waiting on. With an `IImageDownloader` registered
  the same holds: one download per URL per scope (per user on Blazor Server), one blob.
- **Progress:
`ShinyImage` streams remote images through `fetch` and a `ReadableStream` reader so the ring can show
a genuine percentage, which no `<img>` element can report.

> **CORS caveat.** A plain `<img>` may load a cross-origin image with no server cooperation; `fetch`
> may not. When the streamed load is blocked, the component silently falls back to letting the
> browser load the URL directly — the image still appears, the ring just stays indeterminate. Set
> `DisableProgress="true"` to take that path deliberately.

For authenticated images, register a downloader:

```csharp
builder.Services.AddShinyImages();                          // uses the registered HttpClient
builder.Services.AddShinyImages<AuthenticatedDownloader>(); // or your own
```

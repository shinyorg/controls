using System.ComponentModel;
using Microsoft.JSInterop;
using Shiny.Controls.Camera;

namespace Shiny.Blazor.Controls.Camera.Media;

/// <summary>The encoding a captured or picked photo is handed back in.</summary>
public enum MediaImageFormat
{
    /// <summary>Lossy JPEG at the requested compression rate. The default — far smaller for photographs.</summary>
    Jpeg,

    /// <summary>Lossless PNG. Compression rate is ignored (PNG has no quality knob).</summary>
    Png
}


/// <summary>
/// The outcome of a permission request — the same four answers as the MAUI service, so shared view-model code
/// branches the same way on both hosts.
/// </summary>
public enum MediaPermissionStatus
{
    /// <summary>Access is granted.</summary>
    Granted,

    /// <summary>The user (or the browser, for this site) declined. Browsers offer no way to reopen the prompt — the user has to change it in the address bar's site settings.</summary>
    Denied,

    /// <summary>A permissions policy or an insecure (non-HTTPS) context forbids it. Asking again will not help.</summary>
    Restricted,

    /// <summary>There is no camera (or no <c>getUserMedia</c>) here.</summary>
    Unsupported
}


/// <summary>
/// A still image produced by <see cref="IMediaService"/> — captured through the modal camera or picked from
/// the gallery — already re-encoded to the requested <see cref="MediaImageFormat"/>, compression rate and
/// maximum dimension.
/// </summary>
/// <param name="Data">The encoded image bytes, in <paramref name="ContentType"/>.</param>
/// <param name="Width">Pixel width of the (possibly downscaled) image.</param>
/// <param name="Height">Pixel height of the (possibly downscaled) image.</param>
/// <param name="ContentType">MIME type of <paramref name="Data"/> — <c>image/jpeg</c> or <c>image/png</c>.</param>
/// <param name="FileName">The picked file's name; null for a camera capture.</param>
public record MediaPhoto(byte[] Data, int Width, int Height, string ContentType = "image/jpeg", string? FileName = null)
{
    /// <summary>Open a fresh read-only stream over the encoded bytes.</summary>
    public Stream OpenRead() => new MemoryStream(this.Data, false);

    /// <summary>A <c>data:</c> URL for an <c>&lt;img src&gt;</c>. Fine for a thumbnail; for large images prefer uploading the bytes.</summary>
    public string ToDataUrl() => $"data:{this.ContentType};base64,{Convert.ToBase64String(this.Data)}";
}


/// <summary>
/// A video produced by <see cref="IMediaService"/>. The bytes stay in the browser — a minute of video is tens
/// of megabytes, and pulling that into .NET (or across a Blazor Server circuit) should be a deliberate choice,
/// so <see cref="Url"/> plays it with no copy at all and <see cref="OpenReadAsync"/> streams it when you want
/// it. Dispose it when done to free the browser memory behind it.
/// </summary>
public sealed class MediaVideo : IAsyncDisposable
{
    readonly IJSObjectReference module;
    readonly string id;
    bool disposed;

    internal MediaVideo(IJSObjectReference module, MediaBlobInfo info)
    {
        this.module = module;
        this.id = info.Id;
        this.Url = info.Url;
        this.Length = info.Size;
        this.ContentType = String.IsNullOrEmpty(info.ContentType) ? "video/webm" : info.ContentType;
        this.Duration = info.DurationMs >= 0 ? TimeSpan.FromMilliseconds(info.DurationMs) : null;
        this.Width = info.Width;
        this.Height = info.Height;
        this.FileName = info.Name;
    }

    /// <summary>An object URL for <c>&lt;video src&gt;</c>. Valid until this is disposed.</summary>
    public string Url { get; }

    /// <summary>Size in bytes.</summary>
    public long Length { get; }

    /// <summary>MIME type — <c>video/webm</c> from Chromium/Firefox recordings, <c>video/mp4</c> from Safari, or whatever was picked.</summary>
    public string ContentType { get; }

    /// <summary>Length of the video when known.</summary>
    public TimeSpan? Duration { get; }

    /// <summary>Pixel width when known, else 0.</summary>
    public int Width { get; }

    /// <summary>Pixel height when known, else 0.</summary>
    public int Height { get; }

    /// <summary>The picked file's name; null for a recording.</summary>
    public string? FileName { get; }

    /// <summary>Stream the bytes into .NET. Blazor reads them in chunks, so this is safe on Blazor Server too.</summary>
    public async Task<Stream> OpenReadAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        var reference = await this.module.InvokeAsync<IJSStreamReference>("read", ct, this.id);
        return await reference.OpenReadStreamAsync(this.Length + 1, ct);
    }

    /// <summary>Free the browser-side blob and revoke <see cref="Url"/>.</summary>
    public async ValueTask DisposeAsync()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        try
        {
            await this.module.InvokeVoidAsync("release", this.id);
        }
        catch (JSDisconnectedException) { /* circuit gone — the page took the blob with it */ }
        catch (ObjectDisposedException) { /* service already disposed */ }
    }
}


/// <summary>
/// One entry in the modal camera's effect strip — a <see cref="CameraFilter"/> colour grade or a full
/// <see cref="ICameraEffect"/>.
/// </summary>
/// <param name="Name">The chip caption.</param>
/// <param name="Filter">The colour grade applied to <see cref="CameraView.Filter"/>.</param>
/// <param name="Effect">An effect applied on top of the filter.</param>
public record MediaEffectChoice(string Name, CameraFilter Filter = CameraFilter.None, ICameraEffect? Effect = null);


/// <summary>
/// Descriptor for a blob kept browser-side (a capture, a recording or a picked file). JS-interop DTO — public
/// only because System.Text.Json needs the accessors; not something to construct yourself.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class MediaBlobInfo
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public long Size { get; set; }
    public string ContentType { get; set; } = "";
    public double DurationMs { get; set; } = -1;
    public string? Name { get; set; }
}

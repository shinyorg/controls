namespace Shiny.Maui.Controls.Camera.Media;

/// <summary>The encoding a captured or picked photo is handed back in.</summary>
public enum MediaImageFormat
{
    /// <summary>Lossy JPEG at the requested compression rate. The default — far smaller for photographs.</summary>
    Jpeg,

    /// <summary>Lossless PNG. Compression rate is ignored (PNG has no quality knob).</summary>
    Png
}


/// <summary>
/// The outcome of a permission request. Deliberately not MAUI's <c>PermissionStatus</c>: the distinctions
/// that change what an app does are "you may proceed", "the user said no", "policy forbids it so asking is
/// pointless", and "there is no such capability here".
/// </summary>
/// <remarks>
/// There is intentionally no <c>PermanentlyDenied</c>. Whether a second request will re-prompt is not
/// knowable the same way on both platforms — iOS silently returns the previous refusal, Android answers
/// through <c>ShouldShowRationale</c> — so a value claiming to mean it would be right on one head and a
/// guess on the other. Treat <see cref="Denied"/> as "offer them
/// <see cref="IMediaService.OpenSettingsAsync"/>", which is correct either way.
/// </remarks>
public enum MediaPermissionStatus
{
    /// <summary>Access is granted. iOS's <i>limited</i> photo selection also lands here — the user picked what you may see, and you may proceed.</summary>
    Granted,

    /// <summary>The user declined. A further request may or may not re-prompt; <see cref="IMediaService.OpenSettingsAsync"/> always works.</summary>
    Denied,

    /// <summary>Policy (MDM, parental controls, a disabled feature) forbids it. Asking again will not help.</summary>
    Restricted,

    /// <summary>There is no such capability on this platform — e.g. the bare <c>net10.0</c> head, which has no camera.</summary>
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
public record MediaPhoto(byte[] Data, int Width, int Height, string ContentType = "image/jpeg")
{
    /// <summary>Open a fresh read-only stream over the encoded bytes.</summary>
    public Stream OpenRead() => new MemoryStream(this.Data, false);

    /// <summary>An <see cref="ImageSource"/> for binding/display; yields a new stream on each load.</summary>
    public ImageSource AsImageSource() => ImageSource.FromStream(() => new MemoryStream(this.Data, false));

    /// <summary>Write the bytes to <paramref name="filePath"/>, returning the path written.</summary>
    public async Task<string> SaveAsync(string filePath, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!String.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllBytesAsync(filePath, this.Data, ct).ConfigureAwait(false);
        return filePath;
    }
}


/// <summary>
/// A video produced by <see cref="IMediaService"/>. Video stays a <i>file</i> rather than a byte array
/// because a minute of 1080p is hundreds of megabytes and nothing good comes of holding that in memory.
/// </summary>
/// <param name="FilePath">Absolute path to the recorded/picked file.</param>
/// <param name="Duration">Length of the recording when known.</param>
/// <param name="ContentType">MIME type — <c>video/quicktime</c> on Apple, <c>video/mp4</c> elsewhere.</param>
public record MediaVideo(string FilePath, TimeSpan? Duration = null, string ContentType = "video/mp4")
{
    /// <summary>Open a read stream over the file.</summary>
    public Stream OpenRead() => File.OpenRead(this.FilePath);

    /// <summary>Size of the file on disk in bytes, or 0 when it no longer exists.</summary>
    public long Length => File.Exists(this.FilePath) ? new FileInfo(this.FilePath).Length : 0;
}


/// <summary>
/// One entry in the modal camera's effect strip. A look is either a <see cref="CameraFilter"/> colour grade
/// or a full <see cref="ICameraEffect"/> (a spatial/GPU look) — the strip presents both because that is how
/// a person picking a look thinks about it, even though they are different APIs underneath.
/// </summary>
/// <param name="Name">The chip caption.</param>
/// <param name="Filter">The colour grade applied to <see cref="CameraView.Filter"/>.</param>
/// <param name="Effect">An effect added to <see cref="CameraView.Effects"/> on top of the filter.</param>
public record MediaEffectChoice(string Name, CameraFilter Filter = CameraFilter.None, ICameraEffect? Effect = null);

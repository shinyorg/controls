namespace Shiny.Blazor.Controls;


/// <summary>
/// A single photo held by a <see cref="MediaPickerButton"/>, already compressed/converted
/// to the button's output format.
/// </summary>
/// <remarks>
/// The bytes live in the browser until something asks for them. <see cref="Data"/> is filled in for
/// you unless the button is uploading them itself (see <c>MediaPickerButton.UploadUrl</c>), and even
/// then they are streamed rather than base64-encoded — a photo that crossed as one base64 message
/// was big enough to break a Blazor Server circuit.
/// </remarks>
public sealed class MediaPickerItem
{
    /// <summary>Identifies the photo to the browser for the life of the page.</summary>
    public string Id { get; set; } = "";

    /// <summary>
    /// Encoded image bytes in <see cref="ContentType"/>, empty while they are still only in the
    /// browser. <see cref="ReadAllBytesAsync"/> fetches them on demand.
    /// </summary>
    public byte[] Data { get; set; } = [];

    /// <summary>An object URL over the photo, for binding straight to <c>&lt;img src&gt;</c>.</summary>
    public string DataUri { get; set; } = "";

    /// <summary>Pixel width of the (possibly resized) image.</summary>
    public int Width { get; set; }

    /// <summary>Pixel height of the (possibly resized) image.</summary>
    public int Height { get; set; }

    /// <summary>MIME type of the photo (e.g. <c>image/jpeg</c>).</summary>
    public string ContentType { get; set; } = "";

    /// <summary>Encoded size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>The name of the file it was picked from, when the browser said.</summary>
    public string FileName { get; set; } = "";

    /// <summary>True once <see cref="Data"/> holds the bytes.</summary>
    public bool HasData => this.Data.Length > 0;

    /// <summary>Reads the bytes from the browser, in chunks. Set by the button that made this item.</summary>
    internal Func<long, CancellationToken, Task<Stream>>? Opener { get; set; }


    /// <summary>
    /// Opens the photo's bytes as a stream, straight from the browser — for handing to an
    /// <c>HttpContent</c> or writing to disk without holding the whole thing twice.
    /// </summary>
    /// <param name="maxAllowedSize">Refuses anything larger. 32MB by default.</param>
    public Task<Stream> OpenReadStreamAsync(long maxAllowedSize = 32 * 1024 * 1024, CancellationToken cancellationToken = default)
    {
        if (this.Data.Length > 0)
            return Task.FromResult<Stream>(new MemoryStream(this.Data, false));

        if (this.Opener == null)
            throw new InvalidOperationException("This photo's bytes are no longer available.");

        return this.Opener(maxAllowedSize, cancellationToken);
    }


    /// <summary>Reads the whole photo into memory, and keeps it in <see cref="Data"/>.</summary>
    /// <param name="maxAllowedSize">Refuses anything larger. 32MB by default.</param>
    public async Task<byte[]> ReadAllBytesAsync(long maxAllowedSize = 32 * 1024 * 1024, CancellationToken cancellationToken = default)
    {
        if (this.Data.Length > 0)
            return this.Data;

        await using var stream = await this.OpenReadStreamAsync(maxAllowedSize, cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();

        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        this.Data = buffer.ToArray();

        return this.Data;
    }
}


/// <summary>Where and how <see cref="MediaPickerButton"/> should post a photo.</summary>
/// <param name="Url">The address to post to. Relative URLs resolve against the page.</param>
public record MediaPickerUpload(string Url)
{
    /// <summary>The form field the file is sent as. Defaults to <c>file</c>.</summary>
    public string FieldName { get; init; } = "file";

    /// <summary>Overrides the file name sent with the photo.</summary>
    public string? FileName { get; init; }

    public string Method { get; init; } = "POST";

    /// <summary>Headers to send — an <c>Authorization</c> header, a CSRF token, a one-time ticket.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>Sends cookies to another origin. Off by default.</summary>
    public bool WithCredentials { get; init; }
}


/// <summary>What became of one photo's upload.</summary>
/// <param name="Item">The photo that was uploaded.</param>
/// <param name="Success">True for a 2xx answer.</param>
/// <param name="StatusCode">The HTTP status, or 0 when the request never completed.</param>
/// <param name="Body">The server's answer, verbatim — usually JSON the caller wants to read.</param>
public record MediaPickerUploadResult(MediaPickerItem Item, bool Success, int StatusCode, string Body);


/// <summary>How far one photo's upload has got.</summary>
public record MediaPickerUploadProgress(MediaPickerItem Item, long BytesSent, long TotalBytes)
{
    /// <summary>0-100, or null when the browser cannot say how big the body is.</summary>
    public int? Percent => this.TotalBytes <= 0 ? null : (int)(this.BytesSent * 100 / this.TotalBytes);
}

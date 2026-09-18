using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

public partial class MediaPickerButton : IAsyncDisposable
{
    IJSObjectReference? module;
    bool disposed;
    DotNetObjectReference<MediaPickerButton>? selfRef;
    ElementReference rootEl;
    ElementReference galleryInputEl;
    ElementReference cameraInputEl;
    ElementReference chooserEl;
    ElementReference editorEl;
    ImageEditor? editor;
    byte[] editBytes = [];

    readonly List<MediaPickerItem> items = new();

    bool viewerOpen;
    string? viewerSource;
    bool showChooser;
    bool nativeChooser;
    bool permissionDenied;
    bool editing;
    bool chooserRaised;
    bool editorRaised;
    int currentIndex;

    [Parameter] public bool AllowGallery { get; set; } = true;
    [Parameter] public bool AllowCamera { get; set; } = true;
    [Parameter] public bool AllowPhotoEdit { get; set; }
    [Parameter] public string PermissionDeniedText { get; set; } = "Permission denied. Please enable access in Settings.";
    [Parameter] public RenderFragment? NoImagesTemplate { get; set; }
    [Parameter] public bool ShowAsCarouselInView { get; set; } = true;
    [Parameter] public int MaxPhotos { get; set; } = 1;

    /// <summary>Encoder quality as a percentage (1..100). Default 92.</summary>
    [Parameter] public int CompressionQuality { get; set; } = 92;

    /// <summary>If &gt; 0, the longest edge of each saved photo is capped to this many pixels.</summary>
    [Parameter] public int MaxImageDimension { get; set; }

    /// <summary>Output image format: <c>"jpeg"</c> or <c>"png"</c>.</summary>
    [Parameter] public string OutputFormat { get; set; } = "jpeg";

    [Parameter] public string AddButtonText { get; set; } = "➕ Add Photo";
    [Parameter] public string GalleryActionText { get; set; } = "Choose from Gallery";
    [Parameter] public string CameraActionText { get; set; } = "Take Photo";

    /// <summary>
    /// Where each photo is posted as multipart form data, by the browser itself. Set it and the bytes
    /// never enter .NET at all — which is what a Blazor Server app wants, since a photo carried over
    /// the circuit is a large message on a connection that is meant for small ones.
    /// </summary>
    /// <remarks>
    /// With <see cref="AutoUpload"/> off (the default), nothing is sent until
    /// <see cref="UploadAllAsync"/> is called — so a screen can save its record first and upload
    /// against the id that record was given.
    /// </remarks>
    [Parameter] public MediaPickerUpload? UploadUrl { get; set; }

    /// <summary>Uploads each photo the moment it is picked. Requires <see cref="UploadUrl"/>.</summary>
    [Parameter] public bool AutoUpload { get; set; }

    /// <summary>
    /// Reads each photo's bytes into <see cref="MediaPickerItem.Data"/> when it is picked, streamed
    /// rather than base64-encoded. Defaults to true unless the button is uploading the photos itself,
    /// in which case nothing needs them in .NET. Either way <see cref="MediaPickerItem.ReadAllBytesAsync"/>
    /// fetches them on demand.
    /// </summary>
    [Parameter] public bool? LoadBytes { get; set; }

    /// <summary>Refuses a photo larger than this when reading its bytes. 32MB by default.</summary>
    [Parameter] public long MaxReadSize { get; set; } = 32 * 1024 * 1024;

    [Parameter] public IReadOnlyList<MediaPickerItem> Photos { get; set; } = [];
    [Parameter] public EventCallback<IReadOnlyList<MediaPickerItem>> PhotosChanged { get; set; }
    [Parameter] public EventCallback<MediaPickerItem> PhotoAdded { get; set; }
    [Parameter] public EventCallback<MediaPickerItem> PhotoRemoved { get; set; }
    [Parameter] public EventCallback<string> PermissionDenied { get; set; }

    /// <summary>One photo finished uploading, for better or worse.</summary>
    [Parameter] public EventCallback<MediaPickerUploadResult> Uploaded { get; set; }

    /// <summary>How far an upload has got, as the browser sends it.</summary>
    [Parameter] public EventCallback<MediaPickerUploadProgress> UploadProgress { get; set; }

    bool ShouldLoadBytes => this.LoadBytes ?? this.UploadUrl == null;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var loaded = await JS.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/Shiny.Blazor.Controls/media-picker.js");
            if (disposed) { await loaded.ReleaseLateAsync(); return; }
            module = loaded;
            selfRef = DotNetObjectReference.Create(this);

            nativeChooser = await module.InvokeAsync<bool>("init", rootEl, galleryInputEl, cameraInputEl, selfRef, BuildOptions());
        }

        // Inside a SheetView the chooser and the editor have a transformed ancestor, which turns their
        // position:fixed into "fixed to the sheet" - below the fold and clipped, so the button seemed
        // to do nothing. Once on the page they are raised to the top layer. Nothing lowers them: they
        // leave the top layer when they leave the DOM.
        chooserRaised = await RaiseAsync(showChooser, chooserRaised, chooserEl);
        editorRaised = await RaiseAsync(editing, editorRaised, editorEl);
    }

    async Task<bool> RaiseAsync(bool shown, bool raised, ElementReference element)
    {
        if (shown && !raised && module != null)
            await module.InvokeVoidAsync("raise", element);

        return shown;
    }

    MediaPickerJsOptions BuildOptions() => new()
    {
        Format = OutputFormat == "png" ? "png" : "jpeg",
        Quality = Math.Clamp(CompressionQuality, 1, 100) / 100.0,
        MaxDimension = MaxImageDimension
    };

    async Task OnAddClickAsync()
    {
        permissionDenied = false;
        if (items.Count >= MaxPhotos)
            return;

        if (module != null)
            await module.InvokeVoidAsync("updateOptions", rootEl, BuildOptions());

        // On iPhone and iPad the browser shows its own library/camera/files sheet for the gallery
        // input, so ours would only put the same choice in front of it.
        if (AllowGallery && AllowCamera && nativeChooser)
        {
            await PickFromGalleryAsync();
        }
        else if (AllowGallery && AllowCamera)
        {
            showChooser = true;
        }
        else if (AllowCamera)
        {
            await CaptureFromCameraAsync();
        }
        else if (AllowGallery)
        {
            await PickFromGalleryAsync();
        }
    }

    async Task PickFromGalleryAsync()
    {
        showChooser = false;
        if (module != null)
            await module.InvokeVoidAsync("openGallery", rootEl);
    }

    async Task CaptureFromCameraAsync()
    {
        showChooser = false;
        if (module != null)
            await module.InvokeVoidAsync("openCamera", rootEl);
    }

    [JSInvokable]
    public async Task OnFilePicked(MediaPickerJsResult result)
    {
        if (items.Count >= MaxPhotos)
            return;

        var item = ToItem(result);
        items.Add(item);

        // Fetched in chunks, and only if anything here wants them.
        if (this.ShouldLoadBytes)
            await item.ReadAllBytesAsync(this.MaxReadSize);

        await NotifyChangedAsync();
        await PhotoAdded.InvokeAsync(item);
        StateHasChanged();

        if (this.AutoUpload && this.UploadUrl != null)
            await this.UploadAsync(item);
    }

    MediaPickerItem ToItem(MediaPickerJsResult result) => new()
    {
        Id = result.Id,
        DataUri = result.PreviewUrl,
        Width = result.Width,
        Height = result.Height,
        ContentType = result.ContentType,
        Size = result.Size,
        FileName = result.FileName,
        Opener = (max, ct) => this.OpenAsync(result.Id, max, ct)
    };

    async Task<Stream> OpenAsync(string id, long maxAllowedSize, CancellationToken cancellationToken)
    {
        if (this.module == null)
            throw new InvalidOperationException("The media picker is not running.");

        var reference = await this.module
            .InvokeAsync<IJSStreamReference>("read", cancellationToken, this.rootEl, id)
            .ConfigureAwait(false);

        return await reference
            .OpenReadStreamAsync(maxAllowedSize, cancellationToken)
            .ConfigureAwait(false);
    }


    /// <summary>
    /// Posts every photo that is still waiting, one at a time, and reports each as it lands.
    /// </summary>
    /// <param name="upload">Overrides <see cref="UploadUrl"/> — for an address only known once
    /// whatever the photos belong to has been saved.</param>
    /// <param name="keep">Holds on to the photos afterwards, for sending the same ones somewhere else.</param>
    public async Task<IReadOnlyList<MediaPickerUploadResult>> UploadAllAsync(
        MediaPickerUpload? upload = null,
        bool keep = false,
        CancellationToken cancellationToken = default
    )
    {
        var results = new List<MediaPickerUploadResult>();

        foreach (var item in this.items.ToList())
            results.Add(await this.UploadAsync(item, upload, keep, cancellationToken).ConfigureAwait(false));

        return results;
    }


    /// <summary>Posts one photo.</summary>
    public async Task<MediaPickerUploadResult> UploadAsync(
        MediaPickerItem item,
        MediaPickerUpload? upload = null,
        bool keep = false,
        CancellationToken cancellationToken = default
    )
    {
        var target = upload ?? this.UploadUrl
            ?? throw new InvalidOperationException($"Set {nameof(UploadUrl)}, or pass one to {nameof(UploadAsync)}.");

        if (this.module == null)
            throw new InvalidOperationException("The media picker is not running.");

        var answer = await this.module.InvokeAsync<MediaPickerJsUploadResult>(
            "upload",
            cancellationToken,
            this.rootEl,
            item.Id,
            new MediaPickerJsUpload
            {
                Url = target.Url,
                Method = target.Method,
                FieldName = target.FieldName,
                FileName = target.FileName ?? item.FileName,
                Headers = target.Headers,
                WithCredentials = target.WithCredentials
            },
            this.selfRef
        ).ConfigureAwait(false);

        var result = new MediaPickerUploadResult(item, answer.Ok, answer.Status, answer.Body ?? "");

        // Kept on a failure, so the screen can offer to try again rather than asking for the photo twice.
        if (answer.Ok && !keep)
            await this.RemoveAsync(item, notify: false).ConfigureAwait(false);

        await this.Uploaded.InvokeAsync(result).ConfigureAwait(false);

        if (answer.Ok && !keep)
            await this.NotifyChangedAsync().ConfigureAwait(false);

        this.StateHasChanged();

        return result;
    }


    [JSInvokable]
    public Task OnUploadProgress(string id, long sent, long total)
    {
        var item = this.items.FirstOrDefault(x => x.Id == id);

        return item == null
            ? Task.CompletedTask
            : this.UploadProgress.InvokeAsync(new MediaPickerUploadProgress(item, sent, total));
    }


    /// <summary>Drops every photo, and lets the browser forget their bytes.</summary>
    public async Task ClearAsync()
    {
        foreach (var item in this.items.ToList())
            await this.RemoveAsync(item, notify: false).ConfigureAwait(false);

        await this.NotifyChangedAsync().ConfigureAwait(false);
        this.StateHasChanged();
    }


    void OpenViewer(int index)
    {
        if (items.Count == 0)
            return;
        currentIndex = Math.Clamp(index, 0, items.Count - 1);
        viewerSource = items[currentIndex].DataUri;
        viewerOpen = true;
    }

    void Page(int delta)
    {
        if (items.Count == 0)
            return;
        currentIndex = (currentIndex + delta + items.Count) % items.Count;
        viewerSource = items[currentIndex].DataUri;
    }

    async Task RemoveAt(int index)
    {
        if (index < 0 || index >= items.Count)
            return;

        await this.RemoveAsync(items[index]);
        StateHasChanged();
    }


    async Task RemoveAsync(MediaPickerItem item, bool notify = true)
    {
        if (!this.items.Remove(item))
            return;

        if (this.module != null)
        {
            try
            {
                await this.module.InvokeVoidAsync("release", this.rootEl, item.Id).ConfigureAwait(false);
            }
            catch (JSDisconnectedException) { /* the page is gone; so are its object URLs */ }
        }

        if (!notify)
            return;

        await this.NotifyChangedAsync().ConfigureAwait(false);
        await this.PhotoRemoved.InvokeAsync(item).ConfigureAwait(false);
    }


    async Task StartEdit()
    {
        viewerOpen = false;

        // The editor works on bytes, so they come over now even when nothing else needed them.
        editBytes = currentIndex >= 0 && currentIndex < items.Count
            ? await items[currentIndex].ReadAllBytesAsync(this.MaxReadSize)
            : [];

        editing = true;
    }

    async Task SaveEditAsync()
    {
        if (editor == null || currentIndex < 0 || currentIndex >= items.Count)
        {
            editing = false;
            return;
        }

        var quality = Math.Clamp(CompressionQuality, 1, 100) / 100.0;
        var format = OutputFormat == "png" ? "png" : "jpeg";
        var bytes = await editor.ExportAsync(format, quality);
        editing = false;
        editBytes = [];

        if (bytes.Length == 0 || module == null)
            return;

        var contentType = format == "png" ? "image/png" : "image/jpeg";
        var existing = items[currentIndex];

        // Back to the browser as a stream, and it becomes the blob that gets uploaded or read later.
        using var stream = new MemoryStream(bytes);
        var replaced = await module.InvokeAsync<MediaPickerJsResult?>(
            "replace",
            rootEl,
            existing.Id,
            new DotNetStreamReference(stream),
            contentType
        );

        if (replaced == null)
            return;

        var item = ToItem(replaced);
        item.FileName = existing.FileName;

        if (this.ShouldLoadBytes)
            item.Data = bytes;

        items[currentIndex] = item;
        viewerSource = item.DataUri;

        await NotifyChangedAsync();
        StateHasChanged();
    }

    async Task NotifyChangedAsync()
    {
        Photos = items.ToArray();
        await PhotosChanged.InvokeAsync(Photos);
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        if (module != null)
        {
            try
            {
                await module.InvokeVoidAsync("dispose", rootEl);
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
        }
        selfRef?.Dispose();
    }

    // Named DTOs for trim/AOT-safe JS interop (anonymous types lose ctor param names on publish).
    public sealed class MediaPickerJsResult
    {
        public string Id { get; set; } = "";
        public string PreviewUrl { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public string ContentType { get; set; } = "";
        public long Size { get; set; }
        public string FileName { get; set; } = "";
    }

    sealed class MediaPickerJsOptions
    {
        public string Format { get; set; } = "jpeg";
        public double Quality { get; set; } = 0.92;
        public int MaxDimension { get; set; }
    }

    sealed class MediaPickerJsUpload
    {
        public string Url { get; set; } = "";
        public string Method { get; set; } = "POST";
        public string FieldName { get; set; } = "file";
        public string? FileName { get; set; }
        public IReadOnlyDictionary<string, string>? Headers { get; set; }
        public bool WithCredentials { get; set; }
    }

    public sealed class MediaPickerJsUploadResult
    {
        public bool Ok { get; set; }
        public int Status { get; set; }
        public string? Body { get; set; }
    }
}

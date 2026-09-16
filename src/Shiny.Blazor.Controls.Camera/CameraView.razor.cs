using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;

namespace Shiny.Blazor.Controls.Camera;

public partial class CameraView : IAsyncDisposable
{
    [Inject] IJSRuntime JS { get; set; } = default!;

    IJSObjectReference? module;
    DotNetObjectReference<CameraView>? selfRef;
    ElementReference videoEl;
    ElementReference overlayEl;
    bool started;
    bool disposed;
    TaskCompletionSource<CameraBarcode>? pendingScan;
    TaskCompletionSource<CameraDocumentImage>? pendingDocument;

    /// <summary>Which camera to use. <see cref="CameraFacing.Front"/> maps to the browser "user" facing mode.</summary>
    [Parameter] public CameraFacing Facing { get; set; } = CameraFacing.Back;

    /// <summary>Exact device id (from <see cref="GetAvailableCamerasAsync"/>) to use; overrides <see cref="Facing"/> when set.</summary>
    [Parameter] public string? CameraId { get; set; }

    /// <summary>
    /// The single in-browser frame analyzer (null = no analysis). Assign a <see cref="BarcodeAnalyzer"/> to scan
    /// barcodes; swap it freely. Mirrors the MAUI <c>CameraView.Analyzer</c> property.
    /// </summary>
    [Parameter] public CameraAnalyzer? Analyzer { get; set; }

    /// <summary>Draw detection bounding boxes on the overlay canvas.</summary>
    [Parameter] public bool ShowOverlay { get; set; } = true;

    /// <summary>Start the preview automatically on first render.</summary>
    [Parameter] public bool AutoStart { get; set; } = true;

    /// <summary>
    /// Live color filter applied to the preview via CSS. Sugar over <see cref="Effects"/> — the chosen filter
    /// is applied <b>first</b>, so setting both is well-defined.
    /// </summary>
    [Parameter] public CameraFilter Filter { get; set; } = CameraFilter.None;

    /// <summary>
    /// The ordered effects applied to the preview and baked into <see cref="CapturePhotoAsync"/> stills.
    /// Mirrors the MAUI <c>CameraView.Effects</c> collection.
    /// </summary>
    /// <remarks>
    /// The browser backend applies effects through CSS <c>filter</c>, which cannot express an arbitrary colour
    /// matrix — check <see cref="GetEffectSupport"/> before offering an effect in a UI.
    /// </remarks>
    [Parameter] public IReadOnlyList<ICameraEffect>? Effects { get; set; }

    /// <summary>
    /// The current effects resolved into an immutable, ordered snapshot — rebuilt whenever
    /// <see cref="Filter"/> or <see cref="Effects"/> changes.
    /// </summary>
    public CameraEffectChain EffectChain { get; private set; } = CameraEffectChain.Empty;

    /// <summary>How much of <paramref name="effect"/> the browser backend will actually honour.</summary>
    public static EffectSupport GetEffectSupport(ICameraEffect effect) => BlazorCameraFilters.Resolve(effect);

    [Parameter] public string? CssClass { get; set; }
    [Parameter] public string? Style { get; set; }

    /// <summary>Raised whenever the in-browser analyzers produce a new styled overlay-box set.</summary>
    [Parameter] public EventCallback<IReadOnlyList<OverlayBox>> OverlaysChanged { get; set; }

    /// <summary>
    /// Raised with <b>every</b> barcode decoded in a frame (a frame can hold several) when detected in-browser —
    /// but only while a <see cref="RequestBarcodeAsync"/> is outstanding (gated, so it's quiet by default rather
    /// than a per-frame firehose). For most flows prefer awaiting <see cref="RequestBarcodeAsync"/> directly.
    /// </summary>
    [Parameter] public EventCallback<IReadOnlyList<CameraBarcode>> BarcodesDetected { get; set; }

    /// <summary>Raised when the camera cannot start (permission denied, no device, insecure context).</summary>
    [Parameter] public EventCallback<string> OnError { get; set; }

    /// <summary>Raised after the preview has started (permission granted) — a good time to call <see cref="GetAvailableCamerasAsync"/>, whose device labels only populate once permission is granted.</summary>
    [Parameter] public EventCallback OnStarted { get; set; }


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        var imported = await this.JS.InvokeAsync<IJSObjectReference>(
            "import", "./_content/Shiny.Blazor.Controls.Camera/camera.js");

        // Disposed while the module was loading: starting now would open a camera nothing will ever close.
        if (this.disposed)
        {
            await imported.DisposeAsync();
            return;
        }

        this.module = imported;
        this.selfRef = DotNetObjectReference.Create(this);

        // Filter first: it only needs the element, whereas StartAsync blocks on getUserMedia — which sits on the
        // permission prompt for as long as the user takes to answer it. Applying after would leave the preview
        // unfiltered for that whole window, then pop the effect in once permission lands.
        await this.ApplyFilterAsync();

        if (this.AutoStart)
            await this.StartAsync();
    }


    string appliedFilterKey = "none";
    CameraAnalyzer? appliedAnalyzer;
    bool appliedOverlay;
    CameraFacing appliedFacing;
    string? appliedCameraId;

    protected override async Task OnParametersSetAsync()
    {
        if (this.module == null)
            return;

        await this.ApplyFilterAsync();   // CSS filter — applied live without a restart (no-op when unchanged)

        // Analyzer / ShowOverlay / Facing / CameraId are read by the JS `start` call, so changing them
        // re-acquires the stream. Re-apply by restarting while running (matches MAUI's live toggling).
        if (this.started &&
            (!ReferenceEquals(this.Analyzer, this.appliedAnalyzer) ||
             this.ShowOverlay != this.appliedOverlay ||
             this.Facing != this.appliedFacing ||
             this.CameraId != this.appliedCameraId))
        {
            await this.StopAsync();
            await this.StartAsync();
        }
    }

    /// <summary>
    /// What System.Text.Json needs on a JS-interop DTO: a parameterless constructor plus the property accessors.
    /// </summary>
    /// <remarks>
    /// JS interop annotates its generic/parameter types with <see cref="DynamicallyAccessedMembersAttribute"/>, but
    /// that annotation stops at the <b>array</b> type — it never reaches the element type. So every DTO we marshal as
    /// <c>T[]</c> loses its constructor and accessors in a trimmed (published) WASM build, and deserialization then
    /// fails with "DeserializeNoConstructor". Root the element types explicitly with <see cref="DynamicDependencyAttribute"/>.
    /// Non-array DTOs (see <c>MediaPickerJsResult</c>) are handled by the built-in annotation and need nothing.
    /// </remarks>
    const DynamicallyAccessedMemberTypes JsonSerialized =
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties;

    /// <summary>
    /// List the video input devices the browser exposes. Device <c>Name</c>s are only populated once camera
    /// permission has been granted, so call this after the preview has started.
    /// </summary>
    [DynamicDependency(JsonSerialized, typeof(CameraDevice))]
    public async Task<IReadOnlyList<CameraDevice>> GetAvailableCamerasAsync()
    {
        if (this.module == null)
            return [];
        return await this.module.InvokeAsync<CameraDevice[]>("listCameras");
    }


    // Scopes generated SVG filter ids to this component, so two cameras on one page never collide.
    readonly string filterIdPrefix = $"shiny-fx-{Guid.NewGuid():N}";

    async Task ApplyFilterAsync()
    {
        if (this.module == null)
            return;

        this.EffectChain = CameraEffectChain.Create(this.Filter, this.Effects);

        // Effects is a plain list, so we can't observe mutation — compare the resolved rendering instead, which
        // also collapses "different effects, same rendering" into a single no-op. The key covers the injected
        // SVG markup as well as the CSS: url(#id) references are positional, so Comic and Sketch both come back
        // as url(#prefix-0) and comparing CSS alone would skip the swap entirely.
        var resolved = BlazorCameraFilters.Resolve(this.EffectChain, this.filterIdPrefix);
        if (resolved.Key == this.appliedFilterKey)
            return;

        this.appliedFilterKey = resolved.Key;

        // Marshalled as two parallel string arrays instead of the SvgFilterDef list itself: the JS interop args
        // are serialized as object[], so each def goes through a polymorphic write that reflects over the type —
        // and in a trimmed (published) WASM build the record's constructor parameter names are gone, which
        // System.Text.Json rejects with "ConstructorContainsNullParameterNames". string[] carries no metadata to
        // lose, so nothing here depends on the trimmer keeping a DTO alive.
        var count = resolved.Filters.Count;
        var filterIds = new string[count];
        var filterMarkup = new string[count];
        for (var i = 0; i < count; i++)
        {
            filterIds[i] = resolved.Filters[i].Id;
            filterMarkup[i] = resolved.Filters[i].Markup;
        }

        await this.module.InvokeVoidAsync(
            "setFilter", this.videoEl, resolved.Css, this.filterIdPrefix, filterIds, filterMarkup);
    }


    /// <summary>Request the camera and begin previewing + analyzing.</summary>
    public async Task StartAsync()
    {
        if (this.module == null || this.started)
            return;
        try
        {
            await this.module.InvokeVoidAsync(
                "start", this.videoEl, this.overlayEl, this.selfRef,
                this.Facing == CameraFacing.Front ? "user" : "environment",
                this.Analyzer?.Kind, this.ShowOverlay, this.CameraId,
                this.Analyzer?.ShowBoundingBox ?? true, this.Analyzer?.ScanWindowArray());
            this.started = true;
            this.appliedAnalyzer = this.Analyzer;
            this.appliedOverlay = this.ShowOverlay;
            this.appliedFacing = this.Facing;
            this.appliedCameraId = this.CameraId;
            await this.OnStarted.InvokeAsync();
        }
        catch (Exception ex)
        {
            await this.OnError.InvokeAsync(ex.Message);
        }
    }


    /// <summary>Stop the preview and release the camera.</summary>
    public async Task StopAsync()
    {
        if (this.module == null || !this.started)
            return;
        await this.module.InvokeVoidAsync("stop", this.videoEl);
        this.started = false;
    }


    /// <summary>
    /// Arm the detector and complete on the <i>next</i> decoded barcode, then go quiet — the gated equivalent of
    /// the MAUI <c>OnDetected</c> flow. Bounding boxes keep drawing throughout. Call it again to "keep scanning"
    /// (e.g. in a loop to collect several codes). Pass a <paramref name="ct"/> to cancel an outstanding request.
    /// </summary>
    public async Task<CameraBarcode> RequestBarcodeAsync(CancellationToken ct = default)
    {
        if (this.module == null)
            throw new InvalidOperationException("CameraView is not started");

        // only one outstanding request at a time — supersede any prior wait
        this.pendingScan?.TrySetCanceled();
        var tcs = new TaskCompletionSource<CameraBarcode>(TaskCreationOptions.RunContinuationsAsynchronously);
        this.pendingScan = tcs;

        await using var reg = ct.Register(() =>
        {
            if (tcs.TrySetCanceled(ct))
                _ = this.module.InvokeVoidAsync("disarm", this.videoEl).AsTask();
        });

        await this.module.InvokeVoidAsync("arm", this.videoEl);
        return await tcs.Task;
    }


    /// <summary>
    /// Arm the <see cref="DocumentAnalyzer"/> and complete on the <i>next</i> steadily-present document with its
    /// cropped JPEG, then go quiet — the document equivalent of <see cref="RequestBarcodeAsync"/>. The cheap
    /// presence detection runs every frame (and draws the outline); this only fires once a document has been held
    /// in view, so the (paid) AI call you make with the result isn't run on every frame. Call it again to keep
    /// scanning. Requires the <see cref="Analyzer"/> to be a <see cref="DocumentAnalyzer"/>.
    /// </summary>
    public async Task<CameraDocumentImage> RequestDocumentImageAsync(CancellationToken ct = default)
    {
        if (this.module == null)
            throw new InvalidOperationException("CameraView is not started");

        this.pendingDocument?.TrySetCanceled();
        var tcs = new TaskCompletionSource<CameraDocumentImage>(TaskCreationOptions.RunContinuationsAsynchronously);
        this.pendingDocument = tcs;

        await using var reg = ct.Register(() =>
        {
            if (tcs.TrySetCanceled(ct))
                _ = this.module.InvokeVoidAsync("disarm", this.videoEl).AsTask();
        });

        await this.module.InvokeVoidAsync("arm", this.videoEl);
        return await tcs.Task;
    }


    /// <summary>Capture a still frame as JPEG bytes.</summary>
    public async Task<byte[]> CapturePhotoAsync()
    {
        if (this.module == null)
            return [];
        // bake the current filter into the still so it matches the preview (parity with MAUI)
        // the SVG defs are already in the document from ApplyFilterAsync, so the CSS alone is enough here
        var css = BlazorCameraFilters.Resolve(this.EffectChain, this.filterIdPrefix).Css;
        return await this.module.InvokeAsync<byte[]>("capture", this.videoEl, css);
    }


    /// <summary>Start recording (via MediaRecorder). Pass <c>false</c> to record without audio.</summary>
    public async Task StartRecordingAsync(bool includeAudio = true)
    {
        if (this.module != null)
            await this.module.InvokeVoidAsync("startRecording", this.videoEl, includeAudio);
    }


    /// <summary>Stop recording and return the encoded video bytes (typically WebM).</summary>
    public async Task<byte[]> StopRecordingAsync()
    {
        if (this.module == null)
            return [];
        return await this.module.InvokeAsync<byte[]>("stopRecording", this.videoEl);
    }


    /// <summary>Invoked from JS with the latest styled overlay boxes. Public + named DTO for trim-safe interop.</summary>
    [JSInvokable]
    [DynamicDependency(JsonSerialized, typeof(CameraOverlayBox))]
    public async Task OnOverlays(CameraOverlayBox[] boxes)
    {
        var mapped = boxes.Select(b => b.ToOverlayBox()).ToArray();
        await this.OverlaysChanged.InvokeAsync(mapped);
    }


    /// <summary>
    /// Invoked from JS when barcodes are decoded (only while armed). Completes any outstanding
    /// <see cref="RequestBarcodeAsync"/> with the first code and raises <see cref="BarcodesDetected"/> with all
    /// of them. Public + named DTO for trim-safe interop.
    /// </summary>
    [JSInvokable]
    [DynamicDependency(JsonSerialized, typeof(CameraBarcode))]
    public async Task OnBarcodes(CameraBarcode[] barcodes)
    {
        if (barcodes.Length == 0)
            return;

        var pending = this.pendingScan;
        this.pendingScan = null;
        pending?.TrySetResult(barcodes[0]);
        await this.BarcodesDetected.InvokeAsync(barcodes);
    }


    /// <summary>
    /// Invoked from JS (only while armed) with the cropped JPEG of a steadily-present document and its bounds.
    /// Completes any outstanding <see cref="RequestDocumentImageAsync"/>. <paramref name="box"/> is a flat
    /// [x, y, w, h] in normalized upright video space; <paramref name="jpeg"/> is the cropped image bytes.
    /// </summary>
    [JSInvokable]
    public Task OnDocumentImage(float[] box, byte[] jpeg)
    {
        var bounds = box is { Length: 4 }
            ? new RectF(box[0], box[1], box[2], box[3])
            : new RectF(0, 0, 1, 1);

        var pending = this.pendingDocument;
        this.pendingDocument = null;
        pending?.TrySetResult(new CameraDocumentImage(jpeg, bounds));
        return Task.CompletedTask;
    }


    /// <summary>Invoked from JS when an error occurs after startup.</summary>
    [JSInvokable]
    public Task OnJsError(string message) => this.OnError.InvokeAsync(message);


    public async ValueTask DisposeAsync()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        this.pendingScan?.TrySetCanceled();
        this.pendingDocument?.TrySetCanceled();
        try
        {
            if (this.module != null)
            {
                // Not StopAsync: that is a no-op until start() has returned, and a start still parked on the
                // permission prompt would otherwise open the camera after this component is gone. dispose also
                // stops a running recording's microphone and removes this instance's SVG filter definitions.
                this.started = false;
                await this.module.InvokeVoidAsync("dispose", this.videoEl, this.filterIdPrefix);
                await this.module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { /* circuit gone */ }
        catch (ObjectDisposedException) { /* runtime gone */ }
        this.selfRef?.Dispose();
    }
}

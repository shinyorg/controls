using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls.Camera.Media;

/// <summary>
/// The Blazor <see cref="IMediaService"/>. Owns the one <see cref="ActiveSession"/> a <c>MediaHost</c> renders,
/// and every rule about it — presentation, permissions, encoding defaults, scan limits, teardown.
/// </summary>
/// <remarks>
/// <b>Scoped, not singleton</b>: the active session is per-user state, and a singleton would open one user's
/// camera modal on every connected user's screen under Blazor Server.
/// </remarks>
public sealed class MediaService : IMediaService, IAsyncDisposable
{
    const string ModulePath = "./_content/Shiny.Blazor.Controls.Camera/media.js";

    const DynamicallyAccessedMemberTypes JsonSerialized =
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties;

    readonly IJSRuntime js;
    Task<IJSObjectReference>? moduleTask;
    int hostCount;

    public MediaService(IJSRuntime js, MediaServiceOptions? options = null)
    {
        this.js = js;
        this.Options = options ?? new MediaServiceOptions();
    }

    public MediaServiceOptions Options { get; }

    /// <summary>The modal on screen, or null. Read by <c>MediaHost</c>.</summary>
    public MediaSession? ActiveSession { get; private set; }

    /// <summary>Raised when <see cref="ActiveSession"/> or anything drawn from it changes. May fire off the renderer's thread.</summary>
    public event Action? Changed;


    // -------------------------------------------------------------------------------------------------
    // permissions + devices
    // -------------------------------------------------------------------------------------------------

    public async Task<bool> IsCameraSupportedAsync(CancellationToken ct = default)
    {
        var module = await this.GetModuleAsync();
        return await module.InvokeAsync<bool>("isCameraSupported", ct);
    }


    public async Task<MediaPermissionStatus> RequestCameraPermissionAsync(bool includeMicrophone = false, CancellationToken ct = default)
    {
        var module = await this.GetModuleAsync();
        return ToStatus(await module.InvokeAsync<string>("requestCameraPermission", ct, includeMicrophone));
    }


    [DynamicDependency(JsonSerialized, typeof(CameraDevice))]
    public async Task<IReadOnlyList<CameraDevice>> GetAvailableCamerasAsync(CancellationToken ct = default)
    {
        var module = await this.GetModuleAsync();
        return await module.InvokeAsync<CameraDevice[]>("listCameras", ct);
    }


    // -------------------------------------------------------------------------------------------------
    // capture
    // -------------------------------------------------------------------------------------------------

    public async Task<MediaPhoto?> TakePhotoAsync(PhotoCaptureOptions? options = null, CancellationToken ct = default)
    {
        var opts = this.Prepare(options ?? new PhotoCaptureOptions());
        if (!await this.CanOpenCameraAsync(ct))
            return null;

        var session = new MediaSession(MediaSessionKind.Photo, opts);
        return await this.PresentAsync(session, ct) as MediaPhoto;
    }


    public async Task<MediaVideo?> RecordVideoAsync(VideoCaptureOptions? options = null, CancellationToken ct = default)
    {
        var opts = this.Prepare(options ?? new VideoCaptureOptions());
        if (!await this.CanOpenCameraAsync(ct))
            return null;

        var session = new MediaSession(MediaSessionKind.Video, opts);
        return await this.PresentAsync(session, ct) as MediaVideo;
    }


    // -------------------------------------------------------------------------------------------------
    // gallery
    // -------------------------------------------------------------------------------------------------

    public async Task<MediaPhoto?> PickPhotoAsync(MediaPickOptions? options = null, CancellationToken ct = default)
    {
        var photos = await this.PickPhotosCoreAsync(false, 1, options, ct);
        return photos.Count > 0 ? photos[0] : null;
    }


    public Task<IReadOnlyList<MediaPhoto>> PickPhotosAsync(int maxCount = 10, MediaPickOptions? options = null, CancellationToken ct = default)
        => this.PickPhotosCoreAsync(true, Math.Max(1, maxCount), options, ct);


    [DynamicDependency(JsonSerialized, typeof(MediaBlobInfo))]
    public async Task<MediaVideo?> PickVideoAsync(CancellationToken ct = default)
    {
        var module = await this.GetModuleAsync();

        // no timeout: the chooser stays open for as long as the user browses
        var picked = await module.InvokeAsync<MediaBlobInfo[]>("pick", ct, "video/*", false, 1, false, 0, "", 0);
        return picked.Length > 0 ? new MediaVideo(module, picked[0]) : null;
    }


    [DynamicDependency(JsonSerialized, typeof(MediaBlobInfo))]
    async Task<IReadOnlyList<MediaPhoto>> PickPhotosCoreAsync(bool multiple, int maxCount, MediaPickOptions? options, CancellationToken ct)
    {
        options ??= new MediaPickOptions();
        var module = await this.GetModuleAsync();
        var (mime, quality) = this.Encoding(options.OutputFormat, options.CompressionQuality);

        var picked = await module.InvokeAsync<MediaBlobInfo[]>(
            "pick", ct, "image/*", multiple, maxCount, true, this.Dimension(options.MaxDimension), mime, quality);

        var photos = new List<MediaPhoto>(picked.Length);
        foreach (var info in picked)
            photos.Add(await this.ReadPhotoAsync(module, info, ct));

        return photos;
    }


    // -------------------------------------------------------------------------------------------------
    // scanning
    // -------------------------------------------------------------------------------------------------

    public async IAsyncEnumerable<T> ScanAsync<T>(
        MediaScanRequest<T> request,
        MediaScanOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var opts = this.Prepare(options ?? new MediaScanOptions());
        if (!await this.CanOpenCameraAsync(ct))
            yield break;

        request.Analyzer.ScanWindow = opts.ScanWindow;
        request.Analyzer.ShowBoundingBox = opts.ShowBoundingBox;

        var session = new MediaSession(MediaSessionKind.Scan, opts, request.Analyzer);
        this.Open(session);

        // one token for "this session is over": the caller cancelling, the user closing or tapping ✓
        using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Ended.Token);
        var seen = opts.FilterDuplicates && request.DuplicateKey is not null ? new HashSet<string>() : null;
        var yielded = 0;

        try
        {
            var camera = await WaitAsync(session.CameraReady.Task, sessionCts.Token);
            if (camera is null)
                yield break;

            while (!sessionCts.IsCancellationRequested)
            {
                var batch = await this.NextBatchAsync(request, session, camera, opts, sessionCts.Token);
                if (batch is null)
                    yield break;   // timed out, closed or cancelled

                foreach (var value in batch)
                {
                    if (seen is not null && !seen.Add(request.DuplicateKey!(value)))
                        continue;

                    yielded++;
                    session.ResultCount = yielded;
                    session.LastResult = request.Describe?.Invoke(value);
                    this.RaiseChanged();

                    if (opts.VibrateOnResult)
                        _ = this.VibrateAsync();

                    yield return value;

                    if (opts.MaxResults is { } max && yielded >= max)
                        yield break;
                }
            }
        }
        finally
        {
            // every exit — including the caller breaking out of their await foreach, which is how the
            // single-result overloads close the modal after one hit
            this.Close(session);
        }
    }


    /// <summary>
    /// One pull from the analyzer, bounded by the idle timeout. Null means the session is over (closed, done,
    /// idle, cancelled). Separate from
    /// the iterator because C# cannot <c>yield</c> inside a <c>try</c> that has a <c>catch</c>.
    /// </summary>
    async Task<IReadOnlyList<T>?> NextBatchAsync<T>(MediaScanRequest<T> request, MediaSession session, CameraView camera, MediaScanOptions opts, CancellationToken sessionToken)
    {
        // Timeout is an *idle* timeout: a fresh clock per pull, so finding something restarts it and a
        // productive session is never cut off mid-scan
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
        if (opts.Timeout is { } timeout)
            idle.CancelAfter(timeout);

        try
        {
            var context = new MediaScanContext(session, session.Camera ?? camera, this.RaiseChanged);
            context.SetStatus(null);
            return await request.Next(context, idle.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        // anything else — a model call failing, say — propagates out of the caller's await foreach (the iterator's
        // finally still closes the modal); swallowing it would end the scan looking exactly like a user cancel
    }


    // -------------------------------------------------------------------------------------------------
    // MediaHost → service
    // -------------------------------------------------------------------------------------------------

    internal async Task<bool> IsBarcodeDetectorAvailableAsync()
    {
        var module = await this.GetModuleAsync();
        return await module.InvokeAsync<bool>("isBarcodeDetectorAvailable");
    }


    internal void AttachHost() => Interlocked.Increment(ref this.hostCount);

    internal void DetachHost()
    {
        if (Interlocked.Decrement(ref this.hostCount) == 0 && this.ActiveSession is { } session)
            session.Finish(null);   // the only surface drawing the modal is gone — nothing can finish it now
    }


    internal void OnCameraStarted(MediaSession session, CameraView camera)
    {
        session.Camera = camera;
        session.Error = null;
        session.CameraReady.TrySetResult(camera);
        this.RaiseChanged();
    }


    internal void OnCameraError(MediaSession session, string message)
    {
        // getUserMedia reports a refusal as NotAllowedError / "Permission denied" depending on the browser
        var denied = message.Contains("NotAllowed", StringComparison.OrdinalIgnoreCase)
                  || message.Contains("denied", StringComparison.OrdinalIgnoreCase)
                  || message.Contains("permission", StringComparison.OrdinalIgnoreCase);

        session.Error = denied ? session.Options.PermissionDeniedText : message;
        this.RaiseChanged();
    }


    /// <summary>The user closed the modal (✕, Escape) — the call returns null / the scan ends.</summary>
    internal void Cancel(MediaSession session) => session.Finish(null);


    /// <summary>✓ on a scan: end the session with what has been collected.</summary>
    internal void Done(MediaSession session) => session.Finish(null);


    internal void Flip(MediaSession session)
    {
        if (session.IsRecording)
            return;

        session.CameraId = null;   // an exact device would pin the lens and make the flip a no-op
        session.Facing = session.Facing == Shiny.Controls.Camera.CameraFacing.Back
            ? Shiny.Controls.Camera.CameraFacing.Front
            : Shiny.Controls.Camera.CameraFacing.Back;
        this.RaiseChanged();
    }


    internal void SelectEffect(MediaSession session, MediaEffectChoice choice)
    {
        session.SelectedEffect = choice;
        session.Filter = choice.Filter;
        session.Effects = choice.Effect is null
            ? session.Options.Effects.ToArray()
            : [.. session.Options.Effects, choice.Effect];
        this.RaiseChanged();
    }


    internal async Task ShutterAsync(MediaSession session)
    {
        if (session.Camera is not { } camera || session.IsBusy || session.PendingPhoto is not null)
            return;

        var opts = (PhotoCaptureOptions)session.Options;
        var (mime, quality) = this.Encoding(opts.OutputFormat, opts.CompressionQuality);

        session.IsBusy = true;
        this.RaiseChanged();
        try
        {
            var info = await camera.CaptureStoredAsync(this.Dimension(opts.MaxDimension), mime, quality);
            if (opts.ShowConfirmation)
            {
                session.PendingPhoto = info;
                return;
            }

            await this.AcceptPhotoAsync(session, info);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            session.Error = ex.Message;
        }
        finally
        {
            session.IsBusy = false;
            this.RaiseChanged();
        }
    }


    internal async Task AcceptAsync(MediaSession session)
    {
        if (session.PendingPhoto is not { } info || session.IsBusy)
            return;

        session.IsBusy = true;
        this.RaiseChanged();
        try
        {
            await this.AcceptPhotoAsync(session, info);
        }
        finally
        {
            session.IsBusy = false;
            this.RaiseChanged();
        }
    }


    internal async Task RetakeAsync(MediaSession session)
    {
        if (session.PendingPhoto is not { } info)
            return;

        session.PendingPhoto = null;
        this.RaiseChanged();
        await this.ReleaseAsync(info.Id);
    }


    internal async Task ToggleRecordingAsync(MediaSession session)
    {
        if (session.Camera is not { } camera || session.IsBusy)
            return;

        var opts = (VideoCaptureOptions)session.Options;
        if (!session.IsRecording)
        {
            // busy while starting: with audio on, the browser's microphone prompt holds this open for as long as
            // the user takes, and a second tap meanwhile would start a second recorder
            session.IsBusy = true;
            this.RaiseChanged();
            try
            {
                await camera.StartRecordingAsync(opts.IncludeAudio);
                session.IsRecording = true;
                session.RecordingStartedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!session.Ended.IsCancellationRequested)
                    session.Error = ex.Message;
                return;
            }
            finally
            {
                session.IsBusy = false;
                this.RaiseChanged();
            }

            if (opts.MaxDuration is { } max)
                _ = this.StopAfterAsync(session, max);

            return;
        }

        session.IsBusy = true;
        this.RaiseChanged();
        try
        {
            var info = await camera.StopRecordingStoredAsync();
            session.IsRecording = false;
            session.Finish(new MediaVideo(await this.GetModuleAsync(), info));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            session.IsRecording = false;
            session.Error = ex.Message;
        }
        finally
        {
            session.IsBusy = false;
            this.RaiseChanged();
        }
    }


    async Task StopAfterAsync(MediaSession session, TimeSpan max)
    {
        try
        {
            await Task.Delay(max, session.Ended.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (session.IsRecording && !session.IsBusy)
            await this.ToggleRecordingAsync(session);
    }


    // -------------------------------------------------------------------------------------------------
    // plumbing
    // -------------------------------------------------------------------------------------------------

    async Task<object?> PresentAsync(MediaSession session, CancellationToken ct)
    {
        this.Open(session);
        using var registration = ct.Register(() => session.Finish(null));
        try
        {
            return await session.Result.Task;
        }
        finally
        {
            this.Close(session);
        }
    }


    void Open(MediaSession session)
    {
        if (Volatile.Read(ref this.hostCount) == 0)
            throw new InvalidOperationException(
                "IMediaService draws its camera in a <MediaHost />. Render one in your layout (next to <DialogHost />, for example).");

        if (this.ActiveSession is not null)
            throw new InvalidOperationException("A camera session is already open. Await it (or cancel it) before starting another.");

        this.ActiveSession = session;
        this.RaiseChanged();
    }


    void Close(MediaSession session)
    {
        session.Finish(null);
        if (session.PendingPhoto is { } pending)
        {
            session.PendingPhoto = null;
            _ = this.ReleaseAsync(pending.Id);
        }

        if (ReferenceEquals(this.ActiveSession, session))
        {
            this.ActiveSession = null;
            this.RaiseChanged();
        }
    }


    async Task AcceptPhotoAsync(MediaSession session, MediaBlobInfo info)
    {
        var module = await this.GetModuleAsync();
        var photo = await this.ReadPhotoAsync(module, info, CancellationToken.None);
        session.PendingPhoto = null;
        session.Finish(photo);
    }


    async Task<MediaPhoto> ReadPhotoAsync(IJSObjectReference module, MediaBlobInfo info, CancellationToken ct)
    {
        try
        {
            // streamed, never returned as byte[]: on Blazor Server a byte[] result is one SignalR message, and a
            // photo is far past the default 32KB cap
            var reference = await module.InvokeAsync<IJSStreamReference>("read", ct, info.Id);
            await using var stream = await reference.OpenReadStreamAsync(info.Size + 1, ct);
            using var buffer = new MemoryStream((int)Math.Min(info.Size, int.MaxValue));
            await stream.CopyToAsync(buffer, ct);

            var contentType = String.IsNullOrEmpty(info.ContentType) ? "image/jpeg" : info.ContentType;
            return new MediaPhoto(buffer.ToArray(), info.Width, info.Height, contentType, info.Name);
        }
        finally
        {
            await this.ReleaseAsync(info.Id);
        }
    }


    async Task ReleaseAsync(string id)
    {
        try
        {
            var module = await this.GetModuleAsync();
            await module.InvokeVoidAsync("release", id);
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
    }


    async Task VibrateAsync()
    {
        try
        {
            var module = await this.GetModuleAsync();
            await module.InvokeVoidAsync("vibrate", 40);
        }
        catch (Exception) { /* haptics are decoration; never fail a scan over them */ }
    }


    /// <summary>
    /// Refuse only when there is no camera or the site is already blocked. "prompt" opens the modal and lets the
    /// camera's own getUserMedia ask — pre-asking would open the camera twice, and on browsers that do not
    /// remember a grant, prompt twice.
    /// </summary>
    async Task<bool> CanOpenCameraAsync(CancellationToken ct)
    {
        var module = await this.GetModuleAsync();
        var state = await module.InvokeAsync<string>("cameraPermissionState", ct);
        return state is not ("denied" or "unsupported");
    }


    static async Task<CameraView?> WaitAsync(Task<CameraView> ready, CancellationToken ct)
    {
        try
        {
            return await ready.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }


    TOptions Prepare<TOptions>(TOptions options) where TOptions : MediaCameraOptions
    {
        this.Options.ConfigureDefaults?.Invoke(options);
        return options;
    }


    (string Mime, int Quality) Encoding(MediaImageFormat? format, int? quality)
        => ((format ?? this.Options.OutputFormat) == MediaImageFormat.Png ? "image/png" : "image/jpeg",
            Math.Clamp(quality ?? this.Options.CompressionQuality, 1, 100));


    int Dimension(int? value) => Math.Max(0, value ?? this.Options.MaxDimension);


    Task<IJSObjectReference> GetModuleAsync()
    {
        // a failed import (network blip) must not poison the service for the rest of the session
        if (this.moduleTask is { IsFaulted: true } or { IsCanceled: true })
            this.moduleTask = null;

        return this.moduleTask ??= this.js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();
    }


    void RaiseChanged() => this.Changed?.Invoke();


    static MediaPermissionStatus ToStatus(string state) => state switch
    {
        "granted" => MediaPermissionStatus.Granted,
        "restricted" => MediaPermissionStatus.Restricted,
        "unsupported" => MediaPermissionStatus.Unsupported,
        _ => MediaPermissionStatus.Denied
    };


    public async ValueTask DisposeAsync()
    {
        this.ActiveSession?.Finish(null);
        if (this.moduleTask is { IsCompletedSuccessfully: true } task)
        {
            try
            {
                await task.Result.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
        }
    }
}

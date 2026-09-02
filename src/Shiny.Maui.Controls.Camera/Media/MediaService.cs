using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;

namespace Shiny.Maui.Controls.Camera.Media;

/// <summary>
/// The default <see cref="IMediaService"/>. Registered by <c>UseShinyCamera()</c>; injected as
/// <see cref="IMediaService"/>.
/// </summary>
public class MediaService(MediaServiceOptions options) : IMediaService
{
    /// <inheritdoc/>
    public MediaServiceOptions Options { get; } = options;

    /// <inheritdoc/>
    public bool IsCameraSupported
    {
        get
        {
#if ANDROID || IOS || MACCATALYST || WINDOWS || MACOS
            return GetNavigation() is not null;
#else
            return false;
#endif
        }
    }

    // -------------------------------------------------------------------------------------------------
    // permissions
    // -------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<MediaPermissionStatus> RequestCameraPermissionAsync(bool includeMicrophone = false, CancellationToken ct = default)
    {
        var camera = await RequestAsync<Permissions.Camera>().ConfigureAwait(false);
        if (!includeMicrophone || camera != MediaPermissionStatus.Granted)
            return camera;

        // the weakest of the two: "granted" has to mean every permission the call needs, or a caller that
        // checks it still gets a silent movie
        return await RequestAsync<Permissions.Microphone>().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<MediaPermissionStatus> RequestGalleryPermissionAsync(bool forWrite = false, CancellationToken ct = default)
        => forWrite
            ? RequestAsync<Permissions.PhotosAddOnly>()
            : RequestAsync<Permissions.Photos>();

    /// <inheritdoc/>
    public Task OpenSettingsAsync() => MainThread.InvokeOnMainThreadAsync(() =>
    {
        try
        {
            AppInfo.Current.ShowSettingsUI();
        }
        catch
        {
            // no settings UI on this head; nothing better to do than leave the caller's message on screen
        }
    });

    static Task<MediaPermissionStatus> RequestAsync<TPermission>() where TPermission : Permissions.BasePermission, new()
        => MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                var status = await Permissions.CheckStatusAsync<TPermission>();
                if (status is not (PermissionStatus.Granted or PermissionStatus.Limited))
                    status = await Permissions.RequestAsync<TPermission>();

                return Map(status);
            }
            catch (PermissionException)
            {
                // the permission isn't declared in the manifest/Info.plist — a build problem, not a user
                // refusal, but from the caller's side the outcome is the same: no access
                return MediaPermissionStatus.Denied;
            }
            catch (NotImplementedException)
            {
                return MediaPermissionStatus.Unsupported; // bare net10.0 reference assembly
            }
        });

    static MediaPermissionStatus Map(PermissionStatus status) => status switch
    {
        PermissionStatus.Granted or PermissionStatus.Limited => MediaPermissionStatus.Granted,
        PermissionStatus.Restricted or PermissionStatus.Disabled => MediaPermissionStatus.Restricted,
        PermissionStatus.Unknown => MediaPermissionStatus.Unsupported,
        _ => MediaPermissionStatus.Denied
    };

    /// <inheritdoc/>
    /// <remarks>
    /// Enumeration needs a connected handler but not a running session or a visible page, so a throwaway
    /// <see cref="CameraView"/> is given a handler directly rather than being shown. Presenting a page to
    /// answer "what lenses does this device have" would flash a black modal at the user for no reason.
    /// </remarks>
    public Task<IReadOnlyList<CameraInfo>> GetAvailableCamerasAsync(CancellationToken ct = default)
    {
#if ANDROID || IOS || MACCATALYST || WINDOWS || MACOS
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var context = Application.Current?.Windows.LastOrDefault()?.Handler?.MauiContext;
            if (context is null)
                return (IReadOnlyList<CameraInfo>)[];

            var camera = new CameraView();
            var handler = Microsoft.Maui.Platform.ElementExtensions.ToHandler(camera, context);
            try
            {
                return await camera.GetAvailableCamerasAsync(ct).ConfigureAwait(true);
            }
            finally
            {
                handler.DisconnectHandler();
            }
        });
#else
        return Task.FromResult<IReadOnlyList<CameraInfo>>([]);
#endif
    }

    // -------------------------------------------------------------------------------------------------
    // capture
    // -------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<MediaPhoto?> TakePhotoAsync(PhotoCaptureOptions? options = null, CancellationToken ct = default)
    {
        var opts = this.Prepare(options ?? new PhotoCaptureOptions());
        var page = new MediaCapturePage(MediaCaptureMode.Photo, opts);

        var result = await this
            .PresentAsync(page, () => this.RequestCameraPermissionAsync(false, ct), ct)
            .ConfigureAwait(true);

        if (result is not CameraPhoto photo)
            return null;

        return await MediaImaging
            .ProcessAsync(photo, this.Format(opts.OutputFormat), this.Quality(opts.CompressionQuality), this.Dimension(opts.MaxDimension))
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<MediaVideo?> RecordVideoAsync(VideoCaptureOptions? options = null, CancellationToken ct = default)
    {
        var opts = this.Prepare(options ?? new VideoCaptureOptions());
        var page = new MediaCapturePage(MediaCaptureMode.Video, opts);

        var result = await this
            .PresentAsync(page, () => this.RequestCameraPermissionAsync(opts.IncludeAudio, ct), ct)
            .ConfigureAwait(true);

        return result is CameraVideo video
            ? new MediaVideo(video.FilePath, video.Duration, ContentTypeForVideo(video.FilePath))
            : null;
    }

    // -------------------------------------------------------------------------------------------------
    // gallery
    // -------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<MediaPhoto?> PickPhotoAsync(MediaPickOptions? options = null, CancellationToken ct = default)
    {
        var opts = this.Prepare(options);
        // the single-select PickPhotoAsync is obsolete in MAUI 10; take the head of the multi-select result
        var picked = await this.PickFirstAsync(() => MediaPicker.Default.PickPhotosAsync(ToPickerOptions(opts))).ConfigureAwait(false);
        if (picked is null)
            return null;

        using var stream = await picked.OpenReadAsync().ConfigureAwait(false);
        return await MediaImaging
            .ProcessAsync(stream, this.Format(opts.OutputFormat), this.Quality(opts.CompressionQuality), this.Dimension(opts.MaxDimension))
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MediaPhoto>> PickPhotosAsync(int maxCount = 10, MediaPickOptions? options = null, CancellationToken ct = default)
    {
        var opts = this.Prepare(options);
        var results = new List<MediaPhoto>();

        IEnumerable<FileResult>? picked;
        try
        {
            picked = await MediaPicker.Default.PickPhotosAsync(ToPickerOptions(opts)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is PermissionException or FeatureNotSupportedException or NotImplementedException)
        {
            return results;
        }

        if (picked is null)
            return results;

        foreach (var file in picked.Take(Math.Max(1, maxCount)))
        {
            ct.ThrowIfCancellationRequested();
            using var stream = await file.OpenReadAsync().ConfigureAwait(false);
            var photo = await MediaImaging
                .ProcessAsync(stream, this.Format(opts.OutputFormat), this.Quality(opts.CompressionQuality), this.Dimension(opts.MaxDimension))
                .ConfigureAwait(false);

            if (photo is not null)
                results.Add(photo);
        }
        return results;
    }

    /// <inheritdoc/>
    public async Task<MediaVideo?> PickVideoAsync(MediaPickOptions? options = null, CancellationToken ct = default)
    {
        var opts = this.Prepare(options);
        var picked = await this.PickFirstAsync(() => MediaPicker.Default.PickVideosAsync(ToPickerOptions(opts))).ConfigureAwait(false);
        return picked is null ? null : new MediaVideo(picked.FullPath, null, ContentTypeForVideo(picked.FullPath));
    }

    async Task<FileResult?> PickFirstAsync(Func<Task<List<FileResult>>> pick)
    {
        try
        {
            var picked = await pick().ConfigureAwait(false);
            return picked?.FirstOrDefault();
        }
        catch (Exception ex) when (ex is PermissionException or FeatureNotSupportedException or NotImplementedException)
        {
            // a refused or unavailable gallery is an ordinary "no photo", not a crash
            return null;
        }
    }

    // -------------------------------------------------------------------------------------------------
    // scanning
    // -------------------------------------------------------------------------------------------------

    /// <inheritdoc/>
    public async IAsyncEnumerable<T> ScanAsync<T>(
        MediaScanRequest<T> request,
        MediaScanOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default
    )
    {
        var opts = this.Prepare(options ?? new MediaScanOptions());

        if (await this.RequestCameraPermissionAsync(false, ct).ConfigureAwait(true) != MediaPermissionStatus.Granted)
            yield break;

        request.Analyzer.ScanWindow = opts.ScanWindow;
        request.Analyzer.ShowBoundingBox = opts.ShowBoundingBox;

        var page = new MediaCapturePage(MediaCaptureMode.Scan, opts, request.Analyzer);

        // unbounded so a fast analyzer never blocks the capture thread waiting on a slow consumer
        var channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleReader = true });
        request.Subscribe(value => channel.Writer.TryWrite(value));

        var nav = GetNavigation();
        if (nav is null)
            yield break;

        await nav.PushModalAsync(page, false).ConfigureAwait(true);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (opts.Timeout is { } timeout)
            linked.CancelAfter(timeout);

        // the modal closing (dismissed, done, or MaxResults reached) ends the sequence
        _ = page.Completion.ContinueWith(_ => channel.Writer.TryComplete(), TaskScheduler.Default);
        using var registration = linked.Token.Register(() => page.Complete(null));

        var seen = opts.FilterDuplicates && request.DuplicateKey is not null ? new HashSet<string>() : null;
        try
        {
            while (true)
            {
                T value;
                try
                {
                    if (!await channel.Reader.WaitToReadAsync(linked.Token).ConfigureAwait(true))
                        break;

                    if (!channel.Reader.TryRead(out value!))
                        continue;
                }
                catch (OperationCanceledException)
                {
                    break; // timeout or caller cancellation — end the sequence, don't throw out of it
                }

                if (seen is not null && !seen.Add(request.DuplicateKey!(value)))
                    continue;

                page.ReportScanResult(request.Describe?.Invoke(value));

                // Timeout is an *idle* timeout, so finding something restarts the clock. A hard total cap
                // would cut a productive session off mid-scan, which is the opposite of what a caller
                // setting "give up if nothing turns up" is asking for.
                if (opts.Timeout is { } idle)
                    linked.CancelAfter(idle);

                yield return value;
            }
        }
        finally
        {
            // covers every exit including the caller breaking out of their await foreach, which is exactly
            // how the single-result overloads close the modal after one hit
            page.Complete(null);
            if (nav.ModalStack.Contains(page))
                await nav.PopModalAsync(false).ConfigureAwait(true);
        }
    }

    // -------------------------------------------------------------------------------------------------
    // plumbing
    // -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Present the modal, gate it on <paramref name="permission"/>, and wait for it to finish. Returns the
    /// page's result, or <c>null</c> for a cancel/refusal.
    /// </summary>
    async Task<object?> PresentAsync(MediaCapturePage page, Func<Task<MediaPermissionStatus>> permission, CancellationToken ct)
    {
        if (await permission().ConfigureAwait(true) != MediaPermissionStatus.Granted)
            return null;

        // resolved per call, never cached: a service that remembers the page it first saw stops working the
        // moment the app navigates somewhere else
        var nav = GetNavigation();
        if (nav is null)
            return null;

        await nav.PushModalAsync(page, false).ConfigureAwait(true);
        using var registration = ct.Register(() => page.Complete(null));
        try
        {
            return await page.Completion.ConfigureAwait(true);
        }
        finally
        {
            if (nav.ModalStack.Contains(page))
                await nav.PopModalAsync(false).ConfigureAwait(true);
        }
    }

    /// <summary>Fold the service-wide defaults into a per-call options object.</summary>
    TOptions Prepare<TOptions>(TOptions options) where TOptions : MediaCameraOptions
    {
        this.Options.ConfigureDefaults?.Invoke(options);
        return options;
    }

    MediaPickOptions Prepare(MediaPickOptions? options) => options ?? new MediaPickOptions();

    // the three encoding settings fall back to the service-wide defaults, which is what makes
    // "our photos are 85% JPEG capped at 2048px" a one-line registration rather than a call-site habit
    int Quality(int? value) => value ?? this.Options.CompressionQuality;

    int Dimension(int? value) => value ?? this.Options.MaxDimension;

    MediaImageFormat Format(MediaImageFormat? value) => value ?? this.Options.OutputFormat;

    static MediaPickerOptions? ToPickerOptions(MediaPickOptions options)
        => String.IsNullOrWhiteSpace(options.Title) ? null : new MediaPickerOptions { Title = options.Title };

    static string ContentTypeForVideo(string path)
        => Path.GetExtension(path).Equals(".mov", StringComparison.OrdinalIgnoreCase) ? "video/quicktime" : "video/mp4";

    /// <summary>
    /// The navigation the modal is pushed onto, resolved fresh every time from the window that is actually
    /// on screen.
    /// </summary>
    static INavigation? GetNavigation()
        => Application.Current?.Windows.LastOrDefault(w => w.Page is not null)?.Page?.Navigation;
}

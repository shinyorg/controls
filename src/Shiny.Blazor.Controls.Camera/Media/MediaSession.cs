using Shiny.Controls.Camera;

namespace Shiny.Blazor.Controls.Camera.Media;

/// <summary>What the modal camera was opened for.</summary>
public enum MediaSessionKind
{
    Photo,
    Video,
    Scan
}


/// <summary>
/// The state of the modal camera on screen, rendered by <c>MediaHost</c>. Owned by <see cref="MediaService"/>;
/// the host only reads it and forwards button presses back to the service.
/// </summary>
public sealed class MediaSession
{
    internal MediaSession(MediaSessionKind kind, MediaCameraOptions options, CameraAnalyzer? analyzer = null)
    {
        this.Kind = kind;
        this.Options = options;
        this.Analyzer = analyzer;
        this.Facing = options.Facing;
        this.CameraId = options.CameraId;
        this.Filter = options.Filter;
        this.Effects = options.Effects.ToArray();

        if (options.ShowEffectPicker)
        {
            this.EffectChoices = options.EffectChoices ?? MediaEffectChoices.Default;
            this.SelectedEffect = this.EffectChoices.FirstOrDefault(c => c.Filter == options.Filter && c.Effect is null)
                ?? this.EffectChoices.FirstOrDefault();
        }
    }

    public MediaSessionKind Kind { get; }
    public MediaCameraOptions Options { get; }

    /// <summary>The analyzer on the camera — scans only.</summary>
    public CameraAnalyzer? Analyzer { get; }

    public CameraFacing Facing { get; internal set; }
    public string? CameraId { get; internal set; }
    public CameraFilter Filter { get; internal set; }
    public IReadOnlyList<ICameraEffect> Effects { get; internal set; }

    /// <summary>The effect strip, or empty when <see cref="MediaCameraOptions.ShowEffectPicker"/> is off.</summary>
    public IReadOnlyList<MediaEffectChoice> EffectChoices { get; } = [];
    public MediaEffectChoice? SelectedEffect { get; internal set; }

    /// <summary>Why the camera is not showing (permission refused, no device) — drawn over the preview.</summary>
    public string? Error { get; internal set; }

    /// <summary>A capture or a stop is in flight — the shutter is disabled.</summary>
    public bool IsBusy { get; internal set; }

    /// <summary>A still awaiting retake/accept, when <see cref="PhotoCaptureOptions.ShowConfirmation"/> is on.</summary>
    public MediaBlobInfo? PendingPhoto { get; internal set; }

    public bool IsRecording { get; internal set; }
    public DateTimeOffset? RecordingStartedAt { get; internal set; }

    public int ResultCount { get; internal set; }
    public string? LastResult { get; internal set; }

    /// <summary>A scan's <see cref="MediaScanRequest{T}.WorkingText"/>, while it is working.</summary>
    public string? Status { get; internal set; }

    /// <summary>Unique per session, so the host builds a fresh <see cref="CameraView"/> for each one.</summary>
    public string Key { get; } = Guid.NewGuid().ToString("N");

    // --- service plumbing ---------------------------------------------------------------------------

    internal CameraView? Camera { get; set; }
    internal TaskCompletionSource<CameraView> CameraReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Fires when the session ends for any reason — close, ✓, a result, cancellation.</summary>
    internal CancellationTokenSource Ended { get; } = new();

    /// <summary>The value the service call returns: a photo, a video, or (scans) nothing.</summary>
    internal TaskCompletionSource<object?> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Finish(object? result)
    {
        this.Result.TrySetResult(result);
        try { this.Ended.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}

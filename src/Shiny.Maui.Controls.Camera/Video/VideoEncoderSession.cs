namespace Shiny.Maui.Controls.Camera.Video;

/// <summary>
/// A snapshot of a <see cref="VideoEncoder"/>'s settings, swapped as one reference so the analysis thread
/// never reads a bindable property and never sees half of a profile change.
/// </summary>
sealed record VideoEncoderSettings(
    VideoCodec Codec,
    int MaxDimension,
    double FrameRate,
    int Bitrate,
    double JpegQuality,
    TimeSpan KeyFrameInterval
);


/// <summary>
/// One platform encoder configured for one output size and one set of settings. The <see cref="VideoEncoder"/>
/// creates it on the first frame, keeps it while <see cref="CanEncode"/> says it still fits, and disposes and
/// recreates it when it doesn't (a profile switch, a rotated buffer).
/// </summary>
/// <remarks>
/// Only ever called from the analysis thread, one call at a time, and never after <see cref="IDisposable.Dispose"/>.
/// Encoded frames go to the sink the session was created with, on whatever thread the platform delivers them.
/// </remarks>
interface IVideoEncoderSession : IDisposable
{
    /// <summary>
    /// Whether this session can encode at <paramref name="width"/> x <paramref name="height"/> with
    /// <paramref name="settings"/>, adopting any change it can make live (a JPEG session takes a new quality on
    /// the next frame; nothing about an H.264 session's size or rate can change without a new one).
    /// </summary>
    bool CanEncode(VideoEncoderSettings settings, int width, int height);

    /// <summary>
    /// Scale <paramref name="frame"/> to <paramref name="width"/> x <paramref name="height"/>, encode it, and
    /// deliver the result to the sink (now or later). The frame is only valid until the returned task
    /// completes, and on Apple only until the first await, so read it synchronously.
    /// </summary>
    ValueTask EncodeAsync(VideoFrame frame, int width, int height, TimeSpan timestamp, bool forceKeyFrame, CancellationToken ct);
}

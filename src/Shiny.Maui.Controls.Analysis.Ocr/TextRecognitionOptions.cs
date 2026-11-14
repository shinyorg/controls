using Microsoft.Maui.Graphics;

namespace Shiny.Maui.Controls.Analysis.Ocr;

/// <summary>
/// Tuning for a single <see cref="TextRecognizer.RecognizeAsync(VideoFrame, TextRecognitionOptions, CancellationToken)"/>
/// call. The default instance is whole-frame recognition at the platform's own settings, which is what the
/// parameterless overload uses.
/// </summary>
/// <remarks>
/// These exist for <b>small, distant text</b> — a license plate, a road sign, a shelf label — which whole-frame
/// OCR does not find. Two things defeat it: the platform engines ignore text below a minimum height (Apple
/// Vision's default is 1/32 of the image height, so ~34px in a 1080p frame), and they downscale the image
/// before recognizing, which erases what little detail small text had. Cropping to a region and upscaling it
/// addresses both — the text is now a large fraction of a small image.
/// <para>
/// This record is the cache key for a frame's recognition results, so two analyzers asking for the same region
/// on the same frame share one pass, while different regions each get their own.
/// </para>
/// </remarks>
/// <param name="RegionOfInterest">
/// Normalized (0..1) rectangle in <b>upright image space</b> — the same space <see cref="OverlayBox"/> and
/// <see cref="RecognizedText.BoundingBox"/> use — to crop and recognize. Results are mapped back into full-frame
/// upright space, so a caller never sees crop coordinates. <c>null</c> (the default) recognizes the whole frame.
/// </param>
/// <param name="MinimumTextHeight">
/// The smallest text to look for, as a fraction of the recognized image's height. <c>0</c> (the default) leaves
/// the platform default in place. Honored by Apple Vision; Android MLKit and Windows expose no equivalent and
/// ignore it — on those platforms <paramref name="MinimumInputHeight"/> is what buys small text back.
/// </param>
/// <param name="MinimumInputHeight">
/// Upscale the cropped region so it is at least this many pixels tall before recognizing. A tight region can be
/// only a few dozen pixels tall, which is under what the engines resolve. <c>0</c> (the default) disables it.
/// Ignored when <paramref name="RegionOfInterest"/> is <c>null</c> — upscaling a whole frame buys nothing and
/// costs a full-resolution resample per frame.
/// </param>
public record TextRecognitionOptions(
    RectF? RegionOfInterest = null,
    float MinimumTextHeight = 0f,
    int MinimumInputHeight = 0
)
{
    /// <summary>Whole-frame recognition at the platform's own settings.</summary>
    public static readonly TextRecognitionOptions Default = new();
}

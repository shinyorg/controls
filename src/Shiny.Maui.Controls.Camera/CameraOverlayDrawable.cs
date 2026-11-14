namespace Shiny.Maui.Controls.Camera;

/// <summary>
/// An <see cref="IDrawable"/> that paints analyzer-supplied <see cref="OverlayBox"/>es (and their captions)
/// over the preview, then the camera's draw effects. The box and scan-window drawing is the shared
/// <see cref="AnalysisOverlayDrawable"/> (a <c>MediaElement</c> uses the same one); this adds the effects. Usually
/// you don't use this directly — drop a <see cref="CameraOverlayView"/> over a <see cref="CameraView"/>, which
/// owns one of these and feeds it.
/// </summary>
public class CameraOverlayDrawable : AnalysisOverlayDrawable
{
    /// <summary>
    /// The camera's <see cref="IDrawEffect"/>s, drawn over the preview after the analyzer boxes. The same
    /// effects are composited into captured stills and recorded video, so one implementation covers all three.
    /// </summary>
    public IReadOnlyList<IDrawEffect> DrawEffects { get; set; } = [];

    /// <summary>The active analyzer's latest typed result, handed to draw effects so they can anchor to it.</summary>
    public object? AnalyzerResult { get; set; }

    /// <summary>Which camera is producing frames — reported to draw effects.</summary>
    public CameraFacing Facing { get; set; }

    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    long drawIndex;

    /// <inheritdoc/>
    public override void Draw(ICanvas canvas, RectF dirtyRect)
    {
        base.Draw(canvas, dirtyRect);
        this.DrawEffectsOver(canvas, dirtyRect);
    }

    // Run the camera's draw effects over the preview.
    //
    // The IDrawEffect contract is "draw in frame pixel space", which on the preview means the space of the
    // *displayed image* — not the whole view, which under AspectFill is a crop of it and under AspectFit is
    // letterboxed. So we map the unit rect through the same transform the boxes use to find where the image
    // actually lands, translate the canvas onto it, and report its size as the frame size. An effect written
    // against context.ToPixels(...) then lands identically on the preview, on a still, and in a recording.
    void DrawEffectsOver(ICanvas canvas, RectF dirtyRect)
    {
        var effects = this.DrawEffects;
        if (effects.Count == 0)
            return;

        var image = this.MapToView(new RectF(0, 0, 1, 1), dirtyRect);

        var context = new CameraEffectContext(
            this.clock.Elapsed,
            this.drawIndex++,
            (int)image.Width,
            (int)image.Height,
            this.Facing,
            CameraSurface.Preview,
            this.Boxes,
            this.AnalyzerResult
        );

        var bounds = new RectF(0, 0, image.Width, image.Height);
        foreach (var effect in effects)
        {
            canvas.SaveState();
            try
            {
                canvas.ClipRectangle(image);
                canvas.Translate(image.X, image.Y);
                effect.Draw(canvas, bounds, context);
            }
            catch (Exception)
            {
                // A misbehaving effect must not take the whole overlay down mid-frame; skip it and keep drawing.
            }
            finally
            {
                canvas.RestoreState();
            }
        }
    }
}

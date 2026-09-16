using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;

namespace Shiny.Maui.Controls.Camera.Ocr;

/// <summary>
/// Recognizes text in each frame (native Vision / MLKit / Windows.Media.Ocr) and raises
/// <see cref="TextRecognized"/> with the recognized blocks. Draws a box around each block (captioned with
/// its text) unless <see cref="IncludeTextBlocks"/> is disabled. For structured extraction (invoices, IDs,
/// …) use the document analyzers in <c>Shiny.Maui.Controls.Camera.Documents</c>, which reuse the same
/// <see cref="TextRecognizer"/>.
/// </summary>
public class OcrAnalyzer : FrameAnalyzer
{
    readonly TextRecognizer recognizer = new();

    /// <inheritdoc/>
    public override string Id => "shiny.camera.ocr";

    /// <summary>Draw a box around each recognized text block. Default <c>true</c> (the event fires either way).</summary>
    public bool IncludeTextBlocks { get; set; } = true;

    /// <summary>Box outline + caption color. Default a violet accent.</summary>
    public Color BoxColor { get; set; } = Color.FromArgb("#A78BFA");

    /// <summary>
    /// Smallest text to recognize, as a fraction of the recognized image's height. <c>0</c> (default) leaves the
    /// platform default — Apple Vision's is 1/32, so ~34px in a 1080p frame, which discards small or distant
    /// text outright. Apple-only; see <see cref="TextRecognitionOptions.MinimumTextHeight"/>.
    /// </summary>
    public float MinimumTextHeight { get; set; }

    /// <summary>
    /// Upscale the <see cref="FrameAnalyzer.ScanWindow"/> crop to at least this many pixels tall before
    /// recognizing, so small text survives the engine's own downscale. <c>0</c> (default) disables it, and it
    /// only applies when a scan window is set. See <see cref="TextRecognitionOptions.MinimumInputHeight"/>.
    /// </summary>
    public int MinimumInputHeight { get; set; }

    /// <summary>Command invoked (with the <see cref="TextRecognizedEventArgs"/>) when text is recognized.</summary>
    public static readonly BindableProperty TextRecognizedCommandProperty = BindableProperty.Create(
        nameof(TextRecognizedCommand), typeof(ICommand), typeof(OcrAnalyzer));

    /// <inheritdoc cref="TextRecognizedCommandProperty"/>
    public ICommand? TextRecognizedCommand
    {
        get => (ICommand?)this.GetValue(TextRecognizedCommandProperty);
        set => this.SetValue(TextRecognizedCommandProperty, value);
    }

    /// <summary>
    /// Optional selector deciding the boxes to draw for the recognized text; return <c>null</c> for no
    /// overlay. When unset the analyzer draws a box per text block (subject to <see cref="IncludeTextBlocks"/>).
    /// </summary>
    public Func<TextRecognizedEventArgs, IReadOnlyList<OverlayBox>?>? OverlayProvider { get; set; }

    /// <summary>
    /// Continuation invoked (on the UI thread) with the recognized text while the analyzer is armed; return
    /// <c>true</c> to keep scanning (stay armed), <c>false</c> to stop until the next <see cref="CameraView.Scan"/>.
    /// When unset, delivery is single-shot. Bindable so it can target a VM method in XAML.
    /// </summary>
    public static readonly BindableProperty OnDetectedProperty = BindableProperty.Create(
        nameof(OnDetected), typeof(Func<TextRecognizedEventArgs, Task<bool>>), typeof(OcrAnalyzer));

    /// <inheritdoc cref="OnDetectedProperty"/>
    public Func<TextRecognizedEventArgs, Task<bool>>? OnDetected
    {
        get => (Func<TextRecognizedEventArgs, Task<bool>>?)this.GetValue(OnDetectedProperty);
        set => this.SetValue(OnDetectedProperty, value);
    }

    /// <summary>Raised on the UI thread when text is recognized in a frame, while the analyzer is armed.</summary>
    public event EventHandler<TextRecognizedEventArgs>? TextRecognized;

    /// <inheritdoc/>
    /// <remarks>Closes the native text recognizer client (Android ML Kit); it is re-created on the next frame.</remarks>
    protected override void OnDetached()
    {
        base.OnDetached();
        this.recognizer.ReleaseNativeResources();
    }

    /// <inheritdoc/>
    public override async ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeAsync(CameraFrame frame, CancellationToken ct)
    {
        // ScanWindow becomes the recognizer's region of interest, so it restricts what the engine actually
        // looks at rather than only post-filtering what came back — which is the difference between finding
        // small text inside the window and never seeing it at all.
        var options = this.ScanWindow is { } window
            ? new TextRecognitionOptions(window, this.MinimumTextHeight, this.MinimumInputHeight)
            : new TextRecognitionOptions(null, this.MinimumTextHeight);

        var text = await this.recognizer.RecognizeAsync(frame, options, ct).ConfigureAwait(false);
        if (text.Count == 0)
            return null;

        var args = new TextRecognizedEventArgs(text);
        this.Deliver(args, () => this.TextRecognized?.Invoke(this, args), this.TextRecognizedCommand, this.OnDetected);

        return this.ResolveOverlay(args, this.OverlayProvider, () =>
        {
            if (!this.IncludeTextBlocks)
                return null;

            var boxes = new OverlayBox[text.Count];
            for (var i = 0; i < text.Count; i++)
                boxes[i] = new OverlayBox(text[i].BoundingBox, this.BoxColor, text[i].Text, this.BoxColor);
            return boxes;
        });
    }
}

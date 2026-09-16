using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;

namespace Shiny.Maui.Controls.Camera.Face;

/// <summary>
/// Detects faces in each frame (Apple Vision on iOS/macOS, Android MLKit, Windows.Media.FaceAnalysis;
/// a no-op on bare net10.0). Raises <see cref="FacesDetected"/> with the located faces and draws a box
/// around each. The per-platform <c>AnalyzeAsync</c> builds the <see cref="DetectedFace"/> list and calls
/// <see cref="Report"/>, which raises the event (on the UI thread) and produces the overlay boxes.
/// </summary>
public partial class FaceAnalyzer : FrameAnalyzer
{
    /// <inheritdoc/>
    public override string Id => "shiny.camera.face";

    /// <summary>Color used for the face boxes and labels. Default a teal accent.</summary>
    public Color BoxColor { get; set; } = Color.FromArgb("#22D3EE");

    /// <summary>Caption drawn on each face box (null/empty for none). Default "Face".</summary>
    public string? Label { get; set; } = "Face";

    /// <summary>Command invoked (with the <see cref="FacesDetectedEventArgs"/>) when faces are detected.</summary>
    public static readonly BindableProperty FacesDetectedCommandProperty = BindableProperty.Create(
        nameof(FacesDetectedCommand), typeof(ICommand), typeof(FaceAnalyzer));

    /// <inheritdoc cref="FacesDetectedCommandProperty"/>
    public ICommand? FacesDetectedCommand
    {
        get => (ICommand?)this.GetValue(FacesDetectedCommandProperty);
        set => this.SetValue(FacesDetectedCommandProperty, value);
    }

    /// <summary>
    /// Optional selector deciding the boxes to draw for the detected faces; return <c>null</c> for no overlay.
    /// When unset the analyzer draws one <see cref="BoxColor"/> box per face.
    /// </summary>
    public Func<FacesDetectedEventArgs, IReadOnlyList<OverlayBox>?>? OverlayProvider { get; set; }

    /// <summary>
    /// Continuation invoked (on the UI thread) with the detected faces while the analyzer is armed; return
    /// <c>true</c> to keep scanning (stay armed), <c>false</c> to stop until the next <see cref="CameraView.Scan"/>.
    /// When unset, delivery is single-shot. Bindable so it can target a VM method in XAML.
    /// </summary>
    public static readonly BindableProperty OnDetectedProperty = BindableProperty.Create(
        nameof(OnDetected), typeof(Func<FacesDetectedEventArgs, Task<bool>>), typeof(FaceAnalyzer));

    /// <inheritdoc cref="OnDetectedProperty"/>
    public Func<FacesDetectedEventArgs, Task<bool>>? OnDetected
    {
        get => (Func<FacesDetectedEventArgs, Task<bool>>?)this.GetValue(OnDetectedProperty);
        set => this.SetValue(OnDetectedProperty, value);
    }

    /// <summary>
    /// Whether to also detect facial feature points (<see cref="FaceLandmarks"/>). Default <c>false</c>.
    /// </summary>
    /// <remarks>
    /// Off by default because landmarks cost meaningfully more per frame than bounding boxes alone, and most
    /// uses (counting faces, framing a portrait) don't need them. Turn it on for anything that anchors to
    /// features — a <c>FaceMaskEffect</c> requires it. Populated by Apple Vision and Android MLKit; the
    /// Windows and managed backends report boxes only, so <c>Landmarks</c> stays <c>null</c> there.
    /// </remarks>
    public static readonly BindableProperty DetectLandmarksProperty = BindableProperty.Create(
        nameof(DetectLandmarks), typeof(bool), typeof(FaceAnalyzer), false);

    /// <inheritdoc cref="DetectLandmarksProperty"/>
    public bool DetectLandmarks
    {
        get => (bool)this.GetValue(DetectLandmarksProperty);
        set => this.SetValue(DetectLandmarksProperty, value);
    }

    /// <summary>Raised on the UI thread when one or more faces are detected in a frame, while the analyzer is armed.</summary>
    public event EventHandler<FacesDetectedEventArgs>? FacesDetected;

    /// <inheritdoc/>
    /// <remarks>Closes the native face detector clients (Android ML Kit); they are re-created on the next frame.</remarks>
    protected override void OnDetached()
    {
        base.OnDetached();
        this.PublishLive(null); // a detached analyzer tracks nothing — don't leave a mask anchored to a stale face
        this.ReleasePlatform();
    }

    partial void ReleasePlatform();

    /// <summary>Deliver <see cref="FacesDetected"/>/command (while armed) and turn the faces into overlay boxes (null clears).</summary>
    protected IReadOnlyList<OverlayBox>? Report(IReadOnlyList<DetectedFace> faces)
    {
        // Publish on EVERY frame, before the arm gate. Draw effects (a face mask) need to follow the face
        // continuously; the typed event below is one-shot by design and would leave a mask frozen in place.
        this.PublishLive(faces.Count == 0 ? null : faces);

        if (faces.Count == 0)
            return null;

        var args = new FacesDetectedEventArgs(faces);
        this.Deliver(args, () => this.FacesDetected?.Invoke(this, args), this.FacesDetectedCommand, this.OnDetected);

        return this.ResolveOverlay(args, this.OverlayProvider, () =>
        {
            var boxes = new OverlayBox[faces.Count];
            for (var i = 0; i < faces.Count; i++)
                boxes[i] = new OverlayBox(faces[i].Bounds, this.BoxColor, this.Label, this.BoxColor);
            return boxes;
        });
    }
}

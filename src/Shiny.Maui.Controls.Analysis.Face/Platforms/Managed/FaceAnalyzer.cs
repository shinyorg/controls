using Shiny.Controls.Camera;

namespace Shiny.Maui.Controls.Analysis.Face;

/// <summary>No-op fallback for the platform-agnostic (net10.0) target; face detection requires a native backend.</summary>
public partial class FaceAnalyzer
{
    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeAsync(VideoFrame frame, CancellationToken ct)
        => default;
}

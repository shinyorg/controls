namespace Shiny.Controls.Camera;

/// <summary>
/// <see cref="IScanOptions"/> for a scan service that drives a camera — what lets an analyzer package that knows
/// nothing about the camera control still ask for a lens (face detection defaults to the front camera).
/// </summary>
public interface ICameraScanOptions : IScanOptions
{
    /// <summary>Which lens to scan with.</summary>
    CameraFacing Facing { get; set; }
}

namespace Shiny.Controls.Camera;

/// <summary>Which physical camera to use.</summary>
public enum CameraFacing
{
    /// <summary>The rear/world-facing camera (default).</summary>
    Back,

    /// <summary>The front/user-facing (selfie) camera.</summary>
    Front,

    /// <summary>An external/USB camera where supported (desktop, Android UVC).</summary>
    External
}


/// <summary>Flash behaviour used when capturing a photo.</summary>
public enum CameraFlashMode
{
    /// <summary>Flash never fires.</summary>
    Off,

    /// <summary>Flash always fires on capture.</summary>
    On,

    /// <summary>The system decides based on the scene.</summary>
    Auto
}


/// <summary>The pixel layout of a <see cref="CameraFrame"/>'s native buffer.</summary>
public enum CameraFrameFormat
{
    /// <summary>Unknown/unspecified.</summary>
    Unknown,

    /// <summary>32-bit BGRA (Apple 32BGRA, Windows BGRA8).</summary>
    Bgra32,

    /// <summary>Planar YUV 4:2:0 (Android YUV_420_888); plane 0 is luminance.</summary>
    Yuv420,

    /// <summary>Single 8-bit luminance plane.</summary>
    Grayscale8
}


/// <summary>How the lens chooses what to focus on.</summary>
public enum CameraFocusMode
{
    /// <summary>
    /// Continuous autofocus across the lens's whole range, from the centre of the frame (default). Right for
    /// a handheld camera whose subject distance keeps changing — a document moved in close, a barcode.
    /// </summary>
    Auto,

    /// <summary>
    /// Continuous autofocus that is told to ignore close subjects. For a camera looking <i>through</i>
    /// something — a windscreen, a window — where full-range autofocus will happily lock onto rain, dirt
    /// or glare on the glass and leave the scene behind it soft.
    /// </summary>
    /// <remarks>
    /// Only Apple has a far-range restriction on autofocus. Android has no equivalent, so there this
    /// behaves as <see cref="Infinity"/>, which is the stricter answer to the same problem.
    /// </remarks>
    Far,

    /// <summary>
    /// Autofocus off, lens parked at its far limit. Nothing close to the lens can pull focus, ever — and
    /// because a phone's wide lens has a short hyperfocal distance, everything from a couple of metres out
    /// stays sharp. This is how a dedicated dash cam works (most are fixed-focus).
    /// </summary>
    Infinity
}

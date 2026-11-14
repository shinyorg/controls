namespace Shiny.Controls.Analysis;

/// <summary>How a camera preview or a video fills its view, which determines how detection boxes map onto it.</summary>
public enum PreviewScaleMode
{
    /// <summary>Scale to cover the view, cropping the overflow (iOS ResizeAspectFill, Android FILL_CENTER). Default.</summary>
    AspectFill,

    /// <summary>Scale to fit inside the view, letterboxing the remainder (ResizeAspect / FIT_CENTER).</summary>
    AspectFit
}

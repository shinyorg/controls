namespace Shiny.Blazor.Controls;

/// <summary>Per-run settings for <see cref="IProgressLineService.Start"/>.</summary>
public class ProgressLineConfig
{
    /// <inheritdoc cref="ProgressLine.Position"/>
    public ProgressLinePosition Position { get; set; } = ProgressLinePosition.Top;

    /// <inheritdoc cref="ProgressLine.Anchor"/>
    public ProgressLineAnchor Anchor { get; set; } = ProgressLineAnchor.Viewport;

    /// <inheritdoc cref="ProgressLine.BarColor"/>
    public string BarColor { get; set; } = "var(--shiny-color-primary, #0055D9)";

    /// <inheritdoc cref="ProgressLine.TrackColor"/>
    public string TrackColor { get; set; } = "transparent";

    /// <inheritdoc cref="ProgressLine.LineHeight"/>
    public double LineHeight { get; set; } = 3;

    /// <inheritdoc cref="ProgressLine.CornerRadius"/>
    public string CornerRadius { get; set; } = "0";

    /// <inheritdoc cref="ProgressLine.UseGradient"/>
    public bool UseGradient { get; set; }

    /// <inheritdoc cref="ProgressLine.GradientStartColor"/>
    public string GradientStartColor { get; set; } = "var(--shiny-color-primary, #0055D9)";

    /// <inheritdoc cref="ProgressLine.GradientEndColor"/>
    public string GradientEndColor { get; set; } = "var(--shiny-color-tertiary, #6A4CAD)";

    /// <inheritdoc cref="ProgressLine.PulseEnabled"/>
    public bool PulseEnabled { get; set; }

    /// <inheritdoc cref="ProgressLine.Offset"/>
    public string Offset { get; set; } = "0px";

    /// <inheritdoc cref="ProgressLine.RespectSafeArea"/>
    public bool RespectSafeArea { get; set; } = true;

    /// <inheritdoc cref="ProgressLine.FadeDuration"/>
    public int FadeDuration { get; set; } = 200;

    /// <inheritdoc cref="ProgressLine.ProgressAnimationDuration"/>
    public int ProgressAnimationDuration { get; set; } = 250;

    /// <summary>
    /// Run the sweeping indeterminate animation instead of a fill. Use it when the work genuinely has
    /// no measurable progress; otherwise the trickle below is a better lie than a sweep, because it
    /// still moves toward completion.
    /// </summary>
    public bool Indeterminate { get; set; }

    /// <summary>
    /// Whether the line creeps forward on its own between <see cref="IProgressLineHandle.SetProgress"/>
    /// calls, so a request that reports nothing for two seconds still looks alive.
    /// </summary>
    public bool Trickle { get; set; } = true;

    /// <summary>Where the line jumps to the instant it appears, so it is never a zero-width nothing.</summary>
    public double StartProgress { get; set; } = 0.08;

    /// <summary>
    /// The asymptote the trickle approaches but never reaches. Completion has to come from the caller
    /// — a line that trickles to 100% on its own has told the user the work finished when it has not.
    /// </summary>
    public double TrickleCeiling { get; set; } = 0.9;

    /// <summary>How often the trickle advances.</summary>
    public TimeSpan TrickleInterval { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Fraction of the remaining distance to the ceiling covered per tick. Lower creeps for longer.
    /// </summary>
    public double TrickleRate { get; set; } = 0.12;
}

namespace Shiny.Blazor.Controls.Toast;

public class ToastConfig
{
    public string Text { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(3);
    public ToastPosition Position { get; set; } = ToastPosition.Bottom;
    public ToastDisplayMode DisplayMode { get; set; } = ToastDisplayMode.Pill;
    public bool DismissOnTap { get; set; } = true;
    public ToastQueueMode QueueMode { get; set; } = ToastQueueMode.Queue;
    public double Offset { get; set; } = 12;
    public ToastSpinnerPosition Spinner { get; set; } = ToastSpinnerPosition.None;
    public bool ShowProgressBar { get; set; }
    public string? BackgroundColor { get; set; }
    public string? TextColor { get; set; }
    public string? BorderColor { get; set; }
    public double BorderThickness { get; set; }
    public double CornerRadius { get; set; } = 20;
    public string? IconHtml { get; set; }
    public ToastTextOverflow TextOverflow { get; set; } = ToastTextOverflow.Ellipsis;
    public double MarqueeSpeedPixelsPerSecond { get; set; } = 40;

    /// <summary>
    /// Number of full marquee scroll passes before auto-dismiss.
    /// Default is 1. Set to 0 to use Duration instead (marquee loops indefinitely).
    /// </summary>
    public int MarqueeLoops { get; set; } = 1;
    /// <summary>Runs when the body is clicked. On a snackbar this never fires from the action button.</summary>
    public Action? TapCallback { get; set; }

    /// <summary>
    /// Holds the auto-dismiss countdown (and its progress bar) while the pointer is over the toast or
    /// focus is inside it, so nobody loses a message while reaching for it. Off by default for a
    /// toast, on for a snackbar.
    /// </summary>
    public bool PauseOnHover { get; set; }
}

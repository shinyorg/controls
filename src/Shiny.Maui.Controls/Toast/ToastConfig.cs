using System.Windows.Input;

namespace Shiny.Maui.Controls.Toast;

public class ToastConfig
{
    /// <summary>Appended to <see cref="AutomationId"/> to name the toast's text label.</summary>
    public const string TextAutomationIdSuffix = "_Text";

    /// <summary>
    /// Names the toast for UI tests. A toast has no owner element a test could find it under, so the id
    /// goes on the toast's tappable chrome and its label is named after it (<see cref="TextAutomationIdSuffix"/>).
    /// Null (the default) leaves the toast unnamed.
    /// </summary>
    public string? AutomationId { get; set; }

    public string Text { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(3);
    public ToastPosition Position { get; set; } = ToastPosition.Bottom;
    public ToastDisplayMode DisplayMode { get; set; } = ToastDisplayMode.Pill;
    public bool DismissOnTap { get; set; } = true;
    public ToastQueueMode QueueMode { get; set; } = ToastQueueMode.Queue;
    public Thickness Offset { get; set; } = new(12);
    public ToastSpinnerPosition Spinner { get; set; } = ToastSpinnerPosition.None;
    public bool UseFeedback { get; set; } = true;
    public bool ShowProgressBar { get; set; }
    public Color? BackgroundColor { get; set; }
    public Color? TextColor { get; set; }
    public Color? BorderColor { get; set; }
    public double BorderThickness { get; set; }
    public double CornerRadius { get; set; } = 20;
    public ImageSource? Icon { get; set; }
    public ToastTextOverflow TextOverflow { get; set; } = ToastTextOverflow.Ellipsis;
    public double MarqueeSpeedPixelsPerSecond { get; set; } = 40;

    /// <summary>
    /// Number of full marquee scroll passes before auto-dismiss.
    /// Default is 1. Set to 0 to use Duration instead (marquee loops indefinitely).
    /// </summary>
    public int MarqueeLoops { get; set; } = 1;
    /// <summary>Runs when the body is tapped. On a snackbar this never fires from the action button.</summary>
    public ICommand? TapCommand { get; set; }

    /// <summary>
    /// Holds the auto-dismiss countdown (and its progress bar) while a pointer is over the toast, so
    /// nobody loses a message while reaching for it. Desktop only: a touch screen has no hover.
    /// Off by default for a toast, on for a snackbar.
    /// </summary>
    public bool PauseOnHover { get; set; }
    public bool AnnounceToScreenReader { get; set; } = true;

    /// <summary>
    /// Set by the typed toast helpers (Info/Success/etc). When set, ToastView binds the
    /// matching theme tokens via dynamic resources for any color not set explicitly.
    /// </summary>
    internal ToastType? Type { get; set; }
}

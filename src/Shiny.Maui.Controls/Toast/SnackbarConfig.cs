using System.Windows.Input;

namespace Shiny.Maui.Controls.Toast;

/// <summary>
/// A toast with snackbar defaults - rectangular, multi-line, replacing the one before it - plus an
/// optional action button. Every <see cref="ToastConfig"/> setting still applies.
/// </summary>
public class SnackbarConfig : ToastConfig
{
    /// <summary>Appended to <see cref="ToastConfig.AutomationId"/> to name the action button.</summary>
    public const string ActionAutomationIdSuffix = "_Action";

    /// <summary>The default duration without an action.</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(4);

    /// <summary>
    /// The default duration once <see cref="ActionText"/> is set: long enough to read the message
    /// and reach the button.
    /// </summary>
    public static readonly TimeSpan DefaultActionDuration = TimeSpan.FromSeconds(8);

    public SnackbarConfig()
    {
        DisplayMode = ToastDisplayMode.Snackbar;
        QueueMode = ToastQueueMode.Replace;
        TextOverflow = ToastTextOverflow.MultiLine;
        Duration = DefaultDuration;
        CornerRadius = 4;
        PauseOnHover = true;
    }

    /// <summary>
    /// Label of the button on the trailing edge. Null (the default) shows no button. The button has
    /// its own touch target: tapping the body still runs <see cref="ToastConfig.TapCommand"/>, never
    /// the action.
    /// </summary>
    public string? ActionText { get; set; }

    /// <summary>Runs when the action button is pressed, before the snackbar closes.</summary>
    public ICommand? ActionCommand { get; set; }

    /// <summary>Action label colour. Null follows the theme's inverse-primary token.</summary>
    public Color? ActionTextColor { get; set; }

    /// <summary>
    /// Shows a line along the top edge that runs until the snackbar is closed. It is indeterminate
    /// until <see cref="ILoadingSnackbar.Progress"/> is given a value. Set by
    /// <see cref="IToaster.LoadingSnackbarAsync"/>.
    /// </summary>
    public bool IsLoading { get; set; }

    /// <summary>Progress, 0 to 1, for a loading snackbar. Null is indeterminate.</summary>
    public double? Progress { get; set; }
}

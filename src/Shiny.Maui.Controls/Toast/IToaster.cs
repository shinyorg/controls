namespace Shiny.Maui.Controls.Toast;

public interface IToaster
{
    Task<IDisposable> ShowAsync(string text);
    Task<IDisposable> ShowAsync(string text, Action<ToastConfig> configure);
    Task<IDisposable> InfoAsync(string text, Action<ToastConfig>? configure = null);
    Task<IDisposable> SuccessAsync(string text, Action<ToastConfig>? configure = null);
    Task<IDisposable> WarningAsync(string text, Action<ToastConfig>? configure = null);
    Task<IDisposable> DangerAsync(string text, Action<ToastConfig>? configure = null);
    Task<IDisposable> CriticalAsync(string text, Action<ToastConfig>? configure = null);

    /// <summary>
    /// Shows a snackbar and completes when it closes, saying why. With <paramref name="actionText"/>
    /// the default duration is <see cref="SnackbarConfig.DefaultActionDuration"/>. Cancelling
    /// <paramref name="cancellationToken"/> dismisses it (or drops it from the queue).
    /// </summary>
    Task<SnackbarResult> SnackbarAsync(
        string text,
        string? actionText = null,
        Action<SnackbarConfig>? configure = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Shows a snackbar with a progress line along its top edge that stays up until the returned
    /// handle is disposed. It does not dismiss on tap; give it an action (e.g. "Cancel") to let the
    /// user stop the work.
    /// </summary>
    Task<ILoadingSnackbar> LoadingSnackbarAsync(string text, Action<SnackbarConfig>? configure = null);
}

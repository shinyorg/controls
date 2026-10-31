namespace Shiny.Blazor.Controls.Toast;

public interface IToastService
{
    Task<IDisposable> ShowAsync(string text, Action<ToastConfig>? configure = null);
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
    /// handle is disposed. It does not dismiss on click; give it an action (e.g. "Cancel") to let the
    /// user stop the work.
    /// </summary>
    Task<ILoadingSnackbar> LoadingSnackbarAsync(string text, Action<SnackbarConfig>? configure = null);
    event Action? OnChanged;
    IReadOnlyList<ToastEntry> ActiveToasts { get; }
}

public sealed class ToastEntry
{
    internal ToastEntry(ToastConfig config, string id)
    {
        Config = config;
        Id = id;
    }

    public ToastConfig Config { get; }
    public string Id { get; }

    /// <summary>Whether the countdown is held by a hover or focus.</summary>
    public bool IsPaused { get; internal set; }

    internal TaskCompletionSource<SnackbarResult> DismissedTcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<IDisposable> ShownTcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource<SnackbarResult> ClosedTcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool IsDismissing { get; set; }

    // The countdown, kept as "time left" so a hover can stop it and pick it up again.
    internal CancellationTokenSource? TimerCts { get; set; }
    internal TimeSpan Remaining { get; set; }
    internal DateTime ArmedAt { get; set; }
}

using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Toast;

public sealed class Toaster : IToaster
{
    public Task<IDisposable> ShowAsync(string text)
        => ShowCoreAsync(text, null);

    public Task<IDisposable> ShowAsync(string text, Action<ToastConfig> configure)
        => ShowCoreAsync(text, configure);

    public Task<IDisposable> InfoAsync(string text, Action<ToastConfig>? configure = null)
        => ShowTypedAsync(ToastType.Info, text, configure);

    public Task<IDisposable> SuccessAsync(string text, Action<ToastConfig>? configure = null)
        => ShowTypedAsync(ToastType.Success, text, configure);

    public Task<IDisposable> WarningAsync(string text, Action<ToastConfig>? configure = null)
        => ShowTypedAsync(ToastType.Warning, text, configure);

    public Task<IDisposable> DangerAsync(string text, Action<ToastConfig>? configure = null)
        => ShowTypedAsync(ToastType.Danger, text, configure);

    public Task<IDisposable> CriticalAsync(string text, Action<ToastConfig>? configure = null)
        => ShowTypedAsync(ToastType.Critical, text, configure);

    public async Task<SnackbarResult> SnackbarAsync(
        string text,
        string? actionText = null,
        Action<SnackbarConfig>? configure = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var config = CreateSnackbar(text, actionText, configure);
        var request = ToastManager.Show(config);
        using var registration = cancellationToken.Register(() => request.Dismiss(SnackbarResult.Dismissed));
        return await request.Closed.Task.ConfigureAwait(false);
    }

    public async Task<ILoadingSnackbar> LoadingSnackbarAsync(string text, Action<SnackbarConfig>? configure = null)
    {
        var config = CreateSnackbar(text, null, cfg =>
        {
            cfg.IsLoading = true;
            cfg.Duration = TimeSpan.Zero;
            cfg.DismissOnTap = false;
            configure?.Invoke(cfg);
        });
        var request = ToastManager.Show(config);
        await request.Shown.Task.ConfigureAwait(false);
        return new LoadingSnackbar(request);
    }

    SnackbarConfig CreateSnackbar(string text, string? actionText, Action<SnackbarConfig>? configure)
    {
        // The action is set before the caller's configure so the longer default duration is in
        // place for it to override.
        var config = new SnackbarConfig { Text = text, ActionText = actionText };
        if (actionText is not null)
            config.Duration = SnackbarConfig.DefaultActionDuration;

        configure?.Invoke(config);

        if (config.UseFeedback)
            FeedbackHelper.Execute(this, "Snackbar", text);

        return config;
    }

    Task<IDisposable> ShowTypedAsync(ToastType type, string text, Action<ToastConfig>? configure) => ShowCoreAsync(text, cfg =>
    {
        ToastStyles.TryApplyStyle(type, cfg);
        ToastStyles.ApplyDefaults(type, cfg);
        configure?.Invoke(cfg);
    });

    Task<IDisposable> ShowCoreAsync(string text, Action<ToastConfig>? configure)
    {
        var config = new ToastConfig { Text = text };
        configure?.Invoke(config);

        if (config.UseFeedback)
            FeedbackHelper.Execute(this, "Show", text);

        return ToastManager.ShowAsync(config);
    }
}

sealed class LoadingSnackbar(ToastRequest request) : ILoadingSnackbar
{
    readonly SnackbarConfig config = (SnackbarConfig)request.Config;

    public string Text
    {
        get => config.Text;
        set
        {
            config.Text = value;
            request.View?.Dispatcher.Dispatch(() => request.View?.UpdateText(value));
        }
    }

    public double? Progress
    {
        get => config.Progress;
        set
        {
            var clamped = value is { } v ? Math.Clamp(v, 0, 1) : (double?)null;
            config.Progress = clamped;
            request.View?.Dispatcher.Dispatch(() => request.View?.UpdateProgress(clamped));
        }
    }

    public Task<SnackbarResult> Closed => request.Closed.Task;

    public void Dispose() => request.Dismiss(SnackbarResult.Dismissed);
}

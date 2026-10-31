namespace Shiny.Blazor.Controls.Toast;

public sealed class ToastService : IToastService
{
    const int MaxStackCount = 5;
    readonly List<ToastEntry> activeToasts = new();
    // A linked list rather than a queue: a Replace toast jumps to the front, and a cancelled one
    // leaves from the middle.
    readonly LinkedList<ToastEntry> queue = new();
    readonly object gate = new();
    ToastEntry? currentQueued;
    bool isProcessingQueue;

    static readonly Dictionary<ToastType, (string Bg, string Text, string Border)> DefaultColors = new()
    {
        [ToastType.Info] = ("#1E40AF", "#FFFFFF", "#3B82F6"),
        [ToastType.Success] = ("#166534", "#FFFFFF", "#22C55E"),
        [ToastType.Warning] = ("#854D0E", "#FFFFFF", "#F59E0B"),
        [ToastType.Danger] = ("#9A3412", "#FFFFFF", "#F97316"),
        [ToastType.Critical] = ("#991B1B", "#FFFFFF", "#EF4444"),
    };

    public event Action? OnChanged;
    public IReadOnlyList<ToastEntry> ActiveToasts => activeToasts.AsReadOnly();

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

    Task<IDisposable> ShowTypedAsync(ToastType type, string text, Action<ToastConfig>? configure)
    {
        return ShowAsync(text, cfg =>
        {
            var (bg, textColor, border) = DefaultColors[type];
            cfg.BackgroundColor ??= bg;
            cfg.TextColor ??= textColor;
            cfg.BorderColor ??= border;
            if (cfg.BorderThickness == 0)
                cfg.BorderThickness = 1;

            configure?.Invoke(cfg);
        });
    }

    public Task<IDisposable> ShowAsync(string text, Action<ToastConfig>? configure = null)
    {
        var config = new ToastConfig { Text = text };
        configure?.Invoke(config);
        return Enqueue(config).ShownTcs.Task;
    }

    public async Task<SnackbarResult> SnackbarAsync(
        string text,
        string? actionText = null,
        Action<SnackbarConfig>? configure = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entry = Enqueue(CreateSnackbar(text, actionText, configure));
        using var registration = cancellationToken.Register(() => Dismiss(entry, SnackbarResult.Dismissed));
        return await entry.ClosedTcs.Task.ConfigureAwait(false);
    }

    public async Task<ILoadingSnackbar> LoadingSnackbarAsync(string text, Action<SnackbarConfig>? configure = null)
    {
        var entry = Enqueue(CreateSnackbar(text, null, cfg =>
        {
            cfg.IsLoading = true;
            cfg.Duration = TimeSpan.Zero;
            cfg.DismissOnTap = false;
            configure?.Invoke(cfg);
        }));
        await entry.ShownTcs.Task.ConfigureAwait(false);
        return new LoadingSnackbar(this, entry);
    }

    static SnackbarConfig CreateSnackbar(string text, string? actionText, Action<SnackbarConfig>? configure)
    {
        // The action is set before the caller's configure so the longer default duration is in
        // place for it to override.
        var config = new SnackbarConfig { Text = text, ActionText = actionText };
        if (actionText is not null)
            config.Duration = SnackbarConfig.DefaultActionDuration;

        configure?.Invoke(config);
        return config;
    }

    ToastEntry Enqueue(ToastConfig config)
    {
        var entry = new ToastEntry(config, Guid.NewGuid().ToString("N"));

        switch (config.QueueMode)
        {
            case ToastQueueMode.Stack:
                _ = ShowStackedAsync(entry);
                return entry;

            case ToastQueueMode.Replace:
                lock (gate)
                    queue.AddFirst(entry);
                if (currentQueued is { } showing)
                    Dismiss(showing, SnackbarResult.Replaced);
                break;

            default:
                lock (gate)
                    queue.AddLast(entry);
                break;
        }

        _ = ProcessQueueAsync();
        return entry;
    }

    async Task ProcessQueueAsync()
    {
        if (isProcessingQueue)
            return;

        isProcessingQueue = true;
        try
        {
            while (TryDequeue(out var entry))
            {
                currentQueued = entry;
                await ShowEntryAsync(entry);
                currentQueued = null;
            }
        }
        finally
        {
            isProcessingQueue = false;
        }
    }

    bool TryDequeue(out ToastEntry entry)
    {
        lock (gate)
        {
            if (queue.First is { } next)
            {
                queue.RemoveFirst();
                entry = next.Value;
                return true;
            }
        }

        entry = null!;
        return false;
    }

    async Task ShowStackedAsync(ToastEntry entry)
    {
        while (activeToasts.Count >= MaxStackCount)
        {
            Dismiss(activeToasts[0], SnackbarResult.Replaced);
            await Task.Delay(50);
        }

        await ShowEntryAsync(entry);
    }

    async Task ShowEntryAsync(ToastEntry entry)
    {
        activeToasts.Add(entry);
        OnChanged?.Invoke();

        entry.ShownTcs.TrySetResult(new ToastHandle(() => Dismiss(entry, SnackbarResult.Dismissed)));

        var effectiveDuration = GetEffectiveDuration(entry.Config);
        if (effectiveDuration > TimeSpan.Zero)
            ArmCountdown(entry, effectiveDuration);

        var reason = await entry.DismissedTcs.Task;
        activeToasts.Remove(entry);
        OnChanged?.Invoke();
        entry.ClosedTcs.TrySetResult(reason);
    }

    static TimeSpan GetEffectiveDuration(ToastConfig config)
    {
        if (config.TextOverflow == ToastTextOverflow.Marquee && config.MarqueeLoops > 0)
        {
            var onePassSeconds = GetOnePassDuration(config);
            return TimeSpan.FromSeconds(onePassSeconds * config.MarqueeLoops);
        }
        return config.Duration;
    }

    public static double GetOnePassDuration(ToastConfig config)
    {
        var estimatedWidth = Math.Max(config.Text.Length * 8, 200);
        var speed = config.MarqueeSpeedPixelsPerSecond > 0 ? config.MarqueeSpeedPixelsPerSecond : 40;
        return estimatedWidth * 2 / speed;
    }

    void ArmCountdown(ToastEntry entry, TimeSpan duration)
    {
        lock (gate)
        {
            entry.TimerCts?.Cancel();
            entry.Remaining = duration;
            if (entry.IsPaused)
                return;

            var cts = entry.TimerCts = new CancellationTokenSource();
            entry.ArmedAt = DateTime.UtcNow;
            _ = Task.Delay(duration, cts.Token).ContinueWith(
                t => { if (!t.IsCanceled) Dismiss(entry, SnackbarResult.TimedOut); },
                TaskScheduler.Default);
        }
    }

    /// <summary>Holds the countdown while the pointer or focus is on the toast. Only toasts with <see cref="ToastConfig.PauseOnHover"/>.</summary>
    public void Pause(ToastEntry entry)
    {
        if (!entry.Config.PauseOnHover || entry.IsPaused || entry.IsDismissing)
            return;

        lock (gate)
        {
            entry.IsPaused = true;
            if (entry.TimerCts is { } cts)
            {
                cts.Cancel();
                entry.TimerCts = null;
                var left = entry.Remaining - (DateTime.UtcNow - entry.ArmedAt);
                entry.Remaining = left < TimeSpan.Zero ? TimeSpan.Zero : left;
            }
        }
        OnChanged?.Invoke();
    }

    public void Resume(ToastEntry entry)
    {
        if (!entry.IsPaused)
            return;

        entry.IsPaused = false;
        if (!entry.IsDismissing && entry.Remaining > TimeSpan.Zero)
            ArmCountdown(entry, entry.Remaining);
        OnChanged?.Invoke();
    }

    /// <summary>Closes <paramref name="entry"/> wherever it is: on screen it animates out, still queued it just leaves.</summary>
    internal void Dismiss(ToastEntry entry, SnackbarResult reason)
    {
        lock (gate)
        {
            if (queue.Remove(entry))
            {
                entry.ShownTcs.TrySetResult(new ToastHandle(static () => { }));
                entry.ClosedTcs.TrySetResult(reason);
                return;
            }

            if (entry.IsDismissing)
                return;
            entry.IsDismissing = true;
            entry.TimerCts?.Cancel();
        }
        OnChanged?.Invoke();

        // Delay removal to allow CSS animation
        _ = Task.Delay(250).ContinueWith(_ => entry.DismissedTcs.TrySetResult(reason), TaskScheduler.Default);
    }

    internal void Refresh() => OnChanged?.Invoke();

    public void HandleTap(ToastEntry entry)
    {
        if (entry.IsDismissing)
            return;

        entry.Config.TapCallback?.Invoke();
        if (entry.Config.DismissOnTap)
            Dismiss(entry, SnackbarResult.Dismissed);
    }

    public void HandleAction(ToastEntry entry)
    {
        if (entry.IsDismissing)
            return;

        (entry.Config as SnackbarConfig)?.ActionCallback?.Invoke();
        Dismiss(entry, SnackbarResult.ActionInvoked);
    }

    sealed class ToastHandle : IDisposable
    {
        Action? dismissAction;
        public ToastHandle(Action action) => dismissAction = action;
        public void Dispose()
        {
            var action = Interlocked.Exchange(ref dismissAction, null);
            action?.Invoke();
        }
    }
}

sealed class LoadingSnackbar(ToastService service, ToastEntry entry) : ILoadingSnackbar
{
    readonly SnackbarConfig config = (SnackbarConfig)entry.Config;

    public string Text
    {
        get => config.Text;
        set
        {
            config.Text = value;
            service.Refresh();
        }
    }

    public double? Progress
    {
        get => config.Progress;
        set
        {
            config.Progress = value is { } v ? Math.Clamp(v, 0, 1) : null;
            service.Refresh();
        }
    }

    public Task<SnackbarResult> Closed => entry.ClosedTcs.Task;

    public void Dispose() => service.Dismiss(entry, SnackbarResult.Dismissed);
}

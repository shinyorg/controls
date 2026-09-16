using System.Runtime.CompilerServices;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Toast;

sealed class ToastManager
{
    const int MaxStackCount = 5;
    // Weak on the window: a closed desktop window must not be kept alive by the manager table.
    static readonly ConditionalWeakTable<Window, ToastManager> Instances = new();

    readonly Queue<(ToastConfig Config, TaskCompletionSource<IDisposable> Tcs)> queue = new();
    readonly List<ToastView> activeToasts = new();
    bool isProcessingQueue;

    public static Task<IDisposable> ShowAsync(ToastConfig config)
    {
        var window = Application.Current?.Windows.FirstOrDefault()
            ?? throw new InvalidOperationException("No active window found. Toast requires an active MAUI window.");

        var manager = Instances.GetValue(window, static _ => new ToastManager());
        return manager.ShowToastAsync(config);
    }

    /// <summary>
    /// The toast layer on the page that is showing <em>now</em>. Resolved per toast rather than cached
    /// on the manager: the manager lives as long as the window, and an overlay captured once held the
    /// first page a toast was ever shown on - popped or not - alive for the life of the app (and kept
    /// every later toast drawing onto it).
    /// </summary>
    static AbsoluteLayout GetOrCreateOverlay()
    {
        var window = Application.Current?.Windows.FirstOrDefault()
            ?? throw new InvalidOperationException("No active window found. Toast requires an active MAUI window.");

        var page = window.Page
            ?? throw new InvalidOperationException("Window has no Page. Toast requires an active page.");

        var targetPage = GetLeafPage(page);
        return PageOverlay.GetOrCreateLayer<PageOverlay.ToastLayer>(PageOverlay.GetOrCreateRoot(targetPage), 9_999);
    }

    static void RemoveFromOverlay(ToastView toastView)
    {
        if (toastView.Parent is Layout layout)
            layout.Children.Remove(toastView);
    }

    static ContentPage GetLeafPage(Page page) => page switch
    {
        ContentPage cp => cp,
        NavigationPage np when np.CurrentPage is not null => GetLeafPage(np.CurrentPage),
        Shell shell when shell.CurrentPage is not null => GetLeafPage(shell.CurrentPage),
        TabbedPage tp when tp.CurrentPage is not null => GetLeafPage(tp.CurrentPage),
        FlyoutPage fp when fp.Detail is not null => GetLeafPage(fp.Detail),
        _ => throw new InvalidOperationException(
            $"Cannot find a ContentPage to host Toast. Current page type: {page.GetType().Name}")
    };

    Task<IDisposable> ShowToastAsync(ToastConfig config)
    {
        var tcs = new TaskCompletionSource<IDisposable>();

        if (config.QueueMode == ToastQueueMode.Stack)
        {
            _ = ShowStackedToastAsync(config, tcs);
        }
        else
        {
            queue.Enqueue((config, tcs));
            _ = ProcessQueueAsync();
        }

        return tcs.Task;
    }

    async Task ProcessQueueAsync()
    {
        if (isProcessingQueue)
            return;

        isProcessingQueue = true;
        try
        {
            while (queue.Count > 0)
            {
                var (config, tcs) = queue.Dequeue();
                await ShowSingleToastAsync(config, tcs);
            }
        }
        finally
        {
            isProcessingQueue = false;
        }
    }

    async Task ShowSingleToastAsync(ToastConfig config, TaskCompletionSource<IDisposable> tcs)
    {
        var dismissed = new TaskCompletionSource();
        var toastView = CreateToastView(config, dismissed);

        AddToOverlay(toastView, config);
        activeToasts.Add(toastView);

        var handle = new ToastHandle(() =>
        {
            if (toastView.IsVisible)
                toastView.Dispatcher.Dispatch(() => _ = toastView.AnimateOutAsync());
        });

        tcs.SetResult(handle);
        await toastView.AnimateInAsync();

        // Wait for this toast to be dismissed before showing next in queue
        await dismissed.Task;
        activeToasts.Remove(toastView);
        RemoveFromOverlay(toastView);
    }

    async Task ShowStackedToastAsync(ToastConfig config, TaskCompletionSource<IDisposable> tcs)
    {
        // Evict oldest if at max
        while (activeToasts.Count >= MaxStackCount)
        {
            var oldest = activeToasts[0];
            await oldest.AnimateOutAsync();
        }

        var dismissed = new TaskCompletionSource();
        var toastView = CreateToastView(config, dismissed);

        AddToOverlay(toastView, config);
        activeToasts.Add(toastView);

        var handle = new ToastHandle(() =>
        {
            if (toastView.IsVisible)
                toastView.Dispatcher.Dispatch(() => _ = toastView.AnimateOutAsync());
        });

        tcs.SetResult(handle);
        await toastView.AnimateInAsync();

        // Wait for dismiss, then reflow
        await dismissed.Task;
        activeToasts.Remove(toastView);
        RemoveFromOverlay(toastView);
        ReflowStackedToasts(config.Position);
    }

    ToastView CreateToastView(ToastConfig config, TaskCompletionSource dismissed)
    {
        var toastView = new ToastView(config);
        toastView.SetOnDismissed(() => dismissed.TrySetResult());
        return toastView;
    }

    void AddToOverlay(ToastView toastView, ToastConfig config)
    {
        var isFill = config.DisplayMode == ToastDisplayMode.FillHorizontal;
        var offset = config.Offset;

        if (isFill)
        {
            AbsoluteLayout.SetLayoutBounds(toastView, config.Position == ToastPosition.Bottom
                ? new Rect(0, 1, 1, -1)
                : new Rect(0, 0, 1, -1));
            AbsoluteLayout.SetLayoutFlags(toastView,
                Microsoft.Maui.Layouts.AbsoluteLayoutFlags.WidthProportional | Microsoft.Maui.Layouts.AbsoluteLayoutFlags.YProportional);

            // Safe area padding for fill mode
            var safeBottom = GetBottomSafeAreaInset();
            var safeTop = GetTopSafeAreaInset();
            if (config.Position == ToastPosition.Bottom && safeBottom > 0)
                toastView.Margin = new Thickness(0, 0, 0, safeBottom);
            else if (config.Position == ToastPosition.Top && safeTop > 0)
                toastView.Margin = new Thickness(0, safeTop, 0, 0);
        }
        else
        {
            // Pill mode: centered horizontally with offset from edges
            var safeBottom = GetBottomSafeAreaInset();
            var safeTop = GetTopSafeAreaInset();

            var bottomMargin = config.Position == ToastPosition.Bottom
                ? offset.Bottom + safeBottom
                : offset.Bottom;
            var topMargin = config.Position == ToastPosition.Top
                ? offset.Top + safeTop
                : offset.Top;

            AbsoluteLayout.SetLayoutBounds(toastView, config.Position == ToastPosition.Bottom
                ? new Rect(0.5, 1, -1, -1)
                : new Rect(0.5, 0, -1, -1));
            AbsoluteLayout.SetLayoutFlags(toastView,
                Microsoft.Maui.Layouts.AbsoluteLayoutFlags.XProportional | Microsoft.Maui.Layouts.AbsoluteLayoutFlags.YProportional);

            toastView.Margin = new Thickness(offset.Left, topMargin, offset.Right, bottomMargin);
        }

        // Stack offset for multiple visible toasts
        if (config.QueueMode == ToastQueueMode.Stack && activeToasts.Count > 0)
        {
            var stackOffset = 0d;
            foreach (var existing in activeToasts)
            {
                stackOffset += (existing.Height > 0 ? existing.Height : 50) + 8;
            }

            var currentMargin = toastView.Margin;
            toastView.Margin = config.Position == ToastPosition.Bottom
                ? new Thickness(currentMargin.Left, currentMargin.Top, currentMargin.Right, currentMargin.Bottom + stackOffset)
                : new Thickness(currentMargin.Left, currentMargin.Top + stackOffset, currentMargin.Right, currentMargin.Bottom);
        }

        GetOrCreateOverlay().Children.Add(toastView);
    }

    void ReflowStackedToasts(ToastPosition position)
    {
        var offset = 0d;
        foreach (var toast in activeToasts)
        {
            var current = toast.Margin;
            var safeBottom = GetBottomSafeAreaInset();
            var safeTop = GetTopSafeAreaInset();
            var baseOffset = 12d; // default offset

            if (position == ToastPosition.Bottom)
            {
                toast.Margin = new Thickness(current.Left, current.Top, current.Right, baseOffset + safeBottom + offset);
            }
            else
            {
                toast.Margin = new Thickness(current.Left, baseOffset + safeTop + offset, current.Right, current.Bottom);
            }

            offset += (toast.Height > 0 ? toast.Height : 50) + 8;
        }
    }

    static double GetBottomSafeAreaInset()
    {
#if IOS || MACCATALYST
        var window = UIKit.UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIKit.UIWindowScene>()
            .SelectMany(s => s.Windows)
            .FirstOrDefault();
        return window?.SafeAreaInsets.Bottom ?? 0;
#else
        return 0;
#endif
    }

    static double GetTopSafeAreaInset()
    {
#if IOS || MACCATALYST
        var window = UIKit.UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIKit.UIWindowScene>()
            .SelectMany(s => s.Windows)
            .FirstOrDefault();
        return window?.SafeAreaInsets.Top ?? 0;
#else
        return 0;
#endif
    }

    sealed class ToastHandle : IDisposable
    {
        Action? dismissAction;

        public ToastHandle(Action dismissAction)
        {
            this.dismissAction = dismissAction;
        }

        public void Dispose()
        {
            var action = Interlocked.Exchange(ref dismissAction, null);
            action?.Invoke();
        }
    }
}

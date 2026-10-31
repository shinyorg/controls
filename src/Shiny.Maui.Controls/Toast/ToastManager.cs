using System.Runtime.CompilerServices;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Toast;

sealed class ToastManager
{
    const int MaxStackCount = 5;
    // Weak on the window: a closed desktop window must not be kept alive by the manager table.
    static readonly ConditionalWeakTable<Window, ToastManager> Instances = new();

    // A linked list rather than a queue: a Replace toast jumps to the front, and a cancelled one
    // leaves from the middle.
    readonly LinkedList<ToastRequest> queue = new();
    readonly List<ToastView> activeToasts = new();
    ToastRequest? currentQueued;
    bool isProcessingQueue;
    IDispatcher? dispatcher;

    public static Task<IDisposable> ShowAsync(ToastConfig config)
        => Show(config).Shown.Task;

    /// <summary>Queues <paramref name="config"/> and returns its request, which says when it shows and why it closed.</summary>
    public static ToastRequest Show(ToastConfig config)
    {
        var window = Application.Current?.Windows.FirstOrDefault()
            ?? throw new InvalidOperationException("No active window found. Toast requires an active MAUI window.");

        var manager = Instances.GetValue(window, static _ => new ToastManager());
        manager.dispatcher ??= window.Dispatcher;

        var request = new ToastRequest(config, manager);
        manager.Enqueue(request);
        return request;
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

    void Enqueue(ToastRequest request)
    {
        switch (request.Config.QueueMode)
        {
            case ToastQueueMode.Stack:
                _ = ShowStackedToastAsync(request);
                return;

            case ToastQueueMode.Replace:
                queue.AddFirst(request);
                if (currentQueued?.View is { } showing)
                    showing.Dispatcher.Dispatch(() => _ = showing.AnimateOutAsync(SnackbarResult.Replaced));
                break;

            default:
                queue.AddLast(request);
                break;
        }

        _ = ProcessQueueAsync();
    }

    /// <summary>Closes <paramref name="request"/> wherever it is: on screen it animates out, still queued it just leaves.</summary>
    internal void Dismiss(ToastRequest request, SnackbarResult reason)
    {
        void Run()
        {
            if (request.View is { } view)
            {
                _ = view.AnimateOutAsync(reason);
            }
            else if (queue.Remove(request))
            {
                request.Shown.TrySetResult(new ToastHandle(static () => { }));
                request.Closed.TrySetResult(reason);
            }
        }

        // A cancellation token can fire on any thread; the queue is only touched on the UI thread.
        if (dispatcher is { IsDispatchRequired: true } d)
            d.Dispatch(Run);
        else
            Run();
    }

    async Task ProcessQueueAsync()
    {
        if (isProcessingQueue)
            return;

        isProcessingQueue = true;
        try
        {
            while (queue.First is { } next)
            {
                queue.RemoveFirst();
                await ShowSingleToastAsync(next.Value);
            }
        }
        finally
        {
            isProcessingQueue = false;
        }
    }

    async Task ShowSingleToastAsync(ToastRequest request)
    {
        var dismissed = new TaskCompletionSource<SnackbarResult>();
        var toastView = CreateToastView(request, dismissed);

        currentQueued = request;
        AddToOverlay(toastView, request.Config);
        activeToasts.Add(toastView);

        request.Shown.TrySetResult(CreateHandle(request));
        await toastView.AnimateInAsync();

        // Wait for this toast to be dismissed before showing next in queue
        var reason = await dismissed.Task;
        activeToasts.Remove(toastView);
        RemoveFromOverlay(toastView);
        currentQueued = null;
        request.Closed.TrySetResult(reason);
    }

    async Task ShowStackedToastAsync(ToastRequest request)
    {
        // Evict oldest if at max
        while (activeToasts.Count >= MaxStackCount)
        {
            var oldest = activeToasts[0];
            await oldest.AnimateOutAsync(SnackbarResult.Replaced);
        }

        var dismissed = new TaskCompletionSource<SnackbarResult>();
        var toastView = CreateToastView(request, dismissed);

        AddToOverlay(toastView, request.Config);
        activeToasts.Add(toastView);

        request.Shown.TrySetResult(CreateHandle(request));
        await toastView.AnimateInAsync();

        // Wait for dismiss, then reflow
        var reason = await dismissed.Task;
        activeToasts.Remove(toastView);
        RemoveFromOverlay(toastView);
        ReflowStackedToasts(request.Config.Position);
        request.Closed.TrySetResult(reason);
    }

    IDisposable CreateHandle(ToastRequest request)
        => new ToastHandle(() => Dismiss(request, SnackbarResult.Dismissed));

    static ToastView CreateToastView(ToastRequest request, TaskCompletionSource<SnackbarResult> dismissed)
    {
        var toastView = new ToastView(request.Config);
        toastView.SetOnDismissed(reason => dismissed.TrySetResult(reason));
        request.View = toastView;
        return toastView;
    }

    void AddToOverlay(ToastView toastView, ToastConfig config)
    {
        // A snackbar takes the full width like a fill bar, then its own maximum width centres it.
        var isFill = config.DisplayMode == ToastDisplayMode.FillHorizontal;
        var isSnackbar = config.DisplayMode == ToastDisplayMode.Snackbar;
        var offset = config.Offset;

        if (isFill || isSnackbar)
        {
            AbsoluteLayout.SetLayoutBounds(toastView, config.Position == ToastPosition.Bottom
                ? new Rect(0, 1, 1, -1)
                : new Rect(0, 0, 1, -1));
            AbsoluteLayout.SetLayoutFlags(toastView,
                Microsoft.Maui.Layouts.AbsoluteLayoutFlags.WidthProportional | Microsoft.Maui.Layouts.AbsoluteLayoutFlags.YProportional);

            // No safe-area margin of its own: the toast layer keeps a layout's default safe-area inset,
            // which already holds it clear of the status bar and home indicator.
            if (isSnackbar)
                toastView.Margin = offset;
        }
        else
        {
            // Pill mode: centered horizontally with offset from edges (the layer supplies the safe area)
            var bottomMargin = offset.Bottom;
            var topMargin = offset.Top;

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

        // A fold moves the toast into one segment: the larger half of a book, and the half matching
        // its position on a tabletop (bottom toasts below the fold, top toasts above it). Zero - and
        // so no change at all - unless a posture source is registered and a fold crosses the page.
        var layer = GetOrCreateOverlay();
        var foldInset = FoldInset(layer, config.Position);
        this.foldInsets.AddOrUpdate(toastView, new StrongBox<Thickness>(foldInset));
        toastView.Margin = Add(toastView.Margin, foldInset);

        layer.Children.Add(toastView);
    }

    readonly ConditionalWeakTable<ToastView, StrongBox<Thickness>> foldInsets = new();

    static Thickness FoldInset(VisualElement layer, ToastPosition position)
    {
        // A layer created for this very toast has not been laid out yet; it fills its overlay root,
        // which fills the page, so the nearest ancestor that has a size stands in for it.
        Element? sized = layer;
        while (sized is VisualElement { Width: <= 0 } && sized.Parent is not null)
            sized = sized.Parent;
        if (sized is VisualElement measured)
            layer = measured;

        if (layer.FoldIn() is not { } fold)
            return default;

        var preference = position == ToastPosition.Bottom ? FoldPreference.Bottom : FoldPreference.Top;
        var rtl = ((IView)layer).FlowDirection == FlowDirection.RightToLeft;
        return FoldLayout.SegmentMargin(new Size(layer.Width, layer.Height), fold, preference, rtl);
    }

    static Thickness Add(Thickness a, Thickness b)
        => new(a.Left + b.Left, a.Top + b.Top, a.Right + b.Right, a.Bottom + b.Bottom);

    void ReflowStackedToasts(ToastPosition position)
    {
        var offset = 0d;
        foreach (var toast in activeToasts)
        {
            var current = toast.Margin;
            var baseOffset = 12d; // default offset

            var fold = this.foldInsets.TryGetValue(toast, out var box) ? box.Value : default;

            if (position == ToastPosition.Bottom)
            {
                toast.Margin = new Thickness(current.Left, current.Top, current.Right, baseOffset + offset + fold.Bottom);
            }
            else
            {
                toast.Margin = new Thickness(current.Left, baseOffset + offset + fold.Top, current.Right, current.Bottom);
            }

            offset += (toast.Height > 0 ? toast.Height : 50) + 8;
        }
    }

    internal sealed class ToastHandle : IDisposable
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

/// <summary>One toast's trip through the manager: when it showed, which view drew it, and why it closed.</summary>
sealed class ToastRequest(ToastConfig config, ToastManager owner)
{
    public ToastConfig Config { get; } = config;
    public TaskCompletionSource<IDisposable> Shown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<SnackbarResult> Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ToastView? View { get; set; }

    public void Dismiss(SnackbarResult reason) => owner.Dismiss(this, reason);
}

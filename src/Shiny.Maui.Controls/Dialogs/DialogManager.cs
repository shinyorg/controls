using System.Runtime.CompilerServices;
using Microsoft.Maui.Layouts;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Dialogs;

sealed class DialogManager
{
    // Weak on the window: a closed desktop window must not be kept alive by the manager table.
    static readonly ConditionalWeakTable<Window, DialogManager> Instances = new();

    readonly Queue<(DialogConfig Config, DialogOptions Options, TaskCompletionSource<DialogOutcome> Tcs)> queue = new();
    bool isProcessingQueue;

    public static Task<DialogOutcome> ShowAsync(DialogConfig config, DialogOptions options)
    {
        var window = Application.Current?.Windows.FirstOrDefault()
            ?? throw new InvalidOperationException("No active window found. Dialogs require an active MAUI window.");

        var manager = Instances.GetValue(window, static _ => new DialogManager());
        return manager.EnqueueAsync(config, options);
    }

    /// <summary>
    /// Resolves the overlay for the page that is current *right now*, attaching one on first
    /// use for that page. Resolved per show (not cached on the manager) so that dialogs raised
    /// after navigation land on the visible page instead of the one that happened to be
    /// current when the first dialog was shown.
    /// </summary>
    static AbsoluteLayout GetOrCreateOverlay()
    {
        var window = Application.Current?.Windows.FirstOrDefault()
            ?? throw new InvalidOperationException("No active window found. Dialogs require an active MAUI window.");

        var page = window.Page
            ?? throw new InvalidOperationException("Window has no Page. Dialogs require an active page.");

        var targetPage = GetLeafPage(page);
        return PageOverlay.GetOrCreateLayer<PageOverlay.DialogLayer>(PageOverlay.GetOrCreateRoot(targetPage), PageOverlay.Layers.Dialog);
    }

    static ContentPage GetLeafPage(Page page) => page switch
    {
        ContentPage cp => cp,
        NavigationPage np when np.CurrentPage is not null => GetLeafPage(np.CurrentPage),
        Shell shell when shell.CurrentPage is not null => GetLeafPage(shell.CurrentPage),
        TabbedPage tp when tp.CurrentPage is not null => GetLeafPage(tp.CurrentPage),
        FlyoutPage fp when fp.Detail is not null => GetLeafPage(fp.Detail),
        _ => throw new InvalidOperationException(
            $"Cannot find a ContentPage to host a Dialog. Current page type: {page.GetType().Name}")
    };

    Task<DialogOutcome> EnqueueAsync(DialogConfig config, DialogOptions options)
    {
        var tcs = new TaskCompletionSource<DialogOutcome>();
        this.queue.Enqueue((config, options, tcs));
        _ = this.ProcessQueueAsync();
        return tcs.Task;
    }

    async Task ProcessQueueAsync()
    {
        if (this.isProcessingQueue)
            return;

        this.isProcessingQueue = true;
        try
        {
            while (this.queue.Count > 0)
            {
                var (config, options, tcs) = this.queue.Dequeue();
                try
                {
                    await this.ShowSingleAsync(config, options, tcs);
                }
                catch (Exception ex)
                {
                    // Nobody awaits this loop, so an exception left to escape is lost and the caller
                    // waits forever for a dialog that will never come. Hand it to that caller instead,
                    // and keep going so the dialogs queued behind it still get their turn.
                    tcs.TrySetException(ex);
                }
            }
        }
        finally
        {
            this.isProcessingQueue = false;
        }
    }

    async Task ShowSingleAsync(DialogConfig config, DialogOptions options, TaskCompletionSource<DialogOutcome> tcs)
    {
        var overlay = GetOrCreateOverlay();

        var dismissed = new TaskCompletionSource();
        var view = new DialogView(config, options);
        view.SetOnDismissed(() => dismissed.TrySetResult());

        AbsoluteLayout.SetLayoutBounds(view, new Rect(0, 0, 1, 1));
        AbsoluteLayout.SetLayoutFlags(view, AbsoluteLayoutFlags.All);
        overlay.Children.Add(view);

        try
        {
            await view.AnimateInAsync();
            view.FocusInput();
            await dismissed.Task;
        }
        finally
        {
            overlay.Children.Remove(view);
        }
        tcs.TrySetResult(view.Outcome);
    }
}

using Microsoft.Maui.Dispatching;

namespace Shiny.Maui.Controls;

/// <summary>
/// Gets app-wide shortcuts a key source in every window.
/// </summary>
/// <remarks>
/// A shortcut registered through <see cref="IKeyboardShortcutService"/> during startup — before the
/// <see cref="Application"/> exists — has no window to attach to yet. There is no public "a window
/// was added" event, and the <c>WindowHandler.Mapper</c> hook that would stand in for one never runs
/// on the AppKit and GTK4 heads, so this does what works everywhere: poll briefly until the
/// application exists, then let <see cref="Application.PageAppearing"/> catch every window that
/// opens after it. Only does work when something is registered app-wide; XAML-declared shortcuts
/// attach their own window when their page appears.
/// </remarks>
sealed class KeyboardShortcutInitializer : IMauiInitializeService
{
    static readonly TimeSpan pollInterval = TimeSpan.FromMilliseconds(250);
    static readonly TimeSpan timeout = TimeSpan.FromSeconds(30);

    public void Initialize(IServiceProvider services)
        => _ = WatchAsync(Dispatcher.GetForCurrentThread());

    static async Task WatchAsync(IDispatcher? dispatcher)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var ready = false;
            void Check()
            {
                if (Application.Current == null)
                    return;

                if (KeyboardShortcutManager.Engine.GlobalScope.Bindings.Count > 0)
                    KeyboardShortcutManager.EnsureOpenWindows();

                ready = Application.Current.Windows.Count > 0;
            }

            if (dispatcher is { IsDispatchRequired: true })
                await dispatcher.DispatchAsync(Check).ConfigureAwait(false);
            else
                Check();

            if (ready)
                return;

            await Task.Delay(pollInterval).ConfigureAwait(false);
        }
    }
}

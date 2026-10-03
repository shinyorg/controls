using System.Runtime.CompilerServices;
using Shiny.Controls.Keyboard;

namespace Shiny.Maui.Controls;

/// <summary>
/// The one shortcut engine for the app, plus the per-window native key sources that feed it.
/// </summary>
/// <remarks>
/// <para>
/// Static because shortcuts are declared in XAML, on elements that have no way to reach the DI
/// container — and because there is only one keyboard. <see cref="IKeyboardShortcutService"/> is a
/// facade over this.
/// </para>
/// <para>
/// A native key source is attached to a window lazily, the first time anything there needs one:
/// a <see cref="KeyboardShortcuts"/> scope activating on one of its pages, or an app-wide
/// registration. MAUI has no "window opened" event that also fires on the AppKit and GTK heads, so
/// waiting to be asked is the reliable option.
/// </para>
/// </remarks>
static class KeyboardShortcutManager
{
    static readonly ConditionalWeakTable<Window, Attachment> attachments = new();
    static readonly object gate = new();
    static bool warnedUnsupported;
    static bool watchingPages;

    public static KeyboardShortcutEngine Engine { get; } = new(DetectPlatform());

    /// <summary>
    /// Attaches the native key source for one window: the MAUI window and its handler's platform
    /// view in, a teardown out (or null when the window cannot be hooked). Core supplies Windows,
    /// Android and iOS; the Desktop add-on supplies AppKit, GTK4 and Mac Catalyst.
    /// </summary>
    public static Func<Window, object, IDisposable?>? PlatformAttach { get; set; } = DefaultAttach();

    public static bool IsSupported => PlatformAttach != null;

    public static KeyboardPlatform DetectPlatform()
    {
        if (OperatingSystem.IsAndroid())
            return KeyboardPlatform.Android;

        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() || OperatingSystem.IsIOS())
            return KeyboardPlatform.Apple;

        if (OperatingSystem.IsWindows())
            return KeyboardPlatform.Windows;

        if (OperatingSystem.IsLinux())
            return KeyboardPlatform.Linux;

        return KeyboardPlatform.Other;
    }

    static Func<Window, object, IDisposable?>? DefaultAttach()
    {
#if WINDOWS
        return WindowsKeySource.Attach;
#elif ANDROID
        return AndroidKeySource.Attach;
#elif IOS
        return GameControllerKeySource.Attach;
#else
        return null;
#endif
    }

    /// <summary>Feed one stroke from a native source. True means swallow the native event.</summary>
    public static bool Process(KeyStroke stroke, Window? window)
    {
        try
        {
            return Engine.Process(stroke, window);
        }
        catch (Exception ex)
        {
            // A shortcut handler that throws must not take the native key pipeline down with it —
            // on GTK an exception crossing back into the main loop ends the process.
            System.Diagnostics.Debug.WriteLine($"[Shiny.KeyboardShortcuts] A shortcut handler threw: {ex}");
            return true;
        }
    }

    /// <summary>Make sure <paramref name="window"/> has a native key source.</summary>
    public static void EnsureWindow(Window? window)
    {
        if (window == null)
            return;

        if (PlatformAttach == null)
        {
            if (!warnedUnsupported)
            {
                warnedUnsupported = true;
                System.Diagnostics.Debug.WriteLine(
                    "[Shiny.KeyboardShortcuts] No native key source on this platform. On macOS (AppKit), Linux (GTK4) and Mac Catalyst " +
                    "add Shiny.Maui.Controls.Desktop and call builder.UseDesktopKeyboardShortcuts().");
            }
            return;
        }

        lock (gate)
        {
            if (attachments.TryGetValue(window, out _))
                return;

            attachments.Add(window, new Attachment(window));
        }
    }

    /// <summary>Attach every open window, and keep attaching new ones as their first page appears.</summary>
    public static void EnsureOpenWindows()
    {
        var app = Application.Current;
        if (app == null)
            return;

        if (!watchingPages)
        {
            watchingPages = true;
            app.PageAppearing += (_, page) => EnsureWindow(page.Window);
        }

        foreach (var window in app.Windows)
            EnsureWindow(window);
    }

    /// <summary>The MAUI window whose handler owns <paramref name="platformWindow"/>.</summary>
    public static Window? FindWindow(object? platformWindow)
    {
        if (platformWindow == null || Application.Current is not { } app)
            return null;

        foreach (var window in app.Windows)
        {
            if (ReferenceEquals(window.Handler?.PlatformView, platformWindow))
                return window;
        }

        return null;
    }

    sealed class Attachment
    {
        IDisposable? native;

        public Attachment(Window window)
        {
            // Release held keys when focus leaves: their key-ups go to another window, or nowhere,
            // and a push-to-talk must not stay on. The native sources do this too where they can see
            // focus more precisely; doing it twice is harmless.
            window.Deactivated += OnDeactivated;
            window.Destroying += this.OnDestroying;

            if (window.Handler?.PlatformView != null)
                this.AttachNative(window);
            else
                window.HandlerChanged += this.OnHandlerChanged;
        }

        static void OnDeactivated(object? sender, EventArgs e)
        {
            if (sender is Window w)
                Engine.ReleaseAll(w);
        }

        void OnHandlerChanged(object? sender, EventArgs e)
        {
            if (sender is not Window w || w.Handler?.PlatformView == null)
                return;

            w.HandlerChanged -= this.OnHandlerChanged;
            this.AttachNative(w);
        }

        void AttachNative(Window w)
        {
            var attach = PlatformAttach;
            if (attach == null || w.Handler?.PlatformView is not { } platformView)
                return;

            try
            {
                this.native = attach(w, platformView);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Shiny.KeyboardShortcuts] Attaching the native key source failed: {ex}");
            }
        }

        void OnDestroying(object? sender, EventArgs e)
        {
            if (sender is Window w)
            {
                w.Deactivated -= OnDeactivated;
                w.Destroying -= this.OnDestroying;
                w.HandlerChanged -= this.OnHandlerChanged;
                Engine.ReleaseAll(w);

                lock (gate)
                    attachments.Remove(w);
            }

            this.native?.Dispose();
            this.native = null;
        }
    }
}

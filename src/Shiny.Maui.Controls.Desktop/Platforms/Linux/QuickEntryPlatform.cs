using Shiny.Maui.Controls.QuickEntry;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Shiny.Maui.Controls.Desktop.QuickEntry;

/// <summary>
/// GTK 4 backing for the quick entry popup.
/// </summary>
/// <remarks>
/// <para>
/// What is achievable here depends entirely on the session. Under <b>X11</b> the popup behaves the
/// same as on Windows and macOS: undecorated, placed exactly where asked, and raised above other
/// windows via <c>_NET_WM_STATE_ABOVE</c>.
/// </para>
/// <para>
/// Under <b>Wayland</b> a client is not allowed to position its own toplevel or raise itself, and
/// GTK 4 dropped <c>gtk_window_move</c> and <c>set_keep_above</c> to match. The popup is still
/// undecorated and transparent, but the compositor decides where it lands and it is an ordinary
/// window in the stack. Doing better needs the <c>gtk4-layer-shell</c> protocol, which not every
/// compositor implements and which is not a dependency this package takes.
/// </para>
/// </remarks>
static unsafe class QuickEntryPlatform
{
    sealed class Hooks
    {
        public IDesktopQuickEntryHost Host = null!;
        public IntPtr Window;
        public ulong ActiveHandler;
    }

    static readonly Dictionary<IntPtr, Hooks> Registry = new();
    static readonly object Gate = new();
    static IntPtr x11Display;

    public static bool IsSupported => OperatingSystem.IsLinux();

    /// <summary>True when the session is Wayland, where placement and stacking requests are ignored.</summary>
    public static bool IsWayland { get; } =
        !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) ||
        String.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase);

    public static void BeginInvokeOnMainThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || !dispatcher.IsDispatchRequired)
            action();
        else
            dispatcher.Dispatch(action);
    }

    public static void Initialize(IDesktopQuickEntryHost host, object platformWindow, QuickEntryOptions options)
    {
        // GTK is not thread-safe and the service reaches here after an await, so every entry point
        // re-marshals rather than trusting where its continuation resumed. BeginInvokeOnMainThread
        // runs inline when already on the UI thread, so this costs nothing in the normal case.
        BeginInvokeOnMainThread(() => InitializeCore(host, platformWindow, options));
    }

    static void InitializeCore(IDesktopQuickEntryHost host, object platformWindow, QuickEntryOptions options)
    {
        var window = GetNativeHandle(platformWindow);
        if (window == IntPtr.Zero)
            return;

        Gtk4Interop.WindowSetDecorated(window, 0);
        Gtk4Interop.WindowSetResizable(window, 0);
        Gtk4Interop.WidgetAddCssClass(window, "shiny-quick-entry");
        Gtk4Styling.EnsureCss(window);

        var hooks = new Hooks { Host = host, Window = window };
        lock (Gate)
            Registry[window] = hooks;

        var controller = Gtk4Interop.EventControllerKeyNew();
        delegate* unmanaged<IntPtr, uint, uint, uint, IntPtr, int> keyCallback = &OnKeyPressed;
        Gtk4Interop.SignalConnectData(controller, "key-pressed", (IntPtr)keyCallback, window, IntPtr.Zero, 0);
        Gtk4Interop.WidgetAddController(window, controller);

        delegate* unmanaged<IntPtr, IntPtr, IntPtr, void> activeCallback = &OnActiveChanged;
        hooks.ActiveHandler = Gtk4Interop.SignalConnectData(window, "notify::is-active", (IntPtr)activeCallback, window, IntPtr.Zero, 0);

        Gtk4Interop.WidgetSetVisible(window, 0);
    }

    public static void Show(object platformWindow, QuickEntryOptions options, double width, double height)
        => BeginInvokeOnMainThread(() => ShowCore(platformWindow, options, width, height));

    static void ShowCore(object platformWindow, QuickEntryOptions options, double width, double height)
    {
        var window = GetNativeHandle(platformWindow);
        if (window == IntPtr.Zero)
            return;

        Gtk4Interop.WindowSetDefaultSize(window, (int)Math.Round(width), (int)Math.Round(height));
        Gtk4Interop.WidgetSetVisible(window, 1);
        Gtk4Interop.WindowPresent(window);
        ApplyX11Geometry(window, options, width, height);
    }

    public static void Hide(object platformWindow)
        => BeginInvokeOnMainThread(() =>
        {
            var window = GetNativeHandle(platformWindow);
            if (window != IntPtr.Zero)
                Gtk4Interop.WidgetSetVisible(window, 0);
        });

    public static void Resize(object platformWindow, QuickEntryOptions options, double width, double height)
        => BeginInvokeOnMainThread(() => ResizeCore(platformWindow, options, width, height));

    static void ResizeCore(object platformWindow, QuickEntryOptions options, double width, double height)
    {
        var window = GetNativeHandle(platformWindow);
        if (window == IntPtr.Zero)
            return;

        Gtk4Interop.WindowSetDefaultSize(window, (int)Math.Round(width), (int)Math.Round(height));
        ApplyX11Geometry(window, options, width, height);
    }

    public static void Teardown(object platformWindow)
    {
        var window = GetNativeHandle(platformWindow);
        if (window == IntPtr.Zero)
            return;

        lock (Gate)
        {
            if (Registry.Remove(window, out var hooks) && hooks.ActiveHandler != 0)
                Gtk4Interop.SignalHandlerDisconnect(window, hooks.ActiveHandler);
        }
    }

    /// <summary>The prompt's flat look comes from the stylesheet installed in <see cref="EnsureCss"/>, so there is nothing per-entry to do here.</summary>
    /// <summary>
    /// Takes the frame and fill off a <c>BorderlessEntry</c>'s GTK entry - by a CSS class rather than
    /// <c>gtk_entry_set_has_frame</c>, because the theme's background survives the frame going.
    /// </summary>
    public static void PolishEntry(object? platformView)
    {
        var widget = GetNativeHandle(platformView);
        if (widget == IntPtr.Zero)
            return;

        Gtk4Interop.WidgetAddCssClass(widget, Gtk4Styling.BorderlessEntryClass);
        Gtk4Styling.EnsureCss(widget);
    }

    // -------------------------------------------------------------------------------------

    [UnmanagedCallersOnly]
    static int OnKeyPressed(IntPtr controller, uint keyval, uint keycode, uint state, IntPtr userData)
    {
        try
        {
            Hooks? hooks;
            lock (Gate)
                Registry.TryGetValue(userData, out hooks);

            if (hooks == null)
                return 0;

            var key = keyval switch
            {
                Gtk4Interop.KeyEscape => (QuickEntryKey?)QuickEntryKey.Escape,
                Gtk4Interop.KeyReturn or Gtk4Interop.KeyKpEnter => QuickEntryKey.Enter,
                Gtk4Interop.KeyUp => QuickEntryKey.ArrowUp,
                Gtk4Interop.KeyDown => QuickEntryKey.ArrowDown,
                Gtk4Interop.KeyTab => QuickEntryKey.Tab,
                _ => null
            };

            if (key == null)
                return 0;

            // Enter is reported but never claimed — the prompt's own Completed handler commits it.
            var handled = hooks.Host.NotifyKey(key.Value);
            return handled && key != QuickEntryKey.Enter ? 1 : 0;
        }
        catch
        {
            // A managed exception crossing back into the GTK main loop terminates the process.
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    static void OnActiveChanged(IntPtr window, IntPtr pspec, IntPtr userData)
    {
        try
        {
            Hooks? hooks;
            lock (Gate)
                Registry.TryGetValue(userData, out hooks);

            if (hooks != null && Gtk4Interop.WindowIsActive(userData) == 0)
                hooks.Host.NotifyDeactivated();
        }
        catch
        {
        }
    }

    // -------------------------------------------------------------------------------------

    static void ApplyX11Geometry(IntPtr window, QuickEntryOptions options, double width, double height)
    {
        if (IsWayland)
            return;

        var surface = Gtk4Interop.NativeGetSurface(window);
        if (surface == IntPtr.Zero)
            return;

        ulong xid;
        try
        {
            xid = Gtk4Interop.X11SurfaceGetXid(surface);
        }
        catch (EntryPointNotFoundException)
        {
            return;
        }

        if (xid == 0)
            return;

        var display = EnsureX11Display();
        if (display == IntPtr.Zero)
            return;

        var screenWidth = X11Interop.DisplayWidth(display, X11Interop.DefaultScreen(display));
        var screenHeight = X11Interop.DisplayHeight(display, X11Interop.DefaultScreen(display));
        var w = (int)Math.Round(width);
        var h = (int)Math.Round(height);

        var (x, y) = options.Placement switch
        {
            QuickEntryPlacement.BottomCenter => ((screenWidth - w) / 2, screenHeight - h - (int)(screenHeight * options.BottomMarginRatio)),
            QuickEntryPlacement.Center => ((screenWidth - w) / 2, (screenHeight - h) / 2),
            QuickEntryPlacement.Manual => ((int)Math.Round(options.X), (int)Math.Round(options.Y)),
            QuickEntryPlacement.NearCursor => ((screenWidth - w) / 2, (int)(screenHeight * options.TopMarginRatio)),
            _ => ((screenWidth - w) / 2, (int)(screenHeight * options.TopMarginRatio))
        };

        x = Math.Clamp(x, 0, Math.Max(0, screenWidth - w));
        y = Math.Clamp(y, 0, Math.Max(0, screenHeight - h));

        X11Interop.MoveResizeWindow(display, (IntPtr)xid, x, y, (uint)w, (uint)h);
        X11Interop.SetAlwaysOnTop(display, xid);
        X11Interop.Flush(display);
    }

    internal static IntPtr EnsureX11Display()
    {
        if (x11Display != IntPtr.Zero)
            return x11Display;

        try
        {
            x11Display = X11Interop.OpenDisplay(null);
        }
        catch (DllNotFoundException)
        {
            x11Display = IntPtr.Zero;
        }
        return x11Display;
    }

    /// <summary>
    /// GirCore wraps every GObject in a <see cref="SafeHandle"/> exposed as <c>Handle</c>. Reading it
    /// reflectively keeps this package off a GirCore package reference — which would otherwise be
    /// dragged into the plain <c>net11.0</c> build for every consumer, Linux or not. Failure just
    /// means no native styling, never a crash.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "The GTK4 MAUI head is a plain net11.0 app that is not trimmed, and a missing Handle property degrades to an undecorated-only popup rather than failing.")]
    internal static IntPtr GetNativeHandleForGlow(object? platformWindow) => GetNativeHandle(platformWindow);

    // The GTK window type belongs to the app's GTK4 head, which this package does not reference, so
    // Handle can only be found by reflection. The head reads the same property itself, so it survives.
    [UnconditionalSuppressMessage("Trimming", "IL2075",
        Justification = "Handle is public API of the GTK4 head's window and is used by the head itself.")]
    static IntPtr GetNativeHandle(object? platformWindow)
    {
        if (platformWindow == null)
            return IntPtr.Zero;

        try
        {
            var property = platformWindow.GetType().GetProperty("Handle", BindingFlags.Public | BindingFlags.Instance);
            return property?.GetValue(platformWindow) switch
            {
                SafeHandle safe => safe.DangerousGetHandle(),
                IntPtr raw => raw,
                _ => IntPtr.Zero
            };
        }
        catch
        {
            return IntPtr.Zero;
        }
    }
}

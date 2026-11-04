using System.Runtime.InteropServices;
using Shiny.Maui.Controls.Desktop.QuickEntry;

namespace Shiny.Maui.Controls.Desktop.Notch;

/// <summary>
/// GTK 4 backing for the notch window.
/// </summary>
/// <remarks>
/// <para>
/// As with the quick entry popup, what is achievable depends on the session. Under <b>X11</b> the
/// notch is an undecorated, transparent window centred at the top of the primary monitor's work area
/// (below a top panel), kept above other windows, out of the taskbar and pager, and — with
/// <see cref="NotchWindowOptions.JoinAllSpaces"/> — on every workspace.
/// </para>
/// <para>
/// Under <b>Wayland</b> a client can neither place its own toplevel nor raise it, so the notch still
/// morphs and still reports the pointer, but the compositor decides where the window lands and it is
/// an ordinary window in the stack. Pinning it properly needs the <c>wlr-layer-shell</c> protocol,
/// which GNOME does not implement and which this package does not take a dependency on.
/// </para>
/// <para>
/// The window is transparent, so the notch's own rounded outline is what shows — given a compositing
/// window manager. Without one the corners paint black, which matches the default surface colour.
/// </para>
/// </remarks>
static unsafe class NotchWindowPlatform
{
    sealed class Hooks
    {
        public NotchWindowOptions Options = null!;
        public Action<bool>? Pointer;
        public int Width;
        public int Height;
        public double TopMargin;
    }

    static readonly Dictionary<IntPtr, Hooks> Registry = new();
    static readonly object Gate = new();

    /// <summary>GTK has no animated window resize; the service steps the morph frame by frame.</summary>
    public static bool AnimatesFrame => false;

    public static bool IsTransparent => true;

    public static NotchGeometry GetGeometry() => NotchGeometry.None;

    public static void Initialize(object platformWindow, NotchWindowOptions options)
        => QuickEntryPlatform.BeginInvokeOnMainThread(() =>
        {
            var window = QuickEntryPlatform.GetNativeHandleForGlow(platformWindow);
            if (window == IntPtr.Zero)
                return;

            // Left resizable, unlike the quick entry popup: GTK sizes a non-resizable window to its
            // content's natural size and ignores the default size, and this window is sized from
            // outside, to the notch. Undecorated, it has no edges to drag anyway.
            Gtk4Interop.WindowSetDecorated(window, 0);
            Gtk4Interop.WidgetAddCssClass(window, "shiny-notch");
            Gtk4Styling.EnsureCss(window);

            lock (Gate)
                Registry[window] = new Hooks { Options = options };

            var motion = Gtk4Interop.EventControllerMotionNew();
            delegate* unmanaged<IntPtr, double, double, IntPtr, void> enter = &OnPointerEnter;
            delegate* unmanaged<IntPtr, IntPtr, void> leave = &OnPointerLeave;
            Gtk4Interop.SignalConnectData(motion, "enter", (IntPtr)enter, window, IntPtr.Zero, 0);
            Gtk4Interop.SignalConnectData(motion, "leave", (IntPtr)leave, window, IntPtr.Zero, 0);
            Gtk4Interop.WidgetAddController(window, motion);

            Gtk4Interop.WidgetSetVisible(window, 0);
        });

    public static void Place(object platformWindow, double width, double height, CornerRadius corners, bool hugNotch, double topMargin, bool coverMenuBar, double durationMs)
        => QuickEntryPlatform.BeginInvokeOnMainThread(() =>
        {
            var window = QuickEntryPlatform.GetNativeHandleForGlow(platformWindow);
            if (window == IntPtr.Zero)
                return;

            var w = Math.Max(1, (int)Math.Round(width));
            var h = Math.Max(1, (int)Math.Round(height));
            lock (Gate)
            {
                if (Registry.TryGetValue(window, out var hooks))
                {
                    hooks.Width = w;
                    hooks.Height = h;
                    hooks.TopMargin = topMargin;
                }
            }

            Gtk4Interop.WindowSetDefaultSize(window, w, h);
            MoveX11(window, w, h, topMargin);
        });

    public static void Show(object platformWindow)
        => QuickEntryPlatform.BeginInvokeOnMainThread(() =>
        {
            var window = QuickEntryPlatform.GetNativeHandleForGlow(platformWindow);
            if (window == IntPtr.Zero)
                return;

            Hooks? hooks;
            lock (Gate)
                Registry.TryGetValue(window, out hooks);

            // Not gtk_window_present: that activates the window, and the notch must never take focus.
            Gtk4Interop.WidgetSetVisible(window, 1);

            if (hooks != null)
            {
                // A window manager is free to re-place a window when it maps, and it drops the window's
                // states when it unmaps, so both are re-asked for on every show.
                ApplyX11State(window, hooks.Options);
                if (hooks.Width > 0)
                    MoveX11(window, hooks.Width, hooks.Height, hooks.TopMargin);
            }
        });

    /// <summary>
    /// Nothing to freeze here: the window is resized once per frame and lays out each time, so the new
    /// content simply fades in as the notch morphs. (On macOS the old content collapses with it.)
    /// </summary>
    public static void FreezeContent(object platformWindow) { }

    public static void Hide(object platformWindow)
        => QuickEntryPlatform.BeginInvokeOnMainThread(() =>
        {
            var window = QuickEntryPlatform.GetNativeHandleForGlow(platformWindow);
            if (window != IntPtr.Zero)
                Gtk4Interop.WidgetSetVisible(window, 0);
        });

    public static void Teardown(object platformWindow)
    {
        Hide(platformWindow);

        var window = QuickEntryPlatform.GetNativeHandleForGlow(platformWindow);
        if (window == IntPtr.Zero)
            return;

        lock (Gate)
            Registry.Remove(window);
    }

    public static IDisposable TrackPointer(object platformWindow, Action<bool> changed)
    {
        var window = QuickEntryPlatform.GetNativeHandleForGlow(platformWindow);
        lock (Gate)
        {
            if (window == IntPtr.Zero || !Registry.TryGetValue(window, out var hooks))
                return new Tracking(IntPtr.Zero);

            hooks.Pointer = changed;
        }
        return new Tracking(window);
    }

    sealed class Tracking(IntPtr window) : IDisposable
    {
        public void Dispose()
        {
            if (window == IntPtr.Zero)
                return;

            lock (Gate)
            {
                if (Registry.TryGetValue(window, out var hooks))
                    hooks.Pointer = null;
            }
        }
    }

    // -------------------------------------------------------------------------------------

    [UnmanagedCallersOnly]
    static void OnPointerEnter(IntPtr controller, double x, double y, IntPtr userData) => NotifyPointer(userData, true);

    [UnmanagedCallersOnly]
    static void OnPointerLeave(IntPtr controller, IntPtr userData) => NotifyPointer(userData, false);

    static void NotifyPointer(IntPtr window, bool over)
    {
        try
        {
            Action<bool>? pointer;
            lock (Gate)
                pointer = Registry.TryGetValue(window, out var hooks) ? hooks.Pointer : null;

            pointer?.Invoke(over);
        }
        catch
        {
            // A managed exception crossing back into the GTK main loop terminates the process.
        }
    }

    // -------------------------------------------------------------------------------------

    static void ApplyX11State(IntPtr window, NotchWindowOptions options)
    {
        if (!TryGetX11(window, out var display, out var xid))
            return;

        X11Interop.AddWmState(display, xid, "_NET_WM_STATE_ABOVE", "_NET_WM_STATE_SKIP_TASKBAR");
        X11Interop.AddWmState(display, xid, "_NET_WM_STATE_SKIP_PAGER", options.JoinAllSpaces ? "_NET_WM_STATE_STICKY" : null);
    }

    /// <summary>
    /// Top-centre of the primary monitor's work area. GDK reports monitors in application pixels and X
    /// positions windows in device pixels, so everything is scaled by the monitor's factor.
    /// </summary>
    static void MoveX11(IntPtr window, int width, int height, double topMargin)
    {
        if (!TryGetX11(window, out var display, out var xid))
            return;

        var (area, scale) = WorkArea(window, display);
        var x = area.X + ((area.Width - width) / 2);
        var y = area.Y + (int)Math.Round(topMargin);

        X11Interop.MoveResizeWindow(display, (IntPtr)xid, x * scale, y * scale, (uint)(width * scale), (uint)(height * scale));
        X11Interop.Flush(display);
    }

    static (Gtk4Interop.GdkRectangle Area, int Scale) WorkArea(IntPtr window, IntPtr x11Display)
    {
        var display = Gtk4Interop.WidgetGetDisplay(window);
        var monitor = IntPtr.Zero;
        var owned = false;

        try
        {
            if (display != IntPtr.Zero)
            {
                try
                {
                    monitor = Gtk4Interop.X11DisplayGetPrimaryMonitor(display);
                }
                catch (EntryPointNotFoundException)
                {
                }

                // No primary set (xrandr leaves it unset on some setups): the first monitor instead.
                if (monitor == IntPtr.Zero)
                {
                    var monitors = Gtk4Interop.DisplayGetMonitors(display);
                    if (monitors != IntPtr.Zero)
                    {
                        monitor = Gtk4Interop.ListModelGetItem(monitors, 0);
                        owned = monitor != IntPtr.Zero;
                    }
                }
            }

            if (monitor == IntPtr.Zero)
            {
                var screen = X11Interop.DefaultScreen(x11Display);
                return (new Gtk4Interop.GdkRectangle
                {
                    Width = X11Interop.DisplayWidth(x11Display, screen),
                    Height = X11Interop.DisplayHeight(x11Display, screen)
                }, 1);
            }

            Gtk4Interop.GdkRectangle area;
            try
            {
                Gtk4Interop.X11MonitorGetWorkarea(monitor, out area);
            }
            catch (EntryPointNotFoundException)
            {
                Gtk4Interop.MonitorGetGeometry(monitor, out area);
            }

            return (area, Math.Max(1, Gtk4Interop.MonitorGetScaleFactor(monitor)));
        }
        finally
        {
            if (owned)
                Gtk4Interop.ObjectUnref(monitor);
        }
    }

    static bool TryGetX11(IntPtr window, out IntPtr display, out ulong xid)
    {
        display = IntPtr.Zero;
        xid = 0;
        if (QuickEntryPlatform.IsWayland)
            return false;

        var surface = Gtk4Interop.NativeGetSurface(window);
        if (surface == IntPtr.Zero)
            return false;

        try
        {
            xid = Gtk4Interop.X11SurfaceGetXid(surface);
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }

        display = QuickEntryPlatform.EnsureX11Display();
        return xid != 0 && display != IntPtr.Zero;
    }
}

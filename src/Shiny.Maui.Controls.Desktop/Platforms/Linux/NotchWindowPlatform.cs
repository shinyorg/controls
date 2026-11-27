using System.Runtime.InteropServices;
using Shiny.Maui.Controls.Desktop.QuickEntry;

namespace Shiny.Maui.Controls.Desktop.Notch;

/// <summary>
/// GTK 4 backing for the notch window.
/// </summary>
/// <remarks>
/// <para>
/// As with the quick entry popup, what is achievable depends on the session. Under <b>X11</b> the
/// notch is an undecorated, transparent window centred at the top of its monitor's work area (the primary
/// monitor unless another is asked for)
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
        public string? DisplayId;
        public double Position = 0.5;
        /// <summary>The pointer is over the window, from the motion controller: a press here starts a drag.</summary>
        public bool Over;
    }

    static readonly Dictionary<IntPtr, Hooks> Registry = new();
    static readonly object Gate = new();

    /// <summary>GTK has no animated window resize; the service steps the morph frame by frame.</summary>
    public static bool AnimatesFrame => false;

    public static bool IsTransparent => true;

    public static NotchGeometry GetGeometry(string? displayId) => NotchGeometry.None;

    /// <summary>
    /// Every monitor GDK knows, the primary first, by connector ("HDMI-1"). Under Wayland the list is
    /// there, but the compositor still decides where the window goes.
    /// </summary>
    public static IReadOnlyList<NotchDisplay> GetDisplays()
    {
        var displays = new List<NotchDisplay>();
        var display = Gtk4Interop.DisplayGetDefault();
        if (display == IntPtr.Zero)
            return displays;

        var primary = PrimaryMonitor(display);
        var monitors = Gtk4Interop.DisplayGetMonitors(display);
        var count = monitors == IntPtr.Zero ? 0 : Gtk4Interop.ListModelGetNItems(monitors);
        for (uint i = 0; i < count; i++)
        {
            var monitor = Gtk4Interop.ListModelGetItem(monitors, i);
            if (monitor != IntPtr.Zero)
            {
                try
                {
                    var id = Utf8(Gtk4Interop.MonitorGetConnector(monitor)) ?? $"monitor-{i}";
                    displays.Add(new NotchDisplay(id, MonitorName(monitor, (int)i), monitor == primary || (primary == IntPtr.Zero && i == 0), Size.Zero));
                }
                finally
                {
                    Gtk4Interop.ObjectUnref(monitor);
                }
            }
        }
        return displays.OrderByDescending(d => d.IsPrimary).ToList();
    }

    static string MonitorName(IntPtr monitor, int index)
    {
        string? name = null;
        try
        {
            name = Utf8(Gtk4Interop.MonitorGetDescription(monitor));
        }
        catch (EntryPointNotFoundException)
        {
        }
        name ??= Utf8(Gtk4Interop.MonitorGetModel(monitor));
        return string.IsNullOrWhiteSpace(name) ? $"Display {index + 1}" : name;
    }

    static string? Utf8(IntPtr text) => text == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(text);

    /// <summary>The X11 primary monitor (transfer none), or zero under Wayland or with none set.</summary>
    static IntPtr PrimaryMonitor(IntPtr display)
    {
        if (QuickEntryPlatform.IsWayland)
            return IntPtr.Zero;

        try
        {
            return Gtk4Interop.X11DisplayGetPrimaryMonitor(display);
        }
        catch (EntryPointNotFoundException)
        {
            return IntPtr.Zero;
        }
    }

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

    public static void Place(object platformWindow, double width, double height, CornerRadius corners, bool hugNotch, double topMargin, bool coverMenuBar, double durationMs, string? displayId, double position)
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
                    hooks.DisplayId = displayId;
                    hooks.Position = position;
                }
            }

            Gtk4Interop.WindowSetDefaultSize(window, w, h);
            MoveX11(window, w, h, topMargin, displayId, position);
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
                    MoveX11(window, hooks.Width, hooks.Height, hooks.TopMargin, hooks.DisplayId, hooks.Position);
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

    /// <summary>
    /// Reports the left button going down on the window, the pointer moving with it held and it coming up,
    /// for dragging the notch along the top of its monitor: a poll of the X pointer, with the motion
    /// controller saying whether the press was on the notch. X11 only; under Wayland the compositor places
    /// the window, so there is nothing to drag. The pointer is a fraction of the monitor's work area, with
    /// that width in application pixels.
    /// </summary>
    public static IDisposable TrackDrag(object platformWindow, string? displayId, Action<NotchDragPhase, double, double> changed)
    {
        var window = QuickEntryPlatform.GetNativeHandleForGlow(platformWindow);
        if (window == IntPtr.Zero || QuickEntryPlatform.IsWayland || Application.Current?.Dispatcher is not { } dispatcher)
            return new DragPoll(null);

        var wasDown = false;
        var dragging = false;

        var timer = dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.IsRepeating = true;
        timer.Tick += (_, _) =>
        {
            if (!TryGetX11(window, out var display, out _))
                return;

            var root = X11Interop.DefaultRootWindow(display);
            if (X11Interop.QueryPointer(display, root, out _, out _, out var rootX, out _, out _, out _, out var mask) == 0)
                return;

            bool over;
            lock (Gate)
                over = Registry.TryGetValue(window, out var hooks) && hooks.Over;

            var down = (mask & X11Interop.Button1Mask) != 0;
            var phase = (down, wasDown, dragging) switch
            {
                (true, false, _) when over => NotchDragPhase.Pressed,
                (true, _, true) => NotchDragPhase.Moved,
                (false, _, true) => NotchDragPhase.Released,
                _ => (NotchDragPhase?)null
            };
            wasDown = down;
            if (phase is not { } reported)
                return;

            dragging = reported != NotchDragPhase.Released;
            var (area, scale) = WorkArea(window, display, displayId);
            var widthPx = area.Width * scale;
            var fraction = widthPx > 0 ? (rootX - (area.X * scale)) / (double)widthPx : 0.5;
            changed(reported, fraction, area.Width);
        };
        timer.Start();
        return new DragPoll(timer);
    }

    sealed class DragPoll(IDispatcherTimer? timer) : IDisposable
    {
        public void Dispose() => timer?.Stop();
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
            Action<bool>? pointer = null;
            lock (Gate)
            {
                if (Registry.TryGetValue(window, out var hooks))
                {
                    hooks.Over = over;
                    pointer = hooks.Pointer;
                }
            }

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
    /// The top of the monitor's work area (the primary's for null), centred at <paramref name="position"/>
    /// (a fraction of its width) and kept on it. GDK reports monitors in application pixels and X positions
    /// windows in device pixels, so everything is scaled by the monitor's factor.
    /// </summary>
    static void MoveX11(IntPtr window, int width, int height, double topMargin, string? displayId, double position)
    {
        if (!TryGetX11(window, out var display, out var xid))
            return;

        var (area, scale) = WorkArea(window, display, displayId);
        var x = area.X + (int)Math.Round(Math.Clamp((area.Width * position) - (width / 2d), 0, Math.Max(0, area.Width - width)));
        var y = area.Y + (int)Math.Round(topMargin);

        X11Interop.MoveResizeWindow(display, (IntPtr)xid, x * scale, y * scale, (uint)(width * scale), (uint)(height * scale));
        X11Interop.Flush(display);
    }

    static (Gtk4Interop.GdkRectangle Area, int Scale) WorkArea(IntPtr window, IntPtr x11Display, string? displayId)
    {
        var display = Gtk4Interop.WidgetGetDisplay(window);
        var monitor = IntPtr.Zero;
        var owned = false;

        try
        {
            if (display != IntPtr.Zero && displayId != null)
            {
                monitor = MonitorByConnector(display, displayId);
                owned = monitor != IntPtr.Zero;
            }

            if (display != IntPtr.Zero && monitor == IntPtr.Zero)
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

    /// <summary>The monitor on this connector, transfer full (the caller unrefs it); zero when it's gone.</summary>
    static IntPtr MonitorByConnector(IntPtr display, string connector)
    {
        var monitors = Gtk4Interop.DisplayGetMonitors(display);
        var count = monitors == IntPtr.Zero ? 0 : Gtk4Interop.ListModelGetNItems(monitors);
        for (uint i = 0; i < count; i++)
        {
            var monitor = Gtk4Interop.ListModelGetItem(monitors, i);
            if (monitor != IntPtr.Zero && Utf8(Gtk4Interop.MonitorGetConnector(monitor)) == connector)
                return monitor;
            if (monitor != IntPtr.Zero)
                Gtk4Interop.ObjectUnref(monitor);
        }
        return IntPtr.Zero;
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

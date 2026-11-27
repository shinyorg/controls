using System.Runtime.InteropServices;

namespace Shiny.Maui.Controls.Desktop.QuickEntry;

/// <summary>
/// P/Invoke into GTK 4 for the quick entry popup.
/// </summary>
/// <remarks>
/// Separate from <c>LinuxInterop</c>, which targets GTK 3 for the ayatana app indicator. The two
/// major versions cannot be loaded into one process safely, but the tray path only ever touches
/// GTK 3 symbols and this one only GTK 4, and the GTK4 MAUI head has already initialised GTK 4 by
/// the time any of this runs.
/// </remarks>
static partial class Gtk4Interop
{
    const string Gtk = "libgtk-4.so.1";
    const string GObject = "libgobject-2.0.so.0";

    // GDK keyvals
    public const uint KeyEscape = 0xff1b;
    public const uint KeyReturn = 0xff0d;
    public const uint KeyKpEnter = 0xff8d;
    public const uint KeyUp = 0xff52;
    public const uint KeyDown = 0xff54;
    public const uint KeyTab = 0xff09;

    [LibraryImport(Gtk, EntryPoint = "gtk_window_set_decorated")]
    public static partial void WindowSetDecorated(IntPtr window, [MarshalAs(UnmanagedType.I4)] int decorated);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_set_resizable")]
    public static partial void WindowSetResizable(IntPtr window, [MarshalAs(UnmanagedType.I4)] int resizable);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_set_default_size")]
    public static partial void WindowSetDefaultSize(IntPtr window, int width, int height);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_present")]
    public static partial void WindowPresent(IntPtr window);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_is_active")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial int WindowIsActive(IntPtr window);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_set_visible")]
    public static partial void WidgetSetVisible(IntPtr widget, [MarshalAs(UnmanagedType.I4)] int visible);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_add_css_class", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void WidgetAddCssClass(IntPtr widget, string cssClass);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_add_controller")]
    public static partial void WidgetAddController(IntPtr widget, IntPtr controller);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_get_display")]
    public static partial IntPtr WidgetGetDisplay(IntPtr widget);

    [LibraryImport(Gtk, EntryPoint = "gtk_event_controller_key_new")]
    public static partial IntPtr EventControllerKeyNew();

    [LibraryImport(Gtk, EntryPoint = "gtk_native_get_surface")]
    public static partial IntPtr NativeGetSurface(IntPtr native);

    [LibraryImport(Gtk, EntryPoint = "gtk_css_provider_new")]
    public static partial IntPtr CssProviderNew();

    /// <summary>GTK 4.12+. Older runtimes need <see cref="CssProviderLoadFromData"/>, which is tried as a fallback.</summary>
    [LibraryImport(Gtk, EntryPoint = "gtk_css_provider_load_from_string", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void CssProviderLoadFromString(IntPtr provider, string css);

    [LibraryImport(Gtk, EntryPoint = "gtk_css_provider_load_from_data", StringMarshalling = StringMarshalling.Utf8)]
    public static partial void CssProviderLoadFromData(IntPtr provider, string css, IntPtr length);

    [LibraryImport(Gtk, EntryPoint = "gtk_style_context_add_provider_for_display")]
    public static partial void StyleContextAddProviderForDisplay(IntPtr display, IntPtr provider, uint priority);

    [LibraryImport(Gtk, EntryPoint = "gdk_x11_surface_get_xid")]
    public static partial ulong X11SurfaceGetXid(IntPtr surface);

    [LibraryImport(Gtk, EntryPoint = "gtk_editable_set_alignment")]
    public static partial void EditableSetAlignment(IntPtr editable, float alignment);


    [LibraryImport(Gtk, EntryPoint = "gtk_window_fullscreen")]
    public static partial void WindowFullscreen(IntPtr window);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_set_can_target")]
    public static partial void WidgetSetCanTarget(IntPtr widget, [MarshalAs(UnmanagedType.I4)] int canTarget);

    /// <summary>An empty input region is what makes the glow overlay click-through; the pointer passes to whatever is underneath.</summary>
    [LibraryImport(Gtk, EntryPoint = "gdk_surface_set_input_region")]
    public static partial void SurfaceSetInputRegion(IntPtr surface, IntPtr region);

    [LibraryImport("libcairo.so.2", EntryPoint = "cairo_region_create")]
    public static partial IntPtr CairoRegionCreate();

    [LibraryImport("libcairo.so.2", EntryPoint = "cairo_region_destroy")]
    public static partial void CairoRegionDestroy(IntPtr region);

    [LibraryImport(GObject, EntryPoint = "g_signal_connect_data", StringMarshalling = StringMarshalling.Utf8)]
    public static partial ulong SignalConnectData(IntPtr instance, string signal, IntPtr handler, IntPtr data, IntPtr destroy, int flags);

    [LibraryImport(GObject, EntryPoint = "g_signal_handler_disconnect")]
    public static partial void SignalHandlerDisconnect(IntPtr instance, ulong handlerId);

    [LibraryImport(GObject, EntryPoint = "g_object_set_property")]
    public static partial void ObjectSetProperty(IntPtr obj, IntPtr name, IntPtr value);

    public const uint StyleProviderPriorityApplication = 600;

    // ---- notch window ------------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    public struct GdkRectangle
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;
    }

    /// <summary>Pointer enter/leave on a widget. Unlike a global pointer query, this works under Wayland too.</summary>
    [LibraryImport(Gtk, EntryPoint = "gtk_event_controller_motion_new")]
    public static partial IntPtr EventControllerMotionNew();

    /// <summary>A GListModel of GdkMonitor, owned by the display.</summary>
    [LibraryImport(Gtk, EntryPoint = "gdk_display_get_monitors")]
    public static partial IntPtr DisplayGetMonitors(IntPtr display);

    [LibraryImport(Gtk, EntryPoint = "gdk_monitor_get_geometry")]
    public static partial void MonitorGetGeometry(IntPtr monitor, out GdkRectangle geometry);

    [LibraryImport(Gtk, EntryPoint = "gdk_monitor_get_scale_factor")]
    public static partial int MonitorGetScaleFactor(IntPtr monitor);

    /// <summary>X11 backend only. Transfer none.</summary>
    [LibraryImport(Gtk, EntryPoint = "gdk_x11_display_get_primary_monitor")]
    public static partial IntPtr X11DisplayGetPrimaryMonitor(IntPtr display);

    /// <summary>X11 backend only: the monitor minus panels and docks (<c>_NET_WORKAREA</c>).</summary>
    [LibraryImport(Gtk, EntryPoint = "gdk_x11_monitor_get_workarea")]
    public static partial void X11MonitorGetWorkarea(IntPtr monitor, out GdkRectangle workarea);

    [LibraryImport(Gtk, EntryPoint = "gdk_display_get_default")]
    public static partial IntPtr DisplayGetDefault();

    /// <summary>The connector, such as "HDMI-1". Transfer none; may be null.</summary>
    [LibraryImport(Gtk, EntryPoint = "gdk_monitor_get_connector")]
    public static partial IntPtr MonitorGetConnector(IntPtr monitor);

    /// <summary>GTK 4.10 and later: a name for the monitor to show the user. Transfer none; may be null.</summary>
    [LibraryImport(Gtk, EntryPoint = "gdk_monitor_get_description")]
    public static partial IntPtr MonitorGetDescription(IntPtr monitor);

    /// <summary>The monitor's model. Transfer none; may be null.</summary>
    [LibraryImport(Gtk, EntryPoint = "gdk_monitor_get_model")]
    public static partial IntPtr MonitorGetModel(IntPtr monitor);

    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_list_model_get_n_items")]
    public static partial uint ListModelGetNItems(IntPtr list);

    /// <summary>Transfer full: the caller unrefs the item.</summary>
    [LibraryImport("libgio-2.0.so.0", EntryPoint = "g_list_model_get_item")]
    public static partial IntPtr ListModelGetItem(IntPtr list, uint position);

    [LibraryImport(GObject, EntryPoint = "g_object_unref")]
    public static partial void ObjectUnref(IntPtr obj);
}

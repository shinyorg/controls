using System.Reflection;
using System.Runtime.InteropServices;
using Shiny.Controls.Keyboard;
using Shiny.Maui.Controls;
using MauiWindow = Microsoft.Maui.Controls.Window;

namespace Shiny.Maui.Controls.Desktop.KeyboardShortcuts;

/// <summary>
/// GTK 4 key source for keyboard shortcuts — and the do-nothing implementation everywhere else this
/// package's <c>net10.0</c> asset is used.
/// </summary>
/// <remarks>
/// <para>
/// A <c>GtkEventControllerKey</c> on the toplevel window in the <b>capture</b> phase, so the window
/// is offered every key before the focused widget — before a <c>GtkText</c> inserts it and before a
/// WebKitGTK view gets it. Returning <c>TRUE</c> from <c>key-pressed</c> stops the event there.
/// </para>
/// <para>
/// GTK does not mark auto-repeats, so the source keeps the set of keys it has seen go down: a press
/// for a key already in it is a repeat. The set — and anything a shortcut holds — is cleared when the
/// window stops being the active one, because the matching releases will go elsewhere.
/// </para>
/// </remarks>
static unsafe class KeyboardShortcutPlatform
{
    static readonly Dictionary<IntPtr, LinuxKeySource> registry = new();
    static readonly object gate = new();

    public static IDisposable? Attach(MauiWindow window, object platformWindow)
    {
        if (!OperatingSystem.IsLinux())
            return null;

        var handle = GetNativeHandle(platformWindow);
        if (handle == IntPtr.Zero)
        {
            System.Diagnostics.Debug.WriteLine("[Shiny.KeyboardShortcuts] The GTK window handle could not be read, so keyboard shortcuts were not attached.");
            return null;
        }

        try
        {
            BeginInvokeOnMainThread(() =>
            {
                var source = new LinuxKeySource(window, handle);
                lock (gate)
                    registry[handle] = source;
            });
            return new Detach(handle);
        }
        catch (DllNotFoundException ex)
        {
            // The net10.0 asset running somewhere that is not a GTK4 session.
            System.Diagnostics.Debug.WriteLine($"[Shiny.KeyboardShortcuts] GTK 4 is not available: {ex.Message}");
            return null;
        }
    }

    static void BeginInvokeOnMainThread(Action action)
    {
        // GTK is not thread-safe, and attaching can be reached from a startup poll's continuation.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || !dispatcher.IsDispatchRequired)
            action();
        else
            dispatcher.Dispatch(action);
    }

    static IntPtr GetNativeHandle(object? platformWindow)
    {
        if (platformWindow == null)
            return IntPtr.Zero;

        try
        {
            // The GTK4 head is loaded by the app, not referenced here, so its window type is only
            // reachable by reflection — the same approach file drop and the quick entry popup take.
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

    static LinuxKeySource? Find(IntPtr userData)
    {
        lock (gate)
            return registry.GetValueOrDefault(userData);
    }

    // -------------------------------------------------------------------------------------

    [UnmanagedCallersOnly]
    static int OnKeyPressed(IntPtr controller, uint keyval, uint keycode, uint state, IntPtr userData)
    {
        try
        {
            return Find(userData)?.OnKey(keyval, keycode, state, isUp: false) == true ? 1 : 0;
        }
        catch
        {
            // A managed exception crossing back into the GTK main loop terminates the process.
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    static void OnKeyReleased(IntPtr controller, uint keyval, uint keycode, uint state, IntPtr userData)
    {
        try
        {
            Find(userData)?.OnKey(keyval, keycode, state, isUp: true);
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly]
    static void OnActiveChanged(IntPtr window, IntPtr pspec, IntPtr userData)
    {
        try
        {
            if (Find(userData) is { } source && GtkKeyInterop.WindowIsActive(window) == 0)
                source.ReleaseAll();
        }
        catch
        {
        }
    }

    sealed class Detach(IntPtr handle) : IDisposable
    {
        public void Dispose()
        {
            LinuxKeySource? source;
            lock (gate)
            {
                registry.Remove(handle, out source);
            }

            if (source != null)
                BeginInvokeOnMainThread(source.Dispose);
        }
    }

    sealed class LinuxKeySource : IDisposable
    {
        readonly MauiWindow window;
        readonly IntPtr handle;
        readonly IntPtr controller;
        readonly ulong activeHandler;
        readonly HashSet<uint> down = new();

        public LinuxKeySource(MauiWindow window, IntPtr handle)
        {
            this.window = window;
            this.handle = handle;

            this.controller = GtkKeyInterop.EventControllerKeyNew();
            GtkKeyInterop.EventControllerSetPropagationPhase(this.controller, GtkKeyInterop.PhaseCapture);

            delegate* unmanaged<IntPtr, uint, uint, uint, IntPtr, int> pressed = &OnKeyPressed;
            delegate* unmanaged<IntPtr, uint, uint, uint, IntPtr, void> released = &OnKeyReleased;
            delegate* unmanaged<IntPtr, IntPtr, IntPtr, void> active = &OnActiveChanged;

            // The window pointer doubles as user data, so callbacks find their way back without
            // pinning a managed object.
            GtkKeyInterop.SignalConnectData(this.controller, "key-pressed", (IntPtr)pressed, handle, IntPtr.Zero, 0);
            GtkKeyInterop.SignalConnectData(this.controller, "key-released", (IntPtr)released, handle, IntPtr.Zero, 0);
            GtkKeyInterop.WidgetAddController(handle, this.controller);

            this.activeHandler = GtkKeyInterop.SignalConnectData(handle, "notify::is-active", (IntPtr)active, handle, IntPtr.Zero, 0);
        }

        public bool OnKey(uint keyval, uint keycode, uint state, bool isUp)
        {
            bool repeat;
            if (isUp)
            {
                this.down.Remove(keycode);
                repeat = false;
            }
            else
            {
                repeat = !this.down.Add(keycode);
            }

            var modifiers = KeyModifiers.None;
            if ((state & GtkKeyInterop.ShiftMask) != 0) modifiers |= KeyModifiers.Shift;
            if ((state & GtkKeyInterop.ControlMask) != 0) modifiers |= KeyModifiers.Control;
            if ((state & GtkKeyInterop.AltMask) != 0) modifiers |= KeyModifiers.Alt;
            if ((state & (GtkKeyInterop.SuperMask | GtkKeyInterop.MetaMask)) != 0) modifiers |= KeyModifiers.Meta;

            // The keyval already carries Shift ("A", "question"), which is what a character gesture
            // compares against.
            var unicode = GtkKeyInterop.KeyvalToUnicode(keyval);
            var stroke = new KeyStroke
            {
                Code = NativeKeyCodes.FromXKeycode((int)keycode),
                Character = IsPrintable(unicode) ? Char.ConvertFromUtf32((int)unicode) : null,
                Modifiers = modifiers,
                IsKeyUp = isUp,
                IsRepeat = repeat,
                // ISO_Level3_Shift (AltGr) arrives as Mod5 on X11 layouts that have it.
                IsAltGraph = (state & GtkKeyInterop.Mod5Mask) != 0,
                IsTextInputFocused = this.IsTextInputFocused()
            };

            return KeyboardShortcutManager.Process(stroke, this.window);
        }

        static bool IsPrintable(uint codepoint)
            => codepoint is > 0x1F and < 0x110000
               and not (>= 0x7F and <= 0x9F)
               and not (>= 0xD800 and <= 0xDFFF);

        bool IsTextInputFocused()
        {
            var focus = GtkKeyInterop.RootGetFocus(this.handle);
            if (focus == IntPtr.Zero)
                return false;

            if (GtkKeyInterop.TypeCheckInstanceIsA(focus, GtkKeyInterop.EditableGetType()) != 0 ||
                GtkKeyInterop.TypeCheckInstanceIsA(focus, GtkKeyInterop.TextViewGetType()) != 0)
                return true;

            // A WebKitGTK view: typing may be going into the page.
            var name = Marshal.PtrToStringUTF8(GtkKeyInterop.TypeNameFromInstance(focus));
            return name != null && name.StartsWith("WebKit", StringComparison.Ordinal);
        }

        public void ReleaseAll()
        {
            this.down.Clear();
            KeyboardShortcutManager.Engine.ReleaseAll(this.window);
        }

        public void Dispose()
        {
            GtkKeyInterop.WidgetRemoveController(this.handle, this.controller);
            if (this.activeHandler != 0)
                GtkKeyInterop.SignalHandlerDisconnect(this.handle, this.activeHandler);
        }
    }
}

/// <summary>P/Invoke into GTK 4 for the keyboard shortcut source.</summary>
static partial class GtkKeyInterop
{
    const string Gtk = "libgtk-4.so.1";
    const string GObject = "libgobject-2.0.so.0";

    public const int PhaseCapture = 1;

    // GdkModifierType
    public const uint ShiftMask = 1 << 0;
    public const uint ControlMask = 1 << 2;
    public const uint AltMask = 1 << 3;
    public const uint Mod5Mask = 1 << 7;
    public const uint SuperMask = 1 << 26;
    public const uint MetaMask = 1 << 28;

    [LibraryImport(Gtk, EntryPoint = "gtk_event_controller_key_new")]
    public static partial IntPtr EventControllerKeyNew();

    [LibraryImport(Gtk, EntryPoint = "gtk_event_controller_set_propagation_phase")]
    public static partial void EventControllerSetPropagationPhase(IntPtr controller, int phase);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_add_controller")]
    public static partial void WidgetAddController(IntPtr widget, IntPtr controller);

    [LibraryImport(Gtk, EntryPoint = "gtk_widget_remove_controller")]
    public static partial void WidgetRemoveController(IntPtr widget, IntPtr controller);

    [LibraryImport(Gtk, EntryPoint = "gtk_window_is_active")]
    public static partial int WindowIsActive(IntPtr window);

    [LibraryImport(Gtk, EntryPoint = "gtk_root_get_focus")]
    public static partial IntPtr RootGetFocus(IntPtr root);

    [LibraryImport(Gtk, EntryPoint = "gtk_editable_get_type")]
    public static partial nuint EditableGetType();

    [LibraryImport(Gtk, EntryPoint = "gtk_text_view_get_type")]
    public static partial nuint TextViewGetType();

    [LibraryImport(Gtk, EntryPoint = "gdk_keyval_to_unicode")]
    public static partial uint KeyvalToUnicode(uint keyval);

    [LibraryImport(GObject, EntryPoint = "g_type_check_instance_is_a")]
    public static partial int TypeCheckInstanceIsA(IntPtr instance, nuint type);

    [LibraryImport(GObject, EntryPoint = "g_type_name_from_instance")]
    public static partial IntPtr TypeNameFromInstance(IntPtr instance);

    [LibraryImport(GObject, EntryPoint = "g_signal_connect_data", StringMarshalling = StringMarshalling.Utf8)]
    public static partial ulong SignalConnectData(IntPtr instance, string signal, IntPtr handler, IntPtr data, IntPtr destroy, int flags);

    [LibraryImport(GObject, EntryPoint = "g_signal_handler_disconnect")]
    public static partial void SignalHandlerDisconnect(IntPtr instance, ulong handlerId);
}

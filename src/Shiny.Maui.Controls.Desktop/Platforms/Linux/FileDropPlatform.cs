using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Maui.Graphics;
using MauiWindow = Microsoft.Maui.Controls.Window;

namespace Shiny.Maui.Controls.Desktop.FileDrop;

/// <summary>
/// GTK 4 backing for window-level file drop — and the do-nothing implementation everywhere else
/// this package's <c>net11.0</c> asset is used.
/// </summary>
/// <remarks>
/// <para>
/// A <c>GtkDropTarget</c> is an ordinary event controller, so the whole feature reduces to putting
/// one on the toplevel window in the <b>capture</b> phase. Capture means the toplevel is offered
/// the drag before any of its children, which is precisely the "over top of a web view" behaviour —
/// WebKitGTK's own drop handling is a controller on a child widget and never gets asked.
/// <see cref="FileDropOptions.SuppressWebViewDrop"/> switches the phase back to bubble, which hands
/// the ordering back to the children.
/// </para>
/// <para>
/// Preloading is on, so the file list is readable during <c>motion</c> rather than only on
/// <c>drop</c>. Without it a drag hovering over the window could report that it carries files but
/// never say which, and the extension filter would have nothing to work with until it was too late
/// to refuse.
/// </para>
/// </remarks>
static unsafe class FileDropPlatform
{
    public static bool IsSupported => OperatingSystem.IsLinux();

    public static IDisposable? Attach(IFileDropHost host, MauiWindow window, object platformWindow)
    {
        if (!OperatingSystem.IsLinux())
            return null;

        var widget = GetNativeHandle(platformWindow);
        if (widget == IntPtr.Zero)
        {
            host.LogError("The GTK window handle could not be read, so file drop was not attached.");
            return null;
        }

        try
        {
            return new LinuxDropTarget(host, window, widget);
        }
        catch (DllNotFoundException ex)
        {
            // Reaching here means the net11.0 asset is running somewhere that is not a GTK4 session.
            host.LogError("GTK 4 is not available, so file drop was not attached", ex);
            return null;
        }
    }

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
            // The GTK4 head is loaded by the app, not referenced here, so its window type is only
            // reachable by reflection — the same approach the quick entry popup takes.
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


unsafe sealed class LinuxDropTarget : IDisposable
{
    static readonly Dictionary<IntPtr, LinuxDropTarget> registry = new();
    static readonly object gate = new();

    readonly IFileDropHost host;
    readonly MauiWindow window;
    readonly IntPtr widget;
    readonly IntPtr target;
    bool disposed;

    public LinuxDropTarget(IFileDropHost host, MauiWindow window, IntPtr widget)
    {
        this.host = host;
        this.window = window;
        this.widget = widget;

        this.target = Gtk4DropInterop.DropTargetNew(Gtk4DropInterop.FileListGetType(), Gtk4DropInterop.ActionCopy);
        if (this.target == IntPtr.Zero)
            throw new InvalidOperationException("gtk_drop_target_new returned NULL.");

        Gtk4DropInterop.DropTargetSetPreload(this.target, 1);
        Gtk4DropInterop.EventControllerSetPropagationPhase(
            this.target,
            host.Options.SuppressWebViewDrop ? Gtk4DropInterop.PhaseCapture : Gtk4DropInterop.PhaseBubble
        );

        lock (gate)
            registry[this.target] = this;

        // The target pointer doubles as the user data, so every callback can find its way back to
        // this instance without pinning a managed object.
        delegate* unmanaged<IntPtr, double, double, IntPtr, uint> enter = &OnEnter;
        delegate* unmanaged<IntPtr, double, double, IntPtr, uint> motion = &OnMotion;
        delegate* unmanaged<IntPtr, IntPtr, void> leave = &OnLeave;
        delegate* unmanaged<IntPtr, IntPtr, double, double, IntPtr, int> drop = &OnDrop;

        Gtk4DropInterop.SignalConnectData(this.target, "enter", (IntPtr)enter, this.target, IntPtr.Zero, 0);
        Gtk4DropInterop.SignalConnectData(this.target, "motion", (IntPtr)motion, this.target, IntPtr.Zero, 0);
        Gtk4DropInterop.SignalConnectData(this.target, "leave", (IntPtr)leave, this.target, IntPtr.Zero, 0);
        Gtk4DropInterop.SignalConnectData(this.target, "drop", (IntPtr)drop, this.target, IntPtr.Zero, 0);

        Gtk4DropInterop.WidgetAddController(this.widget, this.target);
    }

    static LinuxDropTarget? Find(IntPtr userData)
    {
        lock (gate)
            return registry.GetValueOrDefault(userData);
    }

    IReadOnlyList<DroppedFile> Current()
    {
        var value = Gtk4DropInterop.DropTargetGetValue(this.target);
        return value == IntPtr.Zero ? [] : Read(value);
    }

    static IReadOnlyList<DroppedFile> Read(IntPtr gvalue)
    {
        var boxed = Gtk4DropInterop.ValueGetBoxed(gvalue);
        if (boxed == IntPtr.Zero)
            return [];

        var paths = Gtk4DropInterop.ReadPaths(boxed);
        var files = new List<DroppedFile>(paths.Count);

        foreach (var path in paths)
            files.Add(DroppedFile.FromPath(path));

        return files;
    }

    // A managed exception crossing back into the GTK main loop terminates the process, so every
    // callback swallows and reports the drag as unhandled.

    [UnmanagedCallersOnly]
    static uint OnEnter(IntPtr target, double x, double y, IntPtr userData)
    {
        try
        {
            if (Find(userData) is not { } self)
                return 0;

            var files = self.Current();
            if (!self.host.WouldAccept(files))
                return 0;

            self.host.NotifyEnter(self.window, files, new Point(x, y));
            return Gtk4DropInterop.ActionCopy;
        }
        catch
        {
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    static uint OnMotion(IntPtr target, double x, double y, IntPtr userData)
    {
        try
        {
            if (Find(userData) is not { } self)
                return 0;

            var files = self.Current();
            if (!self.host.WouldAccept(files))
                return 0;

            self.host.NotifyOver(self.window, files, new Point(x, y));
            return Gtk4DropInterop.ActionCopy;
        }
        catch
        {
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    static void OnLeave(IntPtr target, IntPtr userData)
    {
        try
        {
            if (Find(userData) is { } self)
                self.host.NotifyLeave(self.window);
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly]
    static int OnDrop(IntPtr target, IntPtr value, double x, double y, IntPtr userData)
    {
        try
        {
            if (Find(userData) is not { } self)
                return 0;

            var files = Read(value);
            if (files.Count == 0)
                return 0;

            self.host.NotifyDrop(self.window, files, new Point(x, y));
            return 1;
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;

        lock (gate)
            registry.Remove(this.target);

        try
        {
            // Removing the controller drops the widget's reference to it, which is the only one
            // held once gtk_widget_add_controller has taken ownership.
            Gtk4DropInterop.WidgetRemoveController(this.widget, this.target);
        }
        catch (Exception ex)
        {
            this.host.LogError("Removing the GTK drop target failed", ex);
        }
    }
}

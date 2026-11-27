using System.Runtime.InteropServices;

namespace Shiny.Maui.Controls.Desktop.QuickEntry;

/// <summary>
/// The slice of Xlib the popup needs: moving a window the window manager owns, asking for
/// always-on-top, and grabbing keys system-wide.
/// </summary>
/// <remarks>
/// X11 only. Under Wayland none of this exists — a client cannot place its own toplevel, raise
/// itself above others, or grab a key — which is why the Linux backend degrades to a centred,
/// ordinary window there and leans on the desktop portal for hotkeys.
/// </remarks>
static partial class X11Interop
{
    const string X11 = "libX11.so.6";

    public const int KeyPress = 2;
    public const int ClientMessage = 33;
    public const long KeyPressMask = 1L << 0;
    public const int PropModeReplace = 0;
    public const int GrabModeAsync = 1;
    public const int SubstructureNotifyMask = 1 << 19;
    public const int SubstructureRedirectMask = 1 << 20;

    // Lock modifiers must be ORed into every grab or the hotkey silently stops working the moment
    // Caps Lock or Num Lock is on.
    public const uint LockMask = 1 << 1;
    public const uint Mod2Mask = 1 << 4;

    public const uint ShiftMask = 1 << 0;
    public const uint ControlMask = 1 << 2;
    public const uint Mod1Mask = 1 << 3;   // Alt
    public const uint Mod4Mask = 1 << 6;   // Super

    /// <summary>An <c>XEvent</c> is a union sized at 24 machine words; only the fields we read are named.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct XEvent
    {
        public fixed long Pad[24];

        public int Type => (int)this.Pad[0];

        /// <summary>state and keycode of an <c>XKeyEvent</c>, at byte offsets 80 and 84 on LP64.</summary>
        public (uint State, uint KeyCode) AsKey()
        {
            fixed (long* p = this.Pad)
            {
                var bytes = (byte*)p;
                var state = *(uint*)(bytes + 80);
                var keycode = *(uint*)(bytes + 84);
                return (state, keycode);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XClientMessageEvent
    {
        public int Type;
        public IntPtr Serial;
        public int SendEvent;
        public IntPtr Display;
        public IntPtr Window;
        public IntPtr MessageType;
        public int Format;
        public long Data0;
        public long Data1;
        public long Data2;
        public long Data3;
        public long Data4;
        // XSendEvent reads a full XEvent (24 words); anything short of that is a buffer overread.
        public long Pad0, Pad1, Pad2, Pad3, Pad4, Pad5, Pad6, Pad7, Pad8, Pad9, Pad10;
    }

    [LibraryImport(X11, EntryPoint = "XOpenDisplay", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr OpenDisplay(string? name);

    [LibraryImport(X11, EntryPoint = "XCloseDisplay")]
    public static partial int CloseDisplay(IntPtr display);

    [LibraryImport(X11, EntryPoint = "XDefaultRootWindow")]
    public static partial IntPtr DefaultRootWindow(IntPtr display);

    [LibraryImport(X11, EntryPoint = "XInternAtom", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr InternAtom(IntPtr display, string name, [MarshalAs(UnmanagedType.I4)] int onlyIfExists);

    /// <summary>Button 1 (the left button) is down, in <see cref="QueryPointer"/>'s mask.</summary>
    public const uint Button1Mask = 1 << 8;

    [LibraryImport(X11, EntryPoint = "XQueryPointer")]
    [return: MarshalAs(UnmanagedType.I4)]
    public static partial int QueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int windowX, out int windowY, out uint mask);

    [LibraryImport(X11, EntryPoint = "XMoveResizeWindow")]
    public static partial int MoveResizeWindow(IntPtr display, IntPtr window, int x, int y, uint width, uint height);

    [LibraryImport(X11, EntryPoint = "XSendEvent")]
    public static partial int SendEvent(IntPtr display, IntPtr window, [MarshalAs(UnmanagedType.I4)] int propagate, long eventMask, ref XClientMessageEvent evt);

    [LibraryImport(X11, EntryPoint = "XFlush")]
    public static partial int Flush(IntPtr display);

    [LibraryImport(X11, EntryPoint = "XSync")]
    public static partial int Sync(IntPtr display, [MarshalAs(UnmanagedType.I4)] int discard);

    [LibraryImport(X11, EntryPoint = "XStringToKeysym", StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr StringToKeysym(string name);

    [LibraryImport(X11, EntryPoint = "XKeysymToKeycode")]
    public static partial byte KeysymToKeycode(IntPtr display, IntPtr keysym);

    [LibraryImport(X11, EntryPoint = "XGrabKey")]
    public static partial int GrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow, [MarshalAs(UnmanagedType.I4)] int ownerEvents, int pointerMode, int keyboardMode);

    [LibraryImport(X11, EntryPoint = "XUngrabKey")]
    public static partial int UngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow);

    [LibraryImport(X11, EntryPoint = "XSelectInput")]
    public static partial int SelectInput(IntPtr display, IntPtr window, long mask);

    [LibraryImport(X11, EntryPoint = "XNextEvent")]
    public static partial int NextEvent(IntPtr display, out XEvent evt);


    [LibraryImport(X11, EntryPoint = "XPending")]
    public static partial int Pending(IntPtr display);

    [LibraryImport(X11, EntryPoint = "XSetErrorHandler")]
    public static partial IntPtr SetErrorHandler(IntPtr handler);


    [LibraryImport(X11, EntryPoint = "XDefaultScreen")]
    public static partial int DefaultScreen(IntPtr display);

    [LibraryImport(X11, EntryPoint = "XDisplayWidth")]
    public static partial int DisplayWidth(IntPtr display, int screen);

    [LibraryImport(X11, EntryPoint = "XDisplayHeight")]
    public static partial int DisplayHeight(IntPtr display, int screen);

    /// <summary>The window manager can only be asked to raise an already-mapped window through a client message; setting the property directly is only honoured before the first map.</summary>
    public static void SetAlwaysOnTop(IntPtr display, ulong xid) => AddWmState(display, xid, "_NET_WM_STATE_ABOVE");

    /// <summary>
    /// Asks the window manager to add up to two <c>_NET_WM_STATE</c> atoms to a mapped window. The WM
    /// drops the whole property when a window is unmapped, so a window that is hidden and shown again
    /// has to ask again.
    /// </summary>
    public static void AddWmState(IntPtr display, ulong xid, string state, string? secondState = null)
    {
        var stateAtom = InternAtom(display, "_NET_WM_STATE", 0);
        var first = InternAtom(display, state, 0);
        var second = secondState == null ? IntPtr.Zero : InternAtom(display, secondState, 0);
        var root = DefaultRootWindow(display);

        var evt = new XClientMessageEvent
        {
            Type = ClientMessage,
            Display = display,
            Window = (IntPtr)xid,
            MessageType = stateAtom,
            Format = 32,
            Data0 = 1,                    // _NET_WM_STATE_ADD
            Data1 = (long)first,
            Data2 = (long)second,
            Data3 = 1                     // source indication: normal application
        };

        SendEvent(display, root, 0, SubstructureNotifyMask | SubstructureRedirectMask, ref evt);
        Flush(display);
    }
}

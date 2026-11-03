using System.Runtime.InteropServices;

namespace Sample.UITests;

/// <summary>
/// Real mouse clicks on macOS, posted through Core Graphics into the window server - the same path a
/// physical mouse takes, so AppKit's gesture recognizers see them. DevFlow's tap invokes the control's
/// action directly and can't catch a recognizer that swallows the click before the control gets it.
/// </summary>
/// <remarks>
/// Posting events needs the Accessibility permission for whatever runs the tests (Terminal, the IDE,
/// the CI agent). Without it the events are dropped silently, so <see cref="CanPost"/> is checked first.
/// </remarks>
static class MacMouse
{
    const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    const uint OnScreenOnly = 1 << 0;
    const uint ExcludeDesktopElements = 1 << 4;
    const uint LeftMouseDown = 1;
    const uint LeftMouseUp = 2;
    const uint MouseMoved = 5;
    const uint HidEventTap = 0;
    const long NumberSInt64 = 4;
    const uint Utf8 = 0x08000100;

    [StructLayout(LayoutKind.Sequential)]
    struct CGPoint(double x, double y)
    {
        public double X = x;
        public double Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CGRect
    {
        public double X, Y, Width, Height;
    }

    [DllImport(CoreGraphics)] static extern bool CGPreflightPostEventAccess();
    [DllImport(CoreGraphics)] static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);
    [DllImport(CoreGraphics)] static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dict, out CGRect rect);
    [DllImport(CoreGraphics)] static extern IntPtr CGEventCreateMouseEvent(IntPtr source, uint type, CGPoint point, uint button);
    [DllImport(CoreGraphics)] static extern void CGEventPost(uint tap, IntPtr evt);
    [DllImport(CoreFoundation)] static extern long CFArrayGetCount(IntPtr array);
    [DllImport(CoreFoundation)] static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, long index);
    [DllImport(CoreFoundation)] static extern IntPtr CFDictionaryGetValue(IntPtr dict, IntPtr key);
    [DllImport(CoreFoundation)] static extern bool CFNumberGetValue(IntPtr number, long type, out long value);
    [DllImport(CoreFoundation)] static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);
    [DllImport(CoreFoundation)] static extern void CFRelease(IntPtr obj);

    /// <summary>Whether this process may post input events (the Accessibility permission).</summary>
    public static bool CanPost => OperatingSystem.IsMacOS() && CGPreflightPostEventAccess();

    /// <summary>
    /// The frame of the process's main window - its largest normal-level window on screen - in global
    /// points, top-left origin, title bar included.
    /// </summary>
    public static (double X, double Y, double Width, double Height)? MainWindowFrame(int pid)
    {
        var pidKey = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowOwnerPID", Utf8);
        var layerKey = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowLayer", Utf8);
        var boundsKey = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowBounds", Utf8);
        var windows = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktopElements, 0);
        try
        {
            CGRect? best = null;
            for (long i = 0, count = CFArrayGetCount(windows); i < count; i++)
            {
                var window = CFArrayGetValueAtIndex(windows, i);
                if (Number(window, pidKey) != pid || Number(window, layerKey) != 0)
                    continue;
                if (!CGRectMakeWithDictionaryRepresentation(CFDictionaryGetValue(window, boundsKey), out var frame))
                    continue;
                if (best is not { } b || frame.Width * frame.Height > b.Width * b.Height)
                    best = frame;
            }
            return best is { } f ? (f.X, f.Y, f.Width, f.Height) : null;
        }
        finally
        {
            CFRelease(windows);
            CFRelease(pidKey);
            CFRelease(layerKey);
            CFRelease(boundsKey);
        }
    }

    /// <summary>Moves the pointer to a global point and clicks the left button there.</summary>
    public static async Task Click(double x, double y)
    {
        var point = new CGPoint(x, y);
        foreach (var type in new[] { MouseMoved, LeftMouseDown, LeftMouseUp })
        {
            var evt = CGEventCreateMouseEvent(IntPtr.Zero, type, point, 0);
            CGEventPost(HidEventTap, evt);
            CFRelease(evt);
            // AppKit tells a click from a drag by timing; give each event its own beat.
            await Task.Delay(60);
        }
    }

    /// <summary>
    /// Whether the process has a menu open on screen - an NSPopUpButton's or a context menu - i.e. a
    /// window at the pop-up menu level. Read from the window server, so it holds while AppKit's menu
    /// tracking loop has the app's main thread.
    /// </summary>
    public static bool HasOpenMenu(int pid)
    {
        var pidKey = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowOwnerPID", Utf8);
        var layerKey = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowLayer", Utf8);
        var windows = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktopElements, 0);
        try
        {
            for (long i = 0, count = CFArrayGetCount(windows); i < count; i++)
            {
                var window = CFArrayGetValueAtIndex(windows, i);
                if (Number(window, pidKey) == pid && Number(window, layerKey) == PopUpMenuWindowLevel)
                    return true;
            }
            return false;
        }
        finally
        {
            CFRelease(windows);
            CFRelease(pidKey);
            CFRelease(layerKey);
        }
    }

    /// <summary>Presses and releases a key (a virtual key code, e.g. <see cref="KeyDownArrow"/>) in the frontmost app.</summary>
    public static async Task Press(ushort keyCode)
    {
        foreach (var down in new[] { true, false })
        {
            var evt = CGEventCreateKeyboardEvent(IntPtr.Zero, keyCode, down);
            CGEventPost(HidEventTap, evt);
            CFRelease(evt);
            await Task.Delay(60);
        }
    }

    public const ushort KeyReturn = 36;
    public const ushort KeyEscape = 53;
    public const ushort KeyDownArrow = 125;

    // kCGPopUpMenuWindowLevel
    const long PopUpMenuWindowLevel = 101;

    [DllImport(CoreGraphics)] static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort keyCode, bool keyDown);

    static long? Number(IntPtr dict, IntPtr key)
    {
        var value = CFDictionaryGetValue(dict, key);
        return value != IntPtr.Zero && CFNumberGetValue(value, NumberSInt64, out var n) ? n : null;
    }
}

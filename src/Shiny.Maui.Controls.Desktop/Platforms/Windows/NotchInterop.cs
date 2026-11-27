using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Shiny.Maui.Controls.Desktop.Notch;

/// <summary>The Win32 the notch window needs on top of what the quick entry popup already declares.</summary>
[SupportedOSPlatform("windows")]
static unsafe partial class NotchInterop
{
    public const int GWL_STYLE = -16;
    public const long WS_CAPTION = 0x00C00000;
    public const long WS_THICKFRAME = 0x00040000;
    public const long WS_SYSMENU = 0x00080000;
    public const long WS_POPUP = 0x80000000;

    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_FRAMECHANGED = 0x0020;

    public const uint MONITOR_DEFAULTTOPRIMARY = 1;
    public const uint MONITORINFOF_PRIMARY = 1;
    /// <summary>MDT_EFFECTIVE_DPI: the DPI the user's scale setting gives the monitor.</summary>
    public const int MDT_EFFECTIVE_DPI = 0;

    /// <summary>MONITORINFOEXW: <see cref="QuickEntry.QuickEntryInterop.MONITORINFO"/> plus the monitor's device name.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFOEX
    {
        public uint cbSize;
        public QuickEntry.QuickEntryInterop.RECT rcMonitor;
        public QuickEntry.QuickEntryInterop.RECT rcWork;
        public uint dwFlags;
        public fixed char szDevice[32];
    }

    /// <summary>DISPLAY_DEVICEW, for the monitor's own name.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAY_DEVICE
    {
        public uint cb;
        public fixed char DeviceName[32];
        public fixed char DeviceString[128];
        public uint StateFlags;
        public fixed char DeviceID[128];
        public fixed char DeviceKey[128];
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, delegate* unmanaged<IntPtr, IntPtr, QuickEntry.QuickEntryInterop.RECT*, IntPtr, int> callback, IntPtr data);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDisplayDevices(string device, uint index, ref DISPLAY_DEVICE info, uint flags);

    public const int VK_LBUTTON = 0x01;

    /// <summary>The high bit is set while the key (or mouse button) is down.</summary>
    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int key);

    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    /// <summary>DWMWCP_DONOTROUND: the window region is the outline, so DWM must not round it a second time.</summary>
    public const int DWMWCP_DONOTROUND = 1;

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [LibraryImport("gdi32.dll")]
    public static partial IntPtr CreateRectRgn(int left, int top, int right, int bottom);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(IntPtr obj);

    /// <summary>On success the system owns the region; the caller must not delete it.</summary>
    [LibraryImport("user32.dll")]
    public static partial int SetWindowRgn(IntPtr hwnd, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hwnd, out QuickEntry.QuickEntryInterop.RECT rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr hwnd);
}

using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Microsoft.UI.Windowing;
using Shiny.Maui.Controls.Desktop.QuickEntry;
using Win32 = Shiny.Maui.Controls.Desktop.QuickEntry.QuickEntryInterop;
using WinUIContentControl = Microsoft.UI.Xaml.Controls.ContentControl;
using WinUIPanel = Microsoft.UI.Xaml.Controls.Panel;
using WinUIThickness = Microsoft.UI.Xaml.Thickness;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace Shiny.Maui.Controls.Desktop.Notch;

/// <summary>
/// WinUI 3 backing for the notch window: a borderless popup pinned topmost at the top-centre of the
/// primary monitor, kept out of the taskbar and Alt+Tab, and never activated — clicking it does not
/// take focus from the app you are using.
/// </summary>
/// <remarks>
/// <para>
/// There is no camera notch to find on Windows, so the notch is always a floating island below the top
/// of the work area (so it clears a taskbar docked at the top).
/// </para>
/// <para>
/// A WinUI window cannot be see-through, so the window is clipped to the notch's outline with a window
/// region, re-cut on every frame of a morph, and the page behind the notch paints the surface colour.
/// Region edges are not antialiased, which is the price of not having transparency, and a
/// <see cref="NotchView.Stroke"/> is clipped away with the corners.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
static class NotchWindowPlatform
{
    /// <summary>Windows has no animated window resize to hand the morph to; the service steps it frame by frame.</summary>
    public static bool AnimatesFrame => false;

    public static bool IsTransparent => false;

    public static NotchGeometry GetGeometry() => NotchGeometry.None;

    public static void Initialize(object platformWindow, NotchWindowOptions options)
    {
        if (platformWindow is WinUIWindow window)
            QuickEntryPlatform.BeginInvokeOnMainThread(() => InitializeCore(window));
    }

    static void InitializeCore(WinUIWindow window)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var appWindow = GetAppWindow(window, hwnd);

        if (appWindow?.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        if (appWindow != null)
            appWindow.IsShownInSwitchers = false;

        // A caption or sizing frame also brings Windows' minimum tracking size with it, which is wider
        // than the empty notch: the window would refuse to get as small as the island.
        var style = (long)Win32.GetWindowLongPtr(hwnd, NotchInterop.GWL_STYLE);
        style &= ~(NotchInterop.WS_CAPTION | NotchInterop.WS_THICKFRAME | NotchInterop.WS_SYSMENU);
        style |= NotchInterop.WS_POPUP;
        Win32.SetWindowLongPtr(hwnd, NotchInterop.GWL_STYLE, (IntPtr)style);

        // TOOLWINDOW keeps it out of the taskbar and Alt+Tab; NOACTIVATE means a click on it is
        // delivered without pulling focus away from whatever app the user is typing in.
        var ex = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
        ex |= Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE;
        ex &= ~(long)Win32.WS_EX_APPWINDOW;
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, (IntPtr)ex);

        Win32.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0, 0, 0, 0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | NotchInterop.SWP_NOZORDER | Win32.SWP_NOACTIVATE | NotchInterop.SWP_FRAMECHANGED
        );

        var corner = NotchInterop.DWMWCP_DONOTROUND;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, in corner, sizeof(int));

        // MAUI gives every window a Mica backdrop; the page paints the surface colour instead.
        window.SystemBackdrop = null;
        CollapseTitleBar(window);

        Win32.ShowWindow(hwnd, Win32.SW_HIDE);
    }

    public static void Place(object platformWindow, double width, double height, CornerRadius corners, bool hugNotch, double topMargin, bool coverMenuBar, double durationMs)
    {
        if (platformWindow is not WinUIWindow window)
            return;

        QuickEntryPlatform.BeginInvokeOnMainThread(() =>
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

            // Window positions are physical pixels; the notch measures in DIPs.
            var dpi = Win32.GetDpiForWindow(hwnd);
            var scale = dpi <= 0 ? 1d : dpi / 96d;
            var w = Math.Max(1, (int)Math.Round(width * scale));
            var h = Math.Max(1, (int)Math.Round(height * scale));

            var work = PrimaryWorkArea();
            var x = work.Left + ((work.Right - work.Left - w) / 2);
            var y = work.Top + (int)Math.Round(topMargin * scale);

            Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, x, y, w, h, Win32.SWP_NOACTIVATE);
            Clip(hwnd, w, h, corners, scale);
        });
    }

    public static void Show(object platformWindow)
    {
        if (platformWindow is not WinUIWindow window)
            return;

        QuickEntryPlatform.BeginInvokeOnMainThread(() =>
        {
            // Again here: the root view's template is only applied once the window has content on
            // screen, and applying it can bring the title-bar strip back.
            CollapseTitleBar(window);

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            Win32.SetWindowPos(
                hwnd,
                Win32.HWND_TOPMOST,
                0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW
            );
        });
    }

    /// <summary>
    /// Nothing to freeze here: the window is resized once per frame and lays out each time, so the new
    /// content simply fades in as the notch morphs. (On macOS the old content collapses with it.)
    /// </summary>
    public static void FreezeContent(object platformWindow) { }

    public static void Hide(object platformWindow)
    {
        if (platformWindow is WinUIWindow window)
            QuickEntryPlatform.BeginInvokeOnMainThread(() => Win32.ShowWindow(WinRT.Interop.WindowNative.GetWindowHandle(window), Win32.SW_HIDE));
    }

    public static void Teardown(object platformWindow) => Hide(platformWindow);

    /// <summary>
    /// Polls the cursor against the window. A poll rather than XAML pointer events because every content
    /// swap builds a new page, which would need re-hooking each time, and because it reports the same
    /// way whether or not this app is the active one.
    /// </summary>
    public static IDisposable TrackPointer(object platformWindow, Action<bool> changed)
    {
        if (platformWindow is not WinUIWindow window || Application.Current?.Dispatcher is not { } dispatcher)
            return new PointerPoll(null);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var last = false;

        var timer = dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(50);
        timer.IsRepeating = true;
        timer.Tick += (_, _) =>
        {
            // The top edge inflated a couple of pixels, as on macOS: a pointer pushed against the top
            // of the screen should still count as over a notch that touches it.
            var over = NotchInterop.IsWindowVisible(hwnd)
                && Win32.GetCursorPos(out var p)
                && NotchInterop.GetWindowRect(hwnd, out var r)
                && p.X >= r.Left && p.X < r.Right
                && p.Y >= r.Top - 2 && p.Y < r.Bottom;

            if (over == last)
                return;

            last = over;
            changed(over);
        };
        timer.Start();
        return new PointerPoll(timer);
    }

    sealed class PointerPoll(IDispatcherTimer? timer) : IDisposable
    {
        public void Dispose() => timer?.Stop();
    }

    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Cuts the window to the notch outline. A notch-shaped one (square top corners) is cut from a round
    /// rectangle that starts a radius above the window, so only its bottom corners land inside it.
    /// </summary>
    static void Clip(IntPtr hwnd, int width, int height, CornerRadius corners, double scale)
    {
        var r = (int)Math.Round(Math.Max(corners.BottomLeft, corners.BottomRight) * scale);
        var squareTop = corners.TopLeft <= 0 && corners.TopRight <= 0;

        // The right and bottom coordinates are exclusive, hence the +1.
        var region = r <= 0
            ? NotchInterop.CreateRectRgn(0, 0, width, height)
            : NotchInterop.CreateRoundRectRgn(0, squareTop ? -r : 0, width + 1, height + 1, r * 2, r * 2);

        if (region == IntPtr.Zero)
            return;

        // The system owns the region once it is set; it is ours to free only if it was refused.
        if (NotchInterop.SetWindowRgn(hwnd, region, true) == 0)
            NotchInterop.DeleteObject(region);
    }

    /// <summary>
    /// MAUI's root view reserves a title-bar strip across the top of every window's content — the
    /// height of a real title bar, more than the empty notch is tall — and only exposes the switch that
    /// clears it internally. Missing on a MAUI that renamed it, the content just sits lower.
    /// </summary>
    static void CollapseTitleBar(WinUIWindow window)
    {
        if (FindRootView(window.Content) is not { } root)
            return;

        try
        {
            UpdateAppTitleBar(root, 0, false, new WinUIThickness(0));
        }
        catch (MissingMethodException)
        {
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "UpdateAppTitleBar")]
    static extern void UpdateAppTitleBar(Microsoft.Maui.Platform.WindowRootView root, int appTitleBarHeight, bool useCustomAppTitleBar, WinUIThickness margin);

    static Microsoft.Maui.Platform.WindowRootView? FindRootView(object? element) => element switch
    {
        Microsoft.Maui.Platform.WindowRootView root => root,
        WinUIPanel panel => panel.Children.Select(FindRootView).FirstOrDefault(r => r != null),
        WinUIContentControl content => FindRootView(content.Content),
        _ => null
    };

    static Win32.RECT PrimaryWorkArea()
    {
        var monitor = Win32.MonitorFromPoint(default, NotchInterop.MONITOR_DEFAULTTOPRIMARY);
        var info = new Win32.MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        if (monitor != IntPtr.Zero && Win32.GetMonitorInfo(monitor, ref info))
            return info.rcWork;

        return new Win32.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
    }

    static AppWindow? GetAppWindow(WinUIWindow window, IntPtr hwnd)
    {
        try
        {
            return AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));
        }
        catch
        {
            return window.AppWindow;
        }
    }
}

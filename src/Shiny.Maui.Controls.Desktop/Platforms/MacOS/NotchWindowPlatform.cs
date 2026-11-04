using AppKit;
using CoreAnimation;
using CoreGraphics;
using Foundation;
using Shiny.Maui.Controls.Desktop.TrayIcon;

namespace Shiny.Maui.Controls.Desktop.Notch;

/// <summary>
/// AppKit backing for the notch window.
/// </summary>
/// <remarks>
/// Unlike the quick entry popup this window is genuinely borderless. It never needs keyboard focus, so
/// the title-bar trick that one relies on buys nothing — and a titled window is constrained to sit below
/// the menu bar, which is exactly where the camera notch is not.
/// </remarks>
static class NotchWindowPlatform
{
    /// <summary>The window animates its own frame (see <see cref="Place"/>), so the service hands it the whole morph.</summary>
    public static bool AnimatesFrame => true;

    /// <summary>A clear, non-opaque NSWindow shows the notch's own rounded outline, so nothing has to clip it.</summary>
    public static bool IsTransparent => true;

    /// <summary>
    /// The notch on the primary display, if it has one (the built-in display, undocked), measured from the
    /// menu-bar areas either side of it. Falls back to <see cref="NotchGeometry.None"/> when it has none
    /// and on macOS before 12, which predates the API.
    /// </summary>
    public static NotchGeometry GetGeometry() => MacMainThread.Invoke(() =>
    {
        if (!OperatingSystem.IsMacOSVersionAtLeast(12))
            return NotchGeometry.None;

        var screen = NotchScreen();
        if (screen == null)
            return NotchGeometry.None;

        var top = screen.SafeAreaInsets.Top;
        if (top <= 0)
            return NotchGeometry.None;

        var left = screen.AuxiliaryTopLeftArea;
        var right = screen.AuxiliaryTopRightArea;
        var width = screen.Frame.Width - left.Width - right.Width;
        return width > 0 ? new NotchGeometry(true, width, top) : NotchGeometry.None;
    });

    public static void Initialize(object platformWindow, NotchWindowOptions options)
    {
        if (platformWindow is not NSWindow window)
            return;

        MacMainThread.Invoke(() =>
        {
            window.StyleMask = NSWindowStyle.Borderless;
            window.IsOpaque = false;
            window.BackgroundColor = NSColor.Clear;
            window.HasShadow = false;
            window.MovableByWindowBackground = false;

            // Explicitly NO, not left at its default. The default lets clicks fall through any part of a
            // non-opaque window the window server thinks is transparent, and with layer-backed MAUI
            // content that was all of it: a click on a button in the notch landed on the app behind.
            // The window is sized to the notch surface, so taking every click costs nothing.
            window.IgnoresMouseEvents = false;
            window.HidesOnDeactivate = false;

            // Above the menu bar (MainMenu), so the notch can sit over it and reach into the cut-out.
            window.Level = NSWindowLevel.Status;

            var behavior = NSWindowCollectionBehavior.Stationary | NSWindowCollectionBehavior.IgnoresCycle;
            if (options.JoinAllSpaces)
                behavior |= NSWindowCollectionBehavior.CanJoinAllSpaces | NSWindowCollectionBehavior.FullScreenAuxiliary;
            window.CollectionBehavior = behavior;
            window.SetExcludedFromWindowsMenu(true);

            if (window.ContentView is { } content)
            {
                content.WantsLayer = true;
                if (content.Layer != null)
                    content.Layer.BackgroundColor = NSColor.Clear.CGColor;
            }

            // The first click after using another app. Clicking an inactive app's window makes the window
            // server activate the app, and AppKit spends that mouse-down on the activation: the view never
            // sees it, so a play button took two clicks. Handing the mouse-down to the window ourselves
            // doesn't help - the activation still happens and the view still drops it. What works is to
            // let AppKit drop it, remember it, and replay it to the window just before its own mouse-up,
            // by which point the app is active. Replaying at the mouse-up rather than on a timer keeps
            // down-before-up even for an instant click.
            NSEvent? pending = null;
            var handle = window.Handle;
            RemoveClickMonitor(handle);
            var monitor = NSEvent.AddLocalMonitorForEventsMatchingMask(
                NSEventMask.LeftMouseDown | NSEventMask.LeftMouseUp | NSEventMask.RightMouseDown | NSEventMask.RightMouseUp,
                e =>
                {
                    if (e.Window != window)
                        return e;

                    var isDown = e.Type is NSEventType.LeftMouseDown or NSEventType.RightMouseDown;
                    if (isDown)
                    {
                        pending = NSApplication.SharedApplication.Active ? null : e;
                        return e;
                    }

                    if (pending != null)
                    {
                        var down = pending;
                        pending = null;
                        window.SendEvent(down);
                    }
                    return e;
                });

            // The monitor's block holds the window, and AppKit holds the block until it is removed. The
            // service removes it on dispose; this covers the window being closed some other way first.
            var closeObserver = NSNotificationCenter.DefaultCenter.AddObserver(
                NSWindow.WillCloseNotification,
                _ => RemoveClickMonitor(handle),
                window
            );
            ClickMonitors[handle] = (monitor, closeObserver);

            window.OrderOut(null);
        });
    }

    static readonly Dictionary<IntPtr, (NSObject Monitor, NSObject CloseObserver)> ClickMonitors = new();

    // Main thread only, like every caller.
    static void RemoveClickMonitor(IntPtr handle)
    {
        if (!ClickMonitors.Remove(handle, out var entry))
            return;

        NSEvent.RemoveMonitor(entry.Monitor);
        NSNotificationCenter.DefaultCenter.RemoveObserver(entry.CloseObserver);
    }

    /// <summary>
    /// NotchView's own gentle "back" ease (overshoots ~3% and settles), as a cubic Bézier. That ease
    /// is a cubic in t, and a Bézier whose x control points sit at 1/3 and 2/3 is too, so these
    /// points reproduce it exactly: f(0)=0, f'(0)=3.9, f(1)=1, f'(1)=0.
    /// </summary>
    /// <summary>Holds near opaque for most of the morph, then drops away: the frozen old content's fade.</summary>
    static readonly CAMediaTimingFunction FadeOutTiming = CAMediaTimingFunction.FromControlPoints(0.7f, 0f, 0.9f, 0.6f);

    static readonly CAMediaTimingFunction MorphTiming = CAMediaTimingFunction.FromControlPoints(1f / 3f, 1.3f, 2f / 3f, 1f);

    public static void Place(object platformWindow, double width, double height, CornerRadius corners, bool hugNotch, double topMargin, bool coverMenuBar, double durationMs)
    {
        if (platformWindow is not NSWindow window)
            return;

        MacMainThread.Invoke(() =>
        {
            var frame = FrameFor(width, height, hugNotch, topMargin, coverMenuBar);
            Frozen.TryGetValue(window.Handle, out var frozen);
            if (durationMs <= 0 || !window.IsVisible)
            {
                window.SetFrame(frame, true);
                Thaw(window.Handle);
                return;
            }

            // This morph owns the overlay now; one that started earlier mustn't remove it when it ends.
            var generation = 0;
            if (frozen.Overlay != null)
                Frozen[window.Handle] = (frozen.Overlay, generation = ++frozenGeneration);

            // Animated through the window's animator rather than SetFrame(…, animate: true): that one's
            // duration is fixed by animationResizeTime, which only a subclass can change, and MAUI owns
            // the window class. Every step of the animation is a real resize, which is what makes AppKit
            // lay the content out as the notch grows.
            NSAnimationContext.RunAnimation(ctx =>
            {
                ctx.Duration = durationMs / 1000d;
                ctx.TimingFunction = MorphTiming;
                ((NSWindow)window.Animator).SetFrame(frame, true);

                // The frozen old content morphs with the notch - clipped as it collapses - and fades out
                // as the new content fades in.
                if (frozen.Overlay is { } overlay)
                {
                    ((NSWindow)overlay.Animator).SetFrame(frame, true);

                    // Late, though: the new content may draw no surface of its own (a notch that is just
                    // the camera), and an image fading as fast as it shrinks reads as a ghost, not a
                    // collapse. So it stays solid most of the way and goes at the end.
                    NSAnimationContext.RunAnimation(fade =>
                    {
                        fade.Duration = durationMs / 1000d;
                        fade.TimingFunction = FadeOutTiming;
                        ((NSWindow)overlay.Animator).AlphaValue = 0;
                    }, () => { });
                }
            }, () =>
            {
                // A later morph may have taken over this overlay, or a later swap replaced it.
                if (frozen.Overlay != null && Frozen.TryGetValue(window.Handle, out var now) && now.Generation == generation)
                    Thaw(window.Handle);
            });
        });
    }

    static readonly Dictionary<IntPtr, (NSWindow? Overlay, int Generation)> Frozen = new();
    static int frozenGeneration;

    /// <summary>
    /// Freezes what the notch shows now into an image, in a click-through window over it, before its
    /// content is swapped. The next <see cref="Place"/> morphs that window with the notch and fades it
    /// out, so the old content collapses (or grows) with the notch rather than vanishing the moment the
    /// new content arrives.
    /// </summary>
    /// <remarks>
    /// The image is pinned to the top centre and never scaled, so as the overlay shrinks it is clipped
    /// exactly as the notch is. It is a window of its own, a child of the notch's, rather than a view in
    /// it: the swap replaces the notch's whole content view tree.
    /// </remarks>
    public static void FreezeContent(object platformWindow)
    {
        if (platformWindow is not NSWindow window)
            return;

        MacMainThread.Invoke(() =>
        {
            Thaw(window.Handle);
            if (!window.IsVisible || window.ContentView is not { } view || view.Bounds.Width <= 0 || view.Bounds.Height <= 0)
                return;

            var bounds = view.Bounds;
            if (view.BitmapImageRepForCachingDisplayInRect(bounds) is not { } bitmap)
                return;
            view.CacheDisplay(bounds, bitmap);
            var image = new NSImage(bounds.Size);
            image.AddRepresentation(bitmap);

            var overlay = new NSWindow(window.Frame, NSWindowStyle.Borderless, NSBackingStore.Buffered, false)
            {
                IsOpaque = false,
                BackgroundColor = NSColor.Clear,
                HasShadow = false,
                IgnoresMouseEvents = true,
                Level = window.Level,
                CollectionBehavior = window.CollectionBehavior,
                ContentView = new NSImageView(new CGRect(CGPoint.Empty, bounds.Size))
                {
                    Image = image,
                    ImageScaling = NSImageScale.None,
                    ImageAlignment = NSImageAlignment.Top,
                    AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable
                }
            };
            // .NET holds the reference; AppKit releasing it as well on Close would free it twice.
            overlay.ReleaseWhenClosed(false);
            overlay.SetExcludedFromWindowsMenu(true);
            window.AddChildWindow(overlay, NSWindowOrderingMode.Above);
            Frozen[window.Handle] = (overlay, ++frozenGeneration);
        });
    }

    // Main thread only, like every caller.
    static void Thaw(IntPtr handle)
    {
        if (!Frozen.Remove(handle, out var frozen) || frozen.Overlay is not { } overlay)
            return;

        overlay.ParentWindow?.RemoveChildWindow(overlay);
        overlay.OrderOut(null);
        overlay.Close();
    }

    public static void Show(object platformWindow)
    {
        if (platformWindow is NSWindow window)
            MacMainThread.Invoke(() => window.OrderFrontRegardless());
    }

    public static void Hide(object platformWindow)
    {
        if (platformWindow is NSWindow window)
        {
            MacMainThread.Invoke(() =>
            {
                Thaw(window.Handle);
                window.OrderOut(null);
            });
        }
    }

    public static void Teardown(object platformWindow)
    {
        Hide(platformWindow);
        if (platformWindow is NSWindow window)
        {
            MacMainThread.Invoke(() => RemoveClickMonitor(window.Handle));
        }
    }

    /// <summary>
    /// Reports the pointer moving on and off the window. Event monitors rather than a tracking area: a
    /// tracking area needs an NSResponder of our own to receive mouseEntered, and the content view is
    /// MAUI's. A global monitor sees movement over other apps and a local one over ours; mouse-moved
    /// events need no accessibility permission (only key events do).
    /// </summary>
    public static IDisposable TrackPointer(object platformWindow, Action<bool> changed)
    {
        if (platformWindow is not NSWindow window)
            return new PointerTracking(null, null);

        const NSEventMask mask = NSEventMask.MouseMoved | NSEventMask.LeftMouseDragged | NSEventMask.RightMouseDragged | NSEventMask.OtherMouseDragged;
        var last = false;

        void Check()
        {
            if (!window.IsVisible)
                return;

            // Inflated a little: the window touches the top of the screen, and a pointer pushed against
            // that edge reports a y on the frame's max edge, which Contains() excludes.
            var frame = window.Frame.Inset(-2, -2);
            var over = frame.Contains(NSEvent.CurrentMouseLocation);
            if (over == last)
                return;

            last = over;
            changed(over);
        }

        return MacMainThread.Invoke(() =>
        {
            var global = NSEvent.AddGlobalMonitorForEventsMatchingMask(mask, _ => MacMainThread.Invoke(Check));
            var local = NSEvent.AddLocalMonitorForEventsMatchingMask(mask, e =>
            {
                Check();
                return e;
            });
            Check();
            return new PointerTracking(global, local);
        });
    }

    sealed class PointerTracking(NSObject? global, NSObject? local) : IDisposable
    {
        public void Dispose() => MacMainThread.Invoke(() =>
        {
            if (global != null)
                NSEvent.RemoveMonitor(global);
            if (local != null)
                NSEvent.RemoveMonitor(local);
        });
    }

    /// <summary>
    /// Top-centre of the notch screen. Hugging the notch, the top edge is the screen's own top edge —
    /// over the menu bar, inside the cut-out; otherwise it sits <paramref name="topMargin"/> below the
    /// menu bar, or below the screen's top edge (over the menu bar) with <paramref name="coverMenuBar"/>.
    /// AppKit's origin is bottom-left, so the y is the top minus the height.
    /// </summary>
    static CGRect FrameFor(double width, double height, bool hugNotch, double topMargin, bool coverMenuBar)
    {
        var screen = (hugNotch ? NotchScreen() : null) ?? PrimaryScreen();
        var w = (nfloat)width;
        var h = (nfloat)height;
        var x = screen.Frame.X + ((screen.Frame.Width - w) / 2);
        var top = hugNotch
            ? screen.Frame.Y + screen.Frame.Height
            : coverMenuBar
                ? screen.Frame.Y + screen.Frame.Height - (nfloat)topMargin
                : screen.VisibleFrame.Y + screen.VisibleFrame.Height - (nfloat)topMargin;
        return new CGRect(Math.Round(x), Math.Round(top - h), w, h);
    }

    /// <summary>
    /// The primary display - the one chosen as main in Displays settings (Screens[0], whose origin is
    /// 0,0) - which is where the notch always lives. Not <see cref="NSScreen.MainScreen"/>: that is the
    /// screen with the key window, so with two monitors the notch would follow whichever one was clicked
    /// in last. Docked with the lid shut, this is the main monitor; undocked, the built-in display.
    /// </summary>
    static NSScreen PrimaryScreen() => NSScreen.Screens.FirstOrDefault() ?? NSScreen.MainScreen!;

    /// <summary>
    /// The primary display, if it has a camera notch. A notched built-in display that isn't primary (the
    /// lid open beside a main monitor) is passed over, so the notch stays on the main display.
    /// </summary>
    static NSScreen? NotchScreen()
    {
        if (OperatingSystem.IsMacOSVersionAtLeast(12) && NSScreen.Screens.FirstOrDefault() is { } primary && primary.SafeAreaInsets.Top > 0)
            return primary;
        return null;
    }
}

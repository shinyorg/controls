#if IOS || MACCATALYST
using Foundation;
using GameController;
using Shiny.Controls.Keyboard;
using Shiny.Maui.Controls;
using UIKit;
using MauiWindow = Microsoft.Maui.Controls.Window;

// Compiled into the core package for iOS and linked into the Desktop add-on for Mac Catalyst —
// core has no Catalyst target — so the namespace follows whichever assembly it lands in.
#if MACCATALYST
namespace Shiny.Maui.Controls.Desktop.KeyboardShortcuts;
#else
namespace Shiny.Maui.Controls;
#endif

/// <summary>
/// Hardware-keyboard key source for iOS, iPadOS and Mac Catalyst, read through GameController's
/// <c>GCKeyboard</c>.
/// </summary>
/// <remarks>
/// <para>
/// UIKit only delivers key presses to a responder in the chain, and the responders there belong to
/// MAUI — the window, its root view controller, the focused control. Overriding
/// <c>pressesBegan</c> or adding <c>UIKeyCommand</c>s would mean subclassing types the app does not
/// create. <c>GCKeyboard</c> sees every key on any connected hardware keyboard without any of that.
/// </para>
/// <para>
/// The trade-off: it <em>observes</em>. A shortcut fires, but the key still reaches the focused
/// control too, and the system sends no auto-repeat. In practice that matters little, because the
/// text-field rule keeps plain keys away from shortcuts while a field has focus.
/// </para>
/// </remarks>
static class GameControllerKeySource
{
    static readonly object gate = new();
    static readonly HashSet<nint> pressed = new();
    static bool installed;
    static NSObject? connectObserver;

    sealed class NoOp : IDisposable
    {
        public static readonly NoOp Instance = new();
        public void Dispose() { }
    }

    /// <summary>The keyboard is app-wide; the first window to need it installs the hook and the rest share it.</summary>
    public static IDisposable? Attach(MauiWindow window, object platformWindow)
    {
        Install();
        return NoOp.Instance;
    }

    static void Install()
    {
        lock (gate)
        {
            if (installed)
                return;

            installed = true;
        }

        if (!OperatingSystem.IsIOSVersionAtLeast(14) && !OperatingSystem.IsMacCatalystVersionAtLeast(14))
            return;

        if (GCKeyboard.CoalescedKeyboard is { } keyboard)
            Hook(keyboard);

        // A keyboard connected later (Bluetooth, a Smart Connector cover) arrives as a notification.
        connectObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            GCKeyboard.DidConnectNotification,
            n =>
            {
                if (n.Object is GCKeyboard connected)
                    Hook(connected);
            });
    }

    static void Hook(GCKeyboard keyboard)
    {
        if (keyboard.KeyboardInput is not { } input)
            return;

        input.KeyChangedHandler = (source, _, keyCode, isPressed) =>
        {
            try
            {
                OnKey(source, keyCode, isPressed);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Shiny.KeyboardShortcuts] Key handling failed: {ex}");
            }
        };
    }

    static void OnKey(GCKeyboardInput input, nint keyCode, bool isPressed)
    {
        bool repeat;
        lock (gate)
            repeat = isPressed && !pressed.Add(keyCode);

        if (!isPressed)
        {
            lock (gate)
                pressed.Remove(keyCode);
        }

        var modifiers = KeyModifiers.None;
        if (IsDown(input, GCKeyCode.LeftControl) || IsDown(input, GCKeyCode.RightControl)) modifiers |= KeyModifiers.Control;
        if (IsDown(input, GCKeyCode.LeftAlt) || IsDown(input, GCKeyCode.RightAlt)) modifiers |= KeyModifiers.Alt;
        if (IsDown(input, GCKeyCode.LeftShift) || IsDown(input, GCKeyCode.RightShift)) modifiers |= KeyModifiers.Shift;
        if (IsDown(input, GCKeyCode.LeftGui) || IsDown(input, GCKeyCode.RightGui)) modifiers |= KeyModifiers.Meta;

        var code = NativeKeyCodes.FromHidUsage((int)keyCode);
        var stroke = new KeyStroke
        {
            Code = code,
            // GameController reports positions only. The US character is the best available guess,
            // and is only consulted for character gestures like "?".
            Character = NativeKeyCodes.UsCharacter(code, modifiers.HasFlag(KeyModifiers.Shift)),
            Modifiers = modifiers,
            IsKeyUp = !isPressed,
            IsRepeat = repeat,
            IsTextInputFocused = IsTextInputFocused()
        };

        var window = FindKeyWindow();
        UIApplication.SharedApplication.BeginInvokeOnMainThread(() => KeyboardShortcutManager.Process(stroke, window));
    }

    static bool IsDown(GCKeyboardInput input, nint key) => input.GetButton(key)?.IsPressed ?? false;

    static MauiWindow? FindKeyWindow()
    {
        if (Application.Current is not { } app)
            return null;

        foreach (var window in app.Windows)
        {
            if (window.Handler?.PlatformView is UIWindow { IsKeyWindow: true })
                return window;
        }

        return app.Windows.FirstOrDefault();
    }

    static bool IsTextInputFocused()
    {
        // UITextInput covers UITextField, UITextView and the content view inside a WKWebView, so a
        // focused BlazorWebView counts as typing too — plain keys belong to the page in there.
        var window = FindKeyWindow()?.Handler?.PlatformView as UIWindow;
        return window != null && FindFirstResponder(window) is IUITextInput;
    }

    static UIView? FindFirstResponder(UIView view)
    {
        if (view.IsFirstResponder)
            return view;

        foreach (var subview in view.Subviews)
        {
            if (FindFirstResponder(subview) is { } found)
                return found;
        }

        return null;
    }
}
#endif

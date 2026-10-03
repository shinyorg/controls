using AppKit;
using Foundation;
using Shiny.Controls.Keyboard;
using Shiny.Maui.Controls;
using Shiny.Maui.Controls.Desktop.TrayIcon;
using WebKit;
using MauiWindow = Microsoft.Maui.Controls.Window;

namespace Shiny.Maui.Controls.Desktop.KeyboardShortcuts;

/// <summary>
/// AppKit key source for keyboard shortcuts.
/// </summary>
/// <remarks>
/// <para>
/// One <em>local</em> event monitor for the whole app, installed with the first window. A local
/// monitor sees every key event routed to this process before the window dispatches it — before
/// the focused text view and before the main menu's key equivalents — needs no accessibility
/// permission, and swallows an event by returning null. Events are routed to the MAUI window whose
/// <c>NSWindow</c> they belong to; a key in a window nobody attached is left alone.
/// </para>
/// <para>
/// Losing key status releases held keys. That is observed through
/// <c>NSWindowDidResignKeyNotification</c> rather than the <c>DidResignKey</c> event, because MAUI's
/// window handler owns the delegate and the event accessor throws when one is already set.
/// </para>
/// </remarks>
static class KeyboardShortcutPlatform
{
    static readonly Dictionary<IntPtr, Registration> registry = new();
    static NSObject? monitor;

    public static IDisposable? Attach(MauiWindow window, object platformWindow)
    {
        if (platformWindow is not NSWindow native)
            return null;

        var registration = new Registration(window, native);
        MacMainThread.Invoke(() =>
        {
            registry[native.Handle] = registration;
            registration.ResignObserver = NSNotificationCenter.DefaultCenter.AddObserver(
                NSWindow.DidResignKeyNotification,
                _ => KeyboardShortcutManager.Engine.ReleaseAll(window),
                native
            );

            monitor ??= NSEvent.AddLocalMonitorForEventsMatchingMask(NSEventMask.KeyDown | NSEventMask.KeyUp, OnEvent);
        });

        return registration;
    }

    static NSEvent OnEvent(NSEvent ev)
    {
        try
        {
            if (ev.Window is not { } native || !registry.TryGetValue(native.Handle, out var registration))
                return ev;

            var flags = ev.ModifierFlags;
            var modifiers = KeyModifiers.None;
            if (flags.HasFlag(NSEventModifierMask.ControlKeyMask)) modifiers |= KeyModifiers.Control;
            if (flags.HasFlag(NSEventModifierMask.AlternateKeyMask)) modifiers |= KeyModifiers.Alt;
            if (flags.HasFlag(NSEventModifierMask.ShiftKeyMask)) modifiers |= KeyModifiers.Shift;
            if (flags.HasFlag(NSEventModifierMask.CommandKeyMask)) modifiers |= KeyModifiers.Meta;

            var isUp = ev.Type == NSEventType.KeyUp;

            // charactersIgnoringModifiers keeps Shift but drops Control/Option/Command — exactly the
            // "what does this key type" the engine wants. Option+letter would otherwise turn "a"
            // into "å" and lose the letter.
            var characters = ev.CharactersIgnoringModifiers;
            var stroke = new KeyStroke
            {
                Code = NativeKeyCodes.FromMacKeyCode(ev.KeyCode),
                Character = characters is { Length: > 0 } && !Char.IsControl(characters[0]) && !IsFunctionKeyCharacter(characters[0]) ? characters : null,
                Modifiers = modifiers,
                IsKeyUp = isUp,
                IsRepeat = !isUp && ev.IsARepeat,
                IsTextInputFocused = IsTextInput(native.FirstResponder)
            };

            // null swallows the event: the window never sees it, and nothing beeps.
            return KeyboardShortcutManager.Process(stroke, registration.Window) ? null! : ev;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Shiny.KeyboardShortcuts] AppKit key handling failed: {ex}");
            return ev;
        }
    }

    /// <summary>AppKit reports arrows and function keys as characters in the private-use range.</summary>
    static bool IsFunctionKeyCharacter(char c) => c is >= '' and <= '';

    static bool IsTextInput(NSResponder? responder)
    {
        // A focused NSTextField edits through the window's shared field editor, an NSTextView — so
        // NSText covers single-line fields, editors and text views alike. Inside a WKWebView the
        // first responder is the web view itself; treat it as typing, because the page may well be.
        for (var r = responder; r != null; r = r.NextResponder)
        {
            if (r is NSText or WKWebView)
                return true;

            if (r is NSWindow)
                break;
        }

        return false;
    }

    sealed class Registration(MauiWindow window, NSWindow native) : IDisposable
    {
        public MauiWindow Window { get; } = window;
        public NSObject? ResignObserver { get; set; }

        public void Dispose()
        {
            MacMainThread.Invoke(() =>
            {
                registry.Remove(native.Handle);
                if (this.ResignObserver != null)
                {
                    NSNotificationCenter.DefaultCenter.RemoveObserver(this.ResignObserver);
                    this.ResignObserver = null;
                }

                if (registry.Count == 0 && monitor != null)
                {
                    NSEvent.RemoveMonitor(monitor);
                    monitor = null;
                }
            });
        }
    }
}

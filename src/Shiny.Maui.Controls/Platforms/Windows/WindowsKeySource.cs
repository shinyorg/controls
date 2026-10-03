using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Shiny.Controls.Keyboard;
using Windows.System;
using Windows.UI.Core;
using MauiWindow = Microsoft.Maui.Controls.Window;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace Shiny.Maui.Controls;

/// <summary>
/// WinUI key source for keyboard shortcuts.
/// </summary>
/// <remarks>
/// <para>
/// Listens with <c>PreviewKeyDown</c>/<c>PreviewKeyUp</c> on the window's root element. Preview
/// events tunnel from the root down, so the shortcut gets first refusal before the focused control
/// — which is what lets <c>Ctrl+B</c> work inside a <c>TextBox</c>. The handlers are added with
/// <c>handledEventsToo</c> so a control that marks keys handled cannot hide them.
/// </para>
/// <para>
/// Keys typed into a <c>WebView2</c> (a <c>BlazorWebView</c>) never enter the XAML tree; the
/// Blazor <c>KeyboardShortcuts</c> component covers that content instead.
/// </para>
/// </remarks>
sealed class WindowsKeySource : IDisposable
{
    readonly MauiWindow window;
    readonly WinUIWindow native;
    readonly KeyEventHandler down;
    readonly KeyEventHandler up;
    UIElement? root;

    WindowsKeySource(MauiWindow window, WinUIWindow native)
    {
        this.window = window;
        this.native = native;
        this.down = (_, e) => this.OnKey(e, isUp: false);
        this.up = (_, e) => this.OnKey(e, isUp: true);

        this.native.Activated += this.OnActivated;
        this.Hook();
    }

    public static IDisposable? Attach(MauiWindow window, object platformWindow)
        => platformWindow is WinUIWindow native ? new WindowsKeySource(window, native) : null;

    void Hook()
    {
        if (this.root != null || this.native.Content is not UIElement content)
            return;

        this.root = content;
        content.AddHandler(UIElement.PreviewKeyDownEvent, this.down, true);
        content.AddHandler(UIElement.PreviewKeyUpEvent, this.up, true);
    }

    void OnActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs e)
    {
        // MAUI sets the root content before the first activation, but not necessarily before the
        // handler hands us the window.
        this.Hook();

        if (e.WindowActivationState == WindowActivationState.Deactivated)
            KeyboardShortcutManager.Engine.ReleaseAll(this.window);
    }

    void OnKey(KeyRoutedEventArgs e, bool isUp)
    {
        var modifiers = KeyModifiers.None;
        if (IsDown(VirtualKey.Control)) modifiers |= KeyModifiers.Control;
        if (IsDown(VirtualKey.Menu)) modifiers |= KeyModifiers.Alt;
        if (IsDown(VirtualKey.Shift)) modifiers |= KeyModifiers.Shift;
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) modifiers |= KeyModifiers.Meta;

        var vk = (int)e.Key;
        var stroke = new KeyStroke
        {
            Code = NativeKeyCodes.FromWindowsVirtualKey(vk),
            Character = ToCharacter(vk, e.KeyStatus.ScanCode, modifiers.HasFlag(KeyModifiers.Shift)),
            Modifiers = modifiers,
            IsKeyUp = isUp,
            IsRepeat = !isUp && e.KeyStatus.WasKeyDown,
            // Windows reports AltGr as Ctrl + right Alt.
            IsAltGraph = IsDown(VirtualKey.RightMenu) && modifiers.HasFlag(KeyModifiers.Control),
            IsTextInputFocused = this.IsTextInputFocused()
        };

        if (KeyboardShortcutManager.Process(stroke, this.window))
            e.Handled = true;
    }

    bool IsTextInputFocused()
    {
        if (this.root?.XamlRoot is not { } xamlRoot)
            return false;

        return FocusManager.GetFocusedElement(xamlRoot) is TextBox or PasswordBox or RichEditBox or WebView2;
    }

    static bool IsDown(VirtualKey key)
        => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    /// <summary>
    /// The character the key types with Shift applied and Control/Alt ignored, from the active
    /// layout — so a <c>?</c> gesture works on whatever key types <c>?</c>.
    /// </summary>
    static string? ToCharacter(int vk, uint scanCode, bool shift)
    {
        var state = new byte[256];
        if (shift)
            state[0x10] = 0x80;

        var buffer = new char[4];
        try
        {
            // Flag 0x4: leave the kernel's keyboard state alone, so a dead key the user is halfway
            // through composing is not consumed by us looking at it.
            var count = ToUnicode((uint)vk, scanCode, state, buffer, buffer.Length, 0x4);
            return count > 0 && !Char.IsControl(buffer[0]) ? new string(buffer, 0, count) : null;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    static extern int ToUnicode(uint virtualKey, uint scanCode, byte[] keyState, [Out] char[] buffer, int bufferSize, uint flags);

    public void Dispose()
    {
        this.native.Activated -= this.OnActivated;
        if (this.root != null)
        {
            this.root.RemoveHandler(UIElement.PreviewKeyDownEvent, this.down);
            this.root.RemoveHandler(UIElement.PreviewKeyUpEvent, this.up);
            this.root = null;
        }
    }
}

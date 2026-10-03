using Android.App;
using Android.Views;
using Android.Widget;
using Shiny.Controls.Keyboard;
using MauiWindow = Microsoft.Maui.Controls.Window;

namespace Shiny.Maui.Controls;

/// <summary>
/// Android key source for keyboard shortcuts — hardware keyboards, Chromebooks, DeX.
/// </summary>
/// <remarks>
/// Fed from MAUI's <c>OnKeyDown</c>/<c>OnKeyUp</c> activity lifecycle events, registered once by
/// <c>UseShinyControls()</c>. Android offers the activity a key only after the focused view has
/// declined it, so an <c>EditText</c> gets first refusal: plain typing and the editing keys it
/// understands (<c>Ctrl+A/C/V/X/Z</c>) stay with it, and everything else reaches the shortcuts.
/// </remarks>
static class AndroidKeySource
{
    sealed class NoOp : IDisposable
    {
        public static readonly NoOp Instance = new();
        public void Dispose() { }
    }

    /// <summary>Per-window attach. The hook is activity-wide and installed at startup, so there is nothing to do per window.</summary>
    public static IDisposable? Attach(MauiWindow window, object platformWindow) => NoOp.Instance;

    /// <param name="sender">The activity — MAUI's key lifecycle delegates type it as <c>object</c>.</param>
    public static bool OnKey(object sender, Keycode keyCode, KeyEvent? e, bool isUp)
    {
        if (e == null || sender is not Activity activity)
            return false;

        var modifiers = KeyModifiers.None;
        if (e.IsCtrlPressed) modifiers |= KeyModifiers.Control;
        if (e.IsAltPressed) modifiers |= KeyModifiers.Alt;
        if (e.IsShiftPressed) modifiers |= KeyModifiers.Shift;
        if (e.IsMetaPressed) modifiers |= KeyModifiers.Meta;

        var unicode = e.GetUnicodeChar(e.MetaState & (MetaKeyStates.ShiftMask | MetaKeyStates.CapsLockOn));
        var stroke = new KeyStroke
        {
            Code = NativeKeyCodes.FromAndroidKeycode((int)keyCode),
            Character = unicode > 0 && !Char.IsControl((char)unicode) ? Char.ConvertFromUtf32(unicode) : null,
            Modifiers = modifiers,
            IsKeyUp = isUp,
            IsRepeat = !isUp && e.RepeatCount > 0,
            IsTextInputFocused = activity.CurrentFocus is EditText or Android.Webkit.WebView
        };

        return KeyboardShortcutManager.Process(stroke, KeyboardShortcutManager.FindWindow(activity));
    }
}

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// The keys a canvas editor's hidden entry never sees as text.
/// </summary>
/// <remarks>
/// The editors empty their hidden entry after every character, so Backspace on it deletes nothing and
/// raises no <c>TextChanged</c> - Android's keyboard reports it as a bare DEL key event instead, and so
/// does a hardware keyboard, along with the arrows. Without this, Backspace did nothing at all on
/// Android. Keys the entry could act on itself (it holds text, e.g. mid-composition) are left to it.
/// </remarks>
static class HiddenInputKeys
{
    public static void Attach(Entry input, Func<EditorKey, bool, bool, bool> handleKey)
    {
#if ANDROID
        input.HandlerChanged += (_, _) =>
        {
            if (input.Handler?.PlatformView is not Android.Widget.EditText native)
                return;

            native.KeyPress += (_, e) =>
            {
                e.Handled = false;
                if (e.Event is not { Action: Android.Views.KeyEventActions.Down } key)
                    return;

                var empty = string.IsNullOrEmpty(native.Text);
                var shift = key.IsShiftPressed;
                var control = key.IsCtrlPressed;

                EditorKey? mapped = e.KeyCode switch
                {
                    Android.Views.Keycode.Del when empty => EditorKey.Backspace,
                    Android.Views.Keycode.ForwardDel when empty => EditorKey.Delete,
                    Android.Views.Keycode.DpadLeft when empty => EditorKey.Left,
                    Android.Views.Keycode.DpadRight when empty => EditorKey.Right,
                    Android.Views.Keycode.DpadUp => EditorKey.Up,
                    Android.Views.Keycode.DpadDown => EditorKey.Down,
                    Android.Views.Keycode.MoveHome => EditorKey.Home,
                    Android.Views.Keycode.MoveEnd => EditorKey.End,
                    Android.Views.Keycode.Tab => EditorKey.Tab,
                    _ => null
                };

                if (mapped is { } k)
                    e.Handled = handleKey(k, shift, control);
            };
        };
#else
        _ = input;
        _ = handleKey;
#endif
    }
}

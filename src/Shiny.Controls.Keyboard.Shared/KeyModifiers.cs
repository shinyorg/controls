namespace Shiny.Controls.Keyboard;

/// <summary>
/// The modifier keys a shortcut requires.
/// </summary>
/// <remarks>
/// <para>
/// Modifiers match <b>exactly</b>: a shortcut for <c>Control+B</c> does not fire for
/// <c>Control+Shift+B</c>. Leaving a modifier out means "must not be held", so there is no
/// separate "false" to set.
/// </para>
/// <para>
/// <see cref="Primary"/> is the one to reach for in cross-platform code. It is <see cref="Meta"/>
/// (⌘) on Apple platforms and <see cref="Control"/> everywhere else, which is what users on each
/// platform expect <c>Save</c> to be — writing <c>Control</c> gives Mac users <c>⌃S</c>, which no
/// Mac app uses.
/// </para>
/// </remarks>
[Flags]
public enum KeyModifiers
{
    None = 0,

    /// <summary>Ctrl on every platform — ⌃ on a Mac, which is <em>not</em> where Mac shortcuts live. See <see cref="Primary"/>.</summary>
    Control = 1,

    /// <summary>Alt, or ⌥ Option on a Mac.</summary>
    Alt = 2,

    /// <summary>Shift.</summary>
    Shift = 4,

    /// <summary>⌘ Command on Apple platforms, the Windows key on Windows, Super on Linux.</summary>
    Meta = 8,

    /// <summary>
    /// The platform's main shortcut modifier: <see cref="Meta"/> (⌘) on Apple platforms,
    /// <see cref="Control"/> everywhere else. Resolved when the shortcut is matched, never stored on
    /// a key stroke.
    /// </summary>
    Primary = 16
}

/// <summary>
/// Which platform family a shortcut is being matched or displayed for. It decides what
/// <see cref="KeyModifiers.Primary"/> means and how a gesture is written out.
/// </summary>
public enum KeyboardPlatform
{
    /// <summary>Anything not listed — treated like Windows (Ctrl as the primary modifier).</summary>
    Other,

    Windows,

    /// <summary>macOS, iOS, iPadOS and Mac Catalyst — ⌘ is the primary modifier.</summary>
    Apple,

    Linux,

    Android
}

/// <summary>
/// Whether a shortcut fires while the user is typing in a text field.
/// </summary>
public enum TextInputBehavior
{
    /// <summary>
    /// Fire only when the gesture cannot be ordinary typing: it holds Control, Alt or Meta, or its
    /// key is Escape or a function key. <c>J</c>, <c>Shift+J</c> and <c>?</c> stay with the text
    /// field; <c>Ctrl+B</c> fires.
    /// </summary>
    Auto,

    /// <summary>Fire even while a text field has focus — the field never sees the key.</summary>
    Always,

    /// <summary>Never fire while a text field has focus.</summary>
    Never
}

public static class KeyModifiersExtensions
{
    /// <summary>
    /// Replace <see cref="KeyModifiers.Primary"/> with the concrete modifier it stands for on
    /// <paramref name="platform"/>.
    /// </summary>
    public static KeyModifiers Resolve(this KeyModifiers modifiers, KeyboardPlatform platform)
    {
        if ((modifiers & KeyModifiers.Primary) == 0)
            return modifiers;

        var primary = platform == KeyboardPlatform.Apple ? KeyModifiers.Meta : KeyModifiers.Control;
        return (modifiers & ~KeyModifiers.Primary) | primary;
    }
}

using Shiny.Controls.Keyboard;

namespace Shiny.Maui.Controls;

/// <summary>
/// Keyboard shortcuts from code — app-wide ones that live as long as you keep the registration, the
/// list of what is currently reachable for a cheat sheet, and the platform's way of writing a gesture.
/// </summary>
/// <remarks>
/// Registered by <c>UseShinyControls()</c>. Shortcuts declared in XAML through
/// <see cref="KeyboardShortcuts"/> and shortcuts registered here run on the same engine and follow
/// the same precedence: app-wide registrations sit beneath every page and view, and a modal element
/// blocks them too.
/// </remarks>
public interface IKeyboardShortcutService
{
    /// <summary>
    /// True when a native key source exists for this platform: always on Windows, Android and iOS;
    /// on macOS (AppKit), Linux (GTK4) and Mac Catalyst only once the Desktop add-on's
    /// <c>UseDesktopKeyboardShortcuts()</c> has been called.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>The platform gestures are resolved and written for — it decides what <see cref="KeyModifiers.Primary"/> means.</summary>
    KeyboardPlatform Platform { get; }

    /// <summary>How long a sequence like <c>Ctrl+K, Ctrl+C</c> waits for its next chord. Two seconds by default.</summary>
    TimeSpan ChordTimeout { get; set; }

    /// <summary>True while the first chord of a sequence has been pressed and the rest is awaited.</summary>
    bool IsChordPending { get; }

    /// <summary>The chords pressed so far in a pending sequence — for a "⌘K was pressed, waiting for the next key" hint.</summary>
    IReadOnlyList<KeyChord> PendingChords { get; }

    /// <summary>Raised when a sequence starts waiting, completes, is abandoned or times out.</summary>
    event EventHandler? ChordStateChanged;

    /// <summary>Raised after any shortcut fires, wherever it was declared.</summary>
    event EventHandler<KeyboardShortcutEventArgs>? ShortcutInvoked;

    /// <summary>
    /// Register a shortcut. Dispose the result to remove it.
    /// </summary>
    /// <param name="gesture">Gesture syntax — <c>"Primary+Shift+P"</c>, <c>"Ctrl+K, Ctrl+C"</c>. Throws <see cref="FormatException"/> when it is not one.</param>
    /// <param name="pressed">What to do. Runs on the UI thread.</param>
    /// <param name="configure">Set the rest of the binding — <c>Released</c>, <c>AllowRepeat</c>, <c>TextInput</c>, <c>Description</c>.</param>
    /// <param name="window">Only in this window. Null (the default) means every window, at the lowest precedence.</param>
    IDisposable Register(string gesture, Action<KeyboardShortcutEventArgs> pressed, Action<KeyboardShortcutBinding>? configure = null, Window? window = null);

    /// <summary>Register a binding built yourself. Dispose the result to remove it.</summary>
    IDisposable Register(KeyboardShortcutBinding binding, Window? window = null);

    /// <summary>
    /// Every shortcut reachable right now in <paramref name="window"/> (the app's first window when
    /// null), highest precedence first — the content of a "press ? for shortcuts" sheet.
    /// </summary>
    IReadOnlyList<ActiveShortcut> GetActiveShortcuts(Window? window = null);

    /// <summary>A gesture written the way this platform writes it — <c>Ctrl+Shift+P</c> or <c>⇧⌘P</c>.</summary>
    string Format(string gesture);

    /// <inheritdoc cref="Format(string)"/>
    string Format(KeyGesture gesture);
}

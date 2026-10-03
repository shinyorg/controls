using Shiny.Controls.Keyboard;

namespace Shiny.Blazor.Controls;

/// <summary>
/// Keyboard shortcuts for the browser — the service behind <see cref="KeyboardShortcuts"/> and
/// <see cref="KeyboardShortcut"/>, usable directly for shortcuts registered in code.
/// </summary>
/// <remarks>
/// <para>
/// One <c>keydown</c>/<c>keyup</c> listener on the window, in the capture phase, decides in
/// JavaScript whether a key presses a shortcut. That is not an optimisation: <c>preventDefault</c>
/// has to be decided synchronously, before the browser acts on the key, and Blazor Server cannot
/// reach .NET synchronously at all. Only a match crosses the interop boundary, so typing costs no
/// round trips.
/// </para>
/// <para>
/// Scoped: each browser tab (each circuit under Blazor Server) gets its own.
/// </para>
/// </remarks>
public interface IKeyboardShortcutService : IAsyncDisposable
{
    /// <summary>
    /// The platform the browser runs on, which decides what <see cref="KeyModifiers.Primary"/>
    /// means and how gestures are written. <see cref="KeyboardPlatform.Other"/> until the JS module
    /// has started and reported it — see <see cref="PlatformChanged"/>.
    /// </summary>
    KeyboardPlatform Platform { get; }

    /// <summary>Raised once the browser's platform is known, so displayed gestures can re-render as <c>⌘K</c> rather than <c>Ctrl+K</c>.</summary>
    event EventHandler? PlatformChanged;

    /// <summary>True once the JS listener is running.</summary>
    bool IsRunning { get; }

    /// <summary>How long a sequence like <c>Ctrl+K, Ctrl+C</c> waits for its next chord. Two seconds by default.</summary>
    TimeSpan ChordTimeout { get; set; }

    /// <summary>True while the first chord of a sequence has been pressed and the rest is awaited.</summary>
    bool IsChordPending { get; }

    /// <summary>The chords pressed so far in a pending sequence.</summary>
    IReadOnlyList<KeyChord> PendingChords { get; }

    /// <summary>Raised when a sequence starts waiting, completes, is abandoned or times out.</summary>
    event EventHandler? ChordStateChanged;

    /// <summary>Raised after any shortcut fires.</summary>
    event EventHandler<KeyboardShortcutEventArgs>? ShortcutInvoked;

    /// <summary>
    /// Start the JS listener. Any <see cref="KeyboardShortcuts"/> or <see cref="KeyboardShortcut"/>
    /// on the page does this after its first render; call it yourself (from
    /// <c>OnAfterRenderAsync</c>) when every shortcut is registered in code.
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Register an app-wide shortcut at the lowest precedence. Dispose the result to remove it.
    /// Throws <see cref="FormatException"/> for a gesture that is not one.
    /// </summary>
    IDisposable Register(string gesture, Action<KeyboardShortcutEventArgs> pressed, Action<KeyboardShortcutBinding>? configure = null);

    /// <summary>Register a binding built yourself. Dispose the result to remove it.</summary>
    IDisposable Register(KeyboardShortcutBinding binding);

    /// <summary>Every shortcut on the page that can fire, highest precedence first — for a cheat sheet.</summary>
    IReadOnlyList<ActiveShortcut> GetActiveShortcuts();

    /// <summary>A gesture written the way this platform writes it — <c>Ctrl+Shift+P</c> or <c>⇧⌘P</c>.</summary>
    string Format(string gesture);

    /// <inheritdoc cref="Format(string)"/>
    string Format(KeyGesture gesture);
}

namespace Shiny.Controls.Keyboard;

/// <summary>
/// One physical key going down or up, already translated out of the platform's native event.
/// </summary>
/// <remarks>
/// Each host's job is only to fill this in honestly. Every decision about what the stroke means —
/// which shortcut, if any, it presses — is made by <see cref="KeyboardShortcutEngine"/>, so the
/// rules cannot drift between Windows, AppKit, GTK, Android, iOS and the browser.
/// </remarks>
public sealed record KeyStroke
{
    /// <summary>
    /// The physical key as a W3C <c>code</c> name (<c>KeyA</c>, <c>Digit1</c>, <c>ArrowUp</c>), or
    /// null when the platform could not say. See <see cref="NativeKeyCodes"/>.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>
    /// The character the key types with Shift applied but Control, Alt and Meta ignored — <c>a</c>,
    /// <c>A</c>, <c>?</c>, <c>ф</c> — or null for a key that types nothing.
    /// </summary>
    public string? Character { get; init; }

    /// <summary>The modifiers held. Never contains <see cref="KeyModifiers.Primary"/>.</summary>
    public KeyModifiers Modifiers { get; init; }

    /// <summary>True when this down is the operating system's auto-repeat of a key already held.</summary>
    public bool IsRepeat { get; init; }

    /// <summary>True for the key coming back up.</summary>
    public bool IsKeyUp { get; init; }

    /// <summary>True when a text field (entry, editor, contenteditable) has keyboard focus.</summary>
    public bool IsTextInputFocused { get; init; }

    /// <summary>
    /// True when the right Alt key is acting as AltGr. Windows reports AltGr as Control+Alt, so on
    /// a German layout <c>AltGr+E</c> — the euro sign — would otherwise press a <c>Ctrl+Alt+E</c>
    /// shortcut while the user is typing.
    /// </summary>
    public bool IsAltGraph { get; init; }

    /// <summary>The key that identifies this stroke for repeat and release tracking.</summary>
    internal string Identity => this.Code ?? this.Character?.ToUpperInvariant() ?? String.Empty;

    /// <inheritdoc/>
    public override string ToString()
        => $"{(this.IsKeyUp ? "up" : "down")} {this.Modifiers} {this.Code ?? "?"} '{this.Character}'{(this.IsRepeat ? " (repeat)" : "")}";
}

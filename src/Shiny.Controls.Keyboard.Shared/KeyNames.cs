namespace Shiny.Controls.Keyboard;

/// <summary>
/// How a gesture's key is compared against a key stroke. Decided once, when the gesture is parsed.
/// </summary>
public enum KeyKind
{
    /// <summary>
    /// <c>A</c>–<c>Z</c>. Matched against the letter the key <em>types</em>, so <c>Ctrl+Z</c> on an
    /// AZERTY keyboard is the key printed Z. On a layout whose letters are not Latin (Cyrillic,
    /// Greek, Hebrew…) it falls back to the physical position, which is what users of those layouts
    /// expect shortcuts to follow.
    /// </summary>
    Letter,

    /// <summary>
    /// <c>0</c>–<c>9</c> on the main row, matched by physical position. On AZERTY the digits are
    /// shifted, so matching the typed character would make <c>Ctrl+1</c> unreachable.
    /// </summary>
    Digit,

    /// <summary>
    /// A key that types nothing — Escape, the arrows, F1–F24, the numeric keypad — or a punctuation
    /// key named by its US position (<c>Slash</c>, <c>BracketLeft</c>). Matched by physical key.
    /// </summary>
    Code,

    /// <summary>
    /// A single typed character such as <c>?</c> or <c>+</c>. Matched against the character the
    /// stroke produces, with Shift ignored — because Shift is how the character gets typed in the
    /// first place, and on which key it lives depends on the layout.
    /// </summary>
    Character
}

/// <summary>
/// The key vocabulary: the names a gesture may use, the physical key codes strokes report, and how
/// both are written out for a person to read.
/// </summary>
/// <remarks>
/// Physical keys use the W3C UI Events <c>code</c> names (<c>KeyA</c>, <c>Digit1</c>,
/// <c>ArrowUp</c>, <c>NumpadEnter</c>) on every host, because the browser already reports them and
/// they describe a position rather than a layout. The native tables in <see cref="NativeKeyCodes"/>
/// translate into the same names.
/// </remarks>
public static class KeyNames
{
    /// <summary>Keys that type nothing, by their canonical name (which is also their physical code).</summary>
    static readonly HashSet<string> namedKeys = new(StringComparer.Ordinal)
    {
        "Escape", "Enter", "Tab", "Space", "Backspace", "Delete", "Insert",
        "Home", "End", "PageUp", "PageDown",
        "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight",
        "ContextMenu", "PrintScreen", "Pause", "ScrollLock", "CapsLock", "NumLock",
        "Numpad0", "Numpad1", "Numpad2", "Numpad3", "Numpad4",
        "Numpad5", "Numpad6", "Numpad7", "Numpad8", "Numpad9",
        "NumpadAdd", "NumpadSubtract", "NumpadMultiply", "NumpadDivide", "NumpadDecimal", "NumpadEnter",
        "NumpadEqual",
        "Minus", "Equal", "BracketLeft", "BracketRight", "Backslash", "Semicolon", "Quote",
        "Comma", "Period", "Slash", "Backquote", "IntlBackslash",
        "MediaPlayPause", "MediaStop", "MediaTrackNext", "MediaTrackPrevious",
        "AudioVolumeUp", "AudioVolumeDown", "AudioVolumeMute"
    };

    static readonly Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Esc"] = "Escape",
        ["Return"] = "Enter",
        ["Del"] = "Delete",
        ["Ins"] = "Insert",
        ["Back"] = "Backspace",
        ["Spacebar"] = "Space",
        ["Up"] = "ArrowUp",
        ["Down"] = "ArrowDown",
        ["Left"] = "ArrowLeft",
        ["Right"] = "ArrowRight",
        ["PgUp"] = "PageUp",
        ["PgDn"] = "PageDown",
        ["PageDn"] = "PageDown",
        ["Menu"] = "ContextMenu",
        ["Apps"] = "ContextMenu",
        ["PrtSc"] = "PrintScreen",
        ["Break"] = "Pause",
        ["Hyphen"] = "Minus",
        ["Equals"] = "Equal",
        ["OpenBracket"] = "BracketLeft",
        ["CloseBracket"] = "BracketRight",
        ["Apostrophe"] = "Quote",
        ["Grave"] = "Backquote",
        ["Tilde"] = "Backquote",
        ["Dot"] = "Period"
    };

    /// <summary>Spelled-out names for characters that are awkward or impossible in a gesture string.</summary>
    static readonly Dictionary<string, string> characterAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Plus"] = "+",
        ["Question"] = "?",
        ["QuestionMark"] = "?",
        ["Asterisk"] = "*",
        ["Star"] = "*"
    };

    /// <summary>
    /// Turn one key token from a gesture string into its canonical name and how it is matched.
    /// Case-insensitive; accepts the aliases a person is likely to type (<c>Esc</c>, <c>Up</c>,
    /// <c>PgDn</c>, <c>Plus</c>).
    /// </summary>
    public static bool TryNormalize(string? token, out string key, out KeyKind kind)
    {
        key = String.Empty;
        kind = KeyKind.Code;

        if (String.IsNullOrEmpty(token))
            return false;

        // A literal space is the Space key, not whitespace to trim away.
        if (token == " ")
        {
            key = "Space";
            return true;
        }

        token = token.Trim();
        if (token.Length == 0)
            return false;

        if (token.Length == 1)
        {
            var c = token[0];
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
            {
                key = Char.ToUpperInvariant(c).ToString();
                kind = KeyKind.Letter;
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                key = token;
                kind = KeyKind.Digit;
                return true;
            }

            if (Char.IsControl(c) || Char.IsWhiteSpace(c))
                return false;

            key = token;
            kind = KeyKind.Character;
            return true;
        }

        // "KeyA" / "Digit1" — the physical codes are accepted too, so a gesture can be copied
        // straight out of a browser's KeyboardEvent.code.
        if (token.Length == 4 && token.StartsWith("Key", StringComparison.OrdinalIgnoreCase) && Char.IsAsciiLetter(token[3]))
        {
            key = Char.ToUpperInvariant(token[3]).ToString();
            kind = KeyKind.Letter;
            return true;
        }

        if (token.Length == 6 && token.StartsWith("Digit", StringComparison.OrdinalIgnoreCase) && Char.IsAsciiDigit(token[5]))
        {
            key = token[5].ToString();
            kind = KeyKind.Digit;
            return true;
        }

        if (IsFunctionKey(token, out var number))
        {
            key = "F" + number;
            return true;
        }

        if (aliases.TryGetValue(token, out var alias))
        {
            key = alias;
            return true;
        }

        foreach (var name in namedKeys)
        {
            if (String.Equals(name, token, StringComparison.OrdinalIgnoreCase))
            {
                key = name;
                return true;
            }
        }

        if (characterAliases.TryGetValue(token, out var character))
        {
            key = character;
            kind = KeyKind.Character;
            return true;
        }

        return false;
    }

    /// <summary>True for <c>F1</c>…<c>F24</c>.</summary>
    public static bool IsFunctionKey(string key, out int number)
    {
        number = 0;
        return key.Length is 2 or 3
            && (key[0] == 'F' || key[0] == 'f')
            && Int32.TryParse(key.AsSpan(1), out number)
            && number is >= 1 and <= 24;
    }

    /// <summary>
    /// The letter a stroke stands for: the ASCII letter it types when there is one, otherwise the
    /// letter printed at its position on a US keyboard.
    /// </summary>
    internal static char? LetterOf(string? code, string? character)
    {
        if (character is { Length: 1 } && Char.IsAsciiLetter(character[0]))
            return Char.ToUpperInvariant(character[0]);

        if (code is { Length: 4 } && code.StartsWith("Key", StringComparison.Ordinal) && Char.IsAsciiLetterUpper(code[3]))
            return code[3];

        return null;
    }

    /// <summary>True when the physical code is a modifier key on its own — those never trigger a shortcut.</summary>
    public static bool IsModifierCode(string? code) => code is
        "ShiftLeft" or "ShiftRight" or "ControlLeft" or "ControlRight" or "AltLeft" or "AltRight" or
        "MetaLeft" or "MetaRight" or "OSLeft" or "OSRight" or "Fn" or "FnLock" or "AltGraph" or "CapsLock";

    // -------------------------------------------------------------------------------------
    // Display

    /// <summary>How a key is written on <paramref name="platform"/> — <c>↑</c>, <c>Esc</c>, <c>⌫</c>.</summary>
    public static string Display(string key, KeyKind kind, KeyboardPlatform platform)
    {
        if (kind is KeyKind.Letter or KeyKind.Digit or KeyKind.Character)
            return key;

        var apple = platform == KeyboardPlatform.Apple;
        return key switch
        {
            "Escape" => apple ? "⎋" : "Esc",
            "Enter" => apple ? "↩" : "Enter",
            "NumpadEnter" => apple ? "⌤" : "Num Enter",
            "Tab" => apple ? "⇥" : "Tab",
            "Space" => apple ? "Space" : "Space",
            "Backspace" => apple ? "⌫" : "Backspace",
            "Delete" => apple ? "⌦" : "Del",
            "Insert" => "Ins",
            "Home" => apple ? "↖" : "Home",
            "End" => apple ? "↘" : "End",
            "PageUp" => apple ? "⇞" : "PgUp",
            "PageDown" => apple ? "⇟" : "PgDn",
            "ArrowUp" => "↑",
            "ArrowDown" => "↓",
            "ArrowLeft" => "←",
            "ArrowRight" => "→",
            "ContextMenu" => "Menu",
            "PrintScreen" => "PrtSc",
            "Minus" => "-",
            "Equal" => "=",
            "BracketLeft" => "[",
            "BracketRight" => "]",
            "Backslash" => "\\",
            "Semicolon" => ";",
            "Quote" => "'",
            "Comma" => ",",
            "Period" => ".",
            "Slash" => "/",
            "Backquote" => "`",
            "NumpadAdd" => "Num +",
            "NumpadSubtract" => "Num -",
            "NumpadMultiply" => "Num *",
            "NumpadDivide" => "Num /",
            "NumpadDecimal" => "Num .",
            _ when key.StartsWith("Numpad", StringComparison.Ordinal) => "Num " + key.Substring(6),
            _ => key
        };
    }

    /// <summary>The modifier symbols/words for <paramref name="platform"/>, in that platform's conventional order.</summary>
    internal static IEnumerable<string> DisplayModifiers(KeyModifiers resolved, KeyboardPlatform platform)
    {
        if (platform == KeyboardPlatform.Apple)
        {
            // Apple's Human Interface Guidelines order: Control, Option, Shift, Command.
            if (resolved.HasFlag(KeyModifiers.Control)) yield return "⌃";
            if (resolved.HasFlag(KeyModifiers.Alt)) yield return "⌥";
            if (resolved.HasFlag(KeyModifiers.Shift)) yield return "⇧";
            if (resolved.HasFlag(KeyModifiers.Meta)) yield return "⌘";
            yield break;
        }

        if (resolved.HasFlag(KeyModifiers.Control)) yield return "Ctrl";
        if (resolved.HasFlag(KeyModifiers.Meta)) yield return platform == KeyboardPlatform.Windows ? "Win" : "Super";
        if (resolved.HasFlag(KeyModifiers.Alt)) yield return "Alt";
        if (resolved.HasFlag(KeyModifiers.Shift)) yield return "Shift";
    }
}

namespace Shiny.Controls.Keyboard;

/// <summary>
/// Native key codes translated into W3C <c>code</c> names, so every host reports the same physical
/// key the same way.
/// </summary>
/// <remarks>
/// Pure tables, kept here rather than in each platform's source so they can be unit-tested on any
/// machine. A key a table does not know comes back null; the stroke then still carries the character
/// it typed, which is enough for letters and character gestures.
/// </remarks>
public static class NativeKeyCodes
{
    // -------------------------------------------------------------------------------------
    // Shared tails

    static readonly string[] letters = Enumerable.Range('A', 26).Select(c => "Key" + (char)c).ToArray();

    static string? Letter(int offset) => offset is >= 0 and < 26 ? letters[offset] : null;

    // -------------------------------------------------------------------------------------
    // Windows virtual-key codes (WinUI VirtualKey has the same values)

    /// <summary>A Windows virtual-key code (<c>VK_*</c>, WinUI <c>VirtualKey</c>).</summary>
    public static string? FromWindowsVirtualKey(int vk) => vk switch
    {
        >= 0x41 and <= 0x5A => Letter(vk - 0x41),
        >= 0x30 and <= 0x39 => "Digit" + (char)vk,
        >= 0x60 and <= 0x69 => "Numpad" + (vk - 0x60),
        >= 0x70 and <= 0x87 => "F" + (vk - 0x6F),
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x13 => "Pause",
        0x14 => "CapsLock",
        0x1B => "Escape",
        0x20 => "Space",
        0x21 => "PageUp",
        0x22 => "PageDown",
        0x23 => "End",
        0x24 => "Home",
        0x25 => "ArrowLeft",
        0x26 => "ArrowUp",
        0x27 => "ArrowRight",
        0x28 => "ArrowDown",
        0x2C => "PrintScreen",
        0x2D => "Insert",
        0x2E => "Delete",
        0x5B => "MetaLeft",
        0x5C => "MetaRight",
        0x5D => "ContextMenu",
        0x6A => "NumpadMultiply",
        0x6B => "NumpadAdd",
        0x6D => "NumpadSubtract",
        0x6E => "NumpadDecimal",
        0x6F => "NumpadDivide",
        0x90 => "NumLock",
        0x91 => "ScrollLock",
        0x10 or 0xA0 => "ShiftLeft",
        0xA1 => "ShiftRight",
        0x11 or 0xA2 => "ControlLeft",
        0xA3 => "ControlRight",
        0x12 or 0xA4 => "AltLeft",
        0xA5 => "AltRight",
        0xAD => "AudioVolumeMute",
        0xAE => "AudioVolumeDown",
        0xAF => "AudioVolumeUp",
        0xB0 => "MediaTrackNext",
        0xB1 => "MediaTrackPrevious",
        0xB2 => "MediaStop",
        0xB3 => "MediaPlayPause",
        // OEM keys, named by their US position. Windows moves the VK with the layout for these, which
        // is close enough for the physical-punctuation gestures; character gestures do not use them.
        0xBA => "Semicolon",
        0xBB => "Equal",
        0xBC => "Comma",
        0xBD => "Minus",
        0xBE => "Period",
        0xBF => "Slash",
        0xC0 => "Backquote",
        0xDB => "BracketLeft",
        0xDC => "Backslash",
        0xDD => "BracketRight",
        0xDE => "Quote",
        0xE2 => "IntlBackslash",
        _ => null
    };

    // -------------------------------------------------------------------------------------
    // macOS virtual key codes (kVK_* in Carbon's Events.h) — physical positions on an ANSI board.

    static readonly Dictionary<int, string> mac = new()
    {
        [0x00] = "KeyA", [0x0B] = "KeyB", [0x08] = "KeyC", [0x02] = "KeyD", [0x0E] = "KeyE",
        [0x03] = "KeyF", [0x05] = "KeyG", [0x04] = "KeyH", [0x22] = "KeyI", [0x26] = "KeyJ",
        [0x28] = "KeyK", [0x25] = "KeyL", [0x2E] = "KeyM", [0x2D] = "KeyN", [0x1F] = "KeyO",
        [0x23] = "KeyP", [0x0C] = "KeyQ", [0x0F] = "KeyR", [0x01] = "KeyS", [0x11] = "KeyT",
        [0x20] = "KeyU", [0x09] = "KeyV", [0x0D] = "KeyW", [0x07] = "KeyX", [0x10] = "KeyY",
        [0x06] = "KeyZ",
        [0x1D] = "Digit0", [0x12] = "Digit1", [0x13] = "Digit2", [0x14] = "Digit3", [0x15] = "Digit4",
        [0x17] = "Digit5", [0x16] = "Digit6", [0x1A] = "Digit7", [0x1C] = "Digit8", [0x19] = "Digit9",
        [0x24] = "Enter", [0x30] = "Tab", [0x31] = "Space", [0x33] = "Backspace", [0x35] = "Escape",
        [0x75] = "Delete", [0x72] = "Insert", [0x73] = "Home", [0x77] = "End",
        [0x74] = "PageUp", [0x79] = "PageDown",
        [0x7B] = "ArrowLeft", [0x7C] = "ArrowRight", [0x7D] = "ArrowDown", [0x7E] = "ArrowUp",
        [0x7A] = "F1", [0x78] = "F2", [0x63] = "F3", [0x76] = "F4", [0x60] = "F5", [0x61] = "F6",
        [0x62] = "F7", [0x64] = "F8", [0x65] = "F9", [0x6D] = "F10", [0x67] = "F11", [0x6F] = "F12",
        [0x69] = "F13", [0x6B] = "F14", [0x71] = "F15", [0x6A] = "F16", [0x40] = "F17", [0x4F] = "F18",
        [0x50] = "F19", [0x5A] = "F20",
        [0x1B] = "Minus", [0x18] = "Equal", [0x21] = "BracketLeft", [0x1E] = "BracketRight",
        [0x2A] = "Backslash", [0x29] = "Semicolon", [0x27] = "Quote", [0x2B] = "Comma",
        [0x2F] = "Period", [0x2C] = "Slash", [0x32] = "Backquote", [0x0A] = "IntlBackslash",
        [0x52] = "Numpad0", [0x53] = "Numpad1", [0x54] = "Numpad2", [0x55] = "Numpad3", [0x56] = "Numpad4",
        [0x57] = "Numpad5", [0x58] = "Numpad6", [0x59] = "Numpad7", [0x5B] = "Numpad8", [0x5C] = "Numpad9",
        [0x45] = "NumpadAdd", [0x4E] = "NumpadSubtract", [0x43] = "NumpadMultiply", [0x4B] = "NumpadDivide",
        [0x41] = "NumpadDecimal", [0x4C] = "NumpadEnter", [0x51] = "NumpadEqual",
        [0x38] = "ShiftLeft", [0x3C] = "ShiftRight", [0x3B] = "ControlLeft", [0x3E] = "ControlRight",
        [0x3A] = "AltLeft", [0x3D] = "AltRight", [0x37] = "MetaLeft", [0x36] = "MetaRight",
        [0x39] = "CapsLock", [0x3F] = "Fn",
        [0x48] = "AudioVolumeUp", [0x49] = "AudioVolumeDown", [0x4A] = "AudioVolumeMute"
    };

    /// <summary>A macOS virtual key code (<c>NSEvent.keyCode</c>, <c>kVK_*</c>).</summary>
    public static string? FromMacKeyCode(int keyCode) => mac.GetValueOrDefault(keyCode);

    // -------------------------------------------------------------------------------------
    // Linux evdev scancodes (linux/input-event-codes.h). X11 and GDK hardware keycodes are these + 8.

    static readonly Dictionary<int, string> evdev = new()
    {
        [1] = "Escape", [2] = "Digit1", [3] = "Digit2", [4] = "Digit3", [5] = "Digit4", [6] = "Digit5",
        [7] = "Digit6", [8] = "Digit7", [9] = "Digit8", [10] = "Digit9", [11] = "Digit0",
        [12] = "Minus", [13] = "Equal", [14] = "Backspace", [15] = "Tab",
        [16] = "KeyQ", [17] = "KeyW", [18] = "KeyE", [19] = "KeyR", [20] = "KeyT", [21] = "KeyY",
        [22] = "KeyU", [23] = "KeyI", [24] = "KeyO", [25] = "KeyP",
        [26] = "BracketLeft", [27] = "BracketRight", [28] = "Enter", [29] = "ControlLeft",
        [30] = "KeyA", [31] = "KeyS", [32] = "KeyD", [33] = "KeyF", [34] = "KeyG", [35] = "KeyH",
        [36] = "KeyJ", [37] = "KeyK", [38] = "KeyL", [39] = "Semicolon", [40] = "Quote", [41] = "Backquote",
        [42] = "ShiftLeft", [43] = "Backslash",
        [44] = "KeyZ", [45] = "KeyX", [46] = "KeyC", [47] = "KeyV", [48] = "KeyB", [49] = "KeyN", [50] = "KeyM",
        [51] = "Comma", [52] = "Period", [53] = "Slash", [54] = "ShiftRight", [55] = "NumpadMultiply",
        [56] = "AltLeft", [57] = "Space", [58] = "CapsLock",
        [59] = "F1", [60] = "F2", [61] = "F3", [62] = "F4", [63] = "F5", [64] = "F6",
        [65] = "F7", [66] = "F8", [67] = "F9", [68] = "F10", [87] = "F11", [88] = "F12",
        [69] = "NumLock", [70] = "ScrollLock",
        [71] = "Numpad7", [72] = "Numpad8", [73] = "Numpad9", [74] = "NumpadSubtract",
        [75] = "Numpad4", [76] = "Numpad5", [77] = "Numpad6", [78] = "NumpadAdd",
        [79] = "Numpad1", [80] = "Numpad2", [81] = "Numpad3", [82] = "Numpad0", [83] = "NumpadDecimal",
        [86] = "IntlBackslash",
        [96] = "NumpadEnter", [97] = "ControlRight", [98] = "NumpadDivide", [99] = "PrintScreen",
        [100] = "AltRight", [102] = "Home", [103] = "ArrowUp", [104] = "PageUp", [105] = "ArrowLeft",
        [106] = "ArrowRight", [107] = "End", [108] = "ArrowDown", [109] = "PageDown",
        [110] = "Insert", [111] = "Delete", [113] = "AudioVolumeMute", [114] = "AudioVolumeDown",
        [115] = "AudioVolumeUp", [117] = "NumpadEqual", [119] = "Pause",
        [125] = "MetaLeft", [126] = "MetaRight", [127] = "ContextMenu",
        [163] = "MediaTrackNext", [164] = "MediaPlayPause", [165] = "MediaTrackPrevious", [166] = "MediaStop",
        [183] = "F13", [184] = "F14", [185] = "F15", [186] = "F16", [187] = "F17", [188] = "F18",
        [189] = "F19", [190] = "F20", [191] = "F21", [192] = "F22", [193] = "F23", [194] = "F24"
    };

    /// <summary>A Linux evdev scancode.</summary>
    public static string? FromEvdev(int scancode) => evdev.GetValueOrDefault(scancode);

    /// <summary>An X11 / GDK hardware keycode — evdev plus 8.</summary>
    public static string? FromXKeycode(int keycode) => FromEvdev(keycode - 8);

    // -------------------------------------------------------------------------------------
    // USB HID keyboard usages (page 0x07) — UIKit's UIKeyboardHIDUsage and GameController's GCKeyCode.

    /// <summary>A USB HID keyboard usage (UIKit <c>UIKeyboardHIDUsage</c>, GameController <c>GCKeyCode</c>).</summary>
    public static string? FromHidUsage(int usage) => usage switch
    {
        >= 0x04 and <= 0x1D => Letter(usage - 0x04),
        >= 0x1E and <= 0x26 => "Digit" + (usage - 0x1D),
        0x27 => "Digit0",
        0x28 => "Enter",
        0x29 => "Escape",
        0x2A => "Backspace",
        0x2B => "Tab",
        0x2C => "Space",
        0x2D => "Minus",
        0x2E => "Equal",
        0x2F => "BracketLeft",
        0x30 => "BracketRight",
        0x31 => "Backslash",
        0x33 => "Semicolon",
        0x34 => "Quote",
        0x35 => "Backquote",
        0x36 => "Comma",
        0x37 => "Period",
        0x38 => "Slash",
        0x39 => "CapsLock",
        >= 0x3A and <= 0x45 => "F" + (usage - 0x39),
        0x46 => "PrintScreen",
        0x47 => "ScrollLock",
        0x48 => "Pause",
        0x49 => "Insert",
        0x4A => "Home",
        0x4B => "PageUp",
        0x4C => "Delete",
        0x4D => "End",
        0x4E => "PageDown",
        0x4F => "ArrowRight",
        0x50 => "ArrowLeft",
        0x51 => "ArrowDown",
        0x52 => "ArrowUp",
        0x53 => "NumLock",
        0x54 => "NumpadDivide",
        0x55 => "NumpadMultiply",
        0x56 => "NumpadSubtract",
        0x57 => "NumpadAdd",
        0x58 => "NumpadEnter",
        >= 0x59 and <= 0x61 => "Numpad" + (usage - 0x58),
        0x62 => "Numpad0",
        0x63 => "NumpadDecimal",
        0x64 => "IntlBackslash",
        0x65 => "ContextMenu",
        0x67 => "NumpadEqual",
        >= 0x68 and <= 0x73 => "F" + (usage - 0x5B),
        0x7F => "AudioVolumeMute",
        0x80 => "AudioVolumeUp",
        0x81 => "AudioVolumeDown",
        0xE0 => "ControlLeft",
        0xE1 => "ShiftLeft",
        0xE2 => "AltLeft",
        0xE3 => "MetaLeft",
        0xE4 => "ControlRight",
        0xE5 => "ShiftRight",
        0xE6 => "AltRight",
        0xE7 => "MetaRight",
        _ => null
    };

    /// <summary>
    /// The character a US keyboard types for a physical key — used by sources that only see key
    /// codes (GameController on iOS), so character gestures like <c>?</c> still work there.
    /// </summary>
    public static string? UsCharacter(string? code, bool shift)
    {
        if (code == null)
            return null;

        if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal))
            return shift ? code.Substring(3) : code.Substring(3).ToLowerInvariant();

        if (code.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal))
        {
            var digit = code[5];
            return shift ? ")!@#$%^&*(".Substring(digit - '0', 1) : digit.ToString();
        }

        return code switch
        {
            "Space" => " ",
            "Minus" => shift ? "_" : "-",
            "Equal" => shift ? "+" : "=",
            "BracketLeft" => shift ? "{" : "[",
            "BracketRight" => shift ? "}" : "]",
            "Backslash" => shift ? "|" : "\\",
            "Semicolon" => shift ? ":" : ";",
            "Quote" => shift ? "\"" : "'",
            "Comma" => shift ? "<" : ",",
            "Period" => shift ? ">" : ".",
            "Slash" => shift ? "?" : "/",
            "Backquote" => shift ? "~" : "`",
            "NumpadAdd" => "+",
            "NumpadSubtract" => "-",
            "NumpadMultiply" => "*",
            "NumpadDivide" => "/",
            "NumpadDecimal" => ".",
            _ when code.StartsWith("Numpad", StringComparison.Ordinal) && code.Length == 7 && Char.IsAsciiDigit(code[6]) => code.Substring(6),
            _ => null
        };
    }

    // -------------------------------------------------------------------------------------
    // Android KeyEvent keycodes

    /// <summary>An Android <c>KeyEvent.KEYCODE_*</c> value.</summary>
    public static string? FromAndroidKeycode(int keycode) => keycode switch
    {
        >= 29 and <= 54 => Letter(keycode - 29),
        >= 7 and <= 16 => "Digit" + (keycode - 7),
        >= 144 and <= 153 => "Numpad" + (keycode - 144),
        >= 131 and <= 142 => "F" + (keycode - 130),
        19 => "ArrowUp",
        20 => "ArrowDown",
        21 => "ArrowLeft",
        22 => "ArrowRight",
        24 => "AudioVolumeUp",
        25 => "AudioVolumeDown",
        55 => "Comma",
        56 => "Period",
        57 => "AltLeft",
        58 => "AltRight",
        59 => "ShiftLeft",
        60 => "ShiftRight",
        61 => "Tab",
        62 => "Space",
        66 => "Enter",
        67 => "Backspace",
        68 => "Backquote",
        69 => "Minus",
        70 => "Equal",
        71 => "BracketLeft",
        72 => "BracketRight",
        73 => "Backslash",
        74 => "Semicolon",
        75 => "Quote",
        76 => "Slash",
        82 => "ContextMenu",
        85 => "MediaPlayPause",
        86 => "MediaStop",
        87 => "MediaTrackNext",
        88 => "MediaTrackPrevious",
        92 => "PageUp",
        93 => "PageDown",
        111 => "Escape",
        112 => "Delete",
        113 => "ControlLeft",
        114 => "ControlRight",
        115 => "CapsLock",
        116 => "ScrollLock",
        117 => "MetaLeft",
        118 => "MetaRight",
        120 => "PrintScreen",
        121 => "Pause",
        122 => "Home",
        123 => "End",
        124 => "Insert",
        143 => "NumLock",
        154 => "NumpadDivide",
        155 => "NumpadMultiply",
        156 => "NumpadSubtract",
        157 => "NumpadAdd",
        158 => "NumpadDecimal",
        160 => "NumpadEnter",
        161 => "NumpadEqual",
        164 => "AudioVolumeMute",
        _ => null
    };
}

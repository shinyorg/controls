using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Shiny.Controls.Keyboard;

/// <summary>
/// One key with its modifiers — a single step of a <see cref="KeyGesture"/>.
/// </summary>
/// <param name="Modifiers">The modifiers that must be held, possibly including <see cref="KeyModifiers.Primary"/>.</param>
/// <param name="Key">The canonical key name — see <see cref="KeyNames.TryNormalize"/>.</param>
/// <param name="Kind">How <paramref name="Key"/> is compared against a stroke.</param>
public sealed record KeyChord(KeyModifiers Modifiers, string Key, KeyKind Kind)
{
    /// <summary>Build a chord from a key name, normalizing it. Throws for a name that is not a key.</summary>
    public static KeyChord Create(KeyModifiers modifiers, string key)
    {
        if (!KeyNames.TryNormalize(key, out var normalized, out var kind))
            throw new FormatException($"'{key}' is not a recognised key name.");

        return new KeyChord(modifiers, normalized, kind);
    }

    /// <summary>
    /// True when this chord, held while a text field has focus, cannot be ordinary typing —
    /// the test behind <see cref="TextInputBehavior.Auto"/>.
    /// </summary>
    public bool IsCommandLike(KeyboardPlatform platform)
    {
        var resolved = this.Modifiers.Resolve(platform);
        if ((resolved & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0)
            return true;

        return this.Kind == KeyKind.Code && (this.Key == "Escape" || KeyNames.IsFunctionKey(this.Key, out _));
    }

    /// <summary>Does <paramref name="stroke"/> press this chord on <paramref name="platform"/>?</summary>
    public bool Matches(KeyStroke stroke, KeyboardPlatform platform)
    {
        var required = this.Modifiers.Resolve(platform);

        switch (this.Kind)
        {
            case KeyKind.Character:
                // Shift is part of how the character is typed, so it is neither required nor refused.
                const KeyModifiers ignoreShift = ~KeyModifiers.Shift;
                return (stroke.Modifiers & ignoreShift) == (required & ignoreShift)
                    && String.Equals(stroke.Character, this.Key, StringComparison.Ordinal);

            case KeyKind.Letter:
                return stroke.Modifiers == required
                    && KeyNames.LetterOf(stroke.Code, stroke.Character) is { } letter
                    && letter == this.Key[0];

            case KeyKind.Digit:
                if (stroke.Modifiers != required)
                    return false;

                // A source that cannot report a physical key still gets the digit it typed.
                return stroke.Code != null
                    ? stroke.Code == "Digit" + this.Key
                    : stroke.Character == this.Key;

            default:
                if (stroke.Modifiers != required)
                    return false;

                if (stroke.Code == this.Key)
                    return true;

                // Enter means either Enter; NumpadEnter is there for whoever needs to tell them apart.
                return this.Key == "Enter" && stroke.Code == "NumpadEnter";
        }
    }

    /// <summary>The chord written for a person on <paramref name="platform"/> — <c>Ctrl+Shift+A</c> or <c>⇧⌘A</c>.</summary>
    public string ToDisplayString(KeyboardPlatform platform)
    {
        var resolved = this.Modifiers.Resolve(platform);
        var key = KeyNames.Display(this.Key, this.Kind, platform);
        var mods = KeyNames.DisplayModifiers(resolved, platform);

        return platform == KeyboardPlatform.Apple
            ? String.Concat(mods) + key
            : String.Join("+", mods.Append(key));
    }

    /// <summary>The chord in gesture syntax, round-trippable through <see cref="KeyGesture.Parse"/>.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        if (this.Modifiers.HasFlag(KeyModifiers.Primary)) sb.Append("Primary+");
        if (this.Modifiers.HasFlag(KeyModifiers.Control)) sb.Append("Ctrl+");
        if (this.Modifiers.HasFlag(KeyModifiers.Alt)) sb.Append("Alt+");
        if (this.Modifiers.HasFlag(KeyModifiers.Shift)) sb.Append("Shift+");
        if (this.Modifiers.HasFlag(KeyModifiers.Meta)) sb.Append("Meta+");
        sb.Append(this.Kind == KeyKind.Character && this.Key == "+" ? "Plus" : this.Key);
        return sb.ToString();
    }
}

/// <summary>
/// A keyboard shortcut: one chord (<c>Ctrl+S</c>), or a sequence of them pressed one after another
/// (<c>Ctrl+K, Ctrl+C</c>).
/// </summary>
/// <remarks>
/// <para>Gesture syntax, case-insensitive:</para>
/// <list type="bullet">
/// <item><description>Modifiers joined with <c>+</c>, the key last: <c>Ctrl+Shift+A</c>, <c>Primary+S</c>, <c>Alt+F4</c>.</description></item>
/// <item><description>Modifier names: <c>Ctrl</c>/<c>Control</c>, <c>Alt</c>/<c>Option</c>/<c>Opt</c>, <c>Shift</c>, <c>Meta</c>/<c>Cmd</c>/<c>Command</c>/<c>Win</c>/<c>Super</c>, and <c>Primary</c>/<c>Mod</c>/<c>CmdOrCtrl</c>.</description></item>
/// <item><description>Keys: a letter or digit, <c>F1</c>–<c>F24</c>, a named key (<c>Esc</c>, <c>Enter</c>, <c>Tab</c>, <c>Space</c>, <c>Up</c>, <c>PageDown</c>, <c>Numpad5</c>…), a physical punctuation key (<c>Slash</c>, <c>BracketLeft</c>), or a single character (<c>?</c>, <c>/</c>, <c>Plus</c> for <c>+</c>).</description></item>
/// <item><description>Chords separated by a comma: <c>Ctrl+K, Ctrl+C</c>, or <c>G, G</c>.</description></item>
/// </list>
/// </remarks>
public sealed class KeyGesture : IEquatable<KeyGesture>
{
    KeyGesture(IReadOnlyList<KeyChord> chords) => this.Chords = chords;

    /// <summary>The chords, in the order they are pressed. Always at least one.</summary>
    public IReadOnlyList<KeyChord> Chords { get; }

    /// <summary>True for a multi-chord sequence like <c>Ctrl+K, Ctrl+C</c>.</summary>
    public bool IsSequence => this.Chords.Count > 1;

    /// <summary>A single-chord gesture.</summary>
    public static KeyGesture From(KeyModifiers modifiers, string key) => new([KeyChord.Create(modifiers, key)]);

    /// <summary>A gesture from chords already built.</summary>
    public static KeyGesture From(params KeyChord[] chords)
    {
        if (chords.Length == 0)
            throw new ArgumentException("A gesture needs at least one chord.", nameof(chords));

        return new KeyGesture(chords.ToArray());
    }

    /// <summary>Parse gesture syntax. Throws <see cref="FormatException"/> when it is not one.</summary>
    public static KeyGesture Parse(string value)
        => TryParse(value, out var gesture, out var error)
            ? gesture
            : throw new FormatException($"'{value}' is not a keyboard gesture: {error}");

    /// <summary>Parse gesture syntax.</summary>
    public static bool TryParse([NotNullWhen(true)] string? value, [NotNullWhen(true)] out KeyGesture? gesture)
        => TryParse(value, out gesture, out _);

    static bool TryParse(string? value, [NotNullWhen(true)] out KeyGesture? gesture, out string error)
    {
        gesture = null;
        error = "it is empty";

        if (String.IsNullOrWhiteSpace(value))
            return false;

        var chords = new List<KeyChord>();
        foreach (var part in SplitChords(value))
        {
            if (!TryParseChord(part, out var chord, out error))
                return false;

            chords.Add(chord);
        }

        if (chords.Count == 0)
            return false;

        gesture = new KeyGesture(chords);
        return true;
    }

    /// <summary>
    /// Split on the commas between chords — but not a comma that <em>is</em> the key, as in
    /// <c>Ctrl+,</c> (the settings shortcut on a Mac).
    /// </summary>
    static IEnumerable<string> SplitChords(string value)
    {
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != ',')
                continue;

            // A comma right after '+' (or at the very start) is the key itself.
            var before = value.AsSpan(start, i - start).TrimEnd();
            if (before.Length == 0 || before[^1] == '+')
                continue;

            yield return value.Substring(start, i - start);
            start = i + 1;
        }

        yield return value.Substring(start);
    }

    static bool TryParseChord(string text, [NotNullWhen(true)] out KeyChord? chord, out string error)
    {
        chord = null;
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            error = "a chord between commas is empty";
            return false;
        }

        // A trailing '+' after a separator is the plus key: "Ctrl++".
        string keyToken;
        string modifierPart;
        if (trimmed.EndsWith("++", StringComparison.Ordinal))
        {
            keyToken = "+";
            modifierPart = trimmed[..^2];
        }
        else if (trimmed == "+")
        {
            keyToken = "+";
            modifierPart = String.Empty;
        }
        else
        {
            var last = trimmed.LastIndexOf('+');
            keyToken = last < 0 ? trimmed : trimmed[(last + 1)..];
            modifierPart = last < 0 ? String.Empty : trimmed[..last];
        }

        var modifiers = KeyModifiers.None;
        foreach (var raw in modifierPart.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var modifier = ParseModifier(raw);
            if (modifier == null)
            {
                error = $"'{raw}' is not a modifier";
                return false;
            }

            modifiers |= modifier.Value;
        }

        if (!KeyNames.TryNormalize(keyToken, out var key, out var kind))
        {
            error = $"'{keyToken}' is not a key";
            return false;
        }

        chord = new KeyChord(modifiers, key, kind);
        error = String.Empty;
        return true;
    }

    static KeyModifiers? ParseModifier(string token) => token.ToLowerInvariant() switch
    {
        "ctrl" or "control" or "ctl" => KeyModifiers.Control,
        "alt" or "option" or "opt" => KeyModifiers.Alt,
        "shift" => KeyModifiers.Shift,
        "meta" or "cmd" or "command" or "win" or "windows" or "super" => KeyModifiers.Meta,
        "primary" or "mod" or "cmdorctrl" or "commandorcontrol" => KeyModifiers.Primary,
        _ => null
    };

    /// <summary>The gesture written for a person on <paramref name="platform"/>: <c>Ctrl+K, Ctrl+C</c> or <c>⌘K ⌘C</c>.</summary>
    public string ToDisplayString(KeyboardPlatform platform)
        => String.Join(platform == KeyboardPlatform.Apple ? " " : ", ", this.Chords.Select(x => x.ToDisplayString(platform)));

    /// <summary>Gesture syntax, round-trippable through <see cref="Parse"/>.</summary>
    public override string ToString() => String.Join(", ", this.Chords);

    public bool Equals(KeyGesture? other)
        => other != null && this.Chords.SequenceEqual(other.Chords);

    public override bool Equals(object? obj) => this.Equals(obj as KeyGesture);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var chord in this.Chords)
            hash.Add(chord);

        return hash.ToHashCode();
    }
}

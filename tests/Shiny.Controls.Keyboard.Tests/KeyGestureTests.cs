namespace Shiny.Controls.Keyboard.Tests;

public class KeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Shift+A", KeyModifiers.Control | KeyModifiers.Shift, "A", KeyKind.Letter)]
    [InlineData("ctrl+shift+a", KeyModifiers.Control | KeyModifiers.Shift, "A", KeyKind.Letter)]
    [InlineData("Primary+S", KeyModifiers.Primary, "S", KeyKind.Letter)]
    [InlineData("Mod+S", KeyModifiers.Primary, "S", KeyKind.Letter)]
    [InlineData("CmdOrCtrl+S", KeyModifiers.Primary, "S", KeyKind.Letter)]
    [InlineData("Cmd+Opt+Space", KeyModifiers.Meta | KeyModifiers.Alt, "Space", KeyKind.Code)]
    [InlineData("Alt+F4", KeyModifiers.Alt, "F4", KeyKind.Code)]
    [InlineData("Esc", KeyModifiers.None, "Escape", KeyKind.Code)]
    [InlineData("Up", KeyModifiers.None, "ArrowUp", KeyKind.Code)]
    [InlineData("PgDn", KeyModifiers.None, "PageDown", KeyKind.Code)]
    [InlineData("Ctrl+1", KeyModifiers.Control, "1", KeyKind.Digit)]
    [InlineData("Ctrl+Digit1", KeyModifiers.Control, "1", KeyKind.Digit)]
    [InlineData("Ctrl+KeyZ", KeyModifiers.Control, "Z", KeyKind.Letter)]
    [InlineData("?", KeyModifiers.None, "?", KeyKind.Character)]
    [InlineData("Ctrl+Slash", KeyModifiers.Control, "Slash", KeyKind.Code)]
    [InlineData("Ctrl+/", KeyModifiers.Control, "/", KeyKind.Character)]
    [InlineData("Ctrl++", KeyModifiers.Control, "+", KeyKind.Character)]
    [InlineData("Ctrl+Plus", KeyModifiers.Control, "+", KeyKind.Character)]
    [InlineData("Primary+,", KeyModifiers.Primary, ",", KeyKind.Character)]
    [InlineData("Numpad5", KeyModifiers.None, "Numpad5", KeyKind.Code)]
    [InlineData("F24", KeyModifiers.None, "F24", KeyKind.Code)]
    public void Parses_single_chords(string text, KeyModifiers modifiers, string key, KeyKind kind)
    {
        var gesture = KeyGesture.Parse(text);
        gesture.Chords.Count.ShouldBe(1);
        gesture.Chords[0].ShouldBe(new KeyChord(modifiers, key, kind));
    }

    [Fact]
    public void Parses_a_sequence()
    {
        var gesture = KeyGesture.Parse("Ctrl+K, Ctrl+C");
        gesture.IsSequence.ShouldBeTrue();
        gesture.Chords.ShouldBe([
            new KeyChord(KeyModifiers.Control, "K", KeyKind.Letter),
            new KeyChord(KeyModifiers.Control, "C", KeyKind.Letter)
        ]);
    }

    [Fact]
    public void A_comma_after_a_plus_is_the_key_not_a_separator()
    {
        var gesture = KeyGesture.Parse("Ctrl+K, Ctrl+,");
        gesture.Chords.Count.ShouldBe(2);
        gesture.Chords[1].Key.ShouldBe(",");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Hyper+A")]
    [InlineData("Ctrl+NotAKey")]
    [InlineData("A,,B")]
    [InlineData("F25")]
    public void Rejects_garbage(string text)
        => KeyGesture.TryParse(text, out _).ShouldBeFalse();

    [Theory]
    [InlineData("Primary+Shift+A", KeyboardPlatform.Windows, "Ctrl+Shift+A")]
    [InlineData("Primary+Shift+A", KeyboardPlatform.Apple, "⇧⌘A")]
    [InlineData("Ctrl+Alt+Delete", KeyboardPlatform.Apple, "⌃⌥⌦")]
    [InlineData("Meta+E", KeyboardPlatform.Windows, "Win+E")]
    [InlineData("Meta+E", KeyboardPlatform.Linux, "Super+E")]
    [InlineData("Ctrl+K, Ctrl+C", KeyboardPlatform.Windows, "Ctrl+K, Ctrl+C")]
    [InlineData("Primary+K, Primary+C", KeyboardPlatform.Apple, "⌘K ⌘C")]
    [InlineData("Up", KeyboardPlatform.Windows, "↑")]
    [InlineData("Esc", KeyboardPlatform.Windows, "Esc")]
    [InlineData("Ctrl+Slash", KeyboardPlatform.Windows, "Ctrl+/")]
    public void Displays_per_platform(string text, KeyboardPlatform platform, string expected)
        => KeyGesture.Parse(text).ToDisplayString(platform).ShouldBe(expected);

    [Theory]
    [InlineData("Primary+Shift+A")]
    [InlineData("Ctrl+K, Ctrl+C")]
    [InlineData("Ctrl+Plus")]
    [InlineData("?")]
    [InlineData("Alt+F4")]
    public void ToString_round_trips(string text)
    {
        var gesture = KeyGesture.Parse(text);
        KeyGesture.Parse(gesture.ToString()).ShouldBe(gesture);
    }

    [Fact]
    public void Primary_resolves_per_platform()
    {
        KeyModifiers.Primary.Resolve(KeyboardPlatform.Apple).ShouldBe(KeyModifiers.Meta);
        KeyModifiers.Primary.Resolve(KeyboardPlatform.Windows).ShouldBe(KeyModifiers.Control);
        KeyModifiers.Primary.Resolve(KeyboardPlatform.Linux).ShouldBe(KeyModifiers.Control);
        (KeyModifiers.Primary | KeyModifiers.Shift).Resolve(KeyboardPlatform.Apple).ShouldBe(KeyModifiers.Meta | KeyModifiers.Shift);
    }
}

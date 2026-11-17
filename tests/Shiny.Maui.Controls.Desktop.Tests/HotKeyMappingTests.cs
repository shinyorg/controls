using Shiny.Maui.Controls.QuickEntry;
using Shiny.Maui.Controls.Desktop.QuickEntry;
using Shiny.Maui.Controls.Desktop.TrayIcon;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Desktop.Tests;

/// <summary>
/// The X11 half of the hotkey key tables. Each platform keeps its own table because the codes are
/// unrelated — Carbon uses positional key codes, Win32 uses virtual keys, X11 uses keysym names —
/// and a typo in any of them shows up as a hotkey that silently never fires. This is the table that
/// compiles on the plain <c>net11.0</c> target, so it is the one a headless test can reach.
/// </summary>
public class HotKeyMappingTests
{
    [Theory]
    [InlineData("a", "a")]
    [InlineData("Z", "z")]
    [InlineData("7", "7")]
    [InlineData("space", "space")]
    [InlineData("Space", "space")]
    [InlineData("enter", "Return")]
    [InlineData("esc", "Escape")]
    [InlineData("F5", "F5")]
    [InlineData("f12", "F12")]
    [InlineData("pgup", "Prior")]
    [InlineData("pagedown", "Next")]
    [InlineData(",", "comma")]
    public void Known_keys_map_to_their_keysym_name(string key, string expected)
        => X11HotKeyBackend.MapKeysym(key).ShouldBe(expected);

    [Theory]
    [InlineData("F0")]
    [InlineData("F25")]
    [InlineData("nonsense")]
    public void Unmappable_keys_decline_rather_than_guess(string key)
        => X11HotKeyBackend.MapKeysym(key).ShouldBeNull();

    [Fact]
    public void The_accelerator_grammar_the_hotkey_services_share_parses_the_documented_form()
    {
        var parsed = TrayAccelerator.Parse("Ctrl+Alt+Space");

        parsed.ShouldNotBeNull();
        parsed!.Key.ShouldBe("Space");
        parsed.Modifiers.ShouldBe(TrayAcceleratorModifiers.Control | TrayAcceleratorModifiers.Alt);
    }

    [Fact]
    public void Cmd_and_Meta_are_the_same_modifier()
    {
        TrayAccelerator.Parse("Cmd+K")!.Modifiers.ShouldBe(TrayAcceleratorModifiers.Meta);
        TrayAccelerator.Parse("Super+K")!.Modifiers.ShouldBe(TrayAcceleratorModifiers.Meta);
    }
}

namespace Shiny.Controls.Keyboard.Tests;

public class NativeKeyCodesTests
{
    [Theory]
    [InlineData(0x41, "KeyA")]
    [InlineData(0x5A, "KeyZ")]
    [InlineData(0x31, "Digit1")]
    [InlineData(0x70, "F1")]
    [InlineData(0x87, "F24")]
    [InlineData(0x1B, "Escape")]
    [InlineData(0x26, "ArrowUp")]
    [InlineData(0x65, "Numpad5")]
    [InlineData(0xBF, "Slash")]
    public void Windows(int vk, string code) => NativeKeyCodes.FromWindowsVirtualKey(vk).ShouldBe(code);

    [Theory]
    [InlineData(0x00, "KeyA")]
    [InlineData(0x06, "KeyZ")]
    [InlineData(0x12, "Digit1")]
    [InlineData(0x35, "Escape")]
    [InlineData(0x7E, "ArrowUp")]
    [InlineData(0x2C, "Slash")]
    [InlineData(0x4C, "NumpadEnter")]
    public void Mac(int keyCode, string code) => NativeKeyCodes.FromMacKeyCode(keyCode).ShouldBe(code);

    [Theory]
    [InlineData(38, "KeyA")]   // X keycode = evdev 30 + 8
    [InlineData(10, "Digit1")]
    [InlineData(9, "Escape")]
    [InlineData(111, "ArrowUp")]
    [InlineData(61, "Slash")]
    public void X11(int keycode, string code) => NativeKeyCodes.FromXKeycode(keycode).ShouldBe(code);

    [Theory]
    [InlineData(0x04, "KeyA")]
    [InlineData(0x1D, "KeyZ")]
    [InlineData(0x1E, "Digit1")]
    [InlineData(0x27, "Digit0")]
    [InlineData(0x3A, "F1")]
    [InlineData(0x45, "F12")]
    [InlineData(0x68, "F13")]
    [InlineData(0x59, "Numpad1")]
    [InlineData(0x62, "Numpad0")]
    [InlineData(0x52, "ArrowUp")]
    public void Hid(int usage, string code) => NativeKeyCodes.FromHidUsage(usage).ShouldBe(code);

    [Theory]
    [InlineData(29, "KeyA")]
    [InlineData(54, "KeyZ")]
    [InlineData(7, "Digit0")]
    [InlineData(131, "F1")]
    [InlineData(111, "Escape")]
    [InlineData(19, "ArrowUp")]
    public void Android(int keycode, string code) => NativeKeyCodes.FromAndroidKeycode(keycode).ShouldBe(code);

    [Theory]
    [InlineData("KeyA", false, "a")]
    [InlineData("KeyA", true, "A")]
    [InlineData("Digit1", true, "!")]
    [InlineData("Digit0", true, ")")]
    [InlineData("Slash", true, "?")]
    [InlineData("Equal", true, "+")]
    [InlineData("ArrowUp", false, null)]
    public void Us_characters(string code, bool shift, string? expected)
        => NativeKeyCodes.UsCharacter(code, shift).ShouldBe(expected);

    [Fact]
    public void Every_table_only_produces_codes_the_gesture_vocabulary_understands()
    {
        var codes = Enumerable.Range(0, 0x100).Select(NativeKeyCodes.FromWindowsVirtualKey)
            .Concat(Enumerable.Range(0, 0x80).Select(NativeKeyCodes.FromMacKeyCode))
            .Concat(Enumerable.Range(0, 0x100).Select(NativeKeyCodes.FromEvdev))
            .Concat(Enumerable.Range(0, 0x100).Select(NativeKeyCodes.FromHidUsage))
            .Concat(Enumerable.Range(0, 0x100).Select(NativeKeyCodes.FromAndroidKeycode))
            .Where(x => x != null && !KeyNames.IsModifierCode(x) && x != "NumLock")
            .Distinct()
            .ToList();

        foreach (var code in codes)
            KeyNames.TryNormalize(code, out _, out _).ShouldBeTrue($"'{code}' cannot be named in a gesture");
    }
}

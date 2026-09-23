using Shiny.Gamepad;

namespace Shiny.Controls.Gaming;


/// <summary>What is printed on the buttons, and in what colours.</summary>
/// <remarks>
/// A face style only relabels - it never changes which <see cref="GamepadButton"/> a position reports.
/// The bottom face button is <see cref="GamepadButton.A"/> under every style, drawn "A" for Xbox, a
/// cross for PlayStation and "B" for Nintendo, which is exactly how Shiny.Gamepad reports the physical
/// controllers those labels come from.
/// </remarks>
public enum GamepadFaceStyle
{
    /// <summary>A/B/X/Y in green, red, blue and yellow; LB/RB/LT/RT; View and Menu.</summary>
    Xbox,

    /// <summary>Cross, circle, square and triangle; L1/R1/L2/R2; Create and Options.</summary>
    PlayStation,

    /// <summary>Switch labels - B/A/Y/X by position; L/R/ZL/ZR; minus and plus.</summary>
    Nintendo,

    /// <summary>Nintendo labels in the Super Famicom's yellow, red, green and blue; SELECT and START.</summary>
    SuperNintendo,

    /// <summary>Red B and A; SELECT and START.</summary>
    Nes
}


/// <summary>How one button is drawn under a face style.</summary>
/// <param name="Text">The label, or null when <paramref name="Glyph"/> is drawn instead.</param>
/// <param name="Glyph">SVG path data in a 0-1 unit box, stroked rather than filled, or null.</param>
/// <param name="Accent">
/// The colour of the label or glyph as <c>#RRGGBB</c>, or null to use the view's label colour.
/// Xbox and PlayStation colour the symbol; Nintendo styles colour the button.
/// </param>
/// <param name="Fill">The button's own fill as <c>#RRGGBB</c>, or null to use the view's button colour.</param>
public readonly record struct GamepadButtonFace(string? Text, string? Glyph, string? Accent, string? Fill);


/// <summary>Labels and colours for each face style.</summary>
public static class GamepadFaces
{
    // Glyphs are written with an explicit command on every segment and a space between every number.
    // MAUI's PathBuilder reads an implicit lineto as a moveto and truncates run-together decimals,
    // both silently, so a path that looks right in the browser can draw a dot on MAUI.
    const string CrossGlyph = "M 0.2 0.2 L 0.8 0.8 M 0.8 0.2 L 0.2 0.8";
    // four cubics rather than an arc - arcs are the least consistently supported path command
    const string CircleGlyph = "M 0.5 0.2 C 0.6657 0.2 0.8 0.3343 0.8 0.5 C 0.8 0.6657 0.6657 0.8 0.5 0.8 C 0.3343 0.8 0.2 0.6657 0.2 0.5 C 0.2 0.3343 0.3343 0.2 0.5 0.2 Z";
    const string SquareGlyph = "M 0.22 0.22 L 0.78 0.22 L 0.78 0.78 L 0.22 0.78 Z";
    const string TriangleGlyph = "M 0.5 0.18 L 0.84 0.76 L 0.16 0.76 Z";

    /// <summary>A house outline, for the Home button under every style.</summary>
    public const string HomeGlyph = "M 0.2 0.52 L 0.5 0.24 L 0.8 0.52 M 0.3 0.44 L 0.3 0.78 L 0.7 0.78 L 0.7 0.44";


    /// <summary>How <paramref name="button"/> is drawn under <paramref name="style"/>.</summary>
    public static GamepadButtonFace Get(GamepadFaceStyle style, GamepadButton button)
    {
        if (button == GamepadButton.Home)
            return new(null, HomeGlyph, null, null);

        return style switch
        {
            GamepadFaceStyle.Xbox => button switch
            {
                GamepadButton.A => new("A", null, "#5CC45C", null),
                GamepadButton.B => new("B", null, "#F0584B", null),
                GamepadButton.X => new("X", null, "#4A90F0", null),
                GamepadButton.Y => new("Y", null, "#F5C518", null),
                GamepadButton.LeftShoulder => Text("LB"),
                GamepadButton.RightShoulder => Text("RB"),
                GamepadButton.LeftTrigger => Text("LT"),
                GamepadButton.RightTrigger => Text("RT"),
                GamepadButton.Select => Text("View"),
                GamepadButton.Start => Text("Menu"),
                _ => Generic(button)
            },

            GamepadFaceStyle.PlayStation => button switch
            {
                GamepadButton.A => new(null, CrossGlyph, "#8FB4F0", null),
                GamepadButton.B => new(null, CircleGlyph, "#F07C7C", null),
                GamepadButton.X => new(null, SquareGlyph, "#EB94D0", null),
                GamepadButton.Y => new(null, TriangleGlyph, "#4CC9AF", null),
                GamepadButton.LeftShoulder => Text("L1"),
                GamepadButton.RightShoulder => Text("R1"),
                GamepadButton.LeftTrigger => Text("L2"),
                GamepadButton.RightTrigger => Text("R2"),
                GamepadButton.Select => Text("Create"),
                GamepadButton.Start => Text("Options"),
                _ => Generic(button)
            },

            GamepadFaceStyle.Nintendo => button switch
            {
                GamepadButton.A => Text("B"),
                GamepadButton.B => Text("A"),
                GamepadButton.X => Text("Y"),
                GamepadButton.Y => Text("X"),
                GamepadButton.LeftShoulder => Text("L"),
                GamepadButton.RightShoulder => Text("R"),
                GamepadButton.LeftTrigger => Text("ZL"),
                GamepadButton.RightTrigger => Text("ZR"),
                GamepadButton.Select => Text("−"),
                GamepadButton.Start => Text("+"),
                _ => Generic(button)
            },

            GamepadFaceStyle.SuperNintendo => button switch
            {
                GamepadButton.A => new("B", null, "#FFFFFF", "#E8B823"),
                GamepadButton.B => new("A", null, "#FFFFFF", "#D63A3A"),
                GamepadButton.X => new("Y", null, "#FFFFFF", "#3E9E4C"),
                GamepadButton.Y => new("X", null, "#FFFFFF", "#3B5FC4"),
                GamepadButton.LeftShoulder => Text("L"),
                GamepadButton.RightShoulder => Text("R"),
                GamepadButton.LeftTrigger => Text("ZL"),
                GamepadButton.RightTrigger => Text("ZR"),
                GamepadButton.Select => Text("SELECT"),
                GamepadButton.Start => Text("START"),
                _ => Generic(button)
            },

            GamepadFaceStyle.Nes => button switch
            {
                GamepadButton.A => new("B", null, "#FFFFFF", "#C8202E"),
                GamepadButton.B => new("A", null, "#FFFFFF", "#C8202E"),
                GamepadButton.X => new("Y", null, "#FFFFFF", "#C8202E"),
                GamepadButton.Y => new("X", null, "#FFFFFF", "#C8202E"),
                GamepadButton.Select => Text("SELECT"),
                GamepadButton.Start => Text("START"),
                _ => Generic(button)
            },

            _ => Generic(button)
        };
    }


    static GamepadButtonFace Text(string text) => new(text, null, null, null);


    static GamepadButtonFace Generic(GamepadButton button) => button switch
    {
        GamepadButton.LeftStick => Text("L3"),
        GamepadButton.RightStick => Text("R3"),
        GamepadButton.Touchpad => Text("Pad"),
        GamepadButton.Paddle1 => Text("P1"),
        GamepadButton.Paddle2 => Text("P2"),
        GamepadButton.Paddle3 => Text("P3"),
        GamepadButton.Paddle4 => Text("P4"),
        _ => Text(button.ToString())
    };
}

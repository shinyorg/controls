using Shiny.Gamepad;

namespace Shiny.Controls.Gaming;


/// <summary>The built-in controller layouts.</summary>
public enum GamepadPreset
{
    /// <summary>D-pad, B and A, Select and Start - the NES pad.</summary>
    Nes,

    /// <summary>D-pad, a Y/X/B/A diamond, L and R shoulders, Select and Start - the Super Nintendo pad.</summary>
    Snes,

    /// <summary>
    /// Two sticks, d-pad, a four-button diamond, bumpers, triggers, View/Menu and Home - the modern
    /// Xbox / PlayStation / Switch Pro layout. Relabel it with <see cref="GamepadFaceStyle"/>.
    /// </summary>
    Standard,

    /// <summary>Two sticks and a pause button - the mobile twin-stick shooter.</summary>
    TwinStick,

    /// <summary>One stick and six buttons in two rows of three - the arcade fighting-game panel.</summary>
    Arcade
}


/// <summary>Factory for the built-in layouts. Every call returns a fresh copy, safe to edit.</summary>
/// <remarks>
/// <para><b>Buttons are positional, as they are in Shiny.Gamepad.</b> <see cref="GamepadButton.A"/> is
/// always the bottom face button and <see cref="GamepadButton.B"/> the right one, whatever is printed
/// on them. So the SNES pad's bottom "B" reports <see cref="GamepadButton.A"/> and its right "A"
/// reports <see cref="GamepadButton.B"/> - exactly what a physical Switch or SNES-style controller
/// reports through Shiny.Gamepad, which is the point: the same game code handles both.</para>
/// <para>The NES pad has no diamond, only B on the left and A on the right. They are mapped the way
/// Nintendo maps them on its own later hardware - NES B to the bottom button, NES A to the right one -
/// so a game written for the SNES layout plays unchanged on the NES one.</para>
/// </remarks>
public static class GamepadLayouts
{
    /// <summary>The layout for a preset.</summary>
    public static GamepadLayout Get(GamepadPreset preset) => preset switch
    {
        GamepadPreset.Nes => Nes(),
        GamepadPreset.Snes => Snes(),
        GamepadPreset.Standard => Standard(),
        GamepadPreset.TwinStick => TwinStick(),
        GamepadPreset.Arcade => Arcade(),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown gamepad preset")
    };


    /// <summary>The NES pad.</summary>
    public static GamepadLayout Nes() => new()
    {
        Name = "NES",
        FaceStyle = GamepadFaceStyle.Nes,
        DesignWidth = 520,
        DesignHeight = 200,
        BodyRoundness = 0.06f,
        Elements =
        [
            DPad(GamepadAnchor.BottomLeft, 100, -100, 130),
            Pill("select", GamepadButton.Select, GamepadAnchor.Bottom, -34, -62),
            Pill("start", GamepadButton.Start, GamepadAnchor.Bottom, 34, -62),
            // 78 apart at 70 wide: the default hit slop overlaps them by a thumb-width band in the middle,
            // so rolling a thumb across presses both - the NES "B+A" move
            Face("b", GamepadButton.A, GamepadAnchor.BottomRight, -156, -88, 70),
            Face("a", GamepadButton.B, GamepadAnchor.BottomRight, -78, -88, 70)
        ]
    };


    /// <summary>The Super Nintendo pad.</summary>
    public static GamepadLayout Snes()
    {
        const float cx = -108, cy = -98, gap = 44, size = 52;
        return new()
        {
            Name = "SNES",
            FaceStyle = GamepadFaceStyle.SuperNintendo,
            DesignWidth = 520,
            DesignHeight = 220,
            BodyRoundness = 0.5f,
            Elements =
            [
                Shoulder("l", GamepadButton.LeftShoulder, GamepadAnchor.TopLeft, 88, 22),
                Shoulder("r", GamepadButton.RightShoulder, GamepadAnchor.TopRight, -88, 22),
                DPad(GamepadAnchor.BottomLeft, 108, -98, 124),
                Pill("select", GamepadButton.Select, GamepadAnchor.Bottom, -38, -84),
                Pill("start", GamepadButton.Start, GamepadAnchor.Bottom, 38, -84),
                Face("y", GamepadButton.X, GamepadAnchor.BottomRight, cx - gap, cy, size),
                Face("x", GamepadButton.Y, GamepadAnchor.BottomRight, cx, cy - gap, size),
                Face("b", GamepadButton.A, GamepadAnchor.BottomRight, cx, cy + gap, size),
                Face("a", GamepadButton.B, GamepadAnchor.BottomRight, cx + gap, cy, size)
            ]
        };
    }


    /// <summary>The modern two-stick pad.</summary>
    public static GamepadLayout Standard()
    {
        const float cx = -112, cy = -158, gap = 42, size = 48;
        return new()
        {
            Name = "Standard",
            FaceStyle = GamepadFaceStyle.Xbox,
            DesignWidth = 620,
            DesignHeight = 310,
            BodyRoundness = 0.32f,
            Elements =
            [
                Trigger("lt", GamepadButton.LeftTrigger, GamepadAnchor.TopLeft, 78, 26),
                Shoulder("lb", GamepadButton.LeftShoulder, GamepadAnchor.TopLeft, 78, 66),
                Trigger("rt", GamepadButton.RightTrigger, GamepadAnchor.TopRight, -78, 26),
                Shoulder("rb", GamepadButton.RightShoulder, GamepadAnchor.TopRight, -78, 66),
                Stick("ls", GamepadButton.LeftStick, GamepadAnchor.BottomLeft, 112, -158, 124),
                DPad(GamepadAnchor.BottomLeft, 232, -64, 104),
                Stick("rs", GamepadButton.RightStick, GamepadAnchor.BottomRight, -232, -64, 104),
                Pill("select", GamepadButton.Select, GamepadAnchor.Bottom, -44, -168),
                Pill("start", GamepadButton.Start, GamepadAnchor.Bottom, 44, -168),
                Face("home", GamepadButton.Home, GamepadAnchor.Bottom, 0, -112, 36),
                Face("x", GamepadButton.X, GamepadAnchor.BottomRight, cx - gap, cy, size),
                Face("y", GamepadButton.Y, GamepadAnchor.BottomRight, cx, cy - gap, size),
                Face("a", GamepadButton.A, GamepadAnchor.BottomRight, cx, cy + gap, size),
                Face("b", GamepadButton.B, GamepadAnchor.BottomRight, cx + gap, cy, size)
            ]
        };
    }


    /// <summary>Two sticks and pause.</summary>
    public static GamepadLayout TwinStick() => new()
    {
        Name = "Twin Stick",
        FaceStyle = GamepadFaceStyle.Xbox,
        DesignWidth = 560,
        DesignHeight = 240,
        BodyRoundness = 0.4f,
        Elements =
        [
            Stick("ls", GamepadButton.LeftStick, GamepadAnchor.BottomLeft, 124, -120, 150),
            Stick("rs", GamepadButton.RightStick, GamepadAnchor.BottomRight, -124, -120, 150),
            Pill("start", GamepadButton.Start, GamepadAnchor.Top, 0, 32)
        ]
    };


    /// <summary>
    /// The arcade panel. The top row is X, Y and the right bumper, the bottom row A, B and the right
    /// trigger - the light/medium/heavy punch-and-kick mapping fighting games use on a pad.
    /// </summary>
    public static GamepadLayout Arcade()
    {
        const float size = 58, step = 66;
        return new()
        {
            Name = "Arcade",
            FaceStyle = GamepadFaceStyle.Xbox,
            DesignWidth = 600,
            DesignHeight = 260,
            BodyRoundness = 0.08f,
            Elements =
            [
                Stick("ls", GamepadButton.LeftStick, GamepadAnchor.BottomLeft, 120, -120, 150),
                Pill("select", GamepadButton.Select, GamepadAnchor.Top, -40, 30),
                Pill("start", GamepadButton.Start, GamepadAnchor.Top, 40, 30),
                // arcade rows are staggered - each button sits a little lower than the one before it
                Face("x", GamepadButton.X, GamepadAnchor.BottomRight, -(60 + step * 2), -150, size),
                Face("y", GamepadButton.Y, GamepadAnchor.BottomRight, -(60 + step), -158, size),
                Face("rb", GamepadButton.RightShoulder, GamepadAnchor.BottomRight, -60, -150, size),
                Face("a", GamepadButton.A, GamepadAnchor.BottomRight, -(60 + step * 2), -78, size),
                Face("b", GamepadButton.B, GamepadAnchor.BottomRight, -(60 + step), -86, size),
                Face("rt", GamepadButton.RightTrigger, GamepadAnchor.BottomRight, -60, -78, size, GamepadElementKind.Trigger)
            ]
        };
    }


    static GamepadElement Face(string id, GamepadButton button, GamepadAnchor anchor, float x, float y, float size, GamepadElementKind kind = GamepadElementKind.Button) => new()
    {
        Id = id,
        Kind = kind,
        Button = button,
        Shape = GamepadElementShape.Circle,
        Anchor = anchor,
        X = x,
        Y = y,
        Width = size,
        Height = size
    };


    static GamepadElement Pill(string id, GamepadButton button, GamepadAnchor anchor, float x, float y) => new()
    {
        Id = id,
        Kind = GamepadElementKind.Button,
        Button = button,
        Shape = GamepadElementShape.Pill,
        Anchor = anchor,
        X = x,
        Y = y,
        Width = 62,
        Height = 24
    };


    static GamepadElement Shoulder(string id, GamepadButton button, GamepadAnchor anchor, float x, float y) => new()
    {
        Id = id,
        Kind = GamepadElementKind.Button,
        Button = button,
        Shape = GamepadElementShape.Rounded,
        Anchor = anchor,
        X = x,
        Y = y,
        Width = 112,
        Height = 34
    };


    static GamepadElement Trigger(string id, GamepadButton button, GamepadAnchor anchor, float x, float y) => new()
    {
        Id = id,
        Kind = GamepadElementKind.Trigger,
        Button = button,
        Shape = GamepadElementShape.Rounded,
        Anchor = anchor,
        X = x,
        Y = y,
        Width = 112,
        Height = 34
    };


    static GamepadElement DPad(GamepadAnchor anchor, float x, float y, float size) => new()
    {
        Id = "dpad",
        Kind = GamepadElementKind.DPad,
        Button = GamepadButton.DPad,
        Anchor = anchor,
        X = x,
        Y = y,
        Width = size,
        Height = size
    };


    static GamepadElement Stick(string id, GamepadButton button, GamepadAnchor anchor, float x, float y, float size) => new()
    {
        Id = id,
        Kind = GamepadElementKind.Stick,
        Button = button,
        Anchor = anchor,
        X = x,
        Y = y,
        Width = size,
        Height = size
    };
}

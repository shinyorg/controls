using Shiny.Gamepad;

namespace Shiny.Controls.Gaming;


/// <summary>What an on-screen element is, which decides how a finger on it is read.</summary>
public enum GamepadElementKind
{
    /// <summary>
    /// A digital button. A finger can slide from one button onto its neighbours, and a finger landing
    /// between two adjacent buttons presses both - the NES/SNES "roll across A and B" move.
    /// </summary>
    Button,

    /// <summary>
    /// A digital button that also drives its trigger axis - full travel while held, zero when released.
    /// Bind it to <see cref="GamepadButton.LeftTrigger"/> or <see cref="GamepadButton.RightTrigger"/>.
    /// </summary>
    Trigger,

    /// <summary>A four- or eight-way directional pad. Captures its finger until it lifts.</summary>
    DPad,

    /// <summary>
    /// An analog stick. Captures its finger until it lifts; bind it to
    /// <see cref="GamepadButton.LeftStick"/> or <see cref="GamepadButton.RightStick"/> to say which
    /// stick it drives (and which button a double-tap clicks).
    /// </summary>
    Stick
}


/// <summary>The drawn outline of a button. Hit testing always uses the element's full rectangle.</summary>
public enum GamepadElementShape
{
    /// <summary>A circle - face buttons.</summary>
    Circle,

    /// <summary>A capsule - Start, Select and the NES-style centre buttons.</summary>
    Pill,

    /// <summary>A rectangle with softened corners - shoulders and triggers.</summary>
    Rounded
}


/// <summary>Which point of the arranged area an element's offset is measured from.</summary>
/// <remarks>
/// Anchoring is what lets one layout work as a phone overlay in both orientations: a d-pad anchored
/// bottom-left stays under the left thumb whether the screen is 844 wide or 390, where a normalised
/// position would drift toward the centre and stretch the gaps between buttons.
/// </remarks>
public enum GamepadAnchor
{
    /// <summary>The top-left corner.</summary>
    TopLeft,

    /// <summary>The middle of the top edge.</summary>
    Top,

    /// <summary>The top-right corner.</summary>
    TopRight,

    /// <summary>The middle of the left edge.</summary>
    Left,

    /// <summary>The centre of the area.</summary>
    Center,

    /// <summary>The middle of the right edge.</summary>
    Right,

    /// <summary>The bottom-left corner - where a d-pad or left stick usually sits.</summary>
    BottomLeft,

    /// <summary>The middle of the bottom edge.</summary>
    Bottom,

    /// <summary>The bottom-right corner - where the face buttons usually sit.</summary>
    BottomRight
}


/// <summary>How a d-pad resolves the angle of a finger into directions.</summary>
public enum GamepadDPadMode
{
    /// <summary>Up, down, left and right, with diagonals - what most games expect.</summary>
    EightWay,

    /// <summary>Only ever one direction at a time - menus, and puzzle games that punish a stray diagonal.</summary>
    FourWay
}


/// <summary>How a layout is fitted into the space it is given.</summary>
public enum GamepadSizing
{
    /// <summary>
    /// Elements sit at their anchor plus offset at their natural size (times the view's scale), so
    /// the view can cover a whole screen as an overlay and keep each cluster under a thumb. A view
    /// smaller than the layout's design canvas - a portrait phone - shrinks it to fit so the corner
    /// clusters never meet; a larger one never grows it. The usual choice for a game overlay.
    /// </summary>
    Anchored,

    /// <summary>
    /// The layout is arranged on its own design canvas and scaled uniformly to fit, like a picture of a
    /// controller - for a gamepad placed inline in a page rather than laid over a game.
    /// </summary>
    Uniform
}


/// <summary>One thing on the on-screen controller: a button, a d-pad or a stick.</summary>
/// <remarks>
/// A plain mutable class so it serialises cleanly and a layout editor can move it; the engine never
/// reads an element it is not currently arranging, so changing one takes effect on the next arrange.
/// </remarks>
public sealed class GamepadElement
{
    /// <summary>Unique within its layout. Used to match an edited element back to its preset and to persist positions.</summary>
    public string Id { get; set; } = "";

    /// <summary>How a finger on the element is read.</summary>
    public GamepadElementKind Kind { get; set; }

    /// <summary>
    /// The button this element reports. A <see cref="GamepadElementKind.DPad"/> ignores it (it always
    /// reports the four d-pad buttons); a <see cref="GamepadElementKind.Stick"/> uses it to say which
    /// stick it is.
    /// </summary>
    public GamepadButton Button { get; set; }

    /// <summary>The outline drawn for a button or trigger.</summary>
    public GamepadElementShape Shape { get; set; } = GamepadElementShape.Circle;

    /// <summary>The point <see cref="X"/> and <see cref="Y"/> are measured from.</summary>
    public GamepadAnchor Anchor { get; set; }

    /// <summary>Horizontal offset of the element's centre from its anchor, in device-independent units. Positive is right.</summary>
    public float X { get; set; }

    /// <summary>Vertical offset of the element's centre from its anchor, in device-independent units. Positive is down.</summary>
    public float Y { get; set; }

    /// <summary>Width in device-independent units.</summary>
    public float Width { get; set; } = 56;

    /// <summary>Height in device-independent units.</summary>
    public float Height { get; set; } = 56;

    /// <summary>Text drawn on the element, replacing the face style's label. Null keeps the style's.</summary>
    public string? Label { get; set; }

    /// <summary>Fill colour as <c>#RRGGBB</c> or <c>#AARRGGBB</c>, replacing the face style's. Null keeps the style's.</summary>
    public string? Color { get; set; }

    /// <summary>
    /// For a stick: the stick re-centres wherever the thumb lands inside its <see cref="FloatingZone"/>
    /// and springs home on release - the "touch anywhere on the left" stick most mobile games use.
    /// </summary>
    public bool IsFloating { get; set; }

    /// <summary>
    /// For a floating stick: how far beyond the stick's own size a touch still grabs it, in
    /// device-independent units on each side.
    /// </summary>
    public float FloatingZone { get; set; } = 90;

    /// <summary>For a button: rapid-fires while held, at the view's turbo rate.</summary>
    public bool IsTurbo { get; set; }


    /// <summary>A deep copy, so an edited layout never mutates a preset or the caller's instance.</summary>
    public GamepadElement Clone() => (GamepadElement)this.MemberwiseClone();
}


/// <summary>A complete on-screen controller: its elements and the canvas they were designed on.</summary>
public sealed class GamepadLayout
{
    /// <summary>Display name - shown nowhere by the controls, but kept through a JSON round trip.</summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The face style to use when the view does not set one. Presets pick the style of the console
    /// they imitate.
    /// </summary>
    public GamepadFaceStyle FaceStyle { get; set; } = GamepadFaceStyle.Xbox;

    /// <summary>Width of the canvas the anchors were designed against - the canvas <see cref="GamepadSizing.Uniform"/> scales, and the size below which <see cref="GamepadSizing.Anchored"/> starts shrinking.</summary>
    public float DesignWidth { get; set; } = 600;

    /// <summary>Height of the canvas the anchors were designed against. See <see cref="DesignWidth"/>.</summary>
    public float DesignHeight { get; set; } = 280;

    /// <summary>Corner radius of the controller body drawn in <see cref="GamepadSizing.Uniform"/>, as a fraction of its height (0.5 is a full capsule).</summary>
    public float BodyRoundness { get; set; } = 0.25f;

    /// <summary>The elements, drawn in order - later ones on top.</summary>
    public List<GamepadElement> Elements { get; set; } = [];


    /// <summary>Every button the layout can report - what <see cref="IGamepad.SupportedButtons"/> answers for a virtual pad.</summary>
    public GamepadButton SupportedButtons
    {
        get
        {
            var all = GamepadButton.None;
            foreach (var e in this.Elements)
            {
                all |= e.Kind switch
                {
                    GamepadElementKind.DPad => GamepadButton.DPad,
                    // a stick's own button is the click, reachable by double-tap
                    _ => e.Button
                };
            }
            return all;
        }
    }


    /// <summary>A deep copy.</summary>
    public GamepadLayout Clone() => new()
    {
        Name = this.Name,
        FaceStyle = this.FaceStyle,
        DesignWidth = this.DesignWidth,
        DesignHeight = this.DesignHeight,
        BodyRoundness = this.BodyRoundness,
        Elements = this.Elements.Select(x => x.Clone()).ToList()
    };


    /// <summary>The element with this id, or null.</summary>
    public GamepadElement? Find(string id) => this.Elements.FirstOrDefault(x => x.Id == id);


    /// <summary>Serialises the layout - what an app persists after the player rearranges the controller.</summary>
    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(this, GamepadJsonContext.Default.GamepadLayout);


    /// <summary>Reads a layout written by <see cref="ToJson"/>.</summary>
    public static GamepadLayout FromJson(string json)
        => System.Text.Json.JsonSerializer.Deserialize(json, GamepadJsonContext.Default.GamepadLayout)
           ?? throw new ArgumentException("The JSON did not contain a gamepad layout", nameof(json));
}


[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    WriteIndented = false,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
)]
[System.Text.Json.Serialization.JsonSerializable(typeof(GamepadLayout))]
partial class GamepadJsonContext : System.Text.Json.Serialization.JsonSerializerContext;

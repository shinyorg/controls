using Shiny.Gamepad;

namespace Shiny.Controls.Gaming;


/// <summary>
/// One element as it should be drawn right now: where it is, how it is labelled and what the fingers
/// on it are doing. Written by <see cref="GamepadEngine"/>, read by the MAUI and Blazor renderers.
/// </summary>
public sealed class GamepadElementVisual
{
    internal GamepadElementVisual(GamepadElement element, int index)
    {
        this.Element = element;
        this.Index = index;
    }


    /// <summary>The layout element this visual draws.</summary>
    public GamepadElement Element { get; }

    /// <summary>Position in the layout's element list.</summary>
    public int Index { get; }

    /// <summary>Where the element is drawn at rest.</summary>
    public GamepadRect Bounds { get; internal set; }

    /// <summary>Where a touch still counts as landing on it - the drawn bounds plus hit slop, or the whole zone of a floating stick.</summary>
    public GamepadRect HitBounds { get; internal set; }

    /// <summary>Label and colours from the face style, with the element's own overrides applied.</summary>
    public GamepadButtonFace Face { get; internal set; }

    /// <summary>A button or trigger is held.</summary>
    public bool IsPressed { get; internal set; }

    /// <summary>For a d-pad: which of the four directions are held.</summary>
    public GamepadButton PressedDirections { get; internal set; }

    /// <summary>For a stick: a finger is on it.</summary>
    public bool IsActive { get; internal set; }

    /// <summary>For a stick: it has been double-tapped and is being held as its click button.</summary>
    public bool IsClicked { get; internal set; }

    /// <summary>For a stick: the centre of the base, which moves to the thumb for a floating stick.</summary>
    public float BaseX { get; internal set; }

    /// <summary>For a stick: the centre of the base.</summary>
    public float BaseY { get; internal set; }

    /// <summary>For a stick: the centre of the knob.</summary>
    public float KnobX { get; internal set; }

    /// <summary>For a stick: the centre of the knob.</summary>
    public float KnobY { get; internal set; }

    /// <summary>For a stick: how far the knob can travel from the base centre.</summary>
    public float Travel { get; internal set; }

    /// <summary>The element is being moved or resized in edit mode.</summary>
    public bool IsSelected { get; internal set; }
}

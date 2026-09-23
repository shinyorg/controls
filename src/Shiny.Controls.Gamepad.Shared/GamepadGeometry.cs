namespace Shiny.Controls.Gaming;


/// <summary>A rectangle in the view's device-independent units. Host-neutral on purpose - MAUI's RectF and the browser's DOMRect never meet here.</summary>
public readonly record struct GamepadRect(float X, float Y, float Width, float Height)
{
    /// <summary>Horizontal centre.</summary>
    public float CenterX => this.X + this.Width / 2f;

    /// <summary>Vertical centre.</summary>
    public float CenterY => this.Y + this.Height / 2f;

    /// <summary>Right edge.</summary>
    public float Right => this.X + this.Width;

    /// <summary>Bottom edge.</summary>
    public float Bottom => this.Y + this.Height;

    /// <summary>Whether the point is inside, edges included.</summary>
    public bool Contains(float x, float y) => x >= this.X && x <= this.Right && y >= this.Y && y <= this.Bottom;

    /// <summary>Grown by <paramref name="amount"/> on every side.</summary>
    public GamepadRect Inflate(float amount) => new(this.X - amount, this.Y - amount, this.Width + amount * 2, this.Height + amount * 2);

    /// <summary>A rectangle of the given size centred on a point.</summary>
    public static GamepadRect FromCenter(float cx, float cy, float width, float height) => new(cx - width / 2f, cy - height / 2f, width, height);
}

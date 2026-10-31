namespace Shiny.Controls.BarcodeReader;

/// <summary>An axis-aligned rectangle. The engine works in pixels; the tracker works in normalized units.</summary>
public readonly record struct BoxF(float X, float Y, float Width, float Height)
{
    /// <summary>X + Width.</summary>
    public float Right => this.X + this.Width;
    /// <summary>Y + Height.</summary>
    public float Bottom => this.Y + this.Height;
    /// <summary>Horizontal center.</summary>
    public float CenterX => this.X + this.Width / 2f;
    /// <summary>Vertical center.</summary>
    public float CenterY => this.Y + this.Height / 2f;
    /// <summary>Width x Height, 0 when either is negative.</summary>
    public float Area => Math.Max(0f, this.Width) * Math.Max(0f, this.Height);

    /// <summary>Intersection over union, 0 when the boxes do not touch.</summary>
    public float IntersectionOverUnion(BoxF other)
    {
        var x0 = Math.Max(this.X, other.X);
        var y0 = Math.Max(this.Y, other.Y);
        var x1 = Math.Min(this.Right, other.Right);
        var y1 = Math.Min(this.Bottom, other.Bottom);
        if (x1 <= x0 || y1 <= y0)
            return 0f;

        var inter = (x1 - x0) * (y1 - y0);
        var union = this.Area + other.Area - inter;
        return union <= 0f ? 0f : inter / union;
    }

    /// <summary>Grow by <paramref name="dx"/> on the left and right and <paramref name="dy"/> on the top and bottom.</summary>
    public BoxF Inflate(float dx, float dy) => new(this.X - dx, this.Y - dy, this.Width + 2 * dx, this.Height + 2 * dy);

    /// <summary>Clip to <c>0..width</c> x <c>0..height</c>.</summary>
    public BoxF Clamp(float width, float height)
    {
        var x0 = Math.Clamp(this.X, 0f, width);
        var y0 = Math.Clamp(this.Y, 0f, height);
        var x1 = Math.Clamp(this.Right, 0f, width);
        var y1 = Math.Clamp(this.Bottom, 0f, height);
        return new BoxF(x0, y0, x1 - x0, y1 - y0);
    }
}

/// <summary>
/// A rotated rectangle around a candidate. <see cref="Angle"/> is the scan direction — for a linear code it
/// runs <i>across</i> the bars, so sampling along it crosses every element.
/// </summary>
/// <param name="CenterX">Center, pixels.</param>
/// <param name="CenterY">Center, pixels.</param>
/// <param name="Angle">Scan axis in radians (0 = left to right).</param>
/// <param name="HalfLength">Half the extent along the scan axis, pixels.</param>
/// <param name="HalfHeight">Half the extent along the bars, pixels.</param>
public readonly record struct OrientedBox(float CenterX, float CenterY, float Angle, float HalfLength, float HalfHeight)
{
    /// <summary>The point <paramref name="u"/> along the scan axis and <paramref name="v"/> along the bars from the center.</summary>
    public (float X, float Y) Point(float u, float v)
    {
        var c = MathF.Cos(this.Angle);
        var s = MathF.Sin(this.Angle);
        return (this.CenterX + u * c - v * s, this.CenterY + u * s + v * c);
    }

    /// <summary>The axis-aligned bounds of the four corners.</summary>
    public BoxF Bounds
    {
        get
        {
            var c = MathF.Abs(MathF.Cos(this.Angle));
            var s = MathF.Abs(MathF.Sin(this.Angle));
            var hw = this.HalfLength * c + this.HalfHeight * s;
            var hh = this.HalfLength * s + this.HalfHeight * c;
            return new BoxF(this.CenterX - hw, this.CenterY - hh, hw * 2, hh * 2);
        }
    }

    /// <summary>A copy narrowed along the scan axis to <paramref name="u0"/>..<paramref name="u1"/> (relative to the center).</summary>
    public OrientedBox Span(float u0, float u1)
    {
        var (x, y) = this.Point((u0 + u1) / 2f, 0f);
        return this with { CenterX = x, CenterY = y, HalfLength = (u1 - u0) / 2f };
    }
}

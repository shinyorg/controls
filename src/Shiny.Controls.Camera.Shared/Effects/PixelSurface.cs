namespace Shiny.Controls.Camera;

/// <summary>
/// A plain 32-bit BGRA pixel buffer handed to a managed effect pass. This is the portable fallback used for
/// <b>still images</b> wherever a platform has no GPU path for an effect (Windows, the bare <c>net11.0</c>
/// head, Android below the shader API level).
/// </summary>
/// <remarks>
/// <para>
/// Rows are tightly packed: <c>stride == Width * 4</c>, channel order B, G, R, A. Alpha is
/// <b>not</b> premultiplied.
/// </para>
/// <para>
/// A managed pass may mutate <see cref="Pixels"/> in place and return the same surface, or allocate a new one
/// (e.g. when the pass changes dimensions). It must never be used on the live preview path — a per-pixel
/// managed loop cannot keep up with a frame budget.
/// </para>
/// </remarks>
public sealed class PixelSurface
{
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="pixels">BGRA32 buffer, exactly <paramref name="width"/> * <paramref name="height"/> * 4 bytes.</param>
    public PixelSurface(int width, int height, byte[] pixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(pixels);

        if (pixels.Length != width * height * 4)
            throw new ArgumentException($"Expected {width * height * 4} bytes for a {width}x{height} BGRA32 surface, got {pixels.Length}.", nameof(pixels));

        this.Width = width;
        this.Height = height;
        this.Pixels = pixels;
    }

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>The BGRA32 pixels, tightly packed (<c>stride == Width * 4</c>).</summary>
    public byte[] Pixels { get; }

    /// <summary>Allocate a blank surface of the same dimensions — for passes that cannot work in place.</summary>
    public PixelSurface CloneShape() => new(this.Width, this.Height, new byte[this.Pixels.Length]);

    /// <summary>Apply a colour matrix to every pixel, in place.</summary>
    public void Apply(ColorMatrix4x5 matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (matrix.IsIdentity)
            return;

        var px = this.Pixels;
        for (var i = 0; i < px.Length; i += 4)
            matrix.ApplyBgra(ref px[i], ref px[i + 1], ref px[i + 2], ref px[i + 3]);
    }
}

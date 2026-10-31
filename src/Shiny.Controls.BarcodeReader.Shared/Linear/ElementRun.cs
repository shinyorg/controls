namespace Shiny.Controls.BarcodeReader.Linear;

/// <summary>
/// The bars and spaces along one scanline, as widths. Element 0 is whatever the line started in (normally
/// the quiet zone), so the first and last elements are the margins either side.
/// </summary>
sealed class ElementRun
{
    public ElementRun(float[] widths, int count, bool firstIsBar)
    {
        this.Widths = widths;
        this.Count = count;
        this.FirstIsBar = firstIsBar;
    }

    public float[] Widths { get; }
    public int Count { get; }
    public bool FirstIsBar { get; }

    public bool IsBar(int index) => (index % 2 == 0) == this.FirstIsBar;

    public float Sum(int start, int length)
    {
        var s = 0f;
        for (var i = start; i < start + length; i++)
            s += this.Widths[i];
        return s;
    }

    /// <summary>The same line read from the other end.</summary>
    public ElementRun Reversed()
    {
        var w = new float[this.Count];
        for (var i = 0; i < this.Count; i++)
            w[i] = this.Widths[this.Count - 1 - i];
        return new ElementRun(w, this.Count, this.IsBar(this.Count - 1));
    }

    /// <summary>
    /// Turn an intensity profile into elements. Edges are the extrema of the profile's derivative, placed to
    /// a fraction of a sample by a parabola through the peak — at a pixel per module the difference between
    /// a 1 and a 2 is a fraction of a pixel, so whole-sample edges are not good enough. Extrema below a
    /// threshold are noise; two in a row of the same sign keep the stronger, so what remains alternates.
    /// </summary>
    /// <param name="profile">Intensity samples, light = high.</param>
    /// <param name="count">Samples in use.</param>
    /// <param name="step">Distance between samples, pixels.</param>
    /// <param name="scratch">Reusable buffer at least <paramref name="count"/> long.</param>
    public static ElementRun? FromProfile(float[] profile, int count, float step, float[] scratch)
    {
        if (count < 8)
            return null;

        // a plain central difference: the scanline is already averaged along the bars, and any smoothing
        // here would blur away exactly the narrow elements a distant code depends on
        var d = scratch;
        var max = 0f;
        d[0] = d[count - 1] = 0;
        for (var i = 1; i < count - 1; i++)
        {
            d[i] = profile[i + 1] - profile[i - 1];
            max = Math.Max(max, Math.Abs(d[i]));
        }

        if (max < 4f)
            return null;

        // blur shrinks the contrast of the narrowest elements first, so the threshold has to sit well below
        // the strongest edge; the alternation rule below does the rest of the cleaning
        var threshold = Math.Max(1f, 0.07f * max);
        var edges = new List<float>(128);
        var strengths = new List<float>(128);
        var signs = new List<bool>(128); // true = falling (light to dark)

        for (var i = 1; i < count - 1; i++)
        {
            var v = d[i];
            var mag = Math.Abs(v);
            if (mag < threshold)
                continue;
            if (mag < Math.Abs(d[i - 1]) || mag <= Math.Abs(d[i + 1]))
                continue;
            var denom = d[i - 1] - 2 * v + d[i + 1];
            var offset = denom == 0 ? 0 : 0.5f * (d[i - 1] - d[i + 1]) / denom;
            offset = Math.Clamp(offset, -0.5f, 0.5f);
            var position = (i + offset) * step;
            var falling = v < 0;

            if (signs.Count > 0 && signs[^1] == falling)
            {
                if (mag > strengths[^1])
                {
                    edges[^1] = position;
                    strengths[^1] = mag;
                }
                continue;
            }

            edges.Add(position);
            strengths.Add(mag);
            signs.Add(falling);
        }

        // Sharpening rings: a step from the quiet zone into the first bar gets a small bump of overshoot in
        // front of it, which reads as a weak rising edge ahead of the real falling one and flips every bar
        // and space on the line. Bars are dark on light, so a line opens with a falling edge and closes with
        // a rising one; a weak edge facing the wrong way at either end is ringing (or noise), not a bar.
        while (edges.Count > 2 && !signs[0] && strengths[0] < 0.4f * strengths[1])
        {
            edges.RemoveAt(0);
            strengths.RemoveAt(0);
            signs.RemoveAt(0);
        }
        while (edges.Count > 2 && signs[^1] && strengths[^1] < 0.4f * strengths[^2])
        {
            edges.RemoveAt(edges.Count - 1);
            strengths.RemoveAt(strengths.Count - 1);
            signs.RemoveAt(signs.Count - 1);
        }

        if (edges.Count < 6)
            return null;

        var widths = new float[edges.Count + 1];
        widths[0] = edges[0];
        for (var i = 1; i < edges.Count; i++)
            widths[i] = edges[i] - edges[i - 1];
        widths[edges.Count] = (count - 1) * step - edges[^1];

        // the line starts in whatever the first edge leaves: a falling edge leaves light behind it
        return new ElementRun(widths, widths.Length, firstIsBar: !signs[0]);
    }
}

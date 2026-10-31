namespace Shiny.Controls.BarcodeReader.Linear;

/// <summary>
/// Reads a linear candidate: samples a fan of parallel scanlines across it, decodes each line in both
/// directions, then votes.
/// </summary>
/// <remarks>
/// <para>
/// Lines are sampled every half pixel with bilinear interpolation, so a code whose bars are barely a pixel
/// wide still shows each edge as a slope the edge finder can place to a fraction of a pixel. Each sample is
/// the average of five points a pixel apart along the bars — the bars are the same for their whole height,
/// the noise is not, so this is free denoising.
/// </para>
/// <para>
/// Checksummed symbologies need two lines to agree, the rest three. If no line reads outright, the EAN
/// evidence from every line whose guards lined up is summed and read once (<see cref="EanDecoder"/>).
/// </para>
/// </remarks>
sealed class LinearReader
{
    float[] profile = new float[1024];
    float[] scratch = new float[1024];
    float[] sharpened = new float[1024];
    float[] blurred = new float[1024];

    // unsharp-mask strength per attempt; 0 = the line as sampled
    static readonly float[] Sharpening = [0f, 1.5f, 3.5f];

    /// <summary>Scanlines per candidate, before the clamp to its height.</summary>
    public int MaxLines { get; set; } = 15;

    public BarcodeRead? Read(LumaImage image, BarcodeCandidate candidate, FormatSet formats)
    {
        var box = RefineAngle(image, candidate.Box);
        var lines = Math.Clamp((int)(box.HalfHeight * 2 / 3), 3, this.MaxLines);

        // reach well past the located texture: the guards sit at its very edge and the quiet zone beyond
        var reach = box.HalfLength * 1.3f + 10f;
        var step = 0.5f;
        var samples = (int)(reach * 2 / step) + 1;
        if (samples > 4096)
        {
            step = reach * 2 / 4095;
            samples = 4096;
        }
        if (this.profile.Length < samples)
        {
            this.profile = new float[samples];
            this.scratch = new float[samples];
            this.sharpened = new float[samples];
            this.blurred = new float[samples];
        }

        var reads = new List<LinearRead>();
        var structures = new List<EanStructure>();
        var votes = new Dictionary<(string, Symbology), (int Count, bool Checksummed, float U0, float U1)>();
        var c = MathF.Cos(box.Angle);
        var s = MathF.Sin(box.Angle);
        float elementSum = 0;
        var elementLines = 0;

        for (var l = 0; l < lines; l++)
        {
            var v = lines == 1 ? 0 : -0.8f * box.HalfHeight + 1.6f * box.HalfHeight * l / (lines - 1);
            this.SampleLine(image, box, v, -reach, step, samples, c, s);

            // Try the line as sampled, then sharpened, then sharpened hard. Defocus merges the two edges of a
            // narrow element into one slope with no peak between them; an unsharp mask pulls them apart
            // again. Most lines read on the first attempt, so a sharp code pays nothing for this.
            for (var attempt = 0; attempt < Sharpening.Length; attempt++)
            {
                var source = this.profile;
                if (attempt > 0)
                {
                    Sharpen(this.profile, this.sharpened, samples, 1f / step, Sharpening[attempt]);
                    source = this.sharpened;
                }

                var run = ElementRun.FromProfile(source, samples, step, this.scratch);
                if (run == null)
                    break; // nothing with contrast on this line; sharpening will not create it

                if (attempt == 0)
                {
                    elementSum += run.Sum(1, run.Count - 2) / Math.Max(1, run.Count - 2);
                    elementLines++;
                }

                reads.Clear();
                DecodeRun(run, formats, reads, structures);
                var reversed = run.Reversed();
                var forwardCount = reads.Count;
                var forwardStructures = structures.Count;
                DecodeRun(reversed, formats, reads, structures);
                for (var k = forwardStructures; k < structures.Count; k++)
                    structures[k] = structures[k] with { Reversed = true };

                // one vote per line per value, whichever direction(s) read it
                var seen = new HashSet<(string, Symbology)>();
                for (var r = 0; r < reads.Count; r++)
                {
                    var read = reads[r];
                    var key = (read.Text, read.Symbology);
                    if (!seen.Add(key))
                        continue;

                    var (u0, u1) = Extent(r < forwardCount ? run : reversed, read, r >= forwardCount, -reach, (samples - 1) * step);
                    votes[key] = votes.TryGetValue(key, out var existing)
                        ? (existing.Count + 1, read.IsChecksummed, Math.Min(existing.U0, u0), Math.Max(existing.U1, u1))
                        : (1, read.IsChecksummed, u0, u1);
                }

                if (reads.Count > 0)
                    break;
            }
        }

        if (elementLines > 0)
            candidate.ElementWidth = elementSum / elementLines;

        var best = default(KeyValuePair<(string, Symbology), (int Count, bool Checksummed, float U0, float U1)>);
        foreach (var kv in votes)
        {
            if (kv.Value.Count > best.Value.Count)
                best = kv;
        }

        if (best.Value.Count > 0)
        {
            var needed = best.Value.Checksummed ? 2 : 3;
            // a short candidate only has room for a couple of lines; one checksummed read is all it can give
            if (lines <= 4 && best.Value.Checksummed)
                needed = 1;
            if (best.Value.Count >= needed)
                return new BarcodeRead(best.Key.Item1, best.Key.Item2, box.Span(best.Value.U0, best.Value.U1), best.Value.Count, best.Value.Checksummed);
        }

        return Fuse(structures, formats, box);
    }

    static void DecodeRun(ElementRun run, FormatSet formats, List<LinearRead> reads, List<EanStructure> structures)
    {
        if (formats.Has(Symbology.Ean13) || formats.Has(Symbology.UpcA) || formats.Has(Symbology.Ean8) || formats.Has(Symbology.UpcE))
            EanDecoder.Decode(run, formats, reads, structures);
        if (formats.Has(Symbology.Code128))
            Code128Decoder.Decode(run, reads);
        if (formats.Has(Symbology.Code39))
            TwoWidthDecoders.DecodeCode39(run, reads);
        if (formats.Has(Symbology.Itf))
            TwoWidthDecoders.DecodeItf(run, reads);
    }

    void SampleLine(LumaImage image, OrientedBox box, float v, float u0, float step, int samples, float c, float s)
    {
        // five taps a pixel apart along the bars: the bars are the same all the way up, the noise is not
        var ox = box.CenterX - v * s;
        var oy = box.CenterY + v * c;
        for (var i = 0; i < samples; i++)
        {
            var u = u0 + i * step;
            var x = ox + u * c;
            var y = oy + u * s;
            this.profile[i] = (image.Sample(x + 2 * s, y - 2 * c) + image.Sample(x + s, y - c) + image.Sample(x, y)
                + image.Sample(x - s, y + c) + image.Sample(x - 2 * s, y + 2 * c)) / 5f;
        }
    }

    /// <summary>
    /// The locator's angle comes from whole cells, half of which may hang off a small code, and a few
    /// degrees of skew is enough to smear a code whose bars are a pixel wide once five taps are averaged
    /// along them. Re-measure it from every pixel inside the box.
    /// </summary>
    static OrientedBox RefineAngle(LumaImage image, OrientedBox box)
    {
        var bounds = box.Bounds.Clamp(image.Width, image.Height);
        var x0 = Math.Max(1, (int)bounds.X);
        var y0 = Math.Max(1, (int)bounds.Y);
        var x1 = Math.Min(image.Width - 1, (int)bounds.Right);
        var y1 = Math.Min(image.Height - 1, (int)bounds.Bottom);
        var c = MathF.Cos(box.Angle);
        var s = MathF.Sin(box.Angle);
        var p = image.Pixels;
        var stride = image.Stride;
        double xx = 0, yy = 0, xy = 0;

        for (var y = y0; y < y1; y++)
        {
            var dy = y - box.CenterY;
            for (var x = x0; x < x1; x++)
            {
                var dx = x - box.CenterX;
                if (MathF.Abs(dx * c + dy * s) > box.HalfLength || MathF.Abs(-dx * s + dy * c) > box.HalfHeight)
                    continue;

                var i = y * stride + x;
                int gx = p[i + 1] - p[i - 1];
                int gy = p[i + stride] - p[i - stride];
                xx += gx * gx;
                yy += gy * gy;
                xy += gx * gy;
            }
        }

        if (xx + yy <= 0)
            return box;

        var refined = 0.5f * MathF.Atan2((float)(2 * xy), (float)(xx - yy));
        // keep the locator's direction (the tensor angle is only defined up to a half turn)
        if (MathF.Cos(refined - box.Angle) < 0)
            refined += MathF.PI;
        return box with { Angle = refined };
    }

    // profile + amount * (profile - gaussian(profile)), sigma one pixel
    void Sharpen(float[] src, float[] dst, int count, float samplesPerPixel, float amount)
    {
        var sigma = samplesPerPixel;
        var r = Math.Max(1, (int)MathF.Ceiling(sigma * 2.5f));
        Span<float> k = stackalloc float[2 * r + 1];
        var sum = 0f;
        for (var i = -r; i <= r; i++)
            sum += k[i + r] = MathF.Exp(-i * i / (2 * sigma * sigma));

        for (var i = 0; i < count; i++)
        {
            var acc = 0f;
            for (var j = -r; j <= r; j++)
                acc += src[Math.Clamp(i + j, 0, count - 1)] * k[j + r];
            this.blurred[i] = acc / sum;
        }
        for (var i = 0; i < count; i++)
            dst[i] = src[i] + amount * (src[i] - this.blurred[i]);
    }

    // where along the scan axis (relative to the box center) a read sat, for a tight result box
    static (float, float) Extent(ElementRun run, LinearRead read, bool reversed, float u0, float length)
    {
        var a = run.Sum(0, read.Start);
        var b = run.Sum(0, Math.Min(run.Count, read.End + 1));
        if (reversed)
            (a, b) = (length - b, length - a);
        return (u0 + a, u0 + b);
    }

    static BarcodeRead? Fuse(List<EanStructure> structures, FormatSet formats, OrientedBox box)
    {
        foreach (var symbology in (ReadOnlySpan<Symbology>)[Symbology.Ean13, Symbology.Ean8, Symbology.UpcE])
        foreach (var reversed in (ReadOnlySpan<bool>)[false, true])
        {
            float[][]? sum = null;
            var lines = 0;
            foreach (var s in structures)
            {
                if (s.Symbology != symbology || s.Reversed != reversed)
                    continue;

                if (sum == null)
                {
                    sum = new float[s.Costs.Length][];
                    for (var p = 0; p < sum.Length; p++)
                        sum[p] = new float[s.Costs[p].Length];
                }
                // a line whose worst digit fits nothing is a misaligned guess, not evidence
                if (s.MaxBestCost > 6f)
                    continue;

                for (var p = 0; p < sum.Length; p++)
                for (var k = 0; k < sum[p].Length; k++)
                    sum[p][k] += Math.Min(s.Costs[p][k], 8f);
                lines++;
            }

            if (sum == null || lines < 3)
                continue;

            if (EanDecoder.Resolve(symbology, sum, formats, lines) is { } read)
                return new BarcodeRead(read.Text, read.Symbology, box, lines, true);
        }
        return null;
    }
}

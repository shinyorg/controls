namespace Shiny.Controls.BarcodeReader;

/// <summary>
/// Finds regions that look like barcodes without trying to decode anything, so decoding effort is spent only
/// where a code is.
/// </summary>
/// <remarks>
/// <para>
/// The plane is divided into square cells and each cell accumulates the structure tensor of its gradients
/// (Σgx², Σgy², Σgxgy). From that, per cell: the <b>energy</b> (is there texture at all), the
/// <b>coherence</b> (do the gradients all point one way — parallel bars do, a 2D code or text does not)
/// and the dominant angle. A cell also keeps the plain sum of its gradients next to the sum of their
/// magnitudes: across a barcode the gradients flip sign at every edge and cancel, across a lone edge (the
/// side of a box, a shadow) they all agree and do not. That one ratio throws out most of the coherent
/// texture that is not a code.
/// </para>
/// <para>
/// Gradients are taken at full resolution — a code far from the camera has bars a pixel or two wide, and
/// any downscale before this point averages them into flat grey. Every second row is enough, which halves
/// the cost: the tensor still sees gradients in every direction.
/// </para>
/// </remarks>
public sealed class BarcodeLocator
{
    /// <summary>Cell edge in pixels; 0 picks one from the image size (about 1/68 of the short side, at least 12).</summary>
    public int CellSize { get; set; }

    /// <summary>Mean squared gradient a cell needs before it counts as texture. Lower finds fainter codes and more noise.</summary>
    public float MinEnergy { get; set; } = 40f;

    /// <summary>Coherence at or above which a cell is bars.</summary>
    public float LinearCoherence { get; set; } = 0.7f;

    /// <summary>Coherence at or below which a textured cell is 2D texture.</summary>
    public float MatrixCoherence { get; set; } = 0.45f;

    /// <summary>The most each kind of candidate returned, strongest first.</summary>
    public int MaxLinearCandidates { get; set; } = 8;

    /// <inheritdoc cref="MaxLinearCandidates"/>
    public int MaxMatrixCandidates { get; set; } = 4;

    // reused frame to frame — the pipeline runs one pass at a time
    float[] sxx = [], syy = [], sxy = [], sgx = [], sgy = [], sabs = [];
    int[] count = [];
    byte[] kind = [];
    float[] angle = [], strength = [];
    int[] label = [];

    const byte None = 0, LinearCell = 1, MatrixCell = 2;

    /// <summary>Locate candidates in <paramref name="image"/>, optionally only inside <paramref name="region"/> (pixels).</summary>
    public List<BarcodeCandidate> Locate(LumaImage image, BoxF? region = null)
    {
        var r = (region ?? new BoxF(0, 0, image.Width, image.Height)).Clamp(image.Width, image.Height);
        var x0 = (int)r.X;
        var y0 = (int)r.Y;
        var rw = (int)r.Width;
        var rh = (int)r.Height;

        var cell = this.CellSize > 0 ? this.CellSize : Math.Max(12, Math.Min(image.Width, image.Height) / 68);
        var cols = rw / cell;
        var rows = rh / cell;
        if (cols < 2 || rows < 2)
            return [];

        this.Accumulate(image, x0, y0, cell, cols, rows);
        this.Classify(cols * rows);
        return this.Group(x0, y0, cell, cols, rows);
    }

    void Ensure(int n)
    {
        if (this.sxx.Length >= n)
        {
            Array.Clear(this.sxx, 0, n);
            Array.Clear(this.syy, 0, n);
            Array.Clear(this.sxy, 0, n);
            Array.Clear(this.sgx, 0, n);
            Array.Clear(this.sgy, 0, n);
            Array.Clear(this.sabs, 0, n);
            Array.Clear(this.count, 0, n);
            return;
        }

        this.sxx = new float[n];
        this.syy = new float[n];
        this.sxy = new float[n];
        this.sgx = new float[n];
        this.sgy = new float[n];
        this.sabs = new float[n];
        this.count = new int[n];
        this.kind = new byte[n];
        this.angle = new float[n];
        this.strength = new float[n];
        this.label = new int[n];
    }

    void Accumulate(LumaImage image, int x0, int y0, int cell, int cols, int rows)
    {
        this.Ensure(cols * rows);
        var p = image.Pixels;
        var stride = image.Stride;

        for (var cy = 0; cy < rows; cy++)
        {
            var yStart = Math.Max(1, y0 + cy * cell);
            var yEnd = Math.Min(image.Height - 1, y0 + (cy + 1) * cell);
            for (var cx = 0; cx < cols; cx++)
            {
                var xStart = Math.Max(1, x0 + cx * cell);
                var xEnd = Math.Min(image.Width - 1, x0 + (cx + 1) * cell);
                long xx = 0, yy = 0, xy = 0, gxs = 0, gys = 0, abs = 0;
                var n = 0;
                for (var y = yStart; y < yEnd; y += 2)
                {
                    var row = y * stride;
                    for (var x = xStart; x < xEnd; x++)
                    {
                        var i = row + x;
                        int gx = p[i + 1] - p[i - 1];
                        int gy = p[i + stride] - p[i - stride];
                        xx += gx * gx;
                        yy += gy * gy;
                        xy += gx * gy;
                        gxs += gx;
                        gys += gy;
                        abs += Math.Abs(gx) + Math.Abs(gy);
                    }
                    n += xEnd - xStart;
                }

                var c = cy * cols + cx;
                this.sxx[c] = xx;
                this.syy[c] = yy;
                this.sxy[c] = xy;
                this.sgx[c] = gxs;
                this.sgy[c] = gys;
                this.sabs[c] = abs;
                this.count[c] = n;
            }
        }
    }

    void Classify(int n)
    {
        // Sensor noise alone has gradient energy, and in a dim scene it clears any fixed bar. Most of a frame
        // is not barcode, so the median cell is the noise floor; texture has to stand well above it.
        var floor = this.NoiseFloor(n);
        var minEnergy = Math.Max(this.MinEnergy, 4f * floor);

        for (var c = 0; c < n; c++)
        {
            this.kind[c] = None;
            var count = this.count[c];
            if (count == 0)
                continue;

            var trace = this.sxx[c] + this.syy[c];
            var energy = trace / count;
            if (energy < minEnergy || this.sabs[c] <= 0)
                continue;

            // gradients that do not cancel are one edge, not many
            var net = MathF.Sqrt(this.sgx[c] * this.sgx[c] + this.sgy[c] * this.sgy[c]);
            if (net / this.sabs[c] > 0.45f)
                continue;

            var diff = this.sxx[c] - this.syy[c];
            var coherence = MathF.Sqrt(diff * diff + 4 * this.sxy[c] * this.sxy[c]) / trace;
            if (coherence >= this.LinearCoherence)
            {
                this.kind[c] = LinearCell;
                this.angle[c] = 0.5f * MathF.Atan2(2 * this.sxy[c], diff); // the gradient direction — across the bars
                this.strength[c] = energy * coherence;
            }
            else if (coherence <= this.MatrixCoherence)
            {
                this.kind[c] = MatrixCell;
                this.strength[c] = energy * (1 - coherence);
            }
        }
    }

    float NoiseFloor(int n)
    {
        // a sample of cells is plenty for a median
        var step = Math.Max(1, n / 512);
        var sample = new float[(n + step - 1) / step];
        var k = 0;
        for (var c = 0; c < n && k < sample.Length; c += step)
            sample[k++] = this.count[c] == 0 ? 0 : (this.sxx[c] + this.syy[c]) / this.count[c];
        Array.Sort(sample, 0, k);
        return k == 0 ? 0 : sample[k / 2];
    }

    List<BarcodeCandidate> Group(int x0, int y0, int cell, int cols, int rows)
    {
        var n = cols * rows;
        Array.Clear(this.label, 0, n);
        var linear = new List<BarcodeCandidate>();
        var matrix = new List<BarcodeCandidate>();
        var queue = new Queue<int>();
        var members = new List<int>();
        var next = 0;

        for (var seed = 0; seed < n; seed++)
        {
            if (this.kind[seed] == None || this.label[seed] != 0)
                continue;

            next++;
            var k = this.kind[seed];
            members.Clear();
            queue.Enqueue(seed);
            this.label[seed] = next;

            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                members.Add(c);
                var cx = c % cols;
                var cy = c / cols;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = cx + dx;
                    var ny = cy + dy;
                    if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= cols || ny >= rows)
                        continue;

                    var nc = ny * cols + nx;
                    if (this.label[nc] != 0 || this.kind[nc] != k)
                        continue;
                    if (k == LinearCell && AngleBetween(this.angle[c], this.angle[nc]) > 0.35f)
                        continue;

                    this.label[nc] = next;
                    queue.Enqueue(nc);
                }
            }

            if (k == LinearCell)
            {
                if (this.BuildLinear(members, x0, y0, cell, cols) is { } candidate)
                    linear.Add(candidate);
            }
            else if (this.BuildMatrix(members, x0, y0, cell, cols) is { } candidate)
            {
                matrix.Add(candidate);
            }
        }

        MergeCollinear(linear, cell);
        linear.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        matrix.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        if (linear.Count > this.MaxLinearCandidates)
            linear.RemoveRange(this.MaxLinearCandidates, linear.Count - this.MaxLinearCandidates);
        if (matrix.Count > this.MaxMatrixCandidates)
            matrix.RemoveRange(this.MaxMatrixCandidates, matrix.Count - this.MaxMatrixCandidates);

        linear.AddRange(matrix);
        return linear;
    }

    BarcodeCandidate? BuildLinear(List<int> members, int x0, int y0, int cell, int cols)
    {
        if (members.Count < 3)
            return null;

        // average the angle on the doubled circle so 179° and 1° agree
        float c2 = 0, s2 = 0, score = 0, sx = 0, sy = 0;
        foreach (var c in members)
        {
            var w = this.strength[c];
            c2 += MathF.Cos(2 * this.angle[c]) * w;
            s2 += MathF.Sin(2 * this.angle[c]) * w;
            score += w;
            sx += (x0 + (c % cols + 0.5f) * cell) * w;
            sy += (y0 + (c / cols + 0.5f) * cell) * w;
        }
        var theta = 0.5f * MathF.Atan2(s2, c2);
        var cx = sx / score;
        var cy = sy / score;
        var ux = MathF.Cos(theta);
        var uy = MathF.Sin(theta);

        float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
        foreach (var c in members)
        {
            var px = x0 + (c % cols + 0.5f) * cell - cx;
            var py = y0 + (c / cols + 0.5f) * cell - cy;
            var u = px * ux + py * uy;
            var v = -px * uy + py * ux;
            minU = Math.Min(minU, u);
            maxU = Math.Max(maxU, u);
            minV = Math.Min(minV, v);
            maxV = Math.Max(maxV, v);
        }

        // across the bars it has to span more than a cell, or it is a single stripe
        if (maxU - minU < cell)
            return null;

        // cell centers sit half a cell in from the texture's edge; the decoder adds its own quiet-zone margin
        var half = cell / 2f;
        var mu = (minU + maxU) / 2f;
        var mv = (minV + maxV) / 2f;
        var box = new OrientedBox(
            cx + mu * ux - mv * uy,
            cy + mu * uy + mv * ux,
            theta,
            (maxU - minU) / 2f + half,
            (maxV - minV) / 2f + half);
        return new BarcodeCandidate(CandidateKind.Linear, box, score);
    }

    BarcodeCandidate? BuildMatrix(List<int> members, int x0, int y0, int cell, int cols)
    {
        if (members.Count < 4)
            return null;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = 0, maxY = 0;
        float score = 0;
        foreach (var c in members)
        {
            var cx = c % cols;
            var cy = c / cols;
            minX = Math.Min(minX, cx);
            maxX = Math.Max(maxX, cx);
            minY = Math.Min(minY, cy);
            maxY = Math.Max(maxY, cy);
            score += this.strength[c];
        }

        var w = maxX - minX + 1;
        var h = maxY - minY + 1;
        // 2D codes are roughly square; a line of text is not, and a sparse blob is not a code
        if (w > 3 * h || h > 3 * w || members.Count < 0.45f * w * h)
            return null;

        var box = new OrientedBox(
            x0 + (minX + maxX + 1) * cell / 2f,
            y0 + (minY + maxY + 1) * cell / 2f,
            0f,
            w * cell / 2f,
            h * cell / 2f);
        return new BarcodeCandidate(CandidateKind.Matrix, box, score);
    }

    // A cell that lands on one wide bar holds a single edge and is rejected, so a close code (or one with
    // a scratch or a glare stripe across it) arrives as several pieces lined up along one axis. Join pieces
    // that share an angle, sit on the same line, and leave a gap of no more than a couple of cells.
    static void MergeCollinear(List<BarcodeCandidate> list, int cell)
    {
        bool merged;
        do
        {
            merged = false;
            for (var i = 0; i < list.Count && !merged; i++)
            for (var j = i + 1; j < list.Count && !merged; j++)
            {
                if (TryMerge(list[i], list[j], cell) is not { } m)
                    continue;
                list[i] = m;
                list.RemoveAt(j);
                merged = true;
            }
        }
        while (merged);
    }

    static BarcodeCandidate? TryMerge(BarcodeCandidate a, BarcodeCandidate b, int cell)
    {
        if (AngleBetween(a.Box.Angle, b.Box.Angle) > 0.2f)
            return null;

        // work in a's frame, with b's angle folded onto a's direction
        var theta = a.Box.Angle;
        var ux = MathF.Cos(theta);
        var uy = MathF.Sin(theta);
        var dx = b.Box.CenterX - a.Box.CenterX;
        var dy = b.Box.CenterY - a.Box.CenterY;
        var du = dx * ux + dy * uy;
        var dv = -dx * uy + dy * ux;
        if (MathF.Abs(dv) > Math.Max(a.Box.HalfHeight, b.Box.HalfHeight))
            return null;

        var gap = MathF.Abs(du) - a.Box.HalfLength - b.Box.HalfLength;
        if (gap > 2.5f * cell)
            return null;

        var u0 = Math.Min(-a.Box.HalfLength, du - b.Box.HalfLength);
        var u1 = Math.Max(a.Box.HalfLength, du + b.Box.HalfLength);
        var v0 = Math.Min(-a.Box.HalfHeight, dv - b.Box.HalfHeight);
        var v1 = Math.Max(a.Box.HalfHeight, dv + b.Box.HalfHeight);
        var mu = (u0 + u1) / 2f;
        var mv = (v0 + v1) / 2f;
        var box = new OrientedBox(
            a.Box.CenterX + mu * ux - mv * uy,
            a.Box.CenterY + mu * uy + mv * ux,
            theta,
            (u1 - u0) / 2f,
            (v1 - v0) / 2f);
        return new BarcodeCandidate(CandidateKind.Linear, box, a.Score + b.Score);
    }

    static float AngleBetween(float a, float b)
    {
        var d = MathF.Abs(a - b) % MathF.PI;
        return Math.Min(d, MathF.PI - d);
    }
}

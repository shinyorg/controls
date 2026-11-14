using Microsoft.Maui.Graphics;
using Shiny.Controls.Camera;

namespace Shiny.Maui.Controls.Analysis.Ai;

/// <summary>
/// Dependency-free document-presence detector used wherever there's no native rectangle detector (Android,
/// Windows, bare net10.0). It only answers "is a document-shaped bright region filling much of the frame, and
/// where?" — it does <b>not</b> read text. That's the whole point of the AI scanner: this cheap gate runs
/// every frame, and the expensive model call only fires once a document is actually present. Operates on the
/// frame's cached 8-bit luminance plane (no color copy), downscales it, Otsu-thresholds, takes the largest
/// bright connected region and reads its four extreme corners, then maps them into upright, mirror-corrected
/// normalized space (the <see cref="OverlayBox"/> / <see cref="DocumentQuad"/> contract). Best-effort and
/// tuned for a document that fills a good part of a reasonably-lit frame.
/// </summary>
static class ManagedDocumentEdgeDetector
{
    /// <summary>
    /// Detect the document outline in <paramref name="frame"/>, or <c>null</c> when no plausible document is
    /// found. Corners are normalized (0..1) in upright image space.
    /// </summary>
    public static DocumentQuad? Detect(VideoFrame frame)
    {
        var lum = frame.GetLuminance();
        int width = frame.Width, height = frame.Height;
        if (width < 32 || height < 32 || lum.Length < width * height)
            return null;

        // 1) downscale (nearest) to bound cost
        const int target = 240;
        var scale = Math.Max(1, Math.Max(width, height) / target);
        int sw = width / scale, sh = height / scale;
        if (sw < 16 || sh < 16)
            return null;

        var small = new byte[sw * sh];
        for (var y = 0; y < sh; y++)
        {
            var sy = y * scale;
            for (var x = 0; x < sw; x++)
                small[y * sw + x] = lum[sy * width + x * scale];
        }

        // 2) Otsu threshold; 3) binary foreground (document assumed brighter than its surround)
        var thresh = Otsu(small);
        var fg = new bool[sw * sh];
        for (var i = 0; i < small.Length; i++)
            fg[i] = small[i] > thresh;

        // 4) largest connected component + its extreme corners (iterative flood fill, 4-connectivity)
        var visited = new bool[sw * sh];
        var stack = new Stack<int>();
        var bestArea = 0;
        (int tl, int tr, int br, int bl) best = default;

        for (var seed = 0; seed < fg.Length; seed++)
        {
            if (!fg[seed] || visited[seed])
                continue;

            var area = 0;
            int minSum = int.MaxValue, maxSum = int.MinValue, minDiff = int.MaxValue, maxDiff = int.MinValue;
            int iTL = seed, iTR = seed, iBR = seed, iBL = seed;

            visited[seed] = true;
            stack.Push(seed);
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                int px = p % sw, py = p / sw;
                area++;
                int sum = px + py, diff = px - py;
                if (sum < minSum) { minSum = sum; iTL = p; }
                if (sum > maxSum) { maxSum = sum; iBR = p; }
                if (diff > maxDiff) { maxDiff = diff; iTR = p; }
                if (diff < minDiff) { minDiff = diff; iBL = p; }

                if (px > 0 && fg[p - 1] && !visited[p - 1]) { visited[p - 1] = true; stack.Push(p - 1); }
                if (px < sw - 1 && fg[p + 1] && !visited[p + 1]) { visited[p + 1] = true; stack.Push(p + 1); }
                if (py > 0 && fg[p - sw] && !visited[p - sw]) { visited[p - sw] = true; stack.Push(p - sw); }
                if (py < sh - 1 && fg[p + sw] && !visited[p + sw]) { visited[p + sw] = true; stack.Push(p + sw); }
            }

            if (area > bestArea)
            {
                bestArea = area;
                best = (iTL, iTR, iBR, iBL);
            }
        }

        // 5) sanity: plausible document area fraction
        var frac = bestArea / (float)(sw * sh);
        if (frac is < 0.15f or > 0.99f)
            return null;

        // sensor-space normalized corners (origin top-left)
        PointF N(int idx) => new((idx % sw) / (float)sw, (idx / sw) / (float)sh);
        PointF tl = N(best.tl), tr = N(best.tr), br = N(best.br), bl = N(best.bl);

        // 6) sanity: minimum quad extent so we don't ship a sliver
        var w = Math.Max(Len(tl, tr), Len(bl, br));
        var h = Math.Max(Len(tl, bl), Len(tr, br));
        if (w <= 0.25f || h <= 0.25f)
            return null;

        // map sensor-space corners into upright, mirror-corrected space for the overlay + crop
        PointF U(PointF p) => CoordinateTransform.ApplyOrientation(p, frame.Rotation, frame.IsMirrored);
        return new DocumentQuad(U(tl), U(tr), U(br), U(bl));
    }

    static float Len(PointF a, PointF b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    static byte Otsu(byte[] data)
    {
        Span<int> hist = stackalloc int[256];
        foreach (var v in data)
            hist[v]++;

        var total = data.Length;
        float sum = 0;
        for (var i = 0; i < 256; i++)
            sum += i * hist[i];

        float sumB = 0;
        var wB = 0;
        float maxVar = 0;
        byte thresh = 127;
        for (var t = 0; t < 256; t++)
        {
            wB += hist[t];
            if (wB == 0)
                continue;
            var wF = total - wB;
            if (wF == 0)
                break;
            sumB += t * hist[t];
            var mB = sumB / wB;
            var mF = (sum - sumB) / wF;
            var between = wB * (float)wF * (mB - mF) * (mB - mF);
            if (between > maxVar)
            {
                maxVar = between;
                thresh = (byte)t;
            }
        }
        return thresh;
    }
}

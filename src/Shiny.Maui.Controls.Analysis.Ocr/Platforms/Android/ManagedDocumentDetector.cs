using Microsoft.Maui.Graphics;

namespace Shiny.Maui.Controls.Analysis.Ocr;

/// <summary>
/// Lightweight, dependency-free document-quad detector for the Android rectifier (no OpenCV, so the package
/// stays trim/AOT-clean). Downscales the luminance plane, Otsu-thresholds it, takes the largest bright
/// connected region, and reads its four extreme corners. Best-effort — tuned for a document that fills much
/// of a reasonably-lit frame; returns <c>false</c> when it can't find a plausible quad (the caller then falls
/// back to whole-frame OCR). Corners are returned normalized (0..1) in sensor space (origin top-left).
/// </summary>
static class ManagedDocumentDetector
{
    public static bool TryDetect(ReadOnlySpan<byte> luminance, int width, int height,
        out PointF tl, out PointF tr, out PointF br, out PointF bl)
    {
        tl = tr = br = bl = default;
        if (width < 32 || height < 32 || luminance.Length < width * height)
            return false;

        // 1) downscale (nearest) to bound cost
        const int target = 240;
        var scale = Math.Max(1, Math.Max(width, height) / target);
        int sw = width / scale, sh = height / scale;
        if (sw < 16 || sh < 16)
            return false;

        var small = new byte[sw * sh];
        for (var y = 0; y < sh; y++)
        {
            var sy = y * scale;
            for (var x = 0; x < sw; x++)
                small[y * sw + x] = luminance[sy * width + x * scale];
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
            return false;

        PointF N(int idx) => new((idx % sw) / (float)sw, (idx / sw) / (float)sh);
        tl = N(best.tl); tr = N(best.tr); br = N(best.br); bl = N(best.bl);

        // 6) sanity: minimum quad extent so we don't deskew a sliver
        var w = Math.Max(Len(tl, tr), Len(bl, br));
        var h = Math.Max(Len(tl, bl), Len(tr, br));
        return w > 0.25f && h > 0.25f;
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

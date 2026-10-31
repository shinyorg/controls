namespace Shiny.Controls.BarcodeReader.Linear;

/// <summary>
/// Matches a run of measured elements against a table of module-width patterns.
/// </summary>
/// <remarks>
/// <para>
/// The match leans on <b>edge-to-similar-edge</b> distances — leading edge of one bar to the leading edge of
/// the next, and the same for spaces — rather than on the element widths themselves. Blur and ink spread
/// fatten every bar and thin every space by the same amount, which wrecks the widths but cancels out of a
/// bar+space sum. That is what keeps a code readable when it is far enough away to be soft. The widths
/// still count, at a lower weight, because a few pairs of patterns (EAN 1/7, 2/8) are identical in
/// edge-to-similar-edge terms and only the widths tell them apart.
/// </para>
/// <para>
/// Every measurement is normalized by the symbol's own total width, so perspective (one end of the code
/// further away than the other) only has to be roughly constant across a single character.
/// </para>
/// </remarks>
static class PatternMatcher
{
    const float WidthWeight = 0.3f;

    /// <summary>
    /// Best pattern for the <paramref name="length"/> elements at <paramref name="start"/>. Returns the
    /// pattern index (or -1), with its cost and the runner-up's.
    /// </summary>
    public static int Match(ElementRun run, int start, int length, int modules, int[][] patterns, out float best, out float second, float[]? costs = null)
    {
        best = second = float.MaxValue;
        if (start < 0 || start + length > run.Count)
            return -1;

        Span<float> n = stackalloc float[length];
        var total = run.Sum(start, length);
        if (total <= 0)
            return -1;

        var scale = modules / total;
        for (var i = 0; i < length; i++)
            n[i] = run.Widths[start + i] * scale;

        var bestIndex = -1;
        for (var p = 0; p < patterns.Length; p++)
        {
            var pattern = patterns[p];
            if (pattern.Length != length)
            {
                if (costs != null)
                    costs[p] = float.MaxValue;
                continue;
            }

            var cost = 0f;
            for (var i = 0; i < length - 1; i++)
            {
                var t = n[i] + n[i + 1] - (pattern[i] + pattern[i + 1]);
                cost += t * t;
            }
            for (var i = 0; i < length; i++)
            {
                var e = n[i] - pattern[i];
                cost += WidthWeight * e * e;
            }

            if (costs != null)
                costs[p] = cost;

            if (cost < best)
            {
                second = best;
                best = cost;
                bestIndex = p;
            }
            else if (cost < second)
            {
                second = cost;
            }
        }
        return bestIndex;
    }

    /// <summary>Parse "3211" style width strings.</summary>
    public static int[][] Parse(params string[] patterns)
    {
        var result = new int[patterns.Length][];
        for (var i = 0; i < patterns.Length; i++)
        {
            result[i] = new int[patterns[i].Length];
            for (var j = 0; j < patterns[i].Length; j++)
                result[i][j] = patterns[i][j] - '0';
        }
        return result;
    }
}

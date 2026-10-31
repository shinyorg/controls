namespace Shiny.Controls.BarcodeReader.Linear;

/// <summary>EAN-13 / UPC-A, EAN-8 and UPC-E.</summary>
/// <remarks>
/// Besides a plain read, every line whose guards line up yields a <see cref="EanStructure"/>: the match
/// cost of every possible digit at every position. A line too blurred to read outright usually gets most
/// digits right and a few wrong — and not the same few as the next line — so summing the costs across
/// lines and reading the sum recovers codes no single line can. The check digit then has to agree.
/// </remarks>
static class EanDecoder
{
    // L (odd parity) 0-9, then G (even parity) 0-9. R codes have the L widths with the colours swapped.
    internal static readonly int[][] LG = PatternMatcher.Parse(
        "3211", "2221", "2122", "1411", "1132", "1231", "1114", "1312", "1213", "3112",
        "1123", "1222", "2212", "1141", "2311", "1321", "4111", "2131", "3121", "2113");

    static readonly int[][] L = LG[..10];

    // EAN-13 leading digit from the parity of the left six (bit set = G), first digit in bit 5
    static readonly int[] FirstDigitParity = [0x00, 0x0B, 0x0D, 0x0E, 0x13, 0x19, 0x1C, 0x15, 0x16, 0x1A];

    // UPC-E number system 0 / 1 and check digit from the parity of the six digits (bit set = G)
    static readonly int[][] UpcEParity =
    [
        [0x38, 0x34, 0x32, 0x31, 0x2C, 0x26, 0x23, 0x2A, 0x29, 0x25],
        [0x07, 0x0B, 0x0D, 0x0E, 0x13, 0x19, 0x1C, 0x15, 0x16, 0x1A]
    ];

    // a digit this far off every pattern is not a digit
    const float MaxDigitCost = 2.5f;

    public static void Decode(ElementRun run, FormatSet formats, List<LinearRead> reads, List<EanStructure> structures)
    {
        var ean13 = formats.Has(Symbology.Ean13) || formats.Has(Symbology.UpcA);
        var ean8 = formats.Has(Symbology.Ean8);
        var upcE = formats.Has(Symbology.UpcE);

        for (var i = 1; i < run.Count - 1; i++)
        {
            if (!run.IsBar(i))
                continue;

            if (ean13 && TryStructure(run, i, Symbology.Ean13) is { } s13)
                Accept(s13, formats, reads, structures);
            if (ean8 && TryStructure(run, i, Symbology.Ean8) is { } s8)
                Accept(s8, formats, reads, structures);
            if (upcE && TryStructure(run, i, Symbology.UpcE) is { } se)
                Accept(se, formats, reads, structures);
        }
    }

    static void Accept(EanStructure structure, FormatSet formats, List<LinearRead> reads, List<EanStructure> structures)
    {
        structures.Add(structure);
        if (structure.MaxBestCost <= MaxDigitCost && Resolve(structure.Symbology, structure.Costs, formats) is { } read)
            reads.Add(read with { Start = structure.Start, End = structure.End });
    }

    static EanStructure? TryStructure(ElementRun run, int g, Symbology symbology)
    {
        // element counts and module widths: guard 3, digits 4 each, center 5, end guard 3 (UPC-E: 6)
        var (elements, modules, leftDigits, rightDigits) = symbology switch
        {
            Symbology.Ean13 => (59, 95, 6, 6),
            Symbology.Ean8 => (43, 67, 4, 4),
            _ => (33, 51, 6, 0)
        };
        if (g + elements >= run.Count)
            return null;

        var span = run.Sum(g, elements);
        var m = span / modules;
        if (m < 0.4f)
            return null;

        // quiet zones: the standard asks for 7+ modules; a tight crop or a busy label rarely gives that
        if (run.Widths[g - 1] < 3f * m || run.Widths[g + elements] < 3f * m)
            return null;

        if (!Guard(run, g, 3, m))
            return null;

        var center = g + 3 + leftDigits * 4;
        if (symbology == Symbology.UpcE)
        {
            if (!Guard(run, center, 6, m))
                return null;
        }
        else if (!Guard(run, center, 5, m) || !Guard(run, center + 5 + rightDigits * 4, 3, m))
        {
            return null;
        }

        var positions = leftDigits + rightDigits;
        var costs = new float[positions][];
        var maxBest = 0f;
        for (var p = 0; p < positions; p++)
        {
            var left = p < leftDigits;
            var start = left ? g + 3 + p * 4 : center + 5 + (p - leftDigits) * 4;
            // EAN-8 left digits are always L; EAN-13 and UPC-E carry information in the parity
            var table = left && symbology != Symbology.Ean8 ? LG : L;
            costs[p] = new float[table.Length];
            PatternMatcher.Match(run, start, 4, 7, table, out var best, out _, costs[p]);
            if (best == float.MaxValue)
                return null;
            maxBest = Math.Max(maxBest, best);
        }

        return new EanStructure(symbology, costs, maxBest, g, g + elements - 1);
    }

    // each guard element should be about one module; blur makes bars fat and spaces thin, so be generous
    static bool Guard(ElementRun run, int start, int length, float m)
    {
        for (var i = start; i < start + length; i++)
        {
            var r = run.Widths[i] / m;
            if (r < 0.3f || r > 2.2f)
                return false;
        }
        var sum = run.Sum(start, length) / m;
        return sum > length * 0.6f && sum < length * 1.5f;
    }

    /// <summary>Read the digits out of per-position costs (one line's, or several lines' summed).</summary>
    /// <remarks>
    /// <para>
    /// The best pattern at every position is tried first. If that fails the check digit, positions where the
    /// runner-up came within <see cref="AmbiguityMargin"/> are tried with it instead. This is aimed at one
    /// weakness: 1/7 and 2/8 are identical in edge-to-similar-edge terms and differ only in bar widths, which
    /// blur distorts in the same direction on every scanline — so a code held square to the sensor can read
    /// a 7 as a 1 on all fifteen lines and no vote can fix it.
    /// </para>
    /// <para>
    /// Swapping digits until a check digit passes would manufacture misreads (any one position can be made
    /// to fit), so an alternative is accepted only when it is the <b>only</b> combination that passes, the
    /// swaps are limited to near ties, and at most <see cref="MaxAmbiguous"/> positions are in play.
    /// </para>
    /// </remarks>
    /// <param name="symbology">EAN-13, EAN-8 or UPC-E.</param>
    /// <param name="costs">Per position, the cost of every pattern.</param>
    /// <param name="formats">Decides between UPC-A and EAN-13 for a leading zero.</param>
    /// <param name="lines">How many lines' costs were summed into <paramref name="costs"/>; scales the tie margin.</param>
    public static LinearRead? Resolve(Symbology symbology, float[][] costs, FormatSet formats, int lines = 1)
    {
        var choice = new int[costs.Length];
        Span<int> ambiguous = stackalloc int[MaxAmbiguous];
        Span<int> alternative = stackalloc int[MaxAmbiguous];
        var count = 0;
        var tooAmbiguous = false;

        for (var p = 0; p < costs.Length; p++)
        {
            int best = 0, second = -1;
            for (var k = 1; k < costs[p].Length; k++)
            {
                if (costs[p][k] < costs[p][best])
                {
                    second = best;
                    best = k;
                }
                else if (second < 0 || costs[p][k] < costs[p][second])
                {
                    second = k;
                }
            }
            choice[p] = best;
            if (second >= 0 && costs[p][second] - costs[p][best] < AmbiguityMargin * lines)
            {
                if (count == MaxAmbiguous)
                    tooAmbiguous = true;
                else
                {
                    ambiguous[count] = p;
                    alternative[count] = second;
                    count++;
                }
            }
        }

        if (ResolveChoice(symbology, choice, formats) is { } read)
            return read;
        if (count == 0 || tooAmbiguous)
            return null;

        LinearRead? found = null;
        for (var mask = 1; mask < 1 << count; mask++)
        {
            var trial = (int[])choice.Clone();
            for (var b = 0; b < count; b++)
            {
                if ((mask & (1 << b)) != 0)
                    trial[ambiguous[b]] = alternative[b];
            }
            if (ResolveChoice(symbology, trial, formats) is not { } candidate)
                continue;
            if (found != null && found.Text != candidate.Text)
                return null; // two different codes fit — believe neither
            found = candidate;
        }
        return found;
    }

    const float AmbiguityMargin = 0.6f;
    const int MaxAmbiguous = 4;

    static LinearRead? ResolveChoice(Symbology symbology, int[] choice, FormatSet formats)
    {
        var digits = new int[choice.Length];
        var parity = 0;
        for (var p = 0; p < choice.Length; p++)
        {
            digits[p] = choice[p] % 10;
            if (choice[p] >= 10)
                parity |= 1 << (choice.Length - 1 - p) >> (symbology == Symbology.Ean13 ? 6 : 0);
        }

        switch (symbology)
        {
            case Symbology.Ean13:
            {
                // only the left six digits carry parity; the shift above already dropped the right six
                var first = Array.IndexOf(FirstDigitParity, parity);
                if (first < 0)
                    return null;

                var all = new int[13];
                all[0] = first;
                Array.Copy(digits, 0, all, 1, 12);
                if (!CheckDigitOk(all))
                    return null;

                var text = string.Concat(all);
                if (first == 0 && formats.Has(Symbology.UpcA))
                    return new LinearRead(text[1..], Symbology.UpcA, true);
                return formats.Has(Symbology.Ean13) ? new LinearRead(text, Symbology.Ean13, true) : null;
            }
            case Symbology.Ean8:
                return CheckDigitOk(digits) ? new LinearRead(string.Concat(digits), Symbology.Ean8, true) : null;
            default:
            {
                for (var ns = 0; ns < 2; ns++)
                {
                    var check = Array.IndexOf(UpcEParity[ns], parity);
                    if (check < 0)
                        continue;

                    var upcA = ExpandUpcE(ns, digits, check);
                    if (CheckDigitOk(upcA))
                        return new LinearRead($"{ns}{string.Concat(digits)}{check}", Symbology.UpcE, true);
                }
                return null;
            }
        }
    }

    static int[] ExpandUpcE(int ns, int[] d, int check)
    {
        var s = d[5] switch
        {
            0 or 1 or 2 => $"{ns}{d[0]}{d[1]}{d[5]}0000{d[2]}{d[3]}{d[4]}{check}",
            3 => $"{ns}{d[0]}{d[1]}{d[2]}00000{d[3]}{d[4]}{check}",
            4 => $"{ns}{d[0]}{d[1]}{d[2]}{d[3]}00000{d[4]}{check}",
            _ => $"{ns}{d[0]}{d[1]}{d[2]}{d[3]}{d[4]}0000{d[5]}{check}"
        };
        var digits = new int[s.Length];
        for (var i = 0; i < s.Length; i++)
            digits[i] = s[i] - '0';
        return digits;
    }

    /// <summary>GS1 mod-10: weights 3,1,3,1… from the digit left of the check digit.</summary>
    internal static bool CheckDigitOk(ReadOnlySpan<int> digits)
    {
        var sum = 0;
        for (var i = digits.Length - 2; i >= 0; i--)
            sum += digits[i] * ((digits.Length - 2 - i) % 2 == 0 ? 3 : 1);
        return (10 - sum % 10) % 10 == digits[^1];
    }
}

/// <summary>One line's worth of evidence for an EAN-family code: the cost of every digit at every position.</summary>
sealed record EanStructure(Symbology Symbology, float[][] Costs, float MaxBestCost, int Start, int End)
{
    /// <summary>Read from the far end of the line — never summed with lines read the other way.</summary>
    public bool Reversed { get; init; }
}

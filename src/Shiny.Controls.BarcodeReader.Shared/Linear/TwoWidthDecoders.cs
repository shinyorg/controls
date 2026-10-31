using System.Text;

namespace Shiny.Controls.BarcodeReader.Linear;

/// <summary>
/// The two-width symbologies — Code 39 and Interleaved 2 of 5. Every element is narrow or wide, so a
/// character is read by splitting its elements at the widest gap between narrow and wide. Neither carries a
/// mandatory check digit, which is why the engine asks for more scanlines to agree on these before it
/// believes them.
/// </summary>
static class TwoWidthDecoders
{
    const string Code39Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%*";

    // 9 elements, most significant bit first, bit set = wide
    static readonly int[] Code39Encodings =
    [
        0x034, 0x121, 0x061, 0x160, 0x031, 0x130, 0x070, 0x025, 0x124, 0x064,
        0x109, 0x049, 0x148, 0x019, 0x118, 0x058, 0x00D, 0x10C, 0x04C, 0x01C,
        0x103, 0x043, 0x142, 0x013, 0x112, 0x052, 0x007, 0x106, 0x046, 0x016,
        0x181, 0x0C1, 0x1C0, 0x091, 0x190, 0x0D0, 0x085, 0x184, 0x0C4, 0x0A8,
        0x0A2, 0x08A, 0x02A, 0x094
    ];

    // 5 elements, first element in bit 4, bit set = wide
    static readonly int[] ItfDigits = [0x06, 0x11, 0x09, 0x18, 0x05, 0x14, 0x0C, 0x03, 0x12, 0x0A];

    static readonly int[] ItfLengths = [6, 8, 10, 12, 14];

    public static void DecodeCode39(ElementRun run, List<LinearRead> reads)
    {
        for (var i = 1; i + 9 < run.Count; i++)
        {
            if (!run.IsBar(i) || Code39Char(run, i, out var narrow) != '*')
                continue;
            if (run.Widths[i - 1] < 5f * narrow)
                continue;

            var sb = new StringBuilder();
            var j = i + 10; // 9 elements + the inter-character gap
            var ok = false;
            while (j + 9 < run.Count && sb.Length < 60)
            {
                // the gap before each character is a narrow space, give or take
                if (run.Widths[j - 1] > 3.5f * narrow)
                    break;

                var c = Code39Char(run, j, out var n);
                if (c == '\0')
                    break;
                if (c == '*')
                {
                    ok = sb.Length > 0 && (j + 9 >= run.Count || run.Widths[j + 9] >= 5f * n);
                    break;
                }
                sb.Append(c);
                narrow = n;
                j += 10;
            }

            if (ok)
            {
                reads.Add(new LinearRead(sb.ToString(), Symbology.Code39, false) { Start = i, End = j + 8 });
                i = j + 9;
            }
        }
    }

    static char Code39Char(ElementRun run, int start, out float narrow)
    {
        narrow = 0;
        var bits = Classify(run, start, 9, 3, out narrow);
        if (bits < 0)
            return '\0';
        var index = Array.IndexOf(Code39Encodings, bits);
        return index < 0 ? '\0' : Code39Alphabet[index];
    }

    public static void DecodeItf(ElementRun run, List<LinearRead> reads)
    {
        for (var i = 1; i + 10 < run.Count; i++)
        {
            if (!run.IsBar(i))
                continue;

            // start: four narrow elements
            var narrow = run.Sum(i, 4) / 4f;
            if (!AllNarrow(run, i, 4, narrow) || run.Widths[i - 1] < 6f * narrow)
                continue;

            var sb = new StringBuilder();
            var j = i + 4;
            var ok = false;
            while (j + 3 <= run.Count)
            {
                // end: wide bar, narrow space, narrow bar, then the quiet zone
                if (IsItfEnd(run, j, narrow))
                {
                    ok = true;
                    break;
                }
                if (j + 10 > run.Count)
                    break;

                var bars = ClassifyInterleaved(run, j, 0, out var nb);
                var spaces = ClassifyInterleaved(run, j, 1, out var ns);
                var a = bars < 0 ? -1 : Array.IndexOf(ItfDigits, bars);
                var b = spaces < 0 ? -1 : Array.IndexOf(ItfDigits, spaces);
                if (a < 0 || b < 0)
                    break;

                sb.Append((char)('0' + a)).Append((char)('0' + b));
                narrow = (nb + ns) / 2f;
                j += 10;
            }

            if (!ok || Array.IndexOf(ItfLengths, sb.Length) < 0)
                continue;

            var text = sb.ToString();
            var checksummed = false;
            if (text.Length == 14)
            {
                // ITF-14 is a GTIN, and a GTIN carries the GS1 check digit
                if (!GtinOk(text))
                    continue;
                checksummed = true;
            }

            reads.Add(new LinearRead(text, Symbology.Itf, checksummed) { Start = i, End = j + 2 });
            i = j + 3;
        }
    }

    static bool GtinOk(string text)
    {
        Span<int> digits = stackalloc int[text.Length];
        for (var k = 0; k < text.Length; k++)
            digits[k] = text[k] - '0';
        return EanDecoder.CheckDigitOk(digits);
    }

    static bool IsItfEnd(ElementRun run, int j, float narrow)
    {
        if (j + 3 > run.Count || !run.IsBar(j))
            return false;
        var w = run.Widths[j] / narrow;
        var s = run.Widths[j + 1] / narrow;
        var b = run.Widths[j + 2] / narrow;
        var quiet = j + 3 >= run.Count || run.Widths[j + 3] >= 6f * narrow;
        return w > 1.6f && s < 1.6f && b < 1.6f && quiet;
    }

    static bool AllNarrow(ElementRun run, int start, int length, float narrow)
    {
        for (var i = start; i < start + length; i++)
        {
            var r = run.Widths[i] / narrow;
            if (r < 0.4f || r > 1.6f)
                return false;
        }
        return true;
    }

    // five elements of one colour out of a ten-element interleaved pair, 2 of them wide
    static int ClassifyInterleaved(ElementRun run, int start, int offset, out float narrow)
    {
        Span<float> w = stackalloc float[5];
        for (var k = 0; k < 5; k++)
            w[k] = run.Widths[start + offset + k * 2];
        return Split(w, 2, out narrow);
    }

    static int Classify(ElementRun run, int start, int length, int wide, out float narrow)
    {
        narrow = 0;
        if (start + length > run.Count)
            return -1;
        return Split(run.Widths.AsSpan(start, length), wide, out narrow);
    }

    // the `wide` widest elements are wide; they must stand clearly apart from the rest
    static int Split(ReadOnlySpan<float> widths, int wide, out float narrow)
    {
        narrow = 0;
        Span<float> sorted = stackalloc float[widths.Length];
        widths.CopyTo(sorted);
        sorted.Sort();

        var maxNarrow = sorted[widths.Length - wide - 1];
        var minWide = sorted[widths.Length - wide];
        if (minWide < 1.5f * maxNarrow)
            return -1;

        var threshold = (maxNarrow + minWide) / 2f;
        var bits = 0;
        var sum = 0f;
        for (var i = 0; i < widths.Length; i++)
        {
            bits <<= 1;
            if (widths[i] > threshold)
                bits |= 1;
            else
                sum += widths[i];
        }
        narrow = sum / (widths.Length - wide);
        return bits;
    }
}

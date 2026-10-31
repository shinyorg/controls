using System.Text;

namespace Shiny.Controls.BarcodeReader.Linear;

/// <summary>Code 128 (sets A, B and C, GS1-128).</summary>
static class Code128Decoder
{
    // values 0-105; 106 is the stop without its final 2-module bar
    internal static readonly int[][] Patterns = PatternMatcher.Parse(
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "233111");

    static readonly int[][] StartPatterns = Patterns[103..106];

    const int StartA = 103, StartB = 104, StartC = 105, Stop = 106;
    const int CodeC = 99, CodeB = 100, CodeA = 101, Fnc1 = 102, Shift = 98;
    const float MaxSymbolCost = 2.2f;

    public static void Decode(ElementRun run, List<LinearRead> reads)
    {
        for (var i = 1; i + 6 < run.Count; i++)
        {
            if (!run.IsBar(i))
                continue;

            var start = PatternMatcher.Match(run, i, 6, 11, StartPatterns, out var cost, out _);
            if (start < 0 || cost > MaxSymbolCost)
                continue;

            var m = run.Sum(i, 6) / 11f;
            if (run.Widths[i - 1] < 4f * m)
                continue;

            if (TryRead(run, i, StartA + start) is { } read)
            {
                reads.Add(read);
                i = read.End; // the next code (if any) starts past this one
            }
        }
    }

    static LinearRead? TryRead(ElementRun run, int i, int startValue)
    {
        var values = new List<int>(32) { startValue };
        var j = i + 6;
        while (j + 6 < run.Count && values.Count < 90)
        {
            var v = PatternMatcher.Match(run, j, 6, 11, Patterns, out var cost, out _);
            if (v < 0 || cost > MaxSymbolCost)
                return null;

            if (v == Stop)
            {
                // the stop is 7 elements: a final 2-module bar, then the quiet zone
                var m = run.Sum(j, 6) / 11f;
                var bar = run.Widths[j + 6] / m;
                if (bar < 1f || bar > 3.2f)
                    return null;
                if (j + 7 < run.Count && run.Widths[j + 7] < 4f * m)
                    return null;
                if (values.Count < 3) // start + check at the least
                    return null;

                var text = Checksum(values) ? Text(values) : null;
                return text == null ? null : new LinearRead(text, Symbology.Code128, true) { Start = i, End = j + 6 };
            }

            if (v >= StartA)
                return null;

            values.Add(v);
            j += 6;
        }
        return null;
    }

    static bool Checksum(List<int> values)
    {
        var sum = values[0];
        for (var k = 1; k < values.Count - 1; k++)
            sum += values[k] * k;
        return sum % 103 == values[^1];
    }

    static string? Text(List<int> values)
    {
        var sb = new StringBuilder();
        var set = values[0] switch { StartA => 'A', StartB => 'B', _ => 'C' };
        var shifted = false;

        // the data is everything between the start and the check symbol
        for (var k = 1; k < values.Count - 1; k++)
        {
            var v = values[k];
            var current = set;
            if (shifted)
            {
                current = set == 'A' ? 'B' : 'A';
                shifted = false;
            }

            if (v == Fnc1)
            {
                // a leading FNC1 only marks the data as GS1; later ones are the GS1 field separator
                if (k > 1)
                    sb.Append('\u001d');
                continue;
            }

            if (current == 'C')
            {
                if (v < 100)
                    sb.Append(v.ToString("00"));
                else if (v == CodeB)
                    set = 'B';
                else if (v == CodeA)
                    set = 'A';
                continue;
            }

            if (v < 96)
            {
                if (current == 'A')
                    sb.Append(v < 64 ? (char)(v + 32) : (char)(v - 64));
                else
                    sb.Append((char)(v + 32));
                continue;
            }

            switch (v)
            {
                case Shift:
                    shifted = true;
                    break;
                case CodeC:
                    set = 'C';
                    break;
                case CodeB when current == 'A':
                    set = 'B';
                    break;
                case CodeA when current == 'B':
                    set = 'A';
                    break;
                // FNC2/FNC3/FNC4 carry no text
            }
        }
        return sb.Length == 0 ? null : sb.ToString();
    }
}

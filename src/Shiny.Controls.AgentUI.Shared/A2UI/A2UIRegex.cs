using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Shiny.Controls.AgentUI.A2UI;

/// <summary>
/// The A2UI regex dialect: a portable ECMAScript subset in Unicode mode, statically checked for
/// catastrophic backtracking before it ever runs, then translated to an equivalent .NET pattern.
/// </summary>
/// <remarks>
/// The dialect and the four safety rules are the spec's, so a pattern the agent writes is accepted
/// or rejected the same way on every renderer. Translation pins JavaScript semantics .NET would
/// otherwise differ on: <c>^</c>/<c>$</c> only at the ends (no "before a trailing newline"),
/// ASCII-only <c>\d \w \b</c>, ECMAScript's <c>\s</c>, and <c>.</c> matching an astral code point
/// whole. A match timeout backs the static check up.
/// </remarks>
static class A2UIRegex
{
    public const int MaxPatternCodePoints = 256;
    public const int MaxValueCodePoints = 4096;

    static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);
    static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);

    /// <summary>The <c>regex</c> function: valid when the pattern matches anywhere in the value.</summary>
    public static A2UIValidationResult Test(string value, string pattern)
    {
        var regex = Compile(pattern);
        if (CodePoints(value) > MaxValueCodePoints)
            return new A2UIValidationResult(false);

        try
        {
            return new A2UIValidationResult(regex.IsMatch(value));
        }
        catch (RegexMatchTimeoutException)
        {
            throw new A2UIExpressionException($"Pattern '{pattern}' took too long to evaluate.");
        }
    }

    /// <summary>Validates, safety-checks and translates a pattern.</summary>
    /// <exception cref="A2UIExpressionException">The pattern isn't in the dialect or fails a safety rule.</exception>
    public static Regex Compile(string pattern)
    {
        if (Cache.TryGetValue(pattern, out var cached))
            return cached;

        if (CodePoints(pattern) > MaxPatternCodePoints)
            throw new A2UIExpressionException($"Pattern is longer than {MaxPatternCodePoints} code points.");

        var ast = new Parser(pattern).Parse();
        SafetyCheck(ast, pattern);

        var sb = new StringBuilder();
        Emit(ast, sb);
        var regex = new Regex(sb.ToString(), RegexOptions.CultureInvariant, Timeout);

        if (Cache.Count > 256)
            Cache.Clear();
        Cache[pattern] = regex;
        return regex;
    }

    static int CodePoints(string s)
    {
        var n = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                i++;
            n++;
        }
        return n;
    }

    // ── AST ──────────────────────────────────────────────────────────────────────

    abstract record Node;
    sealed record Alt(List<Node> Branches) : Node;
    sealed record Seq(List<Node> Items) : Node;
    sealed record Quant(Node Child, int Min, int? Max, bool Lazy) : Node;
    sealed record Group(Node Child) : Node;
    sealed record Look(Node Child, bool Negative) : Node;
    sealed record Assert(char Kind) : Node;        // '^', '$', 'b', 'B'
    sealed record Atom(CharSet Set) : Node;

    // ── safety check ─────────────────────────────────────────────────────────────

    static void SafetyCheck(Node node, string pattern)
    {
        switch (node)
        {
            case Alt a:
                a.Branches.ForEach(b => SafetyCheck(b, pattern));
                break;

            case Seq s:
                CheckAdjacentUnbounded(s, pattern);
                s.Items.ForEach(i => SafetyCheck(i, pattern));
                break;

            case Quant q:
                // rule 2: a repeat (upper bound > 1) around something that is itself unbounded
                if ((q.Max is null || q.Max > 1) && ContainsUnbounded(q.Child))
                    throw Unsafe(pattern, "a repeated group contains an unbounded quantifier");

                // rule 3: an unbounded repeat of an alternation whose branches can start alike
                if (q.Max is null && Unwrap(q.Child) is Alt alt)
                {
                    var firsts = alt.Branches.Select(First).ToList();
                    for (var i = 0; i < firsts.Count; i++)
                        for (var j = i + 1; j < firsts.Count; j++)
                            if (firsts[i].Overlaps(firsts[j]))
                                throw Unsafe(pattern, "an unbounded repeat of alternatives that can start with the same character");
                }
                SafetyCheck(q.Child, pattern);
                break;

            case Group g:
                SafetyCheck(g.Child, pattern);
                break;

            case Look l:
                SafetyCheck(l.Child, pattern);
                break;
        }
    }

    // rule 4: two unbounded-quantified atoms with overlapping sets, separated only by zero-width or nullable items
    static void CheckAdjacentUnbounded(Seq seq, string pattern)
    {
        for (var i = 0; i < seq.Items.Count; i++)
        {
            if (UnboundedAtom(seq.Items[i]) is not { } left)
                continue;

            for (var j = i + 1; j < seq.Items.Count; j++)
            {
                var item = seq.Items[j];
                if (UnboundedAtom(item) is { } right && left.Overlaps(right))
                    throw Unsafe(pattern, "two adjacent unbounded repeats can match the same characters");
                if (!Nullable(item))
                    break;
            }
        }
    }

    // A quantified atom (or a group holding only one) with no upper bound: its character set.
    static CharSet? UnboundedAtom(Node node) =>
        Unwrap(node) is Quant { Max: null } q && Unwrap(q.Child) is Atom a ? a.Set : null;

    static Node Unwrap(Node node)
    {
        while (node is Group g)
            node = g.Child;
        while (node is Seq { Items.Count: 1 } s)
            node = Unwrap(s.Items[0]);
        return node;
    }

    static bool ContainsUnbounded(Node node) => node switch
    {
        Quant q => q.Max is null || ContainsUnbounded(q.Child),
        Alt a => a.Branches.Any(ContainsUnbounded),
        Seq s => s.Items.Any(ContainsUnbounded),
        Group g => ContainsUnbounded(g.Child),
        Look l => ContainsUnbounded(l.Child),
        _ => false
    };

    static bool Nullable(Node node) => node switch
    {
        Alt a => a.Branches.Any(Nullable),
        Seq s => s.Items.All(Nullable),
        Quant q => q.Min == 0 || Nullable(q.Child),
        Group g => Nullable(g.Child),
        Look or Assert => true,
        _ => false
    };

    static CharSet First(Node node)
    {
        switch (node)
        {
            case Atom a:
                return a.Set;
            case Alt a:
                return a.Branches.Aggregate(CharSet.Empty, (acc, b) => acc.Union(First(b)));
            case Seq s:
                var set = CharSet.Empty;
                foreach (var item in s.Items)
                {
                    set = set.Union(First(item));
                    if (!Nullable(item))
                        break;
                }
                return set;
            case Quant q:
                return First(q.Child);
            case Group g:
                return First(g.Child);
            default:
                return CharSet.Empty;
        }
    }

    static A2UIExpressionException Unsafe(string pattern, string why) =>
        new($"Pattern '{pattern}' was rejected by the safety check: {why}.");

    // ── translation ──────────────────────────────────────────────────────────────

    const string WordClass = "[A-Za-z0-9_]";

    static void Emit(Node node, StringBuilder sb)
    {
        switch (node)
        {
            case Alt a:
                sb.Append("(?:");
                for (var i = 0; i < a.Branches.Count; i++)
                {
                    if (i > 0) sb.Append('|');
                    Emit(a.Branches[i], sb);
                }
                sb.Append(')');
                break;

            case Seq s:
                s.Items.ForEach(i => Emit(i, sb));
                break;

            case Quant q:
                sb.Append("(?:");
                Emit(q.Child, sb);
                sb.Append(')');
                sb.Append(q switch
                {
                    { Min: 0, Max: null } => "*",
                    { Min: 1, Max: null } => "+",
                    { Min: 0, Max: 1 } => "?",
                    { Max: null } => "{" + q.Min.ToString(CultureInfo.InvariantCulture) + ",}",
                    _ when q.Min == q.Max => "{" + q.Min.ToString(CultureInfo.InvariantCulture) + "}",
                    _ => "{" + q.Min.ToString(CultureInfo.InvariantCulture) + "," + q.Max!.Value.ToString(CultureInfo.InvariantCulture) + "}"
                });
                if (q.Lazy) sb.Append('?');
                break;

            case Group g:
                sb.Append("(?:");
                Emit(g.Child, sb);
                sb.Append(')');
                break;

            case Look l:
                sb.Append(l.Negative ? "(?!" : "(?=");
                Emit(l.Child, sb);
                sb.Append(')');
                break;

            case Assert { Kind: '^' }:
                sb.Append(@"\A");
                break;
            case Assert { Kind: '$' }:
                sb.Append(@"\z");
                break;
            case Assert { Kind: 'b' }:
                sb.Append($"(?:(?<={WordClass})(?!{WordClass})|(?<!{WordClass})(?={WordClass}))");
                break;
            case Assert { Kind: 'B' }:
                sb.Append($"(?:(?<={WordClass})(?={WordClass})|(?<!{WordClass})(?!{WordClass}))");
                break;

            case Atom a:
                a.Set.Emit(sb);
                break;
        }
    }

    // ── parser ───────────────────────────────────────────────────────────────────

    sealed class Parser(string pattern)
    {
        int pos;

        public Node Parse()
        {
            var node = ParseAlternation();
            if (pos < pattern.Length)
                throw Error(pattern[pos] == ')' ? "unmatched ')'" : $"unexpected '{pattern[pos]}'");
            return node;
        }

        Node ParseAlternation()
        {
            var branches = new List<Node> { ParseSequence() };
            while (Peek('|'))
            {
                pos++;
                branches.Add(ParseSequence());
            }
            return branches.Count == 1 ? branches[0] : new Alt(branches);
        }

        Node ParseSequence()
        {
            var items = new List<Node>();
            while (pos < pattern.Length && pattern[pos] is not ('|' or ')'))
                items.Add(ParseTerm());
            return items.Count == 1 ? items[0] : new Seq(items);
        }

        Node ParseTerm()
        {
            var atom = ParseAtom();
            if (pos >= pattern.Length)
                return atom;

            int min, max;
            bool unbounded = false;
            switch (pattern[pos])
            {
                case '*': pos++; min = 0; unbounded = true; max = 0; break;
                case '+': pos++; min = 1; unbounded = true; max = 0; break;
                case '?': pos++; min = 0; max = 1; break;
                case '{':
                    if (!TryBraces(out min, out var braceMax))
                        throw Error("a lone '{' is not allowed");
                    unbounded = braceMax is null;
                    max = braceMax ?? 0;
                    break;
                default:
                    return atom;
            }

            if (atom is Assert or Look)
                throw Error("an assertion can't be quantified");
            if (!unbounded && max < min)
                throw Error("quantifier range is out of order");

            var lazy = Peek('?');
            if (lazy) pos++;
            return new Quant(atom, min, unbounded ? null : max, lazy);
        }

        bool TryBraces(out int min, out int? max)
        {
            var m = Regex.Match(pattern[pos..], @"^\{(\d+)(,(\d*))?\}");
            min = 0;
            max = null;
            if (!m.Success)
                return false;

            min = Bounded(m.Groups[1].Value);
            max = !m.Groups[2].Success ? min : m.Groups[3].Value.Length == 0 ? null : Bounded(m.Groups[3].Value);
            pos += m.Length;
            return true;

            static int Bounded(string digits) => int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue;
        }

        Node ParseAtom()
        {
            var c = pattern[pos];
            switch (c)
            {
                case '^': pos++; return new Assert('^');
                case '$': pos++; return new Assert('$');
                case '.': pos++; return new Atom(CharSet.Dot);
                case '[': return ParseClass();
                case '\\': return ParseEscape();
                case '(': return ParseGroup();
                case '*' or '+' or '?': throw Error($"nothing to repeat before '{c}'");
                case '{': throw Error("a lone '{' is not allowed");
                case '}' or ']': throw Error($"a lone '{c}' is not allowed");
                default: return new Atom(CharSet.Single(NextCodePoint()));
            }
        }

        Node ParseGroup()
        {
            pos++; // (
            Node node;
            if (Peek('?'))
            {
                var rest = pattern[pos..];
                if (rest.StartsWith("?:", StringComparison.Ordinal))
                {
                    pos += 2;
                    node = new Group(ParseAlternation());
                }
                else if (rest.StartsWith("?=", StringComparison.Ordinal) || rest.StartsWith("?!", StringComparison.Ordinal))
                {
                    var negative = rest[1] == '!';
                    pos += 2;
                    node = new Look(ParseAlternation(), negative);
                }
                else if (rest.StartsWith("?<=", StringComparison.Ordinal) || rest.StartsWith("?<!", StringComparison.Ordinal))
                    throw Error("lookbehind is not allowed");
                else if (rest.StartsWith("?<", StringComparison.Ordinal))
                    throw Error("named groups are not allowed");
                else
                    throw Error("inline flags are not allowed");
            }
            else
            {
                node = new Group(ParseAlternation());
            }

            if (!Peek(')'))
                throw Error("missing ')'");
            pos++;
            return node;
        }

        Node ParseEscape()
        {
            pos++; // backslash
            if (pos >= pattern.Length)
                throw Error("a pattern can't end with '\\'");

            var c = pattern[pos];
            switch (c)
            {
                case 'b': pos++; return new Assert('b');
                case 'B': pos++; return new Assert('B');
                case 'd' or 'D' or 'w' or 'W' or 's' or 'S':
                    pos++;
                    return new Atom(ClassEscape(c));
                default:
                    return new Atom(CharSet.Single(CharacterEscape(inClass: false)));
            }
        }

        // after the backslash; returns one code point
        int CharacterEscape(bool inClass)
        {
            var c = pattern[pos];
            switch (c)
            {
                case 't': pos++; return '\t';
                case 'n': pos++; return '\n';
                case 'v': pos++; return '\v';
                case 'f': pos++; return '\f';
                case 'r': pos++; return '\r';
                case 'b' when inClass: pos++; return '\b';
                case '-' when inClass: pos++; return '-';
                case '0':
                    pos++;
                    if (pos < pattern.Length && char.IsAsciiDigit(pattern[pos]))
                        throw Error("legacy octal escapes are not allowed");
                    return 0;
                case >= '1' and <= '9':
                    throw Error("backreferences are not allowed");
                case 'k':
                    throw Error("named backreferences are not allowed");
                case 'p' or 'P':
                    throw Error("Unicode property escapes are not allowed");
                case 'c':
                    throw Error("control escapes (\\c) are not allowed");
                case 'x':
                    pos++;
                    return Hex(2);
                case 'u':
                    pos++;
                    if (Peek('{'))
                    {
                        var end = pattern.IndexOf('}', pos);
                        if (end < 0 || end == pos + 1)
                            throw Error("malformed \\u{...} escape");
                        var hex = pattern[(pos + 1)..end];
                        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp) || cp > 0x10FFFF)
                            throw Error("malformed \\u{...} escape");
                        pos = end + 1;
                        return cp;
                    }
                    var unit = Hex(4);
                    // Unicode mode joins an escaped surrogate pair into one code point
                    if (char.IsHighSurrogate((char)unit) && pattern.AsSpan(pos).StartsWith(@"\u") && pos + 6 <= pattern.Length
                        && int.TryParse(pattern.AsSpan(pos + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var low) && char.IsLowSurrogate((char)low))
                    {
                        pos += 6;
                        return char.ConvertToUtf32((char)unit, (char)low);
                    }
                    return unit;
                default:
                    if ("^$\\.*+?()[]{}|/".Contains(c))
                    {
                        pos++;
                        return c;
                    }
                    throw Error($"'\\{c}' is not a valid escape");
            }
        }

        int Hex(int digits)
        {
            if (pos + digits > pattern.Length
                || !int.TryParse(pattern.AsSpan(pos, digits), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                throw Error("malformed hex escape");
            pos += digits;
            return value;
        }

        Node ParseClass()
        {
            pos++; // [
            var negate = Peek('^');
            if (negate) pos++;

            var set = CharSet.Empty;
            while (true)
            {
                if (pos >= pattern.Length)
                    throw Error("missing ']'");
                if (pattern[pos] == ']')
                {
                    pos++;
                    break;
                }

                var (lo, loSet) = ClassAtom();
                if (Peek('-') && pos + 1 < pattern.Length && pattern[pos + 1] != ']')
                {
                    pos++; // -
                    var (hi, hiSet) = ClassAtom();
                    if (loSet is not null || hiSet is not null)
                        throw Error("a class escape can't be a range endpoint");
                    if (hi < lo)
                        throw Error("character class range is out of order");
                    set = set.Union(CharSet.Range(lo, hi));
                }
                else
                {
                    set = set.Union(loSet ?? CharSet.Single(lo));
                }
            }
            return new Atom(negate ? set.Complement() : set);
        }

        (int CodePoint, CharSet? Set) ClassAtom()
        {
            if (pattern[pos] != '\\')
                return (NextCodePoint(), null);

            pos++;
            if (pos >= pattern.Length)
                throw Error("a pattern can't end with '\\'");

            var c = pattern[pos];
            if (c is 'd' or 'D' or 'w' or 'W' or 's' or 'S')
            {
                pos++;
                return (-1, ClassEscape(c));
            }
            if (c == 'B')
                throw Error("'\\B' is not allowed in a class");
            return (CharacterEscape(inClass: true), null);
        }

        static CharSet ClassEscape(char c) => c switch
        {
            'd' => CharSet.Digit,
            'D' => CharSet.Digit.Complement(),
            'w' => CharSet.Word,
            'W' => CharSet.Word.Complement(),
            's' => CharSet.Space,
            _ => CharSet.Space.Complement()
        };

        int NextCodePoint()
        {
            var c = pattern[pos++];
            if (char.IsHighSurrogate(c) && pos < pattern.Length && char.IsLowSurrogate(pattern[pos]))
                return char.ConvertToUtf32(c, pattern[pos++]);
            return c;
        }

        bool Peek(char c) => pos < pattern.Length && pattern[pos] == c;

        A2UIExpressionException Error(string why) => new($"Pattern '{pattern}' is not valid A2UI regex: {why}.");
    }

    // ── code point sets ──────────────────────────────────────────────────────────

    sealed class CharSet
    {
        const int MaxCodePoint = 0x10FFFF;
        readonly List<(int Lo, int Hi)> ranges;

        CharSet(List<(int, int)> ranges) => this.ranges = ranges;

        public static readonly CharSet Empty = new([]);
        public static readonly CharSet Digit = Range('0', '9');
        public static readonly CharSet Word = Range('A', 'Z').Union(Range('a', 'z')).Union(Digit).Union(Single('_'));

        // ECMAScript WhiteSpace + LineTerminator
        public static readonly CharSet Space = new[] { '\t', '\n', '\v', '\f', '\r', ' ', '\u00A0', '\u1680', '\u2028', '\u2029', '\u202F', '\u205F', '\u3000', '\uFEFF' }
            .Aggregate(Range(0x2000, 0x200A), (s, c) => s.Union(Single(c)));

        public static readonly CharSet Dot = Single('\n').Union(Single('\r')).Union(Single('\u2028')).Union(Single('\u2029')).Complement();

        public static CharSet Single(int cp) => new([(cp, cp)]);

        public static CharSet Range(int lo, int hi) => new([(lo, hi)]);

        public CharSet Union(CharSet other)
        {
            var all = ranges.Concat(other.ranges).OrderBy(r => r.Lo).ToList();
            var merged = new List<(int, int)>();
            foreach (var (lo, hi) in all)
            {
                if (merged.Count > 0 && lo <= merged[^1].Item2 + 1)
                    merged[^1] = (merged[^1].Item1, Math.Max(merged[^1].Item2, hi));
                else
                    merged.Add((lo, hi));
            }
            return new CharSet(merged);
        }

        public CharSet Complement()
        {
            var result = new List<(int, int)>();
            var next = 0;
            foreach (var (lo, hi) in ranges)
            {
                if (lo > next)
                    result.Add((next, lo - 1));
                next = hi + 1;
            }
            if (next <= MaxCodePoint)
                result.Add((next, MaxCodePoint));
            return new CharSet(result);
        }

        public bool Overlaps(CharSet other) =>
            ranges.Any(a => other.ranges.Any(b => a.Lo <= b.Hi && b.Lo <= a.Hi));

        // .NET regex works on UTF-16 units: BMP ranges go in one class, astral ranges become surrogate pairs.
        public void Emit(StringBuilder sb)
        {
            var bmp = ranges.Where(r => r.Lo <= 0xFFFF).Select(r => (r.Lo, Hi: Math.Min(r.Hi, 0xFFFF))).ToList();
            var astral = ranges.Where(r => r.Hi > 0xFFFF).Select(r => (Lo: Math.Max(r.Lo, 0x10000), r.Hi)).ToList();

            var alternatives = new List<string>();
            foreach (var (lo, hi) in astral)
                alternatives.AddRange(SurrogateRanges(lo, hi));
            if (bmp.Count > 0)
                alternatives.Add(bmp.Count == 1 && bmp[0].Lo == bmp[0].Hi ? U(bmp[0].Lo) : "[" + string.Concat(bmp.Select(r => r.Lo == r.Hi ? U(r.Lo) : U(r.Lo) + "-" + U(r.Hi))) + "]");

            if (alternatives.Count == 0)
                sb.Append("(?!)");
            else if (alternatives.Count == 1)
                sb.Append(alternatives[0]);
            else
                sb.Append("(?:").AppendJoin('|', alternatives).Append(')');
        }

        static IEnumerable<string> SurrogateRanges(int lo, int hi)
        {
            var (loH, loL) = Split(lo);
            var (hiH, hiL) = Split(hi);
            if (loH == hiH)
            {
                yield return U(loH) + "[" + U(loL) + "-" + U(hiL) + "]";
                yield break;
            }
            yield return U(loH) + "[" + U(loL) + "-" + U(0xDFFF) + "]";
            if (hiH - loH > 1)
                yield return "[" + U(loH + 1) + "-" + U(hiH - 1) + "][" + U(0xDC00) + "-" + U(0xDFFF) + "]";
            yield return U(hiH) + "[" + U(0xDC00) + "-" + U(hiL) + "]";

            static (int, int) Split(int cp) => (((cp - 0x10000) >> 10) + 0xD800, ((cp - 0x10000) & 0x3FF) + 0xDC00);
        }

        static string U(int unit) => "\\u" + unit.ToString("X4", CultureInfo.InvariantCulture);
    }
}

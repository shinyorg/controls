using System.Globalization;
using System.Text;

namespace Shiny.Controls.Keyframe;

/// <summary>
/// The text form of an easing curve, shared by every host so <c>Easing="spring(0.4, 12)"</c> means
/// the same curve in MAUI XAML, a Blazor parameter, and a C# string.
/// </summary>
/// <remarks>
/// <para>Accepts the named curves in <see cref="Easings"/> and CSS-style functions —
/// <c>cubic-bezier(x1, y1, x2, y2)</c>, <c>steps(n[, jumpStart])</c>, <c>spring([damping[, frequency]])</c>
/// and <c>bouncy(bounce[, frequency])</c>. Function names match case-insensitively and with or
/// without the dash, so <c>CubicBezier(...)</c> and <c>Spring(0.4)</c> read as written in C#.</para>
/// <para><see cref="ToCss(string?)"/> goes the other way, for a browser: curves CSS knows natively are
/// passed through, and everything else — springs, bounces, elastics — is sampled into a CSS
/// <c>linear()</c> function, which is how a host that runs on the Web Animations API plays exactly
/// the curve the managed engine would.</para>
/// </remarks>
public static class EasingSyntax
{
    static readonly Dictionary<string, EasingFunction> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Linear"] = Easings.Linear,
        ["Hold"] = Easings.Hold,
        ["StepStart"] = Easings.StepStart,
        ["StepEnd"] = Easings.StepEnd,
        ["QuadIn"] = Easings.QuadIn,
        ["QuadOut"] = Easings.QuadOut,
        ["QuadInOut"] = Easings.QuadInOut,
        ["CubicIn"] = Easings.CubicIn,
        ["CubicOut"] = Easings.CubicOut,
        ["CubicInOut"] = Easings.CubicInOut,
        ["QuartIn"] = Easings.QuartIn,
        ["QuartOut"] = Easings.QuartOut,
        ["QuartInOut"] = Easings.QuartInOut,
        ["QuintIn"] = Easings.QuintIn,
        ["QuintOut"] = Easings.QuintOut,
        ["QuintInOut"] = Easings.QuintInOut,
        ["SinIn"] = Easings.SinIn,
        ["SinOut"] = Easings.SinOut,
        ["SinInOut"] = Easings.SinInOut,
        ["ExpoIn"] = Easings.ExpoIn,
        ["ExpoOut"] = Easings.ExpoOut,
        ["ExpoInOut"] = Easings.ExpoInOut,
        ["CircIn"] = Easings.CircIn,
        ["CircOut"] = Easings.CircOut,
        ["CircInOut"] = Easings.CircInOut,
        ["BackIn"] = Easings.BackIn,
        ["BackOut"] = Easings.BackOut,
        ["BackInOut"] = Easings.BackInOut,
        ["ElasticIn"] = Easings.ElasticIn,
        ["ElasticOut"] = Easings.ElasticOut,
        ["ElasticInOut"] = Easings.ElasticInOut,
        ["BounceIn"] = Easings.BounceIn,
        ["BounceOut"] = Easings.BounceOut,
        ["BounceInOut"] = Easings.BounceInOut,
        ["Ease"] = Easings.Ease,
        ["EaseIn"] = Easings.EaseIn,
        ["EaseOut"] = Easings.EaseOut,
        ["EaseInOut"] = Easings.EaseInOut,
        ["Emphasized"] = Easings.Emphasized,

        // CSS spellings, so a value copied out of a stylesheet resolves as-is.
        ["ease-in"] = Easings.EaseIn,
        ["ease-out"] = Easings.EaseOut,
        ["ease-in-out"] = Easings.EaseInOut,
        ["step-start"] = Easings.StepStart,
        ["step-end"] = Easings.StepEnd
    };

    // The CSS text for the curves a browser knows natively. Anything not in here is sampled.
    static readonly Dictionary<string, string> NativeCss = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Linear"] = "linear",
        ["Hold"] = "step-end",
        ["StepStart"] = "step-start",
        ["StepEnd"] = "step-end",
        ["step-start"] = "step-start",
        ["step-end"] = "step-end",
        ["Ease"] = "ease",
        ["EaseIn"] = "ease-in",
        ["EaseOut"] = "ease-out",
        ["EaseInOut"] = "ease-in-out",
        ["ease-in"] = "ease-in",
        ["ease-out"] = "ease-out",
        ["ease-in-out"] = "ease-in-out",
        ["Emphasized"] = "cubic-bezier(0.2, 0, 0, 1)"
    };

    /// <summary>Adds or replaces a named curve.</summary>
    public static void Register(string name, EasingFunction easing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(easing);
        Named[name] = easing;
        NativeCss.Remove(name);
    }

    /// <summary>Every registered curve name.</summary>
    public static IEnumerable<string> Names => Named.Keys;

    /// <summary>Resolves a curve, returning null when the text is not recognised.</summary>
    public static EasingFunction? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = text.Trim();

        if (Named.TryGetValue(text, out var named))
            return named;

        if (!TrySplitFunction(text, out var function, out var arguments))
            return null;

        return function switch
        {
            "cubicbezier" when arguments.Length == 4
                => Easings.CubicBezier(arguments[0], arguments[1], arguments[2], arguments[3]),

            "steps" when arguments.Length is 1 or 2
                => Easings.Steps((int)arguments[0], arguments.Length == 2 && arguments[1] != 0d),

            "spring" when arguments.Length is 0 or 1 or 2
                => Easings.Spring(
                    arguments.Length > 0 ? arguments[0] : 0.5d,
                    arguments.Length > 1 ? arguments[1] : 10d),

            "bouncy" when arguments.Length is 1 or 2
                => Easings.Bouncy(arguments[0], arguments.Length > 1 ? arguments[1] : 12d),

            _ => null
        };
    }

    /// <summary>Resolves a curve, throwing a message that lists the alternatives.</summary>
    public static EasingFunction Get(string? text)
        => Find(text) ?? throw new FormatException(
            $"'{text}' is not a recognised easing curve. Use one of: {string.Join(", ", Named.Keys.Order())}, " +
            "or a function such as cubic-bezier(0.25, 0.1, 0.25, 1), steps(4), spring(0.4, 12) or bouncy(0.3).");

    /// <summary>
    /// Converts curve text to a CSS <c>&lt;easing-function&gt;</c>. Null or empty text is
    /// <c>linear</c>. Throws <see cref="FormatException"/> for text that is not a curve.
    /// </summary>
    public static string ToCss(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "linear";

        text = text.Trim();

        if (NativeCss.TryGetValue(text, out var native))
            return native;

        if (TrySplitFunction(text, out var function, out var arguments))
        {
            if (function == "cubicbezier" && arguments.Length == 4)
                return FormattableString.Invariant($"cubic-bezier({arguments[0]}, {arguments[1]}, {arguments[2]}, {arguments[3]})");

            if (function == "steps" && arguments.Length is 1 or 2)
            {
                var jumpStart = arguments.Length == 2 && arguments[1] != 0d;
                return FormattableString.Invariant($"steps({(int)arguments[0]}, {(jumpStart ? "jump-start" : "jump-end")})");
            }
        }

        return ToCssLinear(Get(text));
    }

    /// <summary>
    /// Samples any curve into a CSS <c>linear()</c> function. Overshoot and undershoot survive,
    /// because <c>linear()</c> stops may lie outside 0..1.
    /// </summary>
    /// <param name="easing">The curve to sample.</param>
    /// <param name="samples">Number of segments. 48 is visually exact for springs and bounces at
    /// UI durations; raise it for very long, very oscillatory curves.</param>
    public static string ToCssLinear(EasingFunction easing, int samples = 48)
    {
        ArgumentNullException.ThrowIfNull(easing);
        ArgumentOutOfRangeException.ThrowIfLessThan(samples, 2);

        var builder = new StringBuilder("linear(");
        for (var i = 0; i <= samples; i++)
        {
            var t = (double)i / samples;
            if (i > 0)
                builder.Append(", ");

            builder.Append(Math.Round(easing(t), 4).ToString(CultureInfo.InvariantCulture));
            builder.Append(' ');
            builder.Append(Math.Round(t * 100d, 3).ToString(CultureInfo.InvariantCulture));
            builder.Append('%');
        }

        return builder.Append(')').ToString();
    }

    static bool TrySplitFunction(string text, out string function, out double[] arguments)
    {
        function = "";
        arguments = [];

        var open = text.IndexOf('(');
        if (open < 0 || !text.EndsWith(')'))
            return false;

        function = text[..open].Trim().Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();

        var parts = text[(open + 1)..^1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var values = new double[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            // CSS writes the steps() position as a keyword; accept it alongside 0/1.
            var part = parts[i];
            var parsed = part switch
            {
                "jump-start" or "start" => (Ok: true, Value: 1d),
                "jump-end" or "end" => (Ok: true, Value: 0d),
                _ => (Ok: double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number), Value: number)
            };

            if (!parsed.Ok)
                return false;

            values[i] = parsed.Value;
        }

        arguments = values;
        return true;
    }
}

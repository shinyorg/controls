using System.Globalization;

namespace Shiny.Blazor.Controls;

/// <summary>The unit of a <see cref="YogaValue"/>.</summary>
public enum YogaUnit
{
    Undefined,
    Point,
    Percent,
    Auto
}


/// <summary>
/// A Yoga length: points, a percentage of the container, <c>auto</c>, or nothing. <see cref="YogaLayout"/>
/// takes lengths as text (<c>Width="50%"</c>, <c>Width="120"</c>), because Razor treats any non-string
/// attribute value as C# and <c>50%</c> is not an expression. This is what that text is parsed into.
/// </summary>
public readonly record struct YogaValue(double Value, YogaUnit Unit)
{
    public static readonly YogaValue Undefined = new(double.NaN, YogaUnit.Undefined);
    public static readonly YogaValue Auto = new(double.NaN, YogaUnit.Auto);

    public static YogaValue Point(double value) => new(value, YogaUnit.Point);
    public static YogaValue Percent(double value) => new(value, YogaUnit.Percent);

    public static implicit operator YogaValue(double points) => Point(points);

    /// <summary>Reads <c>"120"</c>, <c>"120px"</c>, <c>"50%"</c>, <c>"auto"</c> and <c>"undefined"</c> (or empty).</summary>
    public static YogaValue Parse(string? text)
    {
        if (TryParse(text, out var value))
            return value;

        throw new FormatException($"'{text}' is not a Yoga value. Use a number, a percentage such as \"50%\", or \"auto\".");
    }

    public static bool TryParse(string? text, out YogaValue value)
    {
        value = Undefined;
        var s = text?.Trim();
        if (string.IsNullOrEmpty(s) || s.Equals("undefined", StringComparison.OrdinalIgnoreCase))
            return true;

        if (s.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            value = Auto;
            return true;
        }

        var unit = YogaUnit.Point;
        if (s.EndsWith('%'))
        {
            unit = YogaUnit.Percent;
            s = s[..^1];
        }
        else if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase) || s.EndsWith("pt", StringComparison.OrdinalIgnoreCase))
        {
            s = s[..^2];
        }

        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            return false;

        value = new YogaValue(number, unit);
        return true;
    }

    /// <summary>The CSS length, or null when undefined (the declaration is then left out).</summary>
    public string? ToCss() => this.Unit switch
    {
        YogaUnit.Point => this.Value == 0 ? "0" : this.Value.ToString("0.###", CultureInfo.InvariantCulture) + "px",
        YogaUnit.Percent => this.Value.ToString("0.###", CultureInfo.InvariantCulture) + "%",
        YogaUnit.Auto => "auto",
        _ => null
    };

    public override string ToString() => this.Unit switch
    {
        YogaUnit.Point => this.Value.ToString("0.###", CultureInfo.InvariantCulture),
        YogaUnit.Percent => this.Value.ToString("0.###", CultureInfo.InvariantCulture) + "%",
        YogaUnit.Auto => "auto",
        _ => "undefined"
    };
}

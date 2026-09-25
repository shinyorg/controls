using System.ComponentModel;
using System.Globalization;

namespace Shiny.Maui.Controls;

/// <summary>The unit of a <see cref="YogaValue"/>.</summary>
public enum YogaUnit
{
    Undefined,
    Point,
    Percent,
    Auto
}


/// <summary>
/// A Yoga length: a number of points, a percentage of the container, <c>auto</c>, or nothing at all.
/// In XAML it is written as text: <c>"120"</c>, <c>"50%"</c>, <c>"auto"</c>.
/// </summary>
[TypeConverter(typeof(YogaValueTypeConverter))]
public readonly record struct YogaValue(double Value, YogaUnit Unit)
{
    public static readonly YogaValue Undefined = new(double.NaN, YogaUnit.Undefined);
    public static readonly YogaValue Auto = new(double.NaN, YogaUnit.Auto);

    public static YogaValue Point(double value) => new(value, YogaUnit.Point);
    public static YogaValue Percent(double value) => new(value, YogaUnit.Percent);

    public static implicit operator YogaValue(double points) => Point(points);

    /// <summary>True for a point or percentage value.</summary>
    public bool IsDefined => this.Unit is YogaUnit.Point or YogaUnit.Percent;

    /// <summary>
    /// The value in points. A percentage is taken of <paramref name="reference"/>; it is NaN when the
    /// reference is unbounded, and so is anything that is not a point or percentage.
    /// </summary>
    public double Resolve(double reference) => this.Unit switch
    {
        YogaUnit.Point => this.Value,
        YogaUnit.Percent when double.IsFinite(reference) => this.Value * reference / 100d,
        _ => double.NaN
    };

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

    public override string ToString() => this.Unit switch
    {
        YogaUnit.Point => this.Value.ToString("0.###", CultureInfo.InvariantCulture),
        YogaUnit.Percent => this.Value.ToString("0.###", CultureInfo.InvariantCulture) + "%",
        YogaUnit.Auto => "auto",
        _ => "undefined"
    };
}


/// <summary>Lets XAML write <see cref="YogaValue"/> as <c>"50%"</c>, <c>"auto"</c> or a number.</summary>
public sealed class YogaValueTypeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || sourceType == typeof(double) || sourceType == typeof(int);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        => destinationType == typeof(string);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) => value switch
    {
        string s => YogaValue.Parse(s),
        double d => YogaValue.Point(d),
        int i => YogaValue.Point(i),
        _ => throw new NotSupportedException($"Cannot convert {value.GetType().Name} to a YogaValue.")
    };

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        => value is YogaValue v ? v.ToString() : base.ConvertTo(context, culture, value, destinationType);
}

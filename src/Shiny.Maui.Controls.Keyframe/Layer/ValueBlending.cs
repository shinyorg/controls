using System.Globalization;

namespace Shiny.Maui.Controls.Keyframe;

/// <summary>
/// Blends and parses values of any property type, so a transition or a spec-style
/// <see cref="KeyFrame"/> can animate an arbitrary <see cref="BindableProperty"/> — not only the
/// named ones in <see cref="AnimatableProperties"/>.
/// </summary>
/// <remarks>
/// Numbers, colours (in Oklab), <see cref="Thickness"/>, <see cref="CornerRadius"/>,
/// <see cref="Point"/>, <see cref="Size"/>, <see cref="Rect"/> and <see cref="SolidColorBrush"/> blend
/// smoothly. Anything else is discrete and switches halfway through a segment, the CSS rule — except
/// <see cref="bool"/>, where <c>true</c> holds for the whole segment either side, which is the CSS
/// <c>visibility</c> rule: an <c>IsVisible</c> fade stays visible while it fades. Register a blender for
/// your own types with <see cref="Register{T}"/>.
/// </remarks>
public static class ValueBlending
{
    static readonly Dictionary<Type, Func<object?, object?, double, object?>> Blenders = new()
    {
        [typeof(double)] = static (a, b, t) => Lerp(ToDouble(a), ToDouble(b), t),
        [typeof(float)] = static (a, b, t) => (float)Lerp(ToDouble(a), ToDouble(b), t),
        [typeof(int)] = static (a, b, t) => (int)Math.Round(Lerp(ToDouble(a), ToDouble(b), t)),
        [typeof(long)] = static (a, b, t) => (long)Math.Round(Lerp(ToDouble(a), ToDouble(b), t)),
        [typeof(Color)] = static (a, b, t) => BlendColor(a as Color, b as Color, t),
        [typeof(Thickness)] = static (a, b, t) =>
        {
            var x = a is Thickness ta ? ta : default;
            var y = b is Thickness tb ? tb : default;
            return new Thickness(Lerp(x.Left, y.Left, t), Lerp(x.Top, y.Top, t), Lerp(x.Right, y.Right, t), Lerp(x.Bottom, y.Bottom, t));
        },
        [typeof(CornerRadius)] = static (a, b, t) =>
        {
            var x = a is CornerRadius ca ? ca : default;
            var y = b is CornerRadius cb ? cb : default;
            return new CornerRadius(Lerp(x.TopLeft, y.TopLeft, t), Lerp(x.TopRight, y.TopRight, t), Lerp(x.BottomLeft, y.BottomLeft, t), Lerp(x.BottomRight, y.BottomRight, t));
        },
        [typeof(Point)] = static (a, b, t) =>
        {
            var x = a is Point pa ? pa : default;
            var y = b is Point pb ? pb : default;
            return new Point(Lerp(x.X, y.X, t), Lerp(x.Y, y.Y, t));
        },
        [typeof(Size)] = static (a, b, t) =>
        {
            var x = a is Size sa ? sa : default;
            var y = b is Size sb ? sb : default;
            return new Size(Lerp(x.Width, y.Width, t), Lerp(x.Height, y.Height, t));
        },
        [typeof(Rect)] = static (a, b, t) =>
        {
            var x = a is Rect ra ? ra : default;
            var y = b is Rect rb ? rb : default;
            return new Rect(Lerp(x.X, y.X, t), Lerp(x.Y, y.Y, t), Lerp(x.Width, y.Width, t), Lerp(x.Height, y.Height, t));
        },
        [typeof(Brush)] = BlendBrush,
        [typeof(SolidColorBrush)] = BlendBrush,
        [typeof(bool)] = static (a, b, t) => t <= 0d ? a : t >= 1d ? b : (a is true || b is true)
    };

    /// <summary>Adds or replaces the blender for a value type.</summary>
    public static void Register<T>(IInterpolator<T> interpolator)
    {
        ArgumentNullException.ThrowIfNull(interpolator);
        Blenders[typeof(T)] = (a, b, t) => interpolator.Interpolate(a is T x ? x : default!, b is T y ? y : default!, t);
    }

    /// <summary>Whether values of the type blend smoothly rather than switching.</summary>
    public static bool IsContinuous(Type type) => Blenders.ContainsKey(type) && type != typeof(bool);

    /// <summary>Blends two values of a property's type.</summary>
    public static object? Blend(Type type, object? from, object? to, double progress)
    {
        if (Blenders.TryGetValue(type, out var blender))
            return blender(from, to, progress);

        // A Color-typed value on a property declared as object, or a Brush subclass, still blends.
        if (from is not null && to is not null && from.GetType() == to.GetType() && Blenders.TryGetValue(from.GetType(), out var byValue))
            return byValue(from, to, progress);

        return progress < 0.5d ? from : to;
    }

    /// <summary>
    /// Converts a value the XAML parser left as text into the property's type. Values that are
    /// already the right type pass through.
    /// </summary>
    public static object? Parse(Type type, object? value)
    {
        if (value is null || type.IsInstanceOfType(value))
            return value;

        if (value is not string text)
        {
            if (value is IConvertible convertible && (type == typeof(double) || type == typeof(float) || type == typeof(int) || type == typeof(long)))
                return Convert.ChangeType(convertible, type, CultureInfo.InvariantCulture);

            if (value is Color fill && type.IsAssignableFrom(typeof(SolidColorBrush)))
                return new SolidColorBrush(fill);

            return value;
        }

        text = text.Trim();

        if (type == typeof(double))
            return double.Parse(text, CultureInfo.InvariantCulture);
        if (type == typeof(float))
            return float.Parse(text, CultureInfo.InvariantCulture);
        if (type == typeof(int))
            return int.Parse(text, CultureInfo.InvariantCulture);
        if (type == typeof(long))
            return long.Parse(text, CultureInfo.InvariantCulture);
        if (type == typeof(bool))
            return bool.Parse(text);
        if (type == typeof(string))
            return text;
        if (type == typeof(Color))
            return Color.Parse(text);
        if (type == typeof(Thickness))
            return new Microsoft.Maui.Converters.ThicknessTypeConverter().ConvertFromInvariantString(text);
        if (type == typeof(CornerRadius))
            return new Microsoft.Maui.Converters.CornerRadiusTypeConverter().ConvertFromInvariantString(text);
        if (type == typeof(Point))
            return new Microsoft.Maui.Graphics.Converters.PointTypeConverter().ConvertFromInvariantString(text);
        if (type == typeof(Size))
            return new Microsoft.Maui.Graphics.Converters.SizeTypeConverter().ConvertFromInvariantString(text);
        if (type == typeof(Rect))
            return new Microsoft.Maui.Graphics.Converters.RectTypeConverter().ConvertFromInvariantString(text);
        if (type.IsAssignableFrom(typeof(SolidColorBrush)))
            return new SolidColorBrush(Color.Parse(text));
        if (type.IsEnum)
            return Enum.Parse(type, text, ignoreCase: true);

        throw new FormatException($"Cannot read '{text}' as a {type.Name}. Give the value in that type, or register an AnimatableProperty for it.");
    }

    static double Lerp(double a, double b, double t) => a + (b - a) * t;

    static double ToDouble(object? value) => value switch
    {
        double d => d,
        null => 0d,
        IConvertible c => c.ToDouble(CultureInfo.InvariantCulture),
        _ => 0d
    };

    static Color BlendColor(Color? a, Color? b, double t)
    {
        // Blending to or from "no colour" fades through the other colour's transparent self, not
        // through black, which is what a null treated as default(Color) would do.
        var from = a ?? b?.WithAlpha(0f) ?? Colors.Transparent;
        var to = b ?? a?.WithAlpha(0f) ?? Colors.Transparent;
        return Shiny.Controls.Keyframe.Graphics.ColorInterpolator.Oklab.Interpolate(from, to, t);
    }

    static object? BlendBrush(object? a, object? b, double t)
    {
        var from = (a as SolidColorBrush)?.Color;
        var to = (b as SolidColorBrush)?.Color;

        // Only solid brushes have a meaningful in-between; a gradient or image switches halfway.
        if ((a is not null && from is null) || (b is not null && to is null))
            return t < 0.5d ? a : b;

        return new SolidColorBrush(BlendColor(from, to, t));
    }
}

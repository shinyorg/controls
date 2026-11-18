using System.ComponentModel;
using System.Globalization;

namespace Shiny.Maui.Controls.Keyframe;

/// <summary>
/// One instant of a <see cref="Keyframes"/> animation, written as ordinary MAUI <see cref="Setter"/>s —
/// the storyboard shape. Any bindable property can be set, with its value written exactly as it would
/// be in a <c>Style</c>.
/// </summary>
/// <example>
/// <code language="xml">
/// &lt;kf:Keyframes Duration="0:0:0.4"&gt;
///   &lt;kf:KeyFrame Cue="0%"&gt;
///     &lt;Setter Property="VisualElement.Opacity" Value="0" /&gt;
///     &lt;Setter Property="VisualElement.Scale" Value="0.9" /&gt;
///   &lt;/kf:KeyFrame&gt;
///   &lt;kf:KeyFrame Cue="60%" Easing="CubicOut"&gt;
///     &lt;Setter Property="VisualElement.Opacity" Value="1" /&gt;
///     &lt;Setter Property="VisualElement.Scale" Value="1.05" /&gt;
///   &lt;/kf:KeyFrame&gt;
///   &lt;kf:KeyFrame Cue="100%" Easing="spring(0.4)"&gt;
///     &lt;Setter Property="VisualElement.Scale" Value="1" /&gt;
///   &lt;/kf:KeyFrame&gt;
/// &lt;/kf:Keyframes&gt;
/// </code>
/// </example>
/// <remarks>
/// <para><b>Easing arrives.</b> A frame's <see cref="Easing"/> shapes the segment that <i>ends</i> at
/// it — the WinUI and storyboard convention, so <c>Easing="spring(0.4)"</c> on the last frame springs
/// into it. That is the opposite of <see cref="Key.Easing"/>, which follows CSS and shapes the segment
/// that starts at the key.</para>
/// <para>A property whose first frame is after 0% starts from its live value, as in CSS and WinUI. A
/// <see cref="Setter.TargetName"/> on a setter animates that named element instead.</para>
/// </remarks>
[ContentProperty(nameof(Setters))]
public sealed class KeyFrame : BindableObject
{
    /// <summary>Backing store for <see cref="Cue"/>.</summary>
    public static readonly BindableProperty CueProperty =
        BindableProperty.Create(nameof(Cue), typeof(double), typeof(KeyFrame), 0d);

    /// <summary>Backing store for <see cref="Easing"/>.</summary>
    public static readonly BindableProperty EasingProperty =
        BindableProperty.Create(nameof(Easing), typeof(EasingFunction), typeof(KeyFrame));

    /// <summary>Position within the iteration, 0 to 1. XAML also accepts a percentage: <c>Cue="60%"</c>.</summary>
    [TypeConverter(typeof(CueTypeConverter))]
    public double Cue
    {
        get => (double)GetValue(CueProperty);
        set => SetValue(CueProperty, value);
    }

    /// <summary>Easing for the segment arriving at this frame.</summary>
    [TypeConverter(typeof(EasingFunctionTypeConverter))]
    public EasingFunction? Easing
    {
        get => (EasingFunction?)GetValue(EasingProperty);
        set => SetValue(EasingProperty, value);
    }

    /// <summary>The property values at this instant.</summary>
    public IList<Setter> Setters { get; } = [];

    /// <summary>Turns frames into one track per (element, property).</summary>
    internal static IEnumerable<ITrack> BuildTracks(IList<KeyFrame> frames, VisualElement target, Func<string, VisualElement> resolve)
    {
        var ordered = frames.OrderBy(f => f.Cue).ToList();
        var groups = new Dictionary<(string? TargetName, BindableProperty Property), List<(double Cue, object? Value, EasingFunction? Arriving)>>();

        foreach (var frame in ordered)
        {
            foreach (var setter in frame.Setters.Where(s => s.Property is not null))
            {
                var key = (string.IsNullOrWhiteSpace(setter.TargetName) ? null : setter.TargetName, setter.Property!);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = [];
                    groups[key] = list;
                }

                list.Add((frame.Cue, setter.Value, frame.Easing));
            }
        }

        foreach (var ((targetName, property), points) in groups)
        {
            var element = targetName is null ? target : resolve(targetName);
            var raw = new List<RawKey>(points.Count + 1);

            // Starting after 0% means "from wherever it is", the way CSS and WinUI both read it.
            if (points[0].Cue > 0d)
                raw.Add(new RawKey(0d, null, points[0].Arriving));

            for (var i = 0; i < points.Count; i++)
            {
                // Storyboard easing arrives at a frame; the engine's easing departs from a key. Shift
                // each curve back one key so the authored meaning survives.
                var departing = i + 1 < points.Count ? points[i + 1].Arriving : null;
                raw.Add(new RawKey(points[i].Cue, points[i].Value, departing));
            }

            yield return AnimatableProperty.For(property).CreateTrack(element, raw);
        }
    }
}

/// <summary>Reads a cue as <c>0.6</c> or <c>60%</c>.</summary>
public sealed class CueTypeConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string);

    /// <inheritdoc />
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is not string text)
            return base.ConvertFrom(context, culture, value);

        return Parse(text);
    }

    /// <summary>Parses a cue.</summary>
    public static double Parse(string text)
    {
        text = text.Trim();
        var percent = text.EndsWith('%');
        var number = double.Parse(percent ? text[..^1] : text, NumberStyles.Float, CultureInfo.InvariantCulture);
        return percent ? number / 100d : number;
    }
}

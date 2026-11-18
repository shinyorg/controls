using System.Globalization;

namespace Shiny.Maui.Controls.Keyframe;

/// <summary>A keyframe as authored in XAML, before its value has been converted to the target type.</summary>
/// <param name="Offset">Position within the iteration, 0 to 1.</param>
/// <param name="Value">The raw value, typically a string straight from the XAML parser.
/// Null means "whatever the target's value is when playback starts".</param>
/// <param name="Easing">Easing for the segment starting at this keyframe.</param>
public readonly record struct RawKey(double Offset, object? Value, EasingFunction? Easing);

/// <summary>
/// Describes one animatable property of a view: how to read it, how to write it, how to blend it,
/// and how to parse a value the XAML parser handed us as a string.
/// </summary>
/// <remarks>
/// <para><b>Why a registry instead of reflection.</b> A XAML surface that says
/// <c>Property="Opacity"</c> has to get from that string to a real setter somehow. Reflection or a
/// compiled <c>Expression</c> would both work at runtime and both break under Native AOT and
/// aggressive trimming — the property gets rooted out of existence and the animation fails on
/// device but not in the emulator. Registering explicit delegates means the linker sees ordinary
/// static method calls, so nothing is trimmed away and nothing needs a preservation attribute.</para>
/// </remarks>
public abstract class AnimatableProperty
{
    /// <summary>The name used in XAML.</summary>
    public abstract string Name { get; }

    /// <summary>The property's value type.</summary>
    public abstract Type ValueType { get; }

    /// <summary>
    /// Whether writing this property forces a new measure and arrange pass. Animating one of these
    /// runs full layout every frame, which is the difference between a smooth animation and a
    /// janky one on a complex page.
    /// </summary>
    public abstract bool InvalidatesLayout { get; }

    /// <summary>Builds a track that drives this property on the given view.</summary>
    public abstract ITrack CreateTrack(VisualElement target, IReadOnlyList<RawKey> keys);

    /// <summary>
    /// The <see cref="BindableProperty"/> behind this name on the given view, when there is one.
    /// Properties backed by one are written through the <see cref="AnimationLayer"/>, can be
    /// transitioned implicitly, and release cleanly; delegate-only properties cannot.
    /// </summary>
    public virtual BindableProperty? GetBindableProperty(VisualElement target) => null;

    /// <summary>
    /// Describes any <see cref="BindableProperty"/> as animatable, blending it with
    /// <see cref="ValueBlending"/>. This is how spec-style <see cref="KeyFrame"/> setters and
    /// transitions reach properties that were never registered by name.
    /// </summary>
    public static AnimatableProperty For(BindableProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return new BindableAnimatableProperty(property);
    }
}

/// <summary>Any <see cref="BindableProperty"/>, blended by value type and written through the animation layer.</summary>
sealed class BindableAnimatableProperty(BindableProperty property) : AnimatableProperty
{
    public override string Name => property.PropertyName;

    public override Type ValueType => property.ReturnType;

    public override bool InvalidatesLayout => false;

    public override BindableProperty? GetBindableProperty(VisualElement target) => property;

    public override ITrack CreateTrack(VisualElement target, IReadOnlyList<RawKey> keys)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(keys);

        var type = property.ReturnType;
        var converted = new List<Key<object?>>(keys.Count);

        foreach (var key in keys)
        {
            converted.Add(key.Value is null
                ? Key<object?>.Current(key.Offset, key.Easing)
                : new Key<object?>(key.Offset, ValueBlending.Parse(type, key.Value), key.Easing));
        }

        AnimationLayer.Prepare(target, property);

        return new Track<VisualElement, object?>(
            target,
            (v, x) => AnimationLayer.Write(v, property, x),
            converted,
            new DelegateInterpolator<object?>((a, b, t) => ValueBlending.Blend(type, a, b, t)),
            v => v.GetValue(property),
            property.PropertyName,
            v => AnimationLayer.Release(v, property));
    }
}

/// <summary>A strongly typed animatable property.</summary>
/// <typeparam name="TValue">The property's value type.</typeparam>
public sealed class AnimatableProperty<TValue> : AnimatableProperty
{
    readonly Func<VisualElement, TValue> getter;
    readonly Action<VisualElement, TValue> setter;
    readonly IInterpolator<TValue> interpolator;
    readonly Func<object, TValue> parse;
    readonly Func<VisualElement, BindableProperty?>? bindable;

    /// <summary>
    /// Describes a property backed by a <see cref="BindableProperty"/>. Writes go through the
    /// <see cref="AnimationLayer"/>, so the property's binding, style and visual-state values stay live.
    /// </summary>
    /// <param name="name">The name used in XAML.</param>
    /// <param name="property">The property, or a function choosing it per view for a property several
    /// unrelated types declare (Padding).</param>
    /// <param name="interpolator">Blends between keyframe values.</param>
    /// <param name="parse">Converts a raw XAML value to <typeparamref name="TValue"/>.</param>
    /// <param name="invalidatesLayout">Whether writing forces a measure and arrange pass.</param>
    public AnimatableProperty(
        string name,
        Func<VisualElement, BindableProperty?> property,
        IInterpolator<TValue> interpolator,
        Func<object, TValue> parse,
        bool invalidatesLayout = false)
        : this(
            name,
            v => property(v) is { } bp ? (TValue)v.GetValue(bp) : default!,
            (v, x) =>
            {
                if (property(v) is { } bp)
                    AnimationLayer.Write(v, bp, x);
            },
            interpolator,
            parse,
            invalidatesLayout)
    {
        bindable = property;
    }

    /// <summary>Describes a property backed by a single <see cref="BindableProperty"/>.</summary>
    public AnimatableProperty(
        string name,
        BindableProperty property,
        IInterpolator<TValue> interpolator,
        Func<object, TValue> parse,
        bool invalidatesLayout = false)
        : this(name, _ => property, interpolator, parse, invalidatesLayout)
    {
        ArgumentNullException.ThrowIfNull(property);
    }

    /// <summary>Describes a property.</summary>
    /// <param name="name">The name used in XAML.</param>
    /// <param name="getter">Reads the current value.</param>
    /// <param name="setter">Writes a value.</param>
    /// <param name="interpolator">Blends between keyframe values.</param>
    /// <param name="parse">Converts a raw XAML value to <typeparamref name="TValue"/>.</param>
    /// <param name="invalidatesLayout">Whether writing forces a measure and arrange pass.</param>
    /// <param name="bindable">The <see cref="BindableProperty"/> behind the setter, if any, so the
    /// property can take part in implicit transitions and release. The setter itself decides how it writes.</param>
    public AnimatableProperty(
        string name,
        Func<VisualElement, TValue> getter,
        Action<VisualElement, TValue> setter,
        IInterpolator<TValue> interpolator,
        Func<object, TValue> parse,
        bool invalidatesLayout = false,
        Func<VisualElement, BindableProperty?>? bindable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(getter);
        ArgumentNullException.ThrowIfNull(setter);
        ArgumentNullException.ThrowIfNull(interpolator);
        ArgumentNullException.ThrowIfNull(parse);

        Name = name;
        this.getter = getter;
        this.setter = setter;
        this.interpolator = interpolator;
        this.parse = parse;
        this.bindable = bindable;
        InvalidatesLayout = invalidatesLayout;
    }

    /// <inheritdoc />
    public override string Name { get; }

    /// <inheritdoc />
    public override Type ValueType => typeof(TValue);

    /// <inheritdoc />
    public override bool InvalidatesLayout { get; }

    /// <inheritdoc />
    public override BindableProperty? GetBindableProperty(VisualElement target) => bindable?.Invoke(target);

    /// <inheritdoc />
    public override ITrack CreateTrack(VisualElement target, IReadOnlyList<RawKey> keys)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(keys);

        var converted = new List<Key<TValue>>(keys.Count);

        foreach (var key in keys)
        {
            // A missing value is the implicit keyframe: resolve against the live value at playback
            // time rather than a literal, so a re-triggered animation continues smoothly.
            if (key.Value is null)
            {
                converted.Add(Key<TValue>.Current(key.Offset, key.Easing));
                continue;
            }

            converted.Add(new Key<TValue>(key.Offset, Parse(key.Value), key.Easing));
        }

        var property = GetBindableProperty(target);
        if (property is null)
            return new Track<VisualElement, TValue>(target, setter, converted, interpolator, getter, Name);

        // Start tracking the base value now, so a binding update between building and the first
        // frame is still what a release lands on.
        AnimationLayer.Prepare(target, property);
        return new Track<VisualElement, TValue>(
            target, setter, converted, interpolator, getter, Name,
            v => AnimationLayer.Release(v, property));
    }

    TValue Parse(object value)
    {
        if (value is TValue typed)
            return typed;

        try
        {
            return parse(value);
        }
        catch (Exception error) when (error is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            throw new FormatException(
                $"Could not read '{value}' as a value for the '{Name}' property, which expects {typeof(TValue).Name}.",
                error);
        }
    }
}

/// <summary>
/// The set of view properties that can be animated by name from XAML.
/// </summary>
/// <remarks>
/// Register your own with <see cref="Register"/> at startup to animate properties on a custom
/// control. Built-in entries can be replaced by registering the same name again.
/// </remarks>
public static class AnimatableProperties
{
    static readonly Dictionary<string, AnimatableProperty> Registry =
        new(StringComparer.OrdinalIgnoreCase);

    static AnimatableProperties() => RegisterBuiltIns();

    /// <summary>Adds or replaces a property definition.</summary>
    public static void Register(AnimatableProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        Registry[property.Name] = property;
    }

    /// <summary>Looks up a property by name, returning null if it is not registered.</summary>
    public static AnimatableProperty? Find(string name)
        => string.IsNullOrWhiteSpace(name) ? null : Registry.GetValueOrDefault(name);

    /// <summary>Looks up a property by name, throwing a message that lists the alternatives.</summary>
    public static AnimatableProperty Get(string name)
        => Find(name) ?? throw new KeyNotFoundException(
            $"'{name}' is not a registered animatable property. " +
            $"Known properties: {string.Join(", ", Registry.Keys.Order())}. " +
            "Call AnimatableProperties.Register to add your own.");

    /// <summary>Every registered property name.</summary>
    public static IEnumerable<string> Names => Registry.Keys;

    static void RegisterBuiltIns()
    {
        // --- Transform and opacity. Cheap: these never invalidate layout, and on every platform
        // they map onto a native compositor property. Prefer them wherever a choice exists. ---
        Add("Opacity", VisualElement.OpacityProperty);
        Add("Scale", VisualElement.ScaleProperty);
        Add("ScaleX", VisualElement.ScaleXProperty);
        Add("ScaleY", VisualElement.ScaleYProperty);
        Add("TranslationX", VisualElement.TranslationXProperty);
        Add("TranslationY", VisualElement.TranslationYProperty);
        Add("RotationX", VisualElement.RotationXProperty);
        Add("RotationY", VisualElement.RotationYProperty);
        Add("AnchorX", VisualElement.AnchorXProperty);
        Add("AnchorY", VisualElement.AnchorYProperty);

        // Rotation takes the shortest arc, so 350 to 10 turns forward through zero. Use "Spin"
        // when you actually want multiple turns.
        Register(new AnimatableProperty<double>(
            "Rotation",
            VisualElement.RotationProperty,
            AngleInterpolator.Degrees,
            ParseDouble));

        Register(new AnimatableProperty<double>(
            "Spin",
            VisualElement.RotationProperty,
            DoubleInterpolator.Instance,
            ParseDouble));

        // --- Colour. Blended in Oklab so midpoints stay saturated. ---
        Register(new AnimatableProperty<Microsoft.Maui.Graphics.Color>(
            "BackgroundColor",
            static v => v.BackgroundColor ?? Colors.Transparent,
            static (v, x) => AnimationLayer.Write(v, VisualElement.BackgroundColorProperty, x),
            Shiny.Controls.Keyframe.Graphics.ColorInterpolator.Oklab,
            ParseColor,
            bindable: static _ => VisualElement.BackgroundColorProperty));

        // --- Layout-affecting. Each of these runs measure and arrange on every frame. ---
        Add("WidthRequest", VisualElement.WidthRequestProperty, invalidatesLayout: true);
        Add("HeightRequest", VisualElement.HeightRequestProperty, invalidatesLayout: true);

        Register(new AnimatableProperty<Thickness>(
            "Margin",
            static v => v is View ? View.MarginProperty : null,
            new DelegateInterpolator<Thickness>(LerpThickness),
            ParseThickness,
            invalidatesLayout: true));

        Register(new AnimatableProperty<Thickness>(
            "Padding",
            PaddingProperty,
            new DelegateInterpolator<Thickness>(LerpThickness),
            ParseThickness,
            invalidatesLayout: true));
    }

    // IPaddingElement exposes Padding read-only and several unrelated types declare their own
    // PaddingProperty, so pick it by type. Switching over them keeps this AOT-safe; reflecting for
    // a "PaddingProperty" field would not survive trimming.
    static BindableProperty? PaddingProperty(VisualElement element) => element switch
    {
        Layout => Layout.PaddingProperty,
        Border => Border.PaddingProperty,
        Page => Page.PaddingProperty,
        Button => Button.PaddingProperty,
        _ => null
    };

    static void Add(string name, BindableProperty property, bool invalidatesLayout = false)
        => Register(new AnimatableProperty<double>(
            name, property, DoubleInterpolator.Instance, ParseDouble, invalidatesLayout));

    static double ParseDouble(object value) => value switch
    {
        double d => d,
        string text => double.Parse(text, CultureInfo.InvariantCulture),
        IConvertible convertible => convertible.ToDouble(CultureInfo.InvariantCulture),
        _ => throw new FormatException($"Cannot read '{value}' as a number.")
    };

    static Microsoft.Maui.Graphics.Color ParseColor(object value) => value switch
    {
        Microsoft.Maui.Graphics.Color color => color,
        string text => Microsoft.Maui.Graphics.Color.Parse(text),
        _ => throw new FormatException($"Cannot read '{value}' as a colour.")
    };

    static Thickness ParseThickness(object value)
    {
        if (value is Thickness thickness)
            return thickness;

        if (value is double uniform)
            return new Thickness(uniform);

        if (value is not string text)
            throw new FormatException($"Cannot read '{value}' as a thickness.");

        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var numbers = Array.ConvertAll(parts, p => double.Parse(p, CultureInfo.InvariantCulture));

        return numbers.Length switch
        {
            1 => new Thickness(numbers[0]),
            2 => new Thickness(numbers[0], numbers[1]),
            4 => new Thickness(numbers[0], numbers[1], numbers[2], numbers[3]),
            _ => throw new FormatException(
                $"'{text}' is not a valid thickness. Use one, two, or four comma-separated numbers.")
        };
    }

    static Thickness LerpThickness(Thickness from, Thickness to, double t) => new(
        from.Left + (to.Left - from.Left) * t,
        from.Top + (to.Top - from.Top) * t,
        from.Right + (to.Right - from.Right) * t,
        from.Bottom + (to.Bottom - from.Bottom) * t);
}

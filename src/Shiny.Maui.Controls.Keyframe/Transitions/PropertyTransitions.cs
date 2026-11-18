using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Shiny.Maui.Controls.Keyframe;

/// <summary>
/// Animates every change of one property, whatever makes it — a binding, a style, a trigger, code.
/// The CSS <c>transition</c> property, for MAUI.
/// </summary>
public sealed class PropertyTransition : BindableObject
{
    /// <summary>Backing store for <see cref="Duration"/>.</summary>
    public static readonly BindableProperty DurationProperty =
        BindableProperty.Create(nameof(Duration), typeof(TimeSpan), typeof(PropertyTransition), TimeSpan.FromMilliseconds(200));

    /// <summary>Backing store for <see cref="Easing"/>.</summary>
    public static readonly BindableProperty EasingProperty =
        BindableProperty.Create(nameof(Easing), typeof(EasingFunction), typeof(PropertyTransition), Easings.CubicOut);

    /// <summary>
    /// The property, by registered name — <c>Opacity</c>, <c>Scale</c>, <c>BackgroundColor</c>, … — or
    /// <c>All</c> for every registered property the element has. For a property that isn't registered,
    /// set <see cref="BindableProperty"/> instead.
    /// </summary>
    public string? Property { get; set; }

    /// <summary>Any bindable property, e.g. <c>{x:Static Label.TextColorProperty}</c>.</summary>
    public BindableProperty? BindableProperty { get; set; }

    /// <summary>How long each change takes. 200 ms by default.</summary>
    public TimeSpan Duration
    {
        get => (TimeSpan)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    /// <summary>Wait before a change starts moving.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>The curve. CubicOut by default.</summary>
    [TypeConverter(typeof(EasingFunctionTypeConverter))]
    public EasingFunction? Easing
    {
        get => (EasingFunction?)GetValue(EasingProperty);
        set => SetValue(EasingProperty, value);
    }

    /// <summary>What happens under the OS reduced-motion setting. Auto snaps.</summary>
    public ReducedMotionBehavior ReducedMotion { get; set; }

    internal bool IsAll => string.Equals(Property, "All", StringComparison.OrdinalIgnoreCase) || Property == "*";
}

/// <summary>The <see cref="PropertyTransition"/>s attached to an element.</summary>
public sealed class PropertyTransitionCollection : ObservableCollection<PropertyTransition>;

/// <summary>
/// Implicit transitions: attach <see cref="PropertyTransition"/>s to an element and changes to those
/// properties animate on their own.
/// </summary>
/// <example>
/// <code language="xml">
/// &lt;Border Opacity="{Binding IsSelected, Converter={StaticResource SelectedOpacity}}"&gt;
///   &lt;kf:PropertyTransitions.Transitions&gt;
///     &lt;kf:PropertyTransition Property="Opacity" Duration="0:0:0.2" /&gt;
///     &lt;kf:PropertyTransition Property="Scale" Easing="spring(0.5, 14)" Duration="0:0:0.5" /&gt;
///   &lt;/kf:PropertyTransitions.Transitions&gt;
/// &lt;/Border&gt;
/// </code>
/// </example>
/// <remarks>
/// The value that was set stays in the property's own source the whole time —
/// <see cref="AnimationLayer.GetBaseValue"/> returns it — and only what you <i>see</i> travels there,
/// through the <see cref="AnimationLayer"/>; while the transition runs, the property getter returns the
/// on-screen value. Changes made before the element has a handler (initial values) don't animate.
/// </remarks>
public static class PropertyTransitions
{
    /// <summary>The transitions for an element.</summary>
    public static readonly BindableProperty TransitionsProperty =
        BindableProperty.CreateAttached(
            "Transitions",
            typeof(PropertyTransitionCollection),
            typeof(PropertyTransitions),
            defaultValue: null,
            propertyChanged: OnTransitionsChanged,
            defaultValueCreator: static b =>
            {
                var collection = new PropertyTransitionCollection();
                collection.CollectionChanged += (_, _) => Ensure(b, collection);
                return collection;
            });

    static readonly ConditionalWeakTable<VisualElement, Controller> Controllers = [];

    /// <summary>Gets an element's transitions.</summary>
    public static PropertyTransitionCollection GetTransitions(BindableObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (PropertyTransitionCollection)target.GetValue(TransitionsProperty);
    }

    /// <summary>Sets an element's transitions.</summary>
    public static void SetTransitions(BindableObject target, PropertyTransitionCollection value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(TransitionsProperty, value);
    }

    static void OnTransitionsChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (newValue is PropertyTransitionCollection collection)
            Ensure(bindable, collection);
    }

    static void Ensure(BindableObject bindable, PropertyTransitionCollection collection)
    {
        if (bindable is not VisualElement element)
            return;

        var controller = Controllers.GetValue(element, static e => new Controller(e));
        controller.Collection = collection;
    }

    sealed class Controller
    {
        readonly Dictionary<string, object?> pendingOld = new(StringComparer.Ordinal);

        public Controller(VisualElement element)
        {
            element.PropertyChanging += OnPropertyChanging;
            element.PropertyChanged += OnPropertyChanged;
        }

        public PropertyTransitionCollection? Collection { get; set; }

        void OnPropertyChanging(object? sender, Microsoft.Maui.Controls.PropertyChangingEventArgs e)
        {
            if (sender is not VisualElement element || e.PropertyName is not { } name || !ShouldTrack(element, name))
                return;

            if (Resolve(element, name) is { } match)
                pendingOld.TryAdd(name, AnimationLayer.GetShownValue(element, match.Property));
        }

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not VisualElement element || e.PropertyName is not { } name)
                return;

            // Take the captured old value whatever happens next, so a skipped change can't leave a
            // stale one behind for the next genuine change to start from.
            if (!pendingOld.Remove(name, out var old) || !ShouldTrack(element, name) || Resolve(element, name) is not { } match)
                return;

            TransitionRunner.Start(
                element,
                match.Property,
                match.RegisteredName,
                old,
                element.GetValue(match.Property),
                match.Transition.Duration,
                match.Transition.Delay,
                match.Transition.Easing,
                match.Transition.ReducedMotion);
        }

        static bool ShouldTrack(VisualElement element, string name)
            => element.Handler is not null && !AnimationLayer.IsWriting(element, name);

        (PropertyTransition Transition, BindableProperty Property, string? RegisteredName)? Resolve(VisualElement element, string name)
        {
            if (Collection is null || Collection.Count == 0)
                return null;

            // An explicit entry beats "All".
            foreach (var transition in Collection.Where(t => !t.IsAll))
            {
                if (transition.BindableProperty is { } explicitProperty && explicitProperty.PropertyName == name)
                    return (transition, explicitProperty, null);

                var registered = transition.Property is null ? null : AnimatableProperties.Find(transition.Property);
                if (registered?.GetBindableProperty(element) is { } bp && bp.PropertyName == name)
                    return (transition, bp, transition.Property);
            }

            var all = Collection.FirstOrDefault(t => t.IsAll);
            if (all is null)
                return null;

            // "All" covers the registered names, resolved per element so Padding finds the right type's.
            var match = AnimatableProperties.Names
                .Select(n => (Name: n, Property: AnimatableProperties.Find(n)?.GetBindableProperty(element)))
                .FirstOrDefault(x => x.Property?.PropertyName == name && !string.Equals(x.Name, "Spin", StringComparison.OrdinalIgnoreCase));

            return match.Property is null ? null : (all, match.Property, match.Name);
        }
    }
}

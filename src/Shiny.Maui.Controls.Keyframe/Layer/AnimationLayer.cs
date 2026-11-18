using System.Runtime.CompilerServices;

namespace Shiny.Maui.Controls.Keyframe;

/// <summary>
/// Shows an animated value on a property without taking the property over.
/// </summary>
/// <remarks>
/// <para><b>The problem.</b> Every MAUI animation — <c>FadeToAsync</c>, <c>Animation</c>, and this
/// library before this layer existed — writes with a plain <c>SetValue</c>. That is a local value: it
/// outranks the property's binding, style and visual-state setters, so they keep updating underneath
/// and nothing shows; and on a property with a <c>{DynamicResource}</c> it removes the resource
/// association for good. One fade, and the binding is dead.</para>
/// <para><b>The layer.</b> MAUI keeps one value slot above all the others — the one a handler writes
/// into when user input changes a property — and it has exactly the semantics an animation wants:
/// it shows over everything, and the moment anything else writes the property (a binding update, a
/// style, a visual state, a trigger, your own code) MAUI drops the slot and the new value wins. So an
/// animation written here <i>shows</i> a value without <i>owning</i> the property, and when it
/// releases, whatever the binding, style or state says is simply what you see.</para>
/// <para><b>Base value.</b> While a property is animating, the layer tracks the value it would have
/// without the animation — updated whenever something other than the animation writes it — so a
/// release lands on the live value, not a stale one captured at the start. That is what lets a
/// transition land exactly on the value a state or binding sets, and what lets a storyboard and a
/// <c>FadeToAsync</c> on the same property coexist: the storyboard wins while it runs, the other keeps
/// running underneath, and its latest value shows when the storyboard ends.</para>
/// <para><b>What it can't do.</b> A <c>{DynamicResource}</c> written directly on the animated
/// property is detached by the first frame, exactly as any <c>SetValue</c> would; set it through a
/// <c>Style</c> instead and it stays live. A <c>TwoWay</c> binding lives in this same slot, so its
/// source receives the animated values — avoid animating a property the user also edits.</para>
/// </remarks>
public static class AnimationLayer
{
    static readonly ConditionalWeakTable<VisualElement, ElementLayer> Layers = [];

    /// <summary>Raised on every write a source other than the animation layer makes to a property that is
    /// currently tracked, with the element and the property. Transitions use this to retarget.</summary>
    internal static event Action<VisualElement, BindableProperty>? ExternalWrite;

    [ThreadStatic] static VisualElement? writingElement;
    [ThreadStatic] static BindableProperty? writingProperty;

    /// <summary>Whether the change being notified right now is the animation layer's own write.</summary>
    internal static bool IsWriting(VisualElement element, string? propertyName)
    {
        if (ReferenceEquals(writingElement, element) && writingProperty?.PropertyName == propertyName)
            return true;

        // A write made while MAUI was still notifying a change to the same property is queued and
        // applied after that notification — after the flags above are cleared. Recognise it by value.
        return propertyName is not null
            && Layers.TryGetValue(element, out var layer)
            && layer.TryGetSlot(propertyName, out var slot)
            && slot.IsEcho(element);
    }

    /// <summary>Writes an animated value. The property's own value sources are left untouched.</summary>
    public static void Write(VisualElement element, BindableProperty property, object? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(property);

        var slot = Layers.GetValue(element, static e => new ElementLayer(e)).Track(property);
        WriteThrough(element, slot, value);
        slot.Shown = value;
        slot.Active = true;
    }

    /// <summary>
    /// The value the property would have without the animation — the WPF <c>GetAnimationBaseValue</c>.
    /// For a property that is not animating this is simply its current value.
    /// </summary>
    public static object? GetBaseValue(VisualElement element, BindableProperty property)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(property);

        if (Layers.TryGetValue(element, out var layer) && layer.TryGetSlot(property, out var slot) && slot.Active)
            return slot.BaseValue;

        return element.GetValue(property);
    }

    /// <summary>Whether an animation is currently showing a value on the property.</summary>
    public static bool IsAnimated(VisualElement element, BindableProperty property)
        => Layers.TryGetValue(element, out var layer) && layer.TryGetSlot(property, out var slot) && slot.Active;

    /// <summary>
    /// Withdraws the animated value so the property shows its own — binding, style, visual state or
    /// local value, whichever is in charge now.
    /// </summary>
    public static void Release(VisualElement element, BindableProperty property)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(property);

        if (!Layers.TryGetValue(element, out var layer) || !layer.TryGetSlot(property, out var slot))
            return;

        if (!slot.Active)
        {
            layer.Untrack(property);
            return;
        }

        // MAUI has no public way to remove only the top slot, so put the base value into it. The
        // screen is now exactly what the property's own sources say, and since anything else that
        // writes the property drops the slot, it never masks a later update.
        WriteThrough(element, slot, slot.BaseValue);
        slot.Active = false;
        layer.Untrack(property);
    }

    static void WriteThrough(VisualElement element, Slot slot, object? value)
    {
        var previousElement = writingElement;
        var previousProperty = writingProperty;
        writingElement = element;
        writingProperty = slot.Property;
        slot.Writing = true;
        try
        {
            element.SetValueFromRenderer(slot.Property, value);

            // Inside the property's own change notification MAUI queues the write and applies it once
            // the notification returns. Remember it, so its change event isn't mistaken for someone
            // else's — which would start a transition that writes, which queues, which…
            slot.HasEcho = !Equals(element.GetValue(slot.Property), value);
            slot.Echo = slot.HasEcho ? value : null;
        }
        finally
        {
            slot.Writing = false;
            writingElement = previousElement;
            writingProperty = previousProperty;
        }
    }

    /// <summary>
    /// The value on screen right now: the last animated value while an animation is showing one,
    /// otherwise the property's value. Use it, not <c>GetValue</c>, as the "from" of a change made
    /// mid-animation — MAUI drops the animation slot before it raises <c>PropertyChanging</c>, so by
    /// then <c>GetValue</c> has already lost the value that was on screen.
    /// </summary>
    internal static object? GetShownValue(VisualElement element, BindableProperty property)
    {
        if (Layers.TryGetValue(element, out var layer) && layer.TryGetSlot(property, out var slot) && slot.Active)
            return slot.Shown;

        return element.GetValue(property);
    }

    /// <summary>Starts tracking a property's base value ahead of the first write, so changes made
    /// before the animation's first frame are not missed.</summary>
    internal static void Prepare(VisualElement element, BindableProperty property)
        => Layers.GetValue(element, static e => new ElementLayer(e)).Track(property);

    sealed class Slot(BindableProperty property, object? baseValue)
    {
        public BindableProperty Property { get; } = property;
        public object? BaseValue { get; set; } = baseValue;
        public bool Writing { get; set; }
        public bool Active { get; set; }

        /// <summary>A write MAUI deferred, still to land; see <see cref="WriteThrough"/>.</summary>
        public object? Shown { get; set; }
        public bool HasEcho { get; set; }
        public object? Echo { get; set; }

        public bool IsEcho(VisualElement element) => HasEcho && Equals(element.GetValue(Property), Echo);
    }

    sealed class ElementLayer
    {
        readonly WeakReference<VisualElement> element;
        readonly Dictionary<string, Slot> slots = new(StringComparer.Ordinal);

        public ElementLayer(VisualElement element)
        {
            this.element = new WeakReference<VisualElement>(element);
        }

        public bool TryGetSlot(BindableProperty property, out Slot slot)
            => slots.TryGetValue(property.PropertyName, out slot!);

        public bool TryGetSlot(string propertyName, out Slot slot)
            => slots.TryGetValue(propertyName, out slot!);

        public Slot Track(BindableProperty property)
        {
            if (slots.TryGetValue(property.PropertyName, out var existing))
                return existing;

            element.TryGetTarget(out var target);
            var slot = new Slot(property, target?.GetValue(property));

            if (slots.Count == 0 && target is not null)
                target.PropertyChanged += OnPropertyChanged;

            slots[property.PropertyName] = slot;
            return slot;
        }

        public void Untrack(BindableProperty property)
        {
            slots.Remove(property.PropertyName);

            if (slots.Count == 0 && element.TryGetTarget(out var target))
                target.PropertyChanged -= OnPropertyChanged;
        }

        void OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not { } name || !slots.TryGetValue(name, out var slot) || slot.Writing)
                return;

            if (sender is not VisualElement target || slot.IsEcho(target))
                return;

            // Someone other than the animation wrote the property, and in doing so MAUI dropped the
            // animation slot — so what GetValue returns now is the property's own value.
            slot.BaseValue = target.GetValue(slot.Property);
            ExternalWrite?.Invoke(target, slot.Property);
        }
    }
}

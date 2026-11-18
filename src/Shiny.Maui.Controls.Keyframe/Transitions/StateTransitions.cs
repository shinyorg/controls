using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Shiny.Maui.Controls.Keyframe;

/// <summary>
/// How a visual-state change animates: which change it applies to, how long it takes, and its curve.
/// </summary>
/// <remarks>
/// The most specific match wins: a transition naming both <see cref="From"/> and <see cref="To"/> beats
/// one naming either, which beats one naming neither; one naming its <see cref="Group"/> beats one that
/// doesn't. A state change no transition matches snaps, exactly as it does without any.
/// </remarks>
public sealed class VisualTransition : BindableObject
{
    /// <summary>Backing store for <see cref="Duration"/>.</summary>
    public static readonly BindableProperty DurationProperty =
        BindableProperty.Create(nameof(Duration), typeof(TimeSpan), typeof(VisualTransition), TimeSpan.FromMilliseconds(150));

    /// <summary>Backing store for <see cref="Easing"/>.</summary>
    public static readonly BindableProperty EasingProperty =
        BindableProperty.Create(nameof(Easing), typeof(EasingFunction), typeof(VisualTransition), Easings.CubicOut);

    /// <summary>The visual state group this applies to. Unset matches every group.</summary>
    public string? Group { get; set; }

    /// <summary>The state being left. Unset or <c>*</c> matches any.</summary>
    public string? From { get; set; }

    /// <summary>The state being entered. Unset or <c>*</c> matches any.</summary>
    public string? To { get; set; }

    /// <summary>How long the change takes. 150 ms by default.</summary>
    public TimeSpan Duration
    {
        get => (TimeSpan)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    /// <summary>Wait before the change starts moving; the old value holds meanwhile.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>The curve. CubicOut by default; any curve text works, springs included.</summary>
    [TypeConverter(typeof(EasingFunctionTypeConverter))]
    public EasingFunction? Easing
    {
        get => (EasingFunction?)GetValue(EasingProperty);
        set => SetValue(EasingProperty, value);
    }

    /// <summary>What happens under the OS reduced-motion setting. Auto snaps.</summary>
    public ReducedMotionBehavior ReducedMotion { get; set; }

    /// <summary>
    /// Optional animation played alongside the property changes — a shake on entering an error state,
    /// a burst on selection — on the element whose state changed.
    /// </summary>
    public IAnimationDefinition? Animation { get; set; }

    internal int Score(string group, string? from, string to)
    {
        if (!Matches(Group, group) || !Matches(From, from) || !Matches(To, to))
            return -1;

        return (IsSet(Group) ? 4 : 0) + (IsSet(From) ? 2 : 0) + (IsSet(To) ? 2 : 0);
    }

    static bool IsSet(string? value) => !string.IsNullOrWhiteSpace(value) && value != "*";

    static bool Matches(string? pattern, string? value)
        => !IsSet(pattern) || string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// An animation that plays for as long as an element is in a visual state — a pulse while busy, a
/// glow while focused — and stops, handing every property back, when it leaves.
/// </summary>
[ContentProperty(nameof(Animation))]
public sealed class StateAnimation : BindableObject
{
    /// <summary>The state that runs the animation.</summary>
    public string? State { get; set; }

    /// <summary>The group the state belongs to. Unset matches a state of that name in any group.</summary>
    public string? Group { get; set; }

    /// <summary>The animation to run.</summary>
    public IAnimationDefinition? Animation { get; set; }

    internal bool Matches(string group, string? state)
        => state is not null
           && string.Equals(State, state, StringComparison.OrdinalIgnoreCase)
           && (string.IsNullOrWhiteSpace(Group) || string.Equals(Group, group, StringComparison.OrdinalIgnoreCase));
}

/// <summary>The <see cref="VisualTransition"/>s and <see cref="StateAnimation"/>s attached to an element.</summary>
public sealed class StateTransitionCollection : ObservableCollection<BindableObject>
{
    internal IEnumerable<VisualTransition> Transitions => this.OfType<VisualTransition>();

    internal IEnumerable<StateAnimation> Animations => this.OfType<StateAnimation>();

    /// <inheritdoc />
    protected override void InsertItem(int index, BindableObject item)
    {
        if (item is not (VisualTransition or StateAnimation))
            throw new InvalidOperationException(
                $"StateTransitions can contain VisualTransition and StateAnimation elements, not {item?.GetType().Name ?? "null"}.");

        base.InsertItem(index, item);
    }
}

/// <summary>
/// Animated visual states: attach <see cref="VisualTransition"/>s to an element and its visual state
/// changes animate instead of snapping — from <c>VisualStateManager.GoToState</c>, a state trigger, or
/// MAUI's own Pressed, PointerOver, Focused and Disabled states.
/// </summary>
/// <example>
/// <code language="xml">
/// &lt;Button Text="Save"&gt;
///   &lt;kf:StateTransitions.Transitions&gt;
///     &lt;kf:VisualTransition Duration="0:0:0.15" /&gt;
///     &lt;kf:VisualTransition To="Pressed" Duration="0:0:0.08" Easing="CubicOut" /&gt;
///     &lt;kf:StateAnimation State="Disabled" Animation="{StaticResource Dim}" /&gt;
///   &lt;/kf:StateTransitions.Transitions&gt;
///   &lt;VisualStateManager.VisualStateGroups&gt; ... &lt;/VisualStateManager.VisualStateGroups&gt;
/// &lt;/Button&gt;
/// </code>
/// </example>
/// <remarks>
/// <para><b>How.</b> <c>VisualStateGroup</c> is sealed and its state changes raise nothing public, so
/// each state gets one extra setter at the end of its list that writes a marker property. When the
/// marker changes, every setter of the new state has just been applied; each property that changed in
/// that same pass animates from the value it was showing to the value the state set — through the
/// <see cref="AnimationLayer"/>, so the state still owns the value and the transition just lands on it.
/// Setters with a <c>TargetName</c> animate their named element.</para>
/// <para><b>Interrupted mid-flight</b>, the next transition starts from wherever this one had got to:
/// MAUI reports the on-screen value as the old value of the state change.</para>
/// </remarks>
public static class StateTransitions
{
    /// <summary>The transitions and state animations for an element.</summary>
    public static readonly BindableProperty TransitionsProperty =
        BindableProperty.CreateAttached(
            "Transitions",
            typeof(StateTransitionCollection),
            typeof(StateTransitions),
            defaultValue: null,
            propertyChanged: OnTransitionsChanged,
            defaultValueCreator: static b =>
            {
                var collection = new StateTransitionCollection();
                collection.CollectionChanged += (_, _) => Ensure(b, collection);
                return collection;
            });

    /// <summary>Written by the injected marker setters; its change is the "state change finished" signal.</summary>
    internal static readonly BindableProperty MarkerProperty =
        BindableProperty.CreateAttached(
            "Marker",
            typeof(string),
            typeof(StateTransitions),
            defaultValue: null,
            propertyChanged: OnMarkerChanged);

    static readonly ConditionalWeakTable<VisualElement, Controller> Controllers = [];

    /// <summary>Gets an element's transitions.</summary>
    public static StateTransitionCollection GetTransitions(BindableObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (StateTransitionCollection)target.GetValue(TransitionsProperty);
    }

    /// <summary>Sets an element's transitions.</summary>
    public static void SetTransitions(BindableObject target, StateTransitionCollection value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(TransitionsProperty, value);
    }

    /// <summary>
    /// <c>VisualStateManager.GoToState</c> with a choice: <paramref name="useTransitions"/> false snaps
    /// this one change even when transitions are attached.
    /// </summary>
    public static bool GoToState(VisualElement element, string stateName, bool useTransitions)
    {
        ArgumentNullException.ThrowIfNull(element);

        if (useTransitions || !Controllers.TryGetValue(element, out var controller))
            return VisualStateManager.GoToState(element, stateName);

        controller.Suppressed = true;
        try
        {
            return VisualStateManager.GoToState(element, stateName);
        }
        finally
        {
            controller.Suppressed = false;
        }
    }

    static void OnTransitionsChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (newValue is StateTransitionCollection collection)
            Ensure(bindable, collection);
    }

    static void Ensure(BindableObject bindable, StateTransitionCollection collection)
    {
        if (bindable is not VisualElement element || collection.Count == 0)
            return;

        var controller = Controllers.GetValue(element, static e => new Controller(e));
        controller.Collection = collection;
        controller.Inject();
    }

    static void OnMarkerChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is VisualElement element && newValue is string marker && Controllers.TryGetValue(element, out var controller))
            controller.Commit(marker);
    }

    sealed class Controller
    {
        readonly WeakReference<VisualElement> owner;
        readonly Dictionary<string, string?> lastState = new(StringComparer.Ordinal);
        // A burst of state setters is applied synchronously, so anything captured more than a moment
        // before the commit belongs to some earlier, unrelated change.
        static readonly TimeSpan Fresh = TimeSpan.FromMilliseconds(50);
        readonly Dictionary<(VisualElement Element, string Property), (object? Value, long Stamp)> pendingOld = [];
        readonly Dictionary<VisualElement, Dictionary<string, BindableProperty>> watched = new(ReferenceEqualityComparer.Instance);
        readonly HashSet<VisualStateGroup> injected = new(ReferenceEqualityComparer.Instance);

        public Controller(VisualElement element)
        {
            owner = new WeakReference<VisualElement>(element);
            element.PropertyChanged += OnOwnerPropertyChanged;
            element.Loaded += (_, _) => Inject();
        }

        public StateTransitionCollection? Collection { get; set; }

        public bool Suppressed { get; set; }

        public void Inject()
        {
            if (!owner.TryGetTarget(out var element))
                return;

            var groups = VisualStateManager.GetVisualStateGroups(element);
            if (groups is null)
                return;

            var fresh = groups
                .Select((group, index) => (Group: group, Key: GroupKey(group, index)))
                .Where(x => injected.Add(x.Group))
                .ToList();

            foreach (var (group, key) in fresh)
            {
                lastState.TryAdd(key, group.CurrentState?.Name);

                foreach (var state in group.States.Where(st => !st.Setters.Any(s => s.Property == MarkerProperty)))
                    state.Setters.Add(new Setter { Property = MarkerProperty, Value = $"{key}|{state.Name}" });

                var setters = group.States
                    .SelectMany(st => st.Setters)
                    .Where(s => s.Property is not null && s.Property != MarkerProperty);

                foreach (var setter in setters)
                    Watch(ResolveTarget(element, setter), setter.Property!);
            }
        }

        void Watch(VisualElement? target, BindableProperty property)
        {
            if (target is null)
                return;

            if (!watched.TryGetValue(target, out var properties))
            {
                properties = new Dictionary<string, BindableProperty>(StringComparer.Ordinal);
                watched[target] = properties;
                target.PropertyChanging += OnWatchedPropertyChanging;
            }

            properties[property.PropertyName] = property;
        }

        void OnOwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // A style can replace the groups after the transitions were attached; mark the new ones.
            if (e.PropertyName == VisualStateManager.VisualStateGroupsProperty.PropertyName)
                Inject();
        }

        void OnWatchedPropertyChanging(object? sender, Microsoft.Maui.Controls.PropertyChangingEventArgs e)
        {
            if (sender is not VisualElement target || e.PropertyName is not { } name)
                return;

            if (AnimationLayer.IsWriting(target, name) || target.Handler is null)
                return;

            if (!watched.TryGetValue(target, out var properties) || !properties.TryGetValue(name, out var property))
                return;

            // The first old value seen in a burst is what was on screen at the last frame — including a
            // transition's in-flight value, which is what makes interruption seamless.
            var key = (target, name);
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!pendingOld.TryGetValue(key, out var existing) || IsStale(existing.Stamp, now))
                pendingOld[key] = (AnimationLayer.GetShownValue(target, property), now);
        }

        static bool IsStale(long stamp, long now)
            => System.Diagnostics.Stopwatch.GetElapsedTime(stamp, now) > Fresh;

        public void Commit(string marker)
        {
            if (!owner.TryGetTarget(out var element) || Collection is null)
                return;

            var split = marker.IndexOf('|');
            if (split < 0)
                return;

            var group = marker[..split];
            var to = marker[(split + 1)..];
            lastState.TryGetValue(group, out var from);
            lastState[group] = to;

            if (from == to)
                return;

            var groupName = group.StartsWith('#') ? "" : group;
            RunStateAnimations(element, groupName, from, to);

            var transition = Suppressed
                ? null
                : Collection.Transitions
                    .Select(t => (Transition: t, Score: t.Score(groupName, from, to)))
                    .Where(x => x.Score >= 0)
                    .OrderByDescending(x => x.Score)
                    .Select(x => x.Transition)
                    .FirstOrDefault();

            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var changed = pendingOld.Where(x => !IsStale(x.Value.Stamp, now)).ToList();
            pendingOld.Clear();

            foreach (var ((target, name), (old, _)) in changed)
            {
                var property = watched[target][name];

                if (transition is null)
                {
                    // No transition matches this change: snap, and drop any transition still running
                    // so it can't keep writing over the new state.
                    TransitionRunner.Stop(target, property);
                }
                else
                {
                    TransitionRunner.Start(
                        target, property, name, old, target.GetValue(property),
                        transition.Duration, transition.Delay, transition.Easing, transition.ReducedMotion);
                }
            }

            if (transition?.Animation is { } extra)
                AnimationRuns.Begin(element, extra);
        }

        void RunStateAnimations(VisualElement element, string group, string? from, string to)
        {
            foreach (var animation in Collection!.Animations.Where(a => a.Animation is not null && a.Matches(group, from)))
                AnimationRuns.Stop(element, animation.Animation!, StopBehavior.Release);

            foreach (var animation in Collection.Animations.Where(a => a.Animation is not null && a.Matches(group, to)))
                AnimationRuns.Begin(element, animation.Animation!);
        }

        static VisualElement? ResolveTarget(VisualElement element, Setter setter)
            => string.IsNullOrWhiteSpace(setter.TargetName) ? element : NameScope.Find(element, setter.TargetName);

        // An unnamed group still needs a stable key; its index is stable for the life of the list.
        static string GroupKey(VisualStateGroup group, int index)
            => string.IsNullOrWhiteSpace(group.Name) ? $"#{index}" : group.Name;
    }
}

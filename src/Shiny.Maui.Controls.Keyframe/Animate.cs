namespace Shiny.Maui.Controls.Keyframe;

/// <summary>
/// The attached-property surface: hangs a keyframe animation off any view, in XAML or in code.
/// </summary>
/// <example>
/// <code language="xml">
/// &lt;Border&gt;
///   &lt;kf:Animate.Keyframes&gt;
///     &lt;kf:Keyframes Duration="0:0:1.2" Iterations="Infinite" Direction="Alternate"&gt;
///       &lt;kf:Track Property="Scale"&gt;
///         &lt;kf:Key Offset="0" Value="1" /&gt;
///         &lt;kf:Key Offset="0.5" Value="1.2" Easing="CubicOut" /&gt;
///         &lt;kf:Key Offset="1" Value="1" /&gt;
///       &lt;/kf:Track&gt;
///     &lt;/kf:Keyframes&gt;
///   &lt;/kf:Animate.Keyframes&gt;
/// &lt;/Border&gt;
/// </code>
/// </example>
public static class Animate
{
    /// <summary>
    /// Attaches a keyframe animation to a view. Building and playback are deferred until the
    /// element loads, because a track needs a live element to read its starting value from.
    /// </summary>
    public static readonly BindableProperty KeyframesProperty =
        BindableProperty.CreateAttached(
            "Keyframes",
            typeof(Keyframes),
            typeof(Animate),
            defaultValue: null,
            propertyChanged: OnKeyframesChanged);

    /// <summary>
    /// Attaches a <see cref="Keyframe.Choreography"/> — several animations on one clock, staggered or
    /// sequenced — to a view. Plays on load like <see cref="KeyframesProperty"/>.
    /// </summary>
    public static readonly BindableProperty ChoreographyProperty =
        BindableProperty.CreateAttached(
            "Choreography",
            typeof(Choreography),
            typeof(Animate),
            defaultValue: null,
            propertyChanged: OnChoreographyChanged);

    static readonly BindableProperty ChoreographyControllerProperty =
        BindableProperty.CreateAttached(
            "ChoreographyController",
            typeof(AnimationController),
            typeof(Animate),
            defaultValue: null);

    /// <summary>Holds the live playback state for an element, so it can be paused, seeked or stopped.</summary>
    static readonly BindableProperty ControllerProperty =
        BindableProperty.CreateAttached(
            "Controller",
            typeof(AnimationController),
            typeof(Animate),
            defaultValue: null);

    /// <summary>Reads the attached animation.</summary>
    public static Keyframes? GetKeyframes(BindableObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (Keyframes?)target.GetValue(KeyframesProperty);
    }

    /// <summary>Attaches an animation.</summary>
    public static void SetKeyframes(BindableObject target, Keyframes? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(KeyframesProperty, value);
    }

    /// <summary>Reads the attached choreography.</summary>
    public static Choreography? GetChoreography(BindableObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (Choreography?)target.GetValue(ChoreographyProperty);
    }

    /// <summary>Attaches a choreography.</summary>
    public static void SetChoreography(BindableObject target, Choreography? value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(ChoreographyProperty, value);
    }

    /// <summary>
    /// Gets the player driving an element's attached animation, for pausing, seeking or scrubbing.
    /// Returns null until the element has loaded and the animation has been built.
    /// </summary>
    public static Player? GetPlayer(BindableObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return ((AnimationController?)target.GetValue(ControllerProperty))?.Player;
    }

    /// <summary>Gets the player driving an element's attached choreography.</summary>
    public static Player? GetChoreographyPlayer(BindableObject target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return ((AnimationController?)target.GetValue(ChoreographyControllerProperty))?.Player;
    }

    /// <summary>
    /// Builds and plays a XAML-style definition against a view, returning the player. Playing the
    /// same definition on the same view again replaces the earlier run, continuing from wherever
    /// it got to; trigger actions and <see cref="StopAnimations"/> can reach it afterwards.
    /// </summary>
    public static Player Play(this VisualElement element, IAnimationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(definition);
        return AnimationRuns.Begin(element, definition);
    }

    /// <summary>Plays a definition and completes when it finishes (true) or is stopped or cancelled (false).</summary>
    public static Task<bool> PlayAsync(this VisualElement element, IAnimationDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(definition);
        return AnimationRuns.BeginAsync(element, definition, cancellationToken);
    }

    /// <summary>
    /// Stops every definition started on the element by <see cref="Play(VisualElement, IAnimationDefinition)"/>
    /// or a trigger action. <see cref="StopBehavior.Release"/>, the default, hands each property back to
    /// its binding, style or visual state.
    /// </summary>
    public static void StopAnimations(this VisualElement element, StopBehavior behavior = StopBehavior.Release)
    {
        ArgumentNullException.ThrowIfNull(element);
        AnimationRuns.StopAll(element, behavior);
    }

    /// <summary>The player running a definition on an element, if it has been started there.</summary>
    public static Player? GetPlayer(this VisualElement element, IAnimationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(definition);
        return AnimationRuns.Find(element, definition);
    }

    /// <summary>
    /// Builds and plays a timeline against a view immediately, returning the player. Use this when
    /// the animation is composed in code rather than declared in XAML.
    /// </summary>
    /// <param name="element">The view to animate.</param>
    /// <param name="timeline">The timeline to run.</param>
    /// <param name="clock">Frame source. Defaults to the element's platform ticker.</param>
    public static Player Play(this VisualElement element, IAnimationNode timeline, IClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(timeline);

        var player = new Player(timeline, clock ?? MauiClock.For(element));
        player.Play();
        return player;
    }

    static void OnKeyframesChanged(BindableObject bindable, object oldValue, object newValue)
        => Attach(bindable, ControllerProperty, newValue as IAnimationDefinition, "Keyframes");

    static void OnChoreographyChanged(BindableObject bindable, object oldValue, object newValue)
        => Attach(bindable, ChoreographyControllerProperty, newValue as IAnimationDefinition, "Choreography");

    static void Attach(BindableObject bindable, BindableProperty controllerProperty, IAnimationDefinition? definition, string name)
    {
        if (bindable is not VisualElement element)
            throw new InvalidOperationException(
                $"Animate.{name} can only be attached to a {nameof(VisualElement)}, " +
                $"but was attached to {bindable.GetType().Name}.");

        // Tear down whatever was running before, so reassigning the property does not leave an
        // orphaned player still writing to the view every frame.
        if (element.GetValue(controllerProperty) is AnimationController existing)
        {
            existing.Dispose();
            element.SetValue(controllerProperty, null);
        }

        if (definition is null)
            return;

        element.SetValue(controllerProperty, new AnimationController(element, definition));
    }

    /// <summary>
    /// Owns the lifetime of one attached animation: builds it once the element is loaded, starts it
    /// if asked, and stops it when the element is unloaded.
    /// </summary>
    sealed class AnimationController : IDisposable
    {
        readonly WeakReference<VisualElement> element;
        readonly IAnimationDefinition keyframes;

        MauiClock? clock;
        bool disposed;

        public AnimationController(VisualElement element, IAnimationDefinition keyframes)
        {
            this.element = new WeakReference<VisualElement>(element);
            this.keyframes = keyframes;

            element.Loaded += OnLoaded;
            element.Unloaded += OnUnloaded;

            // An element that is already loaded will never raise Loaded again, so build now.
            if (element.IsLoaded)
                Build(element);
        }

        public Player? Player { get; private set; }

        void OnLoaded(object? sender, EventArgs e)
        {
            if (sender is VisualElement loaded)
                Build(loaded);
        }

        void OnUnloaded(object? sender, EventArgs e)
        {
            // Stop the player only. The clock is shared across every animation on the window, so
            // stopping it here would freeze all of them — and MAUI raises Unloaded during ordinary
            // layout churn, not just teardown. The player unsubscribes on Stop, which is enough to
            // let the clock idle once nothing at all is listening.
            Player?.Stop();
        }

        void Build(VisualElement target)
        {
            if (disposed)
                return;

            if (Player is not null)
            {
                // Already built — this is a reload, so just start again if it plays automatically.
                if (keyframes.AutoPlay)
                    Player.Play();

                return;
            }

            var timeline = keyframes.Build(target, name => NameScope.Find(target, name));

            clock = MauiClock.For(target);
            Player = new Player(timeline, clock) { Rate = keyframes.Speed, ReducedMotion = keyframes.ReducedMotion };

            if (keyframes.AutoPlay)
                Player.Play();
            else
                timeline.CaptureBaselines();
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            if (element.TryGetTarget(out var target))
            {
                target.Loaded -= OnLoaded;
                target.Unloaded -= OnUnloaded;
            }

            // Disposing the player unsubscribes it from the clock, which is all that is needed —
            // the clock belongs to the window, not to this animation.
            Player?.Dispose();
            Player = null;
            clock = null;
        }
    }
}

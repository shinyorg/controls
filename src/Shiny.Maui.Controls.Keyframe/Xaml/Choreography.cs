namespace Shiny.Maui.Controls.Keyframe;

/// <summary>How a <see cref="Choreography"/> places its children in time.</summary>
public enum ChoreographyMode
{
    /// <summary>Children start together, each <see cref="Choreography.Stagger"/> after the one before.</summary>
    Parallel,

    /// <summary>Each child starts when the one before it finishes, plus <see cref="Choreography.Stagger"/>.</summary>
    Sequence
}

/// <summary>What a <see cref="Choreography"/>'s children animate.</summary>
public enum ChoreographyTarget
{
    /// <summary>Each child animates the element the choreography is attached to (or its own
    /// <c>TargetName</c>).</summary>
    Self,

    /// <summary>Each child is a template applied to every visual child of the element the
    /// choreography is attached to, staggered in order — the staggered list or grid entrance.</summary>
    Children
}

/// <summary>
/// Composes animations on one clock — a storyboard in XAML. Children are <see cref="Keyframes"/> or
/// nested choreographies, run in parallel or in sequence with an optional stagger, and the whole
/// thing plays, pauses, seeks and reverses as one.
/// </summary>
/// <example>
/// <code language="xml">
/// &lt;!-- Every card in the stack fades up, 60 ms apart. --&gt;
/// &lt;VerticalStackLayout&gt;
///   &lt;kf:Animate.Choreography&gt;
///     &lt;kf:Choreography ApplyTo="Children" Stagger="0:0:0.06"&gt;
///       &lt;kf:Keyframes Duration="0:0:0.4" Fill="Backwards"&gt;
///         &lt;kf:Track Property="Opacity"&gt;&lt;kf:Key Offset="0" Value="0" /&gt;&lt;kf:Key Offset="1" Value="1" /&gt;&lt;/kf:Track&gt;
///         &lt;kf:Track Property="TranslationY"&gt;&lt;kf:Key Offset="0" Value="24" Easing="CubicOut" /&gt;&lt;kf:Key Offset="1" Value="0" /&gt;&lt;/kf:Track&gt;
///       &lt;/kf:Keyframes&gt;
///     &lt;/kf:Choreography&gt;
///   &lt;/kf:Animate.Choreography&gt;
///   ...
/// &lt;/VerticalStackLayout&gt;
/// </code>
/// </example>
/// <remarks>Not called <c>Storyboard</c> because the engine's C# composition type already has that name
/// in the same XAML namespace; a <c>Choreography</c> builds one.</remarks>
[ContentProperty(nameof(Children))]
public sealed class Choreography : BindableObject, IAnimationDefinition
{
    /// <summary>Backing store for <see cref="Mode"/>.</summary>
    public static readonly BindableProperty ModeProperty =
        BindableProperty.Create(nameof(Mode), typeof(ChoreographyMode), typeof(Choreography), ChoreographyMode.Parallel);

    /// <summary>Backing store for <see cref="Stagger"/>.</summary>
    public static readonly BindableProperty StaggerProperty =
        BindableProperty.Create(nameof(Stagger), typeof(TimeSpan), typeof(Choreography), TimeSpan.Zero);

    /// <summary>Backing store for <see cref="ApplyTo"/>.</summary>
    public static readonly BindableProperty ApplyToProperty =
        BindableProperty.Create(nameof(ApplyTo), typeof(ChoreographyTarget), typeof(Choreography), ChoreographyTarget.Self);

    /// <summary>Backing store for <see cref="AutoPlay"/>.</summary>
    public static readonly BindableProperty AutoPlayProperty =
        BindableProperty.Create(nameof(AutoPlay), typeof(bool), typeof(Choreography), true);

    /// <summary>Backing store for <see cref="Speed"/>.</summary>
    public static readonly BindableProperty SpeedProperty =
        BindableProperty.Create(nameof(Speed), typeof(double), typeof(Choreography), 1d);

    /// <summary>Backing store for <see cref="ReducedMotion"/>.</summary>
    public static readonly BindableProperty ReducedMotionProperty =
        BindableProperty.Create(nameof(ReducedMotion), typeof(ReducedMotionBehavior), typeof(Choreography), ReducedMotionBehavior.Auto);

    /// <summary>Backing store for <see cref="TargetName"/>.</summary>
    public static readonly BindableProperty TargetNameProperty =
        BindableProperty.Create(nameof(TargetName), typeof(string), typeof(Choreography));

    /// <summary>Parallel (the default) or one after another.</summary>
    public ChoreographyMode Mode
    {
        get => (ChoreographyMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    /// <summary>Added between consecutive starts in parallel mode; the gap between children in sequence mode.</summary>
    public TimeSpan Stagger
    {
        get => (TimeSpan)GetValue(StaggerProperty);
        set => SetValue(StaggerProperty, value);
    }

    /// <summary>Whether children animate the target itself or are templates applied to each of its children.</summary>
    public ChoreographyTarget ApplyTo
    {
        get => (ChoreographyTarget)GetValue(ApplyToProperty);
        set => SetValue(ApplyToProperty, value);
    }

    /// <inheritdoc />
    public bool AutoPlay
    {
        get => (bool)GetValue(AutoPlayProperty);
        set => SetValue(AutoPlayProperty, value);
    }

    /// <inheritdoc />
    public double Speed
    {
        get => (double)GetValue(SpeedProperty);
        set => SetValue(SpeedProperty, value);
    }

    /// <inheritdoc />
    public ReducedMotionBehavior ReducedMotion
    {
        get => (ReducedMotionBehavior)GetValue(ReducedMotionProperty);
        set => SetValue(ReducedMotionProperty, value);
    }

    /// <summary>Optional <c>x:Name</c> of the element to animate instead of the one this is attached to.</summary>
    public string? TargetName
    {
        get => (string?)GetValue(TargetNameProperty);
        set => SetValue(TargetNameProperty, value);
    }

    /// <summary>The composed animations.</summary>
    public IList<IAnimationDefinition> Children { get; } = [];

    /// <inheritdoc />
    public IAnimationNode Build(VisualElement target, Func<string, VisualElement?>? resolveTarget = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (Children.Count == 0)
            throw new InvalidOperationException("A Choreography needs at least one child animation.");

        var root = target;
        if (!string.IsNullOrWhiteSpace(TargetName))
        {
            root = resolveTarget?.Invoke(TargetName)
                ?? throw new InvalidOperationException($"Could not find an element named '{TargetName}'.");
        }

        var storyboard = new Storyboard();
        var targets = ApplyTo == ChoreographyTarget.Children
            ? ((IVisualTreeElement)root).GetVisualChildren().OfType<VisualElement>().ToList()
            : [root];

        var first = true;
        for (var e = 0; e < targets.Count; e++)
        {
            for (var c = 0; c < Children.Count; c++)
            {
                var node = Children[c].Build(targets[e], resolveTarget);

                if (Mode == ChoreographyMode.Sequence)
                {
                    if (first)
                        storyboard.Add(node);
                    else
                        storyboard.Then(node, Stagger);
                }
                else
                {
                    // Stagger walks the elements when the children are templates, and the children
                    // themselves otherwise; templates for one element start together.
                    var step = ApplyTo == ChoreographyTarget.Children ? e : c;
                    storyboard.Add(node, Stagger * step);
                }

                first = false;
            }
        }

        return storyboard;
    }
}

using System.Globalization;
using Microsoft.AspNetCore.Components;
using global::Shiny.Controls.Keyframe;

namespace Shiny.Blazor.Controls.Keyframe;

/// <summary>
/// Animated visual-state transitions: named states, each a look, with transitions between them and
/// animations that run while a state is active.
/// </summary>
/// <remarks>
/// <para>States change by setting <see cref="State"/>. With no <see cref="VisualTransition"/> at all a
/// change snaps. Otherwise the most specific match wins - From and To both named, then To, then From,
/// then neither (<c>*</c> or left out) - and a transition without a Duration runs 150ms.</para>
/// <para>The first render never animates: the element starts in its state.</para>
/// </remarks>
public partial class VisualStates
{
    /// <summary>The default length of a transition that does not set one.</summary>
    public static readonly TimeSpan DefaultTransitionDuration = TimeSpan.FromMilliseconds(150);

    /// <summary>The current state's name. Null or a name with no VisualState is the plain look.</summary>
    [Parameter] public string? State { get; set; }

    /// <summary>How transitions respond to <c>prefers-reduced-motion</c>. Auto snaps when it is on.</summary>
    [Parameter] public ReducedMotionBehavior ReducedMotion { get; set; } = ReducedMotionBehavior.Auto;

    /// <summary>The states, transitions, state animations and the content.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public string? Class { get; set; }
    [Parameter] public string? Style { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>The element the states style.</summary>
    public ElementReference Element { get; private set; }

    readonly List<VisualState> states = [];
    readonly List<VisualTransition> transitions = [];

    string? shownState;
    string? transitionCss;
    bool firstRendered;
    bool registrationsChanged;

    internal void Register(VisualState state)
    {
        this.states.Add(state);
        this.registrationsChanged = true;
    }

    internal void Unregister(VisualState state) => this.states.Remove(state);

    internal void Register(VisualTransition transition) => this.transitions.Add(transition);
    internal void Unregister(VisualTransition transition) => this.transitions.Remove(transition);

    /// <summary>A VisualState's parameters changed; re-render with its new look.</summary>
    internal void StateDefinitionChanged()
    {
        if (this.firstRendered)
            this.StateHasChanged();
    }

    VisualState? Find(string? name) => name is null
        ? null
        : this.states.LastOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    protected override void OnParametersSet()
    {
        if (string.Equals(this.State, this.shownState, StringComparison.OrdinalIgnoreCase))
            return;

        var from = this.shownState;
        this.shownState = this.State;

        // The element starts in its state; only a change after it is on screen animates.
        this.transitionCss = this.firstRendered ? this.TransitionFor(from, this.State) : null;
    }

    protected override void OnAfterRender(bool firstRender)
    {
        this.firstRendered = true;

        // States register while the first render runs, after this element's style was written.
        if (firstRender && this.registrationsChanged)
        {
            this.registrationsChanged = false;
            this.StateHasChanged();
        }
    }

    string? TransitionFor(string? from, string? to)
    {
        if (this.ReducedMotion == ReducedMotionBehavior.Instant)
            return null;

        var match = Match(this.transitions, from, to);
        if (match is null)
            return null;

        var properties = TransitionProperties(this.Find(from), this.Find(to));
        if (properties.Count == 0)
            return null;

        var duration = match.Duration ?? DefaultTransitionDuration;
        var easing = EasingSyntax.ToCss(match.Easing ?? "EaseOut");
        var delay = match.Delay;

        return string.Join(", ", properties.Select(p =>
            string.Create(CultureInfo.InvariantCulture, $"{p} {duration.TotalMilliseconds}ms {easing} {delay.TotalMilliseconds}ms")));
    }

    /// <summary>The most specific transition for a change: From and To named, then To, then From, then neither.</summary>
    internal static VisualTransition? Match(IReadOnlyList<VisualTransition> transitions, string? from, string? to)
        => transitions
            .Select((t, index) => (Transition: t, Index: index, Score: Score(t, from, to)))
            .Where(c => c.Score >= 0)
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Index)
            .Select(c => c.Transition)
            .FirstOrDefault();

    /// <summary>-1 when it does not apply; otherwise 2 for a named To plus 1 for a named From.</summary>
    internal static int Score(VisualTransition transition, string? from, string? to)
    {
        var fromAny = IsWildcard(transition.From);
        var toAny = IsWildcard(transition.To);

        if (!fromAny && !string.Equals(transition.From, from, StringComparison.OrdinalIgnoreCase))
            return -1;
        if (!toAny && !string.Equals(transition.To, to, StringComparison.OrdinalIgnoreCase))
            return -1;

        return (toAny ? 0 : 2) + (fromAny ? 0 : 1);
    }

    static bool IsWildcard(string? name) => string.IsNullOrWhiteSpace(name) || name.Trim() == "*";

    /// <summary>The CSS properties either state sets; "all" when a state is a class, whose properties cannot be known.</summary>
    internal static List<string> TransitionProperties(VisualState? from, VisualState? to)
    {
        if (!string.IsNullOrWhiteSpace(from?.Class) || !string.IsNullOrWhiteSpace(to?.Class))
            return ["all"];

        return PropertyNames(from?.Style)
            .Concat(PropertyNames(to?.Style))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>"scale: .96; opacity: .8" → scale, opacity.</summary>
    internal static IEnumerable<string> PropertyNames(string? style)
        => (style ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Split(':', 2)[0].Trim().ToLowerInvariant())
            .Where(n => n.Length > 0);

    string CssClass
    {
        get
        {
            var current = this.Find(this.shownState);
            var classes = new List<string> { "shiny-visual-states" };
            if (this.ReducedMotion == ReducedMotionBehavior.Auto)
                classes.Add("shiny-motion-auto");
            if (!string.IsNullOrWhiteSpace(this.Class))
                classes.Add(this.Class!);
            if (!string.IsNullOrWhiteSpace(current?.Class))
                classes.Add(current!.Class!);
            return string.Join(' ', classes);
        }
    }

    string? CssStyle
    {
        get
        {
            var parts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(this.Style))
                parts.Add(this.Style!.Trim().TrimEnd(';'));
            parts.Add("transition: " + (this.transitionCss ?? "none"));
            var current = this.Find(this.shownState)?.Style;
            if (!string.IsNullOrWhiteSpace(current))
                parts.Add(current!.Trim().TrimEnd(';'));
            return string.Join("; ", parts);
        }
    }
}

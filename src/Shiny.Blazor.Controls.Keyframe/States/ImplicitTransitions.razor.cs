using System.Globalization;
using Microsoft.AspNetCore.Components;
using global::Shiny.Controls.Keyframe;
using Shiny.Blazor.Controls.Keyframe.Compilation;

namespace Shiny.Blazor.Controls.Keyframe;

/// <summary>
/// Animates every change of the listed properties, on the wrapper itself (change its
/// <see cref="Style"/> or <see cref="Class"/>) or on each direct child.
/// </summary>
public partial class ImplicitTransitions
{
    /// <summary>The wrapper itself (the default) or each of its direct children.</summary>
    [Parameter] public ImplicitTransitionTarget ApplyTo { get; set; } = ImplicitTransitionTarget.Self;

    /// <summary>How changes respond to <c>prefers-reduced-motion</c>. Auto applies them at once when it is on.</summary>
    [Parameter] public ReducedMotionBehavior ReducedMotion { get; set; } = ReducedMotionBehavior.Auto;

    /// <summary>The PropertyTransition children and the content.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public string? Class { get; set; }
    [Parameter] public string? Style { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    readonly List<PropertyTransition> transitions = [];
    bool rendered;

    internal void Register(PropertyTransition transition)
    {
        this.transitions.Add(transition);
        if (this.rendered)
            this.StateHasChanged();
    }

    internal void Unregister(PropertyTransition transition) => this.transitions.Remove(transition);

    internal void Changed()
    {
        if (this.rendered)
            this.StateHasChanged();
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (!firstRender)
            return;

        // The PropertyTransition children registered while this element was being written.
        this.rendered = true;
        if (this.transitions.Count > 0)
            this.StateHasChanged();
    }

    /// <summary>The CSS <c>transition</c> value for a set of property transitions.</summary>
    internal static string TransitionCss(IEnumerable<PropertyTransition> transitions)
    {
        var parts = transitions
            .Where(t => !string.IsNullOrWhiteSpace(t.Property))
            .SelectMany(t => CssProperties.TransitionTargets(t.Property).Select(css => (Css: css, Transition: t)))
            .Select(p => string.Create(CultureInfo.InvariantCulture,
                $"{p.Css} {p.Transition.Duration.TotalMilliseconds}ms {EasingSyntax.ToCss(p.Transition.Easing ?? "Ease")} {p.Transition.Delay.TotalMilliseconds}ms"))
            .ToList();

        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }

    string Transition => this.ReducedMotion == ReducedMotionBehavior.Instant ? "none" : TransitionCss(this.transitions);

    string CssClass
    {
        get
        {
            var classes = new List<string> { "shiny-implicit-transitions" };
            if (this.ApplyTo == ImplicitTransitionTarget.Children)
                classes.Add("shiny-implicit-children");
            if (this.ReducedMotion == ReducedMotionBehavior.Auto)
                classes.Add("shiny-motion-auto");
            if (!string.IsNullOrWhiteSpace(this.Class))
                classes.Add(this.Class!);
            return string.Join(' ', classes);
        }
    }

    string CssStyle
    {
        get
        {
            var own = this.ApplyTo == ImplicitTransitionTarget.Children
                ? "--shiny-implicit-transition: " + this.Transition
                : "transition: " + this.Transition;

            return string.IsNullOrWhiteSpace(this.Style) ? own : own + "; " + this.Style!.Trim();
        }
    }
}


/// <summary>One property an <see cref="ImplicitTransitions"/> animates: a MAUI name (Opacity, Scale, TranslationX, ...), a CSS property, or All.</summary>
public sealed class PropertyTransition : ComponentBase, IDisposable
{
    [CascadingParameter] ImplicitTransitions? Owner { get; set; }

    /// <summary>The property. Transform parts (Scale, Translation*, Rotation) cover both the individual CSS property and <c>transform</c>.</summary>
    [Parameter, EditorRequired] public string Property { get; set; } = "";

    /// <summary>How long. Defaults to 250ms.</summary>
    [Parameter] public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>The curve, as curve text. Defaults to Ease.</summary>
    [Parameter] public string? Easing { get; set; }

    /// <summary>Wait this long before starting.</summary>
    [Parameter] public TimeSpan Delay { get; set; }

    string? signature;

    protected override void OnInitialized()
    {
        this.signature = this.Signature();
        this.Owner?.Register(this);
    }

    protected override void OnParametersSet()
    {
        var current = this.Signature();
        if (current == this.signature)
            return;

        this.signature = current;
        this.Owner?.Changed();
    }

    string Signature() => $"{this.Property}|{this.Duration.Ticks}|{this.Easing}|{this.Delay.Ticks}";

    public void Dispose() => this.Owner?.Unregister(this);
}

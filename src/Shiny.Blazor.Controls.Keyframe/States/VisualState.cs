using Microsoft.AspNetCore.Components;

namespace Shiny.Blazor.Controls.Keyframe;

/// <summary>A named look for a <see cref="VisualStates"/>: CSS declarations, a class, or both.</summary>
public sealed class VisualState : ComponentBase, IDisposable
{
    [CascadingParameter] VisualStates? Owner { get; set; }

    /// <summary>The state's name, matched case-insensitively against <see cref="VisualStates.State"/>.</summary>
    [Parameter, EditorRequired] public string Name { get; set; } = "";

    /// <summary>CSS declarations applied in this state, e.g. <c>scale: .96; opacity: .8</c>.</summary>
    [Parameter] public string? Style { get; set; }

    /// <summary>A class added in this state. Transitions to and from a class state cover <c>all</c> properties.</summary>
    [Parameter] public string? Class { get; set; }

    string? lastStyle;
    string? lastClass;

    protected override void OnInitialized()
    {
        this.lastStyle = this.Style;
        this.lastClass = this.Class;
        this.Owner?.Register(this);
    }

    protected override void OnParametersSet()
    {
        if (this.lastStyle == this.Style && this.lastClass == this.Class)
            return;

        this.lastStyle = this.Style;
        this.lastClass = this.Class;
        this.Owner?.StateDefinitionChanged();
    }

    public void Dispose() => this.Owner?.Unregister(this);
}

/// <summary>How a <see cref="VisualStates"/> moves between two states. From and To are state names, or <c>*</c> / left out for any.</summary>
public sealed class VisualTransition : ComponentBase, IDisposable
{
    [CascadingParameter] VisualStates? Owner { get; set; }

    /// <summary>The state being left, or <c>*</c> / null for any.</summary>
    [Parameter] public string? From { get; set; }

    /// <summary>The state being entered, or <c>*</c> / null for any.</summary>
    [Parameter] public string? To { get; set; }

    /// <summary>How long. Defaults to 150ms.</summary>
    [Parameter] public TimeSpan? Duration { get; set; }

    /// <summary>The curve, as curve text (named, cubic-bezier(), steps(), spring(), bouncy()). Defaults to EaseOut.</summary>
    [Parameter] public string? Easing { get; set; }

    /// <summary>Wait this long before starting.</summary>
    [Parameter] public TimeSpan Delay { get; set; }

    protected override void OnInitialized() => this.Owner?.Register(this);

    public void Dispose() => this.Owner?.Unregister(this);
}

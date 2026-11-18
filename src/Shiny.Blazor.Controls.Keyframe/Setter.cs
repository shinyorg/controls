using Microsoft.AspNetCore.Components;

namespace Shiny.Blazor.Controls.Keyframe;

/// <summary>One property's value at a <see cref="KeyFrame"/>'s cue.</summary>
public sealed class Setter : ComponentBase, IDisposable
{
    [CascadingParameter] KeyFrame? Frame { get; set; }

    /// <summary>The property: a MAUI name or any CSS property, as for <see cref="Track.Property"/>.</summary>
    [Parameter, EditorRequired] public string Property { get; set; } = "";

    /// <summary>The value at the cue, as text. Leave it out for the live value when playback starts.</summary>
    [Parameter] public string? Value { get; set; }

    protected override void OnInitialized() => this.Frame?.AddSetter(this);

    public void Dispose() => this.Frame?.RemoveSetter(this);
}

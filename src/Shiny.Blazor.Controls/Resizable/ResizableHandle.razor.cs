using Microsoft.AspNetCore.Components;

namespace Shiny.Blazor.Controls;

/// <summary>
/// The divider between two panels of a <see cref="ResizablePanelGroup"/>. Drag it, or focus it and use
/// the arrow keys, Home, End and Enter (collapse) to move space from one side to the other.
/// </summary>
/// <remarks>
/// The visible line is one pixel; the pointer target is wider, laid over the panels either side, so it
/// is easy to grab without the layout giving up a gutter.
/// </remarks>
public partial class ResizableHandle : IDisposable
{
    [CascadingParameter] ResizablePanelGroup? Group { get; set; }

    /// <summary>Shows a grip in the middle of the line. The handle drags either way; this is only the cue.</summary>
    [Parameter] public bool WithHandle { get; set; }

    /// <summary>Stops the handle from moving, by pointer or keyboard, and takes it out of the tab order.</summary>
    [Parameter] public bool Disabled { get; set; }

    [Parameter] public string AriaLabel { get; set; } = "Resize";

    [Parameter] public string? Class { get; set; }

    [Parameter] public string? Style { get; set; }

    internal int Sequence { get; private set; }

    internal void Refresh() => this.StateHasChanged();

    protected override void OnInitialized()
    {
        if (this.Group is null)
            throw new InvalidOperationException($"{nameof(ResizableHandle)} must be placed inside a {nameof(ResizablePanelGroup)}.");

        this.Sequence = this.Group.Register(this);
    }

    public void Dispose() => this.Group?.Unregister(this);
}

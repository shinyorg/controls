using Microsoft.AspNetCore.Components;
using Shiny.Controls.AgentUI.A2UI;

namespace Shiny.Blazor.Controls.Agents.Renderers;

/// <summary>
/// Base for renderer components: the context parameter, plus what every input needs - a stable id
/// for label and error wiring, and a "touched" flag so a check failure shows once the user has been in
/// the field rather than on an empty form that hasn't been filled in yet.
/// </summary>
public abstract class A2UIRendererBase : ComponentBase
{
    static int nextId;

    /// <summary>The component to draw and where to evaluate it.</summary>
    [Parameter, EditorRequired] public A2UIRenderContext Context { get; set; } = default!;

    /// <summary>A unique id for this renderer's elements.</summary>
    protected string ElementId { get; } = "a2ui-" + Interlocked.Increment(ref nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>True once the user has edited or left the input.</summary>
    protected bool Touched { get; set; }

    /// <summary>The component's checks; the message to show is only returned once <see cref="Touched"/>.</summary>
    protected (A2UICheckResult Result, string? Visible) RunChecks()
    {
        var result = Context.Checks();
        return (result, Touched && !result.IsValid ? result.FirstMessage ?? "Invalid value" : null);
    }

    /// <summary>Marks the input touched (on blur).</summary>
    protected void Touch() => Touched = true;
}

using System.Text;
using Microsoft.AspNetCore.Components;
using Shiny.Blazor.Controls.Keyframe.Compilation;

namespace Shiny.Blazor.Controls.Keyframe;

/// <summary>
/// A page transition defined with Keyframes and generated as <c>::view-transition-old/new</c> CSS,
/// registered by <see cref="Name"/> with the <see cref="PageTransitionHost"/> around it.
/// </summary>
/// <remarks>
/// Enter animates the incoming page and Exit the outgoing one going forward; PopEnter and PopExit do
/// the same going back, and when they are left out Exit and Enter run backwards, so a push gives a
/// matching pop. Each part's tracks and curves are used; its length is the host's (or the page's)
/// Duration, as for the built-in transitions. Translations in % are of the page.
/// </remarks>
public partial class CustomPageTransition
{
    [CascadingParameter] PageTransitionHost? Host { get; set; }

    /// <summary>The name pages and navigations use to pick it.</summary>
    [Parameter, EditorRequired] public string Name { get; set; } = "";

    /// <summary>The incoming page, going forward.</summary>
    [Parameter] public RenderFragment? Enter { get; set; }

    /// <summary>The outgoing page, going forward.</summary>
    [Parameter] public RenderFragment? Exit { get; set; }

    /// <summary>The page coming back into view, going back. Left out, Exit runs backwards.</summary>
    [Parameter] public RenderFragment? PopEnter { get; set; }

    /// <summary>The page being left, going back. Left out, Enter runs backwards.</summary>
    [Parameter] public RenderFragment? PopExit { get; set; }

    readonly KeyframeTemplateSlot enter = new();
    readonly KeyframeTemplateSlot exit = new();
    readonly KeyframeTemplateSlot popEnter = new();
    readonly KeyframeTemplateSlot popExit = new();

    string? registeredName;
    string? registeredCss;

    protected override void OnAfterRender(bool firstRender)
    {
        // The parts' Keyframes register while this renders; build from them once they have.
        if (this.Host is null || string.IsNullOrWhiteSpace(this.Name))
            return;

        var definition = Build(this.Name, this.enter.Template, this.exit.Template, this.popEnter.Template, this.popExit.Template);
        var css = string.Concat(definition.Enter?.Css, definition.Exit?.Css, definition.PopEnter?.Css, definition.PopExit?.Css);
        if (css == this.registeredCss && this.Name == this.registeredName)
            return;

        if (this.registeredName is not null && this.registeredName != this.Name)
            this.Host.UnregisterCustomTransition(this.registeredName);

        this.Host.RegisterCustomTransition(definition);
        this.registeredName = this.Name;
        this.registeredCss = css;
    }

    internal static CustomPageTransitionDefinition Build(string name, Keyframes? enter, Keyframes? exit, Keyframes? popEnter, Keyframes? popExit)
        => new(name, Part(name, "enter", enter), Part(name, "exit", exit), Part(name, "pop-enter", popEnter), Part(name, "pop-exit", popExit));

    /// <summary>One part as <c>@keyframes</c> rules, one per animated CSS property, run together.</summary>
    internal static PageTransitionKeyframes? Part(string name, string role, Keyframes? template)
        => template is null ? null : Part(name, role, template.Spec());

    internal static PageTransitionKeyframes? Part(string name, string role, AnimationSpec spec)
    {
        var effects = KeyframeCompiler.CompileForCss(spec);
        if (effects.Count == 0)
            return null;

        var safe = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());
        var names = new List<string>(effects.Count);
        var css = new StringBuilder();

        for (var i = 0; i < effects.Count; i++)
        {
            var keyframesName = $"shiny-vt-c-{safe}-{role}-{i}";
            names.Add(keyframesName);
            css.AppendLine(ProgramWriter.ToCssKeyframes(keyframesName, effects[i]));
        }

        return new PageTransitionKeyframes(names, css.ToString());
    }

    public void Dispose()
    {
        if (this.registeredName is not null)
            this.Host?.UnregisterCustomTransition(this.registeredName);
    }
}

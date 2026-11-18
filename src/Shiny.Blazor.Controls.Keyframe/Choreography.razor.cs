using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Shiny.Blazor.Controls.Keyframe.Compilation;

namespace Shiny.Blazor.Controls.Keyframe;

/// <summary>
/// Composes Keyframes (and nested Choreographies) into one animation with one player: in parallel or
/// in sequence, with a stagger, or as a template applied to every child element.
/// </summary>
/// <remarks>
/// The storyboard. It is not called Storyboard because that is the shared engine's type, and the MAUI
/// package uses the same name for the same thing. Children inside it do not play on their own -
/// drive the Choreography (AutoPlay, Playing, the player methods) and they play as one, seek as one
/// and reverse as one.
/// </remarks>
public partial class Choreography : IKeyframeContainer, IKeyframeNode, IDisposable
{
    [CascadingParameter] IKeyframeContainer? Container { get; set; }

    /// <summary>Parallel (the default) or Sequence.</summary>
    [Parameter] public ChoreographyMode Mode { get; set; } = ChoreographyMode.Parallel;

    /// <summary>Added per child: each starts this much after the one before (Parallel), or waits this long after it ends (Sequence).</summary>
    [Parameter] public TimeSpan Stagger { get; set; }

    /// <summary>Self (the default) composes the children; Children applies them as a template to each child element.</summary>
    [Parameter] public ChoreographyApplyTo ApplyTo { get; set; } = ChoreographyApplyTo.Self;

    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter] public string? Class { get; set; }
    [Parameter] public string? Style { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    ElementReference element;
    readonly List<IKeyframeNode> nodes = [];

    string CssClass => string.IsNullOrWhiteSpace(this.Class) ? "shiny-choreography" : "shiny-choreography " + this.Class;

    bool IKeyframeContainer.AsTemplates => this.ApplyTo == ChoreographyApplyTo.Children;

    void IKeyframeContainer.Register(IKeyframeNode node) => this.nodes.Add(node);
    void IKeyframeContainer.Unregister(IKeyframeNode node) => this.nodes.Remove(node);

    private protected override bool IsDriver => this.Container is null;

    protected override void OnInitialized() => this.Container?.Register(this);

    Task<NodeProgram> IKeyframeNode.BuildAsync(IJSObjectReference module, ElementCollector elements) => this.BuildProgramAsync(module, elements);

    string IKeyframeNode.Signature() => this.BuildSignature();

    private protected override Task<NodeProgram> BuildProgramAsync(IJSObjectReference module, ElementCollector elements)
        => this.ApplyTo == ChoreographyApplyTo.Children
            ? this.BuildForChildrenAsync(module, elements)
            : this.BuildComposedAsync(module, elements);

    async Task<NodeProgram> BuildComposedAsync(IJSObjectReference module, ElementCollector elements)
    {
        var programs = new List<NodeProgram>(this.nodes.Count);
        foreach (var node in this.nodes.ToList())
            programs.Add(await node.BuildAsync(module, elements));

        return new NodeProgram(Place(programs, this.Mode, this.Stagger.TotalMilliseconds));
    }

    async Task<NodeProgram> BuildForChildrenAsync(IJSObjectReference module, ElementCollector elements)
    {
        var templates = this.nodes.OfType<Keyframes>().Select(k => k.Spec()).ToList();
        if (templates.Count == 0)
            return new NodeProgram([]);

        var root = elements.Add(this.element);
        var needs = templates.SelectMany(t => KeyframeCompiler.LiveNeeds(t.Tracks)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        int count;
        string?[][] rows;
        try
        {
            count = await module.InvokeAsync<int>("childCount", this.element);
            rows = needs.Length > 0
                ? await module.InvokeAsync<string?[][]>("readLiveChildren", this.element, needs)
                : [];
        }
        catch (JSException)
        {
            return new NodeProgram([]);
        }

        var perChild = new List<NodeProgram>(count);
        for (var i = 0; i < count; i++)
        {
            var live = Keyframes.ToLive(needs, i < rows.Length ? rows[i] : null);
            var target = new TargetSpec(root, null, i);
            perChild.Add(new NodeProgram(templates.Select(t => new ProgramItem(target, KeyframeCompiler.Compile(t, live), 0d)).ToList()));
        }

        return new NodeProgram(Place(perChild, this.Mode, this.Stagger.TotalMilliseconds));
    }

    /// <summary>
    /// Places each child's animations in time: Parallel starts child i at i × stagger; Sequence
    /// starts each when the previous ends, plus the stagger as a gap. Anything after a child that
    /// loops forever would never start, so it is left out.
    /// </summary>
    internal static List<ProgramItem> Place(IReadOnlyList<NodeProgram> children, ChoreographyMode mode, double staggerMs)
    {
        var items = new List<ProgramItem>();
        var cursor = 0d;

        for (var i = 0; i < children.Count && double.IsFinite(cursor); i++)
        {
            var start = mode == ChoreographyMode.Parallel ? i * staggerMs : cursor;
            items.AddRange(children[i].Items.Select(item => item with { OffsetMs = item.OffsetMs + start }));

            if (mode == ChoreographyMode.Sequence)
                cursor = start + children[i].EndMs + staggerMs;
        }

        return items;
    }

    private protected override string BuildSignature()
    {
        var builder = new StringBuilder();
        builder.Append(this.Mode).Append('|').Append(this.Stagger.Ticks).Append('|').Append(this.ApplyTo).Append('|').Append(this.ReducedMotion);
        foreach (var node in this.nodes)
            builder.Append('{').Append(node.Signature()).Append('}');
        return builder.ToString();
    }

    public void Dispose() => this.Container?.Unregister(this);

    public override ValueTask DisposeAsync()
    {
        this.Dispose();
        return base.DisposeAsync();
    }
}

using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using global::Shiny.Controls.Keyframe;
using Shiny.Blazor.Controls.Keyframe.Compilation;

namespace Shiny.Blazor.Controls.Keyframe;

/// <summary>Who owns <see cref="Track"/> and <see cref="KeyFrame"/> children.</summary>
internal interface ITrackOwner
{
    void AddTrack(Track track);
    void RemoveTrack(Track track);
    void AddFrame(KeyFrame frame);
    void RemoveFrame(KeyFrame frame);
}


/// <summary>
/// A keyframe animation on one element, run on the browser's Web Animations API: the same
/// <c>Keyframes</c> / <c>Track</c> / <c>Key</c> model, property names and timing as the MAUI package.
/// </summary>
/// <remarks>
/// <para>Property names are MAUI's, mapped to CSS: Opacity; Scale, ScaleX, ScaleY; TranslationX,
/// TranslationY; Rotation (shortest arc), Spin, RotationX, RotationY; AnchorX, AnchorY;
/// BackgroundColor; WidthRequest, HeightRequest; Margin, Padding. Any other name is a CSS property
/// passed straight through (<c>filter</c>, <c>border-radius</c>, <c>--my-var</c>).</para>
/// <para>A <see cref="Key"/> with no value starts from the element's live value, so replaying mid-flight
/// continues rather than snapping. Easing takes the same text as the MAUI package - named curves,
/// <c>cubic-bezier()</c>, <c>steps()</c>, <c>spring()</c>, <c>bouncy()</c> - and a curve CSS has no
/// name for is sampled into <c>linear()</c>, so springs play the same curve on both hosts.</para>
/// </remarks>
public partial class Keyframes : ITrackOwner, IKeyframeNode, IDisposable
{
    /// <summary>For <see cref="Iterations"/>: loop forever.</summary>
    public const double Infinite = double.PositiveInfinity;

    [CascadingParameter] IKeyframeContainer? Container { get; set; }
    [CascadingParameter] StateAnimationContext? StateContext { get; set; }

    /// <summary>One iteration. Defaults to 300ms.</summary>
    [Parameter] public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Before the first iteration. Negative starts partway in, the CSS trick for staggered entrances.</summary>
    [Parameter] public TimeSpan Delay { get; set; }

    /// <summary>After the last iteration, before it reports finished.</summary>
    [Parameter] public TimeSpan EndDelay { get; set; }

    /// <summary>A pause between iterations, holding the pose the last one ended on. The Web Animations API has none; it is baked into the keyframes.</summary>
    [Parameter] public TimeSpan IterationDelay { get; set; }

    /// <summary>How many times to run. Fractions allowed; <see cref="Infinite"/> loops forever.</summary>
    [Parameter] public double Iterations { get; set; } = 1d;

    /// <summary>Where in the first iteration to start, in iterations (0.5 = halfway).</summary>
    [Parameter] public double IterationStart { get; set; }

    /// <summary>Normal, Reverse, Alternate or AlternateReverse.</summary>
    [Parameter] public PlaybackDirection Direction { get; set; } = PlaybackDirection.Normal;

    /// <summary>None, Forwards, Backwards or Both - CSS <c>animation-fill-mode</c>.</summary>
    [Parameter] public FillMode Fill { get; set; } = FillMode.None;

    /// <summary>Easing across each whole iteration, on top of each key's own curve. Curve text, as on MAUI.</summary>
    [Parameter] public string? Easing { get; set; }

    /// <summary>Animate this element instead of rendering a wrapper.</summary>
    [Parameter] public ElementReference? Target { get; set; }

    /// <summary>Animate the element with this id instead of rendering a wrapper.</summary>
    [Parameter] public string? TargetId { get; set; }

    /// <summary>The tracks (or KeyFrames) and the content they animate.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public string? Class { get; set; }
    [Parameter] public string? Style { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    ElementReference element;
    readonly List<Track> tracks = [];
    readonly List<KeyFrame> frames = [];

    bool IsTemplate => this.Container?.AsTemplates == true;

    bool RendersWrapper => !this.IsTemplate && this.Target is null && this.TargetId is null && this.StateContext is null;

    string CssClass => string.IsNullOrWhiteSpace(this.Class) ? "shiny-keyframes" : "shiny-keyframes " + this.Class;

    private protected override bool IsDriver => this.Container is null;

    private protected override bool? EffectivePlaying => this.StateContext is { } context ? context.Playing : this.Playing;

    private protected override bool EffectiveRestoreOnStop => this.StateContext is not null || this.RestoreOnStop;

    protected override void OnInitialized() => this.Container?.Register(this);

    void ITrackOwner.AddTrack(Track track) => this.tracks.Add(track);
    void ITrackOwner.RemoveTrack(Track track) => this.tracks.Remove(track);
    void ITrackOwner.AddFrame(KeyFrame frame) => this.frames.Add(frame);
    void ITrackOwner.RemoveFrame(KeyFrame frame) => this.frames.Remove(frame);


    /// <summary>The authored timing.</summary>
    internal TimingSpec TimingSpec() => new()
    {
        Duration = this.Duration,
        Delay = this.Delay,
        EndDelay = this.EndDelay,
        IterationDelay = this.IterationDelay,
        Iterations = this.Iterations,
        IterationStart = this.IterationStart,
        Direction = this.Direction,
        Fill = this.Fill,
        Easing = this.Easing
    };

    /// <summary>Everything authored - for a parent applying this as a template to other elements.</summary>
    internal AnimationSpec Spec() => new(this.TimingSpec(), this.AllTracks().Select(t => t.Spec).ToList());

    /// <summary>Tracks, then the spec-style KeyFrames folded into one track per property.</summary>
    internal List<(string? TargetId, TrackSpec Spec)> AllTracks()
    {
        var result = this.tracks.Select(t => (t.TargetId, t.ToSpec())).ToList();

        var fromFrames = this.frames
            .SelectMany(f => f.Setters.Select(s => (s.Property, Key: new KeySpec(f.Offset, s.Value, f.Easing))))
            .Where(s => !string.IsNullOrWhiteSpace(s.Property))
            .GroupBy(s => s.Property, StringComparer.OrdinalIgnoreCase)
            .Select(g => ((string?)null, new TrackSpec(g.Key, g.Select(s => s.Key).ToList())));

        result.AddRange(fromFrames);
        return result;
    }

    Task<NodeProgram> IKeyframeNode.BuildAsync(IJSObjectReference module, ElementCollector elements) => this.BuildProgramAsync(module, elements);

    string IKeyframeNode.Signature() => this.BuildSignature();

    private protected override async Task<NodeProgram> BuildProgramAsync(IJSObjectReference module, ElementCollector elements)
    {
        var timing = this.TimingSpec();
        var items = new List<ProgramItem>();

        foreach (var group in this.AllTracks().GroupBy(t => t.TargetId))
        {
            var target = group.Key is { Length: > 0 } trackTarget
                ? new TargetSpec(-1, trackTarget)
                : this.DefaultTarget(elements);

            var specs = group.Select(g => g.Spec).ToList();
            var live = await ReadLiveAsync(module, elements, target, KeyframeCompiler.LiveNeeds(specs));
            var compiled = KeyframeCompiler.Compile(new AnimationSpec(timing, specs), live);
            items.Add(new ProgramItem(target, compiled, 0d));
        }

        return new NodeProgram(items);
    }

    TargetSpec DefaultTarget(ElementCollector elements)
    {
        if (this.Target is { } target)
            return new TargetSpec(elements.Add(target), null);

        if (!string.IsNullOrWhiteSpace(this.TargetId))
            return new TargetSpec(-1, this.TargetId);

        if (this.StateContext is { } context)
            return new TargetSpec(elements.Add(context.Owner.Element), null);

        return new TargetSpec(elements.Add(this.element), null);
    }

    internal static async Task<IReadOnlyDictionary<string, string>?> ReadLiveAsync(
        IJSObjectReference module, ElementCollector elements, TargetSpec target, IReadOnlyList<string> needs)
    {
        if (needs.Count == 0)
            return null;

        try
        {
            var values = await module.InvokeAsync<string?[]>(
                "readLive", elements.ToArray(), target.ElementIndex, target.Id, target.ChildIndex, needs.ToArray());
            return ToLive(needs, values);
        }
        catch (JSException)
        {
            // No reading: the keys fall back to the property's resting value.
            return null;
        }
    }

    internal static Dictionary<string, string> ToLive(IReadOnlyList<string> needs, IReadOnlyList<string?>? values)
    {
        var live = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; values is not null && i < needs.Count && i < values.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(values[i]))
                live[needs[i]] = values[i]!;
        }
        return live;
    }

    private protected override string BuildSignature()
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"{this.Duration.Ticks}|{this.Delay.Ticks}|{this.EndDelay.Ticks}|{this.IterationDelay.Ticks}|{this.Iterations}|{this.IterationStart}|{this.Direction}|{this.Fill}|{this.Easing}|{this.ReducedMotion}");
        foreach (var (target, spec) in this.AllTracks())
        {
            builder.Append('#').Append(target).Append(':').Append(spec.Property);
            foreach (var key in spec.Keys)
                builder.Append(CultureInfo.InvariantCulture, $";{key.Offset},{key.Value},{key.Easing}");
        }
        return builder.ToString();
    }

    public void Dispose() => this.Container?.Unregister(this);

    public override ValueTask DisposeAsync()
    {
        this.Dispose();
        return base.DisposeAsync();
    }
}

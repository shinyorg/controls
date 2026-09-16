using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Shiny.Blazor.Controls.MotionIcons;
using Shiny.Controls.MotionIcons;

// The component itself sits in the root namespace alongside the other top-level controls; only its
// internals live under MotionIcons, so nobody has to add a using to place one on a page.
namespace Shiny.Blazor.Controls;

/// <summary>
/// An icon that animates — on a loop, on hover, on click, when it scrolls into view, or on command.
/// </summary>
/// <remarks>
/// <para>The artwork and the motion come from <c>Shiny.Controls.MotionIcons</c>, the same package the
/// MAUI <c>MotionIconView</c> draws from, so the two hosts render the same icons running the same
/// curves. What differs is who does the work: here the spec is compiled to <c>@keyframes</c> once
/// and the browser composites it, off the main thread, with no C# running per frame.</para>
/// <para>Because the animation only exists while the playing class is applied, stopping is simply
/// removing it — and the icon reverts to its resting artwork with no state to unwind.</para>
/// </remarks>
public partial class MotionIcon : IAsyncDisposable
{
    IJSObjectReference? module;
    DotNetObjectReference<MotionIcon>? selfRef;
    ElementReference element;

    MotionIconDefinition? icon;
    MotionSpec? spec;
    MotionPlan? plan;

    bool playing;
    bool looping;
    bool pendingStop;
    bool attached;

    // A host that drives Play() from a captured reference can outlive the icon it captured — the
    // slot swapped to a busy indicator, say. Rendering a component the renderer has already removed
    // throws, so playback goes quiet once the icon is gone.
    bool disposed;

    [Inject]
    IJSRuntime JS { get; set; } = null!;

    /// <summary>The name of a built-in icon, looked up in <see cref="MotionIconLibrary"/>.</summary>
    [Parameter] public string? Icon { get; set; }

    /// <summary>Explicit artwork. Takes precedence over <see cref="Icon"/> and <see cref="PathData"/>.</summary>
    [Parameter] public MotionIconDefinition? Source { get; set; }

    /// <summary>Raw SVG path data, drawn in a 24x24 box. The quickest way to animate your own glyph.</summary>
    [Parameter] public string? PathData { get; set; }

    /// <summary>Which motion to play. Defaults to whatever was authored for the icon.</summary>
    [Parameter] public MotionPreset Motion { get; set; } = MotionPreset.Default;

    /// <summary>What starts the animation.</summary>
    [Parameter] public MotionTrigger Trigger { get; set; } = MotionTrigger.Hover | MotionTrigger.Press;

    /// <summary>Overrides the length of one cycle. Zero uses the motion's own duration.</summary>
    [Parameter] public TimeSpan Duration { get; set; }

    /// <summary>How long the icon rests between cycles while looping.</summary>
    [Parameter] public TimeSpan Interval { get; set; }

    /// <summary>Playback rate. Two runs it twice as fast.</summary>
    [Parameter] public double Speed { get; set; } = 1d;

    /// <summary>How many cycles a triggered play runs for. Zero or less repeats forever.</summary>
    [Parameter] public int RepeatCount { get; set; } = 1;

    /// <summary>Width and height in px.</summary>
    [Parameter] public double Size { get; set; } = 24d;

    /// <summary>
    /// The primary icon colour. Defaults to <c>currentColor</c>, so an icon inherits the colour of
    /// the text around it — including whatever a disabled or hovered parent sets.
    /// </summary>
    [Parameter] public string Color { get; set; } = "currentColor";

    /// <summary>The secondary colour, used by two-tone artwork. Falls back to <see cref="Color"/>.</summary>
    [Parameter] public string? AccentColor { get; set; }

    /// <summary>Stroke width in the icon's own 24-unit space. The set is drawn for 2.</summary>
    [Parameter] public double StrokeWidth { get; set; } = 2d;

    /// <summary>
    /// Whether the icon is animating. Settable: true loops it, false stops and returns it to rest —
    /// which is the parameter to bind to a busy flag.
    /// </summary>
    [Parameter] public bool IsPlaying { get; set; }

    /// <summary>Raised when playback starts or stops.</summary>
    [Parameter] public EventCallback<bool> IsPlayingChanged { get; set; }

    /// <summary>Raised when the icon is clicked.</summary>
    [Parameter] public EventCallback OnClick { get; set; }

    /// <summary>Raised when a play cycle completes.</summary>
    [Parameter] public EventCallback Completed { get; set; }

    /// <summary>An accessible name. Without one the icon is hidden from assistive technology.</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Extra classes for the wrapper.</summary>
    [Parameter] public string? CssClass { get; set; }

    /// <summary>Anything else is splatted onto the wrapper.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }

    string ViewBox => string.Create(CultureInfo.InvariantCulture,
        $"0 0 {icon?.ViewBox ?? 24f} {icon?.ViewBox ?? 24f}");

    string RootStyle
    {
        get
        {
            // Everything instance-specific rides in as a custom property, which is what lets the
            // compiled stylesheet be shared between every instance of the same icon.
            var cycle = spec?.Duration ?? TimeSpan.Zero;
            var speed = Speed > 0d ? Speed : 1d;

            return string.Create(CultureInfo.InvariantCulture,
                $"--shiny-mi-size:{Size}px;" +
                $"--shiny-mi-color:{Color};" +
                $"--shiny-mi-accent:{AccentColor ?? Color};" +
                $"--shiny-mi-stroke:{StrokeWidth};" +
                $"--shiny-mi-duration:{cycle.TotalMilliseconds / speed:0.##}ms;" +
                $"--shiny-mi-iterations:{Iterations};");
        }
    }

    string Iterations
    {
        get
        {
            if (looping || Trigger.HasFlag(MotionTrigger.Loop) || RepeatCount <= 0)
                return "infinite";

            return RepeatCount.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        icon = MotionResolver.ResolveIcon(Source, Icon, PathData);

        spec = MotionResolver.ResolveMotion(
            icon,
            Motion,
            Duration > TimeSpan.Zero ? Duration : null,
            Interval > TimeSpan.Zero ? Interval : null);

        plan = icon is not null && spec is not null ? MotionPlan.For(icon, spec) : null;

        if (plan is null)
        {
            playing = false;
            return;
        }

        if (Trigger.HasFlag(MotionTrigger.Loop))
        {
            looping = true;
            playing = true;
            return;
        }

        // A caller driving IsPlaying explicitly wins over the trigger flags; this is the busy-state
        // path, so it loops for as long as the flag is set.
        if (IsPlaying && !playing)
        {
            looping = true;
            playing = true;
        }
        else if (!IsPlaying && playing && looping && !Trigger.HasFlag(MotionTrigger.Hover))
        {
            Halt();
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // A loop never ends, so an icon carrying MotionTrigger.Loop skips the interop entirely.
        // Everything else can stop, and stopping is what needs the callbacks: without them `playing`
        // is never cleared, so the icon runs its one cycle and then sits in the playing state
        // forever, ignoring every subsequent Play. That includes MotionTrigger.Manual — the trigger
        // the host controls use precisely because they drive Play() themselves.
        var needsAppear = Trigger.HasFlag(MotionTrigger.Appear);
        var needsCallbacks = !Trigger.HasFlag(MotionTrigger.Loop);

        if (attached || !needsCallbacks || plan is null)
            return;

        attached = true;

        if (module is null)
        {
            var loaded = await JS.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/Shiny.Blazor.Controls/motion-icon.js");
            if (disposed) { await loaded.ReleaseLateAsync(); return; }
            module = loaded;
        }

        selfRef ??= DotNetObjectReference.Create(this);

        await module.InvokeVoidAsync("attach", element, selfRef, needsAppear);
    }

    /// <summary>Plays the animation. Called by the intersection observer for the appear trigger.</summary>
    [JSInvokable]
    public Task OnAppear()
    {
        Play();
        return Task.CompletedTask;
    }

    /// <summary>Called when the animation finishes its last cycle.</summary>
    [JSInvokable]
    public Task OnAnimationEnded() => OnEnded();

    /// <summary>Called at the end of each cycle of a repeating animation.</summary>
    [JSInvokable]
    public Task OnAnimationIteration() => OnCycleCompleted();

    /// <summary>Plays the animation for <see cref="RepeatCount"/> cycles.</summary>
    public void Play()
    {
        if (plan is null)
            return;

        looping = RepeatCount <= 0;
        pendingStop = false;

        SetPlaying(true);
    }

    /// <summary>Plays the animation continuously until stopped.</summary>
    public void Loop()
    {
        if (plan is null)
            return;

        looping = true;
        pendingStop = false;

        SetPlaying(true);
    }

    /// <summary>Stops immediately and returns the icon to its resting pose.</summary>
    public void Stop() => Halt();

    /// <summary>Lets the cycle in progress finish, then stops.</summary>
    public void StopAtCycleEnd()
    {
        if (!playing)
            return;

        pendingStop = true;
    }

    void OnPointerEnter()
    {
        if (Trigger.HasFlag(MotionTrigger.Hover))
            Loop();
    }

    void OnPointerLeave()
    {
        if (!Trigger.HasFlag(MotionTrigger.Hover))
            return;

        // Stopping the moment the pointer leaves would snap a half-swung bell upright. Letting the
        // cycle finish costs at most one cycle and always ends on the resting pose.
        StopAtCycleEnd();
    }

    Task OnClicked()
    {
        if (Trigger.HasFlag(MotionTrigger.Press))
            Play();

        return OnClick.HasDelegate ? OnClick.InvokeAsync() : Task.CompletedTask;
    }

    internal Task OnCycleCompleted()
    {
        // Animation events bubble, so this fires once per animated element in the icon. They all run
        // the same duration and start together, so acting on the first and ignoring the rest is
        // correct — the guard is what stops a four-part icon raising Completed four times.
        if (!pendingStop || !playing)
            return Task.CompletedTask;

        Halt();
        return Completed.InvokeAsync();
    }

    internal Task OnEnded()
    {
        if (!playing || looping)
            return Task.CompletedTask;

        Halt();
        return Completed.InvokeAsync();
    }

    void Halt()
    {
        looping = false;
        pendingStop = false;

        SetPlaying(false);
    }

    void SetPlaying(bool value)
    {
        if (disposed)
            return;

        var changed = playing != value;
        playing = value;

        if (!changed)
        {
            StateHasChanged();
            return;
        }

        IsPlaying = value;

        if (IsPlayingChanged.HasDelegate)
            _ = IsPlayingChanged.InvokeAsync(value);

        StateHasChanged();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        disposed = true;

        if (module is not null)
        {
            try
            {
                await module.InvokeVoidAsync("detach", element);
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The circuit is already gone; there is nothing left to clean up on the other side.
            }
        }

        selfRef?.Dispose();
        GC.SuppressFinalize(this);
    }
}

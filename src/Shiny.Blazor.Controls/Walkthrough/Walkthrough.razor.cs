using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

/// <summary>
/// A guided tour of a page: dim everything, cut a hole around one element at a time, and say what it
/// does.
/// </summary>
/// <remarks>
/// <code>
/// &lt;Walkthrough RememberRunKey="home-v1" AutoStart="true" @@ref="tour"&gt;
///     &lt;WalkthroughStep Target="#search" Title="Find anything" Text="Search across every project." /&gt;
///     &lt;WalkthroughStep Target="#add" Text="Start something new here."
///                      Display="WalkthroughDisplay.Spotlight" Highlight="WalkthroughHighlight.Circle" /&gt;
/// &lt;/Walkthrough&gt;
/// </code>
/// <para>
/// A step advances three ways: the built-in Next (or a bound <c>NextAsync</c>), a click on the
/// highlighted element itself when <c>AdvanceOnTargetClick</c> is set, or a <c>Duration</c> timer.
/// </para>
/// </remarks>
public partial class Walkthrough
{
    const int SignalStop = int.MinValue;
    const int SignalSkip = int.MinValue + 1;

    readonly string instanceId = $"shiny-wt-{Guid.NewGuid():N}";
    readonly List<WalkthroughStep> steps = new();

    IJSObjectReference? module;
    bool disposed;
    DotNetObjectReference<Walkthrough>? selfRef;
    ElementReference calloutRef;
    ElementReference nextRef;

    TaskCompletionSource<int>? move;
    int? pendingSignal;
    CancellationTokenSource? dwell;
    CancellationTokenSource? runCancel;

    bool running;
    bool scrimVisible;

    // Separate from `running` so the scrim and callout survive their own exit transition. Clearing
    // `running` alone would drop the whole block out of the render tree on the same frame the fade
    // was asked for, and the tour would vanish instead of lifting.
    bool chromeVisible;
    bool started;
    bool watchingClicks;

    TooltipRect? spot;
    TooltipRect? viewport;
    string placement = "bottom";
    double tailOffset;
    double calloutLeft;
    double calloutTop;
    bool calloutPlaced;


    /// <summary>The steps, as markup. Children are <c>WalkthroughStep</c> components.</summary>
    [Parameter] public RenderFragment? Steps { get; set; }

    /// <summary>Alias for <see cref="Steps"/>, so steps can be declared without the wrapper tag.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }


    // ---------------------------------------------------------------------------------------------
    // Running
    // ---------------------------------------------------------------------------------------------

    /// <summary>Start as soon as the page is up, subject to <see cref="RememberRunKey"/>.</summary>
    [Parameter] public bool AutoStart { get; set; }

    /// <summary>
    /// Milliseconds to wait before auto-starting. The default leaves room for the page to settle —
    /// measuring a target mid-entrance-animation highlights where it was, not where it lands.
    /// </summary>
    [Parameter] public int AutoStartDelay { get; set; } = 400;

    /// <summary>
    /// Remember, under this key, that the user has been through the tour, and do not auto-start it
    /// again. Leave it unset and the tour runs every time. Clear it with <see cref="ResetAsync"/>.
    /// </summary>
    [Parameter] public string? RememberRunKey { get; set; }

    /// <summary>Count a skip as having run. On by default — a dismissed tour should stay dismissed.</summary>
    [Parameter] public bool RememberOnSkip { get; set; } = true;

    /// <summary>Whether the tour is on screen. Two-way.</summary>
    [Parameter] public bool IsRunning { get; set; }

    [Parameter] public EventCallback<bool> IsRunningChanged { get; set; }


    // ---------------------------------------------------------------------------------------------
    // The backdrop
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Dim the page behind the tour. Off leaves the app fully visible and the callouts floating over
    /// live content — which also disables the cut-out, since there is nothing to cut.
    /// </summary>
    [Parameter] public bool UseOverlay { get; set; } = true;

    /// <summary>Any CSS colour. Defaults to the theme's scrim.</summary>
    [Parameter] public string? OverlayColor { get; set; }

    [Parameter] public double OverlayOpacity { get; set; } = 0.8;

    /// <summary>The default cut-out shape. A step can override it.</summary>
    [Parameter] public WalkthroughHighlight Highlight { get; set; } = WalkthroughHighlight.RoundedRectangle;

    /// <summary>Breathing room left around the target inside the cut-out, in pixels.</summary>
    [Parameter] public double HighlightPadding { get; set; } = 6;

    [Parameter] public double HighlightCornerRadius { get; set; } = 10;

    /// <summary>An outline traced round the cut-out. Any CSS colour; leave unset for none.</summary>
    [Parameter] public string? RingColor { get; set; }

    [Parameter] public double RingThickness { get; set; }

    /// <summary>How long the spotlight takes to travel from one target to the next, in milliseconds.</summary>
    [Parameter] public int SpotlightMoveDuration { get; set; } = 320;

    /// <summary>Stop the page scrolling under the tour while it runs.</summary>
    [Parameter] public bool LockScroll { get; set; } = true;


    // ---------------------------------------------------------------------------------------------
    // Callout chrome
    // ---------------------------------------------------------------------------------------------

    [Parameter] public bool ShowNavigation { get; set; } = true;

    [Parameter] public bool ShowStepCounter { get; set; } = true;

    [Parameter] public bool ShowSkip { get; set; } = true;

    [Parameter] public bool ShowBack { get; set; } = true;

    [Parameter] public string NextText { get; set; } = "Next";

    [Parameter] public string BackText { get; set; } = "Back";

    [Parameter] public string SkipText { get; set; } = "Skip";

    /// <summary>Replaces <see cref="NextText"/> on the last step.</summary>
    [Parameter] public string FinishText { get; set; } = "Done";

    /// <summary>
    /// Clicking the dimmed area moves to the next step. Off by default, because a stray click would
    /// otherwise end a tour early.
    /// </summary>
    [Parameter] public bool AdvanceOnBackdropClick { get; set; }

    /// <summary>Bring each target into view before highlighting it.</summary>
    [Parameter] public bool ScrollToTarget { get; set; } = true;

    /// <summary>Arrow keys move, Escape leaves. On by default — a tour nobody can leave is a trap.</summary>
    [Parameter] public bool EnableKeyboard { get; set; } = true;

    /// <summary>Gap between the highlight and the callout, in pixels.</summary>
    [Parameter] public double CalloutOffset { get; set; } = 14;

    /// <summary>How close to the viewport edges a callout is allowed to get.</summary>
    [Parameter] public double ScreenMargin { get; set; } = 16;

    /// <summary>Ceiling on the callout's width.</summary>
    [Parameter] public string MaxCalloutWidth { get; set; } = "320px";

    /// <summary>Overrides the card background. Any CSS colour.</summary>
    [Parameter] public string? CalloutColor { get; set; }

    [Parameter] public string? CalloutTextColor { get; set; }

    [Parameter] public string? CssClass { get; set; }


    // ---------------------------------------------------------------------------------------------
    // Callbacks
    // ---------------------------------------------------------------------------------------------

    [Parameter] public EventCallback Started { get; set; }

    /// <summary>Fires on every move, with the step's name.</summary>
    [Parameter] public EventCallback<string?> StepChanged { get; set; }

    [Parameter] public EventCallback Completed { get; set; }

    [Parameter] public EventCallback Skipped { get; set; }

    /// <summary>Fires however the run ended.</summary>
    [Parameter] public EventCallback<WalkthroughEndReason> Ended { get; set; }


    /// <summary>
    /// Resolved through the provider rather than injected directly: <c>[Inject]</c> uses
    /// <c>GetRequiredService</c> and would throw for an app that never called
    /// <c>AddShinyWalkthrough()</c> — taking the page down over an optional feature. Without a store a
    /// tour simply runs every time, which is the safe direction for onboarding to fail in.
    /// </summary>
    [Inject] IServiceProvider Services { get; set; } = default!;

    IWalkthroughStore? Store => this.store ??= this.Services.GetService(typeof(IWalkthroughStore)) as IWalkthroughStore;

    IWalkthroughStore? store;


    // ---------------------------------------------------------------------------------------------
    // Position
    // ---------------------------------------------------------------------------------------------

    /// <summary>The steps in the run — visible ones only.</summary>
    public IReadOnlyList<WalkthroughStep> VisibleSteps => this.steps.Where(s => s.IsVisible).ToList();

    /// <summary>Zero-based index of the step showing, or -1 when nothing is running.</summary>
    public int CurrentStepIndex { get; private set; } = -1;

    /// <summary>The step showing.</summary>
    public WalkthroughStep? CurrentStepItem { get; private set; }

    /// <summary>How many steps are in the run.</summary>
    public int StepCount => this.VisibleSteps.Count;

    /// <summary>One-based position of the step showing.</summary>
    public int StepNumber => this.CurrentStepIndex + 1;

    bool IsFirstStep => this.CurrentStepIndex <= 0;

    bool IsLastStep => this.CurrentStepIndex >= this.StepCount - 1;

    WalkthroughDisplay EffectiveDisplay
    {
        get
        {
            var display = this.CurrentStepItem?.Display ?? WalkthroughDisplay.Popover;

            // Bare text on live content is unreadable, so a spotlight step with no backdrop falls back
            // to the card rather than rendering something nobody can see.
            return display == WalkthroughDisplay.Spotlight && !this.UseOverlay
                ? WalkthroughDisplay.Popover
                : display;
        }
    }

    bool IsCompact => this.EffectiveDisplay == WalkthroughDisplay.Tooltip;

    bool HasTail =>
        this.EffectiveDisplay is WalkthroughDisplay.Tooltip or WalkthroughDisplay.Popover
        && this.placement != "center";

    bool PassThrough =>
        this.CurrentStepItem is { } step
        && (step.AllowTargetInteraction || step.AdvanceOnTargetClick)
        && this.spot is { Width: > 0, Height: > 0 };


    // ---------------------------------------------------------------------------------------------
    // Public control
    // ---------------------------------------------------------------------------------------------

    /// <summary>Starts the tour from the first visible step. No-op if it is already running.</summary>
    public Task StartAsync(int fromIndex = 0)
    {
        if (this.running)
            return Task.CompletedTask;

        return this.RunAsync(fromIndex);
    }

    /// <summary>Ends the run. Nothing is recorded, so an auto-start shows it again next time.</summary>
    public Task StopAsync() => this.SignalAsync(SignalStop);

    /// <summary>Moves to the next step, ending the run if this was the last.</summary>
    public Task NextAsync() => this.SignalAsync(1);

    /// <summary>Moves back a step. No-op on the first.</summary>
    public Task BackAsync() => this.SignalAsync(-1);

    /// <summary>Ends the run as skipped.</summary>
    public Task SkipAsync() => this.SignalAsync(SignalSkip);

    /// <summary>Jumps to a step by name.</summary>
    public Task GoToAsync(string name)
    {
        var visible = this.VisibleSteps;
        var index = -1;
        for (var i = 0; i < visible.Count; i++)
        {
            if (string.Equals(visible[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        return index < 0 ? Task.CompletedTask : this.GoToAsync(index);
    }

    /// <summary>Jumps to a step by position among the visible steps.</summary>
    public Task GoToAsync(int index)
        => this.running ? this.SignalAsync(index - this.CurrentStepIndex) : this.StartAsync(index);

    /// <summary>Forgets that this walkthrough has run, so <see cref="AutoStart"/> shows it again.</summary>
    public async Task ResetAsync()
    {
        if (this.Store is not null && !string.IsNullOrWhiteSpace(this.RememberRunKey))
            await this.Store.SetHasRunAsync(this.RememberRunKey!, false);
    }

    /// <summary>Forgets the run flag and starts again — the "show me the tour" menu item.</summary>
    public async Task RestartAsync()
    {
        await this.ResetAsync();
        await this.StartAsync();
    }


    /// <summary>
    /// Moves the run on, or ends it.
    /// </summary>
    /// <remarks>
    /// A signal can arrive while the run is between steps — mid-scroll, mid-spotlight-travel — when
    /// there is no waiter to hand it to. Holding it until the next wait is what stops a Stop during an
    /// animation from being swallowed and the tour carrying on regardless.
    /// </remarks>
    Task SignalAsync(int delta)
    {
        if (!this.running)
            return Task.CompletedTask;

        this.CancelDwell();

        if (delta is SignalStop or SignalSkip)
            this.runCancel?.Cancel();

        if (this.move?.TrySetResult(delta) != true)
            this.pendingSignal = delta;

        return Task.CompletedTask;
    }


    // ---------------------------------------------------------------------------------------------
    // Step registration
    // ---------------------------------------------------------------------------------------------

    void IWalkthroughHost.RegisterStep(WalkthroughStep step)
    {
        if (!this.steps.Contains(step))
            this.steps.Add(step);

        this.StateHasChanged();
    }


    void IWalkthroughHost.UnregisterStep(WalkthroughStep step)
    {
        this.steps.Remove(step);
        this.StateHasChanged();
    }


    void IWalkthroughHost.NotifyStepChanged(WalkthroughStep step) => this.StateHasChanged();


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        var loaded = await this.JS.InvokeAsync<IJSObjectReference>(
            "import",
            "./_content/Shiny.Blazor.Controls/walkthrough.js"
        );
        if (this.disposed) { await loaded.ReleaseLateAsync(); return; }
        this.module = loaded;
        this.selfRef = DotNetObjectReference.Create(this);

        if (this.AutoStart && !this.started)
        {
            this.started = true;
            await Task.Delay(Math.Max(1, this.AutoStartDelay));
            if (this.disposed)
                return;

            if (!await this.HasRunAsync())
                await this.StartAsync();
        }
        else if (this.IsRunning && !this.running)
        {
            await this.StartAsync();
        }
    }


    async Task<bool> HasRunAsync()
    {
        if (this.Store is null || string.IsNullOrWhiteSpace(this.RememberRunKey))
            return false;

        return await this.Store.HasRunAsync(this.RememberRunKey!);
    }
}

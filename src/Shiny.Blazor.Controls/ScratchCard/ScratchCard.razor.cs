using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

/// <summary>
/// A scratch-off card: a cover sits over the hidden content and is scratched away under the
/// pointer until enough is gone to reveal the rest.
/// </summary>
/// <example>
/// <code>
/// &lt;ScratchCard ThresholdPercent="55" OnThresholdReached="Claim" Style="width:320px;height:180px"&gt;
///     &lt;Cover&gt;&lt;img src="foil.png" style="width:100%;height:100%;object-fit:cover" /&gt;&lt;/Cover&gt;
///     &lt;ChildContent&gt;&lt;h2&gt;You won!&lt;/h2&gt;&lt;/ChildContent&gt;
/// &lt;/ScratchCard&gt;
/// </code>
/// </example>
/// <remarks>
/// <para>
/// Both layers are live markup - the cover is not flattened to a bitmap, so an image, a gradient or
/// any HTML works. The script draws the strokes into a canvas and applies it to the cover as a CSS
/// mask, so the scratched edge is smooth and round. It also measures how much is gone and reports
/// it back; the threshold decision is made here.
/// </para>
/// <para>
/// The card is as big as its content unless it is given a size. The content is <c>inert</c> until
/// <see cref="ThresholdPercent"/> is reached - no click, tap or keyboard focus gets through a hole in
/// the cover - and from then on it takes clicks, so a "Claim" button underneath works once earned.
/// </para>
/// </remarks>
public partial class ScratchCard
{
    ElementReference root;
    IJSObjectReference? module;
    DotNetObjectReference<ScratchCard>? selfRef;
    bool disposed;
    bool revealed;
    bool coverGone;
    bool resetPending;
    bool thresholdReached;
    int revealVersion;
    string? lastOptions;

    /// <summary>The content underneath, revealed as the cover is scratched away.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>What sits on top and gets scratched off - an image, a gradient, any markup. Null draws a plain silver foil.</summary>
    [Parameter] public RenderFragment? Cover { get; set; }

    /// <summary>The default <see cref="StrokeWidth"/>: about a fingertip, the same 44px as a minimum touch target.</summary>
    public const double DefaultStrokeWidth = 44;

    /// <summary>How wide a scratch stroke is, in CSS pixels. Defaults to 44 - about the width of a fingertip.</summary>
    [Parameter] public double StrokeWidth { get; set; } = DefaultStrokeWidth;

    /// <summary>
    /// The percentage of the cover (0 to 100) that, once scratched, raises <see cref="OnThresholdReached"/> -
    /// and clears the rest of the cover when <see cref="AutoReveal"/> is on. Defaults to 60.
    /// </summary>
    [Parameter] public double ThresholdPercent { get; set; } = 60;

    /// <summary>
    /// Clears the rest of the cover once <see cref="ThresholdPercent"/> is reached. On by default. Off still
    /// raises <see cref="OnThresholdReached"/> and still unlocks the content, but leaves what is left of the
    /// cover painted - call <see cref="Reveal"/> to clear it later.
    /// </summary>
    [Parameter] public bool AutoReveal { get; set; } = true;

    /// <summary>Whether strokes scratch. False holds the cover as it is.</summary>
    [Parameter] public bool IsScratchEnabled { get; set; } = true;

    /// <summary>How long the remaining cover takes to fade on reveal. Zero snaps it away.</summary>
    [Parameter] public TimeSpan RevealDuration { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Whether the cover has cleared. Bindable: becomes true when the user scratches past the
    /// threshold; set it true to reveal from code, false to put a fresh cover back.
    /// </summary>
    [Parameter] public bool IsRevealed { get; set; }

    [Parameter] public EventCallback<bool> IsRevealedChanged { get; set; }

    /// <summary>A stroke started on the cover.</summary>
    [Parameter] public EventCallback OnScratchStarted { get; set; }

    /// <summary>The scratched share of the cover moved, 0 to 100.</summary>
    [Parameter] public EventCallback<double> OnScratchProgress { get; set; }

    /// <summary>
    /// The scratched share passed <see cref="ThresholdPercent"/>, with that percentage. Raised once per cover -
    /// again only after a <see cref="Reset"/> - whether or not <see cref="AutoReveal"/> then clears the rest.
    /// </summary>
    [Parameter] public EventCallback<double> OnThresholdReached { get; set; }

    /// <summary>The cover cleared - by <see cref="AutoReveal"/> at the threshold, or revealed from code.</summary>
    [Parameter] public EventCallback OnRevealed { get; set; }

    [Parameter] public string? Class { get; set; }

    [Parameter] public string? Style { get; set; }

    /// <summary>How much of the cover has been scratched away, 0 to 100. Jumps to 100 on reveal.</summary>
    public double ScratchedPercent { get; private set; }


    /// <summary>The threshold was reached or the card revealed: the content takes clicks and scratching is over.</summary>
    internal bool IsUnlocked => this.revealed || this.thresholdReached;

    internal bool CanScratch => this.IsScratchEnabled && !this.IsUnlocked;

    internal bool CoverRemoved => this.coverGone;

    internal string RootStyle
    {
        get
        {
            var ms = Math.Max(0, this.RevealDuration.TotalMilliseconds).ToString("0", CultureInfo.InvariantCulture);
            var style = $"--shiny-scratch-card-reveal-duration:{ms}ms;";
            return string.IsNullOrWhiteSpace(this.Style) ? style : style + this.Style;
        }
    }


    protected override async Task OnParametersSetAsync()
    {
        if (this.IsRevealed == this.revealed)
            return;

        if (this.IsRevealed)
            await this.RevealCore(raiseBinding: false);
        else
            await this.ResetCore(raiseBinding: false);
    }


    /// <summary>Clears the cover without scratching.</summary>
    public Task Reveal() => this.RevealCore(raiseBinding: true);


    /// <summary>Puts a fresh, unscratched cover back.</summary>
    public Task Reset() => this.ResetCore(raiseBinding: true);


    async Task RevealCore(bool raiseBinding)
    {
        if (this.revealed)
            return;

        this.revealed = true;
        this.revealVersion++;
        if (this.RevealDuration <= TimeSpan.Zero)
            this.coverGone = true;
        else
            _ = this.RemoveCoverAfterFade(this.revealVersion);

        this.SetPercent(100);
        this.StateHasChanged();

        if (raiseBinding)
            await this.IsRevealedChanged.InvokeAsync(true);

        await this.OnScratchProgress.InvokeAsync(100);
        await this.OnRevealed.InvokeAsync();
    }


    async Task ResetCore(bool raiseBinding)
    {
        this.revealVersion++;
        this.revealed = false;
        this.thresholdReached = false;
        this.coverGone = false;
        this.SetPercent(0);

        // Always tell the script, even when the cover had left the DOM: the new cover element is clean,
        // but the script's canvas still holds the old strokes and would mask the new cover with them.
        this.resetPending = true;
        this.StateHasChanged();

        if (raiseBinding)
            await this.IsRevealedChanged.InvokeAsync(false);

        await this.OnScratchProgress.InvokeAsync(0);
    }


    void SetPercent(double value) => this.ScratchedPercent = Math.Clamp(value, 0, 100);


    /// <summary>
    /// Drops the cover from the DOM once its fade has run, so it cannot catch anything. Timed rather
    /// than hung on transitionend: Blazor maps no transition events, and a handler for one compiles,
    /// renders a literal attribute and never fires.
    /// </summary>
    async Task RemoveCoverAfterFade(int version)
    {
        await Task.Delay(this.RevealDuration + TimeSpan.FromMilliseconds(50));

        // A reset (and perhaps another reveal) since then owns the cover now.
        if (this.disposed || !this.revealed || version != this.revealVersion)
            return;

        this.coverGone = true;
        await this.InvokeAsync(this.StateHasChanged);
    }


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            var options = this.OptionsJson();

            if (firstRender)
            {
                var loaded = await this.JS.InvokeAsync<IJSObjectReference>("import", "./_content/Shiny.Blazor.Controls/scratch-card.js");
                if (this.disposed) { await loaded.ReleaseLateAsync(); return; }

                this.module = loaded;
                this.selfRef = DotNetObjectReference.Create(this);
                this.lastOptions = options;
                await this.module.InvokeVoidAsync("init", this.root, this.selfRef, options);
                return;
            }

            if (this.module is null)
                return;

            if (this.resetPending)
            {
                this.resetPending = false;
                await this.module.InvokeVoidAsync("reset", this.root);
            }

            // Always sent: it is also how the script finds a cover element that came back after a reset.
            if (options != this.lastOptions || !this.coverGone)
            {
                this.lastOptions = options;
                await this.module.InvokeVoidAsync("update", this.root, options);
            }
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
        catch (JSException) { }
    }


    /// <summary>A stroke went down on the cover.</summary>
    [JSInvokable]
    public Task ScratchStarted() => this.CanScratch ? this.OnScratchStarted.InvokeAsync() : Task.CompletedTask;


    /// <summary>The script's measurement of how much of the cover is gone, as a fraction from 0 to 1.</summary>
    [JSInvokable]
    public async Task ReportProgress(double fraction)
    {
        if (!this.CanScratch)
            return;

        var percent = Math.Clamp(fraction, 0, 1) * 100;
        if (Math.Abs(percent - this.ScratchedPercent) < 0.01)
            return;

        this.SetPercent(percent);
        await this.OnScratchProgress.InvokeAsync(percent);

        if (this.thresholdReached || percent < Math.Clamp(this.ThresholdPercent, 0, 100))
            return;

        // Scratching is done and the content unlocks - with AutoReveal off too, where the rest of the
        // cover stays painted but stops catching the pointer. A JS-invoked method does not re-render
        // by itself, so ask for it: the render is what makes the content interactive.
        this.thresholdReached = true;
        this.StateHasChanged();
        await this.OnThresholdReached.InvokeAsync(percent);

        // A handler may have revealed or reset the card already.
        if (this.AutoReveal && !this.revealed && this.thresholdReached)
            await this.RevealCore(raiseBinding: true);
    }


    // A string, not an object: no DTO for the trimmer to strip in a published WASM build.
    internal string OptionsJson()
    {
        var c = CultureInfo.InvariantCulture;
        return "{" +
            $"\"width\":{Math.Max(2, this.StrokeWidth).ToString(c)}," +
            $"\"enabled\":{(this.CanScratch ? "true" : "false")}" +
            "}";
    }


    public async ValueTask DisposeAsync()
    {
        this.disposed = true;

        try
        {
            if (this.module is not null)
            {
                await this.module.InvokeVoidAsync("dispose", this.root);
                await this.module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
        catch (JSException) { }

        this.selfRef?.Dispose();
        GC.SuppressFinalize(this);
    }
}

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using global::Shiny.Controls.Keyframe;
using Shiny.Blazor.Controls.Keyframe.Compilation;

namespace Shiny.Blazor.Controls.Keyframe;

/// <summary>Something a <see cref="Choreography"/> composes: a <see cref="Keyframes"/> or a nested Choreography.</summary>
internal interface IKeyframeNode
{
    /// <summary>Compiles the node into placed animations, reading live values from the page where a key needs them.</summary>
    Task<NodeProgram> BuildAsync(IJSObjectReference module, ElementCollector elements);

    /// <summary>A cheap fingerprint of everything that shapes the animation, to notice edits while it runs.</summary>
    string Signature();
}

/// <summary>A parent that collects <see cref="Keyframes"/> children: as nodes to play, or as templates to apply elsewhere.</summary>
internal interface IKeyframeContainer
{
    /// <summary>True when child Keyframes are templates (applied to other elements) and so render no wrapper of their own.</summary>
    bool AsTemplates { get; }

    void Register(IKeyframeNode node);
    void Unregister(IKeyframeNode node);
}

/// <summary>The compiled animations of a node, each placed at an offset inside it.</summary>
internal sealed record NodeProgram(List<ProgramItem> Items)
{
    /// <summary>When the last animation ends. Infinite if anything loops forever.</summary>
    public double EndMs => this.Items.Count == 0 ? 0d : this.Items.Max(i => i.OffsetMs + i.Animation.TotalMs);
}


/// <summary>
/// The player shared by <see cref="Keyframes"/> and <see cref="Choreography"/>: play, pause, resume,
/// stop, finish, seek, scrub and live rate changes, over one group of Web Animations.
/// </summary>
/// <remarks>
/// Mirrors the MAUI package's <c>Player</c>. The methods return tasks because they cross into the
/// browser, but they can be fired and forgotten; <see cref="PlayAsync"/> is the one to await, and
/// completes with true when the animation runs to its end and false when it is stopped, replaced or
/// cancelled - it never throws for that.
/// </remarks>
public abstract class KeyframePlayerBase : ComponentBase, IAsyncDisposable
{
    [Inject] IJSRuntime JS { get; set; } = default!;

    /// <summary>Starts playing as soon as it renders. Defaults to true. Ignored while <see cref="Playing"/> is set.</summary>
    [Parameter] public bool AutoPlay { get; set; } = true;

    /// <summary>
    /// Drives playback from a value: true plays (from the start, each time it becomes true), false
    /// stops, null leaves it to <see cref="AutoPlay"/> and the methods. The Blazor counterpart of
    /// the MAUI package's trigger actions.
    /// </summary>
    [Parameter] public bool? Playing { get; set; }

    /// <summary>The playback rate. 2 is double speed; a negative value plays backwards, and changing it mid-flight reverses in place.</summary>
    [Parameter] public double Speed { get; set; } = 1d;

    /// <summary>How the animation responds to <c>prefers-reduced-motion</c>. Auto (the default) jumps to the end state, and a loop holds its first frame.</summary>
    [Parameter] public ReducedMotionBehavior ReducedMotion { get; set; } = ReducedMotionBehavior.Auto;

    /// <summary>When true, <see cref="Stop()"/> puts the element back the way it was; otherwise the values hold where they stopped.</summary>
    [Parameter] public bool RestoreOnStop { get; set; }

    /// <summary>Raised when the animation runs to its end (not when it is stopped).</summary>
    [Parameter] public EventCallback Finished { get; set; }

    /// <summary>Raised whenever <see cref="State"/> changes.</summary>
    [Parameter] public EventCallback<PlaybackState> StateChanged { get; set; }

    /// <summary>Where playback is: idle, running, paused or finished.</summary>
    public PlaybackState State { get; private set; } = PlaybackState.Idle;

    /// <summary>The rate playback is running at: <see cref="Speed"/>, or whatever <see cref="SetRate"/> set since.</summary>
    public double Rate { get; private set; } = 1d;

    IJSObjectReference? module;
    IJSObjectReference? group;
    DotNetObjectReference<GroupCallbacks>? callbacks;
    TaskCompletionSource<bool>? completion;
    int generation = -1;
    string? playedSignature;
    bool? appliedPlaying;
    bool started;
    bool disposed;

    /// <summary>False when a parent (a Choreography) plays this one as part of itself.</summary>
    private protected abstract bool IsDriver { get; }

    /// <summary>The value of Playing that applies: a StateAnimation's, or the parameter.</summary>
    private protected virtual bool? EffectivePlaying => this.Playing;

    /// <summary>Whether a plain Stop restores: a StateAnimation always does, so leaving the state puts its look back.</summary>
    private protected virtual bool EffectiveRestoreOnStop => this.RestoreOnStop;

    private protected abstract Task<NodeProgram> BuildProgramAsync(IJSObjectReference module, ElementCollector elements);

    private protected abstract string BuildSignature();


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!this.IsDriver || this.disposed)
            return;

        var playing = this.EffectivePlaying;

        if (!this.started)
        {
            this.started = true;
            this.appliedPlaying = playing;
            this.Rate = this.Speed;
            this.lastSpeedParameter = this.Speed;
            if (playing ?? this.AutoPlay)
                await this.Play();
            return;
        }

        if (playing != this.appliedPlaying)
        {
            this.appliedPlaying = playing;
            if (playing == true)
                await this.Play();
            else if (playing == false)
                await this.Stop();
        }
        else if (this.State == PlaybackState.Running && this.playedSignature is not null && this.BuildSignature() != this.playedSignature)
        {
            // Edited while it runs (a duration slider, a new key): play the new version from where the element is.
            await this.Play();
        }

        // A new Speed from the page reverses or retimes in place; SetRate from code holds until then.
        if (this.Speed != this.lastSpeedParameter)
        {
            this.lastSpeedParameter = this.Speed;
            await this.SetRate(this.Speed);
        }
    }

    double lastSpeedParameter = 1d;


    /// <summary>Plays from the start. A key with no value starts from wherever the element is now, so replaying mid-flight never snaps.</summary>
    public Task Play() => this.StartAsync(null);

    /// <summary>Plays from the start and completes when it ends: true when it ran to the end, false when stopped, replaced or cancelled.</summary>
    public async Task<bool> PlayAsync(CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = cancellationToken.Register(() => _ = this.InvokeAsync(this.Stop));
        await this.StartAsync(tcs);
        return await tcs.Task;
    }

    async Task StartAsync(TaskCompletionSource<bool>? tcs)
    {
        this.completion?.TrySetResult(false);
        this.completion = tcs;

        var loaded = await this.LoadAsync();
        if (loaded is null)
        {
            tcs?.TrySetResult(false);
            return;
        }

        try
        {
            this.generation = await loaded.InvokeAsync<int>("play");
            this.playedSignature = this.BuildSignature();
            await this.SetStateAsync(PlaybackState.Running);
        }
        catch (Exception ex) when (KeyframeModule.IsGone(ex))
        {
            tcs?.TrySetResult(false);
        }
    }

    /// <summary>Compiles and hands the program to the script, creating the group the first time.</summary>
    async Task<IJSObjectReference?> LoadAsync()
    {
        if (this.disposed)
            return null;

        this.module ??= await KeyframeModule.GetAsync(this.JS);
        if (this.module is null || this.disposed)
            return null;

        try
        {
            var elements = new ElementCollector();
            var program = await this.BuildProgramAsync(this.module, elements);
            var json = ProgramWriter.Write(program.Items, this.ReducedMotion, this.EffectiveRestoreOnStop, this.Rate);

            if (this.group is null)
            {
                this.callbacks ??= DotNetObjectReference.Create(new GroupCallbacks(this.OnGroupFinishedAsync));
                this.group = await this.module.InvokeAsync<IJSObjectReference>("createGroup", this.callbacks, elements.ToArray(), json);
            }
            else
            {
                await this.group.InvokeVoidAsync("load", elements.ToArray(), json);
            }

            if (this.disposed)
            {
                await this.ReleaseAsync();
                return null;
            }

            return this.group;
        }
        catch (Exception ex) when (KeyframeModule.IsGone(ex))
        {
            return null;
        }
    }

    /// <summary>Holds where it is.</summary>
    public async Task Pause()
    {
        if (await this.CallAsync("pause"))
            await this.SetStateAsync(PlaybackState.Paused);
    }

    /// <summary>Carries on from where it was paused or seeked to. Plays from the start if it never started.</summary>
    public async Task Resume()
    {
        if (this.group is null)
        {
            await this.Play();
            return;
        }

        try
        {
            this.generation = await this.group.InvokeAsync<int>("resume");
            await this.SetStateAsync(PlaybackState.Running);
        }
        catch (Exception ex) when (KeyframeModule.IsGone(ex))
        {
        }
    }

    /// <summary>Stops, holding the values where they are - or restoring the element when <see cref="RestoreOnStop"/> is set.</summary>
    public Task Stop() => this.Stop(this.EffectiveRestoreOnStop);

    /// <summary>Stops, restoring the element (<paramref name="restore"/> true) or holding the values where they are.</summary>
    public async Task Stop(bool restore)
    {
        this.generation = -1;
        await this.CallAsync("stop", restore);
        this.completion?.TrySetResult(false);
        this.completion = null;
        await this.SetStateAsync(PlaybackState.Idle);
    }

    /// <summary>Jumps to the end. A loop parks at the end of its first pass.</summary>
    public async Task Finish()
    {
        if (this.group is null && await this.LoadAsync() is null)
            return;

        await this.CallAsync("finish");
    }

    /// <summary>Moves to a point in time (from the start, delays included), keeping the current state. Seeking before playing leaves it paused there.</summary>
    public async Task Seek(TimeSpan position)
    {
        if (this.group is null && await this.LoadAsync() is null)
            return;

        await this.CallAsync("seek", position.TotalMilliseconds);
        if (this.State is PlaybackState.Idle or PlaybackState.Finished)
            await this.SetStateAsync(PlaybackState.Paused);
    }

    /// <summary>Moves to a fraction (0..1) of the whole animation - the scrubber hook. A loop spans its first pass.</summary>
    public async Task SeekProgress(double progress)
    {
        if (this.group is null && await this.LoadAsync() is null)
            return;

        await this.CallAsync("seekProgress", Math.Clamp(progress, 0d, 1d));
        if (this.State is PlaybackState.Idle or PlaybackState.Finished)
            await this.SetStateAsync(PlaybackState.Paused);
    }

    /// <summary>Changes the rate in place. Negative plays backwards from wherever it is.</summary>
    public async Task SetRate(double rate)
    {
        if (!double.IsFinite(rate))
            return;

        this.Rate = rate;
        await this.CallAsync("setRate", rate);
    }

    /// <summary>Whether the browser has reduced motion on right now.</summary>
    public Task<bool> IsReducedMotionAsync() => KeyframeModule.IsReducedMotionAsync(this.JS);

    async Task<bool> CallAsync(string method, params object?[] args)
    {
        if (this.group is null)
            return false;

        try
        {
            await this.group.InvokeVoidAsync(method, args);
            return true;
        }
        catch (Exception ex) when (KeyframeModule.IsGone(ex))
        {
            return false;
        }
    }

    async Task OnGroupFinishedAsync(int finishedGeneration)
    {
        if (finishedGeneration != this.generation || this.disposed)
            return;

        await this.InvokeAsync(async () =>
        {
            await this.SetStateAsync(PlaybackState.Finished);
            this.completion?.TrySetResult(true);
            this.completion = null;
            await this.Finished.InvokeAsync();
        });
    }

    async Task SetStateAsync(PlaybackState state)
    {
        if (this.State == state)
            return;

        this.State = state;
        await this.StateChanged.InvokeAsync(state);
    }

    async Task ReleaseAsync()
    {
        var current = this.group;
        this.group = null;
        if (current is null)
            return;

        try
        {
            await current.InvokeVoidAsync("dispose");
        }
        catch (Exception ex) when (KeyframeModule.IsGone(ex) || ex is JSException)
        {
        }

        await current.DisposeQuietlyAsync();
    }

    public virtual async ValueTask DisposeAsync()
    {
        this.disposed = true;
        this.completion?.TrySetResult(false);
        this.completion = null;
        await this.ReleaseAsync();
        this.callbacks?.Dispose();
        this.callbacks = null;
        GC.SuppressFinalize(this);
    }
}


/// <summary>The .NET end of a group's finished callback, kept off the component so the component type is not what JS holds.</summary>
internal sealed class GroupCallbacks(Func<int, Task> finished)
{
    [JSInvokable]
    public Task OnGroupFinished(int generation) => finished(generation);
}

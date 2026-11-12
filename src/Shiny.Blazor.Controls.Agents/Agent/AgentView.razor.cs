using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Shiny.Controls.AgentUI;
using Shiny.Controls.AgentUI.A2UI;

namespace Shiny.Blazor.Controls.Agents;

/// <summary>
/// A complete agent conversation over AG-UI: streaming markdown, reasoning, tool call cards with the
/// built-in UI prompts drawn inline, A2UI surfaces, sub-agents, steps, approvals and errors with Retry,
/// plus a composer whose Send turns into Stop while the agent works.
/// </summary>
/// <remarks>
/// <para>
/// Give it a <see cref="Session"/> (the view creates and owns an <see cref="AgentRunner"/>) or a
/// <see cref="Runner"/> the app owns. <see cref="Tools"/> are offered to the agent; when they include
/// the built-in UI tools (<c>AgentUiTools.AddAll(tools, prompts)</c>), pass the same
/// <see cref="Prompts"/> so the view can draw each question under its tool call.
/// </para>
/// <para>
/// The view fills its parent and owns vertical scroll: put it in a container with a real height
/// (<c>height:100%</c> or <c>flex:1; min-height:0</c>), never inside another scroller. It follows the
/// stream while the reader is at the bottom and offers "jump to latest" when they have scrolled up.
/// </para>
/// <para>
/// Token streams are coalesced to about 30 renders per second; each render only re-parses the
/// markdown blocks that changed, so a long answer streams cheaply on Blazor Server too.
/// </para>
/// </remarks>
public partial class AgentView : IAsyncDisposable
{
    static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(33);

    readonly HashSet<string> expanded = new(StringComparer.Ordinal);
    readonly HashSet<string> collapsed = new(StringComparer.Ordinal);

    AgentRunner? runner;
    bool ownsRunner;
    A2UIAgentBinding? binding;
    IAgentSession? boundSession;
    AgentRunner? boundRunner;
    AgentToolRegistry? boundTools;
    AgentPromptQueue? boundPrompts;
    JsonNode? lastState;
    AgentInterruptContext? interruptContext;
    IReadOnlyList<AGUI.Abstractions.AGUIInterrupt>? interruptSource;

    ElementReference scrollRef;
    ElementReference inputRef;
    IJSObjectReference? module;
    string draft = "";
    bool renderScheduled;
    bool disposed;

    [Inject] IJSRuntime JS { get; set; } = default!;

    // ── parameters ──────────────────────────────────────────────────────────────

    /// <summary>The connection to the agent. The view creates (and disposes) a runner over it. Ignored when <see cref="Runner"/> is set.</summary>
    [Parameter] public IAgentSession? Session { get; set; }

    /// <summary>A runner the app owns (so it can keep the conversation across pages). Takes precedence over <see cref="Session"/>.</summary>
    [Parameter] public AgentRunner? Runner { get; set; }

    /// <summary>Frontend tools offered to the agent. With <see cref="Runner"/> they are added to the runner's own registry.</summary>
    [Parameter] public AgentToolRegistry? Tools { get; set; }

    /// <summary>The queue the UI tools ask through; every pending prompt is drawn inline under its tool call.</summary>
    [Parameter] public AgentPromptQueue? Prompts { get; set; }

    /// <summary>The A2UI catalogs surfaces may use. Defaults to the basic catalog plus the Shiny catalog.</summary>
    [Parameter] public A2UICatalogRegistry? Catalogs { get; set; }

    /// <summary>A2UI component renderers. Defaults to the basic and Shiny catalogs.</summary>
    [Parameter] public A2UIRendererRegistry? Renderers { get; set; }

    /// <summary>How reasoning is shown: hidden, a collapsed "Thought for 4s" row (the default), or in full.</summary>
    [Parameter] public AgentReasoningDisplay ShowReasoning { get; set; } = AgentReasoningDisplay.Collapsed;

    /// <summary>Whether the running steps show as a breadcrumb above the conversation.</summary>
    [Parameter] public bool ShowSteps { get; set; } = true;

    /// <summary>The composer's placeholder.</summary>
    [Parameter] public string PlaceholderText { get; set; } = "Ask anything…";

    /// <summary>Prompts offered as chips while the conversation is empty.</summary>
    [Parameter] public IReadOnlyList<string>? Suggestions { get; set; }

    /// <summary>Replaces the user's bubble.</summary>
    [Parameter] public RenderFragment<AgentMessageItem>? UserMessageTemplate { get; set; }

    /// <summary>Replaces the assistant's markdown body.</summary>
    [Parameter] public RenderFragment<AgentMessageItem>? AssistantMessageTemplate { get; set; }

    /// <summary>Per-tool renderers by tool name, replacing the default tool card (generative UI through tool renderers).</summary>
    [Parameter] public IReadOnlyDictionary<string, RenderFragment<AgentToolCallItem>>? ToolCallTemplates { get; set; }

    /// <summary>Renderers by activity type. A2UI surfaces (<c>a2ui-surface</c>) are built in; other types fall back to their JSON.</summary>
    [Parameter] public IReadOnlyDictionary<string, RenderFragment<AgentActivityItem>>? ActivityTemplates { get; set; }

    /// <summary>Replaces the approval card. Answer each request and call <see cref="AgentInterruptContext.SubmitAsync"/>.</summary>
    [Parameter] public RenderFragment<AgentInterruptContext>? InterruptTemplate { get; set; }

    /// <summary>Replaces the error row. <see cref="RetryAsync"/> re-runs the last turn.</summary>
    [Parameter] public RenderFragment<AgentErrorItem>? ErrorTemplate { get; set; }

    /// <summary>Replaces a sub-agent's group.</summary>
    [Parameter] public RenderFragment<AgentSubagent>? SubagentTemplate { get; set; }

    /// <summary>The conversation's shared state. Setting it sends the new state with the next run.</summary>
    [Parameter] public JsonNode? State { get; set; }

    /// <summary>Raised when the agent changes the shared state.</summary>
    [Parameter] public EventCallback<JsonNode?> StateChanged { get; set; }

    /// <summary>Extra CSS classes on the root element.</summary>
    [Parameter] public string? CssClass { get; set; }

    /// <summary>The runner in use (the app's, or the one the view made over <see cref="Session"/>).</summary>
    public AgentRunner? CurrentRunner => runner;

    /// <summary>The surfaces the conversation's A2UI activities created.</summary>
    public A2UISurfaceHost? SurfaceHost => binding?.Host;

    string RootClass => string.IsNullOrWhiteSpace(CssClass) ? "agent-view" : "agent-view " + CssClass;

    // ── public API ──────────────────────────────────────────────────────────────

    /// <summary>Sends a user turn. Ignored while a run is in progress or the text is blank.</summary>
    public async Task SendAsync(string text)
    {
        if (runner is null || runner.IsBusy || string.IsNullOrWhiteSpace(text))
            return;

        await FollowAsync();
        await runner.SendAsync(text.Trim());
    }

    /// <summary>Stops the current run.</summary>
    public void Cancel() => runner?.Cancel();

    /// <summary>Clears the conversation (and the surfaces it created).</summary>
    public void Clear()
    {
        if (runner is null)
            return;
        runner.Cancel();
        if (binding is not null)
        {
            foreach (var id in binding.Host.Surfaces.Keys.ToList())
                binding.Host.DeleteSurface(id);
        }
        runner.Thread.Clear();
        expanded.Clear();
        collapsed.Clear();
        StateHasChanged();
    }

    /// <summary>Runs the conversation again from where it failed.</summary>
    public async Task RetryAsync()
    {
        if (runner is null || runner.IsBusy)
            return;
        await FollowAsync();
        await runner.RetryAsync();
    }

    // ── lifecycle ───────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(Runner, boundRunner) || (Runner is null && !ReferenceEquals(Session, boundSession)))
            Attach();
        else if (!ReferenceEquals(Tools, boundTools) && runner is not null)
            AddTools(runner, Tools);

        if (!ReferenceEquals(Prompts, boundPrompts))
        {
            if (boundPrompts is not null)
                boundPrompts.Changed -= OnPromptsChanged;
            boundPrompts = Prompts;
            if (Prompts is not null)
                Prompts.Changed += OnPromptsChanged;
        }

        // the app wrote the state: it goes to the agent with the next run
        if (runner is not null && !ReferenceEquals(State, lastState) && !ReferenceEquals(State, runner.Thread.State))
        {
            lastState = State;
            runner.Thread.SetState(State);
        }
    }

    void Attach()
    {
        Detach();
        boundRunner = Runner;
        boundSession = Session;
        boundTools = Tools;

        if (Runner is not null)
        {
            runner = Runner;
            ownsRunner = false;
            AddTools(runner, Tools);
        }
        else if (Session is not null)
        {
            runner = new AgentRunner(Session, tools: Tools ?? new AgentToolRegistry());
            ownsRunner = true;
        }
        if (runner is null)
            return;

        runner.Thread.Changed += OnThreadChanged;
        runner.BusyChanged += OnBusyChanged;
        var catalogs = Catalogs ?? new A2UICatalogRegistry().Add(A2UICatalog.CreateShiny());
        binding = new A2UIAgentBinding(runner, new A2UISurfaceHost(catalogs));
        lastState = runner.Thread.State;
    }

    void AddTools(AgentRunner target, AgentToolRegistry? tools)
    {
        boundTools = tools;
        if (tools is null || ReferenceEquals(tools, target.Tools))
            return;
        foreach (var tool in tools.Tools)
            target.Tools.Add(tool);
    }

    void Detach()
    {
        if (runner is null)
            return;
        runner.Thread.Changed -= OnThreadChanged;
        runner.BusyChanged -= OnBusyChanged;
        binding?.Dispose();
        binding = null;
        if (ownsRunner)
            runner.Dispose();
        runner = null;
        interruptContext = null;
        interruptSource = null;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        try
        {
            var m = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Shiny.Blazor.Controls.Agents/agent-view.js");
            if (disposed)
            {
                await m.DisposeAsync();
                return;
            }
            module = m;
            await module.InvokeVoidAsync("init", scrollRef, inputRef);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (InvalidOperationException)
        {
            // prerendering: no JS yet
        }
        catch (JSException)
        {
            // the module failed to load: the conversation still works, it just doesn't follow the stream
        }
    }

    // ── events ──────────────────────────────────────────────────────────────────

    void OnThreadChanged(object? sender, AgentThreadChange change)
    {
        if (change.Kind == AgentThreadChangeKind.StateChanged && runner is not null)
        {
            var state = runner.Thread.State;
            if (!ReferenceEquals(state, lastState))
            {
                lastState = state;
                _ = InvokeAsync(() => StateChanged.InvokeAsync(state));
            }
        }

        // text streams in token by token: render on the next frame, not per token
        if (change.Kind is AgentThreadChangeKind.ItemUpdated)
            ScheduleRender();
        else
            _ = InvokeAsync(StateHasChanged);
    }

    void OnBusyChanged(object? sender, EventArgs e) => _ = InvokeAsync(StateHasChanged);

    void OnPromptsChanged(object? sender, EventArgs e) => _ = InvokeAsync(StateHasChanged);

    void ScheduleRender()
    {
        if (renderScheduled || disposed)
            return;
        renderScheduled = true;
        _ = RenderSoonAsync();
    }

    async Task RenderSoonAsync()
    {
        await Task.Delay(Frame);
        await InvokeAsync(() =>
        {
            renderScheduled = false;
            if (!disposed)
                StateHasChanged();
        });
    }

    void OnDraftInput(ChangeEventArgs e) => draft = e.Value?.ToString() ?? "";

    async Task OnComposerKeyAsync(KeyboardEventArgs e)
    {
        // Enter sends, Shift+Enter is a newline; agent-view.js cancels the browser's newline for plain Enter
        if (e.Key == "Enter" && !e.ShiftKey && !e.IsComposing)
            await SendDraftAsync();
    }

    async Task SendDraftAsync()
    {
        if (runner is null || runner.IsBusy || string.IsNullOrWhiteSpace(draft))
            return;
        var text = draft;
        draft = "";
        await SendAsync(text);
    }

    async Task FollowAsync()
    {
        if (module is null)
            return;
        try
        {
            await module.InvokeVoidAsync("scrollToEnd", scrollRef, false);
        }
        catch (JSDisconnectedException)
        {
        }
    }

    async Task JumpToLatestAsync()
    {
        if (module is null)
            return;
        try
        {
            await module.InvokeVoidAsync("scrollToEnd", scrollRef, true);
        }
        catch (JSDisconnectedException)
        {
        }
    }

    // ── helpers for the markup ──────────────────────────────────────────────────

    void Toggle(string id)
    {
        if (!expanded.Remove(id))
            expanded.Add(id);
    }

    void ToggleReasoning(string id)
    {
        if (ShowReasoning == AgentReasoningDisplay.Expanded)
        {
            if (!collapsed.Remove(id))
                collapsed.Add(id);
        }
        else
        {
            Toggle(id);
        }
    }

    void ToggleSubagent(string id)
    {
        var key = "sub:" + id;
        if (!collapsed.Remove(key))
            collapsed.Add(key);
    }

    static string ReasoningLabel(AgentReasoningItem r)
    {
        if (r.IsStreaming)
            return "Thinking…";
        if (r.StartedAt is { } start && r.EndedAt is { } end)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling((end - start).TotalSeconds));
            return "Thought for " + seconds.ToString(CultureInfo.CurrentCulture) + "s";
        }
        return "Thought";
    }

    string StepLabel(AgentStep step)
        => step.SubagentRunId is { } sub && runner?.Thread.FindSubagent(sub) is { } s ? s.Name + " › " + step.Name : step.Name;

    static string SubagentStatus(AgentSubagent s) => s.Status switch
    {
        AgentSubagentStatus.Running => "working",
        AgentSubagentStatus.Completed => "done",
        AgentSubagentStatus.Suspended => "waiting for you",
        AgentSubagentStatus.Failed => "failed",
        _ => ""
    };

    IEnumerable<AgentSubagent> SubagentsOf(string toolCallId)
        => runner?.Thread.Subagents.Where(s => s.ParentToolCallId == toolCallId) ?? [];

    // A sub-agent with no tool call to hang under draws where its first item is.
    AgentSubagent? OrphanSubagentStartsAt(string subagentRunId, AgentItem item)
    {
        var thread = runner!.Thread;
        var sub = thread.FindSubagent(subagentRunId);
        if (sub is null)
            return null;
        if (sub.ParentToolCallId is { } parent && thread.FindToolCall(parent) is not null)
            return null;
        if (sub.ParentSubagentRunId is not null)
            return null;
        return thread.Items.First(i => i.SubagentRunId == subagentRunId) == item ? sub : null;
    }

    bool IsStreamingText()
    {
        var items = runner!.Thread.Items;
        if (items.Count == 0)
            return false;
        return items[^1] switch
        {
            AgentMessageItem m => m.IsStreaming,
            AgentReasoningItem r => r.IsStreaming,
            AgentToolCallItem t => !t.HasResult,
            _ => false
        } || Prompts?.Pending.Count > 0;
    }

    bool IsLastError(AgentErrorItem error) => runner!.Thread.Items.LastOrDefault(i => i is AgentErrorItem) == error;

    AgentInterruptContext? InterruptContext()
    {
        var pending = runner!.Thread.PendingInterrupts;
        if (pending.Count == 0)
            return null;
        // keep the same context (and the answers in it) until the agent sends a new set
        if (!ReferenceEquals(pending, interruptSource) || interruptContext is null)
        {
            interruptSource = pending;
            interruptContext = new AgentInterruptContext(runner, pending.Select(i => new AgentInterruptRequest(i)).ToList());
        }
        return interruptContext;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        disposed = true;
        if (boundPrompts is not null)
            boundPrompts.Changed -= OnPromptsChanged;
        Detach();
        if (module is not null)
        {
            try
            {
                await module.InvokeVoidAsync("dispose", scrollRef);
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
        GC.SuppressFinalize(this);
    }
}

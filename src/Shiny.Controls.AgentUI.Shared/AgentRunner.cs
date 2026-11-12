using System.Text.Json;
using System.Text.Json.Nodes;
using AGUI.Abstractions;

namespace Shiny.Controls.AgentUI;

/// <summary>Raised just before a run's input goes out, so contributors can add to it.</summary>
public sealed class AgentRunPreparingEventArgs(RunAgentInput input, JsonObject forwardedProperties) : EventArgs
{
    /// <summary>The input about to be sent. Messages, tools, context and state are already filled in.</summary>
    public RunAgentInput Input { get; } = input;

    /// <summary>Merged into <see cref="RunAgentInput.ForwardedProperties"/> once every handler has run.</summary>
    public JsonObject ForwardedProperties { get; } = forwardedProperties;
}

/// <summary>
/// Drives a conversation: sends the user's turn, streams the run into the <see cref="AgentThread"/>,
/// runs any frontend tools the agent called and starts the follow-up run, and resumes interrupts.
/// Both hosts' <c>AgentView</c> sit on one of these.
/// </summary>
/// <remarks>
/// UI-affine by design: it never leaves its caller's synchronization context, so every event is
/// applied to the thread (and every tool handler runs) on the UI thread when it is called from
/// there - the MAUI main thread or the Blazor renderer's context. No marshalling is needed.
/// </remarks>
public sealed class AgentRunner : IDisposable
{
    CancellationTokenSource? runCts;
    CancellationTokenSource? turnCts;

    /// <summary>Creates a runner over a session, optionally continuing an existing thread and tool set.</summary>
    public AgentRunner(IAgentSession session, AgentThread? thread = null, AgentToolRegistry? tools = null)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Thread = thread ?? new AgentThread(session.ThreadId);
        Tools = tools ?? new AgentToolRegistry();
    }

    /// <summary>The connection to the agent.</summary>
    public IAgentSession Session { get; }

    /// <summary>The conversation the runs fold into.</summary>
    public AgentThread Thread { get; }

    /// <summary>The frontend tools offered to the agent on every run.</summary>
    public AgentToolRegistry Tools { get; }

    /// <summary>Context items sent with every run (what the user is looking at, preferences, ...).</summary>
    public IList<AGUIContext> Context { get; } = new List<AGUIContext>();

    /// <summary>How many frontend-tool round trips one turn may take before the runner stops following up. Default 10.</summary>
    public int MaxToolRounds { get; set; } = 10;

    /// <summary>
    /// Send activity messages (A2UI surfaces and other widgets) back to the agent with the history.
    /// Off by default: the official AG-UI .NET server (<c>AGUI.Server</c> 1.0.1) fails a run whose
    /// input contains the <c>activity</c> role, and a surface's data reaches the agent with its
    /// actions anyway. Turn it on for agents that read activity history.
    /// </summary>
    public bool SendActivityMessages { get; set; }

    /// <summary>
    /// Send reasoning messages (including encrypted reasoning, so the model can continue it) back to
    /// the agent with the history. Off by default for the same reason as <see cref="SendActivityMessages"/>:
    /// <c>AGUI.Server</c> 1.0.1 fails a run whose input contains the <c>reasoning</c> role. Turn it on for
    /// agents that accept it (the TypeScript and Python SDKs do).
    /// </summary>
    public bool SendReasoningMessages { get; set; }

    /// <summary>True from the moment a turn starts until its last run (including tool follow-ups) ends.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>The exception that failed the last run, when it failed outside the protocol (a transport error).</summary>
    public Exception? LastException { get; private set; }

    /// <summary>Raised before every run's input goes out.</summary>
    public event EventHandler<AgentRunPreparingEventArgs>? RunPreparing;

    /// <summary>Raised when <see cref="IsBusy"/> changes.</summary>
    public event EventHandler? BusyChanged;

    /// <summary>Restores the conversation from an <see cref="IAgentHistorySession"/>. A no-op for other sessions.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (Session is not IAgentHistorySession history)
            return;

        var messages = await history.LoadAsync(cancellationToken);
        if (messages is { Count: > 0 })
            Thread.Apply(new MessagesSnapshotEvent { Messages = messages });
    }

    /// <summary>Sends a text turn.</summary>
    public Task SendAsync(string text, CancellationToken cancellationToken = default)
        => SendAsync(new AGUIUserMessage { Id = NewId(), Content = new AGUIContent(text) }, cancellationToken);

    /// <summary>Sends a user turn (text or multimodal).</summary>
    public Task SendAsync(AGUIUserMessage message, CancellationToken cancellationToken = default)
    {
        EnsureIdle();
        Thread.AddLocalMessage(message);
        return TurnAsync(null, null, cancellationToken);
    }

    /// <summary>
    /// Starts a run with no new message - for a UI action the agent should react to (an A2UI button,
    /// a state edit). <paramref name="forwardedProperties"/> are merged into this run's input only.
    /// </summary>
    public Task RunAsync(JsonObject? forwardedProperties = null, CancellationToken cancellationToken = default)
    {
        EnsureIdle();
        return TurnAsync(forwardedProperties, null, cancellationToken);
    }

    /// <summary>
    /// Runs the agent again on the conversation as it stands, with no new message: the Retry after a
    /// failed or cancelled run. The failed run's error row stays in the thread as history.
    /// </summary>
    public Task RetryAsync(CancellationToken cancellationToken = default) => RunAsync(null, cancellationToken);

    /// <summary>Answers the interrupts the last run stopped on, and continues it.</summary>
    public Task ResumeAsync(IEnumerable<AGUIResume> responses, CancellationToken cancellationToken = default)
    {
        EnsureIdle();
        var list = responses.ToList();
        if (list.Count == 0)
            throw new ArgumentException("Resume needs at least one interrupt response.", nameof(responses));
        return TurnAsync(null, list, cancellationToken);
    }

    /// <summary>Approves or rejects every pending interrupt with the same payload.</summary>
    public Task ResumeAllAsync(bool approved, JsonElement? payload = null, CancellationToken cancellationToken = default)
        => ResumeAsync(Thread.PendingInterrupts.Select(i => new AGUIResume
        {
            InterruptId = i.Id,
            Status = approved ? ResumeStatus.Resolved : ResumeStatus.Cancelled,
            Payload = payload
        }), cancellationToken);

    /// <summary>
    /// Stops the current turn: the stream in progress, or a frontend tool waiting on the user between
    /// runs (its prompt is withdrawn and the call fails with "The user cancelled."). The thread is marked cancelled.
    /// </summary>
    public void Cancel()
    {
        turnCts?.Cancel();
        runCts?.Cancel();
    }

    /// <summary>Cancels any run in progress.</summary>
    public void Dispose()
    {
        turnCts?.Cancel();
        runCts?.Cancel();
        runCts?.Dispose();
        runCts = null;
    }

    async Task TurnAsync(JsonObject? forwarded, IList<AGUIResume>? resume, CancellationToken cancellationToken)
    {
        SetBusy(true);
        // one source for the whole turn, so Cancel also reaches a frontend tool waiting between runs
        using var turn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        turnCts = turn;
        var ct = turn.Token;
        try
        {
            var completed = await RunOnceAsync(forwarded, resume, ct);
            for (var round = 0; completed && round < MaxToolRounds; round++)
            {
                if (!await RunFrontendToolsAsync(ct))
                    break;
                completed = await RunOnceAsync(null, null, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // a frontend tool was stopped mid-prompt; its call already carries the error
            Thread.MarkTurnCancelled();
        }
        finally
        {
            turnCts = null;
            SetBusy(false);
        }
    }

    // Returns true when the run completed normally (so tool follow-ups may continue).
    async Task<bool> RunOnceAsync(JsonObject? forwarded, IList<AGUIResume>? resume, CancellationToken cancellationToken)
    {
        runCts?.Dispose();
        runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = runCts.Token;
        LastException = null;

        var input = new RunAgentInput
        {
            ThreadId = Session.ThreadId,
            RunId = NewId(),
            Messages = Thread.ToAGUIMessages(SendActivityMessages, SendReasoningMessages),
            Tools = Tools.ToAGUITools(),
            Context = [.. Context],
            State = Thread.State is null ? null : AgentThread.ToElement(Thread.State),
            Resume = resume
        };

        var props = forwarded?.DeepClone() as JsonObject ?? new JsonObject();
        RunPreparing?.Invoke(this, new AgentRunPreparingEventArgs(input, props));
        if (props.Count > 0)
            input.ForwardedProperties = AgentThread.ToElement(props);

        try
        {
            // no ConfigureAwait(false): events must land on the caller's (UI) context
            await foreach (var e in Session.RunAsync(input, ct).WithCancellation(ct))
                Thread.Apply(e);

            if (Thread.RunStatus == AgentRunStatus.Running)
                Thread.MarkFailed("The agent stream ended without finishing the run.", "STREAM_ENDED");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Thread.MarkCancelled();
            return false;
        }
        catch (Exception ex)
        {
            LastException = ex;
            Thread.MarkFailed(ex.Message, ex.GetType().Name);
            return false;
        }
        return Thread.RunStatus == AgentRunStatus.Completed;
    }

    // Runs every frontend tool call the last run left waiting. Returns true when it ran at least one.
    async Task<bool> RunFrontendToolsAsync(CancellationToken cancellationToken)
    {
        var runId = Thread.RunId;
        var pending = Thread.Items
            .OfType<AgentToolCallItem>()
            .Where(t => t.Status == AgentToolCallStatus.Ready
                        && Tools.Contains(t.Name)
                        && (t.RunId == runId || Thread.PendingToolCallIds.Contains(t.ToolCallId)))
            .ToList();

        foreach (var call in pending)
        {
            Tools.TryGet(call.Name, out var tool);
            Thread.SetToolRunning(call);
            try
            {
                var args = call.Arguments ?? AgentTool.OpenObjectSchema;
                var result = await tool.InvokeAsync(new AgentToolInvocation(call.ToolCallId, call.Name, args, Thread), cancellationToken);
                Thread.SetToolResult(call.ToolCallId, result.Content, result.Error);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Thread.SetToolResult(call.ToolCallId, "", "The user cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                Thread.SetToolResult(call.ToolCallId, "", ex.Message);
            }
        }
        return pending.Count > 0;
    }

    void EnsureIdle()
    {
        if (IsBusy)
            throw new InvalidOperationException("A run is already in progress. Cancel it or wait for it to finish.");
    }

    void SetBusy(bool busy)
    {
        if (IsBusy == busy)
            return;
        IsBusy = busy;
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static string NewId() => Guid.NewGuid().ToString("N");
}

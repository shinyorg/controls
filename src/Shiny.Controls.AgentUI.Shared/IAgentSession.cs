using AGUI.Abstractions;

namespace Shiny.Controls.AgentUI;

/// <summary>
/// The connection between an agent control and an agent. One call to <see cref="RunAsync"/> is one
/// AG-UI run: the input goes out, the event stream comes back.
/// </summary>
/// <remarks>
/// The controls never see an HTTP client, an <c>IChatClient</c> or a model, only this. The shape
/// matches the AG-UI SDK's <c>IAGUITransport.SendAsync</c>, so wrapping it is one line
/// (<c>AgentSession.Create(threadId, transport.SendAsync)</c>), and Shiny.AiConversation implements
/// it over its own session store.
/// <para>
/// <see cref="AgentRunner"/> fills the input with the whole conversation (<see cref="AgentThread.ToAGUIMessages"/>),
/// the frontend tools, the context, the shared state and any resume. A session that keeps its own
/// history may replace <see cref="RunAgentInput.Messages"/> with it.
/// </para>
/// </remarks>
public interface IAgentSession
{
    /// <summary>The AG-UI thread this session talks on.</summary>
    string ThreadId { get; }

    /// <summary>Starts a run and streams its events. Cancelling the token ends the run.</summary>
    IAsyncEnumerable<BaseEvent> RunAsync(RunAgentInput input, CancellationToken cancellationToken = default);
}

/// <summary>A session that can restore an earlier conversation when a control opens on it.</summary>
public interface IAgentHistorySession : IAgentSession
{
    /// <summary>The thread's messages so far, or null when there are none.</summary>
    Task<IList<AGUIMessage>?> LoadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Helpers for building sessions.</summary>
public static class AgentSession
{
    /// <summary>Wraps a run delegate (such as <c>IAGUITransport.SendAsync</c>) as a session.</summary>
    public static IAgentSession Create(string threadId, Func<RunAgentInput, CancellationToken, IAsyncEnumerable<BaseEvent>> run)
        => new DelegateAgentSession(threadId, run);

    sealed class DelegateAgentSession(string threadId, Func<RunAgentInput, CancellationToken, IAsyncEnumerable<BaseEvent>> run) : IAgentSession
    {
        public string ThreadId { get; } = threadId;

        public IAsyncEnumerable<BaseEvent> RunAsync(RunAgentInput input, CancellationToken cancellationToken = default)
            => run(input, cancellationToken);
    }
}

using System.Runtime.CompilerServices;
using AGUI.Abstractions;
using AGUI.Formatting;

namespace Shiny.Controls.AgentUI.Tests;

static class Fixtures
{
    public static string PathOf(params string[] parts) => Path.Combine([AppContext.BaseDirectory, "Fixtures", .. parts]);

    /// <summary>Decodes a recorded AG-UI stream with the SDK's own SSE reader.</summary>
    public static async Task<List<BaseEvent>> LoadAsync(string name)
    {
        var list = new List<BaseEvent>();
        await using var stream = File.OpenRead(PathOf("agui", name + ".sse"));
        await foreach (var e in new SseEventStreamFormatter().ReadAsync(stream, default))
            list.Add(e);
        return list;
    }

    public static async Task<AgentThread> ReplayAsync(string name)
    {
        var thread = new AgentThread();
        foreach (var e in await LoadAsync(name))
            thread.Apply(e);
        return thread;
    }
}

/// <summary>A session that answers each run with the next scripted stream and records what it was sent.</summary>
sealed class ScriptedSession(params Func<RunAgentInput, IEnumerable<BaseEvent>>[] runs) : IAgentSession
{
    int next;

    public string ThreadId => "thread-1";

    public List<RunAgentInput> Inputs { get; } = [];

    public async IAsyncEnumerable<BaseEvent> RunAsync(RunAgentInput input, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Inputs.Add(input);
        if (next >= runs.Length)
            throw new InvalidOperationException($"No scripted run #{next + 1}.");

        foreach (var e in runs[next++](input))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return e;
        }
    }

    public static ScriptedSession FromFixtures(params List<BaseEvent>[] streams)
        => new(streams.Select(s => (Func<RunAgentInput, IEnumerable<BaseEvent>>)(_ => s)).ToArray());
}

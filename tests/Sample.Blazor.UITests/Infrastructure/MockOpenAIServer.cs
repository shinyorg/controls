using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sample.Blazor.UITests;

/// <summary>
/// A tiny OpenAI-compatible endpoint on 127.0.0.1 for the agent page's "Your key" source: it answers
/// the browser's CORS preflight and streams <c>POST /v1/chat/completions</c> back as
/// <c>chat.completion.chunk</c> SSE. A prompt containing "ui" gets a <c>render_ui</c> tool call whose
/// arguments draw a small A2UI surface; the request that carries the tool's result gets a closing
/// line of text; anything else gets a plain reply streamed in two halves, the second held back until
/// the test calls <see cref="Release"/> - which is how a test proves the browser really streams.
/// </summary>
public sealed class MockOpenAIServer : IAsyncDisposable
{
    public const string PlainFirstHalf = "Streaming from the mock, ";
    public const string PlainSecondHalf = "and that is the whole reply.";
    public const string SurfaceText = "Mock surface says hi";
    public const string AfterToolText = "There's your surface from the mock.";

    readonly HttpListener listener = new();
    readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly CancellationTokenSource stop = new();
    readonly Task loop;

    public MockOpenAIServer()
    {
        Port = FreePort();
        listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        listener.Start();
        loop = Task.Run(AcceptAsync);
    }

    public int Port { get; }

    /// <summary>The base URL to give the OpenAI client (it appends <c>/chat/completions</c>).</summary>
    public string BaseUrl => $"http://127.0.0.1:{Port}/v1";

    /// <summary>The <c>Authorization</c> header of every completion request, in order.</summary>
    public ConcurrentQueue<string?> Authorizations { get; } = new();

    /// <summary>The body of every completion request, in order.</summary>
    public ConcurrentQueue<JsonObject> Requests { get; } = new();

    /// <summary>Lets the held-back second half of the plain reply go.</summary>
    public void Release() => release.TrySetResult();

    async Task AcceptAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch
            {
                return;
            }
            _ = Task.Run(() => HandleAsync(context));
        }
    }

    async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        try
        {
            response.AddHeader("Access-Control-Allow-Origin", "*");
            if (request.HttpMethod == "OPTIONS")
            {
                response.AddHeader("Access-Control-Allow-Methods", "POST, OPTIONS");
                response.AddHeader("Access-Control-Allow-Headers", request.Headers["Access-Control-Request-Headers"] ?? "*");
                response.AddHeader("Access-Control-Max-Age", "600");
                response.StatusCode = 204;
                response.Close();
                return;
            }

            if (request.HttpMethod != "POST" || request.Url?.AbsolutePath != "/v1/chat/completions")
            {
                response.StatusCode = 404;
                response.Close();
                return;
            }

            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            var body = JsonNode.Parse(await reader.ReadToEndAsync()) as JsonObject ?? [];
            Authorizations.Enqueue(request.Headers["Authorization"]);
            Requests.Enqueue(body);

            response.StatusCode = 200;
            response.ContentType = "text/event-stream";
            response.AddHeader("Cache-Control", "no-cache");
            response.SendChunked = true;
            var output = response.OutputStream;

            var messages = body["messages"] as JsonArray ?? [];
            var lastRole = messages.LastOrDefault()?["role"]?.GetValue<string>();
            var lastUser = messages.LastOrDefault(m => m?["role"]?.GetValue<string>() == "user")?["content"];
            var prompt = (lastUser is JsonValue v ? v.GetValue<string>() : lastUser?.ToJsonString() ?? "").ToLowerInvariant();

            if (lastRole == "tool")
            {
                await TextAsync(output, AfterToolText);
            }
            else if (prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("ui"))
            {
                await ToolCallAsync(output);
            }
            else
            {
                await WriteAsync(output, Chunk(new JsonObject { ["role"] = "assistant", ["content"] = PlainFirstHalf }));
                // held until the test has seen the first half on screen
                await Task.WhenAny(release.Task, Task.Delay(TimeSpan.FromSeconds(30), stop.Token));
                await WriteAsync(output, Chunk(new JsonObject { ["content"] = PlainSecondHalf }));
                await WriteAsync(output, Chunk(new JsonObject(), "stop"));
                await DoneAsync(output);
            }
            response.Close();
        }
        catch
        {
            try { response.Abort(); } catch { }
        }
    }

    async Task TextAsync(Stream output, string text)
    {
        var first = true;
        foreach (var word in text.Split(' '))
        {
            var delta = new JsonObject { ["content"] = (first ? "" : " ") + word };
            if (first)
                delta["role"] = "assistant";
            first = false;
            await WriteAsync(output, Chunk(delta));
        }
        await WriteAsync(output, Chunk(new JsonObject(), "stop"));
        await DoneAsync(output);
    }

    async Task ToolCallAsync(Stream output)
    {
        var operations = new JsonArray(new JsonObject
        {
            ["version"] = "1.0",
            ["createSurface"] = new JsonObject
            {
                ["surfaceId"] = "mock",
                ["catalogId"] = "https://a2ui.org/specification/v1_0/catalogs/basic/catalog.json",
                ["dataModel"] = new JsonObject { ["greeting"] = SurfaceText },
                ["components"] = new JsonArray(
                    new JsonObject { ["id"] = "root", ["component"] = "Card", ["child"] = "body" },
                    new JsonObject { ["id"] = "body", ["component"] = "Column", ["children"] = new JsonArray("t") },
                    new JsonObject { ["id"] = "t", ["component"] = "Text", ["variant"] = "h4", ["text"] = new JsonObject { ["@path"] = "/greeting" } })
            }
        });
        var arguments = new JsonObject { ["operations"] = operations }.ToJsonString();

        // the arguments arrive in two pieces, as a real model streams them
        var split = arguments.Length / 2;
        await WriteAsync(output, Chunk(new JsonObject
        {
            ["role"] = "assistant",
            ["tool_calls"] = new JsonArray(new JsonObject
            {
                ["index"] = 0,
                ["id"] = "call_mock_1",
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = "render_ui", ["arguments"] = arguments[..split] }
            })
        }));
        await WriteAsync(output, Chunk(new JsonObject
        {
            ["tool_calls"] = new JsonArray(new JsonObject
            {
                ["index"] = 0,
                ["function"] = new JsonObject { ["arguments"] = arguments[split..] }
            })
        }));
        await WriteAsync(output, Chunk(new JsonObject(), "tool_calls"));
        await DoneAsync(output);
    }

    int chunks;

    string Chunk(JsonObject delta, string? finishReason = null) => new JsonObject
    {
        ["id"] = "chatcmpl-mock",
        ["object"] = "chat.completion.chunk",
        ["created"] = 1_760_000_000 + Interlocked.Increment(ref chunks),
        ["model"] = "mock-model",
        ["choices"] = new JsonArray(new JsonObject
        {
            ["index"] = 0,
            ["delta"] = delta,
            ["finish_reason"] = finishReason
        })
    }.ToJsonString();

    static async Task WriteAsync(Stream output, string json)
    {
        var bytes = Encoding.UTF8.GetBytes("data: " + json + "\n\n");
        await output.WriteAsync(bytes);
        await output.FlushAsync();
        // a little pacing, so the browser sees separate network reads
        await Task.Delay(40);
    }

    static Task DoneAsync(Stream output) => output.WriteAsync(Encoding.UTF8.GetBytes("data: [DONE]\n\n")).AsTask();

    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        release.TrySetResult();
        stop.Cancel();
        try { listener.Stop(); listener.Close(); } catch { }
        try { await loop; } catch { }
        stop.Dispose();
    }
}

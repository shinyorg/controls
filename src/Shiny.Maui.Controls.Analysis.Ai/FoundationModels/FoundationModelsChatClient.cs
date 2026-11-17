using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Shiny.Maui.Controls.Analysis.Ai;

/// <summary>
/// A Microsoft.Extensions.AI <see cref="IChatClient"/> over Apple's on-device Foundation Models (the Apple
/// Intelligence model). It runs on the device, offline and free, so it can stand in for a cloud vision model
/// wherever an <see cref="IChatClient"/> is taken — <see cref="AiDocumentAnalyzer"/> included.
/// </summary>
/// <remarks>
/// Text needs iOS / Mac Catalyst / macOS 26 on an Apple Intelligence device with Apple Intelligence turned on;
/// images (<see cref="DataContent"/> with an <c>image/*</c> media type) and the Vision tools need 27. Check
/// <see cref="Availability"/> before choosing it, and fall back to another client when it is not
/// <see cref="FoundationModelsAvailability.Available"/>. On every other platform the type exists so shared code
/// compiles, reports <see cref="FoundationModelsAvailability.UnsupportedPlatform"/>, and throws
/// <see cref="PlatformNotSupportedException"/> if called.
/// <para>
/// Structured output (<c>GetResponseAsync&lt;T&gt;</c>) maps onto the model's guided generation, so the reply is
/// constrained to the schema rather than merely asked for. Schemas that Foundation Models cannot express fall
/// back to asking for JSON in the instructions. <see cref="AIFunction"/> tools are not supported yet; Apple's
/// own Vision tools are switched on through <see cref="FoundationModelsChatClientOptions"/>.
/// </para>
/// <para>
/// The first request after launch loads the model, which takes several seconds; call <see cref="Prewarm"/>
/// when the screen that will use it appears.
/// </para>
/// </remarks>
public sealed partial class FoundationModelsChatClient : IChatClient
{
    /// <summary>The model id reported on responses and in <see cref="ChatClientMetadata"/>.</summary>
    public const string ModelId = "apple-foundation-models";

    readonly FoundationModelsChatClientOptions options;
    readonly ChatClientMetadata metadata = new("apple", null, ModelId);

    /// <param name="options">Tools and model use case; <c>null</c> for the defaults.</param>
    public FoundationModelsChatClient(FoundationModelsChatClientOptions? options = null)
    {
        this.options = options ?? new FoundationModelsChatClientOptions();
    }

    /// <summary>Whether the on-device model can be used right now, and if not, why.</summary>
    public static FoundationModelsAvailability Availability => GetAvailabilityCore();

    /// <summary><c>true</c> when <see cref="Availability"/> is <see cref="FoundationModelsAvailability.Available"/>.</summary>
    public static bool IsAvailable => Availability == FoundationModelsAvailability.Available;

    /// <summary>
    /// Loads the model ahead of the first request, so that request does not pay the cold start. Returns at once;
    /// the loading happens in the background. Does nothing when the model is unavailable.
    /// </summary>
    /// <param name="instructions">The instructions the coming requests will use, if known.</param>
    public static void Prewarm(string? instructions = null)
    {
        if (IsAvailable)
            PrewarmCore(instructions);
    }

    /// <inheritdoc/>
    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var request = FoundationModelsRequestBuilder.Build(messages, options, this.options, false);
        var text = await this.RespondCoreAsync(request, null, cancellationToken).ConfigureAwait(false);
        if (request.JsonInPrompt)
            text = FoundationModelsRequestBuilder.StripCodeFence(text);

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            ModelId = ModelId,
            CreatedAt = DateTimeOffset.UtcNow,
            FinishReason = ChatFinishReason.Stop
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Plain text streams as it is generated. A structured (JSON) reply arrives as one update once it is complete,
    /// because partial JSON from guided generation is not an append-only stream of text.
    /// </remarks>
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = FoundationModelsRequestBuilder.Build(messages, options, this.options, true);
        var responseId = Guid.NewGuid().ToString("N");

        if (!request.Payload.Stream)
        {
            var whole = await this.RespondCoreAsync(request, null, cancellationToken).ConfigureAwait(false);
            if (request.JsonInPrompt)
                whole = FoundationModelsRequestBuilder.StripCodeFence(whole);
            yield return this.Update(responseId, whole, ChatFinishReason.Stop);
            yield break;
        }

        // the bridge reports cumulative snapshots; the channel carries the new tail of each one
        var channel = System.Threading.Channels.Channel.CreateUnbounded<string>(new() { SingleReader = true });
        var sent = 0;

        // cancelled when the caller stops enumerating early, so the model does not keep generating for nobody
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var task = this.RespondCoreAsync(request, snapshot =>
        {
            var start = snapshot.Length >= sent ? sent : 0;
            if (snapshot.Length > start)
                channel.Writer.TryWrite(snapshot[start..]);
            sent = snapshot.Length;
        }, stop.Token);

        _ = task.ContinueWith(t => channel.Writer.TryComplete(t.Exception?.InnerException), TaskScheduler.Default);

        try
        {
            await foreach (var delta in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return this.Update(responseId, delta, null);

            await task.ConfigureAwait(false); // surfaces cancellation / model errors
            yield return this.Update(responseId, null, ChatFinishReason.Stop);
        }
        finally
        {
            if (!task.IsCompleted)
                stop.Cancel();
        }
    }

    ChatResponseUpdate Update(string responseId, string? text, ChatFinishReason? finish) => new(ChatRole.Assistant, text)
    {
        ResponseId = responseId,
        ModelId = ModelId,
        CreatedAt = DateTimeOffset.UtcNow,
        FinishReason = finish
    };

    /// <inheritdoc/>
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is not null)
            return null;
        if (serviceType == typeof(ChatClientMetadata))
            return this.metadata;
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    private static partial FoundationModelsAvailability GetAvailabilityCore();
    private static partial void PrewarmCore(string? instructions);
    private partial Task<string> RespondCoreAsync(FoundationModelsRequest request, Action<string>? onSnapshot, CancellationToken cancellationToken);
}


/// <summary>Options for <see cref="FoundationModelsChatClient"/>.</summary>
public sealed class FoundationModelsChatClientOptions
{
    /// <summary>
    /// Give the model Apple's Vision <c>OCRTool</c>, which it calls to read the text in an attached image with the
    /// Vision text recognizer. The model already reads images itself; the tool helps with dense or small text, at
    /// the cost of roughly a second per request. iOS / macOS 27 and later. Default off.
    /// </summary>
    public bool UseOcrTool { get; set; }

    /// <summary>
    /// Give the model Apple's Vision <c>BarcodeReaderTool</c>, so it can decode barcodes and QR codes in an
    /// attached image. iOS / macOS 27 and later. Default off.
    /// </summary>
    public bool UseBarcodeReaderTool { get; set; }

    /// <summary>The model variant to use. Default <see cref="FoundationModelsUseCase.General"/>.</summary>
    public FoundationModelsUseCase UseCase { get; set; } = FoundationModelsUseCase.General;
}


/// <summary>The specialised variants of the on-device model.</summary>
public enum FoundationModelsUseCase
{
    /// <summary>The general purpose model.</summary>
    General,

    /// <summary>The variant tuned for tagging, topic detection and entity extraction.</summary>
    ContentTagging
}


/// <summary>Whether the on-device model can be used. The values match the native bridge's codes.</summary>
public enum FoundationModelsAvailability
{
    /// <summary>Ready to use.</summary>
    Available = 0,

    /// <summary>The device does not support Apple Intelligence.</summary>
    DeviceNotEligible = 1,

    /// <summary>The device supports it, but the user has not turned Apple Intelligence on.</summary>
    AppleIntelligenceNotEnabled = 2,

    /// <summary>Apple Intelligence is on, but the model is still downloading or otherwise not ready.</summary>
    ModelNotReady = 3,

    /// <summary>The OS is older than iOS / Mac Catalyst / macOS 26.</summary>
    UnsupportedOperatingSystem = 4,

    /// <summary>The model reported a reason this version does not recognise.</summary>
    Unknown = 5,

    /// <summary>Not an Apple platform (Android, Windows, plain .NET).</summary>
    UnsupportedPlatform = 100
}


/// <summary>Why a <see cref="FoundationModelsChatClient"/> request failed. The values match the native bridge's codes.</summary>
public enum FoundationModelsErrorKind
{
    /// <summary>The model or an asset it needs is unavailable.</summary>
    Unavailable = 1,

    /// <summary>Apple's safety guardrails rejected the prompt or the reply.</summary>
    GuardrailViolation = 2,

    /// <summary>The conversation is longer than the model's context window.</summary>
    ContextSizeExceeded = 3,

    /// <summary>The prompt's language or the device locale is not supported by the model.</summary>
    UnsupportedLanguage = 4,

    /// <summary>The request could not be expressed for the model (an image it cannot decode, a schema it cannot take, an OS too old for images or tools).</summary>
    BadRequest = 6,

    /// <summary>The model declined to answer.</summary>
    Refusal = 7,

    /// <summary>The system is limiting requests (for example a background app making too many).</summary>
    RateLimited = 8,

    /// <summary>Any other failure; see the message.</summary>
    Other = 9
}


/// <summary>A <see cref="FoundationModelsChatClient"/> request the on-device model could not complete.</summary>
public sealed class FoundationModelsException(FoundationModelsErrorKind kind, string message) : Exception(message)
{
    /// <summary>Why it failed.</summary>
    public FoundationModelsErrorKind Kind { get; } = kind;
}

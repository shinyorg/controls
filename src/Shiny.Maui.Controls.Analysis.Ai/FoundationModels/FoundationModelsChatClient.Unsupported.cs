#if !(IOS || MACCATALYST || MACOS)
namespace Shiny.Maui.Controls.Analysis.Ai;

// Android, Windows and plain .NET: the type exists so shared code compiles, but there is no model to talk to.
public sealed partial class FoundationModelsChatClient
{
    private static partial FoundationModelsAvailability GetAvailabilityCore() => FoundationModelsAvailability.UnsupportedPlatform;

    private static partial void PrewarmCore(string? instructions)
    {
    }

    private partial Task<string> RespondCoreAsync(FoundationModelsRequest request, Action<string>? onSnapshot, CancellationToken cancellationToken)
        => throw new PlatformNotSupportedException("Apple Foundation Models is only available on iOS, Mac Catalyst and macOS.");
}
#endif

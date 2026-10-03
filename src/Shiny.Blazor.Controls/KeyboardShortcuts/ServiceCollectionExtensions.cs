using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

public static class KeyboardShortcutServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IKeyboardShortcutService"/>, which <see cref="KeyboardShortcuts"/> and
    /// <see cref="KeyboardShortcut"/> need. Also covered by <c>AddShinyControls()</c>; calling both
    /// is safe.
    /// </summary>
    /// <remarks>
    /// <b>Scoped, not singleton.</b> The service owns a JS module, a <c>DotNetObjectReference</c> and
    /// the shortcuts of one page; a singleton under Blazor Server would let one user's keys fire
    /// another user's handlers.
    /// </remarks>
    public static IServiceCollection AddShinyKeyboardShortcuts(this IServiceCollection services)
    {
        services.TryAddScoped<IKeyboardShortcutService>(sp => new KeyboardShortcutService(
            sp.GetRequiredService<IJSRuntime>(),
            sp.GetService<ILogger<KeyboardShortcutService>>()
        ));
        return services;
    }
}

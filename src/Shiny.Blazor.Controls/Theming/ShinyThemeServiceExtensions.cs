using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Shiny.Blazor.Controls.Theming;

public static class ShinyThemeServiceExtensions
{
    /// <summary>
    /// Registers the theme service. Also covered by <c>AddShinyControls()</c> — calling both is safe.
    /// </summary>
    /// <remarks>
    /// <b>Scoped, not singleton.</b> The chosen pack, scheme and token overrides are per-user state.
    /// Under WebAssembly the two are indistinguishable, so a singleton would only bite on Blazor
    /// Server — where every connected user would share whichever theme the last one picked.
    /// </remarks>
    public static IServiceCollection AddShinyTheme(this IServiceCollection services)
    {
        services.TryAddScoped<ShinyThemeService>();
        services.TryAddScoped<IShinyThemeService>(sp => sp.GetRequiredService<ShinyThemeService>());
        return services;
    }
}

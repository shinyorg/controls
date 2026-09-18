using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Shiny.Blazor.Controls.Camera.Media;

public static class MediaServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IMediaService"/>. Render one <c>&lt;MediaHost /&gt;</c> in your layout as well — it
    /// draws the modal camera. Safe to call more than once.
    /// </summary>
    /// <remarks>
    /// <b>Scoped, not singleton</b>, like every Shiny Blazor service holding per-user UI state: a singleton would
    /// open one user's camera modal on every connected user's screen under Blazor Server.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Service-wide defaults — "our photos are 85% JPEG capped at 2048px".</param>
    public static IServiceCollection AddShinyMediaService(this IServiceCollection services, Action<MediaServiceOptions>? configure = null)
    {
        services.TryAddScoped(_ =>
        {
            var options = new MediaServiceOptions();
            configure?.Invoke(options);
            return options;
        });
        services.TryAddScoped<MediaService>();
        services.TryAddScoped<IMediaService>(sp => sp.GetRequiredService<MediaService>());
        return services;
    }
}

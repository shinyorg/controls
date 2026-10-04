using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

/// <summary>
/// Throws confetti over the window from code. To fire it from a click without any code, wrap the
/// element in <see cref="Confetti"/> instead. Needs no host component in the layout.
/// </summary>
public interface IConfettiService
{
    /// <summary>Fires a custom burst. Null fires canvas-confetti's defaults from the centre of the window. Completes once every particle has faded out.</summary>
    Task FireAsync(ConfettiOptions? options = null);

    /// <summary>
    /// Fires a preset. The origin is a fraction of the window (0-1 on each axis) and defaults to
    /// (0.5, 0.6); <see cref="ConfettiPreset.Fireworks"/> and <see cref="ConfettiPreset.SideCannons"/> ignore it.
    /// </summary>
    Task FireAsync(ConfettiPreset preset, double? originX = null, double? originY = null);

    /// <summary>Fires a preset from the centre of <paramref name="element"/>.</summary>
    Task FireFromAsync(ElementReference element, ConfettiPreset preset = ConfettiPreset.Burst);

    /// <summary>Fires a custom burst from the centre of <paramref name="element"/>. Its origin is replaced.</summary>
    Task FireFromAsync(ElementReference element, ConfettiOptions options);

    /// <summary>Removes everything in the air, and anything a preset still had queued.</summary>
    Task ClearAsync();
}


/// <summary>
/// The default <see cref="IConfettiService"/>. Scoped like the other Blazor control services: on
/// Blazor Server each circuit is its own browser, and a singleton would hold the first user's
/// <see cref="IJSRuntime"/>.
/// </summary>
public class ConfettiService(IJSRuntime js) : IConfettiService, IAsyncDisposable
{
    internal const string ModulePath = "./_content/Shiny.Blazor.Controls/confetti.js";
    Task<IJSObjectReference>? module;

    Task<IJSObjectReference> Module => this.module ??= js.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();

    public async Task FireAsync(ConfettiOptions? options = null)
        => await (await this.Module).InvokeVoidAsync("fire", (options ?? new ConfettiOptions()).ToJson());

    public async Task FireAsync(ConfettiPreset preset, double? originX = null, double? originY = null)
        => await (await this.Module).InvokeVoidAsync("firePreset", preset.ToString(), originX, originY);

    public async Task FireFromAsync(ElementReference element, ConfettiPreset preset = ConfettiPreset.Burst)
        => await (await this.Module).InvokeVoidAsync("fireFrom", element, preset.ToString(), null);

    public async Task FireFromAsync(ElementReference element, ConfettiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        await (await this.Module).InvokeVoidAsync("fireFrom", element, ConfettiPreset.Burst.ToString(), options.ToJson());
    }

    public async Task ClearAsync() => await (await this.Module).InvokeVoidAsync("clear");

    public async ValueTask DisposeAsync()
    {
        if (this.module is null)
            return;

        try
        {
            await (await this.module).DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        catch (JSException) { }
        catch (TaskCanceledException) { }
        GC.SuppressFinalize(this);
    }
}


public static class ConfettiServiceExtensions
{
    /// <summary>Registers <see cref="IConfettiService"/>. Also covered by <c>AddShinyControls()</c> - calling both is safe.</summary>
    public static IServiceCollection AddShinyConfetti(this IServiceCollection services)
    {
        services.TryAddScoped<IConfettiService, ConfettiService>();
        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Shiny.Controls.Gaming;

namespace Shiny;


public static class GamepadServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="VirtualGamepadManager"/> that reports on-screen
    /// <c>GamepadView</c>s and the browser's physical controllers together as
    /// <see cref="Shiny.Gamepad.IGamepadManager"/>.
    /// </summary>
    /// <remarks>
    /// <para>Call this <b>instead of</b> Shiny.Gamepad.Blazor's <c>AddGamepads()</c> - it registers the
    /// browser manager itself when none is registered, and wraps the one that is.</para>
    /// <para>Everything is Scoped, the browser manager included. On Blazor Server a singleton would
    /// share one user's on-screen pad - and their controller - with every connected user, and the
    /// browser manager needs the circuit's own <c>IJSRuntime</c>. On WebAssembly a scope lasts as long
    /// as the app, so nothing changes there.</para>
    /// <para>The view works without this registration too; it just isn't reported by
    /// <c>IGamepadManager</c>, and <c>HideWhenControllerConnected</c> has nothing to watch.</para>
    /// </remarks>
    public static IServiceCollection AddShinyGamepad(this IServiceCollection services)
        => services.AddVirtualGamepadManager(ServiceLifetime.Scoped, s => s.AddGamepads());
}

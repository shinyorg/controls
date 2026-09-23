using Microsoft.Extensions.DependencyInjection;
using Shiny.Controls.Gaming;
using Shiny.Maui.Controls.Gaming;

namespace Shiny;


public static class GamepadMauiAppBuilderExtensions
{
    /// <summary>
    /// Registers the <see cref="GamepadView"/> multi-touch handler and a
    /// <see cref="VirtualGamepadManager"/> that reports on-screen and physical controllers together as
    /// <see cref="Shiny.Gamepad.IGamepadManager"/>.
    /// </summary>
    /// <remarks>
    /// <para>Call this <b>instead of</b> Shiny.Gamepad's <c>AddGamepads()</c> - it registers the platform
    /// manager itself when none is registered, and wraps the one that is. An <c>AddGamepads()</c>
    /// called after it would replace the wrapper and the on-screen pads would disappear from
    /// <c>GetGamepads()</c> (the views themselves keep working).</para>
    /// <para>Physical controllers on Android need Shiny's hosting (<c>UseShiny()</c>), which provides the
    /// activity hooks the Android backend reads input through. Without it the on-screen pad still
    /// works and physical controllers are simply not reported.</para>
    /// </remarks>
    public static MauiAppBuilder UseShinyGamepad(this MauiAppBuilder builder)
    {
#if ANDROID || IOS
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<GamepadView, GamepadViewHandler>());
#endif
        builder.Services.AddVirtualGamepadManager(ServiceLifetime.Singleton, services =>
        {
#if ANDROID || IOS || MACCATALYST || WINDOWS
            services.AddGamepads();
#else
            services.AddNotSupportedGamepads();
#endif
        });
        return builder;
    }
}

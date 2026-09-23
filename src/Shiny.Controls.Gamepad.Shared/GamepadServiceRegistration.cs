using Microsoft.Extensions.DependencyInjection;
using Shiny.Gamepad;

namespace Shiny.Controls.Gaming;


/// <summary>The registration both hosts share: put a <see cref="VirtualGamepadManager"/> in front of the platform manager.</summary>
public static class GamepadServiceRegistration
{
    /// <summary>
    /// Registers <see cref="VirtualGamepadManager"/> as both itself and <see cref="IGamepadManager"/>,
    /// wrapping whatever <see cref="IGamepadManager"/> is already registered - or the one
    /// <paramref name="registerPhysical"/> adds when none is.
    /// </summary>
    /// <remarks>
    /// <para>The existing registration is captured and replayed inside the wrapper rather than resolved,
    /// because resolving <see cref="IGamepadManager"/> from inside its own replacement would recurse.
    /// So the wrapper must be registered after the platform manager, and a later
    /// <c>AddGamepads()</c> call would replace it again - both hosts document calling their Use/Add
    /// method <i>instead of</i> <c>AddGamepads()</c>.</para>
    /// <para>The captured registration takes on <paramref name="lifetime"/>. Blazor passes Scoped: on
    /// Blazor Server a singleton would share one user's on-screen pad with every connected user, and
    /// the browser manager needs the circuit's scoped <c>IJSRuntime</c> anyway.</para>
    /// </remarks>
    public static IServiceCollection AddVirtualGamepadManager(
        this IServiceCollection services,
        ServiceLifetime lifetime,
        Action<IServiceCollection>? registerPhysical
    )
    {
        if (services.Any(x => x.ServiceType == typeof(VirtualGamepadManager)))
            return services;

        var existing = services.LastOrDefault(x => x.ServiceType == typeof(IGamepadManager));
        if (existing == null && registerPhysical != null)
        {
            registerPhysical(services);
            existing = services.LastOrDefault(x => x.ServiceType == typeof(IGamepadManager));
        }

        if (existing != null)
            services.Remove(existing);

        var physical = existing == null ? null : Replay(existing);

        services.Add(new ServiceDescriptor(
            typeof(VirtualGamepadManager),
            sp => new VirtualGamepadManager(TryCreate(physical, sp)),
            lifetime
        ));
        services.Add(new ServiceDescriptor(
            typeof(IGamepadManager),
            sp => sp.GetRequiredService<VirtualGamepadManager>(),
            lifetime
        ));
        return services;
    }


    static IGamepadManager? TryCreate(Func<IServiceProvider, IGamepadManager>? physical, IServiceProvider sp)
    {
        if (physical == null)
            return null;

        try
        {
            return physical(sp);
        }
        catch (InvalidOperationException ex)
        {
            // The Android manager needs Shiny's AndroidPlatform, which only UseShiny() registers. An app
            // that wants the on-screen pad without Shiny's hosting should still get it - without
            // physical controllers, and with a line in the output saying exactly why.
            System.Diagnostics.Trace.WriteLine(
                "[Shiny.Gamepad] Physical controllers are unavailable, so only on-screen gamepads will be reported. " +
                "On Android, call UseShiny() (Shiny.Hosting.Maui) to enable them. " + ex.Message
            );
            return null;
        }
    }


    static Func<IServiceProvider, IGamepadManager>? Replay(ServiceDescriptor descriptor)
    {
        if (descriptor.IsKeyedService)
            return null;

        if (descriptor.ImplementationInstance is IGamepadManager instance)
            return _ => instance;

        if (descriptor.ImplementationFactory != null)
        {
            var factory = descriptor.ImplementationFactory;
            return sp => (IGamepadManager)factory(sp);
        }

        if (descriptor.ImplementationType != null)
        {
            var type = descriptor.ImplementationType;
            return sp => (IGamepadManager)ActivatorUtilities.CreateInstance(sp, type);
        }

        return null;
    }
}

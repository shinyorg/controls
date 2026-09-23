using Microsoft.Extensions.DependencyInjection;

namespace Shiny.Controls.Gamepad.Tests;


public class VirtualGamepadManagerTests
{
    [Fact]
    public async Task ReportsPhysicalAndVirtualTogether()
    {
        var physical = new FakeManager();
        physical.Pads.Add(new VirtualGamepad("physical-1"));
        var manager = new VirtualGamepadManager(physical);
        var onScreen = new VirtualGamepad("virtual-1");

        manager.Register(onScreen);
        var pads = await manager.GetGamepads();

        pads.Select(x => x.Id).ShouldBe(["physical-1", "virtual-1"]);
    }


    [Fact]
    public void RegisterAndUnregisterRaiseConnectionEvents()
    {
        var manager = new VirtualGamepadManager(null);
        var events = new List<string>();
        manager.Connected += (_, e) => events.Add("+" + e.Gamepad.Id);
        manager.Disconnected += (_, e) => events.Add("-" + e.Gamepad.Id);
        var pad = new VirtualGamepad("v");

        manager.Register(pad);
        manager.Register(pad);
        manager.Unregister(pad);
        manager.Unregister(pad);

        events.ShouldBe(["+v", "-v"]);
        pad.IsConnected.ShouldBeFalse();
    }


    [Fact]
    public void ForwardsPhysicalEventsAndTracksPresence()
    {
        var physical = new FakeManager();
        var manager = new VirtualGamepadManager(physical);
        var connected = new List<string>();
        var presence = 0;
        manager.Connected += (_, e) => connected.Add(e.Gamepad.Id);
        manager.PhysicalPresenceChanged += (_, _) => presence++;

        var pad = new VirtualGamepad("p1");
        physical.RaiseConnected(pad);
        manager.HasPhysicalGamepad.ShouldBeTrue();

        physical.RaiseDisconnected(pad);
        manager.HasPhysicalGamepad.ShouldBeFalse();

        connected.ShouldBe(["p1"]);
        presence.ShouldBe(2);
    }


    [Fact]
    public async Task PhysicalThatCannotStartStillReportsVirtualPads()
    {
        var physical = new FakeManager { Throw = true };
        var manager = new VirtualGamepadManager(physical);
        manager.Register(new VirtualGamepad("v"));

        var pads = await manager.GetGamepads();

        pads.Single().Id.ShouldBe("v");
    }


    [Fact]
    public void Registration_WrapsTheExistingManager()
    {
        var services = new ServiceCollection();
        var physical = new FakeManager();
        services.AddSingleton<IGamepadManager>(_ => physical);

        services.AddVirtualGamepadManager(ServiceLifetime.Singleton, null);
        using var sp = services.BuildServiceProvider();

        var manager = sp.GetRequiredService<IGamepadManager>().ShouldBeOfType<VirtualGamepadManager>();
        manager.Physical.ShouldBeSameAs(physical);
        sp.GetRequiredService<VirtualGamepadManager>().ShouldBeSameAs(manager);
    }


    [Fact]
    public void Registration_AddsThePlatformManagerWhenNoneIsRegistered()
    {
        var services = new ServiceCollection();
        var physical = new FakeManager();

        services.AddVirtualGamepadManager(ServiceLifetime.Scoped, s => s.AddSingleton<IGamepadManager>(physical));
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        scope.ServiceProvider.GetRequiredService<VirtualGamepadManager>().Physical.ShouldBeSameAs(physical);
    }


    [Fact]
    public void Registration_IsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddVirtualGamepadManager(ServiceLifetime.Singleton, null);
        services.AddVirtualGamepadManager(ServiceLifetime.Singleton, null);

        services.Count(x => x.ServiceType == typeof(IGamepadManager)).ShouldBe(1);
    }


    [Fact]
    public async Task VirtualPad_VibrationIsOnlyACapabilityWithAHandler()
    {
        var pad = new VirtualGamepad("v");
        pad.Supports(GamepadCapabilities.Vibration).ShouldBeFalse();
        await Should.ThrowAsync<GamepadNotSupportedException>(() => pad.SetVibration(GamepadVibration.Both(1)));

        GamepadVibration? played = null;
        pad.VibrationHandler = (v, _) =>
        {
            played = v;
            return Task.CompletedTask;
        };
        pad.Supports(GamepadCapabilities.Vibration).ShouldBeTrue();
        await pad.SetVibration(new GamepadVibration(2f, 0.5f));

        played.ShouldBe(new GamepadVibration(1f, 0.5f), "vibration is clamped before it reaches the device");
        pad.Kind.ShouldBe(GamepadKind.Virtual);
    }


    sealed class FakeManager : IGamepadManager
    {
        public List<IGamepad> Pads { get; } = [];
        public bool Throw { get; set; }

        public Task<IReadOnlyList<IGamepad>> GetGamepads(CancellationToken ct = default)
            => this.Throw
                ? throw new GamepadException("no gamepad api")
                : Task.FromResult<IReadOnlyList<IGamepad>>(this.Pads.ToList());

        public event EventHandler<GamepadConnectionEventArgs>? Connected;
        public event EventHandler<GamepadConnectionEventArgs>? Disconnected;
        public float AxisChangeThreshold { get; set; }
        public TimeSpan PollInterval { get; set; }

        public void RaiseConnected(IGamepad pad) => this.Connected?.Invoke(this, new(pad));
        public void RaiseDisconnected(IGamepad pad) => this.Disconnected?.Invoke(this, new(pad));
    }
}

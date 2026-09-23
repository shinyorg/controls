using Microsoft.Extensions.Logging.Abstractions;
using Shiny.Gamepad;
using Shiny.Gamepad.Infrastructure;

namespace Shiny.Controls.Gaming;


/// <summary>
/// The on-screen controller as an <see cref="IGamepad"/>, indistinguishable to game code from a
/// physical one apart from <see cref="GamepadKind.Virtual"/>.
/// </summary>
/// <remarks>
/// <para>Derives from Shiny.Gamepad's own <see cref="AbstractGamepad"/>, so state diffing, the axis
/// change threshold and one-event-per-button all behave exactly as they do for hardware - a game
/// polling <see cref="IGamepad.GetState"/> or listening to <see cref="IGamepad.ButtonChanged"/> needs
/// no special case.</para>
/// <para>Events are raised on whichever thread delivered the touch, which is the UI thread for
/// pointers and a timer thread for turbo repeats. That matches the physical backends, which raise
/// from input callbacks and poll loops.</para>
/// <para>A disconnected virtual pad stays disconnected - the view hands out a new instance (with the
/// same <see cref="IGamepad.Id"/>) when it is shown again, the same way a physical controller comes
/// back as a new object after it reconnects.</para>
/// </remarks>
public sealed class VirtualGamepad : AbstractGamepad
{
    GamepadButton supportedButtons = StandardButtons;


    /// <summary>Creates a virtual gamepad.</summary>
    /// <param name="id">Stable identity. The views reuse one id across the pads they create.</param>
    /// <param name="name">What <see cref="IGamepad.Name"/> reports.</param>
    public VirtualGamepad(string id, string name = "On-Screen Gamepad") : base(id, NullLogger.Instance)
        => this.Name = name;


    /// <inheritdoc/>
    public override string Name { get; }

    /// <inheritdoc/>
    public override GamepadKind Kind => GamepadKind.Virtual;

    /// <summary>The player slot this pad reports. Set it from the view to seat an on-screen pad as a given player.</summary>
    public int? AssignedPlayerIndex { get; set; }

    /// <inheritdoc/>
    public override int? PlayerIndex => this.AssignedPlayerIndex;

    /// <inheritdoc/>
    public override GamepadCapabilities Capabilities => this.VibrationHandler == null
        ? GamepadCapabilities.None
        : GamepadCapabilities.Vibration;

    /// <inheritdoc/>
    public override GamepadButton SupportedButtons => this.supportedButtons;

    /// <summary>
    /// Plays <see cref="IGamepad.SetVibration"/> on the device itself - the phone's vibration motor, or
    /// <c>navigator.vibrate</c> in a browser. Set by the host; null means the device cannot vibrate and
    /// <see cref="GamepadCapabilities.Vibration"/> is not reported.
    /// </summary>
    public Func<GamepadVibration, CancellationToken, Task>? VibrationHandler { get; set; }


    internal void SetSupportedButtons(GamepadButton buttons) => this.supportedButtons = buttons;

    internal void Push(GamepadState state) => this.UpdateState(state);


    /// <inheritdoc/>
    public override Task SetVibration(GamepadVibration vibration, CancellationToken ct = default)
    {
        this.AssertConnected();
        this.AssertCapability(GamepadCapabilities.Vibration, "this device has no vibration motor the app can reach");

        return this.VibrationHandler!(vibration.Clamp(), ct);
    }


    /// <inheritdoc/>
    public override Task<GamepadBattery> GetBattery(CancellationToken ct = default)
    {
        this.AssertConnected();
        this.AssertCapability(GamepadCapabilities.Battery, "an on-screen gamepad has no battery of its own");
        return Task.FromResult(new GamepadBattery(null, GamepadBatteryState.Unknown));
    }


    /// <inheritdoc/>
    public override Task SetLight(GamepadLight light, CancellationToken ct = default)
    {
        this.AssertConnected();
        this.AssertCapability(GamepadCapabilities.Light, "an on-screen gamepad has no light");
        return Task.CompletedTask;
    }


    /// <inheritdoc/>
    public override Task SetMotionEnabled(bool enabled, CancellationToken ct = default)
    {
        this.AssertConnected();
        this.AssertCapability(GamepadCapabilities.Motion, "an on-screen gamepad reports no motion");
        return Task.CompletedTask;
    }
}

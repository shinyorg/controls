using Shiny.Gamepad;

namespace Shiny.Controls.Gaming;


/// <summary>
/// An <see cref="IGamepadManager"/> that reports on-screen gamepads alongside the physical ones.
/// </summary>
/// <remarks>
/// <para>Registered in place of the platform manager by <c>UseShinyGamepad()</c> (MAUI) or
/// <c>AddShinyGamepad()</c> (Blazor), and wrapping it, so game code keeps asking
/// <see cref="IGamepadManager"/> for controllers and gets both kinds from one list and one pair of
/// events. A view registers its <see cref="VirtualGamepad"/> here when it appears and removes it when it
/// goes, which reads to the game exactly like a controller being plugged in and pulled out.</para>
/// <para>The physical manager is started lazily, like every Shiny.Gamepad manager: nothing touches
/// the hardware until <see cref="GetGamepads"/> is first awaited. A physical manager that cannot start -
/// a browser without the Gamepad API - is treated as having no controllers rather than failing the
/// call, so the on-screen pads are still reported.</para>
/// </remarks>
public sealed class VirtualGamepadManager : IGamepadManager
{
    readonly Lock gate = new();
    readonly List<VirtualGamepad> virtuals = [];
    readonly HashSet<string> physicalIds = [];
    Task? physicalStart;


    /// <summary>Creates the manager.</summary>
    /// <param name="physical">The platform manager to wrap, or null for on-screen gamepads only.</param>
    public VirtualGamepadManager(IGamepadManager? physical)
    {
        this.Physical = physical;
        if (physical != null)
        {
            physical.Connected += this.OnPhysicalConnected;
            physical.Disconnected += this.OnPhysicalDisconnected;
        }
    }


    /// <summary>The wrapped platform manager, or null.</summary>
    public IGamepadManager? Physical { get; }

    /// <inheritdoc/>
    public event EventHandler<GamepadConnectionEventArgs>? Connected;

    /// <inheritdoc/>
    public event EventHandler<GamepadConnectionEventArgs>? Disconnected;

    /// <summary>
    /// Raised when the number of physical controllers goes from none to some or back. The views use it
    /// to step aside when the player picks up a real controller.
    /// </summary>
    public event EventHandler? PhysicalPresenceChanged;


    /// <summary>Whether at least one physical controller is connected. Only meaningful once <see cref="StartPhysicalWatch"/> has run.</summary>
    public bool HasPhysicalGamepad
    {
        get
        {
            lock (this.gate)
                return this.physicalIds.Count > 0;
        }
    }


    /// <summary>The on-screen gamepads currently shown.</summary>
    public IReadOnlyList<VirtualGamepad> VirtualGamepads
    {
        get
        {
            lock (this.gate)
                return this.virtuals.ToList();
        }
    }


    /// <inheritdoc/>
    public float AxisChangeThreshold
    {
        get => this.Physical?.AxisChangeThreshold ?? this.axisChangeThreshold;
        set
        {
            this.axisChangeThreshold = value;
            if (this.Physical != null)
                this.Physical.AxisChangeThreshold = value;

            lock (this.gate)
            {
                foreach (var pad in this.virtuals)
                    pad.AxisChangeThreshold = value;
            }
        }
    }
    float axisChangeThreshold = 0.02f;


    /// <inheritdoc/>
    public TimeSpan PollInterval
    {
        get => this.Physical?.PollInterval ?? this.pollInterval;
        set
        {
            this.pollInterval = value;
            if (this.Physical != null)
                this.Physical.PollInterval = value;
        }
    }
    TimeSpan pollInterval = TimeSpan.FromMilliseconds(16);


    /// <inheritdoc/>
    public async Task<IReadOnlyList<IGamepad>> GetGamepads(CancellationToken ct = default)
    {
        var list = new List<IGamepad>();
        list.AddRange(await this.GetPhysical(ct).ConfigureAwait(false));

        lock (this.gate)
            list.AddRange(this.virtuals);

        return list;
    }


    /// <summary>
    /// Starts watching for physical controllers without waiting for a game to ask, so
    /// <see cref="HasPhysicalGamepad"/> and <see cref="PhysicalPresenceChanged"/> start reporting.
    /// </summary>
    /// <remarks>Idempotent. Never throws for a platform that cannot watch - it reports no controllers instead.</remarks>
    public Task StartPhysicalWatch(CancellationToken ct = default)
    {
        // started uncancellable and shared: one caller giving up must not leave a cancelled task cached
        // for every view that asks after it
        lock (this.gate)
            this.physicalStart ??= this.GetPhysical(CancellationToken.None);

        return this.physicalStart.WaitAsync(ct);
    }


    /// <summary>Adds an on-screen gamepad and raises <see cref="Connected"/>. Ignored if it is already registered.</summary>
    public void Register(VirtualGamepad gamepad)
    {
        ArgumentNullException.ThrowIfNull(gamepad);
        lock (this.gate)
        {
            if (this.virtuals.Contains(gamepad))
                return;

            gamepad.AxisChangeThreshold = this.axisChangeThreshold;
            this.virtuals.Add(gamepad);
        }
        this.Connected?.Invoke(this, new GamepadConnectionEventArgs(gamepad));
    }


    /// <summary>Removes an on-screen gamepad, marks it disconnected and raises <see cref="Disconnected"/>.</summary>
    public void Unregister(VirtualGamepad gamepad)
    {
        ArgumentNullException.ThrowIfNull(gamepad);
        lock (this.gate)
        {
            if (!this.virtuals.Remove(gamepad))
                return;
        }

        gamepad.SetDisconnected();
        this.Disconnected?.Invoke(this, new GamepadConnectionEventArgs(gamepad));
    }


    async Task<IReadOnlyList<IGamepad>> GetPhysical(CancellationToken ct)
    {
        if (this.Physical == null)
            return [];

        IReadOnlyList<IGamepad> pads;
        try
        {
            pads = await this.Physical.GetGamepads(ct).ConfigureAwait(false);
        }
        catch (GamepadException)
        {
            // no Gamepad API in this browser, or none on this platform - still a valid answer, and
            // the on-screen pads must not disappear because of it
            return [];
        }

        bool changed;
        lock (this.gate)
        {
            var before = this.physicalIds.Count > 0;
            foreach (var pad in pads)
                this.physicalIds.Add(pad.Id);

            changed = before != this.physicalIds.Count > 0;
        }
        if (changed)
            this.PhysicalPresenceChanged?.Invoke(this, EventArgs.Empty);

        return pads;
    }


    void OnPhysicalConnected(object? sender, GamepadConnectionEventArgs e)
    {
        bool changed;
        lock (this.gate)
            changed = this.physicalIds.Add(e.Gamepad.Id) && this.physicalIds.Count == 1;

        this.Connected?.Invoke(this, e);
        if (changed)
            this.PhysicalPresenceChanged?.Invoke(this, EventArgs.Empty);
    }


    void OnPhysicalDisconnected(object? sender, GamepadConnectionEventArgs e)
    {
        bool changed;
        lock (this.gate)
            changed = this.physicalIds.Remove(e.Gamepad.Id) && this.physicalIds.Count == 0;

        this.Disconnected?.Invoke(this, e);
        if (changed)
            this.PhysicalPresenceChanged?.Invoke(this, EventArgs.Empty);
    }
}

using Shiny.Controls.Gaming;
using Shiny.Gamepad;

namespace Shiny.Maui.Controls.Gaming;


/// <summary>
/// An on-screen gamepad - lay it over a game as a full-screen overlay, or place it inline like any
/// other view.
/// </summary>
/// <remarks>
/// <para><b>It is a real controller.</b> <see cref="Gamepad"/> is a Shiny.Gamepad
/// <see cref="IGamepad"/> of kind <see cref="GamepadKind.Virtual"/>, and with <c>UseShinyGamepad()</c>
/// it is also reported by <see cref="IGamepadManager"/> alongside any physical controller. Game code
/// written against Shiny.Gamepad needs no changes to be played with it.</para>
/// <para>As an overlay, leave <see cref="Sizing"/> at <see cref="GamepadSizing.Anchored"/> and put the
/// view over the game in a Grid. A touch that lands on no element falls through to the game underneath
/// on iOS and Android (<see cref="PassThrough"/>). Inline, use <see cref="GamepadSizing.Uniform"/> and
/// the whole controller, body included, scales into whatever space it is given.</para>
/// <para>Multi-touch - a thumb on the stick while the other presses a button - needs the native handler
/// registered by <c>UseShinyGamepad()</c> on iOS and Android. Every other platform reads a single
/// pointer, which is enough for a mouse.</para>
/// </remarks>
public partial class GamepadView : GraphicsView
{
    readonly GamepadEngine engine;
    VirtualGamepadManager? manager;
    IDispatcherTimer? idleTimer;
    float idleAlpha = 1f;
    bool isAttached;
    bool steppedAside;
    bool? inputTransparentBeforeStepAside;


    public GamepadView()
    {
        this.Gamepad = this.CreateGamepad();
        this.engine = new GamepadEngine(this.Gamepad);
        this.engine.Layout = GamepadLayouts.Get(this.Preset);
        this.engine.Invalidated += (_, _) => this.RequestRedraw();
        this.engine.ElementPressed += (_, element) => this.OnElementPressed(element);
        this.engine.LayoutEdited += (_, layout) => this.OnLayoutEdited(layout);

        this.Drawable = new GamepadDrawable(this);
        this.BackgroundColor = Colors.Transparent;

        // Only the stock GraphicsView handler raises these - iOS and Android use GamepadPlatformView,
        // which feeds every finger straight into the engine and never calls the base touch handling.
        this.StartInteraction += (_, e) => this.OnSinglePointer(e, 0);
        this.DragInteraction += (_, e) => this.OnSinglePointer(e, 1);
        this.EndInteraction += (_, e) => this.OnSinglePointer(e, 2);
        this.CancelInteraction += (_, _) => this.OnPointerCancel(0);

        this.Loaded += (_, _) => this.Attach();
        this.Unloaded += (_, _) => this.Detach();
    }


    /// <summary>The engine behind the view - what the renderer reads. Exposed for custom drawing and tests.</summary>
    public GamepadEngine Engine => this.engine;

    /// <summary>Raised when a button on <see cref="Gamepad"/> is pressed or released, on the UI thread.</summary>
    public event EventHandler<GamepadButtonChangedEventArgs>? ButtonChanged;

    /// <summary>Raised when a stick or trigger on <see cref="Gamepad"/> moves, on the UI thread.</summary>
    public event EventHandler<GamepadAxisChangedEventArgs>? AxisChanged;

    /// <summary>Raised when the player finishes moving or resizing an element in edit mode. <see cref="ControllerLayout"/> already holds the change - persist it with <see cref="GamepadLayout.ToJson"/>.</summary>
    public event EventHandler<GamepadLayout>? LayoutEdited;


    #region Lifecycle

    VirtualGamepad CreateGamepad()
    {
        var pad = new VirtualGamepad(this.GamepadId, this.GamepadName) { AssignedPlayerIndex = this.PlayerIndex };
        pad.ButtonChanged += this.OnPadButtonChanged;
        pad.AxisChanged += this.OnPadAxisChanged;

        try
        {
            if (Vibration.Default.IsSupported)
                pad.VibrationHandler = PlayVibration;
        }
        catch
        {
            // the plain net10.0 build has no Essentials implementation behind the interface
        }
        return pad;
    }


    void Attach()
    {
        if (this.isAttached)
            return;

        this.isAttached = true;

        // a pad that was shown before has been disconnected - hand out a fresh one with the same id,
        // exactly as a physical controller comes back as a new object after it reconnects
        if (!this.Gamepad.IsConnected)
        {
            this.Gamepad = this.CreateGamepad();
            this.engine.Gamepad = this.Gamepad;
        }

        this.manager = this.Handler?.MauiContext?.Services.GetService(typeof(VirtualGamepadManager)) as VirtualGamepadManager;
        if (this.manager != null)
        {
            this.manager.Register(this.Gamepad);
            this.manager.PhysicalPresenceChanged += this.OnPhysicalPresenceChanged;
            if (this.HideWhenControllerConnected)
                _ = this.manager.StartPhysicalWatch();
        }

        this.UpdateStepAside();
        this.RestartIdle();
    }


    void Detach()
    {
        if (!this.isAttached)
            return;

        this.isAttached = false;
        this.idleTimer?.Stop();

        // lift every finger first, so the game sees releases before it sees the pad go
        this.engine.ReleaseAll();

        if (this.manager != null)
        {
            this.manager.PhysicalPresenceChanged -= this.OnPhysicalPresenceChanged;
            this.manager.Unregister(this.Gamepad);
            this.manager = null;
        }
        else
        {
            this.Gamepad.SetDisconnected();
        }
    }


    static Task PlayVibration(GamepadVibration vibration, CancellationToken ct)
    {
        try
        {
            // the motor has one level - on is on. It runs until told otherwise, like a controller's,
            // with a long ceiling so a game that never sends Off does not buzz forever
            if (vibration.IsSilent)
                Vibration.Default.Cancel();
            else
                Vibration.Default.Vibrate(TimeSpan.FromSeconds(30));
        }
        catch (Exception ex)
        {
            // Android throws without the VIBRATE permission in the manifest
            System.Diagnostics.Trace.WriteLine("[GamepadView] Vibration failed: " + ex.Message);
        }
        return Task.CompletedTask;
    }

    #endregion

    #region Input

    /// <summary>Whether the platform view should take a touch at this point, rather than let it fall through.</summary>
    internal bool ShouldReceive(float x, float y) => !this.PassThrough || this.engine.HitTest(x, y);


    internal bool OnPointerDown(long id, float x, float y)
    {
        var hit = this.engine.PointerDown(id, x, y);
        if (hit)
            this.WakeFromIdle();

        return hit;
    }


    internal void OnPointerMove(long id, float x, float y) => this.engine.PointerMove(id, x, y);


    internal void OnPointerUp(long id)
    {
        this.engine.PointerUp(id);
        if (!this.engine.IsTouched)
            this.RestartIdle();
    }


    internal void OnPointerCancel(long id)
    {
        this.engine.PointerCancel(id);
        if (!this.engine.IsTouched)
            this.RestartIdle();
    }


    void OnSinglePointer(TouchEventArgs e, int phase)
    {
        if (e.Touches.Length == 0)
            return;

        var p = e.Touches[0];
        switch (phase)
        {
            case 0: this.OnPointerDown(0, p.X, p.Y); break;
            case 1: this.OnPointerMove(0, p.X, p.Y); break;
            default: this.OnPointerUp(0); break;
        }
    }


    void OnElementPressed(GamepadElement element)
    {
        var enabled = element.Kind == GamepadElementKind.DPad
            ? this.DirectionalHapticFeedback
            : this.ButtonHapticFeedback;

        if (!enabled)
            return;

        try
        {
            Microsoft.Maui.Devices.HapticFeedback.Default.Perform(HapticFeedbackType.Click);
        }
        catch
        {
            // no haptic engine (simulators, desktops, the plain net10.0 build)
        }
    }


    void OnLayoutEdited(GamepadLayout edited)
    {
        // a copy goes out: the engine keeps editing its own instance, and a view model holding it
        // would see later drags land in an object it thought it had saved
        var layout = edited.Clone();
        this.RunOnUi(() =>
        {
            // the engine already holds the edited layout; setting the property only publishes it to a
            // binding, so don't let the property change rebuild the engine from it
            this.suppressLayoutApply = true;
            try
            {
                this.ControllerLayout = layout;
            }
            finally
            {
                this.suppressLayoutApply = false;
            }
            this.LayoutEdited?.Invoke(this, layout);
        });
    }
    bool suppressLayoutApply;


    void OnPadButtonChanged(object? sender, GamepadButtonChangedEventArgs e)
        => this.RunOnUi(() => this.ButtonChanged?.Invoke(this, e));


    void OnPadAxisChanged(object? sender, GamepadAxisChangedEventArgs e)
        => this.RunOnUi(() => this.AxisChanged?.Invoke(this, e));

    #endregion

    #region Presentation

    /// <summary>The alpha the idle fade is currently at, 0 to 1.</summary>
    internal float IdleAlpha => this.idleAlpha;

    /// <summary>Whether the pad has stepped aside for a physical controller.</summary>
    internal bool IsSteppedAside => this.steppedAside;


    void RequestRedraw() => this.RunOnUi(this.Invalidate);


    void RunOnUi(Action action)
    {
        if (this.Dispatcher is { IsDispatchRequired: true } dispatcher)
            dispatcher.Dispatch(action);
        else
            action();
    }


    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width > 0 && height > 0)
            this.engine.Arrange((float)width, (float)height);
    }


    void OnPhysicalPresenceChanged(object? sender, EventArgs e) => this.RunOnUi(this.UpdateStepAside);


    void UpdateStepAside()
    {
        var aside = this.HideWhenControllerConnected && this.manager?.HasPhysicalGamepad == true;
        if (aside == this.steppedAside)
            return;

        this.steppedAside = aside;
        this.engine.IsEnabled = !aside;

        // the stock handler has no pass-through, so a hidden overlay must stop taking clicks outright
        if (aside)
        {
            this.inputTransparentBeforeStepAside = this.InputTransparent;
            this.InputTransparent = true;
        }
        else if (this.inputTransparentBeforeStepAside is { } previous)
        {
            this.InputTransparent = previous;
            this.inputTransparentBeforeStepAside = null;
        }
        this.Invalidate();
    }


    void WakeFromIdle()
    {
        this.idleTimer?.Stop();
        this.AbortAnimation("ShinyGamepadIdle");
        if (this.idleAlpha != 1f)
        {
            this.idleAlpha = 1f;
            this.Invalidate();
        }
    }


    void RestartIdle()
    {
        if (!this.isAttached || this.IdleOpacity >= 1.0 || this.Dispatcher == null)
            return;

        this.idleTimer ??= this.CreateIdleTimer();
        this.idleTimer.Stop();
        this.idleTimer.Interval = this.IdleDelay;
        this.idleTimer.Start();
    }


    IDispatcherTimer CreateIdleTimer()
    {
        var timer = this.Dispatcher.CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (this.engine.IsTouched)
                return;

            var from = this.idleAlpha;
            var to = (float)Math.Clamp(this.IdleOpacity, 0, 1);
            this.Animate(
                "ShinyGamepadIdle",
                t =>
                {
                    this.idleAlpha = (float)(from + (to - from) * t);
                    this.Invalidate();
                },
                length: 400,
                easing: Easing.CubicOut
            );
        };
        return timer;
    }

    #endregion
}

using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Shiny.Controls.Gaming;
using Shiny.Gamepad;

namespace Shiny.Blazor.Controls.Gaming;


/// <summary>
/// An on-screen gamepad - lay it over a game as an overlay, or place it inline like any other
/// component. Built for touch in mobile browsers; a mouse drives it too.
/// </summary>
/// <remarks>
/// <para><b>It is a real controller.</b> <see cref="Gamepad"/> is a Shiny.Gamepad
/// <see cref="IGamepad"/> of kind <see cref="GamepadKind.Virtual"/>, and with <c>AddShinyGamepad()</c>
/// it is also reported by <see cref="IGamepadManager"/> alongside any physical controller the browser
/// can see. Game code written against Shiny.Gamepad needs no changes to be played with it.</para>
/// <para>As an overlay, give it a positioned box over the game (<c>position:absolute; inset:0</c>) and
/// leave <see cref="Sizing"/> at <see cref="GamepadSizing.Anchored"/>. With <see cref="PassThrough"/>
/// only the elements take pointer events, so a touch anywhere else reaches the game underneath.</para>
/// <para>On Blazor Server every pointer is a round trip, so the knob lags the thumb by the connection's
/// latency - as does every input the game reads. WebAssembly has no such cost.</para>
/// </remarks>
public partial class GamepadView : ComponentBase, IAsyncDisposable
{
    const string ModulePath = "./_content/Shiny.Blazor.Controls.Gamepad/shinyGamepad.js";

    [Inject] IJSRuntime JS { get; set; } = default!;
    [Inject] IServiceProvider Services { get; set; } = default!;

    GamepadEngine engine = default!;
    ElementReference root;
    IJSObjectReference? module;
    DotNetObjectReference<GamepadView>? selfRef;
    VirtualGamepadManager? manager;
    float width, height;
    bool steppedAside, renderQueued, canVibrate, disposed;

    // parameter values last pushed into the engine - an engine setter releases held buttons, so a
    // parent re-rendering with the same values must never reach it
    GamepadPreset appliedPreset;
    GamepadLayout? appliedLayout;
    object? appliedJsOptions;


    /// <summary>The built-in layout to show when <see cref="ControllerLayout"/> is not set.</summary>
    [Parameter] public GamepadPreset Preset { get; set; } = GamepadPreset.Standard;

    /// <summary>A custom or saved layout, replacing <see cref="Preset"/>. Supports <c>@bind-ControllerLayout</c>: after the player rearranges the controller in edit mode the binding receives the edited layout.</summary>
    [Parameter] public GamepadLayout? ControllerLayout { get; set; }

    [Parameter] public EventCallback<GamepadLayout?> ControllerLayoutChanged { get; set; }

    /// <summary>What is printed on the buttons. Null uses the layout's own.</summary>
    [Parameter] public GamepadFaceStyle? FaceStyle { get; set; }

    /// <summary>Anchored for an overlay, Uniform for an inline controller.</summary>
    [Parameter] public GamepadSizing Sizing { get; set; } = GamepadSizing.Anchored;

    /// <summary>Size multiplier in Anchored mode.</summary>
    [Parameter] public double ControllerScale { get; set; } = 1.0;

    [Parameter] public GamepadDPadMode DPadMode { get; set; } = GamepadDPadMode.EightWay;

    /// <summary>Drag elements to move them and pinch to resize, instead of playing.</summary>
    [Parameter] public bool IsEditing { get; set; }

    /// <summary>Presses per second for elements marked <see cref="GamepadElement.IsTurbo"/>.</summary>
    [Parameter] public double TurboRate { get; set; } = 10;

    /// <summary>Whether a double-tap-and-hold on a stick clicks it (L3/R3).</summary>
    [Parameter] public bool StickClickEnabled { get; set; } = true;

    /// <summary>A short vibration when a non-directional button is pressed, where the browser can vibrate (Android - not iOS Safari).</summary>
    [Parameter] public bool ButtonHapticFeedback { get; set; } = true;

    /// <summary>A short vibration each time the d-pad takes a new direction. Off by default - rolling a thumb around a d-pad would buzz.</summary>
    [Parameter] public bool DirectionalHapticFeedback { get; set; }

    /// <summary>Hide, and let every touch through, while a physical controller is connected. Needs <c>AddShinyGamepad()</c>.</summary>
    [Parameter] public bool HideWhenControllerConnected { get; set; }

    /// <summary>Opacity to fade to after <see cref="IdleDelay"/> with no touch. 1 turns fading off.</summary>
    [Parameter] public double IdleOpacity { get; set; } = 1.0;

    [Parameter] public TimeSpan IdleDelay { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>Only the elements take pointer events; a touch anywhere else reaches whatever is beneath.</summary>
    [Parameter] public bool PassThrough { get; set; } = true;

    /// <summary>Draw the controller body. Null draws it only in Uniform sizing.</summary>
    [Parameter] public bool? ShowBody { get; set; }

    /// <summary>The player slot <see cref="Gamepad"/> reports.</summary>
    [Parameter] public int? PlayerIndex { get; set; }

    /// <summary>Stable id for <see cref="Gamepad"/>. Read once, when the component initialises.</summary>
    [Parameter] public string? GamepadId { get; set; }

    [Parameter] public string GamepadName { get; set; } = "On-Screen Gamepad";

    // colours are CSS values; null leaves the stylesheet's --shiny-gamepad-* default
    [Parameter] public string? ButtonColor { get; set; }
    [Parameter] public string? PressedColor { get; set; }
    [Parameter] public string? LabelColor { get; set; }
    [Parameter] public string? OutlineColor { get; set; }
    [Parameter] public string? BodyColor { get; set; }
    [Parameter] public string? AccentColor { get; set; }

    [Parameter] public string? Class { get; set; }
    [Parameter] public string? Style { get; set; }

    /// <summary>A button was pressed or released.</summary>
    [Parameter] public EventCallback<GamepadButtonChangedEventArgs> OnButtonChanged { get; set; }

    /// <summary>A stick or trigger moved.</summary>
    [Parameter] public EventCallback<GamepadAxisChangedEventArgs> OnAxisChanged { get; set; }

    /// <summary>The player finished moving or resizing an element in edit mode. Persist it with <see cref="GamepadLayout.ToJson"/>.</summary>
    [Parameter] public EventCallback<GamepadLayout> OnLayoutEdited { get; set; }

    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }


    /// <summary>The on-screen controller as a Shiny.Gamepad <see cref="IGamepad"/>.</summary>
    public VirtualGamepad Gamepad { get; private set; } = default!;

    /// <summary>The engine behind the component - what it draws from.</summary>
    public GamepadEngine Engine => this.engine;


    protected override void OnInitialized()
    {
        this.Gamepad = new VirtualGamepad(this.GamepadId ?? "virtual-" + Guid.NewGuid().ToString("N")[..8], this.GamepadName);
        this.Gamepad.ButtonChanged += (_, e) => this.Forward(this.OnButtonChanged, e);
        this.Gamepad.AxisChanged += (_, e) => this.Forward(this.OnAxisChanged, e);

        this.engine = new GamepadEngine(this.Gamepad);
        this.appliedPreset = this.Preset;
        this.appliedLayout = this.ControllerLayout;
        this.engine.Layout = this.ControllerLayout?.Clone() ?? GamepadLayouts.Get(this.Preset);
        this.engine.Invalidated += (_, _) => this.QueueRender();
        this.engine.ElementPressed += (_, element) => this.OnElementPressed(element);
        this.engine.LayoutEdited += (_, layout) => _ = this.InvokeAsync(() => this.PublishEdit(layout.Clone()));

        this.manager = this.Services.GetService<VirtualGamepadManager>();
        if (this.manager != null)
        {
            this.manager.Register(this.Gamepad);
            this.manager.PhysicalPresenceChanged += this.OnPhysicalPresenceChanged;
        }
    }


    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(this.ControllerLayout, this.appliedLayout) || (this.ControllerLayout == null && this.Preset != this.appliedPreset))
        {
            this.appliedLayout = this.ControllerLayout;
            this.appliedPreset = this.Preset;
            this.engine.Layout = this.ControllerLayout?.Clone() ?? GamepadLayouts.Get(this.Preset);
        }

        if (this.engine.FaceStyle != this.FaceStyle)
            this.engine.FaceStyle = this.FaceStyle;

        if (this.engine.Sizing != this.Sizing)
            this.engine.Sizing = this.Sizing;

        if (this.engine.Scale != (float)this.ControllerScale)
            this.engine.Scale = (float)this.ControllerScale;

        if (this.engine.IsEditing != this.IsEditing)
            this.engine.IsEditing = this.IsEditing;

        this.engine.DPadMode = this.DPadMode;
        this.engine.TurboRate = (float)this.TurboRate;
        this.engine.StickClickEnabled = this.StickClickEnabled;
        this.Gamepad.AssignedPlayerIndex = this.PlayerIndex;

        if (this.HideWhenControllerConnected && this.manager != null)
            _ = this.manager.StartPhysicalWatch();

        this.UpdateStepAside();
    }


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        this.renderQueued = false;
        if (this.disposed)
            return;

        var options = new GamepadJsOptions(this.IdleOpacity, this.IdleDelay.TotalMilliseconds);
        if (firstRender)
        {
            this.module = await this.JS.InvokeAsync<IJSObjectReference>("import", ModulePath);
            this.selfRef = DotNetObjectReference.Create(this);
            await this.module.InvokeVoidAsync("attach", this.root, this.selfRef, options);
            this.appliedJsOptions = options;

            this.canVibrate = await this.module.InvokeAsync<bool>("canVibrate");
            if (this.canVibrate)
                this.Gamepad.VibrationHandler = this.PlayVibration;
        }
        else if (this.module != null && !Equals(options, this.appliedJsOptions))
        {
            this.appliedJsOptions = options;
            await this.module.InvokeVoidAsync("update", this.root, options);
        }
    }


    /// <summary>Called from JS when the root box changes size.</summary>
    [JSInvokable]
    public void OnResize(double w, double h)
    {
        this.width = (float)w;
        this.height = (float)h;
        this.engine.Arrange(this.width, this.height);
        this.StateHasChanged();
    }


    /// <summary>
    /// Called from JS with a flat batch of pointer events - kind, id, x, y repeated. Kinds: 0 down,
    /// 1 move, 2 up, 3 cancel.
    /// </summary>
    [JSInvokable]
    public void OnPointers(double[] batch)
    {
        for (var i = 0; i + 3 < batch.Length; i += 4)
        {
            var id = (long)batch[i + 1];
            var x = (float)batch[i + 2];
            var y = (float)batch[i + 3];
            switch ((int)batch[i])
            {
                case 0: this.engine.PointerDown(id, x, y); break;
                case 1: this.engine.PointerMove(id, x, y); break;
                case 2: this.engine.PointerUp(id); break;
                default: this.engine.PointerCancel(id); break;
            }
        }
    }


    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        this.engine.ReleaseAll();
        this.engine.Dispose();

        if (this.manager != null)
        {
            this.manager.PhysicalPresenceChanged -= this.OnPhysicalPresenceChanged;
            this.manager.Unregister(this.Gamepad);
        }
        else
        {
            this.Gamepad.SetDisconnected();
        }

        try
        {
            if (this.module != null)
            {
                await this.module.InvokeVoidAsync("detach", this.root);
                await this.module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // the circuit is gone and the listeners with it
        }
        this.selfRef?.Dispose();
    }


    void QueueRender()
    {
        // a pointer batch runs the engine several times; one render per batch is plenty
        if (this.renderQueued || this.disposed)
            return;

        this.renderQueued = true;
        _ = this.InvokeAsync(this.StateHasChanged);
    }


    void Forward<T>(EventCallback<T> callback, T args)
    {
        if (callback.HasDelegate && !this.disposed)
            _ = this.InvokeAsync(() => callback.InvokeAsync(args));
    }


    void OnElementPressed(GamepadElement element)
    {
        var enabled = element.Kind == GamepadElementKind.DPad ? this.DirectionalHapticFeedback : this.ButtonHapticFeedback;
        if (enabled && this.canVibrate && this.module != null)
            _ = this.InvokeAsync(async () => await this.module.InvokeVoidAsync("vibrate", 12));
    }


    Task PlayVibration(GamepadVibration vibration, CancellationToken ct)
        => this.InvokeAsync(async () =>
        {
            // the browser motor has one level; on runs until Off, with a ceiling
            if (this.module != null)
                await this.module.InvokeVoidAsync("vibrate", ct, vibration.IsSilent ? 0 : 30000);
        });


    async Task PublishEdit(GamepadLayout layout)
    {
        // what the binding sends back must not re-apply (and release everything) on the next render
        this.appliedLayout = layout;
        await this.ControllerLayoutChanged.InvokeAsync(layout);
        await this.OnLayoutEdited.InvokeAsync(layout);
    }


    void OnPhysicalPresenceChanged(object? sender, EventArgs e) => _ = this.InvokeAsync(() =>
    {
        this.UpdateStepAside();
        this.StateHasChanged();
    });


    void UpdateStepAside()
    {
        this.steppedAside = this.HideWhenControllerConnected && this.manager?.HasPhysicalGamepad == true;
        this.engine.IsEnabled = !this.steppedAside;
    }


    string RootStyle
    {
        get
        {
            var sb = new StringBuilder();
            Var(sb, "--shiny-gamepad-button", this.ButtonColor);
            Var(sb, "--shiny-gamepad-pressed", this.PressedColor);
            Var(sb, "--shiny-gamepad-label", this.LabelColor);
            Var(sb, "--shiny-gamepad-outline", this.OutlineColor);
            Var(sb, "--shiny-gamepad-body", this.BodyColor);
            Var(sb, "--shiny-gamepad-accent", this.AccentColor);
            sb.Append("--shiny-gamepad-idle-opacity:").Append(F(this.IdleOpacity)).Append(';');

            // the caller's style is appended to ours rather than splatted, which would replace it and
            // drop every variable above
            if (!string.IsNullOrWhiteSpace(this.Style))
                sb.Append(this.Style);

            return sb.ToString();
        }
    }


    static void Var(StringBuilder sb, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            sb.Append(name).Append(':').Append(value).Append(';');
    }


    readonly record struct Hit(GamepadRect Rect, bool Circle);


    Hit HitShape(GamepadElementVisual v)
    {
        var e = v.Element;
        if (this.engine.IsEditing)
            return new(v.Bounds.Inflate(this.engine.HitSlop), false);

        if (e.Kind == GamepadElementKind.Button && e.Shape == GamepadElementShape.Circle)
        {
            // the same circle the engine hit-tests, so the browser never sends a touch the engine
            // would reject or swallows one it would accept
            var r = Math.Min(v.Bounds.Width, v.Bounds.Height) / 2 + this.engine.HitSlop;
            return new(GamepadRect.FromCenter(v.Bounds.CenterX, v.Bounds.CenterY, r * 2, r * 2), true);
        }

        return new(e.Kind == GamepadElementKind.Stick && e.IsFloating ? v.HitBounds : v.Bounds.Inflate(this.engine.HitSlop), false);
    }


    static float FontSize(GamepadElementVisual v)
    {
        var text = v.Face.Text ?? "";
        var b = v.Bounds;
        return v.Element.Shape == GamepadElementShape.Pill || text.Length > 2
            ? Math.Min(b.Height * 0.5f, b.Width / Math.Max(3, text.Length) * 1.5f)
            : b.Height * 0.42f;
    }


    static string CrossPath(GamepadRect b)
    {
        var arm = b.Width / 3f;
        var a0 = b.CenterX - arm / 2; var a1 = b.CenterX + arm / 2;
        var c0 = b.CenterY - arm / 2; var c1 = b.CenterY + arm / 2;
        return $"M {F(a0)} {F(b.Y)} L {F(a1)} {F(b.Y)} L {F(a1)} {F(c0)} L {F(b.Right)} {F(c0)} L {F(b.Right)} {F(c1)} L {F(a1)} {F(c1)} " +
               $"L {F(a1)} {F(b.Bottom)} L {F(a0)} {F(b.Bottom)} L {F(a0)} {F(c1)} L {F(b.X)} {F(c1)} L {F(b.X)} {F(c0)} L {F(a0)} {F(c0)} Z";
    }


    static string ArrowsPath(GamepadRect b)
    {
        var arm = b.Width / 3f;
        var s = arm * 0.22f;
        var sb = new StringBuilder();
        Arrow(sb, b.CenterX, b.Y + arm / 2, 0, -1, s);
        Arrow(sb, b.CenterX, b.Bottom - arm / 2, 0, 1, s);
        Arrow(sb, b.X + arm / 2, b.CenterY, -1, 0, s);
        Arrow(sb, b.Right - arm / 2, b.CenterY, 1, 0, s);
        return sb.ToString();
    }


    static void Arrow(StringBuilder sb, float x, float y, float dx, float dy, float size)
    {
        sb.Append("M ").Append(F(x + dx * size)).Append(' ').Append(F(y + dy * size))
          .Append(" L ").Append(F(x - dy * size - dx * size * 0.6f)).Append(' ').Append(F(y + dx * size - dy * size * 0.6f))
          .Append(" L ").Append(F(x + dy * size - dx * size * 0.6f)).Append(' ').Append(F(y - dx * size - dy * size * 0.6f))
          .Append(" Z ");
    }


    // SVG wants invariant numbers - a German browser culture would otherwise write "12,5"
    static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}


/// <summary>Options handed to the JS module. A named record: anonymous types break trimmed WASM interop.</summary>
public sealed record GamepadJsOptions(double IdleOpacity, double IdleDelayMs);

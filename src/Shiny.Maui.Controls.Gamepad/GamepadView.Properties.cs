using Shiny.Controls.Gaming;
using Shiny.Gamepad;

namespace Shiny.Maui.Controls.Gaming;


public partial class GamepadView
{
    /// <summary>The built-in layout to show when <see cref="ControllerLayout"/> is not set.</summary>
    public static readonly BindableProperty PresetProperty = BindableProperty.Create(
        nameof(Preset), typeof(GamepadPreset), typeof(GamepadView), GamepadPreset.Standard,
        propertyChanged: (b, _, _) => ((GamepadView)b).ApplyLayout());

    /// <summary>
    /// A custom or saved layout, replacing <see cref="Preset"/>. Not named <c>Layout</c>, which would hide
    /// <see cref="VisualElement.Layout(Rect)"/>. Two-way by default: after the player
    /// rearranges the controller in edit mode it holds the edited layout.
    /// </summary>
    public static readonly BindableProperty ControllerLayoutProperty = BindableProperty.Create(
        nameof(ControllerLayout), typeof(GamepadLayout), typeof(GamepadView), null, BindingMode.TwoWay,
        propertyChanged: (b, _, _) => ((GamepadView)b).ApplyLayout());

    /// <summary>What is printed on the buttons. Null uses the layout's own (NES labels on the NES pad, Xbox on Standard).</summary>
    public static readonly BindableProperty FaceStyleProperty = BindableProperty.Create(
        nameof(FaceStyle), typeof(GamepadFaceStyle?), typeof(GamepadView), null,
        propertyChanged: (b, _, n) => ((GamepadView)b).engine.FaceStyle = (GamepadFaceStyle?)n);

    /// <summary>Anchored for an overlay, Uniform for an inline controller.</summary>
    public static readonly BindableProperty SizingProperty = BindableProperty.Create(
        nameof(Sizing), typeof(GamepadSizing), typeof(GamepadView), GamepadSizing.Anchored,
        propertyChanged: (b, _, n) => ((GamepadView)b).engine.Sizing = (GamepadSizing)n);

    /// <summary>Size multiplier in Anchored mode. Named to stay clear of <see cref="VisualElement.Scale"/>, which would scale the drawing but not the touch targets.</summary>
    public static readonly BindableProperty ControllerScaleProperty = BindableProperty.Create(
        nameof(ControllerScale), typeof(double), typeof(GamepadView), 1.0,
        propertyChanged: (b, _, n) => ((GamepadView)b).engine.Scale = (float)(double)n);

    /// <summary>Eight-way (default) or four-way d-pad.</summary>
    public static readonly BindableProperty DPadModeProperty = BindableProperty.Create(
        nameof(DPadMode), typeof(GamepadDPadMode), typeof(GamepadView), GamepadDPadMode.EightWay,
        propertyChanged: (b, _, n) => ((GamepadView)b).engine.DPadMode = (GamepadDPadMode)n);

    /// <summary>Drag elements to move them and pinch to resize, instead of playing.</summary>
    public static readonly BindableProperty IsEditingProperty = BindableProperty.Create(
        nameof(IsEditing), typeof(bool), typeof(GamepadView), false,
        propertyChanged: (b, _, n) => ((GamepadView)b).engine.IsEditing = (bool)n);

    /// <summary>Presses per second for elements marked <see cref="GamepadElement.IsTurbo"/>.</summary>
    public static readonly BindableProperty TurboRateProperty = BindableProperty.Create(
        nameof(TurboRate), typeof(double), typeof(GamepadView), 10.0,
        propertyChanged: (b, _, n) => ((GamepadView)b).engine.TurboRate = (float)(double)n);

    /// <summary>Whether a double-tap-and-hold on a stick clicks it (L3/R3).</summary>
    public static readonly BindableProperty StickClickEnabledProperty = BindableProperty.Create(
        nameof(StickClickEnabled), typeof(bool), typeof(GamepadView), true,
        propertyChanged: (b, _, n) => ((GamepadView)b).engine.StickClickEnabled = (bool)n);

    /// <summary>
    /// A haptic tick when a non-directional button is pressed - face buttons, shoulders, triggers,
    /// Start/Select and Home. Turbo repeats never tick.
    /// </summary>
    public static readonly BindableProperty ButtonHapticFeedbackProperty = BindableProperty.Create(
        nameof(ButtonHapticFeedback), typeof(bool), typeof(GamepadView), true);

    /// <summary>
    /// A haptic tick each time the d-pad takes a new direction. Off by default: a thumb rolling around
    /// a d-pad crosses a direction every few pixels, and ticking on each one reads as a buzz.
    /// </summary>
    public static readonly BindableProperty DirectionalHapticFeedbackProperty = BindableProperty.Create(
        nameof(DirectionalHapticFeedback), typeof(bool), typeof(GamepadView), false);

    /// <summary>Hide, and let every touch through, while a physical controller is connected. Needs <c>UseShinyGamepad()</c>.</summary>
    public static readonly BindableProperty HideWhenControllerConnectedProperty = BindableProperty.Create(
        nameof(HideWhenControllerConnected), typeof(bool), typeof(GamepadView), false,
        propertyChanged: (b, _, n) => ((GamepadView)b).OnHideWhenControllerConnectedChanged((bool)n));

    /// <summary>Opacity the controller fades to after <see cref="IdleDelay"/> with no touch, so it stops covering the game. 1 turns fading off.</summary>
    public static readonly BindableProperty IdleOpacityProperty = BindableProperty.Create(
        nameof(IdleOpacity), typeof(double), typeof(GamepadView), 1.0,
        propertyChanged: (b, _, _) => ((GamepadView)b).RestartIdle());

    /// <summary>How long without a touch before fading to <see cref="IdleOpacity"/>.</summary>
    public static readonly BindableProperty IdleDelayProperty = BindableProperty.Create(
        nameof(IdleDelay), typeof(TimeSpan), typeof(GamepadView), TimeSpan.FromSeconds(4));

    /// <summary>Let touches that land on no element fall through to the view beneath. iOS and Android.</summary>
    public static readonly BindableProperty PassThroughProperty = BindableProperty.Create(
        nameof(PassThrough), typeof(bool), typeof(GamepadView), true);

    /// <summary>Draw the controller body behind the elements. Null draws it only in Uniform sizing.</summary>
    public static readonly BindableProperty ShowBodyProperty = BindableProperty.Create(
        nameof(ShowBody), typeof(bool?), typeof(GamepadView), null,
        propertyChanged: (b, _, _) => ((GamepadView)b).Invalidate());

    /// <summary>The player slot <see cref="Gamepad"/> reports.</summary>
    public static readonly BindableProperty PlayerIndexProperty = BindableProperty.Create(
        nameof(PlayerIndex), typeof(int?), typeof(GamepadView), null,
        propertyChanged: (b, _, n) => ((GamepadView)b).Gamepad.AssignedPlayerIndex = (int?)n);

    public static readonly BindableProperty ButtonColorProperty = ColorProperty(nameof(ButtonColor), Color.FromArgb("#A6272B33"));
    public static readonly BindableProperty PressedColorProperty = ColorProperty(nameof(PressedColor), Color.FromArgb("#E65B6472"));
    public static readonly BindableProperty LabelColorProperty = ColorProperty(nameof(LabelColor), Color.FromArgb("#F2F4F7"));
    public static readonly BindableProperty OutlineColorProperty = ColorProperty(nameof(OutlineColor), Color.FromArgb("#59FFFFFF"));
    public static readonly BindableProperty BodyColorProperty = ColorProperty(nameof(BodyColor), Color.FromArgb("#1E2128"));
    public static readonly BindableProperty AccentColorProperty = ColorProperty(nameof(AccentColor), Color.FromArgb("#38BDF8"));


    static BindableProperty ColorProperty(string name, Color fallback) => BindableProperty.Create(
        name, typeof(Color), typeof(GamepadView), fallback,
        propertyChanged: (b, _, _) => ((GamepadView)b).Invalidate());


    /// <summary>The on-screen controller as a Shiny.Gamepad <see cref="IGamepad"/>. Replaced by a new instance (same id) each time the view is shown again after being removed.</summary>
    public VirtualGamepad Gamepad
    {
        get;
        private set
        {
            if (ReferenceEquals(field, value))
                return;

            this.OnPropertyChanging();
            field = value;
            this.OnPropertyChanged();
        }
    }

    /// <summary>Stable id for <see cref="Gamepad"/>, kept across the pads the view hands out. Set it before the view is shown to persist per-player bindings.</summary>
    public string GamepadId { get; set; } = "virtual-" + Guid.NewGuid().ToString("N")[..8];

    /// <summary>What <see cref="IGamepad.Name"/> reports.</summary>
    public string GamepadName { get; set; } = "On-Screen Gamepad";

    public GamepadPreset Preset
    {
        get => (GamepadPreset)this.GetValue(PresetProperty);
        set => this.SetValue(PresetProperty, value);
    }

    public GamepadLayout? ControllerLayout
    {
        get => (GamepadLayout?)this.GetValue(ControllerLayoutProperty);
        set => this.SetValue(ControllerLayoutProperty, value);
    }

    public GamepadFaceStyle? FaceStyle
    {
        get => (GamepadFaceStyle?)this.GetValue(FaceStyleProperty);
        set => this.SetValue(FaceStyleProperty, value);
    }

    public GamepadSizing Sizing
    {
        get => (GamepadSizing)this.GetValue(SizingProperty);
        set => this.SetValue(SizingProperty, value);
    }

    public double ControllerScale
    {
        get => (double)this.GetValue(ControllerScaleProperty);
        set => this.SetValue(ControllerScaleProperty, value);
    }

    public GamepadDPadMode DPadMode
    {
        get => (GamepadDPadMode)this.GetValue(DPadModeProperty);
        set => this.SetValue(DPadModeProperty, value);
    }

    public bool IsEditing
    {
        get => (bool)this.GetValue(IsEditingProperty);
        set => this.SetValue(IsEditingProperty, value);
    }

    public double TurboRate
    {
        get => (double)this.GetValue(TurboRateProperty);
        set => this.SetValue(TurboRateProperty, value);
    }

    public bool StickClickEnabled
    {
        get => (bool)this.GetValue(StickClickEnabledProperty);
        set => this.SetValue(StickClickEnabledProperty, value);
    }

    public bool ButtonHapticFeedback
    {
        get => (bool)this.GetValue(ButtonHapticFeedbackProperty);
        set => this.SetValue(ButtonHapticFeedbackProperty, value);
    }

    public bool DirectionalHapticFeedback
    {
        get => (bool)this.GetValue(DirectionalHapticFeedbackProperty);
        set => this.SetValue(DirectionalHapticFeedbackProperty, value);
    }

    public bool HideWhenControllerConnected
    {
        get => (bool)this.GetValue(HideWhenControllerConnectedProperty);
        set => this.SetValue(HideWhenControllerConnectedProperty, value);
    }

    public double IdleOpacity
    {
        get => (double)this.GetValue(IdleOpacityProperty);
        set => this.SetValue(IdleOpacityProperty, value);
    }

    public TimeSpan IdleDelay
    {
        get => (TimeSpan)this.GetValue(IdleDelayProperty);
        set => this.SetValue(IdleDelayProperty, value);
    }

    public bool PassThrough
    {
        get => (bool)this.GetValue(PassThroughProperty);
        set => this.SetValue(PassThroughProperty, value);
    }

    public bool? ShowBody
    {
        get => (bool?)this.GetValue(ShowBodyProperty);
        set => this.SetValue(ShowBodyProperty, value);
    }

    public int? PlayerIndex
    {
        get => (int?)this.GetValue(PlayerIndexProperty);
        set => this.SetValue(PlayerIndexProperty, value);
    }

    /// <summary>Fill of an unpressed element.</summary>
    public Color ButtonColor
    {
        get => (Color)this.GetValue(ButtonColorProperty);
        set => this.SetValue(ButtonColorProperty, value);
    }

    /// <summary>Fill of a pressed element, and of a stick's knob.</summary>
    public Color PressedColor
    {
        get => (Color)this.GetValue(PressedColorProperty);
        set => this.SetValue(PressedColorProperty, value);
    }

    /// <summary>Labels and glyphs the face style leaves uncoloured.</summary>
    public Color LabelColor
    {
        get => (Color)this.GetValue(LabelColorProperty);
        set => this.SetValue(LabelColorProperty, value);
    }

    /// <summary>Element outlines.</summary>
    public Color OutlineColor
    {
        get => (Color)this.GetValue(OutlineColorProperty);
        set => this.SetValue(OutlineColorProperty, value);
    }

    /// <summary>The controller body in Uniform sizing.</summary>
    public Color BodyColor
    {
        get => (Color)this.GetValue(BodyColorProperty);
        set => this.SetValue(BodyColorProperty, value);
    }

    /// <summary>Edit-mode outlines and a clicked stick's ring.</summary>
    public Color AccentColor
    {
        get => (Color)this.GetValue(AccentColorProperty);
        set => this.SetValue(AccentColorProperty, value);
    }


    void ApplyLayout()
    {
        if (this.suppressLayoutApply)
            return;

        // the engine edits the layout it is given in place, so a caller's instance is copied - a
        // player rearranging the pad must never rewrite the object a view model is holding
        this.engine.Layout = this.ControllerLayout?.Clone() ?? GamepadLayouts.Get(this.Preset);
    }


    void OnHideWhenControllerConnectedChanged(bool hide)
    {
        if (hide && this.manager != null)
            _ = this.manager.StartPhysicalWatch();

        this.UpdateStepAside();
    }
}

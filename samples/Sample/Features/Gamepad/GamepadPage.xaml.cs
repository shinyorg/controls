using Shiny.Controls.Gaming;
using Shiny.Gamepad;
using Shiny.Maui.Controls.Gaming;

namespace Sample.Features.Gamepad;


public partial class GamepadPage : Shiny.Maui.Controls.ShinyContentPage
{
    readonly IDispatcherTimer loop;
    double x = 0.5, y = 0.4;
    int fieldTaps;
    string lastEvent = "-";


    public GamepadPage()
    {
        InitializeComponent();
        SampleSourceCode.Attach(this);

        PresetPicker.ItemsSource = Enum.GetNames<GamepadPreset>();
        PresetPicker.SelectedIndex = (int)GamepadPreset.Standard;
        FacePicker.ItemsSource = new[] { "Layout default" }.Concat(Enum.GetNames<GamepadFaceStyle>()).ToList();
        FacePicker.SelectedIndex = 0;

        // a real game loop: read the state every frame - the same code a physical controller drives
        loop = Dispatcher.CreateTimer();
        loop.Interval = TimeSpan.FromMilliseconds(16);
        loop.Tick += (_, _) => Tick();
    }


    protected override void OnAppearing()
    {
        base.OnAppearing();
        loop.Start();
    }


    protected override void OnDisappearing()
    {
        loop.Stop();
        base.OnDisappearing();
    }


    void Tick()
    {
        var state = Pad.Gamepad.GetState();
        var move = state.GetMovement();
        var dpad = state.DPad;

        x = Math.Clamp(x + (move.X + dpad.X) * 0.012, 0.03, 0.97);
        y = Math.Clamp(y - (move.Y + dpad.Y) * 0.012, 0.05, 0.95);

        var size = state.IsPressed(GamepadButton.A) ? 48 : 28;
        Player.WidthRequest = Player.HeightRequest = size;
        Player.Fill = state.IsPressed(GamepadButton.B) ? Color.FromArgb("#F472B6")
            : state.IsPressed(GamepadButton.X) ? Color.FromArgb("#A3E635")
            : Color.FromArgb("#38BDF8");

        AbsoluteLayout.SetLayoutBounds(Player, new Rect(x, y, size, size));
        AbsoluteLayout.SetLayoutFlags(Player, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.PositionProportional);

        Hud.Text =
            $"Buttons: {(state.Buttons == GamepadButton.None ? "-" : state.Buttons)}\n" +
            $"Left {state.LeftStick.X:0.00}, {state.LeftStick.Y:0.00}  Right {state.RightStick.X:0.00}, {state.RightStick.Y:0.00}\n" +
            $"Last: {lastEvent}   Field taps (pass-through): {fieldTaps}";
    }


    void OnButtonChanged(object? sender, GamepadButtonChangedEventArgs e)
        => lastEvent = $"{e.Button} {(e.IsPressed ? "pressed" : "released")}";


    void OnFieldTapped(object? sender, TappedEventArgs e) => fieldTaps++;


    void OnPresetChanged(object? sender, EventArgs e) => ApplyLayout();


    void OnTweakToggled(object? sender, ToggledEventArgs e) => ApplyLayout();


    void ApplyLayout()
    {
        if (PresetPicker.SelectedIndex < 0)
            return;

        var preset = (GamepadPreset)PresetPicker.SelectedIndex;
        Pad.Preset = preset;

        if (!FloatingSwitch.IsToggled && !TurboSwitch.IsToggled)
        {
            Pad.ControllerLayout = null;
            return;
        }

        var layout = GamepadLayouts.Get(preset);
        var stick = layout.Elements.FirstOrDefault(el => el.Kind == GamepadElementKind.Stick);
        if (stick != null)
            stick.IsFloating = FloatingSwitch.IsToggled;

        var bottom = layout.Elements.FirstOrDefault(el => el.Button == GamepadButton.A);
        if (bottom != null)
            bottom.IsTurbo = TurboSwitch.IsToggled;

        Pad.ControllerLayout = layout;
    }


    void OnFaceChanged(object? sender, EventArgs e)
        => Pad.FaceStyle = FacePicker.SelectedIndex <= 0 ? null : (GamepadFaceStyle)(FacePicker.SelectedIndex - 1);


    void OnInlineToggled(object? sender, ToggledEventArgs e)
    {
        // overlay: the pad fills the whole stage over the field; inline: it gets a row of its own
        Pad.Sizing = e.Value ? GamepadSizing.Uniform : GamepadSizing.Anchored;
        Stage.RowDefinitions[1].Height = e.Value ? new GridLength(240) : new GridLength(0);
        Grid.SetRow(Pad, e.Value ? 1 : 0);
    }


    void OnFadeToggled(object? sender, ToggledEventArgs e) => Pad.IdleOpacity = e.Value ? 0.35 : 1.0;


    void OnEditToggled(object? sender, ToggledEventArgs e) => Pad.IsEditing = e.Value;
}

using Shiny.Maui.Controls.Keyframe;

namespace Sample.Features.Keyframe;

public partial class MotionPage : ContentPage
{
    readonly Random random = new();

    public MotionPage()
    {
        InitializeComponent();
        BindingContext = this;
    }

    public double BoxOpacity { get; private set; } = 1d;
    public double BoxScale { get; private set; } = 1d;
    public double BoxRotation { get; private set; }
    public bool IsBusy { get; private set; }
    public string BusyText => IsBusy ? "Working… (tap to stop)" : "Start work";

    void OnReplayCards(object? sender, EventArgs e) => Animate.GetChoreographyPlayer(Cards)?.Play();

    void OnState(object? sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: string state })
        {
            VisualStateManager.GoToState(StatusCard, state);
            StatusLabel.Text = state;
        }
    }

    void OnShuffle(object? sender, EventArgs e)
    {
        BoxOpacity = 0.3d + random.NextDouble() * 0.7d;
        BoxScale = 0.6d + random.NextDouble() * 0.8d;
        BoxRotation = random.Next(-180, 180);
        OnPropertyChanged(nameof(BoxOpacity));
        OnPropertyChanged(nameof(BoxScale));
        OnPropertyChanged(nameof(BoxRotation));
    }

    void OnToggleBusy(object? sender, EventArgs e)
    {
        IsBusy = !IsBusy;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(BusyText));
    }

    async void OnPop(object? sender, EventArgs e)
    {
        if (Resources["PopIn"] is IAnimationDefinition popIn)
            await PopCard.PlayAsync(popIn);
    }
}

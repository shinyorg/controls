using Shiny.Maui.Controls;

namespace Sample.Features.ScratchCard;

public partial class ScratchCardPage : ContentPage
{
    public ScratchCardPage()
    {
        InitializeComponent();
        SampleSourceCode.Attach(this);
        this.BindingContext = this;
    }


    void OnProgress(object? sender, ScratchProgressEventArgs e)
        => this.ProgressLabel.Text = $"Scratched: {e.Percent:0}%";

    void OnRevealed(object? sender, EventArgs e)
        => this.ProgressLabel.Text = "Revealed!";

    void OnImageThreshold(object? sender, ScratchProgressEventArgs e)
        => this.ImageThresholdLabel.Text = $"ThresholdReached fired at {e.Percent:0}%";

    void OnReveal(object? sender, EventArgs e) => this.PrizeCard.Reveal();

    void OnReset(object? sender, EventArgs e) => this.PrizeCard.Reset();

    async void OnClaim(object? sender, EventArgs e)
        => await this.DisplayAlertAsync("Claimed", "500 points are yours.", "OK");
}

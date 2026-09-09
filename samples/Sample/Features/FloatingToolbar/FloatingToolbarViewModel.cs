using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Shiny;
using Shiny.Maui.Controls;

namespace Sample.Features.FloatingToolbar;

[ShellMap<FloatingToolbarPage>(registerRoute: false)]
public partial class FloatingToolbarViewModel : ObservableObject
{
    [ObservableProperty]
    string lastAction = "Nothing clicked yet";

    [ObservableProperty]
    bool isVertical;

    [ObservableProperty]
    bool showLabels;

    /// <summary>The same enum the tooltip animates with — one vocabulary, not two.</summary>
    static readonly string[] AnimationNames = ["Scale", "Fade", "Slide", "None"];

    [ObservableProperty]
    string selectedAnimation = "Scale";

    public TooltipAnimation Animation => Enum.TryParse<TooltipAnimation>(this.SelectedAnimation, out var value)
        ? value
        : TooltipAnimation.Scale;

    partial void OnSelectedAnimationChanged(string value) => this.OnPropertyChanged(nameof(this.Animation));

    [RelayCommand]
    void CycleAnimation()
    {
        var next = Array.IndexOf(AnimationNames, this.SelectedAnimation) + 1;
        this.SelectedAnimation = AnimationNames[next % AnimationNames.Length];
    }

    public ToolbarOrientation Orientation => this.IsVertical
        ? ToolbarOrientation.Vertical
        : ToolbarOrientation.Horizontal;

    partial void OnIsVerticalChanged(bool value) => this.OnPropertyChanged(nameof(this.Orientation));

    [RelayCommand]
    void ItemClicked(FloatingToolbarItemEventArgs? args)
    {
        if (args is null)
            return;

        // Context is the target's BindingContext, which is the row a shared bar was acting on.
        var on = args.Context as string ?? "the target";
        this.LastAction = $"{args.Item.Text} on {on}";
    }
}

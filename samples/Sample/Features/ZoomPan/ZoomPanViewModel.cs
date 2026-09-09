using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Shiny;

namespace Sample.Features.ZoomPan;

[ShellMap<ZoomPanPage>(registerRoute: false)]
public partial class ZoomPanViewModel : ObservableObject
{
    /// <summary>Two-way with the control: the gestures write it and the buttons below read it.</summary>
    [ObservableProperty]
    double zoomLevel = 1;

    [ObservableProperty]
    bool isZoomEnabled = true;

    [ObservableProperty]
    bool doubleTapToZoom = true;

    [ObservableProperty]
    int taps;

    [ObservableProperty]
    string typed = String.Empty;

    /// <summary>Proves the content is still live at every scale, not a picture of itself.</summary>
    [RelayCommand]
    void Tap() => this.Taps++;

    [RelayCommand]
    void ZoomIn() => this.ZoomLevel = 2;

    [RelayCommand]
    void ResetZoom() => this.ZoomLevel = 1;
}

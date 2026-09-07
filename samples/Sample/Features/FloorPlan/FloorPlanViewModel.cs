using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shiny.Controls.FloorPlan;

namespace Sample.Features.FloorPlan;

/// <summary>
/// Everything the editor demo binds. Note what is *not* here: no engine, no camera, no surface size.
/// Which tool is live and what is selected are state a view model can own; the viewport is not.
/// </summary>
public partial class FloorPlanViewModel : ObservableObject
{
    [ObservableProperty] FloorPlanDocument document = SampleFloorPlan.CreateOffice();
    [ObservableProperty] IFloorPlanTool activeTool = new SelectTool();
    [ObservableProperty] FloorPlanElement? selectedElement;
    [ObservableProperty] bool showGrid = true;
    [ObservableProperty] bool snapToGrid = true;

    public string Status => this.SelectedElement switch
    {
        CubicleElement { Occupant: { Length: > 0 } occupant } cubicle => $"{cubicle.Name} - {occupant}",
        { } element when element.Name.Length > 0 => element.Name,
        { } element => element.GetType().Name.Replace("Element", String.Empty),
        _ => this.ActiveTool.Name
    };

    /// <remarks>
    /// Both of these feed the same status line, so both have to poke it - MVVM Toolkit only raises
    /// the property that changed, and a computed property has to be told about its inputs.
    /// </remarks>
    partial void OnSelectedElementChanged(FloorPlanElement? value) => this.OnPropertyChanged(nameof(this.Status));

    partial void OnActiveToolChanged(IFloorPlanTool value) => this.OnPropertyChanged(nameof(this.Status));

    [RelayCommand]
    void SelectTool() => this.ActiveTool = new SelectTool();

    [RelayCommand]
    void PanTool() => this.ActiveTool = new PanTool();

    [RelayCommand]
    void DrawRoom() => this.ActiveTool = new DrawRoomTool();

    [RelayCommand]
    void DrawWall() => this.ActiveTool = new DrawWallTool();

    [RelayCommand]
    void PlaceCubicle() => this.ActiveTool = new PlaceElementTool { Kind = FloorPlanElementKind.Cubicle };

    [RelayCommand]
    void PlaceDoor() => this.ActiveTool = new PlaceElementTool { Kind = FloorPlanElementKind.Door };

    [RelayCommand]
    void PlaceFurniture(string? kind) =>
        this.ActiveTool = new PlaceElementTool(Enum.TryParse<FurnitureKind>(kind, out var parsed) ? parsed : FurnitureKind.Desk);

    [RelayCommand]
    void PlaceOutlet(string? type) =>
        this.ActiveTool = new PlaceElementTool(Enum.TryParse<OutletType>(type, out var parsed) ? parsed : OutletType.Standard);
}

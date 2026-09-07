using Shiny.Controls.FloorPlan;
using Shiny.Maui.Controls.FloorPlan;

namespace Sample.Features.FloorPlan;

public partial class SeatingChartPage : ContentPage
{
    public SeatingChartPage()
    {
        this.InitializeComponent();
        SampleSourceCode.Attach(this);

        this.Plan.Document = SampleFloorPlan.CreateOffice();
        this.Plan.ZoomToFit();
    }

    void OnElementTapped(object? sender, FloorPlanElementEventArgs e) =>
        this.Caption.Text = e.Element switch
        {
            CubicleElement { Occupant: { Length: > 0 } occupant } desk => $"{desk.Name} - {occupant}",
            RoomElement room => room.Label ?? room.Name,
            { Name.Length: > 0 } element => element.Name,
            var element => element.GetType().Name.Replace("Element", String.Empty)
        };
}

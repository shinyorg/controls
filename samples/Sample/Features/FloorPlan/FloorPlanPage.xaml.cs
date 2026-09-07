namespace Sample.Features.FloorPlan;

public partial class FloorPlanPage : ContentPage
{
    public FloorPlanPage()
    {
        this.InitializeComponent();
        this.BindingContext = new FloorPlanViewModel();
        SampleSourceCode.Attach(this);

        // The surface has no size yet. ZoomToFit holds the request and applies it on the first frame.
        this.Plan.ZoomToFit();
    }

    void OnDelete(object? sender, EventArgs e) => this.Plan.DeleteSelected();

    void OnBringToFront(object? sender, EventArgs e) => this.Plan.BringToFront();

    void OnSendToBack(object? sender, EventArgs e) => this.Plan.SendToBack();

    void OnZoomIn(object? sender, EventArgs e) => this.Plan.ZoomIn();

    void OnZoomOut(object? sender, EventArgs e) => this.Plan.ZoomOut();

    void OnFit(object? sender, EventArgs e) => this.Plan.ZoomToFit();
}

using Shiny.Maui.Controls;

namespace Sample.Features.Resizable;

public partial class ResizablePage : ContentPage
{
    public ResizablePage()
    {
        InitializeComponent();
        SampleSourceCode.Attach(this);
        this.BindingContext = this;
    }


    void OnLayoutChanged(object? sender, ResizableLayoutChangedEventArgs e)
        => this.StatusLabel.Text = "Sizes: " + String.Join(" · ", e.Sizes.Select(x => x.ToString("0")));

    void OnToggle(object? sender, EventArgs e)
    {
        if (this.Explorer.IsCollapsed)
            this.Explorer.Expand();
        else
            this.Explorer.Collapse();
    }

    void OnExplorerCollapsed(object? sender, EventArgs e) => this.ToggleButton.Text = "Expand explorer";

    void OnExplorerExpanded(object? sender, EventArgs e) => this.ToggleButton.Text = "Collapse explorer";

    void OnReset(object? sender, EventArgs e) => this.Outer.SetSizes(22, 58, 20);
}

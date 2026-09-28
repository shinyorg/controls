using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Shiny.Controls.Office.Shell;

namespace Shiny.Blazor.Controls.Office;

/// <summary>
/// The Office status bar: editor-fed segments on the left; Focus, view modes and zoom on the right.
/// </summary>
public partial class OfficeStatusBar : ComponentBase, IDisposable
{
    bool zoomDialogOpen;
    IReadOnlyList<OfficeStatusItem>? observedItems;
    string? localViewMode;

    [CascadingParameter] public OfficeShell? Shell { get; set; }

    /// <summary>The app, for the default view modes. Null takes the shell's.</summary>
    [Parameter] public OfficeApp? App { get; set; }

    /// <summary>
    /// The left-hand segments. Observable items redraw when their text changes; an
    /// <see cref="INotifyCollectionChanged"/> list redraws when segments come and go.
    /// </summary>
    [Parameter] public IReadOnlyList<OfficeStatusItem>? Items { get; set; }

    /// <summary>Raised when a segment marked <see cref="OfficeStatusItem.IsClickable"/> is pressed.</summary>
    [Parameter] public EventCallback<OfficeStatusItem> ItemClicked { get; set; }

    [Parameter] public bool ShowFocus { get; set; } = true;

    /// <summary>Raised when Focus is pressed. Inside a shell it also toggles the shell's focus mode.</summary>
    [Parameter] public EventCallback FocusRequested { get; set; }

    /// <summary>The view-mode buttons. Null uses the app's three; empty hides them.</summary>
    [Parameter] public IReadOnlyList<OfficeViewMode>? ViewModes { get; set; }

    /// <summary>The <see cref="OfficeViewMode.Id"/> that is on. Two-way bindable.</summary>
    [Parameter] public string? SelectedViewMode { get; set; }

    [Parameter] public EventCallback<string?> SelectedViewModeChanged { get; set; }

    [Parameter] public bool ShowViewModes { get; set; } = true;

    [Parameter] public bool ShowZoom { get; set; } = true;

    [Parameter] public bool ShowZoomSlider { get; set; } = true;

    /// <summary>The zoom factor, 1 = 100%. Two-way bindable.</summary>
    [Parameter] public double Zoom { get; set; } = 1;

    [Parameter] public EventCallback<double> ZoomChanged { get; set; }

    /// <summary>Range, snap points and step. Default 10–500%, snapping at 100%.</summary>
    [Parameter] public OfficeZoomModel? ZoomModel { get; set; }

    /// <summary>The page's width at 100%, for the dialog's fit presets. Same unit as the viewport.</summary>
    [Parameter] public double PageWidth { get; set; }

    [Parameter] public double PageHeight { get; set; }

    [Parameter] public double TextWidth { get; set; }

    [Parameter] public double ViewportWidth { get; set; }

    [Parameter] public double ViewportHeight { get; set; }

    /// <summary>
    /// PowerPoint's "Fit slide to current window" button after the percentage. Off by default — Word and
    /// Excel have none.
    /// </summary>
    [Parameter] public bool ShowFitToWindow { get; set; }

    /// <summary>Draws the fit button pressed: the zoom is following the window rather than a set percentage.</summary>
    [Parameter] public bool IsFitted { get; set; }

    /// <summary>The fit button's tooltip.</summary>
    [Parameter] public string FitToWindowText { get; set; } = "Fit slide to current window";

    /// <summary>The fit button was pressed. The host works out the zoom and feeds it back through <see cref="Zoom"/>.</summary>
    [Parameter] public EventCallback FitToWindowRequested { get; set; }

    /// <summary>Hides Focus, the view modes and the slider. Null follows the shell.</summary>
    [Parameter] public bool? Compact { get; set; }

    [Parameter] public string? CssClass { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }


    internal OfficeZoomModel Model => this.ZoomModel ?? OfficeZoomModel.Default;

    internal bool IsCompact => this.Compact ?? this.Shell?.ShellLayout.IsCompact ?? false;

    internal IReadOnlyList<OfficeViewMode> EffectiveViewModes
        => this.ViewModes ?? OfficeAppInfo.For(this.App ?? this.Shell?.App ?? OfficeApp.Word).ViewModes;

    internal string? EffectiveViewMode
        => this.SelectedViewMode ?? this.localViewMode ?? OfficeViewModes.DefaultId(this.App ?? this.Shell?.App ?? OfficeApp.Word);

    internal bool IsZoomDialogOpen => this.zoomDialogOpen;

    string SliderValue => Math.Round(this.Model.ToSlider(this.Zoom) * 1000).ToString(CultureInfo.InvariantCulture);

    /// <summary>The notch at 100% on the slider track.</summary>
    string TickStyle => $"left:{(this.Model.ToSlider(1) * 100).ToString("0.##", CultureInfo.InvariantCulture)}%";

    string RootCss
        => string.Join(' ', new[] { "office-status", this.IsCompact ? "is-compact" : null, this.CssClass }.Where(x => x is not null));

    string? RootStyle
        => this.App is not null || this.Shell is null
            ? OfficeShellStyle.AccentVariables(OfficeAppInfo.For(this.App ?? OfficeApp.Word))
            : null;


    protected override void OnParametersSet()
    {
        if (ReferenceEquals(this.observedItems, this.Items))
            return;

        this.Unobserve();
        this.observedItems = this.Items;

        if (this.observedItems is INotifyCollectionChanged collection)
            collection.CollectionChanged += this.OnItemsChanged;

        foreach (var item in this.observedItems ?? [])
            item.PropertyChanged += this.OnItemChanged;
    }

    void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Re-subscribe from scratch: cheaper to reason about than tracking adds and removes, and a
        // status bar has a handful of segments.
        var items = this.observedItems;
        this.Unobserve();
        this.observedItems = items;

        if (items is INotifyCollectionChanged collection)
            collection.CollectionChanged += this.OnItemsChanged;

        foreach (var item in items ?? [])
            item.PropertyChanged += this.OnItemChanged;

        _ = this.InvokeAsync(this.StateHasChanged);
    }

    void OnItemChanged(object? sender, PropertyChangedEventArgs e) => _ = this.InvokeAsync(this.StateHasChanged);

    void Unobserve()
    {
        if (this.observedItems is INotifyCollectionChanged collection)
            collection.CollectionChanged -= this.OnItemsChanged;

        foreach (var item in this.observedItems ?? [])
            item.PropertyChanged -= this.OnItemChanged;

        this.observedItems = null;
    }


    /// <summary>Sets the zoom (clamped to the model's range) and raises <see cref="ZoomChanged"/>.</summary>
    public async Task SetZoomAsync(double zoom)
    {
        var clamped = this.Model.Clamp(zoom);
        if (Math.Abs(clamped - this.Zoom) < 0.0001)
            return;

        this.Zoom = clamped;
        await this.ZoomChanged.InvokeAsync(clamped);
    }

    public Task ZoomInAsync() => this.SetZoomAsync(this.Model.StepZoom(this.Zoom, 1));

    public Task ZoomOutAsync() => this.SetZoomAsync(this.Model.StepZoom(this.Zoom, -1));

    /// <summary>Sets the zoom from a slider position (0–1), snapping at 100%.</summary>
    public Task SetZoomFromSliderAsync(double position) => this.SetZoomAsync(this.Model.FromSlider(position));

    public void OpenZoomDialog() => this.zoomDialogOpen = true;

    /// <summary>Presses the fit button. Test seam.</summary>
    public Task FitToWindowAsync() => this.FitToWindowRequested.InvokeAsync();

    async Task OnSliderAsync(ChangeEventArgs e)
    {
        if (double.TryParse(e.Value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var raw))
            await this.SetZoomFromSliderAsync(raw / 1000);
    }

    public async Task SelectViewModeAsync(string id)
    {
        if (id == this.EffectiveViewMode)
            return;

        this.localViewMode = id;
        this.SelectedViewMode = id;
        await this.SelectedViewModeChanged.InvokeAsync(id);
    }

    public async Task FocusAsync()
    {
        await this.FocusRequested.InvokeAsync();

        if (this.Shell is { } shell)
            await shell.ToggleFocusModeAsync();
    }

    public void Dispose() => this.Unobserve();
}

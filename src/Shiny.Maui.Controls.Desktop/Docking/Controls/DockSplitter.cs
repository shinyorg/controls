using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Shiny.Maui.Controls.Desktop.Docking;

/// <summary>
/// Draggable splitter between two adjacent dock children. Reports its position
/// as a 0..1 ratio of the parent's extent so layouts survive resize.
/// </summary>
public class DockSplitter : ContentView
{
    public const double Thickness = 5;

    readonly BoxView bar;
    double startRatio;
    bool dragging;

    public static readonly BindableProperty OrientationProperty = BindableProperty.Create(
        nameof(Orientation), typeof(DockOrientation), typeof(DockSplitter), DockOrientation.Horizontal);

    public DockOrientation Orientation
    {
        get => (DockOrientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public static readonly BindableProperty RatioProperty = BindableProperty.Create(
        nameof(Ratio), typeof(double), typeof(DockSplitter), 0.5);

    public double Ratio
    {
        get => (double)GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    public static readonly BindableProperty IsLockedProperty = BindableProperty.Create(
        nameof(IsLocked), typeof(bool), typeof(DockSplitter), false);

    public bool IsLocked
    {
        get => (bool)GetValue(IsLockedProperty);
        set => SetValue(IsLockedProperty, value);
    }

    /// <summary>Raised continuously while dragging with the live ratio.</summary>
    public event EventHandler<double>? RatioChanging;

    /// <summary>Raised once at the end of a drag with the final ratio.</summary>
    public event EventHandler<double>? RatioCommitted;

    /// <summary>The extent (width or height) of the area the splitter divides. Set by the host.</summary>
    public Func<double>? ExtentProvider { get; set; }

    public DockSplitter()
    {
        BackgroundColor = Colors.Transparent;
        bar = new BoxView
        {
            CornerRadius = 2,
            Opacity = 0
        }.Tint(BoxView.ColorProperty, Shiny.Maui.Controls.Themes.ShinyThemeKeys.Color.Primary);
        Content = bar;

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPan;
        GestureRecognizers.Add(pan);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => { if (!IsLocked) bar.Opacity = 0.55; };
        pointer.PointerExited += (_, _) => { if (!dragging) bar.Opacity = 0; };
        GestureRecognizers.Add(pointer);
    }

    void OnPan(object? sender, PanUpdatedEventArgs e)
    {
        if (IsLocked) return;
        var extent = ExtentProvider?.Invoke() ?? 0;
        if (extent <= 0) return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                startRatio = Ratio;
                dragging = true;
                bar.Opacity = 0.55;
                break;
            case GestureStatus.Running:
            {
                var delta = Orientation == DockOrientation.Horizontal ? e.TotalX : e.TotalY;
                var ratio = Math.Clamp(startRatio + delta / extent, 0.08, 0.92);
                Ratio = ratio;
                RatioChanging?.Invoke(this, ratio);
                break;
            }
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                dragging = false;
                bar.Opacity = 0;
                RatioCommitted?.Invoke(this, Ratio);
                break;
        }
    }
}

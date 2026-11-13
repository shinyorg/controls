using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>
/// The divider between two panels of a <see cref="ResizablePanelGroup"/>. Drag it to move space from
/// one side to the other.
/// </summary>
/// <remarks>
/// The visible line is one unit thick, and that is all the group lays out for it; the touch target is
/// <see cref="HitSize"/> wide, centred on the line and laid over the panels either side, so a finger
/// can find it without the layout giving up a gutter.
/// </remarks>
public class ResizableHandle : ContentView
{
    /// <summary>The width of the visible line, which is what the group lays out for the handle.</summary>
    public const double LineThickness = 1;

    /// <summary>How wide the touch target is, centred on the line.</summary>
    public const double HitSize = 16;

    readonly BoxView line;
    readonly Border grip;
    readonly Grid gripDots;
    readonly DragTouchHook touchHook;
    ResizableDirection direction = ResizableDirection.Horizontal;
    bool hovered;
    bool dragging;

    public ResizableHandle()
    {
        this.BackgroundColor = Colors.Transparent;

        this.line = new BoxView();
        this.gripDots = new Grid { RowSpacing = 2, ColumnSpacing = 2, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        for (var i = 0; i < 6; i++)
        {
            var dot = new BoxView { WidthRequest = 2, HeightRequest = 2, CornerRadius = 1 };
            dot.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
            this.gripDots.Add(dot);
        }

        this.grip = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 3 },
            Padding = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false,
            Content = this.gripDots
        };
        ThemeBrush.Apply(this.grip, Border.StrokeProperty, ShinyThemeKeys.Brush.OutlineVariant);
        ThemeBrush.Apply(this.grip, Border.BackgroundProperty, ShinyThemeKeys.Brush.SurfaceContainerHigh);

        this.Content = new Grid { Children = { this.line, this.grip } };
        this.ApplyDirection();
        this.ApplyLineColor();

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += this.OnPanUpdated;
        this.GestureRecognizers.Add(pan);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => { this.hovered = true; this.ApplyLineColor(); };
        pointer.PointerExited += (_, _) => { this.hovered = false; this.ApplyLineColor(); };
        this.GestureRecognizers.Add(pointer);

        // A pan inside a ScrollView is taken over by the scroller as soon as the finger moves; hold the
        // scroller off from the raw touch-down for as long as the handle is held.
        this.touchHook = new DragTouchHook(this)
        {
            Pressed = () =>
            {
                if (this.IsEnabled && this.Group is not null)
                    this.touchHook!.LockScroller(true);
            },
            Released = () => this.touchHook!.LockScroller(false)
        };

        SemanticProperties.SetDescription(this, "Resize handle");
    }

    internal ResizablePanelGroup? Group { get; set; }

    public static readonly BindableProperty WithHandleProperty = BindableProperty.Create(
        nameof(WithHandle), typeof(bool), typeof(ResizableHandle), false,
        propertyChanged: (b, _, n) => ((ResizableHandle)b).grip.IsVisible = (bool)n);

    /// <summary>Shows a grip in the middle of the line. The handle drags either way; this is only the cue.</summary>
    public bool WithHandle
    {
        get => (bool)this.GetValue(WithHandleProperty);
        set => this.SetValue(WithHandleProperty, value);
    }

    public static readonly BindableProperty LineColorProperty = BindableProperty.Create(
        nameof(LineColor), typeof(Color), typeof(ResizableHandle), null,
        propertyChanged: (b, _, _) => ((ResizableHandle)b).ApplyLineColor());

    /// <summary>Leave unset to follow the theme's outline-variant colour (primary while hovered or dragged).</summary>
    public Color? LineColor
    {
        get => (Color?)this.GetValue(LineColorProperty);
        set => this.SetValue(LineColorProperty, value);
    }

    internal void SetDirection(ResizableDirection value)
    {
        if (this.direction == value)
            return;

        this.direction = value;
        this.ApplyDirection();
    }

    void ApplyDirection()
    {
        var horizontal = this.direction == ResizableDirection.Horizontal;

        // The line runs across the group: a vertical line between side-by-side panels.
        this.line.WidthRequest = horizontal ? LineThickness : -1;
        this.line.HeightRequest = horizontal ? -1 : LineThickness;
        this.line.HorizontalOptions = horizontal ? LayoutOptions.Center : LayoutOptions.Fill;
        this.line.VerticalOptions = horizontal ? LayoutOptions.Fill : LayoutOptions.Center;

        this.grip.WidthRequest = horizontal ? 12 : 18;
        this.grip.HeightRequest = horizontal ? 18 : 12;

        this.gripDots.RowDefinitions.Clear();
        this.gripDots.ColumnDefinitions.Clear();
        var rows = horizontal ? 3 : 2;
        var columns = horizontal ? 2 : 3;
        for (var r = 0; r < rows; r++)
            this.gripDots.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var c = 0; c < columns; c++)
            this.gripDots.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var i = 0; i < this.gripDots.Count; i++)
        {
            var dot = (BindableObject)this.gripDots[i];
            Grid.SetRow(dot, i / columns);
            Grid.SetColumn(dot, i % columns);
        }
    }

    void ApplyLineColor()
    {
        if (this.LineColor is { } color)
        {
            this.line.RemoveDynamicResource(BoxView.ColorProperty);
            this.line.Color = color;
            return;
        }

        var active = (this.hovered || this.dragging) && this.IsEnabled;
        this.line.SetDynamicResource(BoxView.ColorProperty, active ? ShinyThemeKeys.Color.Primary : ShinyThemeKeys.Color.OutlineVariant);
    }

    void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (this.Group is not { } group || !this.IsEnabled)
            return;

        var distance = this.direction == ResizableDirection.Horizontal ? e.TotalX : e.TotalY;
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                this.dragging = true;
                this.ApplyLineColor();
                group.BeginDrag(this);
                break;

            case GestureStatus.Running:
                group.Drag(this, distance);
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                this.dragging = false;
                this.ApplyLineColor();
                this.touchHook.LockScroller(false);
                group.EndDrag(this);
                break;
        }
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == IsEnabledProperty.PropertyName)
            this.ApplyLineColor();
    }
}

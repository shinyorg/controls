using System.Windows.Input;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.ColorPicker;

public class ColorPickerButton : ContentView
{
    readonly Border buttonBorder;
    readonly Label buttonLabel;
    readonly ColorPicker picker;
    readonly Grid overlayGrid;
    readonly Button doneButton;

    bool isOpen;
    Color openedColor;

    public ColorPickerButton()
    {
        buttonLabel = new Label
        {
            FontSize = 13,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

        buttonBorder = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerSmallRadius),
            Padding = new Thickness(12, 6),
            MinimumWidthRequest = 44,
            MinimumHeightRequest = 36,
            BackgroundColor = Colors.Red,
            Content = buttonLabel,
            HorizontalOptions = LayoutOptions.Start
        }.WithStrokeThickness(ShinyThemeKeys.Border.Medium);

        buttonBorder.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Color.OutlineVariant);

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnButtonTapped;
        buttonBorder.GestureRecognizers.Add(tap);

        picker = new ColorPicker();
        picker.ColorChanged += OnPickerColorChanged;

        doneButton = new Button
        {
            CornerRadius = 8,
            HeightRequest = 36,
            HorizontalOptions = LayoutOptions.Fill,
            Margin = new Thickness(12, 0, 12, 12)
        }.Neutralize().WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);
        doneButton.SetBinding(Button.TextProperty, new Binding(nameof(DoneText), source: this));
        doneButton.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);
        doneButton.SetDynamicResource(Button.BackgroundColorProperty, ShinyThemeKeys.Color.Primary);
        doneButton.Clicked += (_, _) => Close();

        var popupBorder = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerMediumRadius),
            Padding = 0,
            Content = new VerticalStackLayout
            {
                Children = { picker, doneButton }
            },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        }
        .WithStrokeThickness(ShinyThemeKeys.Border.Thin)
        .WithElevation(ShinyThemeKeys.Elevation.Level4);

        popupBorder.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Color.OutlineVariant);
        popupBorder.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerLowest);

        var backdrop = new BoxView
        {
            Opacity = 0.3,
            InputTransparent = false
        };
        backdrop.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.Scrim);
        var backdropTap = new TapGestureRecognizer();
        backdropTap.Tapped += (_, _) => CancelAndClose();
        backdrop.GestureRecognizers.Add(backdropTap);

        overlayGrid = new Grid
        {
            InputTransparent = false,
            CascadeInputTransparent = false,
            Children = { backdrop, popupBorder }
        };

        Content = buttonBorder;
        UpdateButtonColor(SelectedColor);
        picker.ShowOpacity = ShowOpacity;

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(ColorPickerButton));
    }

    /// <summary>
    /// Keeps the trigger the width the host asked for.
    /// </summary>
    /// <remarks>
    /// The trigger is <see cref="LayoutOptions.Start"/> by default so that, dropped into a stack with
    /// no width of its own, it shrink-wraps its label rather than stretching across the whole row. That
    /// leaves it at its minimum, though, and a host that pins a <see cref="VisualElement.WidthRequest"/>
    /// - a toolbar sizing its controls to a common width, say - got the width it asked for on this
    /// element and a trigger still drawn at the minimum inside it, with the rest showing as a hole
    /// beside the button. Nothing overflows and nothing is clipped, so it reads as stray padding.
    /// </remarks>
    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        // WhenReady, because a WidthRequest arriving from a Style is applied before this constructor's
        // body has run and there is no trigger to align yet.
        if (propertyName == WidthRequestProperty.PropertyName)
            StyleGuard.WhenReady<ColorPickerButton>(this, self => self.buttonBorder.HorizontalOptions = self.WidthRequest >= 0
                ? LayoutOptions.Fill
                : LayoutOptions.Start);
    }

    // Properties

    public static readonly BindableProperty SelectedColorProperty = BindableProperty.Create(
        nameof(SelectedColor),
        typeof(Color),
        typeof(ColorPickerButton),
        Colors.Red,
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ColorPickerButton), () =>
            {
                ((ColorPickerButton)b).OnSelectedColorChanged((Color)n);
            }));

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(ColorPickerButton),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ColorPickerButton), () =>
            {
                ((ColorPickerButton)b).buttonLabel.Text = (string?)n;
            }));

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly BindableProperty ShowOpacityProperty = BindableProperty.Create(
        nameof(ShowOpacity),
        typeof(bool),
        typeof(ColorPickerButton),
        false,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ColorPickerButton), () =>
            {
                ((ColorPickerButton)b).picker.ShowOpacity = (bool)n;
            }));

    public bool ShowOpacity
    {
        get => (bool)GetValue(ShowOpacityProperty);
        set => SetValue(ShowOpacityProperty, value);
    }

    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(
        nameof(CornerRadius),
        typeof(int),
        typeof(ColorPickerButton),
        8,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(ColorPickerButton), () =>
            {
                ((ColorPickerButton)b).buttonBorder.StrokeShape =
                new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = (int)n };
            }));

    public int CornerRadius
    {
        get => (int)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly BindableProperty ColorChangedCommandProperty = BindableProperty.Create(
        nameof(ColorChangedCommand),
        typeof(ICommand),
        typeof(ColorPickerButton));

    public ICommand? ColorChangedCommand
    {
        get => (ICommand?)GetValue(ColorChangedCommandProperty);
        set => SetValue(ColorChangedCommandProperty, value);
    }

    public static readonly BindableProperty DoneTextProperty = BindableProperty.Create(
        nameof(DoneText),
        typeof(string),
        typeof(ColorPickerButton),
        "Done");

    public string DoneText
    {
        get => (string)GetValue(DoneTextProperty);
        set => SetValue(DoneTextProperty, value);
    }

    public event EventHandler<Color>? ColorChanged;

    // Methods

    void OnButtonTapped(object? sender, TappedEventArgs e)
    {
        if (isOpen)
        {
            Close();
            return;
        }
        Open();
    }

    void Open()
    {
        if (isOpen) return;
        isOpen = true;

        openedColor = SelectedColor;
        picker.SelectedColor = SelectedColor;

        // Inject the overlay into the page's content
        var page = FindPage();
        if (page is not ContentPage cp) return;

        if (cp.Content is Grid grid)
        {
            grid.Children.Add(overlayGrid);
        }
        else
        {
            var original = cp.Content;
            var wrapper = new Grid();
            cp.Content = wrapper;
            if (original != null)
                wrapper.Children.Add(original);
            wrapper.Children.Add(overlayGrid);
        }
    }

    void Close()
    {
        if (!isOpen) return;

        isOpen = false;
        RemoveOverlay();
    }

    void CancelAndClose()
    {
        if (!isOpen) return;

        if (SelectedColor != openedColor)
            SetValue(SelectedColorProperty, openedColor);

        isOpen = false;
        RemoveOverlay();
    }

    void RemoveOverlay()
    {
        // Remove the overlay — clean break, no stale state
        if (overlayGrid.Parent is Layout parent)
        {
            parent.Children.Remove(overlayGrid);

            // Unwrap if we created a wrapper Grid
            if (parent is Grid wrapper && parent != overlayGrid
                && wrapper.Children.Count == 1
                && FindPage() is ContentPage cp && cp.Content == wrapper)
            {
                var original = wrapper.Children[0];
                wrapper.Children.Clear();
                cp.Content = (View)original;
            }
        }
    }

    bool isUpdatingColor;

    void OnPickerColorChanged(object? sender, Color color)
    {
        if (isUpdatingColor) return;
        isUpdatingColor = true;

        SetValue(SelectedColorProperty, color);
        UpdateButtonColor(color);

        ColorChanged?.Invoke(this, color);
        if (ColorChangedCommand?.CanExecute(color) == true)
            ColorChangedCommand.Execute(color);

        isUpdatingColor = false;
    }

    void OnSelectedColorChanged(Color color)
    {
        if (isUpdatingColor) return;
        isUpdatingColor = true;

        UpdateButtonColor(color);
        picker.SelectedColor = color;

        ColorChanged?.Invoke(this, color);
        if (ColorChangedCommand?.CanExecute(color) == true)
            ColorChangedCommand.Execute(color);

        isUpdatingColor = false;
    }

    void UpdateButtonColor(Color color)
    {
        buttonBorder.BackgroundColor = color;

        var luminance = 0.299 * color.Red + 0.587 * color.Green + 0.114 * color.Blue;
        buttonLabel.TextColor = luminance < 0.5 ? Colors.White : Colors.Black;
    }

    Page? FindPage()
    {
        Element? current = this;
        while (current is not null)
        {
            if (current is Page page)
                return page;
            current = current.Parent;
        }
        return null;
    }
}
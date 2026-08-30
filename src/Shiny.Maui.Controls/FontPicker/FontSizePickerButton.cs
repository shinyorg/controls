using System.Globalization;
using System.Windows.Input;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.FontPicker;

public class FontSizePickerButton : ContentView
{
    readonly Border buttonBorder;
    readonly Label buttonLabel;
    readonly Border popupBorder;
    readonly FontSizePicker picker;
    readonly Grid overlay;
    readonly Button doneButton;

    bool isOpen;

    public FontSizePickerButton()
    {
        buttonLabel = new Label
        {
            FontSize = 13,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

        buttonBorder = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerSmallRadius),
            Padding = new Thickness(12, 6),
            MinimumWidthRequest = 60,
            MinimumHeightRequest = 36,
            Content = buttonLabel,
            HorizontalOptions = LayoutOptions.Start
        }.WithStrokeThickness(ShinyThemeKeys.Border.Medium);

        buttonLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSecondaryContainer);
        buttonBorder.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Color.OutlineVariant);
        buttonBorder.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SecondaryContainer);

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnButtonTapped;
        buttonBorder.GestureRecognizers.Add(tap);

        picker = new FontSizePicker
        {
            HeightRequest = 320,
            WidthRequest = 240
        };
        picker.FontSizeChanged += OnPickerFontSizeChanged;

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

        var pickerWithDone = new VerticalStackLayout
        {
            Children = { picker, doneButton }
        };

        popupBorder = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerMediumRadius),
            Padding = 0,
            Content = pickerWithDone,
            IsVisible = false,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        }
        .WithStrokeThickness(ShinyThemeKeys.Border.Thin)
        .WithElevation(ShinyThemeKeys.Elevation.Level4);

        var backdrop = new BoxView
        {
            Opacity = 0.3,
            IsVisible = false,
            InputTransparent = false
        };
        popupBorder.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Color.OutlineVariant);
        popupBorder.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerLowest);
        backdrop.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.Scrim);

        var backdropTap = new TapGestureRecognizer();
        backdropTap.Tapped += (_, _) => Close();
        backdrop.GestureRecognizers.Add(backdropTap);

        overlay = new Grid
        {
            IsVisible = false,
            InputTransparent = false,
            Children = { backdrop, popupBorder }
        };

        Content = buttonBorder;
        UpdateButtonLabel(SelectedFontSize);

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(FontSizePickerButton));
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
            StyleGuard.WhenReady<FontSizePickerButton>(this, self => self.buttonBorder.HorizontalOptions = self.WidthRequest >= 0
                ? LayoutOptions.Fill
                : LayoutOptions.Start);
    }

    public static readonly BindableProperty AvailableFontSizesProperty = BindableProperty.Create(
        nameof(AvailableFontSizes),
        typeof(IList<double>),
        typeof(FontSizePickerButton),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(FontSizePickerButton), () =>
            {
                ((FontSizePickerButton)b).picker.AvailableFontSizes = n as IList<double>;
            }));

    public IList<double>? AvailableFontSizes
    {
        get => (IList<double>?)GetValue(AvailableFontSizesProperty);
        set => SetValue(AvailableFontSizesProperty, value);
    }

    public static readonly BindableProperty SelectedFontSizeProperty = BindableProperty.Create(
        nameof(SelectedFontSize),
        typeof(double),
        typeof(FontSizePickerButton),
        16.0,
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(FontSizePickerButton), () =>
            {
                ((FontSizePickerButton)b).OnSelectedFontSizeChanged((double)n);
            }));

    public double SelectedFontSize
    {
        get => (double)GetValue(SelectedFontSizeProperty);
        set => SetValue(SelectedFontSizeProperty, value);
    }

    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(
        nameof(CornerRadius),
        typeof(int),
        typeof(FontSizePickerButton),
        8,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(FontSizePickerButton), () =>
            {
                ((FontSizePickerButton)b).buttonBorder.StrokeShape =
                new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = (int)n };
            }));

    public int CornerRadius
    {
        get => (int)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly BindableProperty FontSizeChangedCommandProperty = BindableProperty.Create(
        nameof(FontSizeChangedCommand),
        typeof(ICommand),
        typeof(FontSizePickerButton));

    public ICommand? FontSizeChangedCommand
    {
        get => (ICommand?)GetValue(FontSizeChangedCommandProperty);
        set => SetValue(FontSizeChangedCommandProperty, value);
    }

    public static readonly BindableProperty DoneTextProperty = BindableProperty.Create(
        nameof(DoneText),
        typeof(string),
        typeof(FontSizePickerButton),
        "Done");

    public string DoneText
    {
        get => (string)GetValue(DoneTextProperty);
        set => SetValue(DoneTextProperty, value);
    }

    public event EventHandler<double>? FontSizeChanged;

    void OnButtonTapped(object? sender, TappedEventArgs e)
    {
        if (isOpen) { Close(); return; }
        Open();
    }

    void Open()
    {
        if (isOpen) return;
        isOpen = true;

        picker.SelectedFontSize = SelectedFontSize;

        var page = GetParentPage();
        if (page is ContentPage cp)
        {
            if (cp.Content is Grid grid)
            {
                if (!grid.Children.Contains(overlay))
                    grid.Children.Add(overlay);
            }
            else
            {
                var existing = cp.Content;
                var wrapper = new Grid { Children = { existing, overlay } };
                cp.Content = wrapper;
            }
        }

        overlay.IsVisible = true;
        foreach (var child in overlay.Children)
            ((View)child).IsVisible = true;
        popupBorder.IsVisible = true;
    }

    void Close()
    {
        if (!isOpen) return;
        isOpen = false;

        if (overlay.Parent is Layout parent)
            parent.Remove(overlay);

        overlay.IsVisible = false;
        popupBorder.IsVisible = false;
        foreach (var child in overlay.Children)
            ((View)child).IsVisible = false;
    }

    bool isUpdating;

    void OnPickerFontSizeChanged(object? sender, double size)
    {
        if (isUpdating) return;
        isUpdating = true;

        SetValue(SelectedFontSizeProperty, size);
        UpdateButtonLabel(size);

        FontSizeChanged?.Invoke(this, size);
        if (FontSizeChangedCommand?.CanExecute(size) == true)
            FontSizeChangedCommand.Execute(size);

        isUpdating = false;
    }

    void OnSelectedFontSizeChanged(double size)
    {
        if (isUpdating) return;
        isUpdating = true;

        UpdateButtonLabel(size);
        picker.SelectedFontSize = size;

        FontSizeChanged?.Invoke(this, size);
        if (FontSizeChangedCommand?.CanExecute(size) == true)
            FontSizeChangedCommand.Execute(size);

        isUpdating = false;
    }

    void UpdateButtonLabel(double size)
    {
        buttonLabel.Text = $"{size.ToString(CultureInfo.InvariantCulture)} pt";
    }

    ContentPage? GetParentPage()
    {
        Element? current = this;
        while (current is not null)
        {
            if (current is ContentPage page)
                return page;
            current = current.Parent;
        }
        return null;
    }
}

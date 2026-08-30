using Shiny.Maui.Controls.FloatingPanel;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Pickers;

public class DurationPicker : ContentView
{
    readonly Label valueLabel;
    readonly Border tapArea;
    FloatingPanel.FloatingPanel? panel;

    public static readonly BindableProperty DurationProperty = BindableProperty.Create(
        nameof(Duration), typeof(TimeSpan?), typeof(DurationPicker), null,
        BindingMode.TwoWay,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(DurationPicker), () =>
            {
                ((DurationPicker)b).UpdateDisplayText();
            }));

    public static readonly BindableProperty MinDurationProperty = BindableProperty.Create(
        nameof(MinDuration), typeof(TimeSpan), typeof(DurationPicker), TimeSpan.Zero);

    public static readonly BindableProperty MaxDurationProperty = BindableProperty.Create(
        nameof(MaxDuration), typeof(TimeSpan), typeof(DurationPicker), TimeSpan.FromHours(24));

    public static readonly BindableProperty FormatProperty = BindableProperty.Create(
        nameof(Format), typeof(string), typeof(DurationPicker), @"h\:mm",
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(DurationPicker), () =>
            {
                ((DurationPicker)b).UpdateDisplayText();
            }));

    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder), typeof(string), typeof(DurationPicker), "Select duration",
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(DurationPicker), () =>
            {
                ((DurationPicker)b).UpdateDisplayText();
            }));

    public static readonly BindableProperty PlaceholderColorProperty = BindableProperty.Create(
        nameof(PlaceholderColor), typeof(Color), typeof(DurationPicker), null);

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor), typeof(Color), typeof(DurationPicker), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(DurationPicker), () =>
            {
                ((DurationPicker)b).UpdateDisplayText();
            }));

    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize), typeof(double), typeof(DurationPicker), 16d,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(DurationPicker), () =>
            {
                ((DurationPicker)b).valueLabel.FontSize = (double)n;
            }));

    public static readonly BindableProperty MinuteIntervalProperty = BindableProperty.Create(
        nameof(MinuteInterval), typeof(int), typeof(DurationPicker), 5);

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(DurationPicker), "Select Duration");

    public static readonly BindableProperty HourUnitTextProperty = BindableProperty.Create(
        nameof(HourUnitText), typeof(string), typeof(DurationPicker), "hr");

    public static readonly BindableProperty MinuteUnitTextProperty = BindableProperty.Create(
        nameof(MinuteUnitText), typeof(string), typeof(DurationPicker), "min");

    public static readonly BindableProperty HoursPickerTitleProperty = BindableProperty.Create(
        nameof(HoursPickerTitle), typeof(string), typeof(DurationPicker), "Hours");

    public static readonly BindableProperty MinutesPickerTitleProperty = BindableProperty.Create(
        nameof(MinutesPickerTitle), typeof(string), typeof(DurationPicker), "Minutes");

    public static readonly BindableProperty DoneTextProperty = BindableProperty.Create(
        nameof(DoneText), typeof(string), typeof(DurationPicker), "Done");

    public static readonly BindableProperty CancelTextProperty = BindableProperty.Create(
        nameof(CancelText), typeof(string), typeof(DurationPicker), "Cancel");

    public TimeSpan? Duration
    {
        get => (TimeSpan?)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public TimeSpan MinDuration
    {
        get => (TimeSpan)GetValue(MinDurationProperty);
        set => SetValue(MinDurationProperty, value);
    }

    public TimeSpan MaxDuration
    {
        get => (TimeSpan)GetValue(MaxDurationProperty);
        set => SetValue(MaxDurationProperty, value);
    }

    public string Format
    {
        get => (string)GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public Color PlaceholderColor
    {
        get => (Color)GetValue(PlaceholderColorProperty);
        set => SetValue(PlaceholderColorProperty, value);
    }

    public Color? TextColor
    {
        get => (Color?)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public int MinuteInterval
    {
        get => (int)GetValue(MinuteIntervalProperty);
        set => SetValue(MinuteIntervalProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string HourUnitText
    {
        get => (string)GetValue(HourUnitTextProperty);
        set => SetValue(HourUnitTextProperty, value);
    }

    public string MinuteUnitText
    {
        get => (string)GetValue(MinuteUnitTextProperty);
        set => SetValue(MinuteUnitTextProperty, value);
    }

    public string HoursPickerTitle
    {
        get => (string)GetValue(HoursPickerTitleProperty);
        set => SetValue(HoursPickerTitleProperty, value);
    }

    public string MinutesPickerTitle
    {
        get => (string)GetValue(MinutesPickerTitleProperty);
        set => SetValue(MinutesPickerTitleProperty, value);
    }

    public string DoneText
    {
        get => (string)GetValue(DoneTextProperty);
        set => SetValue(DoneTextProperty, value);
    }

    public string CancelText
    {
        get => (string)GetValue(CancelTextProperty);
        set => SetValue(CancelTextProperty, value);
    }

    public event EventHandler<TimeSpan>? DurationSelected;

    public DurationPicker()
    {
        valueLabel = new Label
        {
            VerticalOptions = LayoutOptions.Center,
            VerticalTextAlignment = TextAlignment.Center
        }.WithFontSize(ShinyThemeKeys.Type.BodyLargeSize);

        var chevron = new Label
        {
            Text = "▼",
            FontSize = 10,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        chevron.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        var layout = new HorizontalStackLayout
        {
            Children = { valueLabel, chevron },
            VerticalOptions = LayoutOptions.Center
        };

        tapArea = new Border
        {
            Content = layout,
            Padding = new Thickness(12, 8),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerSmallRadius),
            BackgroundColor = Colors.Transparent
        }.WithStrokeThickness(ShinyThemeKeys.Border.Thin);
        tapArea.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Color.Outline);

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        tapArea.GestureRecognizers.Add(tap);

        Content = tapArea;
        UpdateDisplayText();

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(DurationPicker));
    }

    void UpdateDisplayText()
    {
        if (Duration.HasValue)
        {
            valueLabel.Text = Duration.Value.ToString(Format);
            Tint(valueLabel, Label.TextColorProperty, TextColor, ShinyThemeKeys.Color.OnSurface);
        }
        else
        {
            valueLabel.Text = Placeholder;
            Tint(valueLabel, Label.TextColorProperty, PlaceholderColor, ShinyThemeKeys.Color.OnSurfaceVariant);
        }
    }

    void OnTapped(object? sender, TappedEventArgs e)
    {
        var overlayHost = PickerHelper.FindOverlayHost(this);
        if (overlayHost == null) return;

        if (panel != null && panel.IsOpen)
            return;

        var content = BuildDurationPickerContent();

        if (panel == null)
        {
            panel = new FloatingPanel.FloatingPanel
            {
                FitContent = true,
                HasBackdrop = true,
                CloseOnBackdropTap = true,
                ShowHandle = false,
                IsLocked = true,
                PanelCornerRadius = 16
            };
            overlayHost.Children.Add(panel);
        }

        panel.PanelContent = content;
        panel.IsOpen = true;
    }

    View BuildDurationPickerContent()
    {
        var currentDuration = Duration ?? TimeSpan.Zero;
        var maxHours = (int)MaxDuration.TotalHours;
        var interval = Math.Max(1, MinuteInterval);

        var hourPicker = new Picker
        {
            Title = HoursPickerTitle,
            HorizontalOptions = LayoutOptions.Fill
        };

        var minutePicker = new Picker
        {
            Title = MinutesPickerTitle,
            HorizontalOptions = LayoutOptions.Fill
        };

        for (var h = 0; h <= maxHours; h++)
            hourPicker.Items.Add(h.ToString());

        for (var m = 0; m < 60; m += interval)
            minutePicker.Items.Add(m.ToString("D2"));

        var currentHours = (int)currentDuration.TotalHours;
        if (currentHours <= maxHours)
            hourPicker.SelectedIndex = currentHours;

        var minuteIndex = currentDuration.Minutes / interval;
        if (minuteIndex < minutePicker.Items.Count)
            minutePicker.SelectedIndex = minuteIndex;

        var pickerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8,
            HorizontalOptions = LayoutOptions.Fill
        };
        pickerGrid.Add(hourPicker, 0);
        var hourUnit = new Label
        {
            Text = HourUnitText,
            VerticalOptions = LayoutOptions.Center
        };
        hourUnit.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        pickerGrid.Add(hourUnit, 1);
        pickerGrid.Add(minutePicker, 2);
        var minuteUnit = new Label
        {
            Text = MinuteUnitText,
            VerticalOptions = LayoutOptions.Center
        };
        minuteUnit.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        pickerGrid.Add(minuteUnit, 3);

        var doneButton = new Button
        {
            Text = DoneText,
            HorizontalOptions = LayoutOptions.Fill
        }.Neutralize();

        doneButton.Clicked += (_, _) =>
        {
            var hours = hourPicker.SelectedIndex >= 0 ? hourPicker.SelectedIndex : 0;
            var minutes = minutePicker.SelectedIndex >= 0 ? minutePicker.SelectedIndex * interval : 0;
            var duration = new TimeSpan(hours, minutes, 0);

            if (duration < MinDuration) duration = MinDuration;
            if (duration > MaxDuration) duration = MaxDuration;

            Duration = duration;
            DurationSelected?.Invoke(this, duration);
            panel!.IsOpen = false;
        };

        var cancelButton = new Button
        {
            Text = CancelText,
            HorizontalOptions = LayoutOptions.Fill
        }.Neutralize();
        cancelButton.SetDynamicResource(Button.BackgroundColorProperty, ShinyThemeKeys.Color.SecondaryContainer);
        cancelButton.SetDynamicResource(Button.TextColorProperty, ShinyThemeKeys.Color.OnSecondaryContainer);
        cancelButton.Clicked += (_, _) => panel!.IsOpen = false;

        var buttonGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 10
        };
        buttonGrid.Add(cancelButton, 0);
        buttonGrid.Add(doneButton, 1);

        return new VerticalStackLayout
        {
            Padding = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new Label
                {
                    Text = Title,
                    FontSize = 18,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalTextAlignment = TextAlignment.Center
                },
                pickerGrid,
                buttonGrid
            }
        };
    }

    /// <summary>Uses the explicit colour when one was supplied, otherwise binds to the theme token.</summary>
    static void Tint(Element target, BindableProperty property, Color? explicitColor, string themeKey)
    {
        if (explicitColor is null)
        {
            target.SetDynamicResource(property, themeKey);
        }
        else
        {
            target.RemoveDynamicResource(property);
            target.SetValue(property, explicitColor);
        }
    }
}

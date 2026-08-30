using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Maui.Controls.FloatingPanel;
using Shiny.Maui.Controls.Pickers;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Cells;

public class DurationPickerCell : CellBase
{
    /// <summary>
    /// Children are built by CellBase's constructor (BuildLayout -> the virtual
    /// CreateAccessoryView override above), so by the time this body runs they exist.
    /// Marking ready here replays any property an implicit Style applied before
    /// construction - see StyleGuard.
    /// </summary>
    public DurationPickerCell() => StyleGuard.MarkReady(this, typeof(DurationPickerCell));

    Label valueLabel = default!;
    FloatingPanel.FloatingPanel? panel;

    public static readonly BindableProperty DurationProperty = BindableProperty.Create(
        nameof(Duration), typeof(TimeSpan?), typeof(DurationPickerCell), null,
        BindingMode.TwoWay,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(DurationPickerCell), () =>
            {
                ((DurationPickerCell)b).UpdateDisplayText();
            }));

    public static readonly BindableProperty MinDurationProperty = BindableProperty.Create(
        nameof(MinDuration), typeof(TimeSpan), typeof(DurationPickerCell), TimeSpan.Zero);

    public static readonly BindableProperty MaxDurationProperty = BindableProperty.Create(
        nameof(MaxDuration), typeof(TimeSpan), typeof(DurationPickerCell), TimeSpan.FromHours(24));

    public static readonly BindableProperty FormatProperty = BindableProperty.Create(
        nameof(Format), typeof(string), typeof(DurationPickerCell), @"h\:mm",
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(DurationPickerCell), () =>
            {
                ((DurationPickerCell)b).UpdateDisplayText();
            }));

    public static readonly BindableProperty PickerTitleProperty = BindableProperty.Create(
        nameof(PickerTitle), typeof(string), typeof(DurationPickerCell), "Select Duration");

    public static readonly BindableProperty SelectedCommandProperty = BindableProperty.Create(
        nameof(SelectedCommand), typeof(ICommand), typeof(DurationPickerCell), null);

    public static readonly BindableProperty ValueTextColorProperty = BindableProperty.Create(
        nameof(ValueTextColor), typeof(Color), typeof(DurationPickerCell), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(DurationPickerCell), () =>
            {
                ((DurationPickerCell)b).UpdateValueColor();
            }));

    public static readonly BindableProperty MinuteIntervalProperty = BindableProperty.Create(
        nameof(MinuteInterval), typeof(int), typeof(DurationPickerCell), 5);

    public static readonly BindableProperty DoneTextProperty = BindableProperty.Create(
        nameof(DoneText), typeof(string), typeof(DurationPickerCell), "Done");

    public static readonly BindableProperty CancelTextProperty = BindableProperty.Create(
        nameof(CancelText), typeof(string), typeof(DurationPickerCell), "Cancel");

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

    public string PickerTitle
    {
        get => (string)GetValue(PickerTitleProperty);
        set => SetValue(PickerTitleProperty, value);
    }

    public ICommand? SelectedCommand
    {
        get => (ICommand?)GetValue(SelectedCommandProperty);
        set => SetValue(SelectedCommandProperty, value);
    }

    public Color? ValueTextColor
    {
        get => (Color?)GetValue(ValueTextColorProperty);
        set => SetValue(ValueTextColorProperty, value);
    }

    public int MinuteInterval
    {
        get => (int)GetValue(MinuteIntervalProperty);
        set => SetValue(MinuteIntervalProperty, value);
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

    protected override View? CreateAccessoryView()
    {
        valueLabel = new Label
        {
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End
        };
        UpdateDisplayText();
        return valueLabel;
    }

    protected override bool ShouldKeepSelection() => true;

    protected override void OnTapped()
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
            panel.Closed += (_, _) => ClearSelectionHighlight();
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

        var hourPicker = new Picker { Title = "Hours", HorizontalOptions = LayoutOptions.Fill };
        var minutePicker = new Picker { Title = "Minutes", HorizontalOptions = LayoutOptions.Fill };

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
        var hourUnit = new Label { Text = "hr", VerticalOptions = LayoutOptions.Center };
        hourUnit.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        pickerGrid.Add(hourUnit, 1);
        pickerGrid.Add(minutePicker, 2);
        var minuteUnit = new Label { Text = "min", VerticalOptions = LayoutOptions.Center };
        minuteUnit.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        pickerGrid.Add(minuteUnit, 3);

        var doneButton = new Button { HorizontalOptions = LayoutOptions.Fill }.Neutralize();
        doneButton.SetBinding(Button.TextProperty, new Binding(nameof(DoneText), source: this));
        doneButton.Clicked += (_, _) =>
        {
            var hours = hourPicker.SelectedIndex >= 0 ? hourPicker.SelectedIndex : 0;
            var minutes = minutePicker.SelectedIndex >= 0 ? minutePicker.SelectedIndex * interval : 0;
            var duration = new TimeSpan(hours, minutes, 0);

            if (duration < MinDuration) duration = MinDuration;
            if (duration > MaxDuration) duration = MaxDuration;

            Duration = duration;

            if (SelectedCommand?.CanExecute(Duration) == true)
                SelectedCommand.Execute(Duration);

            panel!.IsOpen = false;
        };

        var cancelButton = new Button
        {
            HorizontalOptions = LayoutOptions.Fill
        }.Neutralize();
        cancelButton.SetBinding(Button.TextProperty, new Binding(nameof(CancelText), source: this));
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
                    Text = PickerTitle,
                    FontSize = 18,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalTextAlignment = TextAlignment.Center
                },
                pickerGrid,
                buttonGrid
            }
        };
    }

    void UpdateDisplayText()
    {
        if (valueLabel == null) return;
        valueLabel.Text = Duration.HasValue ? Duration.Value.ToString(Format) : string.Empty;
    }

    void UpdateValueColor()
    {
        var color = ValueTextColor ?? ParentTableView?.CellValueTextColor;
        if (color != null)
            valueLabel.TextColor = color;
        else
            valueLabel.ClearValue(Label.TextColorProperty);
    }
}

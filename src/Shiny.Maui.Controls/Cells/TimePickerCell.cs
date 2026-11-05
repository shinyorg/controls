using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Cells;

public class TimePickerCell : CellBase
{
    /// <summary>Appended to the cell's <see cref="Element.AutomationId"/> to name the native picker behind it (the tap target on Android).</summary>
    public const string PickerAutomationIdSuffix = "_Picker";

    /// <summary>
    /// Children are built by CellBase's constructor (BuildLayout -> the virtual
    /// CreateAccessoryView override above), so by the time this body runs they exist.
    /// Marking ready here replays any property an implicit Style applied before
    /// construction - see StyleGuard.
    /// </summary>
    public TimePickerCell()
    {
        AutomationIds.Link(this, valueLabel, ValueAutomationIdSuffix);
        AutomationIds.Link(this, hiddenPicker, PickerAutomationIdSuffix);
        StyleGuard.MarkReady(this, typeof(TimePickerCell));
    }

    Label valueLabel = default!;
    TimePicker hiddenPicker = default!;

    public static readonly BindableProperty TimeProperty = BindableProperty.Create(
        nameof(Time), typeof(TimeSpan), typeof(TimePickerCell), TimeSpan.Zero,
        BindingMode.TwoWay,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TimePickerCell), () =>
            {
                ((TimePickerCell)b).OnTimeChanged();
            }));

    public static readonly BindableProperty FormatProperty = BindableProperty.Create(
        nameof(Format), typeof(string), typeof(TimePickerCell), "t",
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TimePickerCell), () =>
            {
                ((TimePickerCell)b).UpdateDisplayText();
            }));

    public static readonly BindableProperty MinuteIntervalProperty = BindableProperty.Create(
        nameof(MinuteInterval), typeof(int), typeof(TimePickerCell), 1,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TimePickerCell), () =>
            {
                ((TimePickerCell)b).SyncMinuteInterval();
            }));

    public static readonly BindableProperty Use24HourProperty = BindableProperty.Create(
        nameof(Use24Hour), typeof(bool), typeof(TimePickerCell), false,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TimePickerCell), () =>
            {
                ((TimePickerCell)b).SyncUse24Hour();
            }));

    public static readonly BindableProperty ValueTextColorProperty = BindableProperty.Create(
        nameof(ValueTextColor), typeof(Color), typeof(TimePickerCell), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(TimePickerCell), () =>
            {
                ((TimePickerCell)b).UpdateValueColor();
            }));

    public TimeSpan Time
    {
        get => (TimeSpan)GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    public string Format
    {
        get => (string)GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    public int MinuteInterval
    {
        get => (int)GetValue(MinuteIntervalProperty);
        set => SetValue(MinuteIntervalProperty, value);
    }

    public bool Use24Hour
    {
        get => (bool)GetValue(Use24HourProperty);
        set => SetValue(Use24HourProperty, value);
    }

    public Color? ValueTextColor
    {
        get => (Color?)GetValue(ValueTextColorProperty);
        set => SetValue(ValueTextColorProperty, value);
    }

    protected override View? CreateAccessoryView()
    {
        valueLabel = new Label
        {
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End
        };

        // On Android the picker lies over the whole cell at 1% opacity so it takes the tap, but it
        // still draws its text there - a faint copy of the time behind the title, easy to see on a
        // dark theme. Transparent text keeps it tappable without that.
        hiddenPicker = new TimePicker { TextColor = Colors.Transparent };
        SyncUse24Hour();

        hiddenPicker.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(TimePicker.Time))
            {
                var raw = hiddenPicker.Time ?? TimeSpan.Zero;
                Time = SnapToInterval(raw);
            }
        };
        hiddenPicker.Unfocused += (s, e) => ClearSelectionHighlight();
        hiddenPicker.HandlerChanged += OnPickerHandlerChanged;

#if ANDROID
        // Android: overlay the transparent picker across the entire cell so tapping
        // anywhere opens the native time dialog (Focus() is unreliable on Android).
        hiddenPicker.Opacity = 0.01;
        Grid.SetColumn(hiddenPicker, 0);
        Grid.SetColumnSpan(hiddenPicker, 3);
        Grid.SetRow(hiddenPicker, 0);
        Grid.SetRowSpan(hiddenPicker, 2);
        RootGrid.Children.Add(hiddenPicker);

        UpdateDisplayText();
        return valueLabel;
#else
        // iOS/Mac: hidden zero-size picker in local Grid; Focus() opens native picker
        hiddenPicker.Opacity = 0;
        hiddenPicker.WidthRequest = 0;
        hiddenPicker.HeightRequest = 0;

        var layout = new Grid();
        layout.Children.Add(hiddenPicker);
        layout.Children.Add(valueLabel);

        UpdateDisplayText();
        return layout;
#endif
    }

#if ANDROID
    protected override void OnCellTapped(object? sender, TappedEventArgs e)
    {
        // Android: native picker overlay handles all touch interaction
    }
#endif

    protected override bool ShouldKeepSelection() => true;

    protected override void OnTapped()
    {
#if !ANDROID
        hiddenPicker.Focus();
#endif
    }

    void OnTimeChanged()
    {
        if (hiddenPicker != null)
            hiddenPicker.Time = Time;
        UpdateDisplayText();
    }

    void UpdateDisplayText()
    {
        if (valueLabel == null) return;
        var dt = DateTime.SpecifyKind(ShinyTime.For(this).Today(), DateTimeKind.Local).Add(Time);
        valueLabel.Text = dt.ToString(Format);
    }

    protected override void ApplyValueTextStyles() => UpdateValueColor();

    void UpdateValueColor()
    {
        var color = ValueTextColor ?? ParentTableView?.CellValueTextColor;
        if (color != null)
            valueLabel.TextColor = color;
        else
            valueLabel.ClearValue(Label.TextColorProperty);
    }

    TimeSpan SnapToInterval(TimeSpan raw)
    {
        var interval = Math.Max(1, MinuteInterval);
        if (interval <= 1)
            return raw;

        var snapped = (int)(Math.Round((double)raw.Minutes / interval) * interval);
        if (snapped >= 60)
            snapped = 60 - interval;

        return new TimeSpan(raw.Hours, snapped, 0);
    }

    void SyncUse24Hour()
    {
        if (hiddenPicker == null)
            return;

        hiddenPicker.Format = Use24Hour ? "HH:mm" : "h:mm tt";
    }

    void SyncMinuteInterval()
    {
        ApplyMinuteIntervalToNative();
    }

    void OnPickerHandlerChanged(object? sender, EventArgs e)
    {
        ApplyMinuteIntervalToNative();
    }

    void ApplyMinuteIntervalToNative()
    {
        if (hiddenPicker?.Handler?.PlatformView == null)
            return;

        var interval = Math.Max(1, MinuteInterval);
#if IOS || MACCATALYST
        if (hiddenPicker.Handler.PlatformView is UIKit.UIDatePicker uiPicker)
            uiPicker.MinuteInterval = (nint)interval;
#endif
    }
}

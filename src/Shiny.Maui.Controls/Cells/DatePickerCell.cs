using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Cells;

public class DatePickerCell : CellBase
{
    /// <summary>Appended to the cell's <see cref="Element.AutomationId"/> to name the native picker behind it (the tap target on Android).</summary>
    public const string PickerAutomationIdSuffix = "_Picker";

    /// <summary>
    /// Children are built by CellBase's constructor (BuildLayout -> the virtual
    /// CreateAccessoryView override above), so by the time this body runs they exist.
    /// Marking ready here replays any property an implicit Style applied before
    /// construction - see StyleGuard.
    /// </summary>
    public DatePickerCell()
    {
        AutomationIds.Link(this, valueLabel, ValueAutomationIdSuffix);
        AutomationIds.Link(this, hiddenPicker, PickerAutomationIdSuffix);
        StyleGuard.MarkReady(this, typeof(DatePickerCell));
    }

    Label valueLabel = default!;
    DatePicker hiddenPicker = default!;

    public static readonly BindableProperty DateProperty = BindableProperty.Create(
        nameof(Date), typeof(DateTime?), typeof(DatePickerCell), null,
        BindingMode.TwoWay,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(DatePickerCell), () =>
            {
                ((DatePickerCell)b).OnDateChanged();
            }));

    public static readonly BindableProperty InitialDateProperty = BindableProperty.Create(
        nameof(InitialDate), typeof(DateTime), typeof(DatePickerCell), new DateTime(2000, 1, 1));

    public static readonly BindableProperty MinimumDateProperty = BindableProperty.Create(
        nameof(MinimumDate), typeof(DateTime), typeof(DatePickerCell), new DateTime(1900, 1, 1),
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(DatePickerCell), () =>
            {
                ((DatePickerCell)b).SyncPickerRange();
            }));

    public static readonly BindableProperty MaximumDateProperty = BindableProperty.Create(
        nameof(MaximumDate), typeof(DateTime), typeof(DatePickerCell), new DateTime(2100, 12, 31),
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(DatePickerCell), () =>
            {
                ((DatePickerCell)b).SyncPickerRange();
            }));

    public static readonly BindableProperty FormatProperty = BindableProperty.Create(
        nameof(Format), typeof(string), typeof(DatePickerCell), "d",
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(DatePickerCell), () =>
            {
                ((DatePickerCell)b).UpdateDisplayText();
            }));

    public static readonly BindableProperty ValueTextColorProperty = BindableProperty.Create(
        nameof(ValueTextColor), typeof(Color), typeof(DatePickerCell), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(DatePickerCell), () =>
            {
                ((DatePickerCell)b).UpdateValueColor();
            }));

    public DateTime? Date
    {
        get => (DateTime?)GetValue(DateProperty);
        set => SetValue(DateProperty, value);
    }

    public DateTime InitialDate
    {
        get => (DateTime)GetValue(InitialDateProperty);
        set => SetValue(InitialDateProperty, value);
    }

    public DateTime MinimumDate
    {
        get => (DateTime)GetValue(MinimumDateProperty);
        set => SetValue(MinimumDateProperty, value);
    }

    public DateTime MaximumDate
    {
        get => (DateTime)GetValue(MaximumDateProperty);
        set => SetValue(MaximumDateProperty, value);
    }

    public string Format
    {
        get => (string)GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
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

        hiddenPicker = new DatePicker
        {
            MinimumDate = MinimumDate,
            MaximumDate = MaximumDate,
            // On Android it lies over the whole cell at 1% opacity to take the tap; transparent text
            // keeps it from drawing a faint copy of the date behind the title.
            TextColor = Colors.Transparent
        };

        if (!Date.HasValue)
            hiddenPicker.Date = InitialDate;

        hiddenPicker.DateSelected += (s, e) => Date = e.NewDate;
        hiddenPicker.Unfocused += (s, e) => ClearSelectionHighlight();

#if ANDROID
        // Android: overlay the transparent picker across the entire cell so tapping
        // anywhere opens the native date dialog (Focus() is unreliable on Android).
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
        if (!Date.HasValue)
            hiddenPicker.Date = InitialDate;

        hiddenPicker.Focus();
#endif
    }

    void OnDateChanged()
    {
        if (hiddenPicker != null && Date.HasValue)
            hiddenPicker.Date = Date.Value;

        UpdateDisplayText();
    }

    void SyncPickerRange()
    {
        if (hiddenPicker == null) return;
        hiddenPicker.MinimumDate = MinimumDate;
        hiddenPicker.MaximumDate = MaximumDate;
    }

    void UpdateDisplayText()
    {
        if (valueLabel == null) return;
        valueLabel.Text = Date?.ToString(Format) ?? string.Empty;
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
}

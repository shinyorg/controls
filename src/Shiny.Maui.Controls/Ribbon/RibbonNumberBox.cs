using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Ribbons;

/// <summary>
/// A labelled number field with a spinner — Word's Layout › Paragraph "Left: 0"" and "Before: 0 pt".
/// </summary>
/// <example>
/// <code language="xaml">
/// &lt;shiny:RibbonNumberBox Text="Before:" Unit="pt" Step="6" Value="{Binding SpaceBefore}" /&gt;
/// </code>
/// </example>
/// <remarks>
/// <see cref="RibbonItem.Text"/> is the caption in front of the field. A text entry rather than a
/// numeric one so the unit can be shown and typed; the value is parsed on commit (Enter or leaving the
/// field) with the unit optional, and anything unreadable puts the last good value back.
/// </remarks>
public class RibbonNumberBox : RibbonContentItem
{
    readonly Label caption;
    readonly Entry entry;

    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value), typeof(double), typeof(RibbonNumberBox), 0d, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => ((RibbonNumberBox)b).OnValueChanged((double)n));

    public static readonly BindableProperty MinimumProperty = BindableProperty.Create(
        nameof(Minimum), typeof(double), typeof(RibbonNumberBox), 0d);

    public static readonly BindableProperty MaximumProperty = BindableProperty.Create(
        nameof(Maximum), typeof(double), typeof(RibbonNumberBox), 1584d);

    public static readonly BindableProperty StepProperty = BindableProperty.Create(
        nameof(Step), typeof(double), typeof(RibbonNumberBox), 1d);

    public static readonly BindableProperty DecimalsProperty = BindableProperty.Create(
        nameof(Decimals), typeof(int), typeof(RibbonNumberBox), 1,
        propertyChanged: (b, _, _) => ((RibbonNumberBox)b).ShowValue());

    public static readonly BindableProperty UnitProperty = BindableProperty.Create(
        nameof(Unit), typeof(string), typeof(RibbonNumberBox), null,
        propertyChanged: (b, _, _) => ((RibbonNumberBox)b).ShowValue());

    public static readonly BindableProperty FieldWidthProperty = BindableProperty.Create(
        nameof(FieldWidth), typeof(double), typeof(RibbonNumberBox), 64d,
        propertyChanged: (b, _, n) => { if (((RibbonNumberBox)b).entry is { } e) e.WidthRequest = (double)n - 16; });


    public RibbonNumberBox()
    {
        this.Size = RibbonItemSize.Small;

        this.caption = new Label { VerticalTextAlignment = TextAlignment.Center }.WithFontSize(ShinyThemeKeys.Type.LabelMediumSize);
        this.caption.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);

        this.entry = new Entry
        {
            WidthRequest = this.FieldWidth - 16,
            VerticalOptions = LayoutOptions.Center,

            // Local, so the MAUI template's implicit Entry style (MinimumHeightRequest 44) cannot
            // stretch the entry past its 24pt field.
            MinimumHeightRequest = 0,
            Keyboard = Keyboard.Numeric
        }.WithFontSize(ShinyThemeKeys.Type.LabelMediumSize);
        this.entry.SetDynamicResource(Entry.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
        this.entry.Completed += (_, _) => this.Commit(this.entry.Text);
        this.entry.Unfocused += (_, _) => this.Commit(this.entry.Text);

        var spin = new Grid { RowDefinitions = { new(GridLength.Star), new(GridLength.Star) }, WidthRequest = 14 };
        spin.Add(SpinButton(new PointCollection { new(0, 4), new(4, 0), new(8, 4) }, "Increase", () => this.StepBy(1)), 0, 0);
        spin.Add(SpinButton(new PointCollection { new(0, 0), new(4, 4), new(8, 0) }, "Decrease", () => this.StepBy(-1)), 0, 1);

        var fieldRow = new Grid { ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) } };
        fieldRow.Add(this.entry, 0);
        fieldRow.Add(spin, 1);

        var field = new Border
        {
            Content = fieldRow,
            StrokeThickness = 1,
            Padding = new Thickness(2, 0, 0, 0),
            HeightRequest = 24,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerExtraSmallRadius)
        };
        field.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Brush.Outline);
        field.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);

        var layout = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
        layout.Children.Add(this.caption);
        layout.Children.Add(field);

        this.Content = layout;
        this.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(this.Text))
                this.caption.Text = this.Text;
            else if (e.PropertyName == nameof(this.IsEnabled))
                layout.IsEnabled = this.IsEnabled;
        };

        this.ShowValue();
    }


    /// <summary>The value. Two-way.</summary>
    public double Value
    {
        get => (double)this.GetValue(ValueProperty);
        set => this.SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => (double)this.GetValue(MinimumProperty);
        set => this.SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)this.GetValue(MaximumProperty);
        set => this.SetValue(MaximumProperty, value);
    }

    /// <summary>What one spinner press moves by.</summary>
    public double Step
    {
        get => (double)this.GetValue(StepProperty);
        set => this.SetValue(StepProperty, value);
    }

    /// <summary>Decimal places shown. Default 1, dropped when zero ("0 pt", "0.5 pt").</summary>
    public int Decimals
    {
        get => (int)this.GetValue(DecimalsProperty);
        set => this.SetValue(DecimalsProperty, value);
    }

    /// <summary>The unit written after the number — <c>pt</c>, <c>"</c>, <c>cm</c>. Optional when typing.</summary>
    public string? Unit
    {
        get => (string?)this.GetValue(UnitProperty);
        set => this.SetValue(UnitProperty, value);
    }

    /// <summary>The field's width including the spinner. Default 64.</summary>
    public double FieldWidth
    {
        get => (double)this.GetValue(FieldWidthProperty);
        set => this.SetValue(FieldWidthProperty, value);
    }

    /// <summary>Raised after <see cref="Value"/> changes from the field or the spinner.</summary>
    public event EventHandler<double>? ValueCommitted;


    /// <summary>Moves the value by <see cref="Step"/> in <paramref name="direction"/>. The seam a test presses through.</summary>
    public void StepBy(int direction)
    {
        if (!this.IsEnabled)
            return;

        this.SetCommitted(this.Value + (direction * this.Step));
    }


    /// <summary>Commits typed text as the user leaving the field would.</summary>
    public void Commit(string? text)
    {
        if (TryParse(text, this.Unit, out var parsed))
            this.SetCommitted(parsed);
        else
            this.ShowValue();
    }


    void SetCommitted(double value)
    {
        var clamped = Clamp(value, this.Minimum, this.Maximum, this.Decimals);
        var changed = clamped != this.Value;
        this.Value = clamped;
        this.ShowValue();

        if (changed)
            this.ValueCommitted?.Invoke(this, clamped);
    }


    void OnValueChanged(double _) => this.ShowValue();

    void ShowValue()
    {
        if (this.entry is not null)
            this.entry.Text = Format(this.Value, this.Decimals, this.Unit);
    }


    /// <summary>"0 pt", "0.5"", "1.2 cm" — no trailing zeros, the unit spaced unless it is an inch mark.</summary>
    public static string Format(double value, int decimals, string? unit)
    {
        var number = Math.Round(value, Math.Max(0, decimals))
            .ToString("0." + new string('#', Math.Max(0, decimals)), CultureInfo.CurrentCulture)
            .TrimEnd('.');

        if (string.IsNullOrWhiteSpace(unit))
            return number;

        return unit is "\"" or "″" ? number + unit : $"{number} {unit}";
    }


    /// <summary>Reads "12", "12pt", "12 pt", "1.5\"" — the unit optional, anything else a failure.</summary>
    public static bool TryParse(string? text, string? unit, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var cleaned = text.Trim();
        if (!string.IsNullOrWhiteSpace(unit) && cleaned.EndsWith(unit.Trim(), StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[..^unit.Trim().Length].Trim();

        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
               || double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }


    public static double Clamp(double value, double minimum, double maximum, int decimals)
        => Math.Round(Math.Clamp(value, minimum, Math.Max(minimum, maximum)), Math.Max(0, decimals));


    static Border SpinButton(PointCollection points, string hint, Action action)
    {
        var glyph = new Polyline
        {
            Points = points,
            StrokeThickness = 1.2,
            StrokeLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            WidthRequest = 8,
            HeightRequest = 5,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        glyph.SetDynamicResource(Shape.StrokeProperty, ShinyThemeKeys.Brush.OnSurfaceVariant);

        var border = new Border { Content = glyph, StrokeThickness = 0, BackgroundColor = Colors.Transparent };
        SemanticProperties.SetDescription(border, hint);
        border.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(action) });
        return border;
    }
}

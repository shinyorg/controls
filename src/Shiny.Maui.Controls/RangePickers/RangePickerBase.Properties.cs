using System.Globalization;
using Shiny.Controls.RangePickers;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

public abstract partial class RangePickerBase
{
    static void Text(BindableObject b) => StyleGuard.WhenReady<RangePickerBase>(b, p => p.RefreshText());


    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder), typeof(string), typeof(RangePickerBase), null, propertyChanged: (b, _, _) => Text(b));

    /// <summary>Shown in the field while there is no value. Null shows each picker's own default ("Select dates").</summary>
    public string? Placeholder
    {
        get => (string?)this.GetValue(PlaceholderProperty);
        set => this.SetValue(PlaceholderProperty, value);
    }

    public static readonly BindableProperty PlaceholderColorProperty = BindableProperty.Create(
        nameof(PlaceholderColor), typeof(Color), typeof(RangePickerBase), null, propertyChanged: (b, _, _) => Text(b));

    public Color? PlaceholderColor
    {
        get => (Color?)this.GetValue(PlaceholderColorProperty);
        set => this.SetValue(PlaceholderColorProperty, value);
    }

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor), typeof(Color), typeof(RangePickerBase), null, propertyChanged: (b, _, _) => Text(b));

    public Color? TextColor
    {
        get => (Color?)this.GetValue(TextColorProperty);
        set => this.SetValue(TextColorProperty, value);
    }

    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize), typeof(double), typeof(RangePickerBase), ThemeTokens.Unset, propertyChanged: (b, _, _) => Text(b));

    /// <summary>The field's font size. Unset follows the theme's body size.</summary>
    public double FontSize
    {
        get => (double)this.GetValue(FontSizeProperty);
        set => this.SetValue(FontSizeProperty, value);
    }

    public static readonly BindableProperty FormatProperty = BindableProperty.Create(
        nameof(Format), typeof(string), typeof(RangePickerBase), null, propertyChanged: (b, _, _) => Text(b));

    /// <summary>A .NET format string for both ends. Null uses the compact form ("Mar 3 – 9, 2026").</summary>
    public string? Format
    {
        get => (string?)this.GetValue(FormatProperty);
        set => this.SetValue(FormatProperty, value);
    }

    public static readonly BindableProperty SeparatorProperty = BindableProperty.Create(
        nameof(Separator), typeof(string), typeof(RangePickerBase), RangeFormatter.DefaultSeparator, propertyChanged: (b, _, _) => Text(b));

    /// <summary>Between the two ends in the field - an en dash with spaces.</summary>
    public string Separator
    {
        get => (string)this.GetValue(SeparatorProperty);
        set => this.SetValue(SeparatorProperty, value);
    }

    public static readonly BindableProperty CultureProperty = BindableProperty.Create(
        nameof(Culture), typeof(CultureInfo), typeof(RangePickerBase), null, propertyChanged: (b, _, _) => Text(b));

    /// <summary>Formatting, month and day names. Null follows the current culture.</summary>
    public CultureInfo? Culture
    {
        get => (CultureInfo?)this.GetValue(CultureProperty);
        set => this.SetValue(CultureProperty, value);
    }

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(RangePickerBase), null);

    /// <summary>The popup heading while nothing is picked; once something is, the heading shows the draft.</summary>
    public string? Title
    {
        get => (string?)this.GetValue(TitleProperty);
        set => this.SetValue(TitleProperty, value);
    }

    public static readonly BindableProperty ApplyTextProperty = BindableProperty.Create(
        nameof(ApplyText), typeof(string), typeof(RangePickerBase), "Apply");

    public string ApplyText
    {
        get => (string)this.GetValue(ApplyTextProperty);
        set => this.SetValue(ApplyTextProperty, value);
    }

    public static readonly BindableProperty CancelTextProperty = BindableProperty.Create(
        nameof(CancelText), typeof(string), typeof(RangePickerBase), "Cancel");

    public string CancelText
    {
        get => (string)this.GetValue(CancelTextProperty);
        set => this.SetValue(CancelTextProperty, value);
    }

    public static readonly BindableProperty ClearTextProperty = BindableProperty.Create(
        nameof(ClearText), typeof(string), typeof(RangePickerBase), "Clear");

    public string ClearText
    {
        get => (string)this.GetValue(ClearTextProperty);
        set => this.SetValue(ClearTextProperty, value);
    }

    public static readonly BindableProperty ShowClearProperty = BindableProperty.Create(
        nameof(ShowClear), typeof(bool), typeof(RangePickerBase), true);

    /// <summary>Whether the popup offers Clear.</summary>
    public bool ShowClear
    {
        get => (bool)this.GetValue(ShowClearProperty);
        set => this.SetValue(ShowClearProperty, value);
    }

    public static readonly BindableProperty IsOpenProperty = BindableProperty.Create(
        nameof(IsOpen), typeof(bool), typeof(RangePickerBase), false, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<RangePickerBase>(b, p =>
        {
            if ((bool)n)
                p.Open();
            else
                p.Cancel();
        }));

    /// <summary>Whether the popup is showing. Set it to open or cancel from code.</summary>
    public bool IsOpen
    {
        get => (bool)this.GetValue(IsOpenProperty);
        set => this.SetValue(IsOpenProperty, value);
    }
}

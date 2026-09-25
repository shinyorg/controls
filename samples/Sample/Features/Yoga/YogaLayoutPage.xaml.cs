using Shiny.Maui.Controls;

namespace Sample.Features.Yoga;

public partial class YogaLayoutPage : ContentPage
{
    static readonly string[] Words =
    [
        "yoga", "flexbox", "react native", "column", "row", "grow", "shrink", "basis",
        "absolute", "percent", "aspect ratio", "auto margin", "gap", "wrap", "maui", "blazor"
    ];


    public YogaLayoutPage()
    {
        InitializeComponent();
        SampleSourceCode.Attach(this);

        Bind(this.DirectionPicker, YogaFlexDirection.Column, v => this.Playground.FlexDirection = v);
        Bind(this.JustifyPicker, YogaJustify.FlexStart, v => this.Playground.JustifyContent = v);
        Bind(this.AlignItemsPicker, YogaAlign.Stretch, v => this.Playground.AlignItems = v);
        Bind(this.WrapPicker, YogaWrap.NoWrap, v => this.Playground.FlexWrap = v);
        this.GapSlider.ValueChanged += (_, e) => this.Playground.Gap = e.NewValue;

        foreach (var word in Words)
        {
            this.Chips.Children.Add(new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                StrokeThickness = 0,
                BackgroundColor = Color.FromArgb("#E0F2FE"),
                Padding = new Thickness(10, 4),
                Content = new Label { Text = word, FontSize = 13, TextColor = Color.FromArgb("#075985") }
            });
        }
    }


    static void Bind<T>(Picker picker, T initial, Action<T> apply) where T : struct, Enum
    {
        var values = Enum.GetValues<T>();
        picker.ItemsSource = values.Select(v => v.ToString()).ToList();
        picker.SelectedIndex = Array.IndexOf(values, initial);
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (picker.SelectedIndex >= 0)
                apply(values[picker.SelectedIndex]);
        };
    }
}

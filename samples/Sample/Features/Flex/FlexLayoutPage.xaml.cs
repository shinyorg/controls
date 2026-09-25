using System.Diagnostics;
using Microsoft.Maui.Layouts;
using Shiny.Maui.Controls;

namespace Sample.Features.Flex;

public partial class FlexLayoutPage : ContentPage
{
    const int Passes = 20;

    static readonly string[] Words =
    [
        "maui", "flex", "layout", "fast", "wrap", "grow", "shrink", "basis", "order", "align",
        "justify", "gap", "responsive", "cache", "measure", "arrange", "rotation", "chips", "tags", "shiny"
    ];


    public FlexLayoutPage()
    {
        InitializeComponent();
        SampleSourceCode.Attach(this);

        Bind(this.DirectionPicker, FlexDirection.Row, v => this.Playground.Direction = v);
        Bind(this.WrapPicker, FlexWrap.Wrap, v => this.Playground.Wrap = v);
        Bind(this.JustifyPicker, FlexJustify.Start, v => this.Playground.JustifyContent = v);
        Bind(this.AlignItemsPicker, FlexAlignItems.Stretch, v => this.Playground.AlignItems = v);
        Bind(this.AlignContentPicker, FlexAlignContent.Stretch, v => this.Playground.AlignContent = v);

        this.SpacingSlider.ValueChanged += (_, e) =>
        {
            this.Playground.RowSpacing = e.NewValue;
            this.Playground.ColumnSpacing = e.NewValue;
        };

        foreach (var word in Words)
        {
            this.Tags.Children.Add(new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                StrokeThickness = 0,
                BackgroundColor = Color.FromArgb("#E0F2FE"),
                Padding = new Thickness(10, 4),
                Content = new Label { Text = "#" + word, FontSize = 13, TextColor = Color.FromArgb("#075985") }
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


    async void OnBenchmark(object? sender, EventArgs e)
    {
        var button = (Button)sender!;
        button.IsEnabled = false;
        this.BenchResult.Text = "Running…";

        // Let the "Running…" text paint before the UI thread is busy.
        await Task.Delay(50);

        var count = (int)this.CountStepper.Value;
        var width = Math.Max(200, this.Width - 48);

        var shinyLabels = CreateLabels(count);
        var mauiLabels = CreateLabels(count);

        var shiny = new ShinyFlexLayout { Wrap = FlexWrap.Wrap, ColumnSpacing = 6, RowSpacing = 6 };
        foreach (var label in shinyLabels)
            shiny.Children.Add(label);

        // MAUI's FlexLayout has no gap, so its children carry a margin for the same spacing.
        var maui = new FlexLayout { Wrap = FlexWrap.Wrap };
        foreach (var label in mauiLabels)
        {
            label.Margin = new Thickness(0, 0, 6, 6);
            maui.Children.Add(label);
        }

        this.BenchHost.Children.Add(shiny);
        this.BenchHost.Children.Add(maui);

        try
        {
            var shinyMs = Time(shiny, shinyLabels, width);
            var mauiMs = Time(maui, mauiLabels, width);

            this.BenchResult.Text =
                $"{count} labels, {Passes} passes (alternating width + one label changing each pass)\n" +
                $"ShinyFlexLayout: {shinyMs:F1} ms ({shinyMs / Passes:F2} ms/pass)\n" +
                $"MAUI FlexLayout: {mauiMs:F1} ms ({mauiMs / Passes:F2} ms/pass)\n" +
                $"{mauiMs / Math.Max(0.001, shinyMs):F1}× faster";
        }
        finally
        {
            this.BenchHost.Children.Clear();
            button.IsEnabled = true;
        }
    }


    static List<Label> CreateLabels(int count)
    {
        var labels = new List<Label>(count);
        for (var i = 0; i < count; i++)
            labels.Add(new Label { Text = Words[i % Words.Length] + " " + i, FontSize = 14 });
        return labels;
    }


    static double Time(IView layout, List<Label> labels, double width)
    {
        // One warm-up pass so neither side pays for first-time JIT inside the timing.
        layout.Measure(width, double.PositiveInfinity);

        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < Passes; i++)
        {
            labels[i * 7 % labels.Count].Text = Words[i % Words.Length] + "!";
            layout.Measure(i % 2 == 0 ? width * 0.75 : width, double.PositiveInfinity);
        }
        return stopwatch.Elapsed.TotalMilliseconds;
    }
}

using System.Collections.Specialized;
using System.Globalization;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.FontPicker;

public class FontSizePicker : ContentView
{
    readonly VerticalStackLayout listLayout;
    readonly ScrollView scrollView;

    public FontSizePicker()
    {
        listLayout = new VerticalStackLayout { Spacing = 0 };
        scrollView = new ScrollView
        {
            Content = listLayout,
            VerticalScrollBarVisibility = ScrollBarVisibility.Default
        };
        Content = scrollView;

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(FontSizePicker));
    }

    public static readonly BindableProperty AvailableFontSizesProperty = BindableProperty.Create(
        nameof(AvailableFontSizes),
        typeof(IList<double>),
        typeof(FontSizePicker),
        null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(FontSizePicker), () =>
            {
                ((FontSizePicker)b).OnAvailableFontSizesChanged(o as IList<double>, n as IList<double>);
            }));

    public IList<double>? AvailableFontSizes
    {
        get => (IList<double>?)GetValue(AvailableFontSizesProperty);
        set => SetValue(AvailableFontSizesProperty, value);
    }

    public static readonly BindableProperty SelectedFontSizeProperty = BindableProperty.Create(
        nameof(SelectedFontSize),
        typeof(double),
        typeof(FontSizePicker),
        16.0,
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(FontSizePicker), () =>
            {
                ((FontSizePicker)b).OnSelectedFontSizeChanged((double)n);
            }));

    public double SelectedFontSize
    {
        get => (double)GetValue(SelectedFontSizeProperty);
        set => SetValue(SelectedFontSizeProperty, value);
    }

    public static readonly BindableProperty PreviewTextProperty = BindableProperty.Create(
        nameof(PreviewText),
        typeof(string),
        typeof(FontSizePicker),
        "Aa",
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(FontSizePicker), () =>
            {
                ((FontSizePicker)b).Rebuild();
            }));

    public string PreviewText
    {
        get => (string)GetValue(PreviewTextProperty);
        set => SetValue(PreviewTextProperty, value);
    }

    public event EventHandler<double>? FontSizeChanged;

    IDisposable? listSubscription;

    void OnAvailableFontSizesChanged(IList<double>? oldList, IList<double>? newList)
    {
        // Weak: a bound collection can outlive the page.
        listSubscription?.Dispose();
        listSubscription = WeakEventSubscription.CollectionChanged(newList, OnCollectionChanged);
        Rebuild();
    }

    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    void OnSelectedFontSizeChanged(double size)
    {
        UpdateSelectionHighlight();
        FontSizeChanged?.Invoke(this, size);
    }

    void Rebuild()
    {
        listLayout.Children.Clear();
        var sizes = AvailableFontSizes;
        if (sizes == null) return;

        foreach (var size in sizes)
        {
            var label = new Label
            {
                Text = $"{size.ToString(CultureInfo.InvariantCulture)} pt — {PreviewText}",
                FontSize = size,
                Padding = new Thickness(12, 8),
                VerticalTextAlignment = TextAlignment.Center
            };

            var border = new Border
            {
                Content = label,
                Stroke = Colors.Transparent,
                StrokeThickness = 0,
                BackgroundColor = Colors.Transparent,
                Padding = 0,
                StyleId = size.ToString(CultureInfo.InvariantCulture)
            };

            var tap = new TapGestureRecognizer();
            var captured = size;
            tap.Tapped += (_, _) => SelectedFontSize = captured;
            border.GestureRecognizers.Add(tap);

            listLayout.Children.Add(border);
        }

        UpdateSelectionHighlight();
    }

    void UpdateSelectionHighlight()
    {
        var selected = SelectedFontSize.ToString(CultureInfo.InvariantCulture);
        foreach (var child in listLayout.Children)
        {
            if (child is Border b)
            {
                var isSelected = b.StyleId == selected;
                if (isSelected)
                {
                    b.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SecondaryContainer);
                }
                else
                {
                    b.RemoveDynamicResource(VisualElement.BackgroundColorProperty);
                    b.BackgroundColor = Colors.Transparent;
                }
            }
        }
    }
}

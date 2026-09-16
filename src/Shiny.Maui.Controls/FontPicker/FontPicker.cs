using System.Collections;
using System.Collections.Specialized;
using System.Windows.Input;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.FontPicker;

public class FontPicker : ContentView
{
    readonly VerticalStackLayout listLayout;
    readonly ScrollView scrollView;

    public FontPicker()
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
        StyleGuard.MarkReady(this, typeof(FontPicker));
    }

    public static readonly BindableProperty AvailableFontsProperty = BindableProperty.Create(
        nameof(AvailableFonts),
        typeof(IList<string>),
        typeof(FontPicker),
        null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(FontPicker), () =>
            {
                ((FontPicker)b).OnAvailableFontsChanged(o as IList<string>, n as IList<string>);
            }));

    public IList<string>? AvailableFonts
    {
        get => (IList<string>?)GetValue(AvailableFontsProperty);
        set => SetValue(AvailableFontsProperty, value);
    }

    public static readonly BindableProperty SelectedFontProperty = BindableProperty.Create(
        nameof(SelectedFont),
        typeof(string),
        typeof(FontPicker),
        null,
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(FontPicker), () =>
            {
                ((FontPicker)b).OnSelectedFontChanged(n as string);
            }));

    public string? SelectedFont
    {
        get => (string?)GetValue(SelectedFontProperty);
        set => SetValue(SelectedFontProperty, value);
    }

    public static readonly BindableProperty PreviewTextProperty = BindableProperty.Create(
        nameof(PreviewText),
        typeof(string),
        typeof(FontPicker),
        "The quick brown fox",
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(FontPicker), () =>
            {
                ((FontPicker)b).Rebuild();
            }));

    public string PreviewText
    {
        get => (string)GetValue(PreviewTextProperty);
        set => SetValue(PreviewTextProperty, value);
    }

    public static readonly BindableProperty PreviewFontSizeProperty = BindableProperty.Create(
        nameof(PreviewFontSize),
        typeof(double),
        typeof(FontPicker),
        18.0,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(FontPicker), () =>
            {
                ((FontPicker)b).Rebuild();
            }));

    public double PreviewFontSize
    {
        get => (double)GetValue(PreviewFontSizeProperty);
        set => SetValue(PreviewFontSizeProperty, value);
    }

    public event EventHandler<string>? FontChanged;

    IDisposable? listSubscription;

    void OnAvailableFontsChanged(IList<string>? oldList, IList<string>? newList)
    {
        // Weak: a bound collection can outlive the page.
        listSubscription?.Dispose();
        listSubscription = WeakEventSubscription.CollectionChanged(newList, OnCollectionChanged);

        Rebuild();
    }

    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    void OnSelectedFontChanged(string? font)
    {
        UpdateSelectionHighlight();
        if (font is not null)
            FontChanged?.Invoke(this, font);
    }

    void Rebuild()
    {
        listLayout.Children.Clear();

        var fonts = AvailableFonts;
        if (fonts == null)
            return;

        foreach (var font in fonts)
        {
            var label = new Label
            {
                Text = string.IsNullOrEmpty(PreviewText) ? font : $"{font}  —  {PreviewText}",
                FontFamily = font,
                FontSize = PreviewFontSize,
                Padding = new Thickness(12, 10),
                VerticalTextAlignment = TextAlignment.Center
            };

            var border = new Border
            {
                Content = label,
                Stroke = Colors.Transparent,
                StrokeThickness = 0,
                BackgroundColor = Colors.Transparent,
                Padding = 0,
                StyleId = font
            };

            var tap = new TapGestureRecognizer();
            var captured = font;
            tap.Tapped += (_, _) => SelectedFont = captured;
            border.GestureRecognizers.Add(tap);

            listLayout.Children.Add(border);
        }

        UpdateSelectionHighlight();
    }

    void UpdateSelectionHighlight()
    {
        foreach (var child in listLayout.Children)
        {
            if (child is Border b)
            {
                var isSelected = !string.IsNullOrEmpty(SelectedFont) && b.StyleId == SelectedFont;
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

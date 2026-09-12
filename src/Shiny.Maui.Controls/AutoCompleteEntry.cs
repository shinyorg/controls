using System.Collections;
using System.Windows.Input;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

public class AutoCompleteEntry : ContentView
{
    const int DefaultDebounceMs = 500;
    const double DefaultMaxDropDownHeight = 200;

    readonly BorderlessEntry entry;
    readonly ActivityIndicator spinner;
    readonly VerticalStackLayout suggestionStack;
    readonly ScrollView dropDownScroll;
    readonly Border dropDownBorder;
    readonly Microsoft.Maui.Controls.Shapes.RoundRectangle dropDownShape;
    readonly Grid rootGrid;

    CancellationTokenSource? debounceCts;
    bool suppressTextChanged;
    IList? currentFilteredItems;

    /// <remarks>
    /// An implicit Style targeting this type is applied by MAUI *before this constructor body
    /// runs at all* - StyleableElement's own constructor creates a MergedStyle, whose ctor
    /// calls RegisterImplicitStyles(), which resolves the style out of
    /// Application.Current.Resources and applies it there and then:
    ///
    ///     at Microsoft.Maui.Controls.Setter.Apply(...)
    ///     at Microsoft.Maui.Controls.MergedStyle.set_ImplicitStyle(...)
    ///     at Microsoft.Maui.Controls.MergedStyle.RegisterImplicitStyles()
    ///     at Microsoft.Maui.Controls.MergedStyle..ctor(...)
    ///     at Microsoft.Maui.Controls.StyleableElement..ctor()
    ///
    /// So the setters run the propertyChanged callbacks below while every field here is still
    /// null, and the page dies on inflation with a NullReferenceException. Reordering this
    /// constructor cannot help - nothing in it has run yet. See
    /// <see cref="Infrastructure.StyleGuard"/>: the callbacks below queue through it, and the
    /// MarkReady call at the end of this constructor replays them.
    /// </remarks>
    public AutoCompleteEntry()
    {
        entry = new BorderlessEntry
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center
        }.WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);
        entry.TextChanged += OnEntryTextChanged;
        entry.Focused += (_, _) => OnEntryFocused();
        entry.Unfocused += (_, _) =>
        {
            // delay to allow tap on suggestion to register
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), HideDropDown);
        };

        spinner = new ActivityIndicator
        {
            IsRunning = false,
            IsVisible = false,
            WidthRequest = 20,
            HeightRequest = 20,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End
        };

        var entryGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            VerticalOptions = LayoutOptions.Center
        };
        entryGrid.Add(entry, 0, 0);
        entryGrid.Add(spinner, 1, 0);

        suggestionStack = new VerticalStackLayout
        {
            Spacing = 0
        };

        dropDownScroll = new ScrollView
        {
            Content = suggestionStack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Default
        };

        dropDownShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle()
            .WithCornerRadius(ShinyThemeKeys.Shape.CornerExtraSmallRadius);

        dropDownBorder = new Border
        {
            IsVisible = false,
            Padding = 0,
            StrokeShape = dropDownShape,
            MaximumHeightRequest = DefaultMaxDropDownHeight,
            Content = dropDownScroll
        }
        .WithStrokeThickness(ShinyThemeKeys.Border.Thin)
        .WithElevation(ShinyThemeKeys.Elevation.Level2);

        rootGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };
        rootGrid.Add(entryGrid, 0, 0);
        rootGrid.Add(dropDownBorder, 0, 1);

        Content = rootGrid;

        // Theme defaults for anything the consumer left unset.
        ApplySpinnerColor();
        ApplyDropDownBackground();
        ApplyDropDownStroke();

        // Last line: replays any callback that fired before the children existed.
        StyleGuard.MarkReady(this, typeof(AutoCompleteEntry));
    }




    // Each of these takes the explicit colour when one is set, and otherwise falls back to the
    // theme resource. Shared by the constructor and the propertyChanged callbacks so the
    // "unset means theme default" behaviour is defined in exactly one place.

    void ApplySpinnerColor()
    {
        if (this.SpinnerColor is Color c)
            spinner.Color = c;
        else
            spinner.SetDynamicResource(ActivityIndicator.ColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
    }

    void ApplyDropDownBackground()
    {
        if (this.DropDownBackgroundColor is Color c)
            dropDownBorder.BackgroundColor = c;
        else
            dropDownBorder.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);
    }

    void ApplyDropDownStroke()
    {
        if (this.DropDownBorderColor is Color c)
        {
            dropDownBorder.Stroke = c;
        }
        else
        {
            ThemeBrush.Apply(dropDownBorder, Border.StrokeProperty, ShinyThemeKeys.Brush.Outline);
        }
    }




    // --- Bindable Properties ---

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(AutoCompleteEntry),
        string.Empty,
        BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<AutoCompleteEntry>(b, ctrl =>
        {
            if (ctrl.entry.Text != (string)n)
            {
                ctrl.suppressTextChanged = true;
                ctrl.entry.Text = (string)n;
                ctrl.suppressTextChanged = false;
            }
        }));
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder),
        typeof(string),
        typeof(AutoCompleteEntry),
        string.Empty,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<AutoCompleteEntry>(b, ctrl => ctrl.entry.Placeholder = (string)n));
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public static readonly BindableProperty PlaceholderColorProperty = BindableProperty.Create(
        nameof(PlaceholderColor),
        typeof(Color),
        typeof(AutoCompleteEntry),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<AutoCompleteEntry>(b, ctrl => { if (n is Color c) ctrl.entry.PlaceholderColor = c; }));
    public Color? PlaceholderColor
    {
        get => (Color?)GetValue(PlaceholderColorProperty);
        set => SetValue(PlaceholderColorProperty, value);
    }

    public static readonly BindableProperty ItemsSourceProperty = BindableProperty.Create(
        nameof(ItemsSource),
        typeof(IList),
        typeof(AutoCompleteEntry),
        null);
    public IList? ItemsSource
    {
        get => (IList?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly BindableProperty SearchCommandProperty = BindableProperty.Create(
        nameof(SearchCommand),
        typeof(ICommand),
        typeof(AutoCompleteEntry),
        null);
    public ICommand? SearchCommand
    {
        get => (ICommand?)GetValue(SearchCommandProperty);
        set => SetValue(SearchCommandProperty, value);
    }

    public static readonly BindableProperty SelectedItemProperty = BindableProperty.Create(
        nameof(SelectedItem),
        typeof(object),
        typeof(AutoCompleteEntry),
        null,
        BindingMode.TwoWay);
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public static readonly BindableProperty IsBusyProperty = BindableProperty.Create(
        nameof(IsBusy),
        typeof(bool),
        typeof(AutoCompleteEntry),
        false,
        BindingMode.TwoWay,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<AutoCompleteEntry>(b, ctrl =>
        {
            var busy = (bool)n;
            ctrl.spinner.IsRunning = busy;
            ctrl.spinner.IsVisible = busy;
        }));
    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public static readonly BindableProperty ItemTemplateProperty = BindableProperty.Create(
        nameof(ItemTemplate),
        typeof(DataTemplate),
        typeof(AutoCompleteEntry),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<AutoCompleteEntry>(b, ctrl =>
        {
            // Re-render if items are already showing
            if (ctrl.currentFilteredItems != null)
                ctrl.RenderDropDownItems(ctrl.currentFilteredItems);
        }));
    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public static readonly BindableProperty TextMemberPathProperty = BindableProperty.Create(
        nameof(TextMemberPath),
        typeof(string),
        typeof(AutoCompleteEntry),
        null);
    public string? TextMemberPath
    {
        get => (string?)GetValue(TextMemberPathProperty);
        set => SetValue(TextMemberPathProperty, value);
    }

    public static readonly BindableProperty DebounceIntervalProperty = BindableProperty.Create(
        nameof(DebounceInterval),
        typeof(int),
        typeof(AutoCompleteEntry),
        DefaultDebounceMs);
    public int DebounceInterval
    {
        get => (int)GetValue(DebounceIntervalProperty);
        set => SetValue(DebounceIntervalProperty, value);
    }

    public static readonly BindableProperty ThresholdProperty = BindableProperty.Create(
        nameof(Threshold),
        typeof(int),
        typeof(AutoCompleteEntry),
        1);
    public int Threshold
    {
        get => (int)GetValue(ThresholdProperty);
        set => SetValue(ThresholdProperty, value);
    }

    public static readonly BindableProperty MaxDropDownHeightProperty = BindableProperty.Create(
        nameof(MaxDropDownHeight),
        typeof(double),
        typeof(AutoCompleteEntry),
        DefaultMaxDropDownHeight,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<AutoCompleteEntry>(b, ctrl => ctrl.dropDownBorder.MaximumHeightRequest = (double)n));
    public double MaxDropDownHeight
    {
        get => (double)GetValue(MaxDropDownHeightProperty);
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor),
        typeof(Color),
        typeof(AutoCompleteEntry),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<AutoCompleteEntry>(b, ctrl => { if (n is Color c) ctrl.entry.TextColor = c; }));
    public Color? TextColor
    {
        get => (Color?)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public static readonly BindableProperty DropDownBackgroundColorProperty = BindableProperty.Create(
        nameof(DropDownBackgroundColor),
        typeof(Color),
        typeof(AutoCompleteEntry),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<AutoCompleteEntry>(b, ctrl => ctrl.ApplyDropDownBackground()));
    public Color? DropDownBackgroundColor
    {
        get => (Color?)GetValue(DropDownBackgroundColorProperty);
        set => SetValue(DropDownBackgroundColorProperty, value);
    }

    public static readonly BindableProperty DropDownBorderColorProperty = BindableProperty.Create(
        nameof(DropDownBorderColor),
        typeof(Color),
        typeof(AutoCompleteEntry),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<AutoCompleteEntry>(b, ctrl => ctrl.ApplyDropDownStroke()));
    public Color? DropDownBorderColor
    {
        get => (Color?)GetValue(DropDownBorderColorProperty);
        set => SetValue(DropDownBorderColorProperty, value);
    }

    public static readonly BindableProperty SpinnerColorProperty = BindableProperty.Create(
        nameof(SpinnerColor),
        typeof(Color),
        typeof(AutoCompleteEntry),
        null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<AutoCompleteEntry>(b, ctrl => ctrl.ApplySpinnerColor()));
    public Color? SpinnerColor
    {
        get => (Color?)GetValue(SpinnerColorProperty);
        set => SetValue(SpinnerColorProperty, value);
    }

    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize),
        typeof(double),
        typeof(AutoCompleteEntry),
        ThemeTokens.Unset,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<AutoCompleteEntry>(
            b,
            ctrl => ctrl.entry.SetTokenOrValue(Entry.FontSizeProperty, (double)n, ShinyThemeKeys.Type.BodyMediumSize)));
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public static readonly BindableProperty FontFamilyProperty = BindableProperty.Create(
        nameof(FontFamily),
        typeof(string),
        typeof(AutoCompleteEntry),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<AutoCompleteEntry>(b, ctrl => ctrl.entry.FontFamily = n as string));
    public string? FontFamily
    {
        get => (string?)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public static readonly BindableProperty FontAttributesProperty = BindableProperty.Create(
        nameof(FontAttributes),
        typeof(FontAttributes),
        typeof(AutoCompleteEntry),
        Microsoft.Maui.Controls.FontAttributes.None,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<AutoCompleteEntry>(b, ctrl => ctrl.entry.FontAttributes = (FontAttributes)n));
    public FontAttributes FontAttributes
    {
        get => (FontAttributes)GetValue(FontAttributesProperty);
        set => SetValue(FontAttributesProperty, value);
    }

    public static readonly BindableProperty ShowAllOnFocusProperty = BindableProperty.Create(
        nameof(ShowAllOnFocus),
        typeof(bool),
        typeof(AutoCompleteEntry),
        false);
    public bool ShowAllOnFocus
    {
        get => (bool)GetValue(ShowAllOnFocusProperty);
        set => SetValue(ShowAllOnFocusProperty, value);
    }

    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(
        nameof(CornerRadius),
        typeof(double),
        typeof(AutoCompleteEntry),
        ThemeTokens.Unset,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<AutoCompleteEntry>(
            b,
            ctrl => ctrl.dropDownShape.SetCornerTokenOrValue((double)n, ShinyThemeKeys.Shape.CornerExtraSmallRadius)));
    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }


    // --- Events ---

    public event EventHandler<object?>? ItemSelected;


    // --- Private Methods ---

    void OnEntryFocused()
    {
        if (ShowAllOnFocus && SearchCommand == null)
        {
            var searchText = Text ?? string.Empty;
            if (searchText.Length < Threshold)
            {
                var source = ItemsSource;
                if (source != null && source.Count > 0)
                {
                    var all = new List<object>();
                    foreach (var item in source)
                        all.Add(item);
                    RenderDropDownItems(all);
                    ShowDropDown();
                }
                return;
            }
        }

        ShowDropDown();
    }

    void OnEntryTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (suppressTextChanged)
            return;

        Text = e.NewTextValue;
        SelectedItem = null;

        debounceCts?.Cancel();

        var searchText = e.NewTextValue ?? string.Empty;
        if (searchText.Length < Threshold)
        {
            if (ShowAllOnFocus && entry.IsFocused && SearchCommand == null)
            {
                var source = ItemsSource;
                if (source != null && source.Count > 0)
                {
                    var all = new List<object>();
                    foreach (var item in source)
                        all.Add(item);
                    RenderDropDownItems(all);
                    ShowDropDown();
                    return;
                }
            }
            HideDropDown();
            return;
        }

        debounceCts = new CancellationTokenSource();
        var token = debounceCts.Token;
        var debounce = DebounceInterval;

        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(debounce), () =>
        {
            if (token.IsCancellationRequested)
                return;

            PerformSearch(searchText);
        });
    }

    void PerformSearch(string searchText)
    {
        if (SearchCommand != null)
        {
            if (SearchCommand.CanExecute(searchText))
                SearchCommand.Execute(searchText);

            ShowDropDown();
        }
        else
        {
            FilterLocalItems(searchText);
        }
    }

    void FilterLocalItems(string searchText)
    {
        var source = ItemsSource;
        if (source == null || source.Count == 0)
        {
            HideDropDown();
            return;
        }

        var filtered = new List<object>();
        var comparison = StringComparison.OrdinalIgnoreCase;

        foreach (var item in source)
        {
            var text = GetDisplayText(item);
            if (text.Contains(searchText, comparison))
                filtered.Add(item);
        }

        if (filtered.Count > 0)
        {
            RenderDropDownItems(filtered);
            ShowDropDown();
        }
        else
        {
            HideDropDown();
        }
    }

    void RenderDropDownItems(IList items)
    {
        currentFilteredItems = items;
        suggestionStack.Children.Clear();

        foreach (var item in items)
        {
            View itemView;

            if (ItemTemplate != null)
            {
                var content = ItemTemplate.CreateContent();
                if (content is View v)
                {
                    v.BindingContext = item;
                    itemView = v;
                }
                else if (content is ViewCell cell)
                {
                    cell.BindingContext = item;
                    itemView = cell.View;
                }
                else
                {
                    itemView = CreateDefaultItemView(item);
                }
            }
            else
            {
                itemView = CreateDefaultItemView(item);
            }

            var tapGesture = new TapGestureRecognizer();
            var capturedItem = item;
            tapGesture.Tapped += (_, _) => OnItemTapped(capturedItem);
            itemView.GestureRecognizers.Add(tapGesture);

            suggestionStack.Children.Add(itemView);
        }
    }

    View CreateDefaultItemView(object item)
    {
        var label = new Label
        {
            Text = GetDisplayText(item),
            Padding = new Thickness(10, 8),
            VerticalTextAlignment = TextAlignment.Center
        }.WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);
        return label;
    }

    string GetDisplayText(object item)
    {
        if (item is string s)
            return s;

        if (!string.IsNullOrEmpty(TextMemberPath))
        {
            var prop = item.GetType().GetProperty(TextMemberPath);
            if (prop != null)
                return prop.GetValue(item)?.ToString() ?? string.Empty;
        }

        return item.ToString() ?? string.Empty;
    }

    void OnItemTapped(object item)
    {
        SelectedItem = item;

        suppressTextChanged = true;
        var displayText = GetDisplayText(item);
        entry.Text = displayText;
        Text = displayText;
        suppressTextChanged = false;

        HideDropDown();
        ItemSelected?.Invoke(this, item);
    }

    void ShowDropDown()
    {
        if (currentFilteredItems != null && currentFilteredItems.Count > 0)
            dropDownBorder.IsVisible = true;
    }

    void HideDropDown()
    {
        dropDownBorder.IsVisible = false;
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        // When ItemsSource changes externally (e.g. from SearchCommand callback), update dropdown
        if (propertyName == nameof(ItemsSource))
        {
            if (ItemsSource != null)
                RenderDropDownItems(ItemsSource);

            if (entry.IsFocused && !string.IsNullOrEmpty(Text) && Text.Length >= Threshold)
                ShowDropDown();
        }
    }
}

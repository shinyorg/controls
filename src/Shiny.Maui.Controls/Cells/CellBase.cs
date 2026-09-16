using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Shiny.Maui.Controls.Infrastructure;
using TvTableView = Shiny.Maui.Controls.TableView;
using TvTableSection = Shiny.Maui.Controls.Sections.TableSection;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Cells;

public abstract class CellBase : ContentView
{
    Border border = default!;
    Grid rootGrid = default!;
    Image iconImage = default!;
    Label titleLabel = default!;
    Label descriptionLabel = default!;
    Label hintLabel = default!;
    View? accessoryView;

    public CellBase()
    {
        BuildLayout();
        var tapGesture = new TapGestureRecognizer();
        tapGesture.Tapped += OnCellTapped;
        GestureRecognizers.Add(tapGesture);

        // Every field this level's callbacks touch is built by BuildLayout above, so the cell
        // base is ready here - concrete cells mark their own level when their constructor ends.
        StyleGuard.MarkReady(this, typeof(CellBase));
    }


    public static readonly BindableProperty IconSourceProperty = BindableProperty.Create(
        nameof(IconSource), typeof(ImageSource), typeof(CellBase), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).UpdateIconVisibility();
            }));

    public static readonly BindableProperty IconSizeProperty = BindableProperty.Create(
        nameof(IconSize), typeof(double), typeof(CellBase), -1d,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            { ((CellBase)b).UpdateIconSize(); ((CellBase)b).UpdateIconRadius(); }));

    public static readonly BindableProperty IconRadiusProperty = BindableProperty.Create(
        nameof(IconRadius), typeof(double), typeof(CellBase), -1d,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).UpdateIconRadius();
            }));

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(CellBase), string.Empty,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).titleLabel.Text = (string)n;
            }));

    public static readonly BindableProperty TitleColorProperty = BindableProperty.Create(
        nameof(TitleColor), typeof(Color), typeof(CellBase), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).UpdateTitleColor();
            }));

    public static readonly BindableProperty TitleFontSizeProperty = BindableProperty.Create(
        nameof(TitleFontSize), typeof(double), typeof(CellBase), -1d,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).UpdateTitleFontSize();
            }));

    public static readonly BindableProperty TitleFontFamilyProperty = BindableProperty.Create(
        nameof(TitleFontFamily), typeof(string), typeof(CellBase), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).UpdateTitleFontFamily();
            }));

    public static readonly BindableProperty TitleFontAttributesProperty = BindableProperty.Create(
        nameof(TitleFontAttributes), typeof(FontAttributes?), typeof(CellBase), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).UpdateTitleFontAttributes();
            }));

    public static readonly BindableProperty DescriptionProperty = BindableProperty.Create(
        nameof(Description), typeof(string), typeof(CellBase), string.Empty,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).UpdateDescription();
            }));

    public static readonly BindableProperty DescriptionColorProperty = BindableProperty.Create(
        nameof(DescriptionColor), typeof(Color), typeof(CellBase), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).UpdateDescriptionColor();
            }));

    public static readonly BindableProperty DescriptionFontSizeProperty = BindableProperty.Create(
        nameof(DescriptionFontSize), typeof(double), typeof(CellBase), -1d,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).UpdateDescriptionFontSize();
            }));

    public static readonly BindableProperty DescriptionFontFamilyProperty = BindableProperty.Create(
        nameof(DescriptionFontFamily), typeof(string), typeof(CellBase), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).UpdateDescriptionFontFamily();
            }));

    public static readonly BindableProperty DescriptionFontAttributesProperty = BindableProperty.Create(
        nameof(DescriptionFontAttributes), typeof(FontAttributes?), typeof(CellBase), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).UpdateDescriptionFontAttributes();
            }));

    public static readonly BindableProperty HintTextProperty = BindableProperty.Create(
        nameof(HintText), typeof(string), typeof(CellBase), string.Empty,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).UpdateHintText();
            }));

    public static readonly BindableProperty HintTextColorProperty = BindableProperty.Create(
        nameof(HintTextColor), typeof(Color), typeof(CellBase), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).UpdateHintTextColor();
            }));

    public static readonly BindableProperty HintTextFontSizeProperty = BindableProperty.Create(
        nameof(HintTextFontSize), typeof(double), typeof(CellBase), -1d,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).UpdateHintTextFontSize();
            }));

    public static readonly BindableProperty HintFontFamilyProperty = BindableProperty.Create(
        nameof(HintFontFamily), typeof(string), typeof(CellBase), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).UpdateHintFontFamily();
            }));

    public static readonly BindableProperty HintFontAttributesProperty = BindableProperty.Create(
        nameof(HintFontAttributes), typeof(FontAttributes?), typeof(CellBase), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).UpdateHintFontAttributes();
            }));

    public static readonly BindableProperty CellBackgroundColorProperty = BindableProperty.Create(
        nameof(CellBackgroundColor), typeof(Color), typeof(CellBase), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).UpdateCellBackground();
            }));

    public static readonly BindableProperty SelectedColorProperty = BindableProperty.Create(
        nameof(SelectedColor), typeof(Color), typeof(CellBase), null);

    public static readonly BindableProperty IsSelectableProperty = BindableProperty.Create(
        nameof(IsSelectable), typeof(bool), typeof(CellBase), true);

    public static readonly BindableProperty CellHeightProperty = BindableProperty.Create(
        nameof(CellHeight), typeof(double), typeof(CellBase), -1d,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
            var cell = (CellBase)b;
            cell.HeightRequest = (double)n >= 0 ? (double)n : -1;
        }));

    public static readonly BindableProperty BorderColorProperty = BindableProperty.Create(
        nameof(BorderColor), typeof(Color), typeof(CellBase), null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).UpdateBorder();
            }));

    public static readonly BindableProperty BorderWidthProperty = BindableProperty.Create(
        nameof(BorderWidth), typeof(double), typeof(CellBase), -1d,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).UpdateBorder();
            }));

    public static readonly BindableProperty BorderRadiusProperty = BindableProperty.Create(
        nameof(BorderRadius), typeof(double), typeof(CellBase), -1d,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(CellBase), () =>
            {
                ((CellBase)b).UpdateBorder();
            }));

    public static readonly BindableProperty UseFeedbackProperty = BindableProperty.Create(
        nameof(UseFeedback), typeof(bool), typeof(CellBase), true);

    public bool UseFeedback
    {
        get => (bool)GetValue(UseFeedbackProperty);
        set => SetValue(UseFeedbackProperty, value);
    }


    public ImageSource? IconSource
    {
        get => (ImageSource?)GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public double IconRadius
    {
        get => (double)GetValue(IconRadiusProperty);
        set => SetValue(IconRadiusProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public Color? TitleColor
    {
        get => (Color?)GetValue(TitleColorProperty);
        set => SetValue(TitleColorProperty, value);
    }

    public double TitleFontSize
    {
        get => (double)GetValue(TitleFontSizeProperty);
        set => SetValue(TitleFontSizeProperty, value);
    }

    public string? TitleFontFamily
    {
        get => (string?)GetValue(TitleFontFamilyProperty);
        set => SetValue(TitleFontFamilyProperty, value);
    }

    public FontAttributes? TitleFontAttributes
    {
        get => (FontAttributes?)GetValue(TitleFontAttributesProperty);
        set => SetValue(TitleFontAttributesProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public Color? DescriptionColor
    {
        get => (Color?)GetValue(DescriptionColorProperty);
        set => SetValue(DescriptionColorProperty, value);
    }

    public double DescriptionFontSize
    {
        get => (double)GetValue(DescriptionFontSizeProperty);
        set => SetValue(DescriptionFontSizeProperty, value);
    }

    public string? DescriptionFontFamily
    {
        get => (string?)GetValue(DescriptionFontFamilyProperty);
        set => SetValue(DescriptionFontFamilyProperty, value);
    }

    public FontAttributes? DescriptionFontAttributes
    {
        get => (FontAttributes?)GetValue(DescriptionFontAttributesProperty);
        set => SetValue(DescriptionFontAttributesProperty, value);
    }

    public string HintText
    {
        get => (string)GetValue(HintTextProperty);
        set => SetValue(HintTextProperty, value);
    }

    public Color? HintTextColor
    {
        get => (Color?)GetValue(HintTextColorProperty);
        set => SetValue(HintTextColorProperty, value);
    }

    public double HintTextFontSize
    {
        get => (double)GetValue(HintTextFontSizeProperty);
        set => SetValue(HintTextFontSizeProperty, value);
    }

    public string? HintFontFamily
    {
        get => (string?)GetValue(HintFontFamilyProperty);
        set => SetValue(HintFontFamilyProperty, value);
    }

    public FontAttributes? HintFontAttributes
    {
        get => (FontAttributes?)GetValue(HintFontAttributesProperty);
        set => SetValue(HintFontAttributesProperty, value);
    }

    public Color? CellBackgroundColor
    {
        get => (Color?)GetValue(CellBackgroundColorProperty);
        set => SetValue(CellBackgroundColorProperty, value);
    }

    public Color? SelectedColor
    {
        get => (Color?)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    public bool IsSelectable
    {
        get => (bool)GetValue(IsSelectableProperty);
        set => SetValue(IsSelectableProperty, value);
    }

    public double CellHeight
    {
        get => (double)GetValue(CellHeightProperty);
        set => SetValue(CellHeightProperty, value);
    }

    public Color? BorderColor
    {
        get => (Color?)GetValue(BorderColorProperty);
        set => SetValue(BorderColorProperty, value);
    }

    public double BorderWidth
    {
        get => (double)GetValue(BorderWidthProperty);
        set => SetValue(BorderWidthProperty, value);
    }

    public double BorderRadius
    {
        get => (double)GetValue(BorderRadiusProperty);
        set => SetValue(BorderRadiusProperty, value);
    }



    public event EventHandler? Tapped;



    internal TvTableView? ParentTableView { get; set; }
    internal TvTableSection? ParentSection { get; set; }



    /// <summary>Gap between the icon/accessory and the title block. See <see cref="BuildLayout"/>.</summary>
    const double ColumnGap = 12;

    void BuildLayout()
    {
        rootGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),   // Icon
                new ColumnDefinition(GridLength.Star),   // Title/Description
                new ColumnDefinition(GridLength.Auto)    // Accessory
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            Padding = new Thickness(16, 12),
            // The gaps around the icon and the accessory are margins on those two children rather
            // than ColumnSpacing. Both columns are Auto and both are usually empty: an icon-less
            // cell (the common case) would still pay the spacing, which indented every title 12px
            // past the section header above it while the accessory kept the padding's 16.
            ColumnSpacing = 0,
            RowSpacing = 2
        };

        // Icon
        iconImage = new Image
        {
            WidthRequest = 24,
            HeightRequest = 24,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, ColumnGap, 0),
            IsVisible = false
        };
        Grid.SetColumn(iconImage, 0);
        Grid.SetRowSpan(iconImage, 2);
        rootGrid.Children.Add(iconImage);

        // Title + Hint area (inner grid to prevent overlap)
        titleLabel = new Label
        {
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        hintLabel = new Label
        {
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End,
            IsVisible = false
        }.WithFontSize(ShinyThemeKeys.Type.BodySmallSize);

        var titleArea = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8
        };
        Grid.SetColumn(titleLabel, 0);
        Grid.SetColumn(hintLabel, 1);
        titleArea.Children.Add(titleLabel);
        titleArea.Children.Add(hintLabel);

        Grid.SetColumn(titleArea, 1);
        Grid.SetRow(titleArea, 0);
        rootGrid.Children.Add(titleArea);

        // Description
        descriptionLabel = new Label
        {
            VerticalOptions = LayoutOptions.Start,
            LineBreakMode = LineBreakMode.WordWrap,
            IsVisible = false
        }.WithFontSize(ShinyThemeKeys.Type.BodySmallSize);
        Grid.SetColumn(descriptionLabel, 1);
        Grid.SetRow(descriptionLabel, 1);
        rootGrid.Children.Add(descriptionLabel);

        // Accessory placeholder - subclasses provide their own
        accessoryView = CreateAccessoryView();
        if (accessoryView != null)
        {
            accessoryView.VerticalOptions = LayoutOptions.Center;
            if (accessoryView.Margin == default)
                accessoryView.Margin = new Thickness(ColumnGap, 0, 0, 0);
            Grid.SetColumn(accessoryView, 2);
            Grid.SetRowSpan(accessoryView, 2);
            rootGrid.Children.Add(accessoryView);
        }

        border = new Border
        {
            StrokeThickness = 0,
            Stroke = Colors.Transparent,
            Padding = 0,
            Content = rootGrid
        };

        Content = border;
    }

    protected virtual View? CreateAccessoryView() => null;

    protected View? AccessoryView => accessoryView;
    protected Grid RootGrid => rootGrid;
    protected Border CellBorder => border;
    protected Label TitleLabel => titleLabel;
    protected Label DescriptionLabel => descriptionLabel;
    protected Label HintLabel => hintLabel;
    protected Image IconImage => iconImage;



    public void Reload()
    {
        InvalidateMeasure();
        ParentSection?.RaiseSectionChanged();
    }



    internal void ApplyCascadedStyles()
    {
        UpdateTitleColor();
        UpdateTitleFontSize();
        UpdateTitleFontFamily();
        UpdateTitleFontAttributes();
        UpdateDescriptionColor();
        UpdateDescriptionFontSize();
        UpdateDescriptionFontFamily();
        UpdateDescriptionFontAttributes();
        UpdateHintTextColor();
        UpdateHintTextFontSize();
        UpdateHintFontFamily();
        UpdateHintFontAttributes();
        UpdateIconSize();
        UpdateIconRadius();
        UpdateCellBackground();
        UpdateCellPadding();
        UpdateBorder();
        ApplyAccentStyles();
    }

    /// <summary>
    /// Binds the cell's accent-coloured accessory to the theme. Overridden by the cells that own one.
    /// It has to run here rather than only from the accent property's own propertyChanged: that
    /// callback never fires while the property sits at its unset default, which left the switch,
    /// checkbox and radio on the platform's own tint in every theme.
    /// </summary>
    protected virtual void ApplyAccentStyles() { }

    protected Color ResolveColor(Color? cellValue, Color? globalValue, Color fallback)
        => cellValue ?? globalValue ?? fallback;

    protected double ResolveDouble(double cellValue, double globalValue, double fallback)
    {
        if (cellValue >= 0) return cellValue;
        if (globalValue >= 0) return globalValue;
        return fallback;
    }

    protected FontAttributes ResolveFontAttributes(FontAttributes? cellValue, FontAttributes? globalValue)
        => cellValue ?? globalValue ?? FontAttributes.None;

    protected string? ResolveFontFamily(string? cellValue, string? globalValue)
        => cellValue ?? globalValue;

    void UpdateTitleColor()
    {
        var color = TitleColor ?? ParentTableView?.CellTitleColor;
        if (color != null)
            titleLabel.TextColor = color;
        else
            titleLabel.ClearValue(Label.TextColorProperty);
    }

    void UpdateTitleFontSize()
        => titleLabel.FontSize = ResolveDouble(TitleFontSize, ParentTableView?.CellTitleFontSize ?? -1, 16);

    void UpdateTitleFontFamily()
        => titleLabel.FontFamily = ResolveFontFamily(TitleFontFamily, ParentTableView?.CellTitleFontFamily);

    void UpdateTitleFontAttributes()
        => titleLabel.FontAttributes = ResolveFontAttributes(TitleFontAttributes, ParentTableView?.CellTitleFontAttributes);

    void UpdateDescriptionColor()
    {
        var color = DescriptionColor ?? ParentTableView?.CellDescriptionColor;
        if (color != null)
            descriptionLabel.TextColor = color;
        else
            descriptionLabel.ClearValue(Label.TextColorProperty);
    }

    void UpdateDescriptionFontSize()
        => descriptionLabel.FontSize = ResolveDouble(DescriptionFontSize, ParentTableView?.CellDescriptionFontSize ?? -1, 12);

    void UpdateDescriptionFontFamily()
        => descriptionLabel.FontFamily = ResolveFontFamily(DescriptionFontFamily, ParentTableView?.CellDescriptionFontFamily);

    void UpdateDescriptionFontAttributes()
        => descriptionLabel.FontAttributes = ResolveFontAttributes(DescriptionFontAttributes, ParentTableView?.CellDescriptionFontAttributes);

    void UpdateDescription()
    {
        var text = Description;
        descriptionLabel.Text = text;
        descriptionLabel.IsVisible = !string.IsNullOrEmpty(text);
    }

    void UpdateHintText()
    {
        var text = HintText;
        hintLabel.Text = text;
        hintLabel.IsVisible = !string.IsNullOrEmpty(text);
    }

    void UpdateHintTextColor()
    {
        var color = HintTextColor ?? ParentTableView?.CellHintTextColor;
        if (color != null)
            hintLabel.TextColor = color;
        else
            hintLabel.ClearValue(Label.TextColorProperty);
    }

    void UpdateHintTextFontSize()
        => hintLabel.FontSize = ResolveDouble(HintTextFontSize, ParentTableView?.CellHintTextFontSize ?? -1, 12);

    void UpdateHintFontFamily()
        => hintLabel.FontFamily = ResolveFontFamily(HintFontFamily, ParentTableView?.CellHintFontFamily);

    void UpdateHintFontAttributes()
        => hintLabel.FontAttributes = ResolveFontAttributes(HintFontAttributes, ParentTableView?.CellHintFontAttributes);

    void UpdateIconVisibility()
    {
        var hasIcon = IconSource != null;
        iconImage.Source = IconSource;
        iconImage.IsVisible = hasIcon;
    }

    void UpdateIconSize()
    {
        var size = ResolveDouble(IconSize, ParentTableView?.CellIconSize ?? -1, 24);
        iconImage.WidthRequest = size;
        iconImage.HeightRequest = size;
    }

    void UpdateIconRadius()
    {
        var radius = ResolveDouble(IconRadius, ParentTableView?.CellIconRadius ?? -1, 0);
        if (radius > 0)
        {
            var size = iconImage.WidthRequest > 0 ? iconImage.WidthRequest : 24;
            iconImage.Clip = new RoundRectangleGeometry(new CornerRadius(radius), new Rect(0, 0, size, size));
        }
        else
        {
            iconImage.Clip = null;
        }
    }

    void UpdateCellBackground()
    {
        var color = CellBackgroundColor ?? ParentTableView?.CellBackgroundColor;
        BackgroundColor = color ?? Colors.Transparent;
    }

    void UpdateCellPadding()
    {
        if (ParentTableView?.CellPadding is Thickness padding)
            rootGrid.Padding = padding;
    }

    void UpdateBorder()
    {
        var color = BorderColor ?? ParentTableView?.CellBorderColor;
        var width = ResolveDouble(BorderWidth, ParentTableView?.CellBorderWidth ?? -1, 0);
        var radius = ResolveDouble(BorderRadius, ParentTableView?.CellBorderRadius ?? -1, 0);

        if (color != null && width > 0)
        {
            border.Stroke = new SolidColorBrush(color);
            border.StrokeThickness = width;
            border.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(radius) };
        }
        else
        {
            border.Stroke = Colors.Transparent;
            border.StrokeThickness = 0;
            border.StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(radius) };
        }
    }



    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == nameof(IsEnabled))
            Opacity = IsEnabled ? 1.0 : 0.4;
    }



    protected virtual void OnCellTapped(object? sender, TappedEventArgs e)
    {
        if (!IsEnabled || !IsSelectable)
            return;

        if (UseFeedback)
            FeedbackHelper.Execute(this, nameof(Tapped));

        ShowTapFeedback();
        Tapped?.Invoke(this, EventArgs.Empty);
        OnTapped();
    }

    protected virtual void OnTapped() { }

    protected virtual bool ShouldKeepSelection() => false;

    internal void ClearSelectionHighlight() => UpdateCellBackground();

    internal void ApplySelectionHighlight()
    {
        var color = SelectedColor ?? ParentTableView?.CellSelectedColor;
        if (color != null)
            BackgroundColor = color;
    }

    protected void RaiseTapped() => Tapped?.Invoke(this, EventArgs.Empty);

    async void ShowTapFeedback()
    {
        var color = SelectedColor ?? ParentTableView?.CellSelectedColor;
        if (color == null)
            return;

        BackgroundColor = color;

        if (!ShouldKeepSelection())
        {
            await Task.Delay(150);
            UpdateCellBackground();
        }
    }



    protected Page? GetParentPage()
    {
        Element? current = this;
        while (current != null)
        {
            if (current is Page page)
                return page;
            current = current.Parent;
        }
        return null;
    }

    Page? selectionClearPage;

    /// <summary>
    /// Clears the kept selection the next time <paramref name="page"/> appears. One pending handler
    /// per cell: every tap used to add another closure to the page's Appearing list, so a cell tapped
    /// repeatedly without navigating grew it without bound.
    /// </summary>
    protected void ClearSelectionOnAppearing(Page page)
    {
        if (this.selectionClearPage is not null)
            this.selectionClearPage.Appearing -= this.OnSelectionClearPageAppearing;

        this.selectionClearPage = page;
        page.Appearing += this.OnSelectionClearPageAppearing;
    }

    void OnSelectionClearPageAppearing(object? sender, EventArgs e)
    {
        if (this.selectionClearPage is not null)
            this.selectionClearPage.Appearing -= this.OnSelectionClearPageAppearing;

        this.selectionClearPage = null;
        ClearSelectionHighlight();
    }
}
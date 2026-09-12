using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>
/// A static segment in a <see cref="ButtonGroup"/> — a label, a unit, a prefix — sitting flush with the
/// buttons beside it.
/// </summary>
/// <remarks>
/// It is not a disabled button: a disabled button reads as an action that is currently unavailable,
/// while this is chrome that was never actionable. It takes no taps and is skipped when the group
/// counts its selectable segments.
/// </remarks>
/// <example>
/// <code language="xaml">
/// &lt;shiny:ButtonGroup&gt;
///     &lt;shiny:ButtonGroupText Text="USD" /&gt;
///     &lt;shiny:ShinyButton LeftMotionIcon="minus" /&gt;
///     &lt;shiny:ShinyButton LeftMotionIcon="plus" /&gt;
/// &lt;/shiny:ButtonGroup&gt;
/// </code>
/// </example>
public class ButtonGroupText : ContentView
{
    const double DefaultMinimumHeight = 44;

    readonly Border border;
    readonly Label label;
    readonly Grid rootGrid;

    CornerRadius? segmentCorners;

    public ButtonGroupText()
    {
        this.label = new Label
        {
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap
        }.WithFontSize(ShinyThemeKeys.Type.LabelLargeSize);
        this.label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        this.rootGrid = new Grid();
        this.rootGrid.Add(this.label);

        this.border = new Border
        {
            Padding = new Thickness(12, 10),
            Content = this.rootGrid,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        }.WithStrokeThickness(ShinyThemeKeys.Border.Thin);

        this.border.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerLow);
        ThemeBrush.Apply(this.border, Border.StrokeProperty, ShinyThemeKeys.Brush.Outline);

        this.Content = this.border;
        this.HorizontalOptions = LayoutOptions.Start;
        this.VerticalOptions = LayoutOptions.Center;
        this.MinimumHeightRequest = DefaultMinimumHeight;

        this.ApplyCornerRadius();

        // Last line: replays any styled property that was applied before the children existed.
        // See StyleGuard.
        StyleGuard.MarkReady(this, typeof(ButtonGroupText));
    }


    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(ButtonGroupText), String.Empty,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ButtonGroupText>(b, x => x.label.Text = (string?)n ?? String.Empty));
    /// <summary>The segment's text.</summary>
    public string Text
    {
        get => (string)this.GetValue(TextProperty);
        set => this.SetValue(TextProperty, value);
    }

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor), typeof(Color), typeof(ButtonGroupText), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ButtonGroupText>(b, x => x.ApplyTextColor()));
    /// <summary>Text colour. Unset follows the theme's on-surface-variant role.</summary>
    public Color? TextColor
    {
        get => (Color?)this.GetValue(TextColorProperty);
        set => this.SetValue(TextColorProperty, value);
    }

    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize), typeof(double), typeof(ButtonGroupText), ThemeTokens.Unset,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ButtonGroupText>(b, x => x.ApplyFontSize()));
    /// <summary>Font size. Unset follows the theme's label-large size.</summary>
    public double FontSize
    {
        get => (double)this.GetValue(FontSizeProperty);
        set => this.SetValue(FontSizeProperty, value);
    }

    public static readonly BindableProperty ContentPaddingProperty = BindableProperty.Create(
        nameof(ContentPadding), typeof(Thickness), typeof(ButtonGroupText), new Thickness(12, 10),
        propertyChanged: (b, _, n) => StyleGuard.WhenReady<ButtonGroupText>(b, x => x.border.Padding = (Thickness)n));
    /// <summary>
    /// Padding inside the painted surface. Named apart from <c>Padding</c> deliberately, exactly as
    /// <see cref="ShinyButton.ContentPadding"/> is: a <c>ContentView</c>'s own padding sits outside the
    /// surface, which is not what a segment's padding means.
    /// </summary>
    public Thickness ContentPadding
    {
        get => (Thickness)this.GetValue(ContentPaddingProperty);
        set => this.SetValue(ContentPaddingProperty, value);
    }

    public static readonly BindableProperty SegmentBackgroundColorProperty = BindableProperty.Create(
        nameof(SegmentBackgroundColor), typeof(Color), typeof(ButtonGroupText), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ButtonGroupText>(b, x => x.ApplyBackground()));
    /// <summary>Fill behind the text. Unset follows the theme's low surface container.</summary>
    public Color? SegmentBackgroundColor
    {
        get => (Color?)this.GetValue(SegmentBackgroundColorProperty);
        set => this.SetValue(SegmentBackgroundColorProperty, value);
    }

    public static readonly BindableProperty BorderColorProperty = BindableProperty.Create(
        nameof(BorderColor), typeof(Color), typeof(ButtonGroupText), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ButtonGroupText>(b, x => x.ApplyStroke()));
    /// <summary>Outline colour. Unset follows the theme's outline role.</summary>
    public Color? BorderColor
    {
        get => (Color?)this.GetValue(BorderColorProperty);
        set => this.SetValue(BorderColorProperty, value);
    }

    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(
        nameof(CornerRadius), typeof(double), typeof(ButtonGroupText), ThemeTokens.Unset,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ButtonGroupText>(b, x => x.ApplyCornerRadius()));
    /// <summary>Corner radius when the segment is not inside a group. Unset follows the theme.</summary>
    public double CornerRadius
    {
        get => (double)this.GetValue(CornerRadiusProperty);
        set => this.SetValue(CornerRadiusProperty, value);
    }


    /// <inheritdoc cref="ShinyButton.SetSegmentCorners"/>
    internal void SetSegmentCorners(CornerRadius? corners)
    {
        if (Nullable.Equals(this.segmentCorners, corners))
            return;

        this.segmentCorners = corners;
        StyleGuard.WhenReady<ButtonGroupText>(this, static x => x.ApplyCornerRadius());
    }


    void ApplyCornerRadius()
    {
        var shape = new RoundRectangle();

        if (this.segmentCorners is CornerRadius corners)
            shape.CornerRadius = corners;
        else
            shape.SetCornerTokenOrValue(this.CornerRadius, ShinyThemeKeys.Shape.CornerMediumRadius);

        this.border.StrokeShape = shape;
    }


    void ApplyFontSize()
        => this.label.SetTokenOrValue(Label.FontSizeProperty, this.FontSize, ShinyThemeKeys.Type.LabelLargeSize);

    void ApplyTextColor()
        => ThemeProbe.Tint(this.label, Label.TextColorProperty, this.TextColor, ShinyThemeKeys.Color.OnSurfaceVariant);

    void ApplyBackground()
        => ThemeProbe.Tint(this.border, VisualElement.BackgroundColorProperty, this.SegmentBackgroundColor, ShinyThemeKeys.Color.SurfaceContainerLow);

    void ApplyStroke()
        => ThemeBrush.Apply(this.border, Border.StrokeProperty, this.BorderColor, ShinyThemeKeys.Brush.Outline);


    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        // ContentView.Content is how a caller puts their own view in the segment; it has to land inside
        // the painted border rather than replacing it.
        if (propertyName == nameof(this.Content) && !ReferenceEquals(this.Content, this.border))
            this.AdoptContent();
    }


    void AdoptContent()
    {
        var supplied = this.Content;
        this.Content = this.border;

        if (supplied is null || ReferenceEquals(supplied, this.border))
            return;

        this.label.IsVisible = false;
        this.rootGrid.Add(supplied);
    }
}


/// <summary>
/// A hairline between two segments of a <see cref="ButtonGroup"/> — the divider in a split button.
/// </summary>
/// <remarks>
/// Reach for it between segments of the same <em>filled</em> appearance, where there is no outline to
/// separate them and the two halves would otherwise read as one very wide button. Outlined segments
/// already have an edge and need none.
/// </remarks>
/// <example>
/// <code language="xaml">
/// &lt;shiny:ButtonGroup&gt;
///     &lt;shiny:ShinyButton Text="Follow" Type="Secondary" /&gt;
///     &lt;shiny:ButtonGroupSeparator /&gt;
///     &lt;shiny:ShinyButton RightMotionIcon="chevron-down" Type="Secondary" /&gt;
/// &lt;/shiny:ButtonGroup&gt;
/// </code>
/// </example>
public class ButtonGroupSeparator : BoxView
{
    public ButtonGroupSeparator()
    {
        // BoxView paints from Color rather than Background on AppKit, and Color is what a colour token
        // can reach at all - a token on a Brush property is dropped silently.
        this.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);
        this.ApplyOrientation(StackOrientation.Horizontal);

        StyleGuard.MarkReady(this, typeof(ButtonGroupSeparator));
    }


    public static readonly BindableProperty ThicknessProperty = BindableProperty.Create(
        nameof(Thickness), typeof(double), typeof(ButtonGroupSeparator), 1d,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ButtonGroupSeparator>(b, x => x.ApplyOrientation(x.orientation)));
    /// <summary>How thick the hairline is. Defaults to 1.</summary>
    public double Thickness
    {
        get => (double)this.GetValue(ThicknessProperty);
        set => this.SetValue(ThicknessProperty, value);
    }

    StackOrientation orientation = StackOrientation.Horizontal;

    /// <summary>Turned by the owning group, so the line runs across the flow rather than along it.</summary>
    internal void SetOrientation(StackOrientation value)
    {
        this.orientation = value;
        this.ApplyOrientation(value);
    }


    void ApplyOrientation(StackOrientation value)
    {
        this.orientation = value;

        if (value == StackOrientation.Vertical)
        {
            this.HeightRequest = this.Thickness;
            this.WidthRequest = -1;
            this.HorizontalOptions = LayoutOptions.Fill;
            this.VerticalOptions = LayoutOptions.Center;
        }
        else
        {
            this.WidthRequest = this.Thickness;
            this.HeightRequest = -1;
            this.HorizontalOptions = LayoutOptions.Center;
            this.VerticalOptions = LayoutOptions.Fill;
        }
    }
}

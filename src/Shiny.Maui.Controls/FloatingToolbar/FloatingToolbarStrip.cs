using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>Raised when a leaf item is invoked, or when one with children wants its menu opened.</summary>
sealed class ToolbarItemEventArgs(ShinyToolbarItem item, View itemView) : EventArgs
{
    public ShinyToolbarItem Item { get; } = item;

    /// <summary>The view the item was drawn as — what a dropdown anchors itself to.</summary>
    public View ItemView { get; } = itemView;
}


/// <summary>
/// The bar itself: a rounded surface holding a run of item buttons, either across or down.
/// </summary>
/// <remarks>
/// Shared by the toolbar and by every dropdown it opens — a menu is the same strip laid out
/// vertically with labels turned on, which is why the item view is built once here rather than twice.
/// The strip knows nothing about anchoring or triggers; <see cref="FloatingToolbar"/> owns those.
/// </remarks>
class FloatingToolbarStrip : Border
{
    const double DefaultItemSize = 40;
    const double IconSize = 20;

    internal const string ChevronDown = "M6,9 L12,15 L18,9";
    internal const string ChevronRight = "M9,6 L15,12 L9,18";
    internal const string Ellipsis = "M5,12 h0.01 M12,12 h0.01 M19,12 h0.01";

    readonly StackLayout items;

    ToolbarOrientation orientation = ToolbarOrientation.Horizontal;
    bool showLabels;


    public FloatingToolbarStrip()
    {
        this.items = new StackLayout
        {
            Orientation = StackOrientation.Horizontal,
            Spacing = 2
        };

        this.Padding = new Thickness(4);
        this.StrokeThickness = 1;
        var shape = new Microsoft.Maui.Controls.Shapes.RoundRectangle();
        shape.SetCornerTokenOrValue(ThemeTokens.Unset, ShinyThemeKeys.Shape.CornerMediumRadius);
        this.StrokeShape = shape;
        this.Content = this.items;

        this.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);
        this.SetDynamicResource(StrokeProperty, ShinyThemeKeys.Color.OutlineVariant);
    }


    /// <summary>A leaf item was pressed.</summary>
    public event EventHandler<ToolbarItemEventArgs>? ItemInvoked;

    /// <summary>An item with children was pressed, or the overflow button was.</summary>
    public event EventHandler<ToolbarItemEventArgs>? MenuRequested;


    public ToolbarOrientation Orientation
    {
        get => this.orientation;
        set
        {
            this.orientation = value;
            this.items.Orientation = value == ToolbarOrientation.Vertical
                ? StackOrientation.Vertical
                : StackOrientation.Horizontal;
        }
    }


    /// <summary>Draw each item's text as well as its icon. Always on inside a menu.</summary>
    public bool ShowLabels
    {
        get => this.showLabels;
        set => this.showLabels = value;
    }


    /// <summary>Size of one item along the bar. The overflow maths is written against it.</summary>
    public double ItemSize { get; set; } = DefaultItemSize;

    /// <summary>Foreground for items that do not override it.</summary>
    public Color? ForegroundColor { get; set; }

    /// <summary>The items currently drawn, overflow already applied.</summary>
    public IReadOnlyList<ShinyToolbarItem> Rendered { get; private set; } = [];


    /// <summary>
    /// Rebuilds the bar.
    /// </summary>
    /// <param name="source">Every item, including ones that will fold into the overflow.</param>
    /// <param name="maxVisible">
    /// Cap on items drawn on the bar. Zero means no cap. The cap <b>counts the overflow button</b>:
    /// four items capped at four draws three and a "⋯", because adding the overflow cell to four
    /// would put the bar straight back over the width that caused the overflow.
    /// </param>
    /// <param name="overflowItem">The "⋯" item, or null to let items run off instead.</param>
    public void Build(IReadOnlyList<ShinyToolbarItem> source, int maxVisible, ShinyToolbarItem? overflowItem)
    {
        this.items.Children.Clear();

        var visible = source.Where(i => i.IsVisible).ToList();
        var overflow = new List<ShinyToolbarItem>();

        if (overflowItem is not null && maxVisible > 0 && visible.Count > maxVisible)
        {
            var keep = Math.Max(0, maxVisible - 1);
            overflow = visible.Skip(keep).ToList();
            visible = visible.Take(keep).ToList();
        }

        foreach (var item in visible)
            this.items.Children.Add(this.BuildItem(item));

        if (overflow.Count > 0 && overflowItem is not null)
        {
            overflowItem.Children.Clear();
            foreach (var item in overflow)
                overflowItem.Children.Add(item);

            this.items.Children.Add(this.BuildItem(overflowItem));
            visible.Add(overflowItem);
        }

        this.Rendered = visible;
    }


    View BuildItem(ShinyToolbarItem item)
    {
        if (item.IsSeparator)
            return this.BuildSeparator();

        var content = new StackLayout
        {
            Orientation = StackOrientation.Horizontal,
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        if (item.Icon is not null)
        {
            var image = new Image
            {
                Source = item.Icon,
                WidthRequest = IconSize,
                HeightRequest = IconSize,
                Aspect = Aspect.AspectFit,
                VerticalOptions = LayoutOptions.Center
            };
            content.Children.Add(image);
        }

        var wantsLabel = this.showLabels && !String.IsNullOrWhiteSpace(item.Text);
        if (wantsLabel || item.Icon is null)
        {
            var label = new Label
            {
                Text = item.Text,
                VerticalOptions = LayoutOptions.Center,
                VerticalTextAlignment = TextAlignment.Center
            }.WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);

            if (item.IconColor is not null)
                label.TextColor = item.IconColor;
            else if (this.ForegroundColor is not null)
                label.TextColor = this.ForegroundColor;
            else
                label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);

            content.Children.Add(label);
        }

        if (item.HasChildren)
            content.Children.Add(this.BuildChevron(item));

        var button = new Border
        {
            Padding = new Thickness(this.showLabels ? 10 : 6, 6),
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            Content = content,
            Opacity = item.IsEnabled ? 1 : 0.4
        };
        button.StrokeShape = SmallCorner();

        // Square only when there is nothing but a glyph in it; a labelled item has to size to its text.
        if (!wantsLabel && item.Icon is not null && !item.HasChildren)
        {
            button.WidthRequest = this.ItemSize;
            button.HeightRequest = this.ItemSize;
        }
        else
        {
            button.MinimumHeightRequest = this.ItemSize;
        }

        var host = item.Badge is { Length: > 0 }
            ? (View)this.WithBadge(button, item.Badge)
            : button;

        // AutomationId is set-once in MAUI, so it is stamped here and never re-stamped on rebuild.
        host.AutomationId = "toolbar-item-" + (item.Text ?? item.Tooltip ?? "item");

        if (item.IsEnabled)
        {
            var tap = new TapGestureRecognizer();
            // Command rather than the Tapped event: an event cannot be raised from a test, and this
            // is the one behaviour of the strip worth asserting without a device.
            tap.Command = new Command(() => this.Invoke(item, host));
            host.GestureRecognizers.Add(tap);
        }

        if (!String.IsNullOrWhiteSpace(item.Tooltip) || !String.IsNullOrWhiteSpace(item.Text))
            Microsoft.Maui.Controls.SemanticProperties.SetDescription(host, item.Tooltip ?? item.Text);

        return host;
    }


    void Invoke(ShinyToolbarItem item, View itemView)
    {
        if (item.HasChildren)
        {
            this.MenuRequested?.Invoke(this, new ToolbarItemEventArgs(item, itemView));
            return;
        }

        this.ItemInvoked?.Invoke(this, new ToolbarItemEventArgs(item, itemView));
    }


    Grid WithBadge(View button, string badge)
    {
        var text = new Label
        {
            Text = badge,
            Margin = new Thickness(4, 0),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        text.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);
        text.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);

        var pill = new Border
        {
            Padding = 0,
            StrokeThickness = 0,
            MinimumWidthRequest = 16,
            HeightRequest = 16,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            TranslationX = 4,
            TranslationY = -4,
            Content = text,
            InputTransparent = true
        };
        pill.StrokeShape = SmallCorner();
        pill.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.Primary);

        return new Grid { Children = { button, pill } };
    }


    /// <summary>A themed corner, so a theme pack can reshape the bar without touching this file.</summary>
    static Microsoft.Maui.Controls.Shapes.RoundRectangle SmallCorner()
    {
        var shape = new Microsoft.Maui.Controls.Shapes.RoundRectangle();
        shape.SetCornerTokenOrValue(ThemeTokens.Unset, ShinyThemeKeys.Shape.CornerSmallRadius);

        return shape;
    }


    View BuildChevron(ShinyToolbarItem item)
    {
        // Down on a bar running across, right on one running down - and menus are always vertical,
        // so a submenu's chevron points along the flyout. Labels do not change the direction.
        var path = this.orientation == ToolbarOrientation.Vertical
            ? ChevronRight
            : ChevronDown;

        var shape = new Microsoft.Maui.Controls.Shapes.Path
        {
            Data = new Microsoft.Maui.Controls.Shapes.PathGeometryConverter().ConvertFromInvariantString(path) as Microsoft.Maui.Controls.Shapes.Geometry,
            StrokeThickness = 2,
            WidthRequest = 12,
            HeightRequest = 12,
            Aspect = Stretch.Uniform,
            VerticalOptions = LayoutOptions.Center
        };

        if (item.IconColor is not null)
            shape.Stroke = item.IconColor;
        else if (this.ForegroundColor is not null)
            shape.Stroke = this.ForegroundColor;
        else
            shape.SetDynamicResource(Microsoft.Maui.Controls.Shapes.Shape.StrokeProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        return shape;
    }


    View BuildSeparator()
    {
        var rule = new BoxView();
        rule.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);

        // A separator runs across the bar, so it is thin on the axis the items run along.
        if (this.orientation == ToolbarOrientation.Vertical)
        {
            rule.HeightRequest = 1;
            rule.Margin = new Thickness(6, 4);
            rule.HorizontalOptions = LayoutOptions.Fill;
        }
        else
        {
            rule.WidthRequest = 1;
            rule.Margin = new Thickness(4, 6);
            rule.VerticalOptions = LayoutOptions.Fill;
        }

        return rule;
    }
}

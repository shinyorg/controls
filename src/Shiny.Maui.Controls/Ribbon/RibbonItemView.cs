using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Ribbons;

/// <summary>
/// The button drawn for one <see cref="RibbonItem"/>.
/// </summary>
/// <remarks>
/// <para>
/// One view per item per rebuild. It keeps a reference to the item so hover, checked and enabled
/// states are repainted in place — the ribbon only rebuilds when the shape of a tab changes, not when
/// a toggle flips, and a bar that rebuilt itself on every <c>IsChecked</c> would drop the pointer's
/// hover on the button being pressed.
/// </para>
/// <para>
/// A split button is two hit targets in one view, so this is a <see cref="Grid"/> of borders rather
/// than a single one. Every other kind uses the same grid with the second cell left out, which keeps
/// one measurement path instead of two that can disagree about height.
/// </para>
/// </remarks>
class RibbonItemView : Grid
{
    internal const double LargeIconSize = 30;
    internal const double SmallIconSize = 16;
    internal const double SmallRowHeight = 24;
    internal const double LargeMaxWidth = 92;

    readonly Ribbon owner;
    readonly RibbonTab? tab;
    readonly RibbonGroup? group;
    readonly RibbonItem item;
    readonly RibbonItemSize size;

    readonly Border face;
    readonly Border? chevronCell;

    bool faceHovered;
    bool chevronHovered;


    public RibbonItemView(
        Ribbon owner,
        RibbonTab? tab,
        RibbonGroup? group,
        RibbonItem item,
        RibbonItemSize size,
        bool showLabel = true
    )
    {
        this.owner = owner;
        this.tab = tab;
        this.group = group;
        this.item = item;
        this.size = size;

        var isSplit = item is RibbonSplitButton;

        this.face = this.BuildFace(showLabel);
        this.Add(this.face);

        if (isSplit)
        {
            // Large splits stack (face over chevron strip) and small ones sit side by side, which is
            // just where the room is in each shape.
            if (size == RibbonItemSize.Large)
            {
                this.RowDefinitions.Add(new RowDefinition(GridLength.Star));
                this.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                this.chevronCell = this.BuildChevronCell(horizontal: true);
                this.SetRow(this.chevronCell, 1);
            }
            else
            {
                this.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                this.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                this.chevronCell = this.BuildChevronCell(horizontal: false);
                this.SetColumn(this.chevronCell, 1);
            }

            this.Add(this.chevronCell);
        }

        // On the face, not on this grid. The tap recognizer lives on the face, so an id on the wrapper
        // named an element that could be found and not pressed. Putting it on both is worse: MAUI
        // de-duplicates a repeated id by suffixing one of them, so the tappable element ends up with a
        // name no test can predict.
        if (!string.IsNullOrWhiteSpace(item.AutomationId))
            this.face.AutomationId = item.AutomationId;

        SemanticProperties.SetDescription(this, item.Tooltip ?? item.Text);

        // Title plus body when there is a description, a bare one-liner otherwise: a tooltip whose
        // title and text say the same thing reads as a rendering bug.
        var hasDescription = !string.IsNullOrWhiteSpace(item.Description);
        var hint = hasDescription ? item.Description : item.TooltipWithShortcut;

        if (owner.ShowTooltips && !string.IsNullOrWhiteSpace(hint))
        {
            TooltipProperties.SetText(this, hint);
            TooltipProperties.SetTitle(this, hasDescription ? item.TooltipWithShortcut : null);
            TooltipProperties.SetTrigger(this, TooltipTrigger.Hover);
            TooltipProperties.SetPlacement(this, TooltipPlacement.Bottom);
        }

        this.Refresh();
    }


    /// <summary>The item this view was built for, so the ribbon can find it again without a dictionary.</summary>
    public RibbonItem Item => this.item;


    // ---------------------------------------------------------------------------------------------
    // Construction
    // ---------------------------------------------------------------------------------------------

    Border BuildFace(bool showLabel)
    {
        var content = this.size == RibbonItemSize.Large
            ? this.BuildLargeContent(showLabel)
            : this.BuildSmallContent(showLabel);

        var border = new Border
        {
            Content = content,
            StrokeThickness = 0,
            Stroke = null,
            BackgroundColor = Colors.Transparent,
            Padding = this.size == RibbonItemSize.Large
                ? new Thickness(6, 4)
                : new Thickness(6, 2),
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerSmallRadius)
        };

        this.Attach(border, () => this.InvokeFace(), h => this.faceHovered = h);
        return border;
    }


    View BuildLargeContent(bool showLabel)
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 2,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        if (this.BuildIcon(LargeIconSize) is { } icon)
            stack.Children.Add(icon);

        if (showLabel && !string.IsNullOrWhiteSpace(this.item.Text))
        {
            var label = new Label
            {
                Text = this.item.Text,
                HorizontalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.WordWrap,
                MaxLines = 2,
                MaximumWidthRequest = LargeMaxWidth
            }.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);
            label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
            stack.Children.Add(label);
        }

        // A menu button says so on its face; a split button says so on its own chevron cell instead.
        if (this.item is RibbonMenuButton and not RibbonSplitButton)
            stack.Children.Add(this.BuildChevron());

        return stack;
    }


    View BuildSmallContent(bool showLabel)
    {
        var stack = new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            MinimumHeightRequest = SmallRowHeight
        };

        if (this.BuildIcon(SmallIconSize) is { } icon)
            stack.Children.Add(icon);

        if (showLabel && !string.IsNullOrWhiteSpace(this.item.Text))
        {
            var label = new Label
            {
                Text = this.item.Text,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.TailTruncation
            }.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);
            label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
            stack.Children.Add(label);
        }

        if (this.item is RibbonMenuButton and not RibbonSplitButton)
            stack.Children.Add(this.BuildChevron());

        return stack;
    }


    Border BuildChevronCell(bool horizontal)
    {
        var border = new Border
        {
            Content = this.BuildChevron(),
            StrokeThickness = 0,
            Stroke = null,
            BackgroundColor = Colors.Transparent,
            Padding = horizontal ? new Thickness(4, 2) : new Thickness(3, 2),
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerSmallRadius)
        };

        SemanticProperties.SetDescription(border, $"{this.item.Text} options");
        this.Attach(border, () => this.OpenMenu(border), h => this.chevronHovered = h);
        return border;
    }


    /// <summary>
    /// The dropdown caret, drawn rather than typed.
    /// </summary>
    /// <remarks>
    /// A "▾" renders as a four-pixel speck at label size and disappears entirely on some heads, so the
    /// caret is a two-segment polyline with a real stroke width. A <see cref="Polyline"/> rather than a
    /// <c>Path</c> on purpose: MAUI's path-data parser turns an implicit lineto into a moveto, which
    /// silently draws half a chevron.
    /// </remarks>
    View BuildChevron() => new Polyline
    {
        Points = new PointCollection { new(0, 0), new(4, 4), new(8, 0) },
        Stroke = this.owner.ForegroundBrush,
        StrokeThickness = 1.4,
        StrokeLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        WidthRequest = 8,
        HeightRequest = 5,
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center
    };


    View? BuildIcon(double iconSize)
    {
        if (this.item.IconTemplate?.CreateContent() is View templated)
        {
            templated.WidthRequest = iconSize;
            templated.HeightRequest = iconSize;
            templated.HorizontalOptions = LayoutOptions.Center;
            templated.VerticalOptions = LayoutOptions.Center;
            templated.InputTransparent = true;
            return templated;
        }

        if (this.item.Icon is not { } source)
            return null;

        return new Image
        {
            Source = source,
            WidthRequest = iconSize,
            HeightRequest = iconSize,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };
    }


    /// <summary>
    /// Wires a hit target's tap and hover.
    /// </summary>
    /// <remarks>
    /// The tap goes on the recognizer's <c>Command</c>, not its <c>Tapped</c> event: both fire under a
    /// finger, but only the command can be invoked from a test, which is what makes the bar's behaviour
    /// assertable rather than only its layout.
    /// </remarks>
    void Attach(Border border, Action onTapped, Action<bool> setHovered)
    {
        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(onTapped)
        });

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) =>
        {
            setHovered(true);
            this.Refresh();
        };
        pointer.PointerExited += (_, _) =>
        {
            setHovered(false);
            this.Refresh();
        };
        border.GestureRecognizers.Add(pointer);
    }


    // ---------------------------------------------------------------------------------------------
    // State
    // ---------------------------------------------------------------------------------------------

    /// <summary>Repaints hover / checked / enabled without rebuilding the view.</summary>
    public void Refresh()
    {
        var enabled = this.item.IsEnabled && this.group?.IsEnabled != false;
        this.Opacity = enabled ? 1d : 0.38d;
        this.InputTransparent = !enabled;

        var isChecked = this.item is RibbonToggleButton { IsChecked: true };

        SetFill(
            this.face,
            isChecked
                ? ShinyThemeKeys.Color.SecondaryContainer
                : this.faceHovered && enabled
                    ? ShinyThemeKeys.Color.SurfaceContainerHighest
                    : null
        );

        // A ring as well as the fill. SecondaryContainer is a near-neighbour of the bar's own surface
        // in a dark scheme - it is there, but it does not read as "this one is on" at a glance, which
        // for a tool palette is the whole job of the checked state.
        if (isChecked)
        {
            this.face.StrokeThickness = 1;
            this.face.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Color.Secondary);
        }
        else
        {
            this.face.RemoveDynamicResource(Border.StrokeProperty);
            this.face.StrokeThickness = 0;
            this.face.Stroke = null;
        }

        if (this.chevronCell is not null)
        {
            SetFill(
                this.chevronCell,
                this.chevronHovered && enabled ? ShinyThemeKeys.Color.SurfaceContainerHighest : null
            );
        }
    }


    static void SetFill(Border border, string? token)
    {
        if (token is null)
        {
            border.RemoveDynamicResource(VisualElement.BackgroundColorProperty);
            border.BackgroundColor = Colors.Transparent;
        }
        else
        {
            border.ClearValue(VisualElement.BackgroundColorProperty);
            border.SetDynamicResource(VisualElement.BackgroundColorProperty, token);
        }
    }


    // ---------------------------------------------------------------------------------------------
    // Invocation
    // ---------------------------------------------------------------------------------------------

    void InvokeFace()
    {
        // A menu button's panel hangs off this button, so opening it stays here where the anchor is.
        // Everything else goes through the ribbon, so a press and a programmatic Invoke are one path.
        if (this.item is RibbonMenuButton and not RibbonSplitButton)
        {
            this.OpenMenu(this.face);
            return;
        }

        this.owner.Invoke(this.item);
        this.Refresh();
    }


    void OpenMenu(View anchor)
    {
        if (this.item is not RibbonMenuButton menu || !this.item.IsEnabled)
            return;

        this.owner.OpenMenu(menu, anchor, () =>
        {
            this.Refresh();
            this.owner.NotifyItemInvoked(this.item, this.group, this.tab);
        });
    }
}

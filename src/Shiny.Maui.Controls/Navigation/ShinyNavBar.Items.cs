using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

public partial class ShinyNavBar
{
    // ---------------------------------------------------------------------------------------------
    // Item collections
    // ---------------------------------------------------------------------------------------------

    void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => StyleGuard.WhenReady<ShinyNavBar>(this, bar => bar.RebuildItems());


    /// <summary>
    /// Re-subscribes to every item currently in either collection. An item's own properties are
    /// bindable, so a command that flips <c>IsEnabled</c> or a count that drives <c>Badge</c> has to
    /// reach the bar without the collection itself changing.
    /// </summary>
    void ResubscribeItems()
    {
        foreach (var item in this.subscribed)
            item.PropertyChanged -= this.OnItemPropertyChanged;

        this.subscribed.Clear();

        foreach (var item in this.leftItems.Concat(this.rightItems))
        {
            item.PropertyChanged += this.OnItemPropertyChanged;
            this.subscribed.Add(item);
        }
    }


    void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => StyleGuard.WhenReady<ShinyNavBar>(this, bar => bar.RebuildItems());


    void RebuildItems()
    {
        this.ResubscribeItems();
        this.ApplyItemMetrics();

        this.leadingHost.Children.Clear();
        this.trailingHost.Children.Clear();
        this.backButton = null;

        this.ApplyBackButton();

        this.Fill(this.leadingHost, this.leftItems, this.leftOverflow, NavBarSide.Left);
        this.Fill(this.trailingHost, this.rightItems, this.rightOverflow, NavBarSide.Right);

        this.UpdateCenterInset();
        this.RefreshMenuContent();
    }


    void Fill(Layout host, IEnumerable<ToolbarItem> source, List<ToolbarItem> overflow, NavBarSide side)
    {
        overflow.Clear();

        // Priority is ToolbarItem's own ordering knob and means the same here. OrderBy is stable, so
        // items that share a priority keep the order they were declared in.
        var visible = source
            .Where(IsItemVisible)
            .OrderBy(i => i.Priority)
            .ToList();

        var primary = visible.Where(i => i.Order != ToolbarItemOrder.Secondary).ToList();
        var secondary = visible.Where(i => i.Order == ToolbarItemOrder.Secondary).ToList();

        var max = this.MaxVisibleItems;
        var onBar = max > 0 ? primary.Take(max).ToList() : primary;

        overflow.AddRange(primary.Skip(onBar.Count));
        overflow.AddRange(secondary);

        foreach (var item in onBar)
        {
            if (item is NavBarItem { IsSeparator: true })
                continue;

            host.Children.Add(this.BuildItemView(item));
        }

        if (overflow.Count > 0)
            host.Children.Add(this.BuildOverflowButton(side, overflow));
    }


    static bool IsItemVisible(ToolbarItem item) => item is not NavBarItem nav || nav.IsVisible;


    /// <summary>
    /// The badge an item shows, from either place it can be said. A <see cref="NavBarItem"/>'s own
    /// <see cref="NavBarItem.Badge"/> wins while it is non-null; everything else — a page's plain
    /// <see cref="ToolbarItem"/>s included — reads the attached <see cref="ShinyNav.BadgeProperty"/>.
    /// </summary>
    internal static string? ResolveBadge(ToolbarItem item)
        => item is NavBarItem { Badge: { } badge } ? badge : ShinyNav.GetBadge(item);


    /// <summary>The badge's fill, resolved the same way. Null falls through to the theme's error colour.</summary>
    internal static Color? ResolveBadgeColor(ToolbarItem item)
        => (item as NavBarItem)?.BadgeColor ?? ShinyNav.GetBadgeColor(item);


    /// <summary>A plain toolbar item seen through the icon contract; a nav item already is one.</summary>
    static ITabIcon IconSpec(ToolbarItem item)
        => item as NavBarItem ?? (ITabIcon)new PlainIconSpec(item.IconImageSource);


    Color? ResolvedIconColor() => this.IconColor ?? this.BarTextColor;


    View BuildItemView(ToolbarItem item)
    {
        var spec = IconSpec(item);
        var display = item is NavBarItem nav ? nav.Display : NavBarItemDisplay.Auto;
        var hasIcon = !TabIcons.IsEmpty(spec);

        var showIcon = display is NavBarItemDisplay.Icon or NavBarItemDisplay.Auto or NavBarItemDisplay.IconAndText && hasIcon;
        var showText = display switch
        {
            NavBarItemDisplay.Text => true,
            NavBarItemDisplay.IconAndText => true,
            NavBarItemDisplay.Icon => false,

            // Auto: the text is the fallback for an item with no artwork, so a text-only toolbar item
            // still renders instead of turning into an invisible tap target.
            _ => !hasIcon
        };

        var content = new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };

        if (showIcon && TabIcons.Realize(spec, null, this.IconSize) is { } icon)
        {
            var tint = (item as NavBarItem)?.IconColor ?? this.ResolvedIconColor();
            TabIcons.Tint(icon, tint, ShinyThemeKeys.Color.OnSurface);
            content.Children.Add(icon);
        }

        if (showText && item.Text is { Length: > 0 } text)
        {
            var label = new Label
            {
                Text = text,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1
            }.WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);

            var tint = item.IsDestructive ? ShinyThemeKeys.Color.Error : ShinyThemeKeys.Color.OnSurface;
            ThemeProbe.Tint(label, Label.TextColorProperty, item.IsDestructive ? null : this.ResolvedIconColor(), tint);
            content.Children.Add(label);
        }

        View inner = content;

        if (ResolveBadge(item) is { } badgeText)
        {
            var badge = new BadgeView
            {
                IsDot = badgeText.Length == 0,
                Text = badgeText,
                BadgeColor = ResolveBadgeColor(item),
                Content = content
            };
            inner = badge;
        }

        var automationId = item.AutomationId;
        return this.WrapAsButton(inner, automationId, () => this.InvokeItem(item), item.IsEnabled);
    }


    View BuildOverflowButton(NavBarSide side, IReadOnlyList<ToolbarItem> overflow)
    {
        View glyph;

        if (this.OverflowIcon is { Length: > 0 } name)
        {
            glyph = TabIcons.Realize(new PlainMotionSpec(name), null, this.IconSize) ?? new Grid();
            TabIcons.Tint(glyph, this.ResolvedIconColor(), ShinyThemeKeys.Color.OnSurface);
        }
        else
        {
            // Three dots rather than a path: the vertical ellipsis is nothing but circles, and drawing
            // it as SVG arcs is a great deal of decimal precision to get wrong for no gain.
            var dots = new VerticalStackLayout
            {
                Spacing = 3,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };

            for (var i = 0; i < 3; i++)
            {
                var dot = new BoxView
                {
                    WidthRequest = 4,
                    HeightRequest = 4,
                    CornerRadius = 2,
                    HorizontalOptions = LayoutOptions.Center
                };
                ThemeProbe.Tint(dot, BoxView.ColorProperty, this.ResolvedIconColor(), ShinyThemeKeys.Color.OnSurface);
                dots.Children.Add(dot);
            }

            glyph = dots;
        }

        // A badged item that collapsed into the menu would otherwise take its badge off the bar
        // entirely - the one place the count is worth seeing. A dot rather than a number: the button
        // stands for several items, and summing counts that may not all be numbers invents a total
        // nobody asked for.
        if (overflow.Any(i => ResolveBadge(i) is not null))
        {
            glyph = new BadgeView
            {
                IsDot = true,
                BadgeColor = overflow.Select(ResolveBadgeColor).FirstOrDefault(c => c is not null),
                Content = glyph
            };
        }

        return this.WrapAsButton(glyph, side == NavBarSide.Left ? "nav-overflow-left" : "nav-overflow-right", () => this.OpenMenu(side));
    }


    /// <summary>Names a built-in motion icon and nothing else — the overflow glyph's spec.</summary>
    sealed class PlainMotionSpec(string name) : ITabIcon
    {
        public string? Icon => name;
        public Shiny.Controls.MotionIcons.MotionIconDefinition? IconSource => null;
        public string? IconPathData => null;
        public ImageSource? IconImage => null;
        public Shiny.Controls.MotionIcons.MotionPreset Motion => Shiny.Controls.MotionIcons.MotionPreset.Default;
    }


    /// <summary>
    /// The tap target every bar affordance shares — a rounded, transparent <see cref="Border"/> big
    /// enough to hit.
    /// </summary>
    /// <remarks>
    /// A <see cref="Border"/> rather than a <see cref="Button"/> on purpose: a Button ignores gesture
    /// recognizers on every platform, its native padding fights a 40pt square, and its text styling
    /// is a second thing to keep in step with the theme.
    /// </remarks>
    View WrapAsButton(View content, string? automationId, Action onTapped, bool enabled = true)
    {
        var host = new Border
        {
            Padding = new Thickness(8, 4),
            StrokeThickness = 0,
            Stroke = null,
            BackgroundColor = Colors.Transparent,
            MinimumWidthRequest = 40,
            MinimumHeightRequest = 40,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerFullRadius),
            Opacity = enabled ? 1 : 0.4,
            Content = content
        };

        if (automationId is { Length: > 0 })
            host.AutomationId = automationId;

        if (!enabled)
            return host;

        // The action goes on the recognizer's Command rather than its Tapped event. Both fire on a
        // tap, but only the Command can be invoked without a platform gesture behind it - which is
        // what lets the bar's behaviour be tested rather than only its layout.
        var tap = new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                TabIcons.Play(FirstIcon(content));
                onTapped();
            })
        };
        host.GestureRecognizers.Add(tap);
        return host;
    }


    /// <summary>The motion icon inside a built affordance, so a tap can play it.</summary>
    static View? FirstIcon(View content) => content switch
    {
        MotionIconView motion => motion,
        BadgeView badge when badge.Content is View inner => FirstIcon(inner),
        Layout layout => layout.Children.OfType<View>().Select(FirstIcon).FirstOrDefault(v => v is not null),
        _ => null
    };


    void InvokeItem(ToolbarItem item)
    {
        if (!item.IsEnabled)
            return;

        // Activate is what a native toolbar calls: it runs Command and raises Clicked, so an item
        // behaves identically whether it was drawn here or by the platform.
        ((IMenuItemController)item).Activate();
        this.ItemInvoked?.Invoke(this, new NavBarItemEventArgs(item));
    }


    // ---------------------------------------------------------------------------------------------
    // Overflow menu
    // ---------------------------------------------------------------------------------------------

    Layout? ResolveMenuLayer()
        => PageOverlay.GetOrCreateLayer<PageOverlay.NavMenuLayer>(this, PageOverlay.Layers.NavMenu);


    async Task OpenMenuAsync()
    {
        if (this.menuCard is not null)
            await this.CloseMenuAsync().ConfigureAwait(true);

        var layer = this.ResolveMenuLayer();
        this.menuLayer = layer;
        if (layer is null)
            return;

        this.menuBackdrop = new BoxView { Opacity = 0 };
        this.menuBackdrop.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.Scrim);
        this.menuBackdrop.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(this.CloseMenu)
        });

        this.menuCard = this.BuildMenuCard();
        this.menuCard.Opacity = 0;
        this.menuCard.TranslationY = -12;
        this.menuCard.Scale = 0.96;

        layer.Children.Add(this.menuBackdrop);
        layer.Children.Add(this.menuCard);

        var duration = this.AnimationDuration;
        if (this.Handler is null || duration == 0)
        {
            // No handler means no animation manager - a headless host, or a bar not yet rendered.
            // Land on the final frame rather than awaiting an animation nothing drives.
            this.menuBackdrop.Opacity = 0.35;
            this.menuCard.Opacity = 1;
            this.menuCard.TranslationY = 0;
            this.menuCard.Scale = 1;
            return;
        }

        try
        {
            await Task.WhenAll(
                this.menuBackdrop.FadeToAsync(0.35, duration, Easing.CubicOut),
                this.menuCard.FadeToAsync(1, duration, Easing.CubicOut),
                this.menuCard.TranslateToAsync(0, 0, duration, Easing.CubicOut),
                this.menuCard.ScaleToAsync(1, duration, Easing.CubicOut)
            ).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Torn down mid-flight (page popped, handler disconnected). Either it is gone or about to be.
        }
    }


    async Task CloseMenuAsync()
    {
        var card = this.menuCard;
        var backdrop = this.menuBackdrop;
        if (card is null)
            return;

        this.menuCard = null;
        this.menuBackdrop = null;

        var duration = this.AnimationDuration;
        if (this.Handler is not null && duration > 0)
        {
            try
            {
                await Task.WhenAll(
                    backdrop?.FadeToAsync(0, duration, Easing.CubicIn) ?? Task.FromResult(true),
                    card.FadeToAsync(0, duration, Easing.CubicIn),
                    card.ScaleToAsync(0.96, duration, Easing.CubicIn)
                ).ConfigureAwait(true);
            }
            catch (Exception)
            {
            }
        }

        // Unparent whatever was lent to the card before dropping it, or the next open finds a view
        // that already has a parent and MAUI throws.
        card.Content = null;

        this.menuLayer?.Children.Remove(card);
        if (backdrop is not null)
            this.menuLayer?.Children.Remove(backdrop);
    }


    void RefreshMenuContent()
    {
        if (this.menuCard is null)
            return;

        this.menuCard.Content = null;
        this.menuCard.Content = this.BuildMenuBody();
    }


    Border BuildMenuCard()
    {
        var card = new Border
        {
            StrokeThickness = 0,
            Stroke = null,
            Padding = new Thickness(0, 6),
            MinimumWidthRequest = 200,
            HorizontalOptions = this.menuSide == NavBarSide.Left ? LayoutOptions.Start : LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(8, this.BarHeight + 4, 8, 0),
            Background = ThemeTokens.TokenBrush(ShinyThemeKeys.Color.SurfaceContainerHigh),
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerLargeRadius),
            Content = this.BuildMenuBody()
        };
        card.WithElevation(ShinyThemeKeys.Elevation.Level3);
        return card;
    }


    View BuildMenuBody()
    {
        var items = this.menuSide == NavBarSide.Left ? this.leftOverflow : this.rightOverflow;

        if (this.MenuTemplate is { } template)
        {
            var view = template.CreateContent() as View ?? new VerticalStackLayout();
            view.BindingContext = items;
            return view;
        }

        var stack = new VerticalStackLayout { Spacing = 0 };
        foreach (var item in items)
            stack.Children.Add(this.BuildMenuRow(item));

        return stack;
    }


    View BuildMenuRow(ToolbarItem item)
    {
        if (item is NavBarItem { IsSeparator: true })
        {
            var line = new BoxView { HeightRequest = 1, Margin = new Thickness(12, 6) };
            line.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);
            return line;
        }

        var tintToken = item.IsDestructive ? ShinyThemeKeys.Color.Error : ShinyThemeKeys.Color.OnSurface;

        var label = new Label
        {
            Text = item.Text,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        label.SetDynamicResource(Label.TextColorProperty, tintToken);

        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 14,
            Padding = new Thickness(18, 12),
            BackgroundColor = Colors.Transparent,
            Opacity = item.IsEnabled ? 1 : 0.4
        };

        if (TabIcons.Realize(IconSpec(item), null, 22) is { } icon)
        {
            TabIcons.Tint(icon, null, tintToken);
            Grid.SetColumn(icon, 0);
            row.Children.Add(icon);
        }

        Grid.SetColumn(label, 1);
        row.Children.Add(label);

        // The same pill the bar draws, in its own column: a badge that shared the title's column
        // would sit over the end of a long row's text, and one drawn as plain grey text would drop
        // the item's BadgeColor on the way into the menu.
        if (ResolveBadge(item) is { } badgeText)
        {
            var badge = new BadgeView
            {
                IsDot = badgeText.Length == 0,
                Text = badgeText,
                BadgeColor = ResolveBadgeColor(item),

                // The corner nudge is for a badge overlapping an icon; this one sits beside the row.
                OffsetX = 0,
                OffsetY = 0,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center
            };
            Grid.SetColumn(badge, 2);
            row.Children.Add(badge);
        }

        var tap = new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                if (!item.IsEnabled)
                    return;

                this.InvokeItem(item);
                this.CloseMenu();
            })
        };
        row.GestureRecognizers.Add(tap);

        return row;
    }
}

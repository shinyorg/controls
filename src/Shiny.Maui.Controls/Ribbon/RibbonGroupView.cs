using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Ribbons;

/// <summary>
/// The box drawn for one <see cref="RibbonGroup"/> — its items, its caption, and the single button it
/// collapses to when the ribbon runs out of room.
/// </summary>
/// <remarks>
/// Both forms are built up front and swapped with <c>IsVisible</c> rather than added and removed. That
/// is partly speed — a width change during a window drag would otherwise rebuild every group on every
/// frame — and partly the macOS AppKit head, where a child added after the page has been laid out
/// never gets a native view and paints nothing.
/// </remarks>
class RibbonGroupView : Grid
{
    readonly Ribbon owner;
    readonly RibbonTab tab;
    readonly RibbonGroup group;
    readonly bool simplified;
    readonly List<RibbonItemView> itemViews = new();
    readonly List<View> hostedContent = new();

    readonly View openView;
    readonly View collapsedView;

    bool collapsed;


    public RibbonGroupView(Ribbon owner, RibbonTab tab, RibbonGroup group, bool simplified)
    {
        this.owner = owner;
        this.tab = tab;
        this.group = group;
        this.simplified = simplified;

        this.openView = this.BuildOpen();
        this.collapsedView = this.BuildCollapsed();
        this.collapsedView.IsVisible = false;

        this.Add(this.openView);
        this.Add(this.collapsedView);

        if (!string.IsNullOrWhiteSpace(group.AutomationId))
            this.AutomationId = group.AutomationId;

        this.Refresh();
    }


    public RibbonGroup Group => this.group;

    /// <summary>Whether the group is currently showing as a single button.</summary>
    public bool IsCollapsed
    {
        get => this.collapsed;
        set
        {
            if (this.collapsed == value)
                return;

            this.collapsed = value;
            this.openView.IsVisible = !value;
            this.collapsedView.IsVisible = value;
        }
    }

    /// <summary>Whether the ribbon is allowed to collapse this one when space runs short.</summary>
    public bool CanCollapse => this.group.CanCollapse && this.owner.AllowGroupCollapse && !this.simplified;

    /// <summary>
    /// How wide the open form wants to be. Zero when it cannot be measured yet — before a handler
    /// exists, or in a headless host — which the ribbon reads as "do not collapse anything".
    /// </summary>
    public double DesiredOpenWidth
    {
        get
        {
            var measured = ((IView)this.openView).Measure(double.PositiveInfinity, double.PositiveInfinity);
            return double.IsFinite(measured.Width) ? measured.Width : 0d;
        }
    }


    /// <summary>Repaints every item's hover / checked / enabled state without rebuilding.</summary>
    public void Refresh()
    {
        foreach (var view in this.itemViews)
            view.Refresh();

        this.Opacity = this.group.IsEnabled ? 1d : 0.38d;
    }


    /// <summary>
    /// Unparents the views a <see cref="RibbonContentItem"/> lent the group, so the next rebuild can
    /// re-place them. MAUI throws when a view that still has a parent is added somewhere else, and the
    /// whole point of hosted content is that it survives a rebuild with its state intact.
    /// </summary>
    public void Release()
    {
        foreach (var content in this.hostedContent)
        {
            if (content.Parent is Layout layout)
                layout.Children.Remove(content);
            else if (content.Parent is ContentView holder)
                holder.Content = null;
            else if (content.Parent is Border border)
                border.Content = null;
        }

        this.hostedContent.Clear();
    }


    // ---------------------------------------------------------------------------------------------
    // The open form
    // ---------------------------------------------------------------------------------------------

    View BuildOpen()
    {
        var items = this.BuildItemsHost();

        if (this.simplified)
            return items;

        var stack = new VerticalStackLayout { Spacing = 2 };
        stack.Children.Add(items);

        if (this.owner.ShowGroupTitles)
            stack.Children.Add(this.BuildFooter());

        return stack;
    }


    /// <summary>
    /// The lines.
    /// </summary>
    /// <remarks>
    /// Nothing in the model declares a column. In the default column flow a large item takes one to
    /// itself, small items fill the current one up to <see cref="Ribbon.SmallItemRows"/> deep, and a
    /// separator, a <see cref="RibbonLineBreak"/> or a large item ends it. In
    /// a group built out of <see cref="RibbonRow"/>s the same rules run the other way round — which is
    /// the whole of a ribbon's layout language, and why reordering a group's items re-flows it without
    /// anything else being touched.
    /// </remarks>
    View BuildItemsHost()
    {
        // The rows are the signal; there is no mode to set. A group is one or the other, and a group
        // that mixes them would have loose items with no answer to which row they are on.
        var rows = this.group.VisibleItems.OfType<RibbonRow>().ToList();

        return rows.Count > 0 && !this.simplified
            ? this.BuildRowsHost(rows)
            : this.BuildColumnsHost();
    }


    View BuildColumnsHost()
    {
        var host = new HorizontalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center
        };

        var rows = this.simplified ? 1 : Math.Max(1, this.owner.SmallItemRows);
        VerticalStackLayout? column = null;

        void Flush() => column = null;

        VerticalStackLayout Column()
        {
            if (column is { } existing && existing.Children.Count < rows)
                return existing;

            var created = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
            host.Children.Add(created);
            column = created;
            return created;
        }

        // Pins the row rather than letting the item size it. Without this each group sizes its rows to
        // its own tallest item, so a group with a picker in it puts its buttons on a different line
        // from the group beside it - see Ribbon.SmallItemRowHeight.
        void AddRow(View child)
        {
            if (this.owner.SmallItemRowHeight > 0)
            {
                child.HeightRequest = this.owner.SmallItemRowHeight;
                child.VerticalOptions = LayoutOptions.Center;
            }

            Column().Children.Add(child);
        }

        foreach (var item in this.group.VisibleItems)
        {
            var size = this.simplified ? RibbonItemSize.Small : item.Size;

            switch (item)
            {
                case RibbonRow:
                    // A group that mixes rows and loose items draws the rows (BuildItemsHost picks
                    // that path); a stray row reaching the column flow has nothing to contribute.
                    continue;

                case RibbonSeparator:
                    Flush();
                    host.Children.Add(this.BuildRule());
                    continue;

                case RibbonContentItem { Content: { } content }:
                    this.Adopt(content);

                    if (size == RibbonItemSize.Small)
                    {
                        AddRow(content);
                    }
                    else
                    {
                        Flush();
                        host.Children.Add(content);
                    }
                    continue;

                case RibbonContentItem:
                    // Declared but empty. Nothing to draw and nothing to warn about at runtime.
                    continue;
            }

            var view = this.BuildItemView(item, size);

            if (size == RibbonItemSize.Large)
            {
                Flush();
                host.Children.Add(view);
                Flush();
            }
            else
            {
                AddRow(view);
            }
        }

        return host;
    }


    /// <summary>
    /// Rows: each <see cref="RibbonRow"/> is a line of its own, stacked top to bottom.
    /// </summary>
    /// <remarks>
    /// The rows are pinned to <see cref="Ribbon.SmallItemRowHeight"/> rather than sized by their
    /// contents, which is what keeps one group's rows on the same baselines as the group beside it —
    /// the same reason the column flow pins each item.
    /// </remarks>
    View BuildRowsHost(IReadOnlyList<RibbonRow> rows)
    {
        var host = new VerticalStackLayout
        {
            Spacing = 1,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start
        };

        foreach (var row in rows)
        {
            var line = new HorizontalStackLayout
            {
                Spacing = 2,
                VerticalOptions = LayoutOptions.Center
            };

            if (this.owner.SmallItemRowHeight > 0)
                line.HeightRequest = this.owner.SmallItemRowHeight;

            foreach (var item in row.VisibleItems)
            {
                switch (item)
                {
                    case RibbonSeparator:
                        line.Children.Add(this.BuildRule());
                        continue;

                    case RibbonContentItem { Content: { } content }:
                        this.Adopt(content);
                        line.Children.Add(content);
                        continue;

                    case RibbonContentItem:
                    case RibbonRow:
                        // A row inside a row is nothing; a content item with no content is nothing to
                        // draw and nothing to warn about at runtime.
                        continue;
                }

                // Everything on a row is drawn small whatever it asked for: a large item is icon-over-
                // label and two rows tall by construction, so one on a row would be the only thing on
                // the bar breaking the row height every other group is lined up to.
                line.Children.Add(this.BuildItemView(item, RibbonItemSize.Small));
            }

            host.Children.Add(line);
        }

        return host;
    }


    /// <summary>
    /// Takes a hosted view off whatever it was parented to last time. MAUI throws when a view that
    /// still has a parent is added somewhere else, and the whole point of hosted content is that it
    /// survives a rebuild with its state intact.
    /// </summary>
    void Adopt(View content)
    {
        if (content.Parent is Layout parent)
            parent.Children.Remove(content);

        this.hostedContent.Add(content);
    }


    RibbonItemView BuildItemView(RibbonItem item, RibbonItemSize size)
    {
        var view = new RibbonItemView(
            this.owner,
            this.tab,
            this.group,
            item,
            size,
            showLabel: !this.simplified || item.Size == RibbonItemSize.Small
        );

        this.itemViews.Add(view);
        return view;
    }


    View BuildRule()
    {
        var rule = new BoxView
        {
            WidthRequest = 1,
            VerticalOptions = LayoutOptions.Fill,
            Margin = new Thickness(4, 4)
        };

        // Color, not BackgroundColor: on the macOS AppKit head a BoxView paints from Color alone and a
        // background-only rule comes out invisible.
        rule.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);
        return rule;
    }


    View BuildFooter()
    {
        var title = new Label
        {
            Text = this.group.Title,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalOptions = LayoutOptions.Center
        }.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);
        title.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        if (!this.group.ShowDialogLauncher)
            return title;

        var launcher = this.BuildDialogLauncher();

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        grid.Add(title, 0);
        grid.Add(launcher, 1);
        return grid;
    }


    /// <summary>
    /// The small corner arrow — the convention for "there is more of this than fits here".
    /// </summary>
    View BuildDialogLauncher()
    {
        var glyph = new Polyline
        {
            // An arrow into the corner: down the left, along the bottom, then the diagonal back out.
            Points = new PointCollection { new(0, 0), new(0, 7), new(7, 7), new(0, 0) },
            Stroke = this.owner.ForegroundBrush,
            StrokeThickness = 1.2,
            StrokeLineJoin = PenLineJoin.Round,
            WidthRequest = 8,
            HeightRequest = 8,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        var border = new Border
        {
            Content = glyph,
            Padding = new Thickness(4, 2),
            StrokeThickness = 0,
            Stroke = null,
            BackgroundColor = Colors.Transparent,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerSmallRadius),
            GestureRecognizers =
            {
                new TapGestureRecognizer { Command = new Command(this.group.InvokeDialogLauncher) }
            }
        };

        var hint = this.group.DialogLauncherTooltip ?? $"{this.group.Title} settings";
        SemanticProperties.SetDescription(border, hint);

        if (this.owner.ShowTooltips)
        {
            TooltipProperties.SetText(border, hint);
            TooltipProperties.SetTrigger(border, TooltipTrigger.Hover);
            TooltipProperties.SetPlacement(border, TooltipPlacement.Top);
        }

        return border;
    }


    // ---------------------------------------------------------------------------------------------
    // The collapsed form
    // ---------------------------------------------------------------------------------------------

    View BuildCollapsed()
    {
        var stack = new VerticalStackLayout
        {
            Spacing = 2,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        // Falls back to the first item's icon, so a group only needs CollapsedIcon when the first
        // command is a poor stand-in for the whole set.
        var icon = this.group.CollapsedIcon
            ?? this.group.VisibleItems.Select(x => x.Icon).FirstOrDefault(x => x is not null);

        if (icon is not null)
        {
            stack.Children.Add(new Image
            {
                Source = icon,
                WidthRequest = RibbonItemView.LargeIconSize,
                HeightRequest = RibbonItemView.LargeIconSize,
                Aspect = Aspect.AspectFit,
                InputTransparent = true
            });
        }

        var label = new Label
        {
            Text = this.group.Title,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaximumWidthRequest = 76
        }.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);
        label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
        stack.Children.Add(label);

        stack.Children.Add(new Polyline
        {
            Points = new PointCollection { new(0, 0), new(4, 4), new(8, 0) },
            Stroke = this.owner.ForegroundBrush,
            StrokeThickness = 1.4,
            StrokeLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            WidthRequest = 8,
            HeightRequest = 5,
            HorizontalOptions = LayoutOptions.Center
        });

        var border = new Border
        {
            Content = stack,
            Padding = new Thickness(8, 4),
            StrokeThickness = 0,
            Stroke = null,
            BackgroundColor = Colors.Transparent,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerSmallRadius)
        };

        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => this.owner.OpenGroupPopup(this.tab, this.group, border))
        });

        SemanticProperties.SetDescription(border, this.group.Title);
        return border;
    }
}

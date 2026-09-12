using System.Text;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Ribbons;

public partial class Ribbon
{
    string? shapeSignature;


    // ---------------------------------------------------------------------------------------------
    // Rebuild
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Rebuilds the strip and every tab's body.
    /// </summary>
    /// <remarks>
    /// Every tab is built, not just the selected one, and the unselected ones are simply hidden. That
    /// is what makes a tab switch free, keeps a hosted picker's state across one, and gets the ribbon
    /// drawn on the macOS AppKit head at all.
    /// </remarks>
    void Rebuild()
    {
        if (this.root is null)
            return;  // A property changed from the constructor's own initialisers.

        this.suppress++;
        try
        {
            this.ReleaseHostedContent();

            this.tabStack.Children.Clear();
            this.quickAccessStack.Children.Clear();
            this.bodyHost.Children.Clear();
            this.tabButtons.Clear();
            this.panels.Clear();

            this.PushBindingContext();
            this.ApplyChrome();
            this.BuildApplicationButton();
            this.BuildQuickAccess();

            var simplified = this.DisplayMode == RibbonDisplayMode.Simplified;

            foreach (var tab in this.tabs)
            {
                this.tabStack.Children.Add(this.BuildTabButton(tab));

                var groupHost = new HorizontalStackLayout { Spacing = 4 };

                // The fades depend on the content being wider than the scroller, and the content is
                // not measured when the ribbon's own SizeChanged fires - a group's width is not known
                // until its handler has laid it out, which is a frame or two later.
                groupHost.SizeChanged += (_, _) => this.UpdateScrollHints();
                var groupViews = new List<RibbonGroupView>();

                for (var i = 0; i < tab.VisibleGroups.Count; i++)
                {
                    var group = tab.VisibleGroups[i];
                    var view = new RibbonGroupView(this, tab, group, simplified);
                    group.DialogLauncherClicked -= this.OnGroupDialogLauncher;
                    group.DialogLauncherClicked += this.OnGroupDialogLauncher;

                    groupViews.Add(view);
                    groupHost.Children.Add(view);

                    if (i < tab.VisibleGroups.Count - 1)
                        groupHost.Children.Add(this.BuildGroupDivider());
                }

                var panel = new ScrollView
                {
                    Orientation = ScrollOrientation.Horizontal,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
                    Content = groupHost,
                    IsVisible = false
                };

                panel.Scrolled += this.OnPanelScrolled;

                this.bodyHost.Children.Add(panel);
                this.panels.Add((tab, panel, groupViews));
            }

            // Last, so they overlay every panel. Children.Clear above dropped the previous pair.
            this.bodyHost.Children.Add(this.startFade);
            this.bodyHost.Children.Add(this.endFade);

            this.shapeSignature = this.Signature();
        }
        finally
        {
            this.suppress--;
        }

        this.ApplySelection(this.SelectedIndex, RibbonTabChangeReason.Programmatic);
        this.lastRelayoutWidth = -1;
        this.RelayoutGroups();
    }


    void ReleaseHostedContent()
    {
        foreach (var (_, _, groups) in this.panels)
        {
            foreach (var group in groups)
                group.Release();
        }
    }


    void OnGroupDialogLauncher(object? sender, EventArgs e)
    {
        if (sender is RibbonGroup group)
            this.NotifyGroupDialogLauncher(group);
    }


    /// <summary>Paints the frames from the theme, or from whatever the consumer set instead.</summary>
    void ApplyChrome()
    {
        ThemeProbe.Tint(this.foregroundProbe, BoxView.ColorProperty, null, ShinyThemeKeys.Color.OnSurfaceVariant);
        ThemeProbe.Tint(this.outlineProbe, BoxView.ColorProperty, null, ShinyThemeKeys.Color.OutlineVariant);
        ThemeProbe.Tint(this.accentProbe, BoxView.ColorProperty, this.AccentColor, ShinyThemeKeys.Color.Primary);

        ThemeProbe.Tint(
            this.headerFrame,
            VisualElement.BackgroundColorProperty,
            this.HeaderBackgroundColor,
            ShinyThemeKeys.Color.SurfaceContainer
        );

        // The chevron sits on the band, so it takes the band's ink rather than the body's foreground -
        // otherwise it is a dark glyph on a saturated header, which is the same unreadable pairing the
        // tab labels had.
        this.collapseGlyph.Stroke = new SolidColorBrush(this.HeaderInk);

        // The tab labels carry resolved colours rather than dynamic resources - see ApplyTabInk - so
        // they have to be re-inked whenever the chrome changes, which is where a theme flip lands.
        foreach (var (tab, _, _, label) in this.tabButtons)
            this.ApplyTabInk(label, ReferenceEquals(tab, this.SelectedTab));

        ThemeProbe.Tint(
            this.bodyFrame,
            VisualElement.BackgroundColorProperty,
            this.BodyBackgroundColor,
            ShinyThemeKeys.Color.SurfaceContainerLow
        );
    }


    // ---------------------------------------------------------------------------------------------
    // Header
    // ---------------------------------------------------------------------------------------------

    void BuildApplicationButton()
    {
        if (string.IsNullOrWhiteSpace(this.ApplicationButtonText))
        {
            this.appButtonHost.Content = null;
            this.appButtonHost.IsVisible = false;
            return;
        }

        var label = new Label
        {
            Text = this.ApplicationButtonText,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center
        }.WithFontSize(ShinyThemeKeys.Type.LabelLargeSize);
        label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);

        var border = new Border
        {
            Content = label,
            Padding = new Thickness(14, 6),
            StrokeThickness = 0,
            Stroke = null,
            Background = this.AccentBrush,
            Margin = new Thickness(0, 4),
            AutomationId = "RibbonApplicationButton",
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerSmallRadius),
            GestureRecognizers =
            {
                new TapGestureRecognizer { Command = new Command(this.InvokeApplicationButton) }
            }
        };

        this.appButtonHost.Content = border;
        this.appButtonHost.IsVisible = true;
    }


    /// <summary>Presses the application button. The seam a test invokes through.</summary>
    public void InvokeApplicationButton()
    {
        if (this.ApplicationButtonCommand?.CanExecute(null) == true)
            this.ApplicationButtonCommand.Execute(null);

        this.ApplicationButtonClicked?.Invoke(this, EventArgs.Empty);
    }


    void BuildQuickAccess()
    {
        this.quickAccessStack.IsVisible = this.ShowQuickAccess && this.quickAccess.Count > 0;
        if (!this.quickAccessStack.IsVisible)
            return;

        foreach (var item in this.quickAccess)
        {
            if (!item.IsVisible)
                continue;

            // Always small and never labelled: the quick access row is a strip of glyphs beside the
            // tabs, and one labelled item in it would set the height of the whole header.
            this.quickAccessStack.Children.Add(
                new RibbonItemView(this, null, null, item, RibbonItemSize.Small, showLabel: false)
            );
        }
    }


    View BuildTabButton(RibbonTab tab)
    {
        var label = new Label
        {
            Text = tab.Title,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center
        }.WithFontSize(ShinyThemeKeys.Type.LabelLargeSize);

        // Left to ApplySelection: which ink reads depends on whether the tab is sitting on the header
        // band or lifted out of it onto the body, and those are two different grounds.

        var underline = new BoxView { HeightRequest = 2, Margin = new Thickness(6, 0, 6, 0) };

        var stack = new VerticalStackLayout
        {
            Spacing = 2,
            Children = { label, underline }
        };

        var border = new Border
        {
            Content = stack,
            Padding = new Thickness(12, 8, 12, 0),
            StrokeThickness = 0,
            Stroke = null,
            BackgroundColor = Colors.Transparent,
            IsVisible = tab.IsVisible,
            StrokeShape = new RoundRectangle()
        };

        if (!string.IsNullOrWhiteSpace(tab.AutomationId))
            border.AutomationId = tab.AutomationId;

        SemanticProperties.SetDescription(border, tab.Title);

        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => this.OnTabTapped(tab))
        });

        // A second tap on the tab already showing collapses the ribbon, which is the gesture every
        // ribbon has. A real double-tap recognizer would fight the single tap that selects the tab.
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) =>
        {
            if (!ReferenceEquals(this.SelectedTab, tab) && tab.IsSelectable)
            {
                border.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);

                // Hovering lifts the tab off the band onto a surface tint, so the ink has to come off
                // the band with it - otherwise pointing at a tab in a light theme makes its label
                // disappear, which is the one moment the user is looking straight at it.
                this.ApplyTabInk(label, onSurface: true);
            }
        };
        pointer.PointerExited += (_, _) =>
        {
            if (!ReferenceEquals(this.SelectedTab, tab))
            {
                border.RemoveDynamicResource(VisualElement.BackgroundColorProperty);
                border.BackgroundColor = Colors.Transparent;
                this.ApplyTabInk(label, onSurface: false);
            }
        };
        border.GestureRecognizers.Add(pointer);

        this.tabButtons.Add((tab, border, underline, label));

        // The probe has usually resolved long before a rebuild, so round this one now rather than
        // waiting for the next theme swap to do it.
        this.OnCornerRadiusChanged();

        return border;
    }


    void OnTabTapped(RibbonTab tab)
    {
        if (!tab.IsSelectable)
            return;

        if (ReferenceEquals(this.SelectedTab, tab))
        {
            if (this.DisplayMode == RibbonDisplayMode.Collapsed)
            {
                // Peeking: re-tapping the open tab puts it away again.
                this.peeking = !this.peeking;
                this.ApplyDisplayMode();
            }
            else if (this.AllowCollapse)
            {
                this.DisplayMode = RibbonDisplayMode.Collapsed;
            }
            return;
        }

        this.ApplySelection(this.tabs.IndexOf(tab), RibbonTabChangeReason.User);
    }


    View BuildGroupDivider()
    {
        var rule = new BoxView
        {
            WidthRequest = 1,
            VerticalOptions = LayoutOptions.Fill,
            Margin = new Thickness(2, 6)
        };

        // Color rather than BackgroundColor: a background-only BoxView is invisible on macOS AppKit.
        rule.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);
        return rule;
    }


    // ---------------------------------------------------------------------------------------------
    // Visual state
    // ---------------------------------------------------------------------------------------------

    void ApplyTabVisuals()
    {
        var selected = this.SelectedTab;

        foreach (var (tab, button, underline, label) in this.tabButtons)
        {
            var isSelected = ReferenceEquals(tab, selected);

            button.IsVisible = tab.IsVisible;
            button.Opacity = tab.IsEnabled ? 1d : 0.38d;

            if (isSelected)
            {
                button.ClearValue(VisualElement.BackgroundColorProperty);
                button.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerLow);

                // The selected tab is lifted out of the header onto the body's own surface, so it takes
                // the theme's ink whatever the band behind it is. Giving it the header's ink instead is
                // white text on a light grey tab in a light theme - present, and unreadable.
                this.ApplyTabInk(label, onSurface: true);
            }
            else
            {
                button.RemoveDynamicResource(VisualElement.BackgroundColorProperty);
                button.BackgroundColor = Colors.Transparent;

                // The rest sit on the band, so they take whatever reads on it.
                this.ApplyTabInk(label, onSurface: false);
            }

            // Color, not BackgroundColor - AppKit paints a BoxView from Color alone.
            if (!isSelected)
            {
                underline.RemoveDynamicResource(BoxView.ColorProperty);
                underline.Color = Colors.Transparent;
            }
            else if (tab.IsContextual)
            {
                // Tertiary, not the accent the permanent tabs use: a contextual tab is meant to read as
                // a different kind of thing, not as the selected one of the same kind.
                ThemeProbe.Tint(underline, BoxView.ColorProperty, tab.ContextColor, ShinyThemeKeys.Color.Tertiary);
            }
            else
            {
                ThemeProbe.Tint(underline, BoxView.ColorProperty, this.AccentColor, ShinyThemeKeys.Color.Primary);
            }
        }

        foreach (var (tab, panel, _) in this.panels)
            panel.IsVisible = ReferenceEquals(tab, selected);

        // Each tab's groups are a different width, so switching tabs changes what overflows.
        this.UpdateScrollHints();

        // The contextual band captions the tab set, so it only means anything while one is showing.
        var contextual = selected is { IsContextual: true } ? selected : null;
        this.contextBand.IsVisible = contextual is not null;
        this.contextLabel.Text = contextual?.ContextTitle;

        if (contextual?.ContextColor is { } color)
        {
            this.contextBand.RemoveDynamicResource(VisualElement.BackgroundColorProperty);
            this.contextBand.BackgroundColor = color.WithAlpha(0.22f);
        }
        else
        {
            this.contextBand.ClearValue(VisualElement.BackgroundColorProperty);
            this.contextBand.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.TertiaryContainer);
        }
    }


    /// <summary>Repaints every drawn button's hover / checked / enabled state without rebuilding.</summary>
    void RefreshStates()
    {
        this.ApplyTabVisuals();

        foreach (var (_, _, groups) in this.panels)
        {
            foreach (var group in groups)
                group.Refresh();
        }
    }


    // ---------------------------------------------------------------------------------------------
    // Group collapse
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Collapses groups on the showing tab until it fits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Lowest <see cref="RibbonGroup.Priority"/> first, rightmost breaking ties, which is the order a
    /// ribbon has always given up its groups in. Only the visible panel is measured — the hidden ones
    /// have no size, and they get their turn when they are selected.
    /// </para>
    /// <para>
    /// A width of zero means the ribbon has not been laid out yet (or is running headless), and the
    /// answer there is to leave every group open: the body scrolls horizontally, so nothing is ever
    /// unreachable if this never runs at all.
    /// </para>
    /// </remarks>
    void RelayoutGroups()
    {
        if (this.Width <= 0 || this.panels.Count == 0)
            return;

        // The width only matters to the pixel while it is changing; re-running for a sub-pixel drift
        // during a window drag would measure every group on every frame.
        if (Math.Abs(this.Width - this.lastRelayoutWidth) < 1)
            return;

        this.lastRelayoutWidth = this.Width;

        var current = this.panels.FirstOrDefault(x => ReferenceEquals(x.Tab, this.SelectedTab));
        if (current.Groups is not { Count: > 0 } groups)
            return;

        foreach (var group in groups)
            group.IsCollapsed = false;

        if (!this.AllowGroupCollapse)
            return;

        var available = this.Width - 24;   // the body's own padding, plus a little slack
        var widths = groups.ToDictionary(g => g, g => g.DesiredOpenWidth);

        // Nothing measured: no handler yet. Leave it open rather than collapsing on a guess.
        if (widths.Values.All(w => w <= 0))
            return;

        const double CollapsedWidth = 76;

        var order = groups
            .Select((g, index) => (Group: g, Index: index))
            .Where(x => x.Group.CanCollapse)
            .OrderBy(x => x.Group.Group.Priority)
            .ThenByDescending(x => x.Index)
            .Select(x => x.Group)
            .ToList();

        var total = widths.Values.Sum() + (groups.Count * 8);

        foreach (var group in order)
        {
            if (total <= available)
                break;

            group.IsCollapsed = true;
            total -= Math.Max(0, widths[group] - CollapsedWidth);
        }
    }


    // ---------------------------------------------------------------------------------------------
    // Shape
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether the bar's <em>structure</em> changed, as opposed to a state a drawn button can repaint.
    /// </summary>
    bool ShapeChanged()
    {
        var signature = this.Signature();
        if (signature == this.shapeSignature)
            return false;

        this.shapeSignature = signature;
        return true;
    }


    /// <summary>
    /// Everything a rebuild would draw differently — which tabs are on the strip, which groups they
    /// hold, which items are in them and how big. Deliberately excludes <c>IsChecked</c> and
    /// <c>IsEnabled</c>: those repaint in place, and folding them in here would rebuild the whole bar
    /// every time a toggle flipped.
    /// </summary>
    string Signature()
    {
        var sb = new StringBuilder();

        sb.Append(this.DisplayMode).Append('|')
            .Append(this.ShowGroupTitles).Append('|')
            .Append(this.SmallItemRows).Append('|');

        foreach (var tab in this.tabs)
        {
            sb.Append(tab.Title).Append('~').Append(tab.IsVisible).Append('~').Append(tab.ContextTitle).Append('[');

            foreach (var group in tab.Groups)
            {
                sb.Append(group.Title).Append('~').Append(group.IsVisible).Append('~')
                    .Append(group.ShowDialogLauncher).Append('(');

                foreach (var item in group.Items)
                {
                    sb.Append(item.GetType().Name).Append(':')
                        .Append(item.Text).Append(':')
                        .Append(item.Size).Append(':')
                        .Append(item.IsVisible).Append(',');
                }

                sb.Append(')');
            }

            sb.Append(']');
        }

        return sb.ToString();
    }
}

using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Tree.Internal;

internal class TreeNodeView : Grid
{
    /// <summary>The checkbox's id while the tree has none of its own - shipped before ids followed the tree's.</summary>
    internal const string CheckBoxAutomationId = "ShinyTreeNodeCheckBox";

    readonly TreeView owner;
    readonly Grid chevronHost;
    readonly Border? checkBox;
    readonly Label? checkGlyph;
    readonly ContentView contentHost;
    readonly Border background;
    readonly Grid dropIndicatorAbove;
    readonly Grid dropIndicatorBelow;
    readonly Grid dropIndicatorInto;

    public TreeNodeView(TreeView owner, TreeNode node)
    {
        this.owner = owner;
        Node = node;

        BindingContext = node.Item;
        Padding = 0;
        ColumnSpacing = 0;
        RowSpacing = 0;

        // Background sits behind content and handles selection highlight + tap.
        background = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = owner.RowBackgroundColor,
            Padding = owner.RowPadding,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        // Indent + chevron + content layout
        var inner = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto), // indent + guide lines
                new ColumnDefinition(GridLength.Auto), // chevron
                new ColumnDefinition(GridLength.Auto), // multi-select checkbox
                new ColumnDefinition(GridLength.Star)  // user content
            },
            ColumnSpacing = 4,
            VerticalOptions = LayoutOptions.Center
        };

        // Indent column
        var indent = BuildIndent();
        inner.Add(indent, 0, 0);

        // Chevron
        chevronHost = new Grid
        {
            WidthRequest = owner.ChevronSize + 6,
            HeightRequest = owner.ChevronSize + 6,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            BackgroundColor = Colors.Transparent
        };
        var chevronTap = new TapGestureRecognizer();
        chevronTap.Tapped += OnChevronTapped;
        chevronHost.GestureRecognizers.Add(chevronTap);
        inner.Add(chevronHost, 1, 0);

        // Multi-select checkbox. Drawn rather than a native CheckBox: the row tap owns
        // selection, and a native control would either swallow the touch (double-toggling
        // through the row gesture) or render greyed once made InputTransparent, which
        // Android implements by disabling the platform view.
        if (owner.SelectionMode == TreeSelectionMode.Multiple && owner.ShowSelectionCheckBoxes)
        {
            checkGlyph = new Label
            {
                Text = "✓",
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0)
            };
            // OnPrimary never changes with selection, so it is resolved live once rather than per refresh.
            checkGlyph.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);

            // The stroke is set in RefreshCheckBox, from a brush token: a colour token cannot reach
            // Border.Stroke (a Brush). Copying the colour out of Application.Current.Resources instead
            // left the box in the old palette after a light/dark flip, and never saw a palette scoped
            // over the tree at all.
            checkBox = new Border
            {
                WidthRequest = 20,
                HeightRequest = 20,
                Padding = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerExtraSmallRadius),
                VerticalOptions = LayoutOptions.Center,
                Opacity = owner.CanSelect(node.Item) ? 1.0 : 0.35,
                Content = checkGlyph
            }.WithStrokeThickness(ShinyThemeKeys.Border.Medium);
            inner.Add(checkBox, 2, 0);
        }

        // Content host
        contentHost = new ContentView
        {
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill
        };

        var template = owner.ItemTemplate;
        if (template is DataTemplateSelector selector)
            template = selector.SelectTemplate(node.Item, owner);

        if (template != null && template.CreateContent() is View userView)
        {
            userView.BindingContext = node.Item;
            contentHost.Content = userView;
        }
        else
        {
            contentHost.Content = new Label
            {
                Text = node.Item?.ToString() ?? string.Empty,
                VerticalTextAlignment = TextAlignment.Center
            };
        }
        inner.Add(contentHost, 3, 0);

        background.Content = inner;

        // Row tap = selection
        var rowTap = new TapGestureRecognizer();
        rowTap.Tapped += OnRowTapped;
        background.GestureRecognizers.Add(rowTap);

        Add(background);

        // Drop indicators (always added; opacity toggled during drag)
        dropIndicatorAbove = MakeDropIndicator(LayoutOptions.Start);
        dropIndicatorBelow = MakeDropIndicator(LayoutOptions.End);
        // Drop affordances follow the theme accent rather than a fixed Windows blue.
        dropIndicatorInto = new Grid { IsVisible = false, Opacity = 0.2 };
        dropIndicatorInto.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Primary);
        Add(dropIndicatorInto);
        Add(dropIndicatorAbove);
        Add(dropIndicatorBelow);

        if (owner.EnableDragDrop)
            WireDragDrop();

        RefreshChevron();
        RefreshSelection();
        HookNodeChanges();
    }

    public TreeNode Node { get; }

    /// <summary>Names this row's parts after the tree, at <paramref name="index"/> in the visible (flattened) order.</summary>
    internal void ApplyAutomationIds(int index)
    {
        AutomationIds.Set(background, AutomationIds.For(owner, TreeView.NodeAutomationIdSuffix, index));
        AutomationIds.Set(chevronHost, AutomationIds.For(owner, TreeView.ExpandAutomationIdSuffix, index));
        if (checkBox != null)
            AutomationIds.Set(checkBox, AutomationIds.For(owner, TreeView.CheckBoxAutomationIdSuffix, index) ?? CheckBoxAutomationId);
    }

    Grid BuildIndent()
    {
        var width = owner.IndentSize * Node.Depth;
        var host = new Grid
        {
            WidthRequest = width,
            HorizontalOptions = LayoutOptions.Start
        };

        if (!owner.ShowGuideLines || Node.Depth == 0)
            return host;

        for (var d = 0; d < Node.Depth; d++)
        {
            // An empty Grid rather than a BoxView: a BoxView with no HeightRequest asks for MAUI's
            // 40x40 default, which made every row with guide lines taller than a root row. The Grid
            // asks for no height and stretches (Fill) to whatever the row is.
            var line = new Grid
            {
                WidthRequest = 1,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Fill,
                InputTransparent = true,
                Margin = new Thickness((d * owner.IndentSize) + (owner.IndentSize / 2), 0, 0, 0)
            };
            if (owner.GuideLineColor is Color gc)
                line.BackgroundColor = gc;
            else
                line.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.OutlineVariant);
            host.Add(line);
        }
        return host;
    }

    static Grid MakeDropIndicator(LayoutOptions vertical)
    {
        var indicator = new Grid
        {
            HeightRequest = 2,
            VerticalOptions = vertical,
            HorizontalOptions = LayoutOptions.Fill,
            IsVisible = false
        };
        indicator.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Primary);
        return indicator;
    }

    public void RefreshChevron()
    {
        chevronHost.Children.Clear();

        var hasChildren = owner.HasChildren(Node.Item);
        var canExpand = owner.CanExpand(Node.Item);

        if (!hasChildren)
            return;

        if (Node.LoadState == TreeLoadState.Loading)
        {
            var indicator = new ActivityIndicator
            {
                IsRunning = true,
                WidthRequest = owner.ChevronSize,
                HeightRequest = owner.ChevronSize,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center
            };
            if (owner.ChevronColor is Color cc)
                indicator.Color = cc;
            else
                indicator.SetDynamicResource(ActivityIndicator.ColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
            chevronHost.Children.Add(indicator);
            return;
        }

        if (Node.LoadState == TreeLoadState.Error)
        {
            chevronHost.Children.Add(BuildChevronImage(owner.RetryIcon, "↻"));
            return;
        }

        var icon = Node.IsExpanded ? owner.ExpandedIcon : owner.CollapsedIcon;
        var glyph = Node.IsExpanded ? "▼" : "▶";
        var view = BuildChevronImage(icon, glyph);
        view.Opacity = canExpand ? 1.0 : 0.35;
        chevronHost.Children.Add(view);
    }

    View BuildChevronImage(ImageSource? icon, string fallbackGlyph)
    {
        if (icon != null)
        {
            return new Image
            {
                Source = icon,
                WidthRequest = owner.ChevronSize,
                HeightRequest = owner.ChevronSize,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
                Aspect = Aspect.AspectFit
            };
        }

        var glyphLabel = new Label
        {
            Text = fallbackGlyph,
            FontSize = owner.ChevronSize * 0.75,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };
        if (owner.ChevronColor is Color cc)
            glyphLabel.TextColor = cc;
        else
            glyphLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        return glyphLabel;
    }

    public void RefreshSelection()
    {
        RefreshCheckBox();

        if (Node.IsSelected)
        {
            if (owner.SelectedBackgroundColor is Color selected)
                background.BackgroundColor = selected;
            else
                background.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SecondaryContainer);
        }
        else
        {
            // Row background defaults to transparent; clear any dynamic resource binding.
            background.RemoveDynamicResource(VisualElement.BackgroundColorProperty);
            background.BackgroundColor = owner.RowBackgroundColor;
        }
    }

    // Every colour goes through ThemeProbe.Tint / ThemeBrush.Apply, so an explicit CheckBoxColor wins and
    // an unset one follows the theme token live - including across a light/dark flip with no selection
    // change.
    void RefreshCheckBox()
    {
        if (checkBox == null)
            return;

        var selected = Node.IsSelected;
        ThemeBrush.Apply(
            checkBox,
            Border.StrokeProperty,
            selected ? owner.CheckBoxColor : null,
            selected ? ShinyThemeKeys.Brush.Primary : ShinyThemeKeys.Brush.OnSurfaceVariant
        );
        ThemeProbe.Tint(
            checkBox,
            VisualElement.BackgroundColorProperty,
            selected ? owner.CheckBoxColor : Colors.Transparent,
            ShinyThemeKeys.Color.Primary
        );
        checkGlyph!.IsVisible = selected;
    }

    void OnChevronTapped(object? sender, EventArgs e)
    {
        if (Node.LoadState == TreeLoadState.Error)
        {
            owner.RetryLoad(Node);
            return;
        }
        if (!owner.HasChildren(Node.Item) || !owner.CanExpand(Node.Item))
            return;
        owner.ToggleExpand(Node);
    }

    void OnRowTapped(object? sender, EventArgs e)
    {
        owner.HandleRowTapped(Node);
    }

    void HookNodeChanges()
    {
        Node.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(TreeNode.IsExpanded):
                case nameof(TreeNode.LoadState):
                    RefreshChevron();
                    break;
                case nameof(TreeNode.IsSelected):
                    RefreshSelection();
                    break;
            }
        };
    }

    // ------------- Drag/drop -------------
    public void ShowDropIndicator(TreeDropPosition zone)
    {
        dropIndicatorAbove.IsVisible = zone == TreeDropPosition.Above;
        dropIndicatorBelow.IsVisible = zone == TreeDropPosition.Below;
        dropIndicatorInto.IsVisible = zone == TreeDropPosition.Into;
    }

    public void HideDropIndicators()
    {
        dropIndicatorAbove.IsVisible = false;
        dropIndicatorBelow.IsVisible = false;
        dropIndicatorInto.IsVisible = false;
    }

    public void SetDragging(bool dragging)
    {
        Opacity = dragging ? 0.6 : 1.0;
        ZIndex = dragging ? 1 : 0;
        if (!dragging)
            TranslationY = 0;
    }

#if IOS || ANDROID || WINDOWS
    void WireDragDrop()
    {
        var drag = new DragGestureRecognizer { CanDrag = true };
        drag.DragStarting += (s, e) =>
        {
            e.Data.Properties["TreeNodeView"] = this;
        };
        background.GestureRecognizers.Add(drag);

        var drop = new DropGestureRecognizer { AllowDrop = true };
        drop.DragOver += OnDragOver;
        drop.DragLeave += OnDragLeave;
        drop.Drop += OnDrop;
        background.GestureRecognizers.Add(drop);
    }

    TreeDropPosition currentZone = TreeDropPosition.Above;

    void OnDragOver(object? sender, DragEventArgs e)
    {
        // DragEventArgs doesn't expose a reliable pointer position on every platform,
        // so the platform-gesture path always targets "below" (reorder as next sibling).
        currentZone = TreeDropPosition.Below;
        ShowDropIndicator(currentZone);
        e.AcceptedOperation = DataPackageOperation.Copy;
    }

    void OnDragLeave(object? sender, DragEventArgs e) => HideDropIndicators();

    void OnDrop(object? sender, DropEventArgs e)
    {
        HideDropIndicators();

        if (e.Data?.Properties != null &&
            e.Data.Properties.TryGetValue("TreeNodeView", out var src) &&
            src is TreeNodeView srcView &&
            !ReferenceEquals(srcView, this))
        {
            owner.HandleDrop(srcView.Node, Node, currentZone);
        }
    }
#else
    // Plain net11.0 serves Mac Catalyst, the AppKit (macOS) host and the GTK4 (Linux)
    // host. Catalyst's Drag/DropGestureRecognizers are broken (dotnet/maui#23627) and
    // the labs hosts don't implement them at all, so the drag is driven by a pan
    // gesture and the TreeView resolves the drop target from row geometry.
    void WireDragDrop()
    {
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;
        background.GestureRecognizers.Add(pan);
    }

    void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                owner.BeginPointerDrag(this);
                break;
            case GestureStatus.Running:
                owner.UpdatePointerDrag(this, e.TotalY);
                break;
            case GestureStatus.Completed:
                owner.CompletePointerDrag(this);
                break;
            case GestureStatus.Canceled:
                owner.CancelPointerDrag(this);
                break;
        }
    }
#endif
}

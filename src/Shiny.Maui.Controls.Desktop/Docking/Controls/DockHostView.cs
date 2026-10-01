using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Microsoft.Maui;
using Microsoft.Maui.Layouts;
using Keys = Shiny.Maui.Controls.Themes.ShinyThemeKeys;

namespace Shiny.Maui.Controls.Desktop.Docking;

/// <summary>
/// Root dock surface. Attaches to an existing <see cref="ContentPage"/> — does not
/// subclass it, so consumers keep control of their Shell / page architecture.
/// </summary>
/// <remarks>
/// Place inside a page like any other <see cref="View"/>:
/// <code>
/// &lt;ContentPage ...&gt;
///     &lt;docking:DockHostView InitialLayout="{Binding StartupLayout}" /&gt;
/// &lt;/ContentPage&gt;
/// </code>
/// </remarks>
public class DockHostView : ContentView, IDockHost
{
    const double RailSize = 230;
    const double RailBarSize = 170;
    const double EdgeRail = 240;     // preview size for an outer-edge (rail) drop
    const double DragOpacity = 0.72; // drag window / dragged floating window

    DockRoot? layout;
    string? pristineJson;
    bool loadAttempted;
    DockableContentRegistry? registry;
    CancellationTokenSource? saveCts;

    readonly Dictionary<string, View> views = new();                    // panelInstanceId -> content view
    readonly Dictionary<string, DockGroupView> groupViews = new();      // groupId -> rendered group
    readonly Dictionary<string, DockArea?> groupRails = new();          // groupId -> owning rail (null = document area)
    readonly Dictionary<string, bool> groupInSplit = new();             // groupId -> rendered inside a split
    readonly Dictionary<string, DockWindowState?> groupFloat = new();   // groupId -> owning floating window
    readonly List<FloatChrome> floatChrome = new();
    readonly Dictionary<DockWindowState, int> floatOrder = new(ReferenceEqualityComparer.Instance);
    int floatZ;
    View? emptyDocView;                                                 // rendered DockEmpty doc area, a valid drop target
    readonly Grid mainGrid;
    readonly AbsoluteLayout overlay;
    readonly DockEventsImpl events = new();
    readonly DockCommandScopeImpl commandScope = new();

    // drag visuals: built once up front and only shown/hidden — on the AppKit head a child added
    // after the layout is realized never gets a platform view
    readonly AbsoluteLayout dragLayer;
    readonly Grid preview;
    readonly BoxView caret;
    readonly Grid compass;
    readonly BoxView[] compassArms;
    readonly Dictionary<DockZone, DockGuide> compassCells = new();
    readonly Dictionary<DockZone, DockGuide> edgeGuides = new();
    readonly Dictionary<DockZone, Rect> edgeRects = new();
    readonly Border dragWindow;
    readonly Label dragTitle;
    readonly Label dragIcon;
    readonly Label dragBody;

    DragSession? drag;
    Point? pressPoint;   // where the pointer went down on pressView, so a drag starts under it
    View? pressView;

    public static readonly BindableProperty InitialLayoutProperty = BindableProperty.Create(
        nameof(InitialLayout), typeof(DockRoot), typeof(DockHostView),
        propertyChanged: (b, _, _) => ((DockHostView)b).TryInitialLoad());

    public DockRoot? InitialLayout
    {
        get => (DockRoot?)GetValue(InitialLayoutProperty);
        set => SetValue(InitialLayoutProperty, value);
    }

    public static readonly BindableProperty IsLockedProperty = BindableProperty.Create(
        nameof(IsLocked), typeof(bool), typeof(DockHostView), false,
        propertyChanged: (b, _, _) => ((DockHostView)b).RebuildAll());

    public bool IsLocked
    {
        get => (bool)GetValue(IsLockedProperty);
        set => SetValue(IsLockedProperty, value);
    }

    public static readonly BindableProperty LayoutStoreProperty = BindableProperty.Create(
        nameof(LayoutStore), typeof(IDockLayoutStore), typeof(DockHostView));

    public IDockLayoutStore? LayoutStore
    {
        get => (IDockLayoutStore?)GetValue(LayoutStoreProperty);
        set => SetValue(LayoutStoreProperty, value);
    }

    public IDockEvents Events => events;
    public IDockCommandScope CommandScope => commandScope;

    public DockHostView()
    {
        // rows: top-rail, top-resizer, center, bottom-resizer, bottom-rail
        // cols: left-rail, left-resizer, doc, right-resizer, right-rail
        mainGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto)
            },
            Padding = 4
        };
        mainGrid.Tint(BackgroundColorProperty, Keys.Color.SurfaceContainer);

        // overlay passes input through itself but its children (floating windows) still receive it
        overlay = new AbsoluteLayout
        {
            InputTransparent = true,
            CascadeInputTransparent = false
        };

        preview = BuildPreview();
        caret = new BoxView { WidthRequest = 3, CornerRadius = 1.5, IsVisible = false, InputTransparent = true }
            .Tint(BoxView.ColorProperty, Keys.Color.Primary);
        compass = BuildCompass(out compassArms);
        dragWindow = BuildDragWindow(out dragIcon, out dragTitle, out dragBody);

        dragLayer = new AbsoluteLayout { InputTransparent = true, CascadeInputTransparent = true };
        dragLayer.Children.Add(preview);
        dragLayer.Children.Add(caret);
        foreach (var zone in new[] { DockZone.Left, DockZone.Right, DockZone.Top, DockZone.Bottom })
        {
            var guide = DockChrome.Guide(zone, edge: true);
            guide.IsVisible = false;
            edgeGuides[zone] = guide;
            AbsoluteLayout.SetLayoutBounds(guide, new Rect(0, 0, DockChrome.GuideSize, DockChrome.GuideSize));
            dragLayer.Children.Add(guide);
        }
        dragLayer.Children.Add(compass);
        dragLayer.Children.Add(dragWindow);

        var root = new Grid();
        root.Add(mainGrid);
        root.Add(overlay);
        root.Add(dragLayer);
        Content = root;

        ShowPlaceholder();
    }

    static Grid BuildPreview()
    {
        var fill = new BoxView { CornerRadius = 6, Opacity = 0.24 }.Tint(BoxView.ColorProperty, Keys.Color.Primary);
        var edge = new Border
        {
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Background = Brush.Transparent
        }.Tint(Border.StrokeProperty, Keys.Brush.Primary);
        return new Grid { IsVisible = false, InputTransparent = true, Children = { fill, edge } };
    }

    // a cross of five guides on a translucent cross-shaped backdrop
    Grid BuildCompass(out BoxView[] arms)
    {
        var size = DockChrome.CompassSize;
        var armWidth = DockChrome.GuideSize + DockChrome.CompassPad * 2;
        var grid = new Grid { WidthRequest = size, HeightRequest = size, IsVisible = false, InputTransparent = true };
        var across = new BoxView { HeightRequest = armWidth, VerticalOptions = LayoutOptions.Center, CornerRadius = 14, Opacity = 0.85 }
            .Tint(BoxView.ColorProperty, Keys.Color.SurfaceContainerHigh);
        var down = new BoxView { WidthRequest = armWidth, HorizontalOptions = LayoutOptions.Center, CornerRadius = 14, Opacity = 0.85 }
            .Tint(BoxView.ColorProperty, Keys.Color.SurfaceContainerHigh);
        grid.Add(across);
        grid.Add(down);
        arms = [across, down];

        foreach (var (zone, col, row) in CompassCells)
        {
            var guide = DockChrome.Guide(zone, edge: false);
            guide.HorizontalOptions = LayoutOptions.Start;
            guide.VerticalOptions = LayoutOptions.Start;
            guide.Margin = new Thickness(CellOffset(col), CellOffset(row), 0, 0);
            compassCells[zone] = guide;
            grid.Add(guide);
        }
        AbsoluteLayout.SetLayoutBounds(grid, new Rect(0, 0, size, size));
        return grid;
    }

    static readonly (DockZone Zone, int Col, int Row)[] CompassCells =
    [
        (DockZone.Top, 1, 0), (DockZone.Left, 0, 1), (DockZone.Center, 1, 1), (DockZone.Right, 2, 1), (DockZone.Bottom, 1, 2)
    ];

    static double CellOffset(int cell) => DockChrome.CompassPad + cell * (DockChrome.GuideSize + DockChrome.GuideGap);

    // a floating-window look-alike that follows the pointer while a tab is dragged
    static Border BuildDragWindow(out Label icon, out Label title, out Label body)
    {
        icon = new Label { FontSize = 12, VerticalOptions = LayoutOptions.Center };
        title = new Label
        {
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalOptions = LayoutOptions.Center
        }.Tint(Label.TextColorProperty, Keys.Color.OnPrimary);
        var header = new Grid
        {
            Padding = new Thickness(10, 7),
            ColumnSpacing = 6,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }
        }.Tint(BackgroundColorProperty, Keys.Color.Primary);
        header.Add(icon, 0, 0);
        header.Add(title, 1, 0);

        body = new Label
        {
            FontSize = 12,
            FontAttributes = FontAttributes.Italic,
            Opacity = 0.6,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        }.Tint(Label.TextColorProperty, Keys.Color.OnSurface);

        var grid = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        grid.Add(header, 0, 0);
        grid.Add(body, 0, 1);

        return new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = grid,
            IsVisible = false,
            InputTransparent = true,
            Opacity = DragOpacity,
            Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.32f, Radius = 24, Offset = new Point(0, 12) }
        }
        .Tint(Border.StrokeProperty, Keys.Brush.Primary)
        .Tint(BackgroundColorProperty, Keys.Color.Surface);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler?.MauiContext?.Services is { } services)
        {
            registry ??= services.GetService<DockableContentRegistry>();
            TryInitialLoad();
        }
    }

    void ShowPlaceholder()
    {
        mainGrid.Children.Clear();
        var placeholder = new Border
        {
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 4 },
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            Margin = 8,
            Content = new Label
            {
                Text = "No dock layout loaded",
                FontSize = 14,
                FontAttributes = FontAttributes.Italic,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }.Tint(Label.TextColorProperty, Keys.Color.OnSurfaceVariant)
        }.Tint(Border.StrokeProperty, Keys.Brush.OutlineVariant);
        mainGrid.Add(placeholder, 1, 1);
    }

    async void TryInitialLoad()
    {
        if (layout is not null || loadAttempted || registry is null) return;
        loadAttempted = true;

        DockRoot? stored = null;
        if (LayoutStore is not null)
        {
            try { stored = await LayoutStore.LoadAsync(); }
            catch { /* fall back to InitialLayout on store failure */ }
        }
        var root = stored ?? InitialLayout;
        if (root is null)
        {
            loadAttempted = false; // a later InitialLayout assignment may retry
            return;
        }
        await LoadCoreAsync(root, CancellationToken.None);
    }

    // ------------------------------------------------------------------ IDockHost
    public Task LoadAsync(DockRoot root, CancellationToken ct = default)
    {
        loadAttempted = true;
        return LoadCoreAsync(root, ct);
    }

    async Task LoadCoreAsync(DockRoot root, CancellationToken ct)
    {
        root = Migrate(root);
        var json = DockSerialization.Serialize(root);
        pristineJson ??= InitialLayout is not null
            ? DockSerialization.Serialize(Migrate(InitialLayout))
            : json;
        layout = DockSerialization.Deserialize(json)!;
        ConvertLegacyCollapsedRails();
        views.Clear();
        floatOrder.Clear();
        await ResolveViewsAsync(ct);
        RebuildAll();
        events.RaiseLayoutChanged(new LayoutChangedEventArgs { Snapshot = Snapshot(), Reason = "load" });
    }

    // older layouts collapsed whole rails via CollapsedRails — convert to per-panel entries
    void ConvertLegacyCollapsedRails()
    {
        var win = layout!.MainWindow;
        foreach (var area in win.CollapsedRails.ToList())
        {
            var node = area switch
            {
                DockArea.Top => win.TopRail,
                DockArea.Right => win.RightRail,
                DockArea.Bottom => win.BottomRail,
                _ => win.LeftRail
            };
            foreach (var tab in GroupsIn(node).SelectMany(g => g.Tabs))
                win.CollapsedTabs.Add(new DockCollapsedPanel { Area = area, Tab = tab });
            switch (area)
            {
                case DockArea.Top: win.TopRail = null; break;
                case DockArea.Right: win.RightRail = null; break;
                case DockArea.Bottom: win.BottomRail = null; break;
                default: win.LeftRail = null; break;
            }
        }
        win.CollapsedRails.Clear();
    }

    DockRoot Migrate(DockRoot root)
    {
        var migrators = Handler?.MauiContext?.Services
            .GetService<IEnumerable<IDockLayoutMigrator>>()?.ToList();
        if (migrators is null || migrators.Count == 0) return root;
        while (root.SchemaVersion < DockRoot.CurrentSchemaVersion)
        {
            var step = migrators.FirstOrDefault(m => m.FromVersion == root.SchemaVersion);
            if (step is null) break;
            root = step.Migrate(root);
            root.SchemaVersion = step.ToVersion;
        }
        return root;
    }

    public DockRoot Snapshot()
    {
        var current = layout ?? InitialLayout;
        if (current is null) return new DockRoot();
        return DockSerialization.Deserialize(DockSerialization.Serialize(current))!;
    }

    public async Task ShowPanelAsync(string panelTypeId, DockArea preferredArea = DockArea.Left, CancellationToken ct = default)
    {
        layout ??= new DockRoot();

        var existing = AllGroups().SelectMany(g => g.Tabs).FirstOrDefault(t => t.PanelTypeId == panelTypeId);
        if (existing is not null)
        {
            await ActivatePanelAsync(existing.PanelInstanceId, ct);
            return;
        }

        var collapsedExisting = layout.MainWindow.CollapsedTabs
            .FirstOrDefault(c => c.Tab.PanelTypeId == panelTypeId);
        if (collapsedExisting is not null)
        {
            await RestoreCollapsedAsync(collapsedExisting);
            return;
        }

        var tab = new DockTab { PanelTypeId = panelTypeId };
        await ResolveViewAsync(tab, ct);

        DockIntoRail(preferredArea, tab);
        layout.MainWindow.ActivePanelId = tab.PanelInstanceId;
        RebuildAll();
        OnLayoutMutated("show-panel");
        events.RaisePanelActivated(new PanelActivatedEventArgs
        {
            PanelInstanceId = tab.PanelInstanceId,
            PanelTypeId = tab.PanelTypeId
        });
    }

    void DockIntoRail(DockArea area, DockTab tab) => DockLayoutOps.DockIntoRail(layout!, area, tab);

    public Task HidePanelAsync(string panelInstanceId, CancellationToken ct = default)
    {
        if (layout is null) return Task.CompletedTask;

        var collapsedEntry = layout.MainWindow.CollapsedTabs
            .FirstOrDefault(c => c.Tab.PanelInstanceId == panelInstanceId);
        if (collapsedEntry is not null)
        {
            layout.MainWindow.CollapsedTabs.Remove(collapsedEntry);
            if (views.TryGetValue(panelInstanceId, out var collapsedView) && collapsedView is IDisposable d)
                d.Dispose();
            views.Remove(panelInstanceId);
            RebuildAll();
            OnLayoutMutated("hide-panel");
            return Task.CompletedTask;
        }

        foreach (var group in AllGroups())
        {
            var tab = group.Tabs.FirstOrDefault(t => t.PanelInstanceId == panelInstanceId);
            if (tab is null) continue;

            if (views.TryGetValue(panelInstanceId, out var view) && view is IDisposable disposable)
                disposable.Dispose();
            views.Remove(panelInstanceId);

            group.Tabs.Remove(tab);
            group.ActiveTabIndex = Math.Clamp(group.ActiveTabIndex, 0, Math.Max(0, group.Tabs.Count - 1));
            SimplifyAll();
            if (layout.MainWindow.ActivePanelId == panelInstanceId)
                layout.MainWindow.ActivePanelId = null;

            RebuildAll();
            OnLayoutMutated("hide-panel");
            return Task.CompletedTask;
        }
        return Task.CompletedTask;
    }

    public Task ActivatePanelAsync(string panelInstanceId, CancellationToken ct = default)
    {
        foreach (var group in AllGroups())
        {
            var idx = group.Tabs.FindIndex(t => t.PanelInstanceId == panelInstanceId);
            if (idx < 0) continue;
            ActivateTab(group, idx);
            return Task.CompletedTask;
        }
        return Task.CompletedTask;
    }

    public async Task ResetLayoutAsync(CancellationToken ct = default)
    {
        if (pristineJson is null) return;
        layout = DockSerialization.Deserialize(pristineJson)!;
        views.Clear();
        floatOrder.Clear();
        await ResolveViewsAsync(ct);
        RebuildAll();
        OnLayoutMutated("reset");
    }

    /// <summary>Collapse or restore every panel on a rail at once. Individual panels
    /// collapse via their tab-strip button; this is the bulk/programmatic form.</summary>
    public async Task SetRailCollapsedAsync(DockArea area, bool collapsed, CancellationToken ct = default)
    {
        if (layout is null) return;
        var win = layout.MainWindow;

        if (collapsed)
        {
            var node = area switch
            {
                DockArea.Top => win.TopRail,
                DockArea.Right => win.RightRail,
                DockArea.Bottom => win.BottomRail,
                _ => win.LeftRail
            };
            var tabs = GroupsIn(node).SelectMany(g => g.Tabs).ToList();
            if (tabs.Count == 0) return;
            foreach (var tab in tabs)
                win.CollapsedTabs.Add(new DockCollapsedPanel { Area = area, Tab = tab });
            switch (area)
            {
                case DockArea.Top: win.TopRail = null; break;
                case DockArea.Right: win.RightRail = null; break;
                case DockArea.Bottom: win.BottomRail = null; break;
                default: win.LeftRail = null; break;
            }
            RebuildAll();
            OnLayoutMutated("rail-collapse");
        }
        else
        {
            var items = CollapsedFor(area);
            if (items.Count == 0) return;
            foreach (var item in items)
            {
                win.CollapsedTabs.Remove(item);
                await ResolveViewAsync(item.Tab, ct);
                DockIntoRail(area, item.Tab);
            }
            RebuildAll();
            OnLayoutMutated("rail-expand");
        }
    }

    void CollapseActiveTab(DockArea area, DockGroup group)
    {
        if (layout is null || group.Tabs.Count == 0) return;
        var idx = Math.Clamp(group.ActiveTabIndex, 0, group.Tabs.Count - 1);
        var tab = group.Tabs[idx];
        group.Tabs.RemoveAt(idx);
        // the next tab becomes active; the group stays expanded while tabs remain
        group.ActiveTabIndex = Math.Clamp(idx, 0, Math.Max(0, group.Tabs.Count - 1));
        layout.MainWindow.CollapsedTabs.Add(new DockCollapsedPanel { Area = area, Tab = tab });
        SimplifyAll();
        RebuildAll();
        OnLayoutMutated("panel-collapse");
    }

    async Task RestoreCollapsedAsync(DockCollapsedPanel item)
    {
        if (layout is null) return;
        layout.MainWindow.CollapsedTabs.Remove(item);
        await ResolveViewAsync(item.Tab, CancellationToken.None);
        DockIntoRail(item.Area, item.Tab);
        RebuildAll();
        OnLayoutMutated("panel-expand");
        await ActivatePanelAsync(item.Tab.PanelInstanceId);
    }

    List<DockCollapsedPanel> CollapsedFor(DockArea area)
        => layout?.MainWindow.CollapsedTabs.Where(c => c.Area == area).ToList() ?? new();

    bool HasCollapsed(DockArea area)
        => layout?.MainWindow.CollapsedTabs.Any(c => c.Area == area) == true;

    public Task SetGroupCollapsedAsync(string groupId, bool collapsed, CancellationToken ct = default)
    {
        var group = AllGroups().FirstOrDefault(g => g.GroupId == groupId);
        if (group is null || group.IsCollapsed == collapsed) return Task.CompletedTask;
        group.IsCollapsed = collapsed;
        RebuildAll(); // split sizing changes with collapse state
        OnLayoutMutated(collapsed ? "group-collapse" : "group-expand");
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ rendering
    void RebuildAll()
    {
        if (layout is null) return;

        mainGrid.Children.Clear();
        groupViews.Clear();
        groupRails.Clear();
        groupInSplit.Clear();
        groupFloat.Clear();
        floatChrome.Clear();
        overlay.Children.Clear();

        var win = layout.MainWindow;

        if (win.TopRail is not null || HasCollapsed(DockArea.Top))
        {
            var v = BuildRail(win.TopRail, DockArea.Top, out var rsz);
            mainGrid.Add(v, 0, 0);
            Grid.SetColumnSpan(v, 5);
            if (rsz is not null)
            {
                mainGrid.Add(rsz, 0, 1);
                Grid.SetColumnSpan(rsz, 5);
            }
        }
        if (win.BottomRail is not null || HasCollapsed(DockArea.Bottom))
        {
            var v = BuildRail(win.BottomRail, DockArea.Bottom, out var rsz);
            mainGrid.Add(v, 0, 4);
            Grid.SetColumnSpan(v, 5);
            if (rsz is not null)
            {
                mainGrid.Add(rsz, 0, 3);
                Grid.SetColumnSpan(rsz, 5);
            }
        }
        if (win.LeftRail is not null || HasCollapsed(DockArea.Left))
        {
            var v = BuildRail(win.LeftRail, DockArea.Left, out var rsz);
            mainGrid.Add(v, 0, 2);
            if (rsz is not null) mainGrid.Add(rsz, 1, 2);
        }
        if (win.RightRail is not null || HasCollapsed(DockArea.Right))
        {
            var v = BuildRail(win.RightRail, DockArea.Right, out var rsz);
            mainGrid.Add(v, 4, 2);
            if (rsz is not null) mainGrid.Add(rsz, 3, 2);
        }

        var docView = BuildNode(win.DocumentArea);
        emptyDocView = win.DocumentArea is DockEmpty ? docView : null;
        mainGrid.Add(docView, 2, 2);

        for (var i = 0; i < layout.FloatingWindows.Count; i++)
            overlay.Children.Add(BuildFloating(i, layout.FloatingWindows[i]));

        RefreshFocusChrome();
    }

    View BuildRail(DockNode? node, DockArea area, out View? resizer)
    {
        resizer = null;
        var vertical = area is DockArea.Left or DockArea.Right;
        var collapsed = CollapsedFor(area);

        View? content = null;
        if (node is not null)
        {
            content = BuildNode(node, area);
            if (vertical)
                content.WidthRequest = GetRailSize(area);
            else
                content.HeightRequest = GetRailSize(area);
            resizer = IsLocked
                ? new BoxView { Color = Colors.Transparent, WidthRequest = 4, HeightRequest = 4 }
                : BuildRailResizer(area, content, vertical);
        }

        if (collapsed.Count == 0)
            return content!;

        var bar = BuildCollapsedRailBar(collapsed, area);
        if (content is null)
            return bar;

        // collapsed edge bar hugs the outer edge, docked content sits beside it
        var wrap = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
        if (vertical)
        {
            wrap.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            wrap.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var barFirst = area == DockArea.Left;
            wrap.Add(bar, barFirst ? 0 : 1, 0);
            wrap.Add(content, barFirst ? 1 : 0, 0);
        }
        else
        {
            wrap.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            wrap.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var barFirst = area == DockArea.Top;
            wrap.Add(bar, 0, barFirst ? 0 : 1);
            wrap.Add(content, 0, barFirst ? 1 : 0);
        }
        return wrap;
    }

    double GetRailSize(DockArea area)
    {
        var win = layout!.MainWindow;
        return area switch
        {
            DockArea.Left => win.LeftRailSize ?? RailSize,
            DockArea.Right => win.RightRailSize ?? RailSize,
            DockArea.Top => win.TopRailSize ?? RailBarSize,
            _ => win.BottomRailSize ?? RailBarSize
        };
    }

    View BuildRailResizer(DockArea area, View rail, bool vertical)
    {
        // Opacity 0 would stop it hit-testing on iOS, so the idle state is a transparent colour
        var handle = new BoxView { Color = Colors.Transparent, CornerRadius = 2 };
        if (vertical) handle.WidthRequest = 5;
        else handle.HeightRequest = 5;

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => { handle.Opacity = 0.55; handle.Tint(BoxView.ColorProperty, Keys.Color.Primary); };
        pointer.PointerExited += (_, _) => { handle.RemoveDynamicResource(BoxView.ColorProperty); handle.Color = Colors.Transparent; handle.Opacity = 1; };
        handle.GestureRecognizers.Add(pointer);

        double startSize = 0;
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    startSize = GetRailSize(area);
                    break;
                case GestureStatus.Running:
                {
                    var delta = area switch
                    {
                        DockArea.Left => e.TotalX,
                        DockArea.Right => -e.TotalX,
                        DockArea.Top => e.TotalY,
                        _ => -e.TotalY
                    };
                    var size = Math.Clamp(startSize + delta, 80, 1200);
                    if (vertical) rail.WidthRequest = size;
                    else rail.HeightRequest = size;
                    break;
                }
                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                {
                    var final = vertical ? rail.WidthRequest : rail.HeightRequest;
                    var win = layout!.MainWindow;
                    switch (area)
                    {
                        case DockArea.Left: win.LeftRailSize = final; break;
                        case DockArea.Right: win.RightRailSize = final; break;
                        case DockArea.Top: win.TopRailSize = final; break;
                        default: win.BottomRailSize = final; break;
                    }
                    OnLayoutMutated("rail-resize");
                    break;
                }
            }
        };
        handle.GestureRecognizers.Add(pan);
        return handle;
    }

    View BuildCollapsedRailBar(List<DockCollapsedPanel> items, DockArea area)
    {
        var vertical = area is DockArea.Left or DockArea.Right;
        Microsoft.Maui.Controls.Layout stack = vertical
            ? new VerticalStackLayout { Spacing = 4, Padding = new Thickness(2, 6) }
            : new HorizontalStackLayout { Spacing = 4, Padding = new Thickness(6, 2) };

        foreach (var entry in items)
        {
            var captured = entry;
            var icon = GetTabIcon(entry.Tab);

            var label = new Label
            {
                Text = GetTabTitle(entry.Tab),
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                LineBreakMode = LineBreakMode.TailTruncation,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }.Tint(Label.TextColorProperty, Keys.Color.OnSurfaceVariant);

            View item;
            if (vertical)
            {
                // icon stays upright at the reading start; the title rotates along the bar
                label.Rotation = area == DockArea.Left ? 270 : 90;
                label.WidthRequest = 110;
                var cell = new VerticalStackLayout { Spacing = 2, WidthRequest = 26 };
                if (icon is not null)
                    cell.Children.Add(new Label
                    {
                        Text = icon,
                        FontSize = 12,
                        HorizontalOptions = LayoutOptions.Center
                    });
                cell.Children.Add(new Grid { HeightRequest = 112, Children = { label } });
                item = cell;
            }
            else
            {
                var row = new HorizontalStackLayout { Spacing = 4, Padding = new Thickness(8, 2) };
                if (icon is not null)
                    row.Children.Add(new Label { Text = icon, FontSize = 12, VerticalOptions = LayoutOptions.Center });
                row.Children.Add(label);
                item = row;
            }

            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => _ = RestoreCollapsedAsync(captured);
            item.GestureRecognizers.Add(tap);
            stack.Children.Add(item);
        }

        var bar = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Content = stack
        }
        .Tint(Border.StrokeProperty, Keys.Brush.OutlineVariant)
        .Tint(BackgroundColorProperty, Keys.Color.SurfaceContainerLow);
        if (vertical) bar.WidthRequest = 32;
        else bar.HeightRequest = 32;
        return bar;
    }

    View BuildNode(DockNode node, DockArea? rail = null, bool inSplit = false, DockWindowState? inFloat = null) => node switch
    {
        DockSplit split => BuildSplit(split, rail, inFloat),
        DockGroup group => BuildGroup(group, rail, inSplit, inFloat),
        _ => BuildEmpty()
    };

    View BuildSplit(DockSplit split, DockArea? rail = null, DockWindowState? inFloat = null)
    {
        var grid = new Grid();
        var horizontal = split.Orientation == DockOrientation.Horizontal;
        var firstCollapsed = split.First is DockGroup { IsCollapsed: true };
        var secondCollapsed = split.Second is DockGroup { IsCollapsed: true };
        var anyCollapsed = firstCollapsed || secondCollapsed;

        // a collapsed child sizes to its tab strip; the other side takes the rest
        var firstLength = firstCollapsed ? GridLength.Auto
            : anyCollapsed ? GridLength.Star
            : new GridLength(split.Ratio, GridUnitType.Star);
        var secondLength = secondCollapsed ? GridLength.Auto
            : anyCollapsed ? GridLength.Star
            : new GridLength(1 - split.Ratio, GridUnitType.Star);

        if (horizontal)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(firstLength));
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(DockSplitter.Thickness)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(secondLength));
        }
        else
        {
            grid.RowDefinitions.Add(new RowDefinition(firstLength));
            grid.RowDefinitions.Add(new RowDefinition(new GridLength(DockSplitter.Thickness)));
            grid.RowDefinitions.Add(new RowDefinition(secondLength));
        }

        var first = BuildNode(split.First, rail, inSplit: true, inFloat);
        var second = BuildNode(split.Second, rail, inSplit: true, inFloat);
        var splitter = new DockSplitter
        {
            Orientation = split.Orientation,
            Ratio = split.Ratio,
            IsLocked = IsLocked || anyCollapsed, // no resizing against a collapsed pane
            ExtentProvider = () => horizontal ? grid.Width : grid.Height
        };
        splitter.RatioChanging += (_, ratio) =>
        {
            if (horizontal)
            {
                grid.ColumnDefinitions[0].Width = new GridLength(ratio, GridUnitType.Star);
                grid.ColumnDefinitions[2].Width = new GridLength(1 - ratio, GridUnitType.Star);
            }
            else
            {
                grid.RowDefinitions[0].Height = new GridLength(ratio, GridUnitType.Star);
                grid.RowDefinitions[2].Height = new GridLength(1 - ratio, GridUnitType.Star);
            }
        };
        splitter.RatioCommitted += (_, ratio) =>
        {
            split.Ratio = ratio;
            OnLayoutMutated("splitter");
        };

        if (horizontal)
        {
            grid.Add(first, 0, 0);
            grid.Add(splitter, 1, 0);
            grid.Add(second, 2, 0);
        }
        else
        {
            grid.Add(first, 0, 0);
            grid.Add(splitter, 0, 1);
            grid.Add(second, 0, 2);
        }
        return grid;
    }

    View BuildGroup(DockGroup group, DockArea? rail = null, bool inSplit = false, DockWindowState? inFloat = null)
    {
        var gv = new DockGroupView();
        gv.TabActivateRequested += (_, tab) =>
        {
            var idx = group.Tabs.IndexOf(tab);
            if (idx >= 0) ActivateTab(group, idx);
        };
        gv.TabDoubleTapped += (_, tab) => _ = OnTabDoubleTapAsync(tab);
        gv.TabCloseRequested += (_, tab) => _ = HidePanelAsync(tab.PanelInstanceId);
        gv.TabPan += (_, e) => HandleTabPan(group, e.Tab, e.View, e.Pan);
        gv.TabPressed += (_, e) =>
        {
            pressView = e.View;
            pressPoint = e.Position;
        };
        gv.FloatRequested += (_, _) =>
        {
            if (group.Tabs.Count == 0) return;
            var active = group.Tabs[Math.Clamp(group.ActiveTabIndex, 0, group.Tabs.Count - 1)];
            _ = FloatPanelAsync(active.PanelInstanceId);
        };

        // one collapse button, collapsing toward where the panel is docked:
        // rail groups collapse the rail to its edge bar; split document groups
        // shrink to their tab strip; a lone document group has no button
        if (rail is { } railArea)
            gv.CollapseRequested += (_, _) => CollapseActiveTab(railArea, group);
        else if (inSplit || group.IsCollapsed)
            gv.CollapseRequested += (_, _) => _ = SetGroupCollapsedAsync(group.GroupId, !group.IsCollapsed);

        gv.Apply(group, ResolveCachedView, GetTabTitle, IsLocked, CollapseDirectionFor(group, rail, inSplit), GetTabIcon, inFloat is null);

        // the window frame is the border of a floating window's lone group, and its title bar
        // already names a lone panel
        if (inFloat is not null && ReferenceEquals(inFloat.DocumentArea, group))
            gv.SetFlush(true, hideStrip: group.Tabs.Count == 1);

        groupViews[group.GroupId] = gv;
        groupRails[group.GroupId] = rail;
        groupInSplit[group.GroupId] = inSplit;
        groupFloat[group.GroupId] = inFloat;
        return gv;
    }

    static DockArea? CollapseDirectionFor(DockGroup group, DockArea? rail, bool inSplit)
    {
        if (rail is { } area)
            return area;
        if (inSplit || group.IsCollapsed)
            return group.IsCollapsed ? DockArea.Right : DockArea.Bottom;
        return null;
    }

    static View BuildEmpty() => new Border
    {
        StrokeThickness = 1,
        StrokeDashArray = new DoubleCollection { 4, 4 },
        StrokeShape = new RoundRectangle { CornerRadius = 6 },
        BackgroundColor = Colors.Transparent,
        Content = new Label
        {
            Text = "Drop a panel here",
            FontSize = 12,
            FontAttributes = FontAttributes.Italic,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        }.Tint(Label.TextColorProperty, Keys.Color.OnSurfaceVariant)
    }.Tint(Border.StrokeProperty, Keys.Brush.OutlineVariant);

    // ------------------------------------------------------------------ floating windows
    sealed record FloatChrome(
        DockWindowState Window,
        Grid Container,
        Border Pane,
        Grid Header,
        Label Title,
        Label Icon,
        IReadOnlyList<DockChromeButton> Buttons);

    static readonly string[] ResizeEdges = ["n", "s", "e", "w", "ne", "nw", "se", "sw"];

    View BuildFloating(int index, DockWindowState fw)
    {
        var bounds = fw.Bounds ?? new DockRect(60 + index * 28, 48 + index * 28, DockLayoutOps.DefaultFloatWidth, DockLayoutOps.DefaultFloatHeight);

        var icon = new Label { FontSize = 12, VerticalOptions = LayoutOptions.Center };
        var title = new Label
        {
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        var header = new Grid
        {
            Padding = new Thickness(10, 0, 4, 0),
            MinimumHeightRequest = 30,
            ColumnSpacing = 6,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        header.Add(icon, 0, 0);
        header.Add(title, 1, 0);

        var buttons = new List<DockChromeButton>();
        if (!IsLocked)
        {
            var actions = new HorizontalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
            var dock = DockChrome.ChromeButton(DockChrome.DockBackIcon, Keys.Brush.OnSurfaceVariant, "Dock",
                () => _ = DockFloatingAsync(index));
            actions.Children.Add(dock);
            buttons.Add(dock);
            if (CanCloseFloating(fw))
            {
                var close = DockChrome.ChromeButton(DockChrome.CloseIcon, Keys.Brush.OnSurfaceVariant, "Close",
                    () => CloseFloating(index), danger: true);
                actions.Children.Add(close);
                buttons.Add(close);
            }
            header.Add(actions, 2, 0);
        }

        var body = BuildNode(fw.DocumentArea, inFloat: fw);
        var paneGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };
        paneGrid.Add(header, 0, 0);
        paneGrid.Add(body, 0, 1);

        var pane = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = paneGrid,
            Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.22f, Radius = 20, Offset = new Point(0, 10) }
        }.Tint(BackgroundColorProperty, Keys.Color.Surface);

        var container = new Grid { Children = { pane }, ZIndex = FloatZ(fw) };
        AbsoluteLayout.SetLayoutBounds(container, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        SemanticProperties.SetDescription(container, FloatTitle(fw));

        // clicking anywhere on a window raises it
        var raise = new TapGestureRecognizer();
        raise.Tapped += (_, _) => BringToFront(fw);
        pane.GestureRecognizers.Add(raise);

        if (!IsLocked)
        {
            var headerPress = new PointerGestureRecognizer();
            headerPress.PointerPressed += (_, e) =>
            {
                pressView = header;
                pressPoint = e.GetPosition(header);
            };
            header.GestureRecognizers.Add(headerPress);

            var pan = new PanGestureRecognizer();
            pan.PanUpdated += (_, e) => HandleFloatPan(index, fw, container, header, e);
            header.GestureRecognizers.Add(pan);

            // a double click on the title bar sends the window home, as in VS
            var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
            doubleTap.Tapped += (_, _) => _ = DockFloatingAsync(index);
            header.GestureRecognizers.Add(doubleTap);

            foreach (var edge in ResizeEdges)
                container.Add(BuildResizeHandle(fw, container, edge));
        }

        var chrome = new FloatChrome(fw, container, pane, header, title, icon, buttons);
        floatChrome.Add(chrome);
        ApplyFloatChrome(chrome);
        return container;
    }

    // edge and corner grips. Transparent, not Opacity 0 — a zero-opacity view does not hit-test on iOS
    View BuildResizeHandle(DockWindowState fw, Grid container, string edge)
    {
        const double band = 6, corner = 12;
        var handle = new BoxView { Color = Colors.Transparent };
        var horizontalEdge = edge is "n" or "s";
        var verticalEdge = edge is "e" or "w";
        handle.HorizontalOptions = edge.Contains('e') ? LayoutOptions.End : edge.Contains('w') ? LayoutOptions.Start : LayoutOptions.Fill;
        handle.VerticalOptions = edge.Contains('s') ? LayoutOptions.End : edge.Contains('n') ? LayoutOptions.Start : LayoutOptions.Fill;
        if (horizontalEdge)
        {
            handle.HeightRequest = band;
            handle.Margin = new Thickness(corner, 0);
        }
        else if (verticalEdge)
        {
            handle.WidthRequest = band;
            handle.Margin = new Thickness(0, corner);
        }
        else
        {
            handle.WidthRequest = corner;
            handle.HeightRequest = corner;
        }

        Rect start = default;
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    start = AbsoluteLayout.GetLayoutBounds(container);
                    break;
                case GestureStatus.Running:
                {
                    double x = start.X, y = start.Y, w = start.Width, h = start.Height;
                    if (edge.Contains('e')) w = Math.Max(DockLayoutOps.MinFloatWidth, start.Width + e.TotalX);
                    if (edge.Contains('s')) h = Math.Max(DockLayoutOps.MinFloatHeight, start.Height + e.TotalY);
                    if (edge.Contains('w'))
                    {
                        w = Math.Max(DockLayoutOps.MinFloatWidth, start.Width - e.TotalX);
                        x = start.X + start.Width - w;
                    }
                    if (edge.Contains('n'))
                    {
                        h = Math.Max(DockLayoutOps.MinFloatHeight, start.Height - e.TotalY);
                        y = start.Y + start.Height - h;
                    }
                    // the title bar must stay reachable — it is the only way to grab the window again
                    if (y < 0) { h += y; y = 0; }
                    AbsoluteLayout.SetLayoutBounds(container, new Rect(x, y, w, h));
                    break;
                }
                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                {
                    var r = AbsoluteLayout.GetLayoutBounds(container);
                    var next = DockLayoutOps.ClampFloat(new DockRect(r.X, r.Y, r.Width, r.Height));
                    if (fw.Bounds != next)
                    {
                        fw.Bounds = next;
                        OnLayoutMutated("float-resize");
                    }
                    break;
                }
            }
        };
        handle.GestureRecognizers.Add(pan);
        return handle;
    }

    void ApplyFloatChrome(FloatChrome chrome)
    {
        var active = IsFloatingActive(chrome.Window);
        chrome.Title.Text = FloatTitle(chrome.Window);
        var icon = FloatIcon(chrome.Window);
        chrome.Icon.Text = icon ?? string.Empty;
        chrome.Icon.IsVisible = icon is not null;

        // the active window's title bar takes the accent, like a focused tool window in VS
        chrome.Header.Tint(BackgroundColorProperty, active ? Keys.Color.Primary : Keys.Color.SurfaceContainerHigh);
        chrome.Title.Tint(Label.TextColorProperty, active ? Keys.Color.OnPrimary : Keys.Color.OnSurfaceVariant);
        chrome.Pane.Tint(Border.StrokeProperty, active ? Keys.Brush.Primary : Keys.Brush.OutlineVariant);
        foreach (var button in chrome.Buttons)
            button.IconBrushKey = active ? Keys.Brush.OnPrimary : Keys.Brush.OnSurfaceVariant;
    }

    /// <summary>Re-tints focus state in place: the focused group's border and accent bar, and float title bars.</summary>
    void RefreshFocusChrome()
    {
        foreach (var gv in groupViews.Values)
            gv.SetFocused(gv.Group is { } g && IsGroupFocused(g));
        foreach (var chrome in floatChrome)
            ApplyFloatChrome(chrome);
    }

    void BringToFront(DockWindowState fw)
    {
        if (floatOrder.TryGetValue(fw, out var z) && z == floatZ) return;
        floatOrder[fw] = ++floatZ;
        // ZIndex re-adds the native child on Android, so this only ever runs between gestures
        foreach (var chrome in floatChrome)
            if (ReferenceEquals(chrome.Window, fw))
                chrome.Container.ZIndex = floatZ;
    }

    int FloatZ(DockWindowState fw) => floatOrder.TryGetValue(fw, out var z) ? z : 0;

    bool CanCloseTab(DockTab tab)
        => !views.TryGetValue(tab.PanelInstanceId, out var view) || view is not IDockableContent d || d.CanClose;

    bool CanCloseFloating(DockWindowState fw)
        => GroupsIn(fw.DocumentArea).SelectMany(g => g.Tabs).All(CanCloseTab);

    static DockTab? ActiveTabOf(DockWindowState fw)
    {
        var group = GroupsIn(fw.DocumentArea).FirstOrDefault();
        if (group is null || group.Tabs.Count == 0) return null;
        return group.Tabs[Math.Clamp(group.ActiveTabIndex, 0, group.Tabs.Count - 1)];
    }

    bool IsFloatingActive(DockWindowState fw)
        => layout?.MainWindow.ActivePanelId is { } id
           && GroupsIn(fw.DocumentArea).Any(g => g.Tabs.Any(t => t.PanelInstanceId == id));

    bool IsGroupFocused(DockGroup group)
        => layout?.MainWindow.ActivePanelId is { } id && group.Tabs.Any(t => t.PanelInstanceId == id);

    string FloatTitle(DockWindowState fw)
        => ActiveTabOf(fw) is { } active ? GetTabTitle(active) : "Floating";

    string? FloatIcon(DockWindowState fw)
        => ActiveTabOf(fw) is { } active ? GetTabIcon(active) : null;

    // ------------------------------------------------------------------ drag + docking guides
    //
    // Dragging works the way Visual Studio's does: a dragged tab becomes a translucent window
    // following the pointer (a dragged floating window IS the window, made translucent), a docking
    // compass appears over the pane underneath and four guides sit on the host's outer edges. Only
    // a drop ON a guide (or a tab strip) docks — anywhere else the panel floats where it was let go.

    sealed class DragSession
    {
        public required bool IsFloat { get; init; }
        public DockGroup? SourceGroup { get; init; }
        public DockTab? Tab { get; init; }
        public Border? TabView { get; init; }
        public bool SourceSolo { get; init; }
        public int FloatIndex { get; init; } = -1;
        public DockWindowState? Float { get; init; }
        public View? FloatView { get; init; }
        public Rect FloatStart { get; init; }
        public required Point Start { get; init; }
        public required Size Size { get; init; }
        public required Point Offset { get; init; }
        public Point Pointer { get; set; }
        public Rect WindowRect { get; set; }
        public DropTarget? Target { get; set; }

        public View? CompassFor { get; set; }
        public DockGroup? CompassGroup { get; set; }
        public Rect CompassRect { get; set; }
        public Rect CompassTargetRect { get; set; }
        public bool CompassCenterOnly { get; set; }
    }

    sealed record DropTarget(DockGroup? Group, DockZone Zone, int Index, Rect Preview);

    void HandleTabPan(DockGroup group, DockTab tab, Border tabView, PanUpdatedEventArgs e)
    {
        if (IsLocked || layout is null) return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
            {
                var tr = AbsRect(tabView);
                var local = ReferenceEquals(pressView, tabView) && pressPoint is { } pp
                    ? pp
                    : new Point(tr.Width / 2, tr.Height / 2);
                var start = new Point(tr.X + local.X, tr.Y + local.Y);
                var gr = groupViews.TryGetValue(group.GroupId, out var gv) ? AbsRect(gv) : tr;
                var size = new Size(Math.Clamp(gr.Width, 260, 520), Math.Clamp(gr.Height, 180, 380));

                drag = new DragSession
                {
                    IsFloat = false,
                    SourceGroup = group,
                    Tab = tab,
                    TabView = tabView,
                    SourceSolo = group.Tabs.Count == 1,
                    Start = start,
                    Size = size,
                    Offset = new Point(Math.Clamp(local.X + 12, 16, size.Width - 48), 15)
                };

                var title = GetTabTitle(tab);
                var icon = GetTabIcon(tab);
                dragTitle.Text = title;
                dragIcon.Text = icon ?? string.Empty;
                dragIcon.IsVisible = icon is not null;
                dragBody.Text = title;
                // the tab being dragged out leaves a faint hole behind
                tabView.Opacity = 0.35;

                BeginDragVisuals();
                MoveDrag(start);
                events.RaiseDragStarted(new DockDragEventArgs { SourcePanelInstanceId = tab.PanelInstanceId });
                break;
            }
            case GestureStatus.Running when drag is { IsFloat: false }:
                MoveDrag(new Point(drag.Start.X + e.TotalX, drag.Start.Y + e.TotalY));
                break;
            case GestureStatus.Completed when drag is { IsFloat: false }:
                EndDrag(commit: true);
                break;
            case GestureStatus.Canceled when drag is { IsFloat: false }:
                EndDrag(commit: false);
                break;
        }
    }

    void HandleFloatPan(int index, DockWindowState fw, View container, View header, PanUpdatedEventArgs e)
    {
        if (IsLocked || layout is null) return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
            {
                var r = AbsoluteLayout.GetLayoutBounds(container);
                var local = ReferenceEquals(pressView, header) && pressPoint is { } pp
                    ? pp
                    : new Point(r.Width / 2, 15);
                var start = new Point(r.X + local.X, r.Y + local.Y);
                drag = new DragSession
                {
                    IsFloat = true,
                    FloatIndex = index,
                    Float = fw,
                    FloatView = container,
                    FloatStart = r,
                    Start = start,
                    Size = r.Size,
                    Offset = local,
                    WindowRect = r
                };
                // the real window travels with the pointer, see-through so the guides under it read
                container.Opacity = DragOpacity;
                BeginDragVisuals();
                MoveDrag(start);
                break;
            }
            case GestureStatus.Running when drag is { IsFloat: true }:
                MoveDrag(new Point(drag.Start.X + e.TotalX, drag.Start.Y + e.TotalY));
                break;
            case GestureStatus.Completed when drag is { IsFloat: true }:
                EndDrag(commit: true);
                break;
            case GestureStatus.Canceled when drag is { IsFloat: true }:
                EndDrag(commit: false);
                break;
        }
    }

    void BeginDragVisuals()
    {
        const double inset = 10;
        var gs = DockChrome.GuideSize;
        var w = Width;
        var h = Height;
        foreach (var (zone, guide) in edgeGuides)
        {
            var rect = zone switch
            {
                DockZone.Left => new Rect(inset, h / 2 - gs / 2, gs, gs),
                DockZone.Right => new Rect(w - inset - gs, h / 2 - gs / 2, gs, gs),
                DockZone.Top => new Rect(w / 2 - gs / 2, inset, gs, gs),
                _ => new Rect(w / 2 - gs / 2, h - inset - gs, gs, gs)
            };
            edgeRects[zone] = rect;
            AbsoluteLayout.SetLayoutBounds(guide, rect);
            guide.IsHot = false;
            guide.IsVisible = true;
        }
        HideCompass();
        preview.IsVisible = false;
        caret.IsVisible = false;
        dragWindow.Opacity = DragOpacity;
        dragWindow.IsVisible = drag is { IsFloat: false };
    }

    void HideDragVisuals()
    {
        foreach (var guide in edgeGuides.Values)
            guide.IsVisible = false;
        HideCompass();
        preview.IsVisible = false;
        caret.IsVisible = false;
        dragWindow.IsVisible = false;
    }

    void MoveDrag(Point p)
    {
        var d = drag!;
        d.Pointer = p;
        if (d.IsFloat)
        {
            var x = Math.Clamp(p.X - d.Offset.X, 0, Math.Max(0, Width - 60));
            var y = Math.Clamp(p.Y - d.Offset.Y, 0, Math.Max(0, Height - 30));
            d.WindowRect = new Rect(x, y, d.Size.Width, d.Size.Height);
            AbsoluteLayout.SetLayoutBounds(d.FloatView!, d.WindowRect);
        }
        else
        {
            d.WindowRect = new Rect(p.X - d.Offset.X, p.Y - d.Offset.Y, d.Size.Width, d.Size.Height);
            AbsoluteLayout.SetLayoutBounds(dragWindow, d.WindowRect);
        }

        d.Target = UpdateTarget(p);
        // over a guide the drag window fades further so the preview underneath is unmistakable
        if (!d.IsFloat)
            dragWindow.Opacity = d.Target is null ? DragOpacity : 0.45;
    }

    /// <summary>Resolves what a drop here would do, and draws it. Null means the drop floats.</summary>
    DropTarget? UpdateTarget(Point p)
    {
        var d = drag!;
        DropTarget? target = null;
        caret.IsVisible = false;

        // 1. what is under the pointer decides where the compass sits
        var (gv, overFloat) = GroupUnder(p);
        var own = gv?.Group is { } g && !d.IsFloat && d.SourceSolo && ReferenceEquals(g, d.SourceGroup);
        if (gv?.Group is { } hovered)
        {
            // a tab alone in its group can't split or merge with itself
            if (own)
                HideCompass();
            else
            {
                var content = gv.ContentHost;
                var cr = content.IsVisible && content.Height > 0 ? AbsRect(content) : AbsRect(gv);
                ShowCompass(gv, hovered, cr, AbsRect(gv), centerOnly: false);
            }
        }
        else if (!overFloat && emptyDocView is not null && AbsRect(emptyDocView).Contains(p))
        {
            var er = AbsRect(emptyDocView);
            ShowCompass(emptyDocView, null, er, er, centerOnly: true);
        }
        else if (!(d.CompassFor is not null && d.CompassRect.Contains(p)))
        {
            HideCompass();
        }

        // 2. outer edge guides
        foreach (var (zone, guide) in edgeGuides)
        {
            var hot = target is null && edgeRects[zone].Contains(p);
            guide.IsHot = hot;
            if (hot)
                target = new DropTarget(null, zone, -1, EdgePreview(zone));
        }

        // 3. compass guides
        foreach (var (zone, cell) in compassCells)
        {
            var visible = d.CompassFor is not null && (!d.CompassCenterOnly || zone == DockZone.Center);
            var hot = target is null && visible && CellRect(d, zone).Contains(p);
            cell.IsHot = hot;
            if (hot)
                target = new DropTarget(d.CompassGroup, zone, -1, Half(d.CompassTargetRect, zone));
        }

        // 4. a tab strip: insert at the gap under the pointer
        if (target is null && !own && gv?.Group is { } stripGroup && gv.TabStrip.IsVisible)
        {
            var sr = AbsRect(gv.TabStrip);
            if (sr.Contains(p))
            {
                var tabs = gv.TabStrip.TabViews;
                var index = tabs.Count;
                for (var i = 0; i < tabs.Count; i++)
                {
                    var tr = AbsRect(tabs[i].View);
                    if (p.X < tr.X + tr.Width / 2) { index = i; break; }
                }
                double cx;
                if (tabs.Count == 0) cx = sr.X + 6;
                else if (index < tabs.Count) cx = AbsRect(tabs[index].View).X - 1;
                else cx = AbsRect(tabs[^1].View).Right + 1;
                AbsoluteLayout.SetLayoutBounds(caret, new Rect(cx - 1.5, sr.Y + 3, 3, Math.Max(4, sr.Height - 6)));
                caret.IsVisible = true;
                target = new DropTarget(stripGroup, DockZone.TabStrip, index, AbsRect(gv));
            }
        }

        if (target is null)
            preview.IsVisible = false;
        else
        {
            AbsoluteLayout.SetLayoutBounds(preview, target.Preview);
            preview.IsVisible = true;
        }
        return target;
    }

    /// <summary>The group under the pointer, floating windows first (top-most wins).</summary>
    (DockGroupView? Group, bool OverFloat) GroupUnder(Point p)
    {
        var floats = floatChrome
            .Where(c => drag?.Float is null || !ReferenceEquals(c.Window, drag.Float))
            .OrderByDescending(c => FloatZ(c.Window))
            .ThenByDescending(c => floatChrome.IndexOf(c));
        foreach (var chrome in floats)
        {
            if (!AbsRect(chrome.Container).Contains(p)) continue;
            foreach (var gv in groupViews.Values)
                if (groupFloat.TryGetValue(gv.GroupId, out var owner)
                    && ReferenceEquals(owner, chrome.Window)
                    && AbsRect(gv).Contains(p))
                    return (gv, true);
            return (null, true);
        }

        foreach (var gv in groupViews.Values)
            if (groupFloat.TryGetValue(gv.GroupId, out var owner) && owner is null && AbsRect(gv).Contains(p))
                return (gv, false);
        return (null, false);
    }

    void ShowCompass(View forView, DockGroup? group, Rect centerOn, Rect targetRect, bool centerOnly)
    {
        var d = drag!;
        var size = DockChrome.CompassSize;
        var rect = new Rect(
            Math.Round(centerOn.X + centerOn.Width / 2 - size / 2),
            Math.Round(centerOn.Y + centerOn.Height / 2 - size / 2),
            size, size);
        if (!ReferenceEquals(d.CompassFor, forView) || d.CompassCenterOnly != centerOnly || d.CompassRect != rect)
        {
            AbsoluteLayout.SetLayoutBounds(compass, rect);
            foreach (var (zone, cell) in compassCells)
                cell.IsVisible = !centerOnly || zone == DockZone.Center;
            // a lone centre guide sits on its own, without the cross behind it
            foreach (var arm in compassArms)
                arm.IsVisible = !centerOnly;
        }
        d.CompassFor = forView;
        d.CompassGroup = group;
        d.CompassRect = rect;
        d.CompassTargetRect = targetRect;
        d.CompassCenterOnly = centerOnly;
        compass.IsVisible = true;
    }

    void HideCompass()
    {
        if (drag is { } d)
        {
            d.CompassFor = null;
            d.CompassGroup = null;
        }
        compass.IsVisible = false;
        foreach (var cell in compassCells.Values)
            cell.IsHot = false;
    }

    static Rect CellRect(DragSession d, DockZone zone)
    {
        var (_, col, row) = CompassCells.First(c => c.Zone == zone);
        return new Rect(d.CompassRect.X + CellOffset(col), d.CompassRect.Y + CellOffset(row), DockChrome.GuideSize, DockChrome.GuideSize);
    }

    static Rect Half(Rect r, DockZone zone) => zone switch
    {
        DockZone.Left => new Rect(r.X, r.Y, r.Width / 2, r.Height),
        DockZone.Right => new Rect(r.X + r.Width / 2, r.Y, r.Width / 2, r.Height),
        DockZone.Top => new Rect(r.X, r.Y, r.Width, r.Height / 2),
        DockZone.Bottom => new Rect(r.X, r.Y + r.Height / 2, r.Width, r.Height / 2),
        _ => r
    };

    Rect EdgePreview(DockZone zone)
    {
        var w = Math.Min(EdgeRail, Width / 3);
        var h = Math.Min(EdgeRail, Height / 3);
        return zone switch
        {
            DockZone.Left => new Rect(0, 0, w, Height),
            DockZone.Right => new Rect(Width - w, 0, w, Height),
            DockZone.Top => new Rect(0, 0, Width, h),
            _ => new Rect(0, Height - h, Width, h)
        };
    }

    void EndDrag(bool commit)
    {
        var d = drag;
        if (d is null) return;
        drag = null;
        pressPoint = null;
        pressView = null;
        HideDragVisuals();
        foreach (var guide in edgeGuides.Values)
            guide.IsHot = false;

        if (d.IsFloat)
            d.FloatView!.Opacity = 1;
        else
            d.TabView!.Opacity = 1;

        if (!commit || layout is null)
        {
            if (d.IsFloat)
                AbsoluteLayout.SetLayoutBounds(d.FloatView!, d.FloatStart);
            else
                events.RaiseDragCancelled(new DockDragEventArgs { SourcePanelInstanceId = d.Tab!.PanelInstanceId });
            return;
        }

        if (d.IsFloat)
            _ = CompleteFloatDropAsync(d);
        else
            _ = CompleteTabDropAsync(d);
    }

    async Task CompleteTabDropAsync(DragSession d)
    {
        var tab = d.Tab!;
        var t = d.Target;
        var zone = t?.Zone ?? DockZone.TearOff;
        var before = layout!.FloatingWindows.Count;

        bool changed;
        if (t is not null)
        {
            changed = DockLayoutOps.DropTab(layout, tab.PanelInstanceId, t.Group, t.Zone, t.Index, new DockRect(0, 0, 0, 0));
        }
        else
        {
            // no guide: float right where the drag window was let go, at its size
            var x = Math.Clamp(d.WindowRect.X, 0, Math.Max(0, Width - 80));
            var y = Math.Clamp(d.WindowRect.Y, 0, Math.Max(0, Height - 30));
            changed = DockLayoutOps.DropTab(layout, tab.PanelInstanceId, null, DockZone.TearOff, -1,
                new DockRect(x, y, d.Size.Width, d.Size.Height));
        }

        if (!changed)
        {
            events.RaiseDragCancelled(new DockDragEventArgs { SourcePanelInstanceId = tab.PanelInstanceId });
            return;
        }

        if (layout.FloatingWindows.Count > before)
            BringToFront(layout.FloatingWindows[^1]);
        RebuildAll();
        OnLayoutMutated("drag-drop");
        events.RaiseDragCompleted(new DockDragEventArgs
        {
            SourcePanelInstanceId = tab.PanelInstanceId,
            TargetGroupId = t?.Group?.GroupId,
            TargetZone = zone
        });
        await ActivatePanelAsync(tab.PanelInstanceId);
    }

    async Task CompleteFloatDropAsync(DragSession d)
    {
        var fw = d.Float!;
        var index = layout!.FloatingWindows.IndexOf(fw);
        if (index < 0) return;
        var t = d.Target;

        if (t is null)
        {
            // no guide: the window just moved
            var r = d.WindowRect;
            var b = fw.Bounds ?? new DockRect(r.X, r.Y, r.Width, r.Height);
            if (Math.Abs(b.X - r.X) < 0.5 && Math.Abs(b.Y - r.Y) < 0.5) return;
            fw.Bounds = DockLayoutOps.ClampFloat(b with { X = r.X, Y = r.Y });
            OnLayoutMutated("float-move");
            return;
        }

        var active = ActiveTabOf(fw);
        if (!DockLayoutOps.DropFloatingWindow(layout, index, t.Group, t.Zone, t.Index))
        {
            if (d.FloatView is { } view)
                AbsoluteLayout.SetLayoutBounds(view, d.FloatStart);
            return;
        }
        floatOrder.Remove(fw);
        RebuildAll();
        OnLayoutMutated("float-dock");
        if (active is not null)
        {
            events.RaiseDragCompleted(new DockDragEventArgs
            {
                SourcePanelInstanceId = active.PanelInstanceId,
                TargetGroupId = t.Group?.GroupId,
                TargetZone = t.Zone
            });
            await ActivatePanelAsync(active.PanelInstanceId);
        }
    }

    // ------------------------------------------------------------------ floating ops
    public async Task FloatPanelAsync(string panelInstanceId, CancellationToken ct = default)
    {
        if (IsLocked || layout is null) return;
        var n = layout.FloatingWindows.Count;
        var bounds = new DockRect(60 + n * 28, 48 + n * 28, DockLayoutOps.DefaultFloatWidth, DockLayoutOps.DefaultFloatHeight);
        var fw = DockLayoutOps.FloatTab(layout, panelInstanceId, bounds);
        if (fw is null || layout.FloatingWindows.Count == n) return;

        BringToFront(fw);
        RebuildAll();
        OnLayoutMutated("float-panel");
        await ActivatePanelAsync(panelInstanceId, ct);
    }

    /// <summary>Double-tapping a tab floats it; inside a floating window it docks the window back home.</summary>
    Task OnTabDoubleTapAsync(DockTab tab)
    {
        if (IsLocked || layout is null) return Task.CompletedTask;
        var group = AllGroups().FirstOrDefault(g => g.Tabs.Contains(tab));
        if (group is not null && DockLayoutOps.FloatingWindowOf(layout, group) is { } owner)
            return DockFloatingAsync(layout.FloatingWindows.IndexOf(owner));
        return FloatPanelAsync(tab.PanelInstanceId);
    }

    async Task DockFloatingAsync(int index)
    {
        if (IsLocked || layout is null || index < 0 || index >= layout.FloatingWindows.Count) return;
        floatOrder.Remove(layout.FloatingWindows[index]);
        var tabs = DockLayoutOps.DockFloatingBack(layout, index);

        RebuildAll();
        OnLayoutMutated("dock-floating");
        if (tabs.Count > 0)
            await ActivatePanelAsync(tabs[^1].PanelInstanceId);
    }

    void CloseFloating(int index)
    {
        if (IsLocked || layout is null || index < 0 || index >= layout.FloatingWindows.Count) return;
        var fw = layout.FloatingWindows[index];
        if (!CanCloseFloating(fw)) return;
        foreach (var t in GroupsIn(fw.DocumentArea).SelectMany(g => g.Tabs))
        {
            if (views.TryGetValue(t.PanelInstanceId, out var view) && view is IDisposable disposable)
                disposable.Dispose();
            views.Remove(t.PanelInstanceId);
        }
        layout.FloatingWindows.RemoveAt(index);
        floatOrder.Remove(fw);
        RebuildAll();
        OnLayoutMutated("close-floating");
    }

    // ------------------------------------------------------------------ internals
    void ActivateTab(DockGroup group, int index)
    {
        if (index < 0 || index >= group.Tabs.Count) return;
        var expanding = group.IsCollapsed;
        if (expanding)
        {
            group.IsCollapsed = false;
            RebuildAll();
            OnLayoutMutated("group-expand");
        }
        group.ActiveTabIndex = index;
        group.FocusHistory.Remove(index);
        group.FocusHistory.Add(index);

        var tab = group.Tabs[index];
        if (layout is not null)
            layout.MainWindow.ActivePanelId = tab.PanelInstanceId;

        commandScope.IsInScope = true;
        commandScope.ActiveGroupId = group.GroupId;
        commandScope.ActivePanelInstanceId = tab.PanelInstanceId;

        // refresh just this group's chrome + visible panel
        if (groupViews.TryGetValue(group.GroupId, out var gv))
        {
            groupRails.TryGetValue(group.GroupId, out var rail);
            groupInSplit.TryGetValue(group.GroupId, out var inSplit);
            groupFloat.TryGetValue(group.GroupId, out var owner);
            gv.Apply(group, ResolveCachedView, GetTabTitle, IsLocked, CollapseDirectionFor(group, rail, inSplit), GetTabIcon, owner is null);
        }
        RefreshFocusChrome();

        events.RaisePanelActivated(new PanelActivatedEventArgs
        {
            PanelInstanceId = tab.PanelInstanceId,
            PanelTypeId = tab.PanelTypeId
        });
    }

    void OnLayoutMutated(string reason)
    {
        events.RaiseLayoutChanged(new LayoutChangedEventArgs { Snapshot = Snapshot(), Reason = reason });
        QueueSave();
    }

    void QueueSave()
    {
        if (LayoutStore is null || layout is null) return;
        saveCts?.Cancel();
        var snapshot = Snapshot();
        var debounce = Math.Max(0, LayoutStore.SaveDebounceMs);
        if (debounce == 0)
        {
            _ = SaveSafeAsync(snapshot, CancellationToken.None);
            return;
        }
        saveCts = new CancellationTokenSource();
        _ = DebouncedSaveAsync(snapshot, debounce, saveCts.Token);
    }

    async Task DebouncedSaveAsync(DockRoot snapshot, int debounce, CancellationToken token)
    {
        try
        {
            await Task.Delay(debounce, token);
            await SaveSafeAsync(snapshot, token);
        }
        catch (OperationCanceledException) { }
    }

    async Task SaveSafeAsync(DockRoot snapshot, CancellationToken ct)
    {
        try { await LayoutStore!.SaveAsync(snapshot, ct); }
        catch { /* persistence must never take the host down */ }
    }

    View? ResolveCachedView(DockTab tab)
        => views.TryGetValue(tab.PanelInstanceId, out var view) ? view : null;

    string GetTabTitle(DockTab tab)
    {
        if (views.TryGetValue(tab.PanelInstanceId, out var view) && view is IDockableContent dockable)
            return dockable.Title;
        return registry?.Resolve(tab.PanelTypeId)?.DisplayName ?? tab.PanelTypeId;
    }

    string? GetTabIcon(DockTab tab)
    {
        if (views.TryGetValue(tab.PanelInstanceId, out var view)
            && view is IDockableContent { Icon: string contentIcon })
            return contentIcon;
        return registry?.Resolve(tab.PanelTypeId)?.Icon;
    }

    async Task ResolveViewsAsync(CancellationToken ct)
    {
        foreach (var group in AllGroups())
            foreach (var tab in group.Tabs)
                await ResolveViewAsync(tab, ct);
    }

    async Task ResolveViewAsync(DockTab tab, CancellationToken ct)
    {
        if (views.ContainsKey(tab.PanelInstanceId)) return;
        var factory = registry?.Resolve(tab.PanelTypeId);
        if (factory is null) return; // group renders the "unknown panel" label
        views[tab.PanelInstanceId] = await factory.CreateAsync(tab.PanelInstanceId, ct);
    }

    Point AbsBounds(VisualElement element)
    {
        double x = 0, y = 0;
        Element? current = element;
        while (current is VisualElement ve && !ReferenceEquals(current, this))
        {
            x += ve.Frame.X;
            y += ve.Frame.Y;
            if (ve.Parent is ScrollView sv)
            {
                x -= sv.ScrollX;
                y -= sv.ScrollY;
            }
            current = current.Parent;
        }
        return new Point(x, y);
    }

    Rect AbsRect(VisualElement element)
    {
        var p = AbsBounds(element);
        return new Rect(p.X, p.Y, element.Width, element.Height);
    }

    IEnumerable<DockGroup> AllGroups()
        => layout is null ? Enumerable.Empty<DockGroup>() : DockLayoutOps.AllGroups(layout);

    static IEnumerable<DockGroup> GroupsIn(DockNode? node) => DockLayoutOps.GroupsIn(node);

    void SimplifyAll()
    {
        if (layout is not null)
            DockLayoutOps.Simplify(layout);
    }

    sealed class DockEventsImpl : IDockEvents
    {
        public event EventHandler<LayoutChangedEventArgs>? LayoutChanged;
        public event EventHandler<PanelActivatedEventArgs>? PanelActivated;
        public event EventHandler<DockDragEventArgs>? DragStarted;
        public event EventHandler<DockDragEventArgs>? DragCompleted;
        public event EventHandler<DockDragEventArgs>? DragCancelled;

        internal void RaiseLayoutChanged(LayoutChangedEventArgs e) => LayoutChanged?.Invoke(this, e);
        internal void RaisePanelActivated(PanelActivatedEventArgs e) => PanelActivated?.Invoke(this, e);
        internal void RaiseDragStarted(DockDragEventArgs e) => DragStarted?.Invoke(this, e);
        internal void RaiseDragCompleted(DockDragEventArgs e) => DragCompleted?.Invoke(this, e);
        internal void RaiseDragCancelled(DockDragEventArgs e) => DragCancelled?.Invoke(this, e);
    }

    sealed class DockCommandScopeImpl : IDockCommandScope
    {
        public bool IsInScope { get; internal set; }
        public string? ActiveGroupId { get; internal set; }
        public string? ActivePanelInstanceId { get; internal set; }
    }
}

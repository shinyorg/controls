using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Ribbons;

/// <summary>
/// The desktop ribbon: a strip of tabs over a body of titled command groups.
/// </summary>
/// <example>
/// <code language="xaml">
/// &lt;shiny:Ribbon ApplicationButtonText="File" SelectedIndex="{Binding Tab}"&gt;
///     &lt;shiny:RibbonTab Title="Home"&gt;
///         &lt;shiny:RibbonGroup Title="Clipboard"&gt;
///             &lt;shiny:RibbonSplitButton Text="Paste" Icon="paste.png" Command="{Binding Paste}"&gt;
///                 &lt;shiny:RibbonMenuEntry Text="Keep formatting" Command="{Binding PasteKeep}" /&gt;
///                 &lt;shiny:RibbonMenuEntry Text="Text only" Command="{Binding PasteText}" /&gt;
///             &lt;/shiny:RibbonSplitButton&gt;
///             &lt;shiny:RibbonButton Text="Cut" Icon="cut.png" Size="Small" Command="{Binding Cut}" /&gt;
///             &lt;shiny:RibbonButton Text="Copy" Icon="copy.png" Size="Small" Command="{Binding Copy}" /&gt;
///         &lt;/shiny:RibbonGroup&gt;
///     &lt;/shiny:RibbonTab&gt;
/// &lt;/shiny:Ribbon&gt;
/// </code>
/// </example>
/// <remarks>
/// <para>
/// A desktop control, and only nominally a cross-platform one: it is built for Windows, macOS and
/// Linux, where there is a pointer to hover with and enough width for three rows of small commands.
/// Nothing stops it running on a phone, but a phone should have a
/// <c>ShinyToolbar</c> or a tab bar instead.
/// </para>
/// <para>
/// Every tab's body is built once and shown with <c>IsVisible</c>, never added and removed. Switching
/// tabs is then free, hosted content keeps its state across a switch, and the macOS AppKit head — which
/// gives no native view to a child added after the page was laid out — draws the ribbon at all.
/// Structural edits (adding a tab, a group or an item after the fact) do rebuild, and are the one path
/// that head cannot follow.
/// </para>
/// </remarks>
[ContentProperty(nameof(Tabs))]
public partial class Ribbon : ContentView
{
    readonly ObservableCollection<RibbonTab> tabs = new();
    readonly ObservableCollection<RibbonItem> quickAccess = new();

    readonly Grid root;
    readonly Border contextBand;
    readonly Label contextLabel;
    readonly Border headerFrame;
    readonly Grid header;
    readonly HorizontalStackLayout tabStack;
    readonly HorizontalStackLayout quickAccessStack;
    readonly ContentView appButtonHost;
    readonly Border collapseToggle;
    readonly Polyline collapseGlyph;
    readonly Border bodyFrame;
    readonly Grid bodyHost;
    readonly BoxView startFade;
    readonly BoxView endFade;

    readonly List<(RibbonTab Tab, Border Button, BoxView Underline, Label Label)> tabButtons = new();
    readonly List<(RibbonTab Tab, ScrollView Panel, List<RibbonGroupView> Groups)> panels = new();

    // A tab is rounded on top and square where it meets the strip, so the theme radius has to be a
    // number before two of its corners can be zeroed. See CornerRadiusProbe.
    readonly CornerRadiusProbe cornerProbe;

    readonly BoxView foregroundProbe;
    readonly BoxView outlineProbe;
    readonly BoxView accentProbe;

    int suppress;
    bool peeking;
    double lastRelayoutWidth = -1;


    public Ribbon()
    {
        (this.ForegroundBrush, this.foregroundProbe) = ThemeProbe.Create();
        (this.OutlineBrush, this.outlineProbe) = ThemeProbe.Create();
        (this.AccentBrush, this.accentProbe) = ThemeProbe.Create();

        this.contextLabel = new Label
        {
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        }.WithFontSize(ShinyThemeKeys.Type.LabelSmallSize);
        this.contextLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnTertiaryContainer);

        this.cornerProbe = new CornerRadiusProbe(ShinyThemeKeys.Shape.CornerSmallRadius, this.OnCornerRadiusChanged);

        this.contextBand = new Border
        {
            Content = this.contextLabel,
            Padding = new Thickness(12, 2),
            StrokeThickness = 0,
            Stroke = null,
            IsVisible = false,
            HorizontalOptions = LayoutOptions.Start,
            StrokeShape = new RoundRectangle()
        };
        this.contextBand.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.TertiaryContainer);

        this.appButtonHost = new ContentView { VerticalOptions = LayoutOptions.Center };

        this.tabStack = new HorizontalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.End };
        this.quickAccessStack = new HorizontalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center };

        this.collapseGlyph = new Polyline
        {
            Stroke = this.ForegroundBrush,
            StrokeThickness = 1.4,
            StrokeLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            WidthRequest = 10,
            HeightRequest = 6,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        this.collapseToggle = new Border
        {
            Content = this.collapseGlyph,
            Padding = new Thickness(8, 6),
            StrokeThickness = 0,
            Stroke = null,
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center,
            AutomationId = "RibbonCollapseToggle",
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerSmallRadius),
            GestureRecognizers =
            {
                new TapGestureRecognizer { Command = new Command(this.ToggleCollapsed) }
            }
        };

        this.header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),  // application button
                new ColumnDefinition(GridLength.Star),  // tabs
                new ColumnDefinition(GridLength.Auto),  // quick access
                new ColumnDefinition(GridLength.Auto)   // collapse chevron
            },
            Padding = new Thickness(6, 0, 6, 0),
            ColumnSpacing = 6
        };
        this.header.Add(this.appButtonHost, 0);
        this.header.Add(
            new ScrollView
            {
                Orientation = ScrollOrientation.Horizontal,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
                Content = this.tabStack,
                VerticalOptions = LayoutOptions.Fill
            },
            1
        );
        this.header.Add(this.quickAccessStack, 2);
        this.header.Add(this.collapseToggle, 3);

        this.headerFrame = new Border
        {
            Content = this.header,
            StrokeThickness = 0,
            Stroke = null,
            Padding = 0
        };

        this.bodyHost = new Grid();

        // Overlays, so they sit on top of whichever tab's panel is showing. Input-transparent because
        // they cover the leading and trailing groups, which still have to be tappable through them.
        this.startFade = new BoxView
        {
            WidthRequest = ScrollHintWidth,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Fill,
            Color = Colors.Transparent,
            InputTransparent = true,
            IsVisible = false
        };

        this.endFade = new BoxView
        {
            WidthRequest = ScrollHintWidth,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Fill,
            Color = Colors.Transparent,
            InputTransparent = true,
            IsVisible = false
        };
        this.bodyFrame = new Border
        {
            Content = this.bodyHost,
            StrokeThickness = 0,
            Stroke = null,
            Padding = new Thickness(8, 4)
        };

        this.root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),  // contextual band
                new RowDefinition(GridLength.Auto),  // tab strip
                new RowDefinition(GridLength.Auto)   // groups
            }
        };
        this.root.Add(this.contextBand, 0, 0);
        this.root.Add(this.headerFrame, 0, 1);
        this.root.Add(this.bodyFrame, 0, 2);

        // The probes resolve theme tokens, which only works for something that is in the tree. They are
        // zero-sized and hidden; see ThemeProbe.
        this.root.Add(this.foregroundProbe, 0, 0);
        this.root.Add(this.outlineProbe, 0, 0);
        this.root.Add(this.accentProbe, 0, 0);
        this.root.Add(this.cornerProbe.View, 0, 0);

        this.OnCornerRadiusChanged();

        this.tabs.CollectionChanged += this.OnTabsChanged;
        this.quickAccess.CollectionChanged += (_, _) => this.Rebuild();
        this.SizeChanged += (_, _) =>
        {
            this.ApplyWidthRule();
            this.RelayoutGroups();

            // A resize changes how much overflows, and collapsing a group in RelayoutGroups above can
            // remove the overflow entirely.
            this.UpdateScrollHints();
        };

        this.Content = this.root;
        this.Rebuild();

        // Last line: replays any styled property applied before the children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(Ribbon));
    }


    // ---------------------------------------------------------------------------------------------
    // Model
    // ---------------------------------------------------------------------------------------------

    /// <summary>The ribbon's tabs, left to right. The content property, so they are the XAML children.</summary>
    public IList<RibbonTab> Tabs => this.tabs;

    /// <summary>
    /// Small icon-only commands pinned to the trailing end of the tab strip — save, undo, redo. They
    /// are always reachable, whichever tab is showing and even while the ribbon is collapsed.
    /// </summary>
    public IList<RibbonItem> QuickAccessItems => this.quickAccess;

    /// <summary>The tabs the strip actually draws.</summary>
    public IReadOnlyList<RibbonTab> VisibleTabs => this.tabs.Where(x => x.IsVisible).ToList();


    /// <summary>Raised after the ribbon moves to a different tab, whatever moved it.</summary>
    public event EventHandler<RibbonTabEventArgs>? TabChanged;

    /// <summary>Raised when any item on the bar is pressed, after the item's own command has run.</summary>
    public event EventHandler<RibbonItemEventArgs>? ItemInvoked;

    /// <summary>Raised when a group's corner arrow is pressed.</summary>
    public event EventHandler<RibbonGroupEventArgs>? GroupDialogLauncherClicked;

    /// <summary>Raised when the application ("File") button is pressed.</summary>
    public event EventHandler? ApplicationButtonClicked;


    // Shared brushes. Brush-typed properties cannot take a Color token directly, so all three are
    // bound to hidden probes that can. See ThemeProbe.
    internal SolidColorBrush ForegroundBrush { get; }

    internal SolidColorBrush OutlineBrush { get; }

    internal SolidColorBrush AccentBrush { get; }


    // ---------------------------------------------------------------------------------------------
    // Selection
    // ---------------------------------------------------------------------------------------------

    /// <summary>Moves to the tab with this <see cref="RibbonTab.Key"/>. Returns false when there is no such tab.</summary>
    public bool SelectTab(string key)
    {
        var index = this.tabs.ToList().FindIndex(x => x.Key == key);
        if (index < 0 || !this.tabs[index].IsSelectable)
            return false;

        this.SelectedIndex = index;
        return true;
    }


    /// <summary>Moves to a tab the caller already holds. Returns false when it is not on this ribbon, or cannot be selected.</summary>
    public bool SelectTab(RibbonTab tab)
    {
        var index = this.tabs.IndexOf(tab);
        if (index < 0 || !tab.IsSelectable)
            return false;

        this.SelectedIndex = index;
        return true;
    }


    void OnSelectedIndexChanged(int oldIndex, int newIndex)
    {
        if (this.suppress > 0)
            return;

        this.ApplySelection(newIndex, RibbonTabChangeReason.Programmatic);
    }


    void OnSelectedTabChanged(RibbonTab? tab)
    {
        if (this.suppress > 0 || tab is null)
            return;

        var index = this.tabs.IndexOf(tab);
        if (index >= 0)
            this.ApplySelection(index, RibbonTabChangeReason.Programmatic);
    }


    /// <summary>
    /// Moves the ribbon to a tab, falling back to the nearest selectable one.
    /// </summary>
    /// <remarks>
    /// The fallback is what makes contextual tabs work without the host managing them: when the tab the
    /// ribbon is on is hidden because its selection went away, the ribbon lands somewhere real instead
    /// of showing an empty body.
    /// </remarks>
    void ApplySelection(int index, RibbonTabChangeReason reason)
    {
        var resolved = this.Resolve(index, out var fellBack);
        if (fellBack)
            reason = RibbonTabChangeReason.Fallback;

        var tab = resolved >= 0 ? this.tabs[resolved] : null;

        this.suppress++;
        try
        {
            this.SelectedIndex = resolved;
            this.SelectedTab = tab;
        }
        finally
        {
            this.suppress--;
        }

        this.ApplyTabVisuals();

        if (this.DisplayMode == RibbonDisplayMode.Collapsed && reason == RibbonTabChangeReason.User)
            this.peeking = true;

        this.ApplyDisplayMode();
        this.TabChanged?.Invoke(this, new RibbonTabEventArgs(tab, resolved, reason));
    }


    /// <summary>The nearest selectable tab to <paramref name="index"/>, or -1 when there is none.</summary>
    int Resolve(int index, out bool fellBack)
    {
        fellBack = false;

        if (index >= 0 && index < this.tabs.Count && this.tabs[index].IsSelectable)
            return index;

        fellBack = true;

        // Outward from where the caller asked, so a tab that vanished hands over to its neighbour
        // rather than throwing the user back to the first tab.
        for (var distance = 1; distance < this.tabs.Count; distance++)
        {
            foreach (var candidate in new[] { index - distance, index + distance })
            {
                if (candidate >= 0 && candidate < this.tabs.Count && this.tabs[candidate].IsSelectable)
                    return candidate;
            }
        }

        var first = this.tabs.ToList().FindIndex(x => x.IsSelectable);
        return first;
    }


    // ---------------------------------------------------------------------------------------------
    // Display mode
    // ---------------------------------------------------------------------------------------------

    /// <summary>Collapses an expanded ribbon and expands a collapsed one. The chevron and a double-tap both come here.</summary>
    /// <summary>The mode to come back to. Collapsed is a hidden body, not a third layout.</summary>
    /// <remarks>
    /// Without this, collapsing a <see cref="RibbonDisplayMode.Simplified"/> bar and re-opening it
    /// returned <see cref="RibbonDisplayMode.Expanded"/> - the dense single row could be put away but
    /// never got back, so on a narrow window the chevron was a one-way trip to a bar three times the
    /// height of the one you collapsed.
    /// </remarks>
    RibbonDisplayMode restoreMode = RibbonDisplayMode.Expanded;

    /// <summary>
    /// Applies <see cref="SimplifyBelowWidth"/>. Silent when the rule is off, when the width is not
    /// known yet, or when the user has collapsed the bar - their choice outranks the width.
    /// </summary>
    /// <summary>
    /// Re-measures the tab strip once there is a platform view.
    /// </summary>
    /// <remarks>
    /// The strip is built in the constructor, before the control has a handler and before the theme's
    /// resources are necessarily resolvable — and on Android the labels come out of that first pass
    /// zero pixels wide and are never measured again. The band is there, correctly sized and correctly
    /// coloured, with nothing in it: the tabs only appear once something else forces a relayout, which
    /// in practice means collapsing and re-opening the bar.
    ///
    /// Queued rather than called straight away because the handler is only half-attached at this
    /// point; the invalidation has to land after the platform view has been added to the tree.
    /// </remarks>
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (this.Handler is null)
            return;

        this.Dispatcher.Dispatch(() =>
        {
            this.tabStack.InvalidateMeasure();
            this.header.InvalidateMeasure();
        });
    }

    /// <summary>
    /// The ink that reads on the header band, whatever colour the band ended up.
    /// </summary>
    /// <remarks>
    /// Derived rather than trusted. <see cref="HeaderForegroundColor"/> is a promise a caller makes
    /// about a background they also set, and the two get out of step - a host that themes one and not
    /// the other, a default that survives a colour change - at which point the tabs are white on a
    /// light grey band and simply cannot be read. Reading the band's resolved colour and choosing
    /// against it cannot be wrong, because it is measuring the thing the text is actually sitting on.
    /// </remarks>
    Color HeaderInk
    {
        get
        {
            if (this.HeaderForegroundColor is { } explicitInk)
                return explicitInk;

            var ground = this.headerFrame.BackgroundColor;
            if (ground is null || ground.Alpha <= 0)
                return this.OnSurfaceInk();

            // sRGB relative luminance, cut at 0.179 - where white and black are equally readable. The
            // midpoint is the wrong cut: contrast is a ratio of (L + 0.05), so a mid-grey scores 2.7:1
            // against white and 7.8:1 against black.
            static double Channel(float v) => v <= 0.03928f ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

            var luminance =
                (0.2126 * Channel(ground.Red)) +
                (0.7152 * Channel(ground.Green)) +
                (0.0722 * Channel(ground.Blue));

            return luminance > 0.179 ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Colors.White;
        }
    }

    Color OnSurfaceInk()
        => Application.Current?.Resources.TryGetValue(ShinyThemeKeys.Color.OnSurface, out var value) == true && value is Color color
            ? color
            : Colors.Black;

    /// <summary>
    /// Gives a tab label the ink for the ground it is currently on.
    /// </summary>
    /// <param name="onSurface">
    /// True while the tab is lifted off the header band — selected, or hovered — and so is sitting on
    /// the theme's own surface rather than on the accent.
    /// </param>
    /// <remarks>
    /// One place decides this because three states reach it — build, selection and hover — and each was
    /// originally written on its own, which is how hover ended up still wearing the band's ink after
    /// selection had been fixed. The header's ink is chosen to read on the band; on a tab lifted onto a
    /// light surface it is white on near-white.
    /// </remarks>
    void ApplyTabInk(Label label, bool onSurface)
    {
        // Both branches assign the colour directly, and that symmetry is the fix rather than a style.
        // One of them used to SetDynamicResource: in MAUI a locally-set value outranks a dynamic
        // resource, so once a tab had been unselected - which assigns TextColor - selecting it again
        // could not put the resource back, and it stayed on the header's ink while sitting on the
        // light selected tab. The first render looked right because nothing had been assigned yet,
        // which is why this only showed up after changing tabs.
        label.TextColor = onSurface ? this.OnSurfaceInk() : this.HeaderInk;
    }

    void ApplyWidthRule()
    {
        if (this.SimplifyBelowWidth <= 0 || this.Width <= 0)
            return;

        if (this.DisplayMode == RibbonDisplayMode.Collapsed)
        {
            // Still worth recording, so re-opening lands in the mode the width now calls for rather
            // than the one it was in when it was put away at some other size.
            this.restoreMode = this.WidthMode;
            return;
        }

        if (this.DisplayMode != this.WidthMode)
            this.DisplayMode = this.WidthMode;
    }

    RibbonDisplayMode WidthMode
        => this.Width < this.SimplifyBelowWidth
            ? RibbonDisplayMode.Simplified
            : RibbonDisplayMode.Expanded;


    public void ToggleCollapsed()
    {
        if (!this.AllowCollapse)
            return;

        if (this.DisplayMode == RibbonDisplayMode.Collapsed)
        {
            this.DisplayMode = this.restoreMode;
            return;
        }

        this.restoreMode = this.DisplayMode;
        this.DisplayMode = RibbonDisplayMode.Collapsed;
    }


    void OnDisplayModeChanged(RibbonDisplayMode oldMode, RibbonDisplayMode newMode)
    {
        this.peeking = false;

        // A mode set from outside - a binding, a width rule - is also the one to come back to.
        if (newMode != RibbonDisplayMode.Collapsed)
            this.restoreMode = newMode;

        // Simplified is a different set of item sizes, not a different visibility - it has to rebuild.
        if (oldMode == RibbonDisplayMode.Simplified || newMode == RibbonDisplayMode.Simplified)
            this.Rebuild();
        else
            this.ApplyDisplayMode();
    }


    void ApplyDisplayMode()
    {
        var collapsed = this.DisplayMode == RibbonDisplayMode.Collapsed;
        this.bodyFrame.IsVisible = !collapsed || this.peeking;
        this.headerFrame.IsVisible = this.ShowTabStrip;

        // Chevron up while there is a body to hide, down while there is one to bring back.
        var pointsUp = this.bodyFrame.IsVisible;
        this.collapseGlyph.Points = pointsUp
            ? new PointCollection { new(0, 5), new(5, 0), new(10, 5) }
            : new PointCollection { new(0, 0), new(5, 5), new(10, 0) };

        this.collapseToggle.IsVisible = this.AllowCollapse;
    }


    // ---------------------------------------------------------------------------------------------
    // Notifications from the item views
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Presses an item as though the user had: runs its command, flips a toggle, and raises
    /// <see cref="ItemInvoked"/>. A menu button opens its dropdown instead.
    /// </summary>
    /// <remarks>
    /// Public because it is both a genuine API — a keyboard shortcut and the button it duplicates
    /// should go down one path, not two — and the seam a test presses through, since a tap gesture
    /// cannot be raised from one. Invoked from a drawn button the dropdown is anchored under it;
    /// invoked from code there is nothing to anchor to, so it opens centred.
    /// </remarks>
    public void Invoke(RibbonItem item)
    {
        var (tab, group) = this.Locate(item);
        if (!item.IsEnabled || group?.IsEnabled == false)
            return;

        switch (item)
        {
            // Order matters: a split button is a menu button, and pressing its face is the one case
            // where a menu button does something other than open its menu.
            case RibbonSplitButton split:
                split.Invoke();
                break;

            case RibbonMenuButton menu:
                this.OpenMenu(menu, null, () => this.NotifyItemInvoked(item, group, tab));
                return;

            case RibbonButton button:
                button.Invoke();
                break;

            default:
                return;
        }

        this.RefreshStates();
        this.NotifyItemInvoked(item, group, tab);
    }


    /// <summary>The tab and group an item belongs to, or nulls when it is a quick access item.</summary>
    (RibbonTab? Tab, RibbonGroup? Group) Locate(RibbonItem item)
    {
        foreach (var tab in this.tabs)
        {
            foreach (var group in tab.Groups)
            {
                if (group.Items.Contains(item))
                    return (tab, group);
            }
        }

        return (null, null);
    }


    internal void NotifyItemInvoked(RibbonItem item, RibbonGroup? group, RibbonTab? tab)
    {
        this.ItemInvoked?.Invoke(this, new RibbonItemEventArgs(item, group, tab));

        // A command run off a peeking ribbon closes it again, which is the whole point of collapsing.
        if (this.peeking)
        {
            this.peeking = false;
            this.ApplyDisplayMode();
        }
    }


    internal void NotifyGroupDialogLauncher(RibbonGroup group)
        => this.GroupDialogLauncherClicked?.Invoke(this, new RibbonGroupEventArgs(group));


    // ---------------------------------------------------------------------------------------------
    // Binding context
    // ---------------------------------------------------------------------------------------------

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        this.PushBindingContext();
    }


    void PushBindingContext()
    {
        foreach (var tab in this.tabs)
        {
            SetInheritedBindingContext(tab, this.BindingContext);
            tab.ApplyBindingContext(this.BindingContext);
        }

        foreach (var item in this.quickAccess)
            SetInheritedBindingContext(item, this.BindingContext);
    }


    /// <summary>
    /// Re-rounds the tabs and the context band from the theme's small radius, top corners only. Runs
    /// once the probe resolves and again on every theme swap, so the geometry follows a live change
    /// rather than freezing at whatever was loaded first.
    /// </summary>
    void OnCornerRadiusChanged()
    {
        var corners = new CornerRadius(this.cornerProbe.Radius, this.cornerProbe.Radius, 0, 0);

        if (this.contextBand.StrokeShape is RoundRectangle band)
            band.CornerRadius = corners;

        foreach (var (_, button, _, _) in this.tabButtons)
        {
            if (button.StrokeShape is RoundRectangle shape)
                shape.CornerRadius = corners;
        }
    }


    void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (RibbonTab tab in e.OldItems)
                tab.Changed -= this.OnTabChanged;
        }

        if (e.NewItems is not null)
        {
            foreach (RibbonTab tab in e.NewItems)
                tab.Changed += this.OnTabChanged;
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var tab in this.tabs)
            {
                tab.Changed -= this.OnTabChanged;
                tab.Changed += this.OnTabChanged;
            }
        }

        this.Rebuild();
    }


    /// <summary>
    /// A tab said something changed. Whether that needs a rebuild depends on what it was.
    /// </summary>
    /// <remarks>
    /// A toggle flipping is by far the most common change on a ribbon, and rebuilding for it would drop
    /// the pointer's hover on the very button being pressed and re-create the view mid-tap. So the
    /// cheap path repaints in place, and only a change that alters the <em>shape</em> of the bar —
    /// which tabs are on the strip, which groups are in a tab — rebuilds.
    /// </remarks>
    void OnTabChanged(object? sender, EventArgs e)
    {
        if (this.ShapeChanged())
            this.Rebuild();
        else
            this.RefreshStates();
    }
}

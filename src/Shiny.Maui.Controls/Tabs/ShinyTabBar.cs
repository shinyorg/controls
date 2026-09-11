using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>
/// A bottom tab bar with motion icons, badges and an optional raised centre button that presents
/// the current page's actions.
/// </summary>
/// <remarks>
/// <para>The bar is only the chrome. It owns the selection and nothing else, which is what lets the
/// same control sit inside a <see cref="ShinyTabbedPage"/> (where it drives lazily-built tab
/// content) and inside a <see cref="Shell"/> (where it drives <c>Shell.CurrentItem</c> and the
/// Shell keeps doing the navigating). Neither host is special-cased here.</para>
/// <para>Nothing in it touches a platform SDK: the icons are drawn <c>GraphicsView</c>s and the
/// motion is the repo's keyframe engine, so the bar renders identically on iOS, Android, Windows,
/// Mac Catalyst, AppKit and GTK4.</para>
/// </remarks>
/// <example>
/// <code language="xaml">
/// &lt;shiny:ShinyTabBar SelectedIndex="{Binding Tab}"&gt;
///     &lt;shiny:ShinyTabItem Title="Home" Icon="home" /&gt;
///     &lt;shiny:ShinyTabItem Title="Chat" Icon="message" Badge="3" /&gt;
///     &lt;shiny:ShinyTabBar.CenterButton&gt;
///         &lt;shiny:TabCenterButton Icon="plus" /&gt;
///     &lt;/shiny:ShinyTabBar.CenterButton&gt;
/// &lt;/shiny:ShinyTabBar&gt;
/// </code>
/// </example>
[ContentProperty(nameof(Items))]
public partial class ShinyTabBar : Grid
{
    const double PillWidthFactor = 2.4;
    const double PillHeightPadding = 8;
    const double IndicatorBarWidth = 28;
    const double IndicatorBarHeight = 3;
    const double DotSize = 5;

    readonly ObservableCollection<ShinyTabItem> items = new();
    readonly List<ShinyTabItem> subscribed = new();
    readonly List<TabCell> cells = new();

    readonly RowDefinition overhangRow;
    readonly Border barSurface;
    readonly Grid barGrid;
    readonly Border travelIndicator;
    readonly BoxView surfaceProbe;

    /// <summary>
    /// The bottom safe-area inset currently padded into the surface. Kept so a re-apply can be
    /// skipped when the answer has not moved — see <see cref="OnBarSizeChanged"/>.
    /// </summary>
    double bottomInset;

    /// <summary>
    /// Whether glass is currently attached. Kept so <see cref="ApplyGlass"/> can tell an ordinary
    /// re-apply from the transition that changes where the page's content is allowed to go.
    /// </summary>
    bool glassActive;
    readonly VerticalStackLayout centerHost;
    readonly Border centerCircle;
    readonly Grid centerIconHost;
    readonly Label centerLabel;

    View? centerIconView;
    bool suppressSelectionSync;
    int menuToken;

    Layout? menuLayer;
    BoxView? menuBackdrop;
    Border? menuCard;

    /// <summary>Creates the bar.</summary>
    public ShinyTabBar()
    {
        this.barGrid = new Grid { ColumnSpacing = 0 };

        // One indicator for the whole bar, placed by translation rather than by a Grid column, so it
        // can sit between two columns mid-flight. Added before the cells so it paints behind them.
        this.travelIndicator = new Border
        {
            StrokeThickness = 0,
            Stroke = null,
            InputTransparent = true,
            IsVisible = false,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            StrokeShape = new RoundRectangle()
        };
        this.barGrid.Children.Add(this.travelIndicator);

        this.barSurface = new Border
        {
            StrokeThickness = 0,
            Stroke = null,
            Content = this.barGrid,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerNoneRadius)
        };

        this.centerIconHost = new Grid
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        this.centerCircle = new Border
        {
            // On the circle rather than the stack around it, because this is where the tap gesture
            // is - an id on an element that has no recognizer resolves and then does nothing, which
            // is the most confusing possible outcome for a UI test.
            AutomationId = "tab-center",
            StrokeThickness = 0,
            Stroke = null,
            Padding = 0,
            Content = this.centerIconHost,
            HorizontalOptions = LayoutOptions.Center,

            // Left at the shape's own default and set from TabCenterButton.Size in
            // ApplyCenterButton. A circle's radius is half its diameter and nothing else - it is
            // intrinsic to the control rather than something a theme gets a say in.
            StrokeShape = new RoundRectangle()
        };

        this.centerLabel = new Label
        {
            HorizontalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            IsVisible = false
        };

        this.centerHost = new VerticalStackLayout
        {
            Spacing = 2,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Start,
            IsVisible = false,
            Children = { this.centerCircle, this.centerLabel }
        };

        // Two rows rather than one: the top row is the gap the centre button rises into, so the
        // button can overhang the bar without being clipped by the Border that paints it.
        this.overhangRow = new RowDefinition(new GridLength(0));
        this.RowDefinitions.Add(this.overhangRow);
        this.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Grid.SetRow((BindableObject)this.barSurface, 1);
        Grid.SetRow((BindableObject)this.centerHost, 0);
        Grid.SetRowSpan((BindableObject)this.centerHost, 2);
        // The fill is driven through a probe rather than assigned to the Border directly: the theme
        // holds Colors and Border.Background takes a Brush, and a colour token cannot cross that gap
        // on its own. The probe is a real (zero-sized, invisible) element, so it resolves the token
        // and keeps resolving it across a theme swap - and having the resolved colour in hand is
        // also what makes BarBackgroundOpacity possible, since an alpha cannot be applied to a token
        // that has not become a colour yet.
        //
        // Built by hand rather than with ThemeProbe.Create, which binds the brush's Color to the
        // probe's. That binding would overwrite the alpha every time the probe resolved a colour,
        // silently turning a translucent bar opaque on the next theme pass. Here the probe only
        // reports the colour and ApplyBarFill decides what to paint.
        this.surfaceProbe = new BoxView
        {
            IsVisible = false,
            WidthRequest = 0,
            HeightRequest = 0,
            InputTransparent = true
        };

        this.Children.Add(this.barSurface);
        this.Children.Add(this.centerHost);
        this.Children.Add(this.surfaceProbe);

        this.surfaceProbe.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BoxView.Color))
                this.ApplyBarFill();
        };

        // The glass is a native view behind the surface, so it has to be re-attached every time MAUI
        // builds the surface a new platform view - which is what a re-parented page does to every
        // handler under it.
        this.barSurface.HandlerChanged += (_, _) => this.ApplyGlass();

        // A corner radius that came from a theme token resolves after the shape was handed to the
        // Border, and the glass has its own copy of the shape. Without this the pane keeps whatever
        // radius the token had not yet answered with - square corners under a rounded bar.
        if (this.barSurface.StrokeShape is RoundRectangle surfaceShape)
        {
            surfaceShape.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RoundRectangle.CornerRadius))
                    this.ApplyGlass();
            };
        }

        var centerTap = new TapGestureRecognizer();
        centerTap.Tapped += this.OnCenterTapped;
        this.centerCircle.GestureRecognizers.Add(centerTap);

        this.items.CollectionChanged += this.OnItemsCollectionChanged;

        this.VerticalOptions = LayoutOptions.End;

        this.ApplySurface();
        this.ApplyMetrics();
        // Auto overflow is a function of the bar's width, which does not exist until it has been
        // laid out - and changes on rotation, on a window resize, and when a Shell bar moves to a
        // page with different chrome.
        this.SizeChanged += (_, _) =>
        {
            this.RefreshBottomInset();
            this.OnBarSizeChanged();
        };

        // Loaded as well as SizeChanged: a bar that is measured once and never resized still needs
        // the inset it could not read from the constructor.
        this.Loaded += (_, _) => this.RefreshBottomInset();

        this.RebuildCells();

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(ShinyTabBar));
    }


    // ---------------------------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------------------------

    /// <summary>Selects the tab at <paramref name="index"/>. Returns false when out of range, hidden or disabled.</summary>
    public bool GoTo(int index)
    {
        if (index < 0 || index >= this.items.Count)
            return false;

        var item = this.items[index];
        if (!item.IsVisible || !item.IsEnabled)
            return false;

        this.SelectedIndex = index;
        return true;
    }


    /// <summary>Selects the tab with this <see cref="ShinyTabItem.Route"/>. Returns false when there is no such tab.</summary>
    public bool GoTo(string route)
    {
        for (var i = 0; i < this.items.Count; i++)
        {
            if (String.Equals(this.items[i].Route, route, StringComparison.OrdinalIgnoreCase))
                return this.GoTo(i);
        }
        return false;
    }


    /// <summary>Opens the centre menu, as pressing the button would.</summary>
    public void OpenMenu() => this.IsMenuOpen = true;

    /// <summary>Closes the centre menu.</summary>
    public void CloseMenu() => this.IsMenuOpen = false;


    // ---------------------------------------------------------------------------------------------
    // Item collection
    // ---------------------------------------------------------------------------------------------

    void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Tracked separately rather than read off the event: a Reset carries neither OldItems nor
        // NewItems, so inferring from it would leave every item still subscribed after a Clear().
        foreach (var item in this.subscribed)
        {
            item.TabChanged -= this.OnItemChanged;
            item.StructureChanged -= this.OnItemStructureChanged;
        }
        this.subscribed.Clear();

        foreach (var item in this.items)
        {
            item.TabChanged += this.OnItemChanged;
            item.StructureChanged += this.OnItemStructureChanged;
            this.subscribed.Add(item);
            SetInheritedBindingContext(item, this.BindingContext);
        }

        StyleGuard.WhenReady<ShinyTabBar>(this, bar =>
        {
            bar.RebuildCells();
            bar.ClampSelection();
        });
    }


    void OnItemChanged(object? sender, EventArgs e)
    {
        if (sender is ShinyTabItem item && this.FindCell(item) is { } cell)
        {
            this.RealizeCellIcon(cell);
            this.ApplyCellState(cell);
        }
    }


    void OnItemStructureChanged(object? sender, EventArgs e) => this.RebuildCells();


    /// <inheritdoc/>
    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        foreach (var item in this.items)
            SetInheritedBindingContext(item, this.BindingContext);
    }


    TabCell? FindCell(ShinyTabItem item) => this.cells.FirstOrDefault(c => ReferenceEquals(c.Item, item));


    // ---------------------------------------------------------------------------------------------
    // Selection
    // ---------------------------------------------------------------------------------------------

    void OnSelectedIndexChanged(int oldIndex, int newIndex)
    {
        if (this.suppressSelectionSync)
            return;

        var newItem = this.ItemAt(newIndex);

        this.suppressSelectionSync = true;
        try
        {
            this.SelectedItem = newItem;
        }
        finally
        {
            this.suppressSelectionSync = false;
        }

        this.CompleteSelection(oldIndex, newIndex, this.ItemAt(oldIndex), newItem);
    }


    void OnSelectedItemChanged(ShinyTabItem? item)
    {
        if (this.suppressSelectionSync)
            return;

        var oldIndex = this.SelectedIndex;
        var newIndex = item is null ? -1 : this.items.IndexOf(item);

        this.suppressSelectionSync = true;
        try
        {
            this.SelectedIndex = newIndex;
        }
        finally
        {
            this.suppressSelectionSync = false;
        }

        this.CompleteSelection(oldIndex, newIndex, this.ItemAt(oldIndex), item);
    }


    /// <summary>
    /// Everything a selection change does once the two properties agree, so it happens exactly once
    /// whichever of them the app wrote.
    /// </summary>
    void CompleteSelection(int oldIndex, int newIndex, ShinyTabItem? oldItem, ShinyTabItem? newItem)
    {
        if (oldIndex == newIndex && ReferenceEquals(oldItem, newItem))
            return;

        // Both menus belong to the tab they were opened over, and changing tabs swaps the page
        // underneath them - a card left standing is annotating content that is no longer there.
        // Here rather than in the tap handlers so it holds however the selection moved: a tap, the
        // overflow menu, GoTo, or a binding on SelectedIndex.
        this.IsMenuOpen = false;

        this.ApplyAllCellStates(animateIndicator: true);

        if (this.AnimateIcons && newItem is not null && this.FindCell(newItem) is { } cell)
            TabIcons.Play(cell.IconView);

        this.SelectionChanged?.Invoke(this, new TabSelectionChangedEventArgs(oldIndex, newIndex, oldItem, newItem));

        var command = this.SelectionChangedCommand;
        if (command?.CanExecute(newItem) == true)
            command.Execute(newItem);
    }


    ShinyTabItem? ItemAt(int index) => index >= 0 && index < this.items.Count ? this.items[index] : null;


    /// <summary>
    /// Pulls the selection back into range after the collection changed. A bar whose selected tab was
    /// removed reports -1 rather than silently sliding onto whatever took its place.
    /// </summary>
    void ClampSelection()
    {
        if (this.items.Count == 0)
        {
            if (this.SelectedIndex != -1)
                this.SelectedIndex = -1;
            return;
        }

        if (this.SelectedIndex >= this.items.Count)
            this.SelectedIndex = this.items.Count - 1;

        else if (this.SelectedIndex < 0 && this.items.FirstOrDefault(i => i.IsVisible && i.IsEnabled) is { } first)
            this.SelectedIndex = this.items.IndexOf(first);

        // The index survived the change but the item did not - which is the state a freshly filled
        // bar is in, because its index defaulted to 0 while it had nothing to point at. Reconciling
        // here is what makes the first tab come up selected without the app asking for it.
        else if (this.SelectedItem is null || !this.items.Contains(this.SelectedItem))
            this.SelectedItem = this.ItemAt(this.SelectedIndex);

        else
            this.ApplyAllCellStates();
    }


    void OnCellTapped(TabCell cell)
    {
        if (!cell.Item.IsEnabled)
            return;

        if (cell.IsOverflow)
        {
            this.OpenOverflow();
            return;
        }

        var index = this.items.IndexOf(cell.Item);
        if (index < 0)
            return;

        if (index == this.SelectedIndex)
        {
            // Replaying the icon is the whole visible answer to a reselect, so it happens whether or
            // not the app handles the event.
            if (this.AnimateIcons)
                TabIcons.Play(cell.IconView);

            this.TabReselected?.Invoke(this, new TabReselectedEventArgs(index, cell.Item));
            return;
        }

        this.SelectedIndex = index;
    }


    // ---------------------------------------------------------------------------------------------
    // Page context - the attached values the page contributes
    // ---------------------------------------------------------------------------------------------

    void OnPageContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Badge" or "BadgeColor" or "Title")
            this.ApplyAllCellStates();
    }


    /// <summary>The attached values for a tab, page first: a live page knows more than its stub does.</summary>
    BindableObject?[] BadgeSources(ShinyTabItem item)
    {
        var pageContext = this.PageContext;
        var isSelected = ReferenceEquals(item, this.SelectedItem);

        // A page only speaks for the tab it is showing. Reading the current page's badge onto every
        // tab is the bug this guard exists for - it put the same count on all of them.
        return isSelected
            ? [pageContext, item.PageContext, item]
            : [item.PageContext, item];
    }


    // ---------------------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------------------

    /// <summary>The one view a tab renders as, kept so a property change can repaint it in place.</summary>
    sealed class TabCell
    {
        public required ShinyTabItem Item { get; init; }
        public required Grid Root { get; init; }
        public required Border Pill { get; init; }
        public required BoxView Line { get; init; }
        public required BoxView Underline { get; init; }
        public required BoxView Dot { get; init; }
        public required BadgeView Badge { get; init; }
        public required Label Label { get; init; }
        public required Grid IconArea { get; init; }
        public required VerticalStackLayout Stack { get; init; }
        public View? IconView { get; set; }

        /// <summary>
        /// The synthesized <b>More</b> cell rather than one of the bar's own tabs. It looks like a
        /// tab and is built by the same code, but it selects nothing — it opens the overflow menu.
        /// </summary>
        public bool IsOverflow { get; init; }

        /// <summary>
        /// Null until the cell has been styled once. Nullable rather than false so the very first
        /// pass can settle the cell without animating every tab in the bar on launch.
        /// </summary>
        public bool? WasSelected { get; set; }
    }


    /// <summary>
    /// How the tab columns divide around the centre button: how many go left of it, how many go
    /// right, and how many empty columns pad the short side.
    /// </summary>
    /// <remarks>
    /// The padding is the whole point. The centre button is positioned by centring it in the bar, so
    /// it is only actually centred over its column when the star weight either side of that column
    /// is equal. An odd number of tabs splits unevenly, and without a spacer the button drifts by
    /// half a tab - which nobody notices until they build a five-tab bar.
    /// </remarks>
    internal static (int Left, int Right, int Spacers) SplitColumns(int visibleCount, bool hasCenter)
    {
        if (!hasCenter)
            return (visibleCount, 0, 0);

        var left = (visibleCount + 1) / 2;
        var right = visibleCount - left;
        return (left, right, left - right);
    }


    /// <summary>The grid the tab cells live in. For tests; the layout is not part of the contract.</summary>
    internal Grid BarLayout => this.barGrid;

    /// <summary>The border that paints the bar's background. For tests.</summary>
    internal Border Surface => this.barSurface;

    /// <summary>The element the bar's colour token resolves on. For tests.</summary>
    internal BoxView SurfaceProbe => this.surfaceProbe;

    /// <summary>
    /// Test seam: runs the tap handler for the cell at <paramref name="index"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="TapGestureRecognizer.Tapped"/> cannot be raised from outside the recognizer, so
    /// there is no way to reach a cell's behaviour through its gesture. This is the same entry point
    /// the gesture uses and nothing more.
    /// </remarks>
    internal void TapCellAt(int index) => this.OnCellTapped(this.cells[index]);

    /// <summary>The centre button's own view, so a test can assert it is (or is not) there.</summary>
    internal View CenterHost => this.centerHost;


    void ApplyMetrics()
    {
        var overhang = this.CenterButton is { } center ? center.EffectiveOverhang : 0d;
        this.overhangRow.Height = new GridLength(Math.Max(0, overhang));

        // The safe-area inset is extra height, not a squeeze. BarHeight is the room the tabs get,
        // and the strip below them is on top of it - pinning the surface to BarHeight and padding
        // the inset in would crush the tabs into a fixed box instead of lifting them out of the
        // home indicator, which is exactly what it looked like.
        this.barSurface.HeightRequest = this.BarHeight + this.bottomInset;
    }


    void ApplySurface()
    {
        ThemeProbe.Tint(this.surfaceProbe, BoxView.ColorProperty, this.BarBackgroundColor, ShinyThemeKeys.Color.SurfaceContainer);
        this.ApplyBarFill();

        var floating = this.BarStyle == TabBarStyle.Floating;

        if (this.barSurface.StrokeShape is RoundRectangle shape)
        {
            // A floating bar is a capsule unless it was given a radius of its own. Half the bar's
            // height rather than a token: the token is a fixed radius, and on a capsule the radius
            // has to track the height or the ends stop being semicircles.
            if (floating && !ThemeTokens.IsSet(this.BarCornerRadius))
                shape.CornerRadius = new CornerRadius(this.BarHeight / 2);
            else
                shape.SetCornerTokenOrValue(this.BarCornerRadius, ShinyThemeKeys.Shape.CornerNoneRadius);
        }

        // Zero reads as "not set" for a floating bar: a capsule welded to the page edges is not a
        // look anyone asks for, whereas a docked bar's zero margin is exactly what it wants.
        this.barSurface.Margin = floating && this.BarMargin == default ? FloatingMargin : this.BarMargin;

        // The bottom inset is padded in, not declared with SafeAreaEdges. Declaring it does nothing
        // here: the bar is laid out inside a chain of Auto-sized rows and the inset arrives as zero,
        // which is indistinguishable from a device that has no home indicator. Padding also gets the
        // geometry right on both counts at once - the bar grows by the inset and is bottom-anchored,
        // so the background reaches the screen edge, and the tabs inside are lifted clear of it.
        // A floating bar is excluded: it lifts its whole capsule instead, a level up.
        this.bottomInset = !floating && this.RespectSafeArea ? SafeAreaInsets.Bottom : 0;
        this.barSurface.Padding = this.BarPadding + new Thickness(0, 0, 0, this.bottomInset);

        // The surface's height carries the inset too, and it is ApplyMetrics that owns it.
        this.ApplyMetrics();

        if (floating)
        {
            // The inset moves up a level. The bar arranges its child out of the home indicator so
            // the capsule clears it; the capsule itself must not extend into it, or the thing that
            // makes it read as floating - the gap under it - is painted over.
            this.SafeAreaEdges = this.RespectSafeArea ? ContainerSafeArea : SafeAreaEdges.None;
            this.barSurface.SafeAreaEdges = SafeAreaEdges.None;
        }
        else
        {
            // Nothing in the docked chain declares an inset: the padding above is the single place
            // it is expressed. Leaving Container on the surface double-counts it - now that the
            // surface genuinely reaches the bottom of the screen, its own inset finally fires and
            // takes another 34pt out of the tab row, squeezing the cells to 16pt and clipping every
            // icon against the top edge.
            this.SafeAreaEdges = SafeAreaEdges.None;
            this.barSurface.SafeAreaEdges = SafeAreaEdges.None;
        }

        // Glass brings its own edge shading, and a Material drop shadow under it reads as a sticker
        // laid on the page rather than as depth. HasShadow is honoured again the moment the material
        // goes back to Solid, or the app runs somewhere with no glass.
        if (this.HasShadow && !this.GlassActive)
            // A floating bar is detached from the page, so it casts the heavier of the two shadows -
            // Level2 against nothing underneath it barely reads as a shadow at all.
            this.barSurface.WithElevation(floating ? ShinyThemeKeys.Elevation.Level3 : ShinyThemeKeys.Elevation.Level2);
        else
            // Not ClearValue: the dynamic resource WithElevation left behind survives it and puts the
            // shadow straight back, which is why HasShadow="False" used to do nothing at all.
            this.barSurface.WithoutElevation();

        this.ApplyGlass();
    }


    /// <summary>
    /// Whether this bar is actually sitting on glass — asked for <em>and</em> available.
    /// </summary>
    /// <remarks>
    /// Every consequence of glass hangs off this rather than off <see cref="BarMaterial"/> alone: the
    /// fill goes transparent, the shadow comes off, and the page lets its content run underneath.
    /// Doing any of that on a head with no glass to show would leave a bar that is simply not there.
    /// </remarks>
    internal bool GlassActive => this.BarMaterial != TabBarMaterial.Solid && GlassSurface.IsSupported;


    /// <summary>
    /// Attaches, updates or removes the pane of glass behind the bar's surface.
    /// </summary>
    /// <remarks>
    /// Called from <see cref="ApplySurface"/> (which owns the shape and the margins the glass has to
    /// match), when the surface gets a new handler, and when a token-driven corner radius finally
    /// resolves. All three are cheap and idempotent - the pane is reused, never stacked.
    /// </remarks>
    void ApplyGlass()
    {
        var wasActive = this.glassActive;
        this.glassActive = this.GlassActive;

        if (!this.glassActive)
        {
            GlassSurface.Remove(this.barSurface);
        }
        else
        {
            // A floating bar with no radius of its own is a capsule, and a capsule tracks the bar's
            // height natively - which matters here, because the radius ApplySurface wrote onto the
            // shape is only correct for the height the bar had when it ran.
            var floating = this.BarStyle == TabBarStyle.Floating;
            var capsule = floating && !ThemeTokens.IsSet(this.BarCornerRadius);
            var radius = this.barSurface.StrokeShape is RoundRectangle shape ? shape.CornerRadius.TopLeft : 0;

            GlassSurface.Apply(this.barSurface, new GlassSurfaceOptions(
                Clear: this.BarMaterial == TabBarMaterial.GlassClear,
                Tint: this.BarGlassTint,
                CornerRadius: radius,
                Capsule: capsule
            ));
        }

        // Whether the content runs under the bar is the page's business, and glass changes the answer
        // the same way BarStyle does - so it goes out on the same event rather than a second one.
        if (wasActive != this.glassActive)
            this.BarPlacementChanged?.Invoke(this, EventArgs.Empty);
    }


    /// <summary>
    /// Paints the bar's background: the resolved colour, dimmed by <see cref="BarBackgroundOpacity"/>.
    /// </summary>
    /// <remarks>
    /// <para>The alpha goes on the <em>colour</em>, never on a view. Setting <c>Opacity</c> on the
    /// surface would fade everything inside it too — a translucent bar whose icons and labels are
    /// also half gone is not a translucent bar, it is a faded one — and opacity multiplies down the
    /// tree, so a child cannot undo it.</para>
    /// <para>Called again whenever the probe resolves a new colour, which is how a theme swap keeps
    /// its transparency instead of snapping back to opaque.</para>
    /// </remarks>
    void ApplyBarFill()
    {
        // Under glass the surface paints nothing at all: any fill it painted would sit between the
        // glass and the page and there would be nothing left to refract. The tint that does colour a
        // glass bar is BarGlassTint, which goes to the effect rather than to a brush.
        if (this.GlassActive)
        {
            this.barSurface.Background = new SolidColorBrush(Colors.Transparent);
            return;
        }

        var color = this.surfaceProbe.Color;
        if (color is null)
            return;

        // Multiplied into whatever alpha the colour already carried rather than replacing it, so a
        // consumer who handed over a semi-transparent BarBackgroundColor keeps what they asked for.
        var opacity = Math.Clamp(this.BarBackgroundOpacity, 0, 1);

        // A *new* brush every time, rather than mutating one the Border already holds. Android's
        // handler maps Background when the property changes, not when the brush it is already
        // holding changes its Color underneath it - so mutating in place left the bar painted with
        // the old colour until some unrelated property happened to re-assign Background. Which is
        // exactly what "the transparency button does nothing until I change something else" was.
        this.barSurface.Background = new SolidColorBrush(color.WithAlpha((float)(color.Alpha * opacity)));
    }


    /// <summary>
    /// Every edge in <see cref="SafeAreaRegions.Container"/> mode. Built rather than taken from
    /// <c>SafeAreaEdges.Container</c>, which MAUI keeps internal.
    /// </summary>
    static readonly SafeAreaEdges ContainerSafeArea = new(SafeAreaRegions.Container);


    /// <summary>
    /// What a <see cref="TabBarStyle.Floating"/> bar insets itself by when the consumer has not set
    /// <see cref="BarMargin"/>. No top inset - the gap above the bar is the content showing through,
    /// not a margin.
    /// </summary>
    static readonly Thickness FloatingMargin = new(16, 0, 16, 8);


    /// <summary>
    /// Raised when something that decides where the page's content may go changes — the bar's
    /// <see cref="BarStyle"/>, or whether it ended up on glass. The bar can restyle itself, but whether the
    /// content stops above it or runs underneath it is the host page's layout to change.
    /// </summary>
    internal event EventHandler? BarPlacementChanged;


    void ApplyBarStyle()
    {
        this.ApplySurface();
        this.ApplyMetrics();
        this.BarPlacementChanged?.Invoke(this, EventArgs.Empty);
    }


    void RebuildCells()
    {
        this.barGrid.Children.Clear();
        this.barGrid.ColumnDefinitions.Clear();
        this.cells.Clear();

        var center = this.CenterButton;
        var (shown, overflowed) = this.SplitForOverflow(this.items.Where(i => i.IsVisible).ToList());
        this.overflowed = overflowed;

        // The overflow tab occupies a column like any other, so the whole layout below - the centre
        // button's split, the spacers, the indicator's span - counts it and needs no special case.
        var visible = overflowed.Count == 0 ? shown : shown.Append(this.OverflowItem).ToList();
        var (left, _, spacers) = SplitColumns(visible.Count, center is not null);

        var totalColumns = visible.Count + (center is null ? 0 : 1) + spacers;
        for (var i = 0; i < totalColumns; i++)
            this.barGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        if (center is not null)
        {
            var centerColumn = this.barGrid.ColumnDefinitions[left];
            centerColumn.Width = new GridLength(center.Size + 16);
        }

        for (var i = 0; i < visible.Count; i++)
        {
            var cell = this.BuildCell(visible[i], ReferenceEquals(visible[i], this.overflowItem));
            var column = center is null || i < left ? i : i + 1;
            Grid.SetColumn(cell.Root, column);
            this.barGrid.Children.Add(cell.Root);
            this.cells.Add(cell);
        }

        // Re-added and re-spanned because RebuildCells clears the grid it lives in.
        this.barGrid.Children.Insert(0, this.travelIndicator);
        Grid.SetColumn((BindableObject)this.travelIndicator, 0);
        Grid.SetColumnSpan((BindableObject)this.travelIndicator, Math.Max(1, totalColumns));

        this.ApplyCenterButton();
        this.ApplyMetrics();
        this.ApplyAllCellStates();
    }


    // ---------------------------------------------------------------------------------------------
    // Overflow
    // ---------------------------------------------------------------------------------------------

    ShinyTabItem? overflowItem;
    IReadOnlyList<ShinyTabItem> overflowed = Array.Empty<ShinyTabItem>();
    int lastAutoMax;

    /// <summary>
    /// The synthesized <b>More</b> tab. Built once and kept: it is handed to <see cref="BuildCell"/>
    /// like any other item, and a fresh one per rebuild would drop its icon's playback state.
    /// </summary>
    ShinyTabItem OverflowItem
    {
        get
        {
            this.overflowItem ??= new ShinyTabItem { Route = "more" };
            this.overflowItem.Title = this.OverflowTitle;
            this.overflowItem.Icon = this.OverflowIcon;
            return this.overflowItem;
        }
    }

    /// <summary>The tabs currently folded away behind the overflow tab. Empty when they all fit.</summary>
    public IReadOnlyList<ShinyTabItem> OverflowItems => this.overflowed;

    /// <summary>Whether the bar is currently drawing an overflow tab.</summary>
    public bool HasOverflow => this.overflowed.Count > 0;

    /// <summary>Opens the overflow menu, as tapping the <b>More</b> tab does. No-op when nothing is folded away.</summary>
    public void OpenOverflow()
    {
        if (!this.HasOverflow)
            return;

        this.menuKind = TabMenuKind.Overflow;
        this.IsMenuOpen = true;
    }


    /// <summary>Selects a tab that is currently folded away and closes the menu.</summary>
    /// <remarks>
    /// Closed before selecting, not after: selecting swaps the page under the card, and a menu still
    /// animating over the page it was opened from reads as the tap not having worked.
    /// </remarks>
    public void SelectOverflowItem(ShinyTabItem item)
    {
        if (!item.IsEnabled)
            return;

        this.IsMenuOpen = false;

        var index = this.items.IndexOf(item);
        if (index >= 0)
            this.SelectedIndex = index;
    }


    /// <summary>
    /// Whether the <b>More</b> cell is drawing as the selected tab — which it does whenever the tab
    /// that is showing is one of the ones it folded away.
    /// </summary>
    public bool IsOverflowCellSelected
        => this.overflowed.Any(i => ReferenceEquals(i, this.SelectedItem) || this.items.IndexOf(i) == this.SelectedIndex);

    /// <summary>
    /// Divides the visible tabs into the ones the bar will draw and the ones that fold away.
    /// </summary>
    /// <remarks>
    /// The cap counts the overflow tab itself, so the drawn tabs are one fewer than the cap whenever
    /// anything is folded - otherwise adding the <b>More</b> cell would push the bar right back over
    /// the width that caused it.
    /// </remarks>
    (List<ShinyTabItem> Shown, IReadOnlyList<ShinyTabItem> Overflowed) SplitForOverflow(List<ShinyTabItem> visible)
    {
        var max = this.EffectiveMaxVisibleTabs;
        if (max <= 0 || visible.Count <= max)
            return (visible, Array.Empty<ShinyTabItem>());

        var keep = max - 1;
        return (visible.Take(keep).ToList(), visible.Skip(keep).ToList());
    }

    /// <summary>
    /// How many cells fit: what the consumer asked for, or what the bar's own width allows.
    /// </summary>
    /// <remarks>
    /// Zero means "no limit", which is also the honest answer before the bar has been measured - a
    /// width of zero would otherwise compute a cap of zero and fold every tab away on the frame
    /// before layout. A width that admits everything is left uncapped rather than capped at the tab
    /// count, so <see cref="HasOverflow"/> stays false.
    /// </remarks>
    internal int EffectiveMaxVisibleTabs
    {
        get
        {
            if (this.MaxVisibleTabs > 0)
                return Math.Max(2, this.MaxVisibleTabs);

            var available = this.barGrid.Width > 0 ? this.barGrid.Width : this.Width;
            if (available <= 0 || this.MinTabWidth <= 0)
                return 0;

            // The centre button owns a fixed column that no tab can use.
            if (this.CenterButton is { } center)
                available -= center.Size + 16;

            return Math.Max(2, (int)(available / this.MinTabWidth));
        }
    }

    /// <summary>
    /// Re-splits the tabs when the width the split was computed from has changed enough to change it.
    /// </summary>
    /// <remarks>
    /// Guarded on the computed cap rather than on the width. Rebuilding on every size change would
    /// run on every frame of a rotation, and - because a rebuild changes the bar's own desired size -
    /// is a plausible way to make layout oscillate. Comparing the answer means a resize that does not
    /// change how many tabs fit costs nothing at all.
    /// </remarks>
    void OnBarSizeChanged()
    {
        if (this.MaxVisibleTabs > 0)
            return;

        var max = this.EffectiveMaxVisibleTabs;
        if (max == this.lastAutoMax)
            return;

        this.lastAutoMax = max;
        this.RebuildCells();
    }


    /// <summary>
    /// Re-pads the surface when the window's bottom inset has changed under it.
    /// </summary>
    /// <remarks>
    /// The inset is read off the window, and there is no window when the constructor runs - so the
    /// first pass always computes zero and the tabs sit under the home indicator until something
    /// else happens to restyle the bar. It also changes on rotation, and when a Shell bar moves to a
    /// page with different chrome. Guarded on the value so the re-apply, which itself resizes the
    /// bar, cannot chase its own SizeChanged.
    /// </remarks>
    void RefreshBottomInset()
    {
        var floating = this.BarStyle == TabBarStyle.Floating;
        var inset = !floating && this.RespectSafeArea ? SafeAreaInsets.Bottom : 0;

        if (Math.Abs(inset - this.bottomInset) < 0.5)
            return;

        this.ApplySurface();
    }


    TabCell BuildCell(ShinyTabItem item, bool isOverflow = false)
    {
        var pill = new Border
        {
            StrokeThickness = 0,
            Stroke = null,
            InputTransparent = true,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerFullRadius)
        };

        var iconArea = new Grid();
        var badge = new BadgeView
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            OffsetX = 6,
            OffsetY = -6
        };
        iconArea.Children.Add(pill);
        iconArea.Children.Add(badge);

        var label = new Label
        {
            HorizontalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };

        var dot = new BoxView
        {
            WidthRequest = DotSize,
            HeightRequest = DotSize,
            CornerRadius = DotSize / 2,
            HorizontalOptions = LayoutOptions.Center,
            IsVisible = false
        };

        var stack = new VerticalStackLayout
        {
            Spacing = 2,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
            Children = { iconArea, label, dot }
        };

        var line = new BoxView
        {
            WidthRequest = IndicatorBarWidth,
            HeightRequest = IndicatorBarHeight,
            CornerRadius = IndicatorBarHeight / 2,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Start,
            IsVisible = false
        };

        var underline = new BoxView
        {
            WidthRequest = IndicatorBarWidth,
            HeightRequest = IndicatorBarHeight,
            CornerRadius = IndicatorBarHeight / 2,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            IsVisible = false
        };

        var root = new Grid
        {
            // A transparent background rather than none: an untouched Grid has no hit area on
            // Android, so the gaps between icons would swallow taps that plainly landed on the tab.
            BackgroundColor = Colors.Transparent,
            Children = { line, underline, stack }
        };

        // Assigned once, here, and never again: AutomationId is set-once in MAUI and throws on a
        // second write. Cells are rebuilt as whole new Grids rather than re-labelled, so a tab that
        // is renamed or reordered still gets a correct id without anything being reassigned.
        if (AutomationIdFor(item) is { } automationId)
            root.AutomationId = automationId;

        var cell = new TabCell
        {
            Item = item,
            IsOverflow = isOverflow,
            Root = root,
            Pill = pill,
            Line = line,
            Underline = underline,
            Dot = dot,
            Badge = badge,
            Label = label,
            IconArea = iconArea,
            Stack = stack
        };

        // Cheap, and the only reliable signal that geometry exists: a travelling indicator cannot be
        // placed until the cell it belongs to has been arranged, and that happens well after the
        // cell is built.
        root.SizeChanged += (_, _) => this.LayoutTravelIndicator(animate: false);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => this.OnCellTapped(cell);
        root.GestureRecognizers.Add(tap);

        this.RealizeCellIcon(cell);
        return cell;
    }


    /// <summary>
    /// The <c>AutomationId</c> a tab's cell carries, so UI tests and DevFlow can address a tab by
    /// name instead of by coordinates or tree position.
    /// </summary>
    /// <remarks>
    /// <see cref="ShinyTabItem.Route"/> first because it is the identity an app already chose and
    /// the one that survives a renamed label; <see cref="ShinyTabItem.Title"/> is the fallback, and
    /// a tab with neither gets none rather than an index-based id that would silently point at a
    /// different tab the moment the order changed.
    /// </remarks>
    internal static string? AutomationIdFor(ShinyTabItem item)
    {
        var name = item.Route ?? item.Title;
        return String.IsNullOrWhiteSpace(name) ? null : "tab-" + name.Trim().ToLowerInvariant().Replace(' ', '-');
    }


    void RealizeCellIcon(TabCell cell)
    {
        var icon = TabIcons.Realize(cell.Item, cell.IconView, this.IconSize);
        if (ReferenceEquals(icon, cell.IconView))
            return;

        cell.IconView = icon;
        cell.Badge.Content = icon;
    }


    /// <summary>
    /// Restyles every cell. <paramref name="animateIndicator"/> is what separates a selection change
    /// from a restyle: a restyle must snap the travelling indicator, or changing a badge would send
    /// it gliding across the bar for no reason.
    /// </summary>
    void ApplyAllCellStates(bool animateIndicator = false)
    {
        foreach (var cell in this.cells)
            this.ApplyCellState(cell);

        this.ApplyCenterButton();
        this.LayoutTravelIndicator(animateIndicator);
    }


    void ApplyCellState(TabCell cell)
    {
        var item = cell.Item;

        // The overflow cell selects nothing of its own, so it reads its state from the tabs behind
        // it: it is the selected tab whenever the selected tab is one of the ones it folded away.
        // Without this, choosing a tab from the menu would leave the whole bar looking unselected.
        var selected = cell.IsOverflow
            ? this.IsOverflowCellSelected
            : ReferenceEquals(item, this.SelectedItem) || this.items.IndexOf(item) == this.SelectedIndex;

        // ---- colour ----
        var color = selected ? this.SelectedColor : this.UnselectedColor;
        var token = selected ? ShinyThemeKeys.Color.Primary : ShinyThemeKeys.Color.OnSurfaceVariant;

        TabIcons.Tint(cell.IconView, color, token);

        if (color is not null)
            cell.Label.TextColor = color;
        else
            cell.Label.SetDynamicResource(Label.TextColorProperty, token);

        cell.Label.FontSize = this.FontSize;
        cell.Label.FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None;
        cell.Label.Text = ShinyTabs.Resolve<string>(ShinyTabs.TitleProperty, this.TitleSources(item)) ?? item.Title;

        // An image icon carries no tint, so the selected/unselected difference has to be opacity.
        // Motion icons recolour instead and stay fully opaque.
        var dimmed = !item.IsEnabled;
        cell.Root.Opacity = dimmed ? 0.4 : 1;
        if (cell.IconView is Image image)
            image.Opacity = selected ? 1 : 0.6;

        cell.Label.IsVisible = this.LabelMode switch
        {
            TabLabelMode.Always => !String.IsNullOrEmpty(cell.Label.Text),
            TabLabelMode.SelectedOnly => selected && !String.IsNullOrEmpty(cell.Label.Text),
            _ => false
        };

        // ---- indicator ----
        var style = this.IndicatorStyle;
        var indicatorColor = this.IndicatorColor;

        // Exactly one of the two mechanisms draws at a time. The per-cell views stay built either
        // way - they are what the bar falls back to before it has been laid out, and rebuilding them
        // on every geometry change would defeat the point.
        var perCell = selected && !this.IsTravelling;

        cell.Pill.IsVisible = style == TabIndicatorStyle.Pill && perCell;
        cell.Line.IsVisible = style == TabIndicatorStyle.Line && perCell;
        cell.Underline.IsVisible = style == TabIndicatorStyle.Underline && perCell;
        cell.Dot.IsVisible = style == TabIndicatorStyle.Dot && perCell;

        cell.Pill.WidthRequest = this.IconSize * PillWidthFactor;
        cell.Pill.HeightRequest = this.IconSize + PillHeightPadding;

        if (indicatorColor is not null)
        {
            cell.Pill.Background = new SolidColorBrush(indicatorColor);
            cell.Line.Color = indicatorColor;
            cell.Underline.Color = indicatorColor;
            cell.Dot.Color = indicatorColor;
        }
        else
        {
            cell.Pill.Background = ThemeTokens.TokenBrush(ShinyThemeKeys.Color.SecondaryContainer);

            // BoxView on AppKit paints from Color and ignores Background/BackgroundColor entirely,
            // so the indicator bars are driven through Color on every head rather than only where it
            // is required - one code path, and one that is right everywhere.
            cell.Line.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.Primary);
            cell.Underline.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.Primary);
            cell.Dot.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.Primary);
        }

        // ---- badge ----
        var badge = ShinyTabs.Resolve<string>(ShinyTabs.BadgeProperty, this.BadgeSources(item)) ?? item.Badge;
        var badgeColor = ShinyTabs.Resolve<Color>(ShinyTabs.BadgeColorProperty, this.BadgeSources(item)) ?? item.BadgeColor;

        cell.Badge.IsDot = badge is { Length: 0 };
        cell.Badge.Text = badge ?? String.Empty;
        cell.Badge.BadgeColor = badgeColor;

        // ---- animation ----
        // Only on a genuine change of selected state. ApplyCellState also runs for a badge update, a
        // colour change and every rebuild, and replaying the animation for those would have tabs
        // twitching at things the user never did.
        var previous = cell.WasSelected;
        cell.WasSelected = selected;

        if (previous != selected)
            // The first pass settles the cell without animating - otherwise every tab in the bar
            // plays its deselect animation the moment the page opens.
            this.AnimateCell(cell, selected, animate: previous is not null);
    }


    void AnimateCell(TabCell cell, bool selected, bool animate)
    {
        var context = new TabAnimationContext
        {
            Item = cell.Item,
            Cell = cell.Root,
            Icon = cell.IconView,
            Label = cell.Label,
            Indicator = this.IndicatorFor(cell),
            IsSelected = selected,
            Duration = animate ? this.AnimationDuration : 0,
            Bar = this
        };

        var animator = this.Animator ?? new DefaultTabAnimator(this.SelectionAnimation);
        _ = animator.AnimateAsync(context);
    }


    /// <summary>The view the current <see cref="IndicatorStyle"/> actually draws, for the animator.</summary>
    View? IndicatorFor(TabCell cell)
    {
        if (this.IsTravelling)
            return this.travelIndicator;

        return this.IndicatorStyle switch
        {
            TabIndicatorStyle.Pill => cell.Pill,
            TabIndicatorStyle.Line => cell.Line,
            TabIndicatorStyle.Underline => cell.Underline,
            TabIndicatorStyle.Dot => cell.Dot,
            _ => null
        };
    }


    // ---------------------------------------------------------------------------------------------
    // The travelling indicator
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// True when one shared indicator is doing the work, rather than one drawn inside each cell.
    /// </summary>
    /// <remarks>
    /// Requires geometry. Sliding is positioned from measured cell bounds, and before the first
    /// layout pass those are all zero - a shared indicator would spend the first frame parked in the
    /// corner at zero width. Falling back to the per-cell drawing until the bar has been arranged
    /// costs one collapsed view per tab and is right from the very first frame.
    /// </remarks>
    bool IsTravelling
        => this.IndicatorTransition == TabIndicatorTransition.Slide
           && this.IndicatorStyle != TabIndicatorStyle.None
           && this.SelectedCell() is { Root.Width: > 0 };


    TabCell? SelectedCell()
    {
        var item = this.SelectedItem ?? this.ItemAt(this.SelectedIndex);
        return item is null ? null : this.FindCell(item);
    }


    /// <summary>The indicator's size for a style, which is the same on every tab.</summary>
    internal static Size IndicatorSizeFor(TabIndicatorStyle style, double iconSize) => style switch
    {
        TabIndicatorStyle.Pill => new Size(iconSize * PillWidthFactor, iconSize + PillHeightPadding),
        TabIndicatorStyle.Line or TabIndicatorStyle.Underline => new Size(IndicatorBarWidth, IndicatorBarHeight),
        TabIndicatorStyle.Dot => new Size(DotSize, DotSize),
        _ => Size.Zero
    };


    /// <summary>
    /// Where the indicator sits over a cell, in the bar grid's own coordinates.
    /// </summary>
    /// <remarks>
    /// Built up from the cell's children rather than from the cell box, because each style anchors
    /// to a different thing: a pill sits behind the icon, a dot under the label, and the two bars on
    /// the cell's own edges. Taking a pill's centre from the cell would drop it behind the label
    /// whenever labels are showing.
    /// </remarks>
    /// <param name="cell">The cell's box, in the bar grid's coordinates.</param>
    /// <param name="stack">The icon/label stack's box, relative to the cell.</param>
    /// <param name="iconArea">The icon's box, relative to the stack.</param>
    internal static Point IndicatorOriginFor(TabIndicatorStyle style, Rect cell, Rect stack, Rect iconArea, Size size)
    {
        var centreX = cell.X + ((cell.Width - size.Width) / 2);

        return style switch
        {
            TabIndicatorStyle.Line => new Point(centreX, cell.Y),
            TabIndicatorStyle.Underline => new Point(centreX, cell.Bottom - size.Height),
            TabIndicatorStyle.Dot => new Point(centreX, cell.Y + stack.Bottom - size.Height),
            _ => new Point(centreX, cell.Y + stack.Y + iconArea.Y + ((iconArea.Height - size.Height) / 2))
        };
    }


    void LayoutTravelIndicator(bool animate)
    {
        if (!this.IsTravelling || this.SelectedCell() is not { } cell)
        {
            this.travelIndicator.IsVisible = false;
            return;
        }

        var style = this.IndicatorStyle;
        var size = IndicatorSizeFor(style, this.IconSize);
        var origin = IndicatorOriginFor(style, cell.Root.Bounds, cell.Stack.Bounds, cell.IconArea.Bounds, size);

        this.travelIndicator.WidthRequest = size.Width;
        this.travelIndicator.HeightRequest = size.Height;

        // Fully rounded whatever the style: a pill is a capsule, and the bars and the dot are all
        // shorter than they are wide or perfectly square, so half the height is right for each.
        if (this.travelIndicator.StrokeShape is RoundRectangle shape)
            shape.CornerRadius = new CornerRadius(size.Height / 2);

        this.ApplyIndicatorPaint(this.travelIndicator);

        var first = !this.travelIndicator.IsVisible;
        this.travelIndicator.IsVisible = true;

        // A first appearance has nowhere to travel from, and neither does a bar with no animation
        // manager behind it.
        if (first || !animate || this.AnimationDuration == 0 || this.Handler is null)
        {
            this.travelIndicator.TranslationX = origin.X;
            this.travelIndicator.TranslationY = origin.Y;
            return;
        }

        _ = this.travelIndicator.TranslateToAsync(origin.X, origin.Y, this.AnimationDuration, this.IndicatorEasing ?? Easing.CubicInOut);
    }


    /// <summary>Paints an indicator view, explicit colour first and the theme token otherwise.</summary>
    void ApplyIndicatorPaint(Border indicator)
    {
        if (this.IndicatorColor is { } color)
            indicator.Background = new SolidColorBrush(color);
        else
            indicator.Background = ThemeTokens.TokenBrush(this.IndicatorStyle == TabIndicatorStyle.Pill
                ? ShinyThemeKeys.Color.SecondaryContainer
                : ShinyThemeKeys.Color.Primary);
    }


    BindableObject?[] TitleSources(ShinyTabItem item) => this.BadgeSources(item);


    // ---------------------------------------------------------------------------------------------
    // Centre button
    // ---------------------------------------------------------------------------------------------

    void OnCenterButtonChanged(object? sender, PropertyChangedEventArgs e)
        // Only the handful that move the columns or the overhang row earn a rebuild; recolouring the
        // button would otherwise throw away and re-create every tab cell in the bar.
        => StyleGuard.WhenReady<ShinyTabBar>(this, bar =>
        {
            if (TabCenterButton.AffectsLayout(e.PropertyName))
                bar.RebuildCells();
            else
                bar.ApplyCenterButton();
        });

    void OnCenterActionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Rows are read when the menu opens, so a change while it is closed needs nothing. A change
        // while it is open has to be rendered, or the row someone just added is invisible until the
        // next open.
        if (this.IsMenuOpen)
            StyleGuard.WhenReady<ShinyTabBar>(this, bar => bar.RefreshMenuContent());
    }


    void ApplyCenterButton()
    {
        var center = this.CenterButton;
        this.centerHost.IsVisible = center is { IsVisible: true };

        if (center is null)
        {
            this.centerIconView = null;
            this.centerIconHost.Children.Clear();
            this.centerCircle.Content = this.centerIconHost;
            return;
        }

        // A template takes over the whole visual. The bar keeps the press, the overhang and the
        // column - Size is the space the template is handed, not a circle it has to draw.
        if (center.ContentTemplate is { } template)
        {
            this.ApplyTemplatedCenterButton(center, template);
            return;
        }

        this.centerCircle.Content = this.centerIconHost;
        this.centerCircle.WidthRequest = center.Size;
        this.centerCircle.HeightRequest = center.Size;
        if (this.centerCircle.StrokeShape is RoundRectangle shape)
            shape.CornerRadius = new CornerRadius(center.Size / 2);

        if (center.BackgroundColor is { } background)
            this.centerCircle.Background = new SolidColorBrush(background);
        else
            this.centerCircle.Background = ThemeTokens.TokenBrush(ShinyThemeKeys.Color.Primary);

        this.centerCircle.Opacity = center.IsEnabled ? 1 : 0.5;
        this.centerCircle.WithElevation(ShinyThemeKeys.Elevation.Level3);

        var icon = TabIcons.Realize(center, this.centerIconView, center.IconSize);
        if (!ReferenceEquals(icon, this.centerIconView))
        {
            this.centerIconView = icon;
            this.centerIconHost.Children.Clear();
            if (icon is not null)
                this.centerIconHost.Children.Add(icon);
        }
        TabIcons.Tint(icon, center.ForegroundColor, ShinyThemeKeys.Color.OnPrimary);

        this.centerLabel.Text = center.Text;
        this.centerLabel.IsVisible = !String.IsNullOrEmpty(center.Text);
        this.centerLabel.FontSize = this.FontSize;
        if (this.UnselectedColor is { } labelColor)
            this.centerLabel.TextColor = labelColor;
        else
            this.centerLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        this.ApplyMetrics();
    }


    void ApplyTemplatedCenterButton(TabCenterButton center, DataTemplate template)
    {
        this.centerIconView = null;
        this.centerIconHost.Children.Clear();

        var content = template.CreateContent() as View;
        if (content is not null)
            SetInheritedBindingContext(content, center);

        // Everything the default look sets is cleared, or the template paints on top of a circle it
        // never asked for.
        this.centerCircle.Content = content;
        this.centerCircle.Background = null;
        this.centerCircle.WithoutElevation();
        this.centerCircle.WidthRequest = center.Size;
        this.centerCircle.HeightRequest = center.Size;
        this.centerCircle.Opacity = center.IsEnabled ? 1 : 0.5;

        if (this.centerCircle.StrokeShape is RoundRectangle shape)
            shape.CornerRadius = new CornerRadius(0);

        this.centerLabel.IsVisible = false;
        this.ApplyMetrics();
    }


    void OnCenterTapped(object? sender, TappedEventArgs e)
    {
        var center = this.CenterButton;
        if (center is null || !center.IsEnabled)
            return;

        if (this.IsMenuOpen)
        {
            this.IsMenuOpen = false;
            return;
        }

        var args = new TabCenterClickedEventArgs();
        this.CenterClicked?.Invoke(this, args);
        center.Invoke();

        if (args.Cancel || center.Mode == TabCenterMode.Action)
            return;

        // Falls back to being a plain button when nothing anywhere has anything to present. A centre
        // button that opens an empty card is worse than one that just does its job.
        if (this.HasMenuToShow())
        {
            this.menuKind = TabMenuKind.Center;
            this.IsMenuOpen = true;
        }
    }


    // ---------------------------------------------------------------------------------------------
    // Centre menu
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The surface a <see cref="ShinyTabBar"/> paints its centre menu onto. Implemented by
    /// <see cref="ShinyTabbedPage"/>, which builds the layer into its own tree.
    /// </summary>
    /// <remarks>
    /// Without a host the bar falls back to <see cref="PageOverlay"/>, which wraps the page's content
    /// in a grid to make room for the layer. That re-parent rebuilds every native view underneath it,
    /// so a host that can offer a layer up front should.
    /// </remarks>
    internal interface ITabMenuHost
    {
        Layout GetTabMenuLayer();
    }


    /// <summary>Which of the two menus <see cref="IsMenuOpen"/> is currently presenting.</summary>
    /// <remarks>
    /// One flag, one card and one animation drive both. They are never open at once - a menu is
    /// modal over a backdrop - so the alternative would be a second copy of the open/close machinery
    /// to keep in step with the first.
    /// </remarks>
    enum TabMenuKind
    {
        Center,
        Overflow
    }

    TabMenuKind menuKind = TabMenuKind.Center;


    /// <summary>The rows the centre menu will show — the page's if it declared any, the button's otherwise.</summary>
    internal IList<TabAction> ResolveMenuActions()
    {
        var context = this.PageContext ?? this.SelectedItem?.PageContext;

        // Deliberately a Count check rather than IsSet: markup fills the collection the getter hands
        // back rather than assigning a new one, so the property never reads as "explicitly set" even
        // on a page that plainly declared rows.
        if (context is not null && ShinyTabs.GetActions(context) is { Count: > 0 } pageActions)
            return Adopt(pageActions, context.BindingContext);

        return this.CenterButton?.Actions is { Count: > 0 } buttonActions
            ? Adopt(buttonActions, this.BindingContext)
            : Array.Empty<TabAction>();
    }


    /// <summary>The custom content the centre menu will show, if any. Beats <see cref="ResolveMenuActions"/>.</summary>
    internal View? ResolveMenuContent()
    {
        var context = this.PageContext ?? this.SelectedItem?.PageContext;

        if (context is not null)
        {
            // Built fresh on every open, deliberately: a menu is transient, and a template that
            // rebuilds is the one that shows current data rather than whatever it captured first.
            if (ShinyTabs.GetMenuContentTemplate(context) is { } pageTemplate)
                return Adopt(pageTemplate.CreateContent() as View, context.BindingContext);

            if (ShinyTabs.GetMenuContent(context) is { } pageContent)
                return Adopt(pageContent, context.BindingContext);
        }

        if (this.CenterButton?.MenuContentTemplate is { } template)
            return Adopt(template.CreateContent() as View, this.BindingContext);

        return Adopt(this.CenterButton?.MenuContent, this.BindingContext);
    }


    /// <summary>
    /// Hands the declaring element's binding context to menu content the framework will never reach
    /// on its own.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the menu's bindings work. A <see cref="TabAction"/> lives in an
    /// attached-property collection and a <c>MenuContent</c> view is only parented once the menu is
    /// already open, so neither is on the declaring page's element chain when its bindings are
    /// evaluated - <c>Command="{Binding Save}"</c> would resolve against nothing, produce null, and
    /// the row would close the menu having done absolutely nothing. Seeding it as an *inherited*
    /// context means an explicit <c>BindingContext</c> on the action still wins.
    /// </remarks>
    static IList<TabAction> Adopt(IList<TabAction> actions, object? bindingContext)
    {
        foreach (var action in actions)
            SetInheritedBindingContext(action, bindingContext);

        return actions;
    }


    /// <inheritdoc cref="Adopt(IList{TabAction}, object?)"/>
    static View? Adopt(View? content, object? bindingContext)
    {
        if (content is not null)
            SetInheritedBindingContext(content, bindingContext);

        return content;
    }


    /// <summary>
    /// Resolves the surface to paint the menu onto, fresh every time.
    /// </summary>
    /// <remarks>
    /// Never cached. In a Shell the one bar instance is moved from page to page as the user
    /// navigates, so a layer held from last time belongs to a page that is no longer on screen - the
    /// menu would open somewhere nobody can see. The lookup is a parent walk and a child scan; it is
    /// not worth caching even if it were safe to.
    /// </remarks>
    /// <summary>Whether pressing the centre button has anything to present.</summary>
    internal bool HasMenuToShow()
        => this.MenuTemplate is not null || this.ResolveMenuActions().Count > 0 || this.ResolveMenuContent() is not null;


    Layout? ResolveMenuLayer()
    {
        Element? element = this;
        while (element is not null)
        {
            if (element is ITabMenuHost host)
                return host.GetTabMenuLayer();

            element = element.Parent;
        }

        return PageOverlay.GetOrCreateLayer<PageOverlay.TabMenuLayer>(this, PageOverlay.Layers.TabMenu);
    }


    void SyncMenuState(bool open)
    {
        if (open)
            _ = this.OpenMenuAsync();
        else
            _ = this.CloseMenuAsync();
    }


    async Task OpenMenuAsync()
    {
        if (this.menuCard is not null)
            return;

        var layer = this.ResolveMenuLayer();
        this.menuLayer = layer;
        if (layer is null)
        {
            // Nowhere to paint - the bar is not on a page yet. Report the state honestly rather than
            // leaving IsMenuOpen true against a menu that does not exist.
            this.IsMenuOpen = false;
            return;
        }

        var token = ++this.menuToken;

        this.menuBackdrop = new BoxView { Opacity = 0 };
        this.menuBackdrop.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.Scrim);
        var backdropTap = new TapGestureRecognizer();
        backdropTap.Tapped += (_, _) => this.IsMenuOpen = false;
        this.menuBackdrop.GestureRecognizers.Add(backdropTap);

        this.menuCard = this.BuildMenuCard();
        this.menuCard.Opacity = 0;
        this.menuCard.TranslationY = 16;
        this.menuCard.Scale = 0.96;

        layer.Children.Add(this.menuBackdrop);
        layer.Children.Add(this.menuCard);

        var duration = this.AnimationDuration;
        if (this.Handler is null || duration == 0)
        {
            // No handler means no animation manager - a headless host, or a bar that has not been
            // rendered yet. Land on the final frame rather than awaiting an animation nothing drives.
            this.menuBackdrop.Opacity = 0.45;
            this.menuCard.Opacity = 1;
            this.menuCard.TranslationY = 0;
            this.menuCard.Scale = 1;
            this.RotateCenterIcon(true, 0);
            this.MenuOpened?.Invoke(this, EventArgs.Empty);
            return;
        }

        this.RotateCenterIcon(true, duration);

        try
        {
            await Task.WhenAll(
                this.menuBackdrop.FadeToAsync(0.45, duration, Easing.CubicOut),
                this.menuCard.FadeToAsync(1, duration, Easing.CubicOut),
                this.menuCard.TranslateToAsync(0, 0, duration, Easing.CubicOut),
                this.menuCard.ScaleToAsync(1, duration, Easing.CubicOut)
            ).ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Torn down mid-flight (page popped, handler disconnected). The menu is either gone or
            // about to be; either way there is nothing left to finish.
        }

        if (token == this.menuToken)
            this.MenuOpened?.Invoke(this, EventArgs.Empty);
    }


    async Task CloseMenuAsync()
    {
        var card = this.menuCard;
        var backdrop = this.menuBackdrop;
        if (card is null)
            return;

        var token = ++this.menuToken;
        this.menuCard = null;
        this.menuBackdrop = null;

        var duration = this.AnimationDuration;
        if (this.Handler is not null && duration > 0)
        {
            this.RotateCenterIcon(false, duration);
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
        else
        {
            this.RotateCenterIcon(false, 0);
        }

        // Unparent whatever the page lent us before dropping the card, or the next open finds a view
        // that already has a parent and MAUI throws.
        card.Content = null;

        this.menuLayer?.Children.Remove(card);
        if (backdrop is not null)
            this.menuLayer?.Children.Remove(backdrop);

        if (token == this.menuToken)
            this.MenuClosed?.Invoke(this, EventArgs.Empty);
    }


    void RotateCenterIcon(bool open, uint duration)
    {
        // Only for the menu the centre button itself opened. The rotation is that button's
        // open/close affordance - spinning it into a close glyph for the overflow menu would offer
        // to close a menu it has nothing to do with.
        if (this.menuKind != TabMenuKind.Center)
            return;

        if (this.CenterButton is not { RotateOnOpen: not 0 } center || this.centerIconView is null)
            return;

        var target = open ? center.RotateOnOpen : 0;
        if (duration == 0 || this.Handler is null)
            this.centerIconView.Rotation = target;
        else
            _ = this.centerIconView.RotateToAsync(target, duration, Easing.CubicOut);
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
        var overhang = this.CenterButton?.EffectiveOverhang ?? 0;
        var card = new Border
        {
            StrokeThickness = 0,
            Stroke = null,
            Padding = new Thickness(0, 6),

            // The overflow menu belongs over the tab that opened it, which is the last one in the
            // bar; the centre menu belongs over the centre button. Anchoring both in the middle
            // would leave the overflow card pointing at nothing.
            HorizontalOptions = this.menuKind == TabMenuKind.Overflow ? LayoutOptions.End : LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            MinimumWidthRequest = 200,
            Margin = new Thickness(16, 0, 16, this.BarHeight + this.BarMargin.Bottom + overhang + 12),
            Background = ThemeTokens.TokenBrush(ShinyThemeKeys.Color.SurfaceContainerHigh),
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerLargeRadius),
            Content = this.BuildMenuBody()
        };
        card.WithElevation(ShinyThemeKeys.Elevation.Level3);
        return card;
    }


    View BuildMenuBody()
    {
        if (this.menuKind == TabMenuKind.Overflow)
        {
            var tabs = new VerticalStackLayout { Spacing = 0 };
            foreach (var item in this.overflowed)
                tabs.Children.Add(this.BuildOverflowRow(item));

            return tabs;
        }

        // A menu template replaces the card's contents wholesale - rows, layout and chrome - while
        // the bar keeps the backdrop, the anchoring above the button and the open/close animation.
        if (this.MenuTemplate is { } menuTemplate)
        {
            var context = this.PageContext ?? this.SelectedItem?.PageContext;
            return Adopt(menuTemplate.CreateContent() as View, context?.BindingContext ?? this.BindingContext)
                   ?? new VerticalStackLayout();
        }

        if (this.ResolveMenuContent() is { } custom)
        {
            // A page can hand the same view over on every open, so it has to come off its previous
            // card before it goes onto this one.
            if (custom.Parent is Border previous)
                previous.Content = null;

            return custom;
        }

        var stack = new VerticalStackLayout { Spacing = 0 };
        foreach (var action in this.ResolveMenuActions())
            stack.Children.Add(this.BuildActionRow(action));

        return stack;
    }


    View BuildOverflowRow(ShinyTabItem item)
    {
        var label = new Label
        {
            Text = ShinyTabs.Resolve<string>(ShinyTabs.TitleProperty, this.TitleSources(item)) ?? item.Title,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        var selected = ReferenceEquals(item, this.SelectedItem) || this.items.IndexOf(item) == this.SelectedIndex;
        var tint = selected ? ShinyThemeKeys.Color.Primary : ShinyThemeKeys.Color.OnSurface;

        label.SetDynamicResource(Label.TextColorProperty, tint);
        label.FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None;

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

        // Realized fresh rather than through the cell's cache: this view belongs to a card that is
        // torn down on close, and handing it the cell's icon would take the icon off the bar.
        if (TabIcons.Realize(item, null, 22) is { } icon)
        {
            TabIcons.Tint(icon, null, tint);
            Grid.SetColumn(icon, 0);
            row.Children.Add(icon);
        }

        Grid.SetColumn(label, 1);
        row.Children.Add(label);

        // The badge belongs on the row too: a tab folded into the menu is exactly the one whose
        // unread count nobody can see any more.
        var badgeText = ShinyTabs.Resolve<string>(ShinyTabs.BadgeProperty, this.TitleSources(item)) ?? item.Badge;
        if (!String.IsNullOrEmpty(badgeText))
        {
            var badge = new PillView
            {
                Text = badgeText,
                VerticalOptions = LayoutOptions.Center
            };
            Grid.SetColumn(badge, 2);
            row.Children.Add(badge);
        }

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => this.SelectOverflowItem(item);
        row.GestureRecognizers.Add(tap);

        return row;
    }


    View BuildActionRow(TabAction action)
    {
        if (action.IsSeparator)
        {
            var separator = new BoxView
            {
                HeightRequest = 1,
                Margin = new Thickness(12, 6)
            };
            separator.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);
            return separator;
        }

        var label = new Label
        {
            Text = action.Text,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 14,
            Padding = new Thickness(18, 12),
            BackgroundColor = Colors.Transparent,
            Opacity = action.IsEnabled ? 1 : 0.4
        };

        var tint = action.IsDestructive ? ShinyThemeKeys.Color.Error : ShinyThemeKeys.Color.OnSurface;
        label.SetDynamicResource(Label.TextColorProperty, tint);

        if (TabIcons.Realize(action, null, 22) is { } icon)
        {
            TabIcons.Tint(icon, null, tint);
            Grid.SetColumn(icon, 0);
            row.Children.Add(icon);
        }

        Grid.SetColumn(label, 1);
        row.Children.Add(label);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (!action.IsEnabled)
                return;

            action.Invoke();
            this.ActionInvoked?.Invoke(this, new TabActionEventArgs(action));
            this.IsMenuOpen = false;
        };
        row.GestureRecognizers.Add(tap);

        return row;
    }
}

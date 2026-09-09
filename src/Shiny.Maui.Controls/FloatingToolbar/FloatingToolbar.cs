using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>What was clicked, and on whose behalf.</summary>
/// <param name="Item">The item invoked — always a leaf, never a menu button.</param>
/// <param name="Target">The view the bar was anchored to when it happened.</param>
/// <param name="Context">That view's <c>BindingContext</c>, which is the row a shared bar was acting on.</param>
public record FloatingToolbarItemEventArgs(ShinyToolbarItem Item, View? Target, object? Context);


/// <summary>
/// A toolbar that floats over a control the way a tooltip does — icons, labels, badges, dropdown
/// menus and an overflow, laid out across or down, anchored to whichever control triggered it.
/// </summary>
/// <remarks>
/// Anchoring is <see cref="Tooltip"/>'s: the same <see cref="AnchorTriggerBinder"/> opens it and the
/// same <see cref="TooltipPlacementSolver"/> decides which side it lands on, so a bar near the bottom
/// of the screen flips above its target for the same reasons a tooltip does.
/// <para>
/// One instance can serve many controls. Point <see cref="Target"/> at a single view, or set
/// <see cref="AttachToProperty"/> on every row of a list and the one bar re-anchors to whichever row
/// was triggered — <see cref="CurrentTarget"/> and the click args say which that was.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// &lt;shiny:FloatingToolbar x:Name="rowBar" Trigger="Hover" Orientation="Vertical"&gt;
///     &lt;shiny:FloatingToolbar.Items&gt;
///         &lt;shiny:ShinyToolbarItem Text="Copy" /&gt;
///         &lt;shiny:ShinyToolbarItem Text="Delete" /&gt;
///     &lt;/shiny:FloatingToolbar.Items&gt;
/// &lt;/shiny:FloatingToolbar&gt;
/// </code>
/// </example>
public partial class FloatingToolbar : ContentView
{
    readonly Dictionary<View, AnchorTriggerBinder> attached = new();
    readonly List<FloatingToolbarStrip> menus = new();

    FloatingToolbarStrip? strip;
    BoxView? catcher;
    AbsoluteLayout? layer;
    AbsoluteLayout? menuLayer;
    AnchorTriggerBinder? ownTriggers;
    IDispatcherTimer? showTimer;
    IDispatcherTimer? hideTimer;
    IDispatcherTimer? dismissTimer;
    View? currentTarget;
    bool isShown;


    public FloatingToolbar()
    {
        // Not a defaultValueCreator: a lazily-created default never raises propertyChanged, so the
        // CollectionChanged hook wired there would never run.
        this.Items = new ObservableCollection<ShinyToolbarItem>();

        // The element itself is a handle, not a visual — it draws nothing and must never eat a touch
        // meant for the page underneath it.
        this.InputTransparent = true;

        this.Loaded += (_, _) => this.RewireTriggers();
        this.Unloaded += (_, _) => this.OnUnloaded();

        StyleGuard.MarkReady(this, typeof(FloatingToolbar));
    }


    /// <summary>Raised for the item actually invoked — a bar button, or a leaf inside a dropdown.</summary>
    public event EventHandler<FloatingToolbarItemEventArgs>? ItemClicked;

    /// <summary>Raised once the bar is on screen.</summary>
    public event EventHandler? Opened;

    /// <summary>Raised once it has gone.</summary>
    public event EventHandler? Closed;


    /// <summary>The view the bar is anchored to right now. With <see cref="AttachToProperty"/> this moves.</summary>
    public View? CurrentTarget => this.currentTarget;

    /// <summary>Where the bar points when nothing has been triggered yet.</summary>
    public View? Anchor => this.Target ?? this.ResolveTargetName() ?? this.Content;


    /// <summary>Open the bar against its default anchor.</summary>
    public void Show() => this.ShowFor(this.Anchor);

    /// <summary>Open the bar against a particular view.</summary>
    public void ShowFor(View? target)
    {
        this.currentTarget = target;
        this.IsOpen = true;
    }

    /// <summary>Close the bar.</summary>
    public void Hide() => this.IsOpen = false;

    /// <summary>Flip it.</summary>
    public void Toggle() => this.IsOpen = !this.IsOpen;


    // ---------------------------------------------------------------------------------------------
    // Attach to many
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Points a view at a shared <see cref="FloatingToolbar"/>. Set it on every row of a list and one
    /// bar serves all of them, re-anchoring to whichever row was triggered — rather than one bar per
    /// row, all of them alive at once.
    /// </summary>
    public static readonly BindableProperty AttachToProperty = BindableProperty.CreateAttached(
        "AttachTo",
        typeof(FloatingToolbar),
        typeof(FloatingToolbar),
        null,
        propertyChanged: OnAttachToChanged);

    /// <inheritdoc cref="AttachToProperty" />
    public static FloatingToolbar? GetAttachTo(BindableObject view) => (FloatingToolbar?)view.GetValue(AttachToProperty);

    /// <inheritdoc cref="AttachToProperty" />
    public static void SetAttachTo(BindableObject view, FloatingToolbar? value) => view.SetValue(AttachToProperty, value);


    static void OnAttachToChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not View view)
            return;

        (oldValue as FloatingToolbar)?.Detach(view);
        (newValue as FloatingToolbar)?.Attach(view);
    }


    void Attach(View view)
    {
        if (this.attached.ContainsKey(view))
            return;

        var binder = new AnchorTriggerBinder(this.SafeDispatcher)
        {
            LongPressDelay = this.LongPressDelay,
            Show = () => this.RequestShow(view),
            Hide = this.RequestHide,
            // A shared bar toggles per target: triggering the row it is already on closes it, but
            // triggering a different row moves it rather than closing it.
            Toggle = () => this.RequestToggle(view)
        };
        binder.Bind(view, this.Trigger);
        this.attached[view] = binder;
    }


    void Detach(View view)
    {
        if (!this.attached.Remove(view, out var binder))
            return;

        binder.Unbind();

        if (ReferenceEquals(this.currentTarget, view))
            this.Hide();
    }


    // ---------------------------------------------------------------------------------------------
    // Triggers
    // ---------------------------------------------------------------------------------------------

    void RewireTriggers()
    {
        this.ownTriggers ??= new AnchorTriggerBinder(this.SafeDispatcher);
        this.ownTriggers.LongPressDelay = this.LongPressDelay;

        var anchor = this.Anchor;
        this.ownTriggers.Show = () => this.RequestShow(anchor);
        this.ownTriggers.Hide = this.RequestHide;
        this.ownTriggers.Toggle = () => this.RequestToggle(anchor);
        this.ownTriggers.Bind(anchor, this.Trigger);

        foreach (var (view, binder) in this.attached)
        {
            binder.LongPressDelay = this.LongPressDelay;
            binder.Bind(view, this.Trigger);
        }
    }


    /// <summary>The dispatcher, or null off a window — where asking for it throws.</summary>
    internal IDispatcher? SafeDispatcher()
    {
        try
        {
            return this.Dispatcher;
        }
        catch
        {
            return null;
        }
    }


    void RequestShow(View? target)
    {
        this.hideTimer?.Stop();
        this.hideTimer = null;

        if (this.ShowDelay <= 0)
        {
            this.ShowFor(target);
            return;
        }

        var owner = this.SafeDispatcher();
        if (owner is null)
        {
            this.ShowFor(target);
            return;
        }

        this.showTimer?.Stop();
        this.showTimer = owner.CreateTimer();
        this.showTimer.Interval = TimeSpan.FromMilliseconds(this.ShowDelay);
        this.showTimer.IsRepeating = false;
        this.showTimer.Tick += (_, _) => this.ShowFor(target);
        this.showTimer.Start();
    }


    /// <summary>
    /// Closes after <see cref="HideDelay"/> rather than at once. The pointer has to cross the gap
    /// between the target and the bar to reach any of its buttons, and a bar that closed on
    /// pointer-exit would be gone before it got there.
    /// </summary>
    void RequestHide()
    {
        this.showTimer?.Stop();
        this.showTimer = null;

        if (this.HideDelay <= 0)
        {
            this.Hide();
            return;
        }

        var owner = this.SafeDispatcher();
        if (owner is null)
        {
            this.Hide();
            return;
        }

        this.hideTimer?.Stop();
        this.hideTimer = owner.CreateTimer();
        this.hideTimer.Interval = TimeSpan.FromMilliseconds(this.HideDelay);
        this.hideTimer.IsRepeating = false;
        this.hideTimer.Tick += (_, _) => this.Hide();
        this.hideTimer.Start();
    }


    void RequestToggle(View? target)
    {
        // Moving between rows re-anchors instead of closing: the second row's tap would otherwise
        // read as "close", and the bar would flicker off the very row it was asked for.
        if (this.IsOpen && !ReferenceEquals(this.currentTarget, target))
        {
            this.currentTarget = target;
            this.Reposition();
            return;
        }

        if (this.IsOpen)
            this.Hide();
        else
            this.RequestShow(target);
    }


    View? ResolveTargetName()
    {
        if (String.IsNullOrWhiteSpace(this.TargetName))
            return null;

        Element? scope = this;
        while (scope is not null)
        {
            if (scope is VisualElement && scope.FindByName(this.TargetName) is View found)
                return found;

            scope = scope.Parent;
        }

        return null;
    }


    // ---------------------------------------------------------------------------------------------
    // Items
    // ---------------------------------------------------------------------------------------------

    void OnItemsChanged(IList<ShinyToolbarItem>? oldItems, IList<ShinyToolbarItem>? newItems)
    {
        if (oldItems is INotifyCollectionChanged oldObservable)
            oldObservable.CollectionChanged -= this.OnItemsCollectionChanged;

        if (newItems is INotifyCollectionChanged newObservable)
            newObservable.CollectionChanged += this.OnItemsCollectionChanged;

        this.RebuildStrip();
    }


    void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => this.RebuildStrip();


    void RebuildStrip()
    {
        if (this.strip is null)
            return;

        this.strip.Orientation = this.Orientation;
        this.strip.ShowLabels = this.ShowLabels;
        this.strip.ItemSize = this.ItemSize;
        this.strip.ForegroundColor = this.ForegroundColor;

        if (this.BarColor is not null)
            this.strip.BackgroundColor = this.BarColor;

        this.strip.StrokeShape = this.BuildCorner();
        this.strip.Build(this.Items.ToList(), this.ResolveMaxVisible(), this.BuildOverflowItem());

        if (this.isShown)
            this.Reposition();
    }


    /// <summary>An explicit radius wins; unset falls through to the theme's shape scale.</summary>
    internal Microsoft.Maui.Controls.Shapes.RoundRectangle BuildCorner()
    {
        var shape = new Microsoft.Maui.Controls.Shapes.RoundRectangle();
        shape.SetCornerTokenOrValue(this.CornerRadius, Themes.ShinyThemeKeys.Shape.CornerMediumRadius);

        return shape;
    }


    ShinyToolbarItem? BuildOverflowItem()
        => this.OverflowEnabled ? new ShinyToolbarItem { Text = "⋯", Tooltip = "More actions" } : null;


    /// <summary>
    /// How many items the bar may draw. An explicit cap wins; otherwise it comes from the room the
    /// overlay actually has along the orientation, so the same bar shows more on a tablet than on a
    /// phone without anyone configuring it.
    /// </summary>
    int ResolveMaxVisible()
    {
        if (this.MaxVisibleItems > 0)
            return this.MaxVisibleItems;

        if (!this.OverflowEnabled)
            return 0;

        var root = this.layer?.Parent as Layout;
        var extent = this.Orientation == ToolbarOrientation.Vertical ? root?.Height : root?.Width;
        if (extent is not > 0)
            return 0;

        var available = extent.Value - (this.ScreenMargin * 2);
        // A labelled item is wider than a square one, and guessing low is the safe direction: the
        // overflow menu can hold anything, a bar running off the screen cannot.
        var per = this.ShowLabels && this.Orientation == ToolbarOrientation.Horizontal
            ? this.ItemSize * 3
            : this.ItemSize;

        return Math.Max(1, (int)(available / Math.Max(1, per)));
    }
}

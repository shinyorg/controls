using Shiny.Controls.Resizable;

namespace Shiny.Maui.Controls;

/// <summary>
/// One resizable region of a <see cref="ResizablePanelGroup"/>. Sizes are percentages of the group,
/// after the handles have taken their share.
/// </summary>
public class ResizablePanel : ContentView
{
    bool settledCollapsed;

    public ResizablePanel()
    {
        // A panel dragged down to nothing must not paint its content over its neighbours.
        this.IsClippedToBounds = true;
    }

    internal ResizablePanelGroup? Group { get; set; }

    internal ResizablePanelLimits Limits => new(
        Math.Clamp(this.MinSize, 0, 100),
        Math.Clamp(this.MaxSize, 0, 100),
        this.Collapsible,
        Math.Clamp(this.CollapsedSize, 0, 100));

    /// <summary>The size to come back to when the panel is expanded: what it was before it collapsed.</summary>
    internal double? ExpandedSize { get; set; }

    public static readonly BindableProperty DefaultSizeProperty = BindableProperty.Create(
        nameof(DefaultSize), typeof(double?), typeof(ResizablePanel), null,
        propertyChanged: (b, _, _) => ((ResizablePanel)b).Group?.OnPanelDefaultsChanged());

    /// <summary>
    /// The starting size in percent. Panels without one share whatever the others leave. Ignored once
    /// the group has sizes of its own (dragged, or set through <see cref="ResizablePanelGroup.Sizes"/>).
    /// </summary>
    public double? DefaultSize
    {
        get => (double?)this.GetValue(DefaultSizeProperty);
        set => this.SetValue(DefaultSizeProperty, value);
    }

    public static readonly BindableProperty MinSizeProperty = BindableProperty.Create(
        nameof(MinSize), typeof(double), typeof(ResizablePanel), 0d,
        propertyChanged: (b, _, _) => ((ResizablePanel)b).Group?.OnPanelLimitsChanged());

    /// <summary>The smallest the panel can be dragged, in percent. 0 by default.</summary>
    public double MinSize
    {
        get => (double)this.GetValue(MinSizeProperty);
        set => this.SetValue(MinSizeProperty, value);
    }

    public static readonly BindableProperty MaxSizeProperty = BindableProperty.Create(
        nameof(MaxSize), typeof(double), typeof(ResizablePanel), 100d,
        propertyChanged: (b, _, _) => ((ResizablePanel)b).Group?.OnPanelLimitsChanged());

    /// <summary>The largest the panel can be dragged, in percent. 100 by default.</summary>
    public double MaxSize
    {
        get => (double)this.GetValue(MaxSizeProperty);
        set => this.SetValue(MaxSizeProperty, value);
    }

    public static readonly BindableProperty CollapsibleProperty = BindableProperty.Create(
        nameof(Collapsible), typeof(bool), typeof(ResizablePanel), false,
        propertyChanged: (b, _, _) => ((ResizablePanel)b).Group?.OnPanelLimitsChanged());

    /// <summary>
    /// Lets the panel snap shut to <see cref="CollapsedSize"/> when it is dragged more than halfway
    /// past its <see cref="MinSize"/>, and snap open again the same way.
    /// </summary>
    public bool Collapsible
    {
        get => (bool)this.GetValue(CollapsibleProperty);
        set => this.SetValue(CollapsibleProperty, value);
    }

    public static readonly BindableProperty CollapsedSizeProperty = BindableProperty.Create(
        nameof(CollapsedSize), typeof(double), typeof(ResizablePanel), 0d,
        propertyChanged: (b, _, _) => ((ResizablePanel)b).Group?.OnPanelLimitsChanged());

    /// <summary>The size of a collapsed panel, in percent - 0 hides it, a few percent leaves a rail.</summary>
    public double CollapsedSize
    {
        get => (double)this.GetValue(CollapsedSizeProperty);
        set => this.SetValue(CollapsedSizeProperty, value);
    }

    public static readonly BindableProperty IsCollapsedProperty = BindableProperty.Create(
        nameof(IsCollapsed), typeof(bool), typeof(ResizablePanel), false, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => ((ResizablePanel)b).Group?.OnPanelIsCollapsedChanged((ResizablePanel)b, (bool)n));

    /// <summary>
    /// Whether the panel is at its collapsed size. Two-way: follows drags, and setting it collapses or
    /// expands the panel. Only means anything with <see cref="Collapsible"/> on.
    /// </summary>
    public bool IsCollapsed
    {
        get => (bool)this.GetValue(IsCollapsedProperty);
        set => this.SetValue(IsCollapsedProperty, value);
    }

    static readonly BindablePropertyKey SizePropertyKey = BindableProperty.CreateReadOnly(
        nameof(Size), typeof(double), typeof(ResizablePanel), 0d);

    public static readonly BindableProperty SizeProperty = SizePropertyKey.BindableProperty;

    /// <summary>The panel's current size in percent of the group. Updated when a resize settles.</summary>
    public double Size => (double)this.GetValue(SizeProperty);

    /// <summary>The panel collapsed - dragged shut, or through <see cref="IsCollapsed"/> / <see cref="Collapse"/>.</summary>
    public event EventHandler? Collapsed;

    /// <summary>The panel opened from collapsed.</summary>
    public event EventHandler? Expanded;

    /// <summary>Collapses the panel. Needs <see cref="Collapsible"/>.</summary>
    public void Collapse() => this.Group?.Collapse(this);

    /// <summary>Opens a collapsed panel to the size it had before.</summary>
    public void Expand() => this.Group?.Expand(this);

    /// <summary>Sets <see cref="Size"/> and <see cref="IsCollapsed"/> from a settled layout, raising the events.</summary>
    internal void Settle(double size, bool collapsed)
    {
        this.SetValue(SizePropertyKey, size);
        if (!collapsed)
            this.ExpandedSize = size;

        // Put IsCollapsed back to the truth even when nothing changed - a collapse that could not make
        // room leaves it set by the caller but not done.
        if (this.IsCollapsed != collapsed)
        {
            // stops the property-changed handler from turning this back into a collapse/expand call
            this.Group!.Syncing = true;
            try
            {
                this.IsCollapsed = collapsed;
            }
            finally
            {
                this.Group!.Syncing = false;
            }
        }

        if (this.settledCollapsed == collapsed)
            return;

        this.settledCollapsed = collapsed;
        (collapsed ? this.Collapsed : this.Expanded)?.Invoke(this, EventArgs.Empty);
    }
}

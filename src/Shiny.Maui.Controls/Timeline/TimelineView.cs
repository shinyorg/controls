using System.Collections;
using System.Collections.Specialized;
using Shiny.Maui.Controls.Collections;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>
/// A vertical timeline: a rail of markers with arbitrary content beside each one.
/// </summary>
/// <remarks>
/// <para>
/// The same three-state marker the wizard draws, turned on its side and made item-driven — an
/// activity feed, an order's progress, an audit trail, a changelog. Where the wizard owns its steps
/// and shows one at a time, this binds a collection and shows all of them at once.
/// </para>
/// <para>
/// <b>Rows size to their content.</b> Each node is one grid row whose height comes from whatever the
/// item template produced, and the rail's connector fills that row however tall it turns out to be —
/// so a node with a paragraph beside it simply makes its own segment longer. That is why the rail is
/// built per row rather than drawn as one line behind the stack: a single line would have to be
/// measured against a total height nothing knows until after layout.
/// </para>
/// <para>
/// Composed from ordinary views rather than rendered natively, and so <b>not virtualized</b>: every
/// item is realised up front. That is the right trade for content that is arbitrary and
/// self-sizing — the thing that makes a timeline useful is that each row can be a different height,
/// which is exactly what a recycling list is worst at — but it does mean a timeline of thousands of
/// entries wants paging by the app.
/// </para>
/// </remarks>
public partial class TimelineView : ContentView
{
    readonly VerticalStackLayout stack;
    readonly List<NodeVisual> visuals = new();

    INotifyCollectionChanged? observed;
    ScrollView? scroller;

    /// <summary>The parts of one row whose colour changes with the active position.</summary>
    /// <remarks>
    /// Held so moving the active index can repaint rather than rebuild. Rebuilding would re-run every
    /// item template and throw away any state the content had — a half-typed entry, a scrolled inner
    /// list — for what is only ever a colour change.
    /// </remarks>
    sealed record NodeVisual(
        int Index,
        BoxView? LineAbove,
        BoxView? LineBelow,
        BoxView? DefaultMarker,
        BoxView? Ring,
        ContentView? MarkerHost);

    public TimelineView()
    {
        this.stack = new VerticalStackLayout { Spacing = 0 };
        this.BuildShell();
    }

    void BuildShell()
    {
        if (this.stack is null)
            return;

        // Re-parenting rather than rebuilding: the rows are the expensive part and they do not care
        // which container they sit in.
        if (this.scroller is not null)
            this.scroller.Content = null;

        if (this.IsScrollable)
        {
            this.scroller ??= new ScrollView();
            this.scroller.Content = this.stack;
            this.Content = this.scroller;
        }
        else
        {
            this.Content = this.stack;
        }
    }

    // ---- items ----

    void OnItemsSourceChanged(IEnumerable? oldValue, IEnumerable? newValue)
    {
        if (this.observed is not null)
        {
            this.observed.CollectionChanged -= this.OnCollectionChanged;
            this.observed = null;
        }

        if (newValue is INotifyCollectionChanged incc)
        {
            this.observed = incc;
            incc.CollectionChanged += this.OnCollectionChanged;
        }

        this.Rebuild();
    }

    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => this.Rebuild();

    IList<object> Items()
    {
        if (this.ItemsSource is null)
            return [];

        var items = new List<object>();
        foreach (var item in this.ItemsSource)
            items.Add(item);

        return items;
    }

    TimelineNodeState StateOf(int index)
        => TimelineNode.StateFor(index, this.ActiveIndex, this.AllActive);

    void Rebuild()
    {
        if (this.stack is null)
            return;

        this.stack.Clear();
        this.visuals.Clear();

        var items = this.Items();

        for (var i = 0; i < items.Count; i++)
            this.stack.Add(this.BuildRow(items[i], i, items.Count));
    }

    View BuildRow(object item, int index, int count)
    {
        var node = new TimelineNode(item, index, this.StateOf(index), index == 0, index == count - 1);
        var railWidth = Math.Max(this.MarkerSize, this.LineThickness);

        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(new GridLength(railWidth)),
                new ColumnDefinition(GridLength.Star)
            }
        };

        var rail = this.BuildRail(node, railWidth, out var visual);
        this.visuals.Add(visual);

        var content = this.BuildContent(item, node);

        // The gap between rows belongs to the content, not to the stack: the stack's own spacing would
        // break the rail into disconnected segments, because nothing is drawn between rows.
        if (!node.IsLast)
            content.Margin = new Thickness(0, 0, 0, this.ItemSpacing);

        var opposite = this.OppositeTemplate is null ? null : this.Inflate(this.OppositeTemplate, item);

        var railColumn = this.RailPosition == TimelineRailPosition.Left ? 1 : 1;
        var contentColumn = this.RailPosition == TimelineRailPosition.Left ? 2 : 0;
        var oppositeColumn = this.RailPosition == TimelineRailPosition.Left ? 0 : 2;

        content.Margin = this.RailPosition == TimelineRailPosition.Left
            ? new Thickness(this.RailSpacing, 0, 0, content.Margin.Bottom)
            : new Thickness(0, 0, this.RailSpacing, content.Margin.Bottom);

        row.Add(rail);
        Grid.SetColumn(rail, railColumn);

        row.Add(content);
        Grid.SetColumn(content, contentColumn);

        if (opposite is not null)
        {
            opposite.Margin = this.RailPosition == TimelineRailPosition.Left
                ? new Thickness(0, 0, this.RailSpacing, 0)
                : new Thickness(this.RailSpacing, 0, 0, 0);

            row.Add(opposite);
            Grid.SetColumn(opposite, oppositeColumn);
        }

        // On Command rather than the Tapped event, so the gesture is assertable from a test.
        row.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => this.NodeTapped?.Invoke(this, new CollectionItemEventArgs(item, index)))
        });

        return row;
    }

    View BuildContent(object item, TimelineNode node)
    {
        var template = this.ItemTemplateSelector?.SelectTemplate(item, this) ?? this.ItemTemplate;

        if (template is null)
        {
            var label = new Label { VerticalOptions = LayoutOptions.Start };
            label.SetBinding(Label.TextProperty, ".");
            label.BindingContext = item;
            return label;
        }

        return this.Inflate(template, item);
    }

    View Inflate(DataTemplate template, object bindingContext)
    {
        var created = template.CreateContent();
        var view = created as View
            ?? (created as ViewCell)?.View
            ?? throw new InvalidOperationException("A TimelineView template must produce a View or a ViewCell.");

        view.BindingContext = bindingContext;

        return view;
    }

    /// <summary>
    /// One row's slice of the rail: the segment above the marker, the marker, and the segment below.
    /// </summary>
    /// <remarks>
    /// Three rows rather than one line with the marker on top. The top segment is exactly as tall as
    /// the marker's offset and the bottom one is starred, so the bottom stretches with the content and
    /// the marker stays pinned beside the first line of text — which is the whole reason the offset
    /// exists.
    /// </remarks>
    View BuildRail(TimelineNode node, double railWidth, out NodeVisual visual)
    {
        var rail = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(new GridLength(this.MarkerOffset)),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            },
            WidthRequest = railWidth
        };

        BoxView? above = null;
        BoxView? below = null;

        // The first node has nothing above it and the last nothing below, so the rail starts and ends
        // at a marker rather than running off into empty space.
        if (!node.IsFirst)
        {
            above = this.Segment();
            rail.Add(above);
            Grid.SetRow(above, 0);
        }

        if (!node.IsLast)
        {
            below = this.Segment();
            rail.Add(below);
            Grid.SetRow(below, 2);
        }

        var markerCell = new Grid { HorizontalOptions = LayoutOptions.Center };
        BoxView? ring = null;
        BoxView? dot = null;
        ContentView? host = null;

        if (this.MarkerTemplate is { } template)
        {
            host = new ContentView { Content = this.Inflate(template, node), BindingContext = node };
            markerCell.Add(host);
        }
        else
        {
            // BoxViews rather than Borders, because a BoxView's colour is a Color and a Border's is a
            // Brush — and a Color theme token assigned to a Brush property is silently dropped, which
            // is a marker that simply never follows the theme pack.
            //
            // The halo sits behind the dot and only shows for the current node, which is what
            // separates "here" from "already happened" at a glance. Filled and faint rather than
            // stroked, matching the ring the Blazor side draws as a box-shadow spread.
            ring = Circle(this.MarkerSize + 8);
            dot = Circle(this.MarkerSize);

            markerCell.Add(ring);
            markerCell.Add(dot);
        }

        rail.Add(markerCell);
        Grid.SetRow(markerCell, 1);

        visual = new NodeVisual(node.Index, above, below, dot, ring, host);
        this.Paint(visual, node);

        return rail;
    }

    BoxView Segment() => new()
    {
        WidthRequest = this.LineThickness,
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Fill
    };

    // ---- state ----

    /// <summary>Repaints the rail for a new active position without rebuilding any content.</summary>
    void RefreshStates()
    {
        var count = this.visuals.Count;

        foreach (var visual in this.visuals)
        {
            var node = new TimelineNode(
                new object(),
                visual.Index,
                this.StateOf(visual.Index),
                visual.Index == 0,
                visual.Index == count - 1);

            this.Paint(visual, node);

            // A custom marker gets the new node so its own bindings re-evaluate; the item on it is a
            // placeholder because repainting never has one, and a template that needs the item has it
            // from the build.
            if (visual.MarkerHost?.BindingContext is TimelineNode existing)
                visual.MarkerHost.BindingContext = existing with { State = node.State };
        }
    }

    void Paint(NodeVisual visual, TimelineNode node)
    {
        // The segment leading *into* a node is filled when that node has been reached, so the rail
        // reads as a progress bar rather than as a set of unrelated links.
        if (visual.LineAbove is { } above)
            this.Tint(above, node.IsActive);

        if (visual.LineBelow is { } below)
            this.Tint(below, this.AllActive || node.Index < this.ActiveIndex);

        if (visual.DefaultMarker is { } dot)
            this.Tint(dot, node.IsActive);

        if (visual.Ring is { } ring)
        {
            this.Tint(ring, true);
            ring.Opacity = node.State == TimelineNodeState.Current ? 0.28 : 0;
        }
    }

    /// <summary>
    /// Colours one piece of the rail, through the theme unless the control was given a colour.
    /// </summary>
    /// <remarks>
    /// A dynamic resource rather than a resolved value, so a theme pack swapped at runtime moves the
    /// rail with it. Setting the property directly would break that binding for good, which is why an
    /// explicit colour and a themed one cannot both be applied to the same element.
    /// </remarks>
    void Tint(BoxView target, bool active)
    {
        var chosen = active ? this.ActiveColor : this.PendingColor;

        if (chosen is { } color)
            target.Color = color;
        else
            target.SetDynamicResource(BoxView.ColorProperty, active
                ? ShinyThemeKeys.Color.Primary
                : ShinyThemeKeys.Color.SurfaceContainerHighest);
    }

    /// <summary>A circle, as a BoxView so its fill is a Color a theme token can reach.</summary>
    static BoxView Circle(double size) => new()
    {
        WidthRequest = size,
        HeightRequest = size,
        CornerRadius = (float)(size / 2),
        HorizontalOptions = LayoutOptions.Center,
        VerticalOptions = LayoutOptions.Center
    };

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (this.Handler is null && this.observed is not null)
        {
            this.observed.CollectionChanged -= this.OnCollectionChanged;
            this.observed = null;
        }
    }
}

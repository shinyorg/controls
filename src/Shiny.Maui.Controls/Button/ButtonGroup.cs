using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

/// <summary>
/// Joins related buttons into one segmented control — inner corners square off and adjacent outlines
/// collapse into a single edge.
/// </summary>
/// <remarks>
/// <para>
/// It is a container for <em>actions</em> first: three buttons that belong together read as one control
/// instead of three. <see cref="SelectionMode"/> is opt-in on top of that and turns the same markup into
/// a segmented picker; leave it <see cref="ButtonGroupSelectionMode.None"/> (the default) and every
/// segment keeps its own <c>Command</c> and nothing here touches its appearance.
/// </para>
/// <para>
/// Segments are <see cref="ShinyButton"/>s, optionally with a <see cref="ButtonGroupSeparator"/> between
/// two filled ones and a <see cref="ButtonGroupText"/> for a static label or unit. A nested
/// <see cref="ButtonGroup"/> turns the outer one into a <em>cluster</em>: each inner group keeps its own
/// merged edges and the two are spaced apart by <see cref="ClusterSpacing"/>, which is what makes an
/// undo/redo pair beside a bold/italic/underline trio read as two things rather than five.
/// </para>
/// </remarks>
/// <example>
/// <code language="xaml">
/// &lt;shiny:ButtonGroup SelectionMode="Single" SelectedIndex="{Binding Range}"&gt;
///     &lt;shiny:ShinyButton Text="Day" /&gt;
///     &lt;shiny:ShinyButton Text="Week" /&gt;
///     &lt;shiny:ShinyButton Text="Month" /&gt;
/// &lt;/shiny:ButtonGroup&gt;
/// </code>
/// </example>
public partial class ButtonGroup : StackLayout
{
    /// <summary>What a segment looked like before this group started pushing appearances at it.</summary>
    sealed class SegmentState
    {
        public ButtonAppearance? Author { get; set; }
        public bool Pushed { get; set; }
    }

    // Which segments this group has written an Appearance onto, and what they carried first. Without it
    // the first push would make IsSet(Appearance) true on the button and every later change would be
    // mistaken for "the author set that themselves, leave it alone" - the same problem, and the same
    // answer, as Accordion.PushDefaults.
    readonly ConditionalWeakTable<ShinyButton, SegmentState> pushed = new();
    readonly List<ShinyButton> hooked = new();
    readonly List<int> selection = new();

    // The theme's corner radius and hairline thickness, as numbers. Both arrive as dynamic resources,
    // and two of the four corners have to be zeroed - which cannot be done to a value that has not
    // resolved yet. So a hidden, zero-sized Border resolves them the way ShinyButton's stroke probe
    // resolves its outline colour, and the group reads them off it. Being a real child keeps it in the
    // resource chain, so a live theme swap re-runs the geometry instead of freezing it at startup.
    readonly Border probe;
    readonly RoundRectangle probeShape;

    bool syncing;

    public ButtonGroup()
    {
        this.probeShape = new RoundRectangle();
        this.probeShape.SetDynamicResource(RoundRectangle.CornerRadiusProperty, ShinyThemeKeys.Shape.CornerMediumRadius);

        this.probe = new Border
        {
            IsVisible = false,
            WidthRequest = 0,
            HeightRequest = 0,
            InputTransparent = true,
            StrokeShape = this.probeShape
        }.WithStrokeThickness(ShinyThemeKeys.Border.Thin);

        this.probeShape.PropertyChanged += this.OnProbeChanged;
        this.probe.PropertyChanged += this.OnProbeChanged;

        // StackLayout runs vertically by default. A button group is a row of segments far more often
        // than a column of them, and it is the orientation every example of one is drawn in.
        this.Orientation = StackOrientation.Horizontal;
        this.Spacing = 0;
        this.HorizontalOptions = LayoutOptions.Start;
        this.VerticalOptions = LayoutOptions.Center;

        this.Children.Add(this.probe);

        // Last line: replays any styled property that was applied before the children existed.
        // See StyleGuard.
        StyleGuard.MarkReady(this, typeof(ButtonGroup));
    }


    // ---------------------------------------------------------------------------------------------
    // Public surface
    // ---------------------------------------------------------------------------------------------

    /// <summary>The selectable segments, in visual order. Text segments and separators are not selectable.</summary>
    public IReadOnlyList<ShinyButton> Segments => this.SegmentList();

    /// <summary>Every selected index, in ascending order. Empty when nothing is selected.</summary>
    public IReadOnlyList<int> SelectedIndexes => this.selection.ToList();

    /// <summary>Raised whenever the selection changes, however it changed.</summary>
    public event EventHandler<ButtonGroupSelectionChangedEventArgs>? SelectionChanged;

    /// <summary>Select the segment at <paramref name="index"/>. Returns false when out of range.</summary>
    public bool Select(int index)
    {
        if (index < 0 || index >= this.SegmentList().Count)
            return false;

        this.ApplySelection(index, true);
        return true;
    }


    /// <summary>Deselect the segment at <paramref name="index"/>. Returns false when out of range.</summary>
    public bool Deselect(int index)
    {
        if (index < 0 || index >= this.SegmentList().Count)
            return false;

        this.ApplySelection(index, false);
        return true;
    }


    /// <summary>Clear the selection entirely.</summary>
    public void ClearSelection()
    {
        if (this.selection.Count == 0)
            return;

        this.selection.Clear();
        this.AfterSelectionChanged();
    }


    // ---------------------------------------------------------------------------------------------
    // Child tracking
    // ---------------------------------------------------------------------------------------------

    protected override void OnChildAdded(Element child)
    {
        base.OnChildAdded(child);

        if (child is ShinyButton button)
            this.Hook(button);

        StyleGuard.WhenReady<ButtonGroup>(this, static g => g.Rebuild());
    }


    protected override void OnChildRemoved(Element child, int oldLogicalIndex)
    {
        base.OnChildRemoved(child, oldLogicalIndex);

        if (child is ShinyButton button)
        {
            this.Unhook(button);
            button.SetSegmentCorners(null);
            this.pushed.Remove(button);
        }
        else if (child is ButtonGroupText text)
        {
            text.SetSegmentCorners(null);
        }

        StyleGuard.WhenReady<ButtonGroup>(this, static g => g.Rebuild());
    }


    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        // Orientation and Spacing are StackLayout's own, so they cannot carry a propertyChanged of ours.
        if (propertyName == nameof(this.Orientation) || propertyName == nameof(this.Spacing))
            StyleGuard.WhenReady<ButtonGroup>(this, static g => g.Rebuild());
    }


    void Hook(ShinyButton button)
    {
        if (this.hooked.Contains(button))
            return;

        button.Clicked += this.OnSegmentClicked;
        button.PropertyChanged += this.OnSegmentPropertyChanged;
        this.hooked.Add(button);
    }


    void Unhook(ShinyButton button)
    {
        if (!this.hooked.Remove(button))
            return;

        button.Clicked -= this.OnSegmentClicked;
        button.PropertyChanged -= this.OnSegmentPropertyChanged;
    }


    /// <summary>
    /// Whether a segment collapses its edge into its neighbour depends on how that segment is painted,
    /// and a segment is free to change that at any time — from markup applied after it was added, from a
    /// style, or from a binding. Without this the collapse would be decided once, on the appearance the
    /// button happened to have when it joined the group.
    /// </summary>
    void OnSegmentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ShinyButton.Appearance) or nameof(ShinyButton.BorderThickness))
            StyleGuard.WhenReady<ButtonGroup>(this, static g => g.ApplyGeometry());
    }


    void OnProbeChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RoundRectangle.CornerRadius) or nameof(Border.StrokeThickness))
            StyleGuard.WhenReady<ButtonGroup>(this, static g => g.ApplyGeometry());
    }


    // ---------------------------------------------------------------------------------------------
    // Geometry
    // ---------------------------------------------------------------------------------------------

    List<ShinyButton> SegmentList() => this.Children.OfType<ShinyButton>().ToList();

    /// <summary>Every child that carries a rounded surface of its own, in visual order.</summary>
    List<View> CornerBearingChildren()
        => this.Children
            .OfType<View>()
            .Where(x => !ReferenceEquals(x, this.probe))
            .Where(x => x is ShinyButton or ButtonGroupText)
            .ToList();

    /// <summary>
    /// A group holding groups is a cluster of controls rather than one segmented control, so the inner
    /// groups keep their own merged edges and are spaced apart instead of being joined.
    /// </summary>
    bool IsCluster => this.Children.OfType<ButtonGroup>().Any();

    double ResolvedRadius
        => ThemeTokens.IsSet(this.CornerRadius) ? this.CornerRadius : this.probeShape.CornerRadius.TopLeft;

    double ResolvedBorderThickness
        => ThemeTokens.IsSet(this.BorderThickness)
            ? this.BorderThickness
            : this.probe.StrokeThickness > 0 ? this.probe.StrokeThickness : 1d;


    void Rebuild()
    {
        this.ApplyGeometry();
        this.ClampSelection();
        this.ApplySelectionVisuals();
    }


    void ApplyGeometry()
    {
        var horizontal = this.Orientation == StackOrientation.Horizontal;

        foreach (var separator in this.Children.OfType<ButtonGroupSeparator>())
            separator.SetOrientation(this.Orientation);

        if (this.IsCluster)
        {
            this.ApplyClusterSpacing(horizontal);
            return;
        }

        var segments = this.CornerBearingChildren();
        var radius = this.ResolvedRadius;
        var collapse = this.CollapseBorders ? this.ResolvedBorderThickness : 0d;
        var leading = true;

        for (var i = 0; i < segments.Count; i++)
        {
            var first = i == 0;
            var last = i == segments.Count - 1;

            CornerRadius? corners = first && last
                ? null                                  // a lone segment keeps its own rounding
                : horizontal
                    ? first ? new CornerRadius(radius, 0, radius, 0)
                        : last ? new CornerRadius(0, radius, 0, radius)
                        : new CornerRadius(0)
                    : first ? new CornerRadius(radius, radius, 0, 0)
                        : last ? new CornerRadius(0, 0, radius, radius)
                        : new CornerRadius(0);

            switch (segments[i])
            {
                case ShinyButton button:
                    button.SetSegmentCorners(corners);
                    break;

                case ButtonGroupText text:
                    text.SetSegmentCorners(corners);
                    break;
            }
        }

        // The collapse walks every child rather than only the corner-bearing ones, because a separator
        // between two segments is part of the run and must not reintroduce the gap.
        foreach (var child in this.Children.OfType<View>())
        {
            if (ReferenceEquals(child, this.probe))
                continue;

            var overlap = leading || collapse <= 0 || !HasOutline(child)
                ? 0d
                : -collapse;

            child.Margin = horizontal ? new Thickness(overlap, 0, 0, 0) : new Thickness(0, overlap, 0, 0);
            leading = false;
        }
    }


    void ApplyClusterSpacing(bool horizontal)
    {
        var leading = true;

        foreach (var child in this.Children.OfType<View>())
        {
            if (ReferenceEquals(child, this.probe))
                continue;

            var gap = leading ? 0d : this.ClusterSpacing;
            child.Margin = horizontal ? new Thickness(gap, 0, 0, 0) : new Thickness(0, gap, 0, 0);
            leading = false;
        }
    }


    /// <summary>
    /// Whether this segment paints an edge that would double up against its neighbour's. A filled
    /// segment has none, and pulling it a pixel into the one before it would only overlap the two fills.
    /// </summary>
    static bool HasOutline(View child) => child switch
    {
        ShinyButton button => button.Appearance == ButtonAppearance.Outlined || button.BorderThickness > 0,
        ButtonGroupText => true,
        _ => false
    };


    // ---------------------------------------------------------------------------------------------
    // Selection
    // ---------------------------------------------------------------------------------------------

    void OnSegmentClicked(object? sender, EventArgs e)
    {
        if (this.SelectionMode == ButtonGroupSelectionMode.None || sender is not ShinyButton button)
            return;

        var index = this.SegmentList().IndexOf(button);
        if (index < 0)
            return;

        var isSelected = this.selection.Contains(index);

        if (this.SelectionMode == ButtonGroupSelectionMode.Single)
        {
            // Re-tapping the selected segment is a no-op unless deselection is allowed: a picker that
            // can be emptied by tapping the answer again is a picker with no answer.
            if (isSelected && !this.AllowDeselect)
                return;

            this.ApplySelection(index, !isSelected);
        }
        else
        {
            this.ApplySelection(index, !isSelected);
        }
    }


    void ApplySelection(int index, bool selected)
    {
        if (this.SelectionMode == ButtonGroupSelectionMode.None)
            return;

        if (selected)
        {
            if (this.SelectionMode == ButtonGroupSelectionMode.Single)
                this.selection.Clear();

            if (!this.selection.Contains(index))
                this.selection.Add(index);
        }
        else
        {
            this.selection.Remove(index);
        }

        this.selection.Sort();
        this.AfterSelectionChanged();
    }


    void ClampSelection()
    {
        var count = this.SegmentList().Count;
        if (this.selection.RemoveAll(x => x >= count) > 0)
            this.PublishIndex();
    }


    void AfterSelectionChanged()
    {
        this.ApplySelectionVisuals();
        this.PublishIndex();

        var args = new ButtonGroupSelectionChangedEventArgs(this.SelectedIndexes);
        this.SelectionChanged?.Invoke(this, args);

        var command = this.SelectionChangedCommand;
        var parameter = this.SelectionChangedCommandParameter ?? (object)this.SelectedIndex;
        if (command?.CanExecute(parameter) == true)
            command.Execute(parameter);
    }


    void PublishIndex()
    {
        var index = this.selection.Count == 0 ? -1 : this.selection[0];
        if (this.SelectedIndex == index)
            return;

        this.syncing = true;
        try
        {
            this.SelectedIndex = index;
        }
        finally
        {
            this.syncing = false;
        }
    }


    void OnSelectedIndexChanged(int index)
    {
        if (this.syncing || this.SelectionMode == ButtonGroupSelectionMode.None)
            return;

        if (index < 0)
        {
            this.ClearSelection();
            return;
        }

        this.Select(index);
    }


    /// <summary>
    /// Paints the segments for the current selection. Nothing happens outside a selection mode, so a
    /// group of plain actions never has its appearances taken over.
    /// </summary>
    void ApplySelectionVisuals()
    {
        var segments = this.SegmentList();

        for (var i = 0; i < segments.Count; i++)
        {
            var button = segments[i];
            var state = this.pushed.GetValue(button, static _ => new SegmentState());

            if (this.SelectionMode == ButtonGroupSelectionMode.None)
            {
                // Hand back whatever the author had before this group started pushing.
                if (state.Pushed)
                {
                    if (state.Author is ButtonAppearance author)
                        button.Appearance = author;
                    else
                        button.ClearValue(ShinyButton.AppearanceProperty);

                    state.Pushed = false;
                }
                continue;
            }

            // Recorded once, on the first push: after that every value on the button is this group's.
            if (!state.Pushed)
                state.Author = button.IsSet(ShinyButton.AppearanceProperty) ? button.Appearance : null;

            var appearance = this.selection.Contains(i)
                ? this.SelectedAppearance
                : state.Author ?? this.UnselectedAppearance;

            state.Pushed = true;
            button.Appearance = appearance;
        }
    }
}


/// <summary>What changed when a <see cref="ButtonGroup"/>'s selection moved.</summary>
public class ButtonGroupSelectionChangedEventArgs(IReadOnlyList<int> selectedIndexes) : EventArgs
{
    /// <summary>Every selected index, in ascending order. Empty when nothing is selected.</summary>
    public IReadOnlyList<int> SelectedIndexes { get; } = selectedIndexes;

    /// <summary>The first selected index, or -1 when nothing is selected.</summary>
    public int SelectedIndex => this.SelectedIndexes.Count == 0 ? -1 : this.SelectedIndexes[0];
}

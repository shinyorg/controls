using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Maui.Controls.Cells;
using Shiny.Maui.Controls.Themes;
using TvTableView = Shiny.Maui.Controls.TableView;
using TvTableSection = Shiny.Maui.Controls.Sections.TableSection;

namespace Shiny.Maui.Controls.Infrastructure;

/// <summary>
/// One reorderable row - the cell plus a drag handle and its insertion indicators.
/// Rendered in place of the bare cell when a section sets UseDragSort.
/// </summary>
/// <remarks>
/// The drag runs on a <see cref="PanGestureRecognizer"/> on every platform rather than
/// on Drag/DropGestureRecognizer. The platform recognizers are broken on Mac Catalyst
/// (dotnet/maui#23627) and missing entirely from the AppKit and GTK4 hosts, and even
/// where they do work DragEventArgs carries no pointer position, so there is no way to
/// tell "drop above" from "drop below". A pan reports a usable delta everywhere.
/// </remarks>
sealed partial class DragSortRow : Grid
{
    readonly TvTableView owner;
    readonly ContentView dragHandle;
    readonly BoxView indicatorAbove;
    readonly BoxView indicatorBelow;

    public DragSortRow(TvTableView owner, TvTableSection section, CellBase cell)
    {
        this.owner = owner;
        this.Section = section;
        this.Cell = cell;

        this.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        this.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        Grid.SetColumn(cell, 0);
        this.Children.Add(cell);

        this.dragHandle = BuildHandle();
        Grid.SetColumn(this.dragHandle, 1);
        this.Children.Add(this.dragHandle);

        this.indicatorAbove = BuildIndicator(LayoutOptions.Start);
        this.indicatorBelow = BuildIndicator(LayoutOptions.End);
        this.Children.Add(this.indicatorAbove);
        this.Children.Add(this.indicatorBelow);

        HookPlatformScrollLock();
    }


    public TvTableSection Section { get; }
    public CellBase Cell { get; }

    /// <summary>The view the pan gesture is attached to - the platform hooks bind to its native view.</summary>
    internal View DragHandle => this.dragHandle;


    public void SetDragging(bool dragging)
    {
        this.Opacity = dragging ? 0.95 : 1.0;
        RaiseAboveSiblings(dragging);

        // The row has no background of its own, so raising it isn't enough - a transparent
        // row lifted over its neighbour renders as two rows of text on top of each other.
        // Paint it while it travels.
        if (dragging)
        {
            // Fallback first: a dynamic resource whose key isn't in the app's dictionaries
            // leaves the property untouched, and an unthemed app would drag a clear row.
            this.BackgroundColor = GetDefaultDragBackground();
            this.SetDynamicResource(BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);
        }
        else
        {
            this.RemoveDynamicResource(BackgroundColorProperty);
            this.BackgroundColor = null;
            this.TranslationY = 0;
            HideIndicators();
        }
    }

    static Color GetDefaultDragBackground()
        => Application.Current?.RequestedTheme == AppTheme.Dark
            ? Color.FromRgb(44, 44, 46)
            : Color.FromRgb(242, 242, 247);

    public void ShowIndicator(bool above)
    {
        this.indicatorAbove.IsVisible = above;
        this.indicatorBelow.IsVisible = !above;
    }

    public void HideIndicators()
    {
        this.indicatorAbove.IsVisible = false;
        this.indicatorBelow.IsVisible = false;
    }


    // Implemented per platform where the enclosing scroller has to be told to keep its
    // hands off the gesture. No-op on the plain net11.0 and Windows builds.
    partial void HookPlatformScrollLock();

    // Reorders the native view among its siblings. Implemented on the platforms that must
    // not go through ZIndex - see RaiseAboveSiblings.
    partial void SetPlatformRaised(bool raised);


    /// <remarks>
    /// The dragged row has to paint over its neighbours, and <see cref="VisualElement.ZIndex"/>
    /// is the obvious way to do that. It cannot be used here: on Android MAUI implements ZIndex
    /// by removing the native child and re-adding it at the new position, and removing a view
    /// mid-gesture dispatches ACTION_CANCEL to it - which is to say, setting ZIndex when the drag
    /// starts kills that very drag on its first frame. Android and iOS reorder the native view
    /// directly instead, which neither platform treats as a touch interruption.
    /// </remarks>
    void RaiseAboveSiblings(bool raised)
    {
#if ANDROID || IOS
        SetPlatformRaised(raised);
#else
        this.ZIndex = raised ? 1 : 0;
#endif
    }


    ContentView BuildHandle()
    {
        var glyph = new Label
        {
            Text = "☰",
            FontSize = 18,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        glyph.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        var host = new ContentView
        {
            Content = glyph,
            Padding = new Thickness(14, 10),
            MinimumWidthRequest = 44,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Fill,
            // Painted rather than null so the whole padded area is a hit target.
            BackgroundColor = Colors.Transparent
        };

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;
        host.GestureRecognizers.Add(pan);

        return host;
    }

    static BoxView BuildIndicator(LayoutOptions vertical)
    {
        var indicator = new BoxView
        {
            HeightRequest = 3,
            VerticalOptions = vertical,
            HorizontalOptions = LayoutOptions.Fill,
            InputTransparent = true,
            IsVisible = false
        };
        // BoxView paints from Color, not Background - a solid Background renders
        // transparent on the AppKit host.
        indicator.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.Primary);
        Grid.SetColumnSpan(indicator, 2);
        return indicator;
    }

    void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                this.owner.DragSort.Begin(this);
                break;

            case GestureStatus.Running:
                this.owner.DragSort.Update(this, e.TotalY);
                break;

            // Android reports zeroed totals here, so the controller commits from the
            // last Running values it saw rather than from anything on this event.
            case GestureStatus.Completed:
                this.owner.DragSort.Complete(this);
                break;

            case GestureStatus.Canceled:
                this.owner.DragSort.Cancel(this);
                break;
        }
    }
}

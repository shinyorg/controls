using System.Collections.Specialized;
using System.Collections;
using System.ComponentModel;
using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Kanban.Internal;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Kanban;

/// <summary>
/// A Kanban board: columns of cards you drag between, with WIP limits, swimlanes, collapsible
/// columns and an inline add-card composer.
/// </summary>
/// <remarks>
/// <para>
/// The bucketing, the WIP arithmetic and every drop decision live in <see cref="KanbanBoard"/>,
/// which is shared - file for file - with the Blazor control and has no idea a screen exists. This
/// class is the MAUI half: it owns the visual tree, the scrolling, and turning a pan gesture into a
/// <see cref="KanbanMove"/>.
/// </para>
/// <para>
/// The layout is a header row over one scroller. The column headers are <em>not</em> inside the
/// scroller - they are translated to follow its horizontal offset, the same arrangement
/// <c>DataGrid</c>'s frozen columns and <c>GanttView</c>'s time axis use, and the only one that
/// behaves identically on all six platforms.
/// </para>
/// <para>
/// Cards are real views, not drawings. A board's cards are the thing consumers most want to
/// template, and a <see cref="DataTemplate"/> full of arbitrary markup cannot be handed to a
/// <see cref="GraphicsView"/>. It also means the platform's own hit testing, focus and
/// accessibility tree work on a card without any of it being reimplemented.
/// </para>
/// </remarks>
public partial class KanbanView : ContentView, IDisposable
{
    readonly Grid root;
    readonly ContentView headerClip;
    readonly Grid headerRow;
    readonly ScrollView bodyScroll;
    readonly Grid bodyContent;
    readonly VerticalStackLayout lanesHost;
    readonly AbsoluteLayout dropLayer;
    readonly BoxView dropIndicator;
    readonly ContentView emptyHost;

    readonly List<LaneCell> laneCells = [];
    readonly Dictionary<KanbanCard, KanbanCardView> defaultCardViews = [];
    readonly Dictionary<KanbanCard, View> cardHosts = [];
    readonly List<KanbanColumnHeaderView> columnHeaders = [];
    readonly List<RoundRectangle> headerShapes = [];
    readonly List<RoundRectangle> cellShapes = [];
    readonly List<INotifyPropertyChanged> observedItems = [];
    readonly CornerRadiusProbe columnRadius;

    INotifyCollectionChanged? observedCards;
    INotifyCollectionChanged? observedColumns;
    INotifyCollectionChanged? observedSwimlanes;

    bool disposed;

    public KanbanView()
    {
        this.headerRow = new Grid { ColumnSpacing = 0, VerticalOptions = LayoutOptions.Start };

        this.headerClip = new ContentView
        {
            Content = this.headerRow,
            HorizontalOptions = LayoutOptions.Fill
        };

        // IsClippedToBounds alone does not hold the header in - scrolled right, the translated grid
        // paints its earlier columns straight out of the control. An explicit Clip geometry does,
        // but it has to be resized with the viewport, hence the handler rather than an assignment.
        this.headerClip.SizeChanged += (_, _) => this.UpdateHeaderClip();

        this.lanesHost = new VerticalStackLayout
        {
            Spacing = 0,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start
        };

        // The drop indicator lives in its own layer rather than being inserted between two cards.
        // Inserting it would re-lay out the very column the finger is over on every frame of the
        // drag, and on Android a layout pass under an in-flight gesture is how that gesture gets
        // cancelled. Painting a line at a computed position costs nothing and moves nothing.
        this.dropLayer = new AbsoluteLayout { InputTransparent = true };
        this.dropIndicator = new BoxView { HeightRequest = 3, IsVisible = false, InputTransparent = true };
        this.dropLayer.Add(this.dropIndicator);

        this.bodyContent = new Grid
        {
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start
        };
        this.bodyContent.Add(this.lanesHost);
        this.bodyContent.Add(this.dropLayer);

        this.bodyScroll = new ScrollView
        {
            Content = this.bodyContent,
            Orientation = ScrollOrientation.Both
        };
        this.bodyScroll.Scrolled += this.OnBodyScrolled;

        this.emptyHost = new ContentView { IsVisible = false, InputTransparent = true };

        this.root = new Grid
        {
            RowSpacing = 0,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };
        this.root.Add(this.headerClip, 0, 0);
        this.root.Add(this.bodyScroll, 0, 1);
        this.root.Add(this.emptyHost, 0, 1);

        // A header is rounded on top and a well on the bottom, and zeroing two corners means doing
        // arithmetic on the token - which a DynamicResource cannot be read for. The probe resolves it
        // to a number, and re-resolves on a live theme swap.
        this.columnRadius = new CornerRadiusProbe(ShinyThemeKeys.Shape.CornerSmallRadius, this.ApplyColumnRadii);
        this.root.Add(this.columnRadius.View);

        this.Content = this.root;

        this.ApplyThemeChrome();

        StyleGuard.MarkReady(this, typeof(KanbanView));
        this.RebuildBoard();
    }


    /// <summary>The board as it currently stands - lanes, counts and limits. Rebuilt on every change.</summary>
    public KanbanBoard Board { get; private set; } = new(null, null);

    internal CultureInfo EffectiveCulture => this.Culture ?? CultureInfo.CurrentCulture;


    // =============================================================================================
    // One column-by-swimlane bucket, as it exists on screen
    // =============================================================================================

    sealed class LaneCell
    {
        public required KanbanLane Lane { get; init; }
        public required Border Host { get; init; }
        public required VerticalStackLayout Stack { get; init; }
        public List<View> CardViews { get; } = [];
    }


    // =============================================================================================
    // Model
    // =============================================================================================

    /// <summary>
    /// Re-buckets the cards and redraws. Call it after changing a card's column from code when the
    /// collection itself did not change.
    /// </summary>
    public void RebuildBoard()
    {
        if (this.disposed)
            return;

        this.Board = new KanbanBoard(
            Materialize<KanbanColumn>(this.Columns),
            Materialize<KanbanCard>(this.Cards),
            Materialize<KanbanSwimlane>(this.Swimlanes),
            new KanbanBoardOptions
            {
                SwimlaneMode = this.SwimlaneMode,
                WipBehavior = this.WipBehavior
            }
        );

        this.BoardBuilt?.Invoke(this, new KanbanBoardBuiltEventArgs(this.Board));
        this.RenderBoard();
    }


    static IEnumerable<T> Materialize<T>(IEnumerable? source)
        => source?.OfType<T>() ?? [];


    // =============================================================================================
    // Render
    // =============================================================================================

    /// <summary>Rebuilds the visual tree from the board it already has.</summary>
    internal void RenderBoard()
    {
        if (this.disposed)
            return;

        this.CancelDrag();

        this.laneCells.Clear();
        this.columnHeaders.Clear();
        this.headerShapes.Clear();
        this.cellShapes.Clear();
        this.cardHosts.Clear();
        this.defaultCardViews.Clear();
        this.lanesHost.Clear();
        this.headerRow.Clear();
        this.headerRow.ColumnDefinitions.Clear();

        var columns = this.Board.Columns;

        this.emptyHost.IsVisible = columns.Count == 0;
        this.headerClip.IsVisible = columns.Count > 0;
        this.bodyScroll.IsVisible = columns.Count > 0;

        if (columns.Count == 0)
            return;

        var widths = columns.Select(this.EffectiveWidth).ToList();
        var totalWidth = widths.Sum() + (this.ColumnSpacing * Math.Max(0, columns.Count - 1));

        this.BuildHeader(columns, widths);

        var grouped = this.SwimlaneMode == KanbanSwimlaneMode.Grouped && this.Board.Swimlanes.Count > 0;

        if (grouped)
        {
            foreach (var swimlane in this.Board.Swimlanes)
            {
                this.lanesHost.Add(this.BuildSwimlaneHeader(swimlane, totalWidth));

                if (!swimlane.IsCollapsed)
                    this.lanesHost.Add(this.BuildLaneRow(columns, widths, swimlane));
            }
        }
        else
        {
            this.lanesHost.Add(this.BuildLaneRow(columns, widths, null));
        }

        this.lanesHost.WidthRequest = totalWidth;
        this.dropLayer.WidthRequest = totalWidth;

        this.ApplyColumnRadii();
        this.ApplySelection();
        this.UpdateHeaderClip();
    }


    /// <summary>
    /// Rounds each header's top corners and each well's bottom corners to the theme's small radius,
    /// so the two read as one shape.
    /// </summary>
    void ApplyColumnRadii()
    {
        var radius = this.columnRadius.Radius;

        foreach (var shape in this.headerShapes)
            shape.CornerRadius = new CornerRadius(radius, radius, 0, 0);

        foreach (var shape in this.cellShapes)
            shape.CornerRadius = new CornerRadius(0, 0, radius, radius);
    }


    double EffectiveWidth(KanbanColumn column)
        => column.IsCollapsed
            ? this.CollapsedColumnWidth
            : column.Width is > 0 ? column.Width.Value : this.ColumnWidth;


    void BuildHeader(IReadOnlyList<KanbanColumn> columns, IReadOnlyList<double> widths)
    {
        this.headerRow.ColumnSpacing = this.ColumnSpacing;

        for (var i = 0; i < columns.Count; i++)
        {
            this.headerRow.ColumnDefinitions.Add(new ColumnDefinition(widths[i]));

            var column = columns[i];
            View header;

            if (this.ColumnHeaderTemplate is { } template)
            {
                header = BuildTemplate(template, column);
            }
            else
            {
                var built = new KanbanColumnHeaderView(this, column, this.Board.CountIn(column.Id));
                this.columnHeaders.Add(built);
                this.headerShapes.Add(built.Shape);
                this.WireColumnCollapse(built);
                header = built;
            }

            this.headerRow.Add(header, i, 0);
        }
    }


    View BuildSwimlaneHeader(KanbanSwimlane swimlane, double totalWidth)
    {
        if (this.SwimlaneHeaderTemplate is { } template)
        {
            var templated = BuildTemplate(template, swimlane);
            templated.WidthRequest = totalWidth;
            return templated;
        }

        var chevron = new Label
        {
            Text = swimlane.IsCollapsed ? "›" : "⌄",
            WidthRequest = 18,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            IsVisible = this.AllowSwimlaneCollapse && !this.IsReadOnly
        }.WithFontSize(ShinyThemeKeys.Type.LabelMediumSize);
        KanbanChrome.Token(chevron, Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        var title = new Label
        {
            Text = swimlane.Title,
            FontAttributes = FontAttributes.Bold,
            VerticalTextAlignment = TextAlignment.Center
        }.WithFontSize(ShinyThemeKeys.Type.LabelMediumSize);
        KanbanChrome.Token(title, Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        var marker = new BoxView { WidthRequest = 3, HeightRequest = 16, VerticalOptions = LayoutOptions.Center };
        var accent = KanbanChrome.Parse(swimlane.Color);
        marker.IsVisible = accent is not null;
        if (accent is not null)
            marker.Color = accent;

        var content = new HorizontalStackLayout
        {
            Spacing = 6,
            Children = { chevron, marker, title }
        };

        var host = new Border
        {
            Content = content,
            StrokeThickness = 0,
            Padding = new Thickness(4, 8),
            WidthRequest = totalWidth,
            Margin = new Thickness(0, 10, 0, 4)
        };
        KanbanChrome.Token(host, BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerLow);

        if (this.AllowSwimlaneCollapse && !this.IsReadOnly)
        {
            var tap = new TapGestureRecognizer
            {
                // Command rather than the Tapped event: a command is assertable from a test, and a
                // Tapped handler is not raisable at all without a platform.
                Command = new Command(() => this.ToggleSwimlane(swimlane))
            };
            host.GestureRecognizers.Add(tap);
        }

        return host;
    }


    Grid BuildLaneRow(IReadOnlyList<KanbanColumn> columns, IReadOnlyList<double> widths, KanbanSwimlane? swimlane)
    {
        var grid = new Grid { ColumnSpacing = this.ColumnSpacing };

        for (var i = 0; i < columns.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(widths[i]));

            var column = columns[i];
            var lane = this.Board.Lane(column.Id, swimlane?.Id) ?? new KanbanLane(column, swimlane, [], 0);

            var cell = this.BuildLaneCell(lane);
            this.laneCells.Add(cell);
            grid.Add(cell.Host, i, 0);
        }

        return grid;
    }


    LaneCell BuildLaneCell(KanbanLane lane)
    {
        var stack = new VerticalStackLayout { Spacing = this.CardSpacing };

        var shape = new RoundRectangle();
        this.cellShapes.Add(shape);

        var host = new Border
        {
            Content = stack,
            StrokeThickness = 0,
            Padding = this.ColumnPadding,
            MinimumHeightRequest = this.MinColumnHeight,
            VerticalOptions = LayoutOptions.Fill,
            StrokeShape = shape
        };
        KanbanChrome.Token(host, BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerLow);

        var cell = new LaneCell { Lane = lane, Host = host, Stack = stack };

        // A collapsed column still counts and is still a drop target, but it draws nothing: the
        // whole point of collapsing it is to get its cards off the screen.
        if (lane.Column.IsCollapsed)
            return cell;

        foreach (var card in lane.Cards)
        {
            var view = this.BuildCardView(card);
            cell.CardViews.Add(view);
            stack.Add(view);
        }

        if (lane.Cards.Count == 0)
            stack.Add(this.BuildEmptyPlaceholder(lane.Column));

        if (this.AddCardMode != KanbanAddCardMode.None && !this.IsReadOnly && lane.Column.AllowAdd)
            stack.Add(this.BuildAddAffordance(lane));

        return cell;
    }


    View BuildEmptyPlaceholder(KanbanColumn column)
    {
        if (this.EmptyColumnTemplate is { } template)
            return BuildTemplate(template, column);

        var label = new Label
        {
            Text = this.EmptyColumnText,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 16),
            InputTransparent = true,
            IsVisible = !String.IsNullOrEmpty(this.EmptyColumnText)
        }.WithFontSize(ShinyThemeKeys.Type.BodySmallSize);
        KanbanChrome.Token(label, Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        label.Opacity = 0.7;
        return label;
    }


    View BuildCardView(KanbanCard card)
    {
        View content;

        if (this.CardTemplate is { } template)
        {
            content = BuildTemplate(template, card);
        }
        else
        {
            var built = new KanbanCardView(this, card);
            this.defaultCardViews[card] = built;
            content = built;
        }

        // Everything the drag touches - the pan, the raise, the selection ring - goes on this host
        // rather than on the content, so a custom template is not obliged to be a Border, or to
        // leave room for a ring it knows nothing about.
        var host = new Border
        {
            Content = content,
            StrokeThickness = 2,
            Stroke = Colors.Transparent,
            BackgroundColor = Colors.Transparent,
            Padding = 0,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerMediumRadius)
        };

        this.cardHosts[card] = host;
        this.WireCard(host, card);

        return host;
    }


    static View BuildTemplate(DataTemplate template, object context)
    {
        var content = template.CreateContent();
        var view = content as View ?? (content as ViewCell)?.View;

        if (view is null)
            return new ContentView();

        view.BindingContext = context;
        return view;
    }


    View BuildAddAffordance(KanbanLane lane)
    {
        var button = new Label
        {
            Text = "+  Add card",
            Padding = new Thickness(6, 8)
        }.WithFontSize(ShinyThemeKeys.Type.BodySmallSize);
        KanbanChrome.Token(button, Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        var entry = new Entry
        {
            Placeholder = "Card title",
            IsVisible = false,
            ReturnType = ReturnType.Done
        }.WithFontSize(ShinyThemeKeys.Type.BodySmallSize);

        var host = new Grid { Children = { button, entry } };

        if (this.AddCardMode == KanbanAddCardMode.Button)
        {
            button.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => this.RaiseAddCard(lane, String.Empty))
            });
            return host;
        }

        button.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                button.IsVisible = false;
                entry.IsVisible = true;
                entry.Focus();
            })
        });

        entry.Completed += (_, _) =>
        {
            var text = entry.Text ?? String.Empty;
            entry.Text = String.Empty;
            entry.IsVisible = false;
            button.IsVisible = true;

            if (!String.IsNullOrWhiteSpace(text))
                this.RaiseAddCard(lane, text.Trim());
        };

        entry.Unfocused += (_, _) =>
        {
            entry.IsVisible = false;
            button.IsVisible = true;
        };

        return host;
    }


    void RaiseAddCard(KanbanLane lane, string title)
    {
        var args = new KanbanAddCardEventArgs(lane.Column, lane.Swimlane) { Title = title };
        this.AddCardRequested?.Invoke(this, args);

        if (this.AddCardCommand?.CanExecute(args) == true)
            this.AddCardCommand.Execute(args);
    }


    // =============================================================================================
    // Chrome, scroll and selection
    // =============================================================================================

    internal void ApplyThemeChrome()
    {
        KanbanChrome.Token(this, BackgroundColorProperty, ShinyThemeKeys.Color.Background);
        KanbanChrome.Token(this.dropIndicator, BoxView.ColorProperty, ShinyThemeKeys.Color.Primary);
    }


    void OnBodyScrolled(object? sender, ScrolledEventArgs e)
        => this.headerRow.TranslationX = -e.ScrollX;


    void UpdateHeaderClip()
    {
        if (this.headerClip.Width <= 0 || this.headerClip.Height <= 0)
            return;

        this.headerClip.Clip = new RectangleGeometry(
            new Rect(0, 0, this.headerClip.Width, this.headerClip.Height)
        );
    }


    internal void ApplySelection()
    {
        var selected = this.SelectedCard;

        foreach (var (card, host) in this.cardHosts)
        {
            if (host is not Border border)
                continue;

            if (ReferenceEquals(card, selected))
                KanbanChrome.Token(border, Border.StrokeProperty, ShinyThemeKeys.Color.Primary);
            else
                border.Stroke = Colors.Transparent;
        }
    }


    // =============================================================================================
    // Observation
    // =============================================================================================

    void OnItemsChanged(object? oldValue, object? newValue)
    {
        Unhook(ref this.observedCards, this.OnCollectionChanged);
        Unhook(ref this.observedColumns, this.OnCollectionChanged);
        Unhook(ref this.observedSwimlanes, this.OnCollectionChanged);

        this.observedCards = Hook(this.Cards, this.OnCollectionChanged);
        this.observedColumns = Hook(this.Columns, this.OnCollectionChanged);
        this.observedSwimlanes = Hook(this.Swimlanes, this.OnCollectionChanged);

        this.HookItems();
        this.RebuildBoard();
    }


    static INotifyCollectionChanged? Hook(IEnumerable? source, NotifyCollectionChangedEventHandler handler)
    {
        if (source is not INotifyCollectionChanged observable)
            return null;

        observable.CollectionChanged += handler;
        return observable;
    }


    static void Unhook(ref INotifyCollectionChanged? observed, NotifyCollectionChangedEventHandler handler)
    {
        if (observed is not null)
            observed.CollectionChanged -= handler;

        observed = null;
    }


    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        this.HookItems();
        this.RebuildBoard();
    }


    /// <summary>
    /// Watches every model object for its own property changes, so a renamed card or a changed WIP
    /// limit redraws without the consumer replacing the collection.
    /// </summary>
    void HookItems()
    {
        foreach (var item in this.observedItems)
            item.PropertyChanged -= this.OnItemPropertyChanged;

        this.observedItems.Clear();

        foreach (var item in Enumerate(this.Cards).Concat(Enumerate(this.Columns)).Concat(Enumerate(this.Swimlanes)))
        {
            item.PropertyChanged += this.OnItemPropertyChanged;
            this.observedItems.Add(item);
        }

        static IEnumerable<INotifyPropertyChanged> Enumerate(IEnumerable? source)
            => source?.OfType<INotifyPropertyChanged>() ?? [];
    }


    void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A card moving column, a column changing width or collapsing, a swimlane collapsing: all of
        // those change what goes where, so they rebuild. Everything else - a title, a colour, a
        // label - only changes what a card says, and repainting that one card is enough.
        var structural = e.PropertyName is
            nameof(KanbanCard.ColumnId) or
            nameof(KanbanCard.SwimlaneId) or
            nameof(KanbanCard.Order) or
            nameof(KanbanColumn.IsCollapsed) or
            nameof(KanbanColumn.Width) or
            nameof(KanbanColumn.WipLimit) or
            nameof(KanbanColumn.AllowAdd) or
            nameof(KanbanSwimlane.Title);

        if (structural)
        {
            this.RebuildBoard();
            return;
        }

        if (sender is KanbanCard card && this.defaultCardViews.TryGetValue(card, out var view))
            view.Refresh();
    }


    // =============================================================================================
    // Public API
    // =============================================================================================

    /// <summary>
    /// Moves a card from code, through exactly the same path a drag takes - including
    /// <see cref="CardMoving"/>, the WIP verdict and <see cref="CardMoved"/>.
    /// </summary>
    /// <param name="card">The card to move.</param>
    /// <param name="columnId">The destination column.</param>
    /// <param name="swimlaneId">The destination swimlane, or null to keep the one it is in.</param>
    /// <param name="index">Where in the destination lane. Negative appends.</param>
    /// <returns>True when the move was allowed and applied.</returns>
    public bool MoveCard(KanbanCard card, string columnId, string? swimlaneId = null, int index = -1)
    {
        var lane = swimlaneId ?? card.SwimlaneId;
        var move = this.Board.PlanMove(card, columnId, lane, index);

        if (move is null)
        {
            this.RaiseRejected(card, this.Board.Evaluate(card, columnId, lane).Reason, columnId);
            return false;
        }

        return this.CommitMove(move);
    }


    /// <summary>Collapses or expands a column, raising <see cref="ColumnCollapseChanged"/>.</summary>
    public void ToggleColumn(KanbanColumn column)
    {
        column.IsCollapsed = !column.IsCollapsed;
        this.ColumnCollapseChanged?.Invoke(this, new KanbanColumnEventArgs(column));
    }


    /// <summary>Collapses or expands a swimlane band.</summary>
    public void ToggleSwimlane(KanbanSwimlane swimlane)
        => swimlane.IsCollapsed = !swimlane.IsCollapsed;


    /// <summary>Collapses every column.</summary>
    public void CollapseAllColumns() => this.SetAllColumns(true);

    /// <summary>Expands every column.</summary>
    public void ExpandAllColumns() => this.SetAllColumns(false);


    void SetAllColumns(bool collapsed)
    {
        foreach (var column in this.Board.Columns)
            column.IsCollapsed = collapsed;
    }


    /// <summary>Brings a card into view, scrolling in both axes.</summary>
    public Task ScrollToCard(KanbanCard card, bool animated = true)
    {
        if (!this.cardHosts.TryGetValue(card, out var host))
            return Task.CompletedTask;

        return this.bodyScroll.ScrollToAsync(host, ScrollToPosition.MakeVisible, animated);
    }


    /// <summary>Brings a column into view horizontally.</summary>
    public Task ScrollToColumn(KanbanColumn column, bool animated = true)
    {
        var index = this.Board.Columns.ToList().FindIndex(c => ReferenceEquals(c, column));
        if (index < 0)
            return Task.CompletedTask;

        var x = this.Board.Columns
            .Take(index)
            .Sum(c => this.EffectiveWidth(c) + this.ColumnSpacing);

        return this.bodyScroll.ScrollToAsync(x, this.bodyScroll.ScrollY, animated);
    }


    // =============================================================================================
    // Teardown
    // =============================================================================================

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;

        Unhook(ref this.observedCards, this.OnCollectionChanged);
        Unhook(ref this.observedColumns, this.OnCollectionChanged);
        Unhook(ref this.observedSwimlanes, this.OnCollectionChanged);

        foreach (var item in this.observedItems)
            item.PropertyChanged -= this.OnItemPropertyChanged;

        this.observedItems.Clear();
        this.bodyScroll.Scrolled -= this.OnBodyScrolled;
        this.ReleaseDragHooks();

        GC.SuppressFinalize(this);
    }
}

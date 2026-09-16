using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls.Kanban;

/// <summary>
/// A Kanban board: columns of cards you drag between, with WIP limits, swimlanes, collapsible
/// columns and an inline add-card composer.
/// </summary>
/// <remarks>
/// <para>
/// The bucketing, the WIP arithmetic and every drop decision live in <see cref="KanbanBoard"/>,
/// which is shared - file for file - with the MAUI control and has no idea a DOM exists. This
/// component is the DOM half: markup, the pointer drag, and turning a drop into a
/// <see cref="KanbanMove"/>.
/// </para>
/// <para>
/// The column headers are a <c>position: sticky</c> row inside the same scroller as the lanes, so
/// they follow a horizontal scroll for free and pin on a vertical one. Nothing measures them and
/// nothing translates them; the browser already knows how to do this.
/// </para>
/// </remarks>
public partial class KanbanView : ComponentBase, IAsyncDisposable
{
    [Inject] IJSRuntime JS { get; set; } = default!;

    ElementReference rootElement;
    IJSObjectReference? module;
    DotNetObjectReference<KanbanView>? selfRef;
    bool attached;
    bool rendered;

    readonly List<INotifyPropertyChanged> observedItems = [];
    INotifyCollectionChanged? observedCards;
    INotifyCollectionChanged? observedColumns;
    INotifyCollectionChanged? observedSwimlanes;

    string? composingLane;
    string composingTitle = string.Empty;


    // =============================================================================================
    // Parameters - data
    // =============================================================================================

    /// <summary>
    /// The cards. Map your own type onto a <see cref="KanbanCard"/> and hang the original off
    /// <see cref="KanbanCard.Item"/>. An observable collection is watched, as is each card.
    /// </summary>
    [Parameter] public IEnumerable<KanbanCard>? Cards { get; set; }

    /// <summary>The columns, left to right.</summary>
    [Parameter] public IEnumerable<KanbanColumn>? Columns { get; set; }

    /// <summary>
    /// The swimlane bands, top to bottom. Ignored unless <see cref="SwimlaneMode"/> is
    /// <see cref="KanbanSwimlaneMode.Grouped"/>.
    /// </summary>
    [Parameter] public IEnumerable<KanbanSwimlane>? Swimlanes { get; set; }


    // =============================================================================================
    // Parameters - behaviour
    // =============================================================================================

    /// <summary>Whether cards are grouped into swimlane bands.</summary>
    [Parameter] public KanbanSwimlaneMode SwimlaneMode { get; set; } = KanbanSwimlaneMode.None;

    /// <summary>
    /// What a full column does to an incoming card. <see cref="KanbanWipBehavior.Warn"/> styles the
    /// column and lets the drop land; <see cref="KanbanWipBehavior.Block"/> refuses it.
    /// </summary>
    [Parameter] public KanbanWipBehavior WipBehavior { get; set; } = KanbanWipBehavior.Warn;

    /// <summary>Cards can be dragged. Defaults to true.</summary>
    [Parameter] public bool AllowDragDrop { get; set; } = true;

    /// <summary>Nothing can be dragged, collapsed or added. Wins over every other permission.</summary>
    [Parameter] public bool IsReadOnly { get; set; }

    /// <summary>Column headers get a collapse chevron.</summary>
    [Parameter] public bool AllowColumnCollapse { get; set; } = true;

    /// <summary>Swimlane headings get a collapse chevron.</summary>
    [Parameter] public bool AllowSwimlaneCollapse { get; set; } = true;

    /// <summary>
    /// The add-card affordance at the foot of each column. The board never creates a card of its
    /// own - this only raises <see cref="OnAddCardRequested"/>.
    /// </summary>
    [Parameter] public KanbanAddCardMode AddCardMode { get; set; } = KanbanAddCardMode.None;


    // =============================================================================================
    // Parameters - layout and display
    // =============================================================================================

    /// <summary>Width of one column in pixels. Defaults to 280.</summary>
    [Parameter] public double ColumnWidth { get; set; } = 280;

    /// <summary>Width of a collapsed column's spine. Defaults to 52.</summary>
    [Parameter] public double CollapsedColumnWidth { get; set; } = 52;

    /// <summary>Gap between columns. Defaults to 12.</summary>
    [Parameter] public double ColumnSpacing { get; set; } = 12;

    /// <summary>Gap between cards within a column. Defaults to 8.</summary>
    [Parameter] public double CardSpacing { get; set; } = 8;

    /// <summary>How tall an empty column stays, so it is still a drop target. Defaults to 120.</summary>
    [Parameter] public double MinColumnHeight { get; set; } = 120;

    /// <summary>An over-limit column is styled as over-limit. Independent of <see cref="WipBehavior"/>.</summary>
    [Parameter] public bool ShowWipLimits { get; set; } = true;

    /// <summary>What the column header shows beside its title. Defaults to <c>3 / 5</c>.</summary>
    [Parameter] public KanbanColumnCount ColumnCountDisplay { get; set; } = KanbanColumnCount.CountAndLimit;

    /// <summary>Label chips are drawn across the top of the default card.</summary>
    [Parameter] public bool ShowLabels { get; set; } = true;

    /// <summary>The assignee avatar and name are drawn in the default card's footer.</summary>
    [Parameter] public bool ShowAssignee { get; set; } = true;

    /// <summary>The due date is drawn in the default card's footer.</summary>
    [Parameter] public bool ShowDueDate { get; set; } = true;

    /// <summary>How many lines of <see cref="KanbanCard.Description"/> the default card shows. Zero hides it.</summary>
    [Parameter] public int DescriptionLineLimit { get; set; } = 2;

    /// <summary>How far ahead a due date counts as "due soon" and is tinted. Defaults to two days.</summary>
    [Parameter] public TimeSpan DueSoonWindow { get; set; } = TimeSpan.FromDays(2);

    /// <summary>Format string for the footer's due date.</summary>
    [Parameter] public string DueDateFormat { get; set; } = "d MMM";

    /// <summary>Placeholder in an empty column. Set to an empty string for a bare well.</summary>
    [Parameter] public string EmptyColumnText { get; set; } = "Nothing here";

    /// <summary>Shown when the board has no columns at all.</summary>
    [Parameter] public string EmptyBoardText { get; set; } = "No columns";

    /// <summary>Culture for the due date and the column counts. Null takes the current culture.</summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>Extra classes on the root element.</summary>
    [Parameter] public string? Class { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }


    // =============================================================================================
    // Parameters - templates
    // =============================================================================================

    /// <summary>Replaces the whole card. The drag still belongs to the board, which wraps it.</summary>
    [Parameter] public RenderFragment<KanbanCard>? CardTemplate { get; set; }

    /// <summary>Replaces the column header.</summary>
    [Parameter] public RenderFragment<KanbanColumn>? ColumnHeaderTemplate { get; set; }

    /// <summary>Replaces the swimlane heading.</summary>
    [Parameter] public RenderFragment<KanbanSwimlane>? SwimlaneHeaderTemplate { get; set; }

    /// <summary>Replaces the placeholder in an empty column.</summary>
    [Parameter] public RenderFragment<KanbanColumn>? EmptyColumnTemplate { get; set; }


    // =============================================================================================
    // Parameters - selection and callbacks
    // =============================================================================================

    /// <summary>The selected card. Supports <c>@bind-SelectedCard</c>.</summary>
    [Parameter] public KanbanCard? SelectedCard { get; set; }

    [Parameter] public EventCallback<KanbanCard?> SelectedCardChanged { get; set; }

    /// <summary>Raised before a card moves. Set <c>Cancel</c> on the argument to refuse the drop.</summary>
    [Parameter] public EventCallback<KanbanCardMovingEventArgs> OnCardMoving { get; set; }

    /// <summary>Raised after a card has moved and the model has been rewritten.</summary>
    [Parameter] public EventCallback<KanbanCardMovedEventArgs> OnCardMoved { get; set; }

    /// <summary>Raised when a drop is refused - by a WIP limit, a locked card, or a closed column.</summary>
    [Parameter] public EventCallback<KanbanDropRejectedEventArgs> OnDropRejected { get; set; }

    /// <summary>Raised when a card is clicked.</summary>
    [Parameter] public EventCallback<KanbanCard> OnCardTapped { get; set; }

    /// <summary>Raised when a column is collapsed or expanded.</summary>
    [Parameter] public EventCallback<KanbanColumn> OnColumnCollapseChanged { get; set; }

    /// <summary>Raised when the user asks for a new card. The board does not create one.</summary>
    [Parameter] public EventCallback<KanbanAddCardEventArgs> OnAddCardRequested { get; set; }

    /// <summary>Raised after the board has been re-bucketed, before it renders.</summary>
    [Parameter] public EventCallback<KanbanBoard> OnBoardBuilt { get; set; }


    // =============================================================================================
    // Board
    // =============================================================================================

    /// <summary>The board as it currently stands - lanes, counts and limits.</summary>
    public KanbanBoard Board { get; private set; } = new(null, null);

    internal CultureInfo EffectiveCulture => this.Culture ?? CultureInfo.CurrentCulture;

    bool Grouped => this.SwimlaneMode == KanbanSwimlaneMode.Grouped && this.Board.Swimlanes.Count > 0;


    protected override void OnParametersSet()
    {
        this.HookCollections();
        this.RebuildBoard(render: false);
    }


    /// <summary>
    /// Re-buckets the cards. Call it after changing a card's column from code when the collection
    /// itself did not change.
    /// </summary>
    public void RebuildBoard() => this.RebuildBoard(render: true);


    void RebuildBoard(bool render)
    {
        this.Board = new KanbanBoard(
            this.Columns,
            this.Cards,
            this.Swimlanes,
            new KanbanBoardOptions
            {
                SwimlaneMode = this.SwimlaneMode,
                WipBehavior = this.WipBehavior
            }
        );

        if (this.OnBoardBuilt.HasDelegate)
            _ = this.OnBoardBuilt.InvokeAsync(this.Board);

        if (render)
            this.Refresh();
    }


    // =============================================================================================
    // Observation
    // =============================================================================================

    void HookCollections()
    {
        Rehook(ref this.observedCards, this.Cards, this.OnCollectionChanged);
        Rehook(ref this.observedColumns, this.Columns, this.OnCollectionChanged);
        Rehook(ref this.observedSwimlanes, this.Swimlanes, this.OnCollectionChanged);
        this.HookItems();
    }


    static void Rehook(ref INotifyCollectionChanged? observed, object? source, NotifyCollectionChangedEventHandler handler)
    {
        var next = source as INotifyCollectionChanged;
        if (ReferenceEquals(observed, next))
            return;

        if (observed is not null)
            observed.CollectionChanged -= handler;

        observed = next;

        if (observed is not null)
            observed.CollectionChanged += handler;
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

        static IEnumerable<INotifyPropertyChanged> Enumerate(System.Collections.IEnumerable? source)
            => source?.OfType<INotifyPropertyChanged>() ?? [];
    }


    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => this.Notify(() =>
        {
            this.HookItems();
            this.RebuildBoard(render: false);
        });


    void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => this.Notify(() => this.RebuildBoard(render: false));


    /// <summary>
    /// Runs work on the renderer's thread and repaints - or, before the component has ever rendered,
    /// just runs it.
    /// </summary>
    /// <remarks>
    /// Both <see cref="ComponentBase.InvokeAsync(Action)"/> and
    /// <see cref="ComponentBase.StateHasChanged"/> throw "The render handle is not yet assigned"
    /// until the component is attached to a renderer. That is reachable ordinary usage, not a test
    /// artefact: a consumer who calls <see cref="RebuildBoard()"/> or <see cref="ToggleColumn"/> from
    /// <c>OnInitialized</c> hits it. There is nothing to repaint before the first render anyway, so
    /// the guard costs nothing.
    /// </remarks>
    void Notify(Action work)
    {
        if (!this.rendered)
        {
            work();
            return;
        }

        _ = this.InvokeAsync(() =>
        {
            work();
            this.StateHasChanged();
        });
    }


    /// <summary>Repaints, or does nothing when the component has yet to render. See <see cref="Notify"/>.</summary>
    void Refresh()
    {
        if (this.rendered)
            this.StateHasChanged();
    }


    // =============================================================================================
    // Public API
    // =============================================================================================

    /// <summary>
    /// Moves a card from code, through exactly the same path a drop takes - including
    /// <see cref="OnCardMoving"/>, the WIP verdict and <see cref="OnCardMoved"/>.
    /// </summary>
    /// <returns>True when the move was allowed and applied.</returns>
    public async Task<bool> MoveCardAsync(KanbanCard card, string columnId, string? swimlaneId = null, int index = -1)
    {
        var lane = swimlaneId ?? card.SwimlaneId;
        var move = this.Board.PlanMove(card, columnId, lane, index);

        if (move is null)
        {
            await this.RaiseRejected(card, this.Board.Evaluate(card, columnId, lane).Reason, columnId);
            return false;
        }

        return await this.CommitMove(move);
    }


    /// <summary>Collapses or expands a column.</summary>
    public void ToggleColumn(KanbanColumn column)
    {
        column.IsCollapsed = !column.IsCollapsed;
        this.RebuildBoard(render: true);

        if (this.OnColumnCollapseChanged.HasDelegate)
            _ = this.OnColumnCollapseChanged.InvokeAsync(column);
    }


    /// <summary>Collapses or expands a swimlane band.</summary>
    public void ToggleSwimlane(KanbanSwimlane swimlane)
    {
        swimlane.IsCollapsed = !swimlane.IsCollapsed;
        this.RebuildBoard(render: true);
    }


    /// <summary>Collapses every column.</summary>
    public void CollapseAllColumns() => this.SetAllColumns(true);

    /// <summary>Expands every column.</summary>
    public void ExpandAllColumns() => this.SetAllColumns(false);


    void SetAllColumns(bool collapsed)
    {
        foreach (var column in this.Board.Columns)
            column.IsCollapsed = collapsed;

        this.RebuildBoard(render: true);
    }


    /// <summary>Brings a card into view, scrolling in both axes.</summary>
    public async Task ScrollToCardAsync(KanbanCard card)
    {
        if (this.module is null)
            return;

        await this.module.InvokeVoidAsync("scrollToCard", this.rootElement, card.Id);
    }


    // =============================================================================================
    // Selection and the composer
    // =============================================================================================

    void SelectCard(KanbanCard card)
    {
        this.SelectedCard = card;

        if (this.SelectedCardChanged.HasDelegate)
            _ = this.SelectedCardChanged.InvokeAsync(card);

        if (this.OnCardTapped.HasDelegate)
            _ = this.OnCardTapped.InvokeAsync(card);
    }


    /// <remarks>
    /// A card is focusable, so it has to answer the keyboard: Enter and Space are what a screen
    /// reader user presses where a mouse user clicks.
    /// </remarks>
    void OnCardKeyDown(KeyboardEventArgs e, KanbanCard card)
    {
        if (e.Key is "Enter" or " ")
            this.SelectCard(card);
    }


    static string LaneKey(KanbanColumn column, KanbanSwimlane? swimlane)
        => $"{column.Id}{swimlane?.Id ?? string.Empty}";


    void OnAddPressed(KanbanColumn column, KanbanSwimlane? swimlane)
    {
        if (this.AddCardMode == KanbanAddCardMode.Inline)
        {
            this.composingLane = LaneKey(column, swimlane);
            this.composingTitle = string.Empty;
            return;
        }

        this.RaiseAddCard(column, swimlane, string.Empty);
    }


    void OnComposerKeyDown(KeyboardEventArgs e, KanbanColumn column, KanbanSwimlane? swimlane)
    {
        if (e.Key == "Escape")
        {
            this.CancelComposer();
            return;
        }

        if (e.Key != "Enter")
            return;

        var title = this.composingTitle.Trim();
        this.CancelComposer();

        if (title.Length > 0)
            this.RaiseAddCard(column, swimlane, title);
    }


    void CancelComposer()
    {
        this.composingLane = null;
        this.composingTitle = string.Empty;
    }


    void RaiseAddCard(KanbanColumn column, KanbanSwimlane? swimlane, string title)
    {
        if (!this.OnAddCardRequested.HasDelegate)
            return;

        _ = this.OnAddCardRequested.InvokeAsync(new KanbanAddCardEventArgs(column, swimlane) { Title = title });
    }


    // =============================================================================================
    // View helpers - everything the markup calls
    // =============================================================================================

    static string Px(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "px";

    double EffectiveWidth(KanbanColumn column)
        => column.IsCollapsed
            ? this.CollapsedColumnWidth
            : column.Width is > 0 ? column.Width.Value : this.ColumnWidth;

    double TotalWidth()
        => this.Board.Columns.Sum(this.EffectiveWidth) +
           (this.ColumnSpacing * Math.Max(0, this.Board.Columns.Count - 1));

    string CellStyle(KanbanColumn column)
        => $"width:{Px(this.EffectiveWidth(column))};min-width:{Px(this.EffectiveWidth(column))}";

    /// <remarks>
    /// The spacings ride on the content element rather than on the root, because the root is where
    /// a caller's splatted <c>style</c> lands - and a splatted style replaces the component's own
    /// outright, which would take every one of these custom properties with it.
    /// </remarks>
    string ContentStyle()
        => $"width:{Px(this.TotalWidth())};" +
           $"--shiny-kanban-col-gap:{Px(this.ColumnSpacing)};" +
           $"--shiny-kanban-card-gap:{Px(this.CardSpacing)};" +
           $"--shiny-kanban-cell-min-h:{Px(this.MinColumnHeight)}";

    bool IsOver(KanbanColumn column) => this.ShowWipLimits && this.Board.IsOverLimit(column.Id);

    string CountLabel(KanbanColumn column)
    {
        var count = this.Board.CountIn(column.Id);
        var limit = column.WipLimit is > 0 ? column.WipLimit : null;

        return this.ColumnCountDisplay switch
        {
            KanbanColumnCount.None => string.Empty,
            KanbanColumnCount.Count => count.ToString(this.EffectiveCulture),
            _ => limit is null ? count.ToString(this.EffectiveCulture) : $"{count} / {limit}"
        };
    }

    static string LabelStyle(KanbanLabel label)
        => string.IsNullOrWhiteSpace(label.Color) ? string.Empty : $"background:{label.Color}";

    bool HasAssignee(KanbanCard card)
        => !string.IsNullOrWhiteSpace(card.AssigneeName) || !string.IsNullOrWhiteSpace(card.AssigneeImage);

    bool HasFooter(KanbanCard card)
        => (this.ShowAssignee && this.HasAssignee(card)) || (this.ShowDueDate && card.DueDate is not null);

    string DueClass(DateTimeOffset due)
    {
        var now = DateTimeOffset.Now;

        if (due < now)
            return "is-overdue";

        return due - now <= this.DueSoonWindow ? "is-soon" : string.Empty;
    }

    static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "?";

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant()
        };
    }


    // =============================================================================================
    // Teardown
    // =============================================================================================

    public async ValueTask DisposeAsync()
    {
        foreach (var item in this.observedItems)
            item.PropertyChanged -= this.OnItemPropertyChanged;

        this.observedItems.Clear();

        if (this.observedCards is not null)
            this.observedCards.CollectionChanged -= this.OnCollectionChanged;

        if (this.observedColumns is not null)
            this.observedColumns.CollectionChanged -= this.OnCollectionChanged;

        if (this.observedSwimlanes is not null)
            this.observedSwimlanes.CollectionChanged -= this.OnCollectionChanged;

        if (this.module is not null)
        {
            try
            {
                await this.module.InvokeVoidAsync("detach", this.rootElement);
                await this.module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The circuit went away before the component did. Nothing to detach from.
            }
        }

        this.selfRef?.Dispose();
        GC.SuppressFinalize(this);
    }
}

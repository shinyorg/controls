namespace Shiny.Maui.Controls.Kanban;

/// <summary>
/// The board itself: cards bucketed into lanes, WIP limits counted, and every drop judged and
/// planned. Deliberately free of MAUI types - it is pure bookkeeping over the model objects.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here touches a view, which is the point: "may this card land here", "where exactly does
/// it land" and "what does that do to every other card's order" are the three questions a Kanban
/// gets wrong, and all three are answerable - and unit testable - without a screen.
/// </para>
/// <para>
/// This file is mirrored, near line for line, by
/// <c>Shiny.Blazor.Controls/Kanban/KanbanBoard.cs</c>. It compiles against each host's own
/// <see cref="KanbanCard"/>, <see cref="KanbanColumn"/> and <see cref="KanbanSwimlane"/>, which
/// carry the same members on both sides. Fix a bug here and fix it there.
/// </para>
/// </remarks>
public sealed class KanbanBoard
{
    readonly Dictionary<string, KanbanColumn> columnsById;
    readonly Dictionary<(string Column, string Swimlane), KanbanLane> lanesByKey;
    readonly Dictionary<string, int> countsByColumn;

    public KanbanBoard(
        IEnumerable<KanbanColumn>? columns,
        IEnumerable<KanbanCard>? cards,
        IEnumerable<KanbanSwimlane>? swimlanes = null,
        KanbanBoardOptions? options = null
    )
    {
        this.Options = options ?? new KanbanBoardOptions();

        this.Columns = columns?.Where(static c => c is not null).ToList() ?? [];
        this.Swimlanes = this.Options.SwimlaneMode == KanbanSwimlaneMode.Grouped
            ? swimlanes?.Where(static s => s is not null).ToList() ?? []
            : [];

        this.columnsById = new Dictionary<string, KanbanColumn>(StringComparer.Ordinal);
        foreach (var column in this.Columns)
        {
            // Last one wins rather than throwing: a duplicated id is a consumer bug that should
            // show up as a card in the wrong place, not as an exception out of a property setter.
            this.columnsById[column.Id ?? string.Empty] = column;
        }

        this.lanesByKey = [];
        this.countsByColumn = new Dictionary<string, int>(StringComparer.Ordinal);

        var orphans = new List<KanbanCard>();
        var buckets = new Dictionary<(string, string), List<KanbanCard>>();

        var sequence = 0;
        var ordering = new Dictionary<KanbanCard, int>();

        foreach (var card in cards ?? [])
        {
            if (card is null)
                continue;

            ordering[card] = sequence++;

            var columnId = card.ColumnId ?? string.Empty;
            if (!this.columnsById.ContainsKey(columnId))
            {
                orphans.Add(card);
                continue;
            }

            var swimlaneId = this.ResolveSwimlaneId(card);
            var key = (columnId, swimlaneId);

            if (!buckets.TryGetValue(key, out var bucket))
                buckets[key] = bucket = [];

            bucket.Add(card);

            this.countsByColumn[columnId] = this.countsByColumn.GetValueOrDefault(columnId) + 1;
        }

        this.Orphans = orphans;

        // Every column crossed with every swimlane, whether or not it holds a card - an empty lane
        // is still a drop target, and building them lazily would mean the first drop into an empty
        // column had nowhere to go.
        var lanes = new List<KanbanLane>();
        var laneSwimlanes = this.Options.SwimlaneMode == KanbanSwimlaneMode.Grouped && this.Swimlanes.Count > 0
            ? this.Swimlanes.Select(static s => (KanbanSwimlane?)s).ToList()
            : [null];

        foreach (var swimlane in laneSwimlanes)
        {
            foreach (var column in this.Columns)
            {
                var columnId = column.Id ?? string.Empty;
                var swimlaneId = swimlane?.Id ?? string.Empty;

                buckets.TryGetValue((columnId, swimlaneId), out var bucket);

                var sorted = bucket is null
                    ? new List<KanbanCard>()
                    : bucket
                        .OrderBy(static c => c.Order)
                        .ThenBy(c => ordering[c])
                        .ToList();

                var lane = new KanbanLane(column, swimlane, sorted, this.countsByColumn.GetValueOrDefault(columnId));
                lanes.Add(lane);
                this.lanesByKey[(columnId, swimlaneId)] = lane;
            }
        }

        this.Lanes = lanes;
    }


    /// <summary>How the board was assembled - swimlane mode and what an over-limit column does.</summary>
    public KanbanBoardOptions Options { get; }

    /// <summary>The columns, in the order they were supplied.</summary>
    public IReadOnlyList<KanbanColumn> Columns { get; }

    /// <summary>The swimlanes, or empty when <see cref="KanbanSwimlaneMode.None"/> is in force.</summary>
    public IReadOnlyList<KanbanSwimlane> Swimlanes { get; }

    /// <summary>Every column-by-swimlane bucket, including the empty ones.</summary>
    public IReadOnlyList<KanbanLane> Lanes { get; }

    /// <summary>
    /// Cards whose <see cref="KanbanCard.ColumnId"/> matched no column. They are not rendered.
    /// </summary>
    /// <remarks>
    /// Surfaced rather than dropped silently: a typo in a column id otherwise presents as cards that
    /// simply do not appear, with nothing anywhere to say why.
    /// </remarks>
    public IReadOnlyList<KanbanCard> Orphans { get; }


    /// <summary>The bucket for one column and swimlane, or null when either is unknown.</summary>
    public KanbanLane? Lane(string? columnId, string? swimlaneId)
        => this.lanesByKey.GetValueOrDefault((columnId ?? string.Empty, swimlaneId ?? string.Empty));

    /// <summary>The column with this id, or null.</summary>
    public KanbanColumn? Column(string? columnId)
        => this.columnsById.GetValueOrDefault(columnId ?? string.Empty);

    /// <summary>
    /// How many cards the column holds across <em>every</em> swimlane.
    /// </summary>
    /// <remarks>
    /// A WIP limit is a limit on work in that state, not on work in that state on one row of the
    /// board, so this is the number a limit is judged against even when swimlanes are on.
    /// </remarks>
    public int CountIn(string? columnId)
        => this.countsByColumn.GetValueOrDefault(columnId ?? string.Empty);

    /// <summary>The column is over its WIP limit right now.</summary>
    public bool IsOverLimit(string? columnId)
    {
        var column = this.Column(columnId);
        return column?.WipLimit is > 0 && this.CountIn(columnId) > column.WipLimit;
    }

    /// <summary>The column is exactly at its WIP limit - one more card would exceed it.</summary>
    public bool IsAtLimit(string? columnId)
    {
        var column = this.Column(columnId);
        return column?.WipLimit is > 0 && this.CountIn(columnId) == column.WipLimit;
    }


    // =============================================================================================
    // Drops
    // =============================================================================================

    /// <summary>
    /// Whether this card may be dropped into this column and swimlane, and if not, why not.
    /// </summary>
    /// <remarks>
    /// A move that stays inside its own column is always allowed as far as WIP goes - reordering
    /// work already in progress does not add any. Getting that backwards makes a full column
    /// impossible to sort, which reads as the board being frozen.
    /// </remarks>
    public KanbanDropVerdict Evaluate(KanbanCard? card, string? toColumnId, string? toSwimlaneId)
    {
        if (card is null)
            return new KanbanDropVerdict(false, KanbanDropRejection.UnknownCard);

        if (card.IsLocked)
            return new KanbanDropVerdict(false, KanbanDropRejection.CardLocked);

        var from = this.Column(card.ColumnId);
        if (from is not null && !from.AllowDrag)
            return new KanbanDropVerdict(false, KanbanDropRejection.ColumnRejectsDrag);

        var to = this.Column(toColumnId);
        if (to is null)
            return new KanbanDropVerdict(false, KanbanDropRejection.UnknownColumn);

        if (!to.AllowDrop)
            return new KanbanDropVerdict(false, KanbanDropRejection.ColumnRejectsDrop);

        if (this.Options.SwimlaneMode == KanbanSwimlaneMode.Grouped &&
            this.Swimlanes.Count > 0 &&
            this.Lane(to.Id, toSwimlaneId ?? string.Empty) is null)
        {
            return new KanbanDropVerdict(false, KanbanDropRejection.UnknownSwimlane);
        }

        var sameColumn = string.Equals(card.ColumnId ?? string.Empty, to.Id ?? string.Empty, StringComparison.Ordinal);

        if (!sameColumn &&
            this.Options.WipBehavior == KanbanWipBehavior.Block &&
            to.WipLimit is > 0 &&
            this.CountIn(to.Id) >= to.WipLimit)
        {
            return new KanbanDropVerdict(false, KanbanDropRejection.WipLimitReached);
        }

        return KanbanDropVerdict.Ok;
    }


    /// <summary>
    /// Works out exactly what a drop would do - where the card comes from, where it lands, and at
    /// which index - without changing anything. Null when <see cref="Evaluate"/> refuses the drop.
    /// </summary>
    /// <param name="toIndex">
    /// The insertion index within the destination lane, in that lane's <em>current</em> terms - that
    /// is, counting the dragged card if it is already in that lane. Out-of-range values are clamped,
    /// and a negative one appends.
    /// </param>
    public KanbanMove? PlanMove(KanbanCard? card, string? toColumnId, string? toSwimlaneId, int toIndex)
    {
        var verdict = this.Evaluate(card, toColumnId, toSwimlaneId);
        if (!verdict.Allowed || card is null)
            return null;

        var to = this.Column(toColumnId)!;
        var toLaneId = this.Options.SwimlaneMode == KanbanSwimlaneMode.Grouped && this.Swimlanes.Count > 0
            ? toSwimlaneId ?? string.Empty
            : string.Empty;

        var fromColumnId = card.ColumnId ?? string.Empty;
        var fromLaneId = this.ResolveSwimlaneId(card);
        var fromLane = this.Lane(fromColumnId, fromLaneId);
        var fromIndex = IndexOf(fromLane?.Cards, card);

        var toLane = this.Lane(to.Id, toLaneId);
        var destination = toLane?.Cards ?? [];

        var sameLane =
            string.Equals(fromColumnId, to.Id ?? string.Empty, StringComparison.Ordinal) &&
            string.Equals(fromLaneId, toLaneId, StringComparison.Ordinal);

        // The index arrives in the destination's current terms, so within one lane the card being
        // dragged is still sitting in that list and still occupying a slot. Pulling it out first is
        // what makes "drop below the card underneath me" land below rather than back where it was.
        var upperBound = sameLane ? destination.Count - 1 : destination.Count;
        var resolved = toIndex < 0 ? upperBound : toIndex;

        if (sameLane && fromIndex >= 0 && resolved > fromIndex)
            resolved--;

        resolved = Math.Clamp(resolved, 0, Math.Max(0, upperBound));

        return new KanbanMove(
            card,
            fromColumnId,
            fromLaneId,
            fromIndex,
            to.Id ?? string.Empty,
            toLaneId,
            resolved,
            IsNoOp: sameLane && resolved == fromIndex
        );
    }


    /// <summary>
    /// Commits a planned move onto the model: the card's column and swimlane are reassigned and
    /// both the source and destination lanes are renumbered from zero.
    /// </summary>
    /// <remarks>
    /// Renumbering the whole lane rather than wedging a fractional order between two neighbours is
    /// the deliberate choice. Fractions run out of precision after a few hundred drags into the same
    /// gap and start colliding, and the collision presents as cards that swap places on their own.
    /// A lane is short enough that rewriting it costs nothing.
    /// </remarks>
    public void Apply(KanbanMove? move)
    {
        if (move is null)
            return;

        var card = move.Card;

        var source = this.Lane(move.FromColumnId, move.FromSwimlaneId)?.Cards.ToList() ?? [];
        source.Remove(card);

        var sameLane =
            string.Equals(move.FromColumnId, move.ToColumnId, StringComparison.Ordinal) &&
            string.Equals(move.FromSwimlaneId, move.ToSwimlaneId, StringComparison.Ordinal);

        var destination = sameLane
            ? source
            : this.Lane(move.ToColumnId, move.ToSwimlaneId)?.Cards.ToList() ?? [];

        destination.Insert(Math.Clamp(move.ToIndex, 0, destination.Count), card);

        card.ColumnId = move.ToColumnId;
        if (this.Options.SwimlaneMode == KanbanSwimlaneMode.Grouped)
            card.SwimlaneId = move.ToSwimlaneId.Length == 0 ? null : move.ToSwimlaneId;

        Renumber(destination);
        if (!sameLane)
            Renumber(source);
    }


    static void Renumber(List<KanbanCard> lane)
    {
        for (var i = 0; i < lane.Count; i++)
            lane[i].Order = i;
    }


    /// <summary>Reference-identity position of a card in a lane. -1 when it is not in one.</summary>
    static int IndexOf(IReadOnlyList<KanbanCard>? cards, KanbanCard card)
    {
        if (cards is null)
            return -1;

        for (var i = 0; i < cards.Count; i++)
        {
            if (ReferenceEquals(cards[i], card))
                return i;
        }
        return -1;
    }


    /// <summary>
    /// The lane a card belongs to. With swimlanes off every card shares one lane, and a card whose
    /// swimlane id matches nothing falls into the first lane rather than vanishing.
    /// </summary>
    string ResolveSwimlaneId(KanbanCard card)
    {
        if (this.Options.SwimlaneMode != KanbanSwimlaneMode.Grouped || this.Swimlanes.Count == 0)
            return string.Empty;

        var id = card.SwimlaneId;
        if (id is not null && this.Swimlanes.Any(s => string.Equals(s.Id, id, StringComparison.Ordinal)))
            return id;

        return this.Swimlanes[0].Id ?? string.Empty;
    }
}


/// <summary>How a board is assembled.</summary>
public sealed class KanbanBoardOptions
{
    /// <summary>Whether cards are grouped into swimlane rows. Defaults to <see cref="KanbanSwimlaneMode.None"/>.</summary>
    public KanbanSwimlaneMode SwimlaneMode { get; init; } = KanbanSwimlaneMode.None;

    /// <summary>What an over-limit column does to an incoming card. Defaults to <see cref="KanbanWipBehavior.Warn"/>.</summary>
    public KanbanWipBehavior WipBehavior { get; init; } = KanbanWipBehavior.Warn;
}


/// <summary>One column-by-swimlane bucket of cards.</summary>
public sealed class KanbanLane
{
    internal KanbanLane(KanbanColumn column, KanbanSwimlane? swimlane, IReadOnlyList<KanbanCard> cards, int columnCount)
    {
        this.Column = column;
        this.Swimlane = swimlane;
        this.Cards = cards;
        this.ColumnCount = columnCount;
    }

    /// <summary>The column this lane sits in.</summary>
    public KanbanColumn Column { get; }

    /// <summary>The swimlane row, or null when swimlanes are off.</summary>
    public KanbanSwimlane? Swimlane { get; }

    /// <summary>The cards, sorted by <see cref="KanbanCard.Order"/>.</summary>
    public IReadOnlyList<KanbanCard> Cards { get; }

    /// <summary>How many cards this lane holds.</summary>
    public int Count => this.Cards.Count;

    /// <summary>How many cards the whole column holds, across every swimlane - what a WIP limit counts.</summary>
    public int ColumnCount { get; }

    /// <summary>The column's WIP limit, if it set one.</summary>
    public int? WipLimit => this.Column.WipLimit is > 0 ? this.Column.WipLimit : null;

    /// <summary>The column holds more cards than its limit allows.</summary>
    public bool IsOverLimit => this.WipLimit is { } limit && this.ColumnCount > limit;

    /// <summary>The column is exactly full.</summary>
    public bool IsAtLimit => this.WipLimit is { } limit && this.ColumnCount == limit;
}


/// <summary>A planned card move. Produced by <see cref="KanbanBoard.PlanMove"/>, applied by <see cref="KanbanBoard.Apply"/>.</summary>
/// <param name="Card">The card being moved.</param>
/// <param name="FromColumnId">The column it started in.</param>
/// <param name="FromSwimlaneId">The swimlane it started in - empty when swimlanes are off.</param>
/// <param name="FromIndex">Its index in the source lane, or -1 when it was not in one.</param>
/// <param name="ToColumnId">The column it lands in.</param>
/// <param name="ToSwimlaneId">The swimlane it lands in - empty when swimlanes are off.</param>
/// <param name="ToIndex">Its index in the destination lane once the move has been applied.</param>
/// <param name="IsNoOp">The card would end up exactly where it already is.</param>
public sealed record KanbanMove(
    KanbanCard Card,
    string FromColumnId,
    string FromSwimlaneId,
    int FromIndex,
    string ToColumnId,
    string ToSwimlaneId,
    int ToIndex,
    bool IsNoOp
)
{
    /// <summary>The card changed columns, rather than being reordered within one.</summary>
    public bool ChangedColumn => !string.Equals(this.FromColumnId, this.ToColumnId, StringComparison.Ordinal);

    /// <summary>The card changed swimlanes.</summary>
    public bool ChangedSwimlane => !string.Equals(this.FromSwimlaneId, this.ToSwimlaneId, StringComparison.Ordinal);
}


/// <summary>Whether a drop is allowed, and what refused it.</summary>
/// <param name="Allowed">The drop may proceed.</param>
/// <param name="Reason">Why it may not. <see cref="KanbanDropRejection.None"/> when it may.</param>
public readonly record struct KanbanDropVerdict(bool Allowed, KanbanDropRejection Reason)
{
    /// <summary>An allowed drop.</summary>
    public static KanbanDropVerdict Ok => new(true, KanbanDropRejection.None);
}

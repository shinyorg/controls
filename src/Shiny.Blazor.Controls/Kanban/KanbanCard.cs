using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Shiny.Blazor.Controls.Kanban;

/// <summary>
/// One card on the board: what it says, which column and swimlane it sits in, and where in that
/// lane it sits.
/// </summary>
/// <remarks>
/// <para>
/// Cards <b>must</b> be a <see cref="KanbanCard"/> - there is no generic <c>TItem</c>. Map your own
/// type onto one and hang the original off <see cref="Item"/>; every event and every template hands
/// the card back, so the original is always one hop away. The same arrangement <c>GanttView</c>
/// uses, and for the same reason: a board that has to reflect over an arbitrary item type cannot
/// write a new column id back onto it when the user drops it somewhere.
/// </para>
/// <para>
/// <see cref="ColumnId"/>, <see cref="SwimlaneId"/> and <see cref="Order"/> are the model, not
/// decoration: a drop rewrites them, which is what makes a board bound to an
/// <see cref="ObservableCollection{T}"/> of cards survive a round trip through a database.
/// </para>
/// <para>
/// <see cref="Color"/> is a string rather than a typed colour so this file is identical on both
/// hosts - it is mirrored, near line for line, by <c>Shiny.Maui.Controls/Kanban/KanbanCard.cs</c>.
/// Anything the host can parse works - <c>#2563eb</c>, <c>rebeccapurple</c>.
/// </para>
/// </remarks>
public class KanbanCard : INotifyPropertyChanged
{
    string id = Guid.NewGuid().ToString("N");
    string columnId = string.Empty;
    string? swimlaneId;
    string title = string.Empty;
    string? description;
    string? color;
    string? assigneeName;
    string? assigneeImage;
    DateTimeOffset? dueDate;
    string? badge;
    int order;
    bool isLocked;
    object? item;

    public KanbanCard()
    {
        this.Labels = [];
        this.Labels.CollectionChanged += this.OnLabelsChanged;
    }


    /// <summary>Stable identity. Defaults to a new GUID.</summary>
    public string Id
    {
        get => this.id;
        set => this.Set(ref this.id, value);
    }

    /// <summary>
    /// The <see cref="KanbanColumn.Id"/> this card lives in. A card naming a column the board does
    /// not have is not rendered - it turns up in <see cref="KanbanBoard.Orphans"/> instead.
    /// </summary>
    public string ColumnId
    {
        get => this.columnId;
        set => this.Set(ref this.columnId, value);
    }

    /// <summary>
    /// The <see cref="KanbanSwimlane.Id"/> this card lives in, or null for the first lane. Ignored
    /// entirely when the board is not grouped into swimlanes.
    /// </summary>
    public string? SwimlaneId
    {
        get => this.swimlaneId;
        set => this.Set(ref this.swimlaneId, value);
    }

    /// <summary>The headline, shown in bold on the default card.</summary>
    public string Title
    {
        get => this.title;
        set => this.Set(ref this.title, value);
    }

    /// <summary>Secondary text under the title. Truncated to <c>KanbanView.DescriptionLineLimit</c> lines.</summary>
    public string? Description
    {
        get => this.description;
        set => this.Set(ref this.description, value);
    }

    /// <summary>An accent stripe down the leading edge of the card. Null for no stripe.</summary>
    public string? Color
    {
        get => this.color;
        set => this.Set(ref this.color, value);
    }

    /// <summary>Who owns the card. Shown in the footer, and used for the avatar's initials when there is no image.</summary>
    public string? AssigneeName
    {
        get => this.assigneeName;
        set => this.Set(ref this.assigneeName, value);
    }

    /// <summary>An avatar for the footer - a URL, or anything else the host resolves to an image.</summary>
    public string? AssigneeImage
    {
        get => this.assigneeImage;
        set => this.Set(ref this.assigneeImage, value);
    }

    /// <summary>
    /// When the card is due. The default card styles it as overdue once it is in the past, and as
    /// due soon inside <c>KanbanView.DueSoonWindow</c>.
    /// </summary>
    public DateTimeOffset? DueDate
    {
        get => this.dueDate;
        set => this.Set(ref this.dueDate, value);
    }

    /// <summary>A short string in the card's top-right corner - a ticket number, an estimate, a count.</summary>
    public string? Badge
    {
        get => this.badge;
        set => this.Set(ref this.badge, value);
    }

    /// <summary>
    /// Position within its lane, ascending. Rewritten by every drop; ties break on the order the
    /// cards arrived in.
    /// </summary>
    public int Order
    {
        get => this.order;
        set => this.Set(ref this.order, value);
    }

    /// <summary>The card cannot be dragged. It still renders, and still counts against a WIP limit.</summary>
    public bool IsLocked
    {
        get => this.isLocked;
        set => this.Set(ref this.isLocked, value);
    }

    /// <summary>Your own object. The board never reads it - it is handed back on every event and template.</summary>
    public object? Item
    {
        get => this.item;
        set => this.Set(ref this.item, value);
    }

    /// <summary>Coloured chips along the top of the card.</summary>
    public ObservableCollection<KanbanLabel> Labels { get; }


    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    void OnLabelsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.Labels)));
}

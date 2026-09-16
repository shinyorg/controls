using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Shiny.Maui.Controls.Kanban;

/// <summary>
/// One vertical column of the board - a state work passes through, and the WIP limit on how much
/// may be in that state at once.
/// </summary>
/// <remarks>
/// This file is mirrored, near line for line, by <c>Shiny.Blazor.Controls/Kanban/KanbanColumn.cs</c>.
/// </remarks>
public class KanbanColumn : INotifyPropertyChanged
{
    string id = Guid.NewGuid().ToString("N");
    string title = string.Empty;
    string? description;
    string? color;
    int? wipLimit;
    bool isCollapsed;
    bool allowDrop = true;
    bool allowDrag = true;
    bool allowAdd = true;
    double? width;
    object? item;

    /// <summary>Stable identity. <see cref="KanbanCard.ColumnId"/> names it. Defaults to a new GUID.</summary>
    public string Id
    {
        get => this.id;
        set => this.Set(ref this.id, value);
    }

    /// <summary>The header text.</summary>
    public string Title
    {
        get => this.title;
        set => this.Set(ref this.title, value);
    }

    /// <summary>Optional sub-heading under the title - the column's definition of done, usually.</summary>
    public string? Description
    {
        get => this.description;
        set => this.Set(ref this.description, value);
    }

    /// <summary>A rule across the top of the column. Null for the theme's outline colour.</summary>
    public string? Color
    {
        get => this.color;
        set => this.Set(ref this.color, value);
    }

    /// <summary>
    /// How many cards may be in this column at once, across every swimlane. Null or zero means no
    /// limit. What happens when it is reached is <c>KanbanView.WipBehavior</c>'s business.
    /// </summary>
    public int? WipLimit
    {
        get => this.wipLimit;
        set => this.Set(ref this.wipLimit, value);
    }

    /// <summary>
    /// The column is rolled up to a narrow spine showing its title and count. Its cards are not
    /// rendered, but they are still counted and it is still a drop target.
    /// </summary>
    public bool IsCollapsed
    {
        get => this.isCollapsed;
        set => this.Set(ref this.isCollapsed, value);
    }

    /// <summary>Cards may be dropped into this column. Defaults to true.</summary>
    public bool AllowDrop
    {
        get => this.allowDrop;
        set => this.Set(ref this.allowDrop, value);
    }

    /// <summary>Cards may be dragged out of this column. Defaults to true.</summary>
    public bool AllowDrag
    {
        get => this.allowDrag;
        set => this.Set(ref this.allowDrag, value);
    }

    /// <summary>The inline add-card affordance appears at the foot of this column. Defaults to true.</summary>
    public bool AllowAdd
    {
        get => this.allowAdd;
        set => this.Set(ref this.allowAdd, value);
    }

    /// <summary>Overrides <c>KanbanView.ColumnWidth</c> for this column alone.</summary>
    public double? Width
    {
        get => this.width;
        set => this.Set(ref this.width, value);
    }

    /// <summary>Your own object. Handed back on every event and template; never read by the board.</summary>
    public object? Item
    {
        get => this.item;
        set => this.Set(ref this.item, value);
    }


    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

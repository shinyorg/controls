using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Shiny.Blazor.Controls.Kanban;

/// <summary>
/// One horizontal band of the board - a team, an epic, a priority. Every swimlane carries the full
/// set of columns.
/// </summary>
/// <remarks>
/// Swimlanes only exist when <c>KanbanView.SwimlaneMode</c> is
/// <see cref="KanbanSwimlaneMode.Grouped"/>. Otherwise the board is one flat row and
/// <see cref="KanbanCard.SwimlaneId"/> is ignored.
/// <para>
/// This file is mirrored, near line for line, by
/// <c>Shiny.Maui.Controls/Kanban/KanbanSwimlane.cs</c>.
/// </para>
/// </remarks>
public class KanbanSwimlane : INotifyPropertyChanged
{
    string id = Guid.NewGuid().ToString("N");
    string title = string.Empty;
    string? color;
    bool isCollapsed;
    object? item;

    /// <summary>Stable identity. <see cref="KanbanCard.SwimlaneId"/> names it. Defaults to a new GUID.</summary>
    public string Id
    {
        get => this.id;
        set => this.Set(ref this.id, value);
    }

    /// <summary>The band's heading.</summary>
    public string Title
    {
        get => this.title;
        set => this.Set(ref this.title, value);
    }

    /// <summary>A marker down the leading edge of the band. Null for none.</summary>
    public string? Color
    {
        get => this.color;
        set => this.Set(ref this.color, value);
    }

    /// <summary>The band is rolled up to its heading. Its cards still count against WIP limits.</summary>
    public bool IsCollapsed
    {
        get => this.isCollapsed;
        set => this.Set(ref this.isCollapsed, value);
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

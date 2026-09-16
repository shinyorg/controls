using System.ComponentModel;

namespace Shiny.Maui.Controls.DataGrid;

/// <summary>
/// Internal per-row wrapper so the grid can track selection/edit/expansion state without mutating the
/// user's data objects. Row cell content binds to <see cref="Data"/>.
/// </summary>
sealed class DataGridRow : INotifyPropertyChanged
{
    bool isSelected;
    bool isEditing;
    bool isExpanded;
    bool isLoadingChildren;
    bool isLoadingDetail;

    public DataGridRow(object data) => this.Data = data;

    public object Data { get; }

    /// <summary>Row position used for striping.</summary>
    public int Index { get; init; }

    /// <summary>Depth in the hierarchy (0 for a flat grid or a root row).</summary>
    public int Level { get; init; }

    /// <summary>
    /// First row of the block this one belongs to - the page, or one group when the grid is grouped.
    /// A column highlight closes its stroke here rather than leaving it running off the top of the
    /// block. Stamped after the rows are built, since "last" is not known until then.
    /// </summary>
    public bool IsFirstRow { get; set; }

    /// <summary>Last row of the block - the other end of the same stroke.</summary>
    public bool IsLastRow { get; set; }

    /// <summary>True when the row has (or may lazily load) child rows.</summary>
    public bool HasChildren { get; init; }

    /// <summary>True when the row can show a detail row - <see cref="DataGrid.IsRowExpandable"/> said so.</summary>
    public bool HasDetail { get; init; }

    public bool IsSelected
    {
        get => this.isSelected;
        set
        {
            if (this.isSelected == value)
                return;
            this.isSelected = value;
            this.PropertyChanged?.Invoke(this, IsSelectedArgs);
        }
    }

    public bool IsEditing
    {
        get => this.isEditing;
        set
        {
            if (this.isEditing == value)
                return;
            this.isEditing = value;
            this.PropertyChanged?.Invoke(this, IsEditingArgs);
        }
    }

    public bool IsExpanded
    {
        get => this.isExpanded;
        set
        {
            if (this.isExpanded == value)
                return;
            this.isExpanded = value;
            this.PropertyChanged?.Invoke(this, IsExpandedArgs);
            this.RaiseGlyphs();
        }
    }

    public bool IsLoadingChildren
    {
        get => this.isLoadingChildren;
        set
        {
            if (this.isLoadingChildren == value)
                return;
            this.isLoadingChildren = value;
            this.PropertyChanged?.Invoke(this, IsLoadingChildrenArgs);
            this.RaiseGlyphs();
        }
    }

    public bool IsLoadingDetail
    {
        get => this.isLoadingDetail;
        set
        {
            if (this.isLoadingDetail == value)
                return;
            this.isLoadingDetail = value;
            this.PropertyChanged?.Invoke(this, IsLoadingDetailArgs);
            this.RaiseGlyphs();
        }
    }

    /// <summary>Caret shown inline in the tree column. Empty for a leaf, so the cell reads as plain text.</summary>
    public string TreeCaretGlyph
        => !this.HasChildren ? string.Empty : this.isExpanded ? "▾" : "▸";

    /// <summary>Caret shown in the detail expander column.</summary>
    public string DetailCaretGlyph => this.isExpanded ? "▾" : "▸";

    // The caret and the busy spinner swap places rather than sitting side by side, so each side of
    // the swap gets its own flag to bind IsVisible to.
    public bool ShowTreeCaret => this.HasChildren && !this.isLoadingChildren;

    public bool ShowDetailCaret => this.HasDetail && !this.isLoadingDetail;

    /// <summary>True while this row is waiting on a children or detail load.</summary>
    public bool IsBusy => this.isLoadingChildren || this.isLoadingDetail;

    /// <summary>
    /// Bumped when the grid's theme colours change. Unused by the background converters; it is a third
    /// input to their bindings so a theme flip re-runs only those converters - cheaper than rebuilding
    /// rows, and without disturbing selection or scroll. It must actually change: a binding that reads
    /// back the same value does not re-run its MultiBinding.
    /// </summary>
    public int BackgroundRevision { get; private set; }

    internal void RaiseBackgroundChanged()
    {
        this.BackgroundRevision++;
        this.PropertyChanged?.Invoke(this, BackgroundRevisionArgs);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void RaiseGlyphs()
    {
        this.PropertyChanged?.Invoke(this, TreeCaretGlyphArgs);
        this.PropertyChanged?.Invoke(this, DetailCaretGlyphArgs);
        this.PropertyChanged?.Invoke(this, ShowTreeCaretArgs);
        this.PropertyChanged?.Invoke(this, ShowDetailCaretArgs);
        this.PropertyChanged?.Invoke(this, IsBusyArgs);
    }

    static readonly PropertyChangedEventArgs IsSelectedArgs = new(nameof(IsSelected));
    static readonly PropertyChangedEventArgs IsEditingArgs = new(nameof(IsEditing));
    static readonly PropertyChangedEventArgs IsExpandedArgs = new(nameof(IsExpanded));
    static readonly PropertyChangedEventArgs IsLoadingChildrenArgs = new(nameof(IsLoadingChildren));
    static readonly PropertyChangedEventArgs TreeCaretGlyphArgs = new(nameof(TreeCaretGlyph));
    static readonly PropertyChangedEventArgs DetailCaretGlyphArgs = new(nameof(DetailCaretGlyph));
    static readonly PropertyChangedEventArgs IsLoadingDetailArgs = new(nameof(IsLoadingDetail));
    static readonly PropertyChangedEventArgs ShowTreeCaretArgs = new(nameof(ShowTreeCaret));
    static readonly PropertyChangedEventArgs ShowDetailCaretArgs = new(nameof(ShowDetailCaret));
    static readonly PropertyChangedEventArgs IsBusyArgs = new(nameof(IsBusy));
    static readonly PropertyChangedEventArgs BackgroundRevisionArgs = new(nameof(BackgroundRevision));
}

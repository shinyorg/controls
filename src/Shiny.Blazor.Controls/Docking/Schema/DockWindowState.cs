namespace Shiny.Blazor.Controls.Docking;

public sealed class DockWindowState
{
    public DockRect? Bounds { get; set; }
    public bool IsMaximized { get; set; }
    public bool IsFullScreen { get; set; }

    public DockNode DocumentArea { get; set; } = new DockEmpty();
    public DockNode? LeftRail { get; set; }
    public DockNode? TopRail { get; set; }
    public DockNode? RightRail { get; set; }
    public DockNode? BottomRail { get; set; }

    public string? ActivePanelId { get; set; }

    /// <summary>
    /// Floating windows only: the group the window was torn from, so docking it back returns it
    /// home instead of to a default rail. Ignored when that group no longer exists.
    /// </summary>
    public string? RestoreGroupId { get; set; }

    /// <summary>Legacy whole-rail collapse state; converted to <see cref="CollapsedTabs"/> on load.</summary>
    public List<DockArea> CollapsedRails { get; set; } = new();

    /// <summary>Panels individually collapsed to an edge bar, keyed by dock side.</summary>
    public List<DockCollapsedPanel> CollapsedTabs { get; set; } = new();

    /// <summary>Rail sizes in pixels (width for left/right, height for top/bottom); null = default.</summary>
    public double? LeftRailSize { get; set; }
    public double? TopRailSize { get; set; }
    public double? RightRailSize { get; set; }
    public double? BottomRailSize { get; set; }
}

/// <summary>A panel collapsed to an edge bar, remembering which side it docks back to.</summary>
public sealed class DockCollapsedPanel
{
    public DockArea Area { get; set; }
    public DockTab Tab { get; set; } = new();
}

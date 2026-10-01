namespace Shiny.Maui.Controls.Desktop.Docking;

/// <summary>
/// Per-window dock state controller. Exposed on <see cref="DockHostView"/>.
/// </summary>
public interface IDockHost
{
    /// <summary>When true, drag/close/resize are disabled and the layout is read-only.</summary>
    bool IsLocked { get; set; }

    IDockEvents Events { get; }
    IDockCommandScope CommandScope { get; }

    Task LoadAsync(DockRoot root, CancellationToken ct = default);
    DockRoot Snapshot();

    Task ShowPanelAsync(string panelTypeId, DockArea preferredArea = DockArea.Left, CancellationToken ct = default);
    Task HidePanelAsync(string panelInstanceId, CancellationToken ct = default);
    Task ActivatePanelAsync(string panelInstanceId, CancellationToken ct = default);

    /// <summary>Reset to the default layout supplied at startup.</summary>
    Task ResetLayoutAsync(CancellationToken ct = default);

    /// <summary>Tears a docked panel off into its own floating window (a no-op if it already floats alone).</summary>
    Task FloatPanelAsync(string panelInstanceId, CancellationToken ct = default);

    /// <summary>Collapse a rail to a slim edge bar of panel titles, or expand it back.</summary>
    Task SetRailCollapsedAsync(DockArea area, bool collapsed, CancellationToken ct = default);
}

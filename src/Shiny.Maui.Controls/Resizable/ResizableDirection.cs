namespace Shiny.Maui.Controls;

/// <summary>Which way a <see cref="ResizablePanelGroup"/> lays out its panels.</summary>
public enum ResizableDirection
{
    /// <summary>Side by side, with vertical handles dragged left and right.</summary>
    Horizontal,

    /// <summary>Stacked, with horizontal handles dragged up and down.</summary>
    Vertical
}

/// <summary>The panel sizes after a <see cref="ResizablePanelGroup"/> settled on a new layout.</summary>
public sealed class ResizableLayoutChangedEventArgs(IReadOnlyList<double> sizes) : EventArgs
{
    /// <summary>Each panel's size in percent of the group, in panel order. They add up to 100.</summary>
    public IReadOnlyList<double> Sizes { get; } = sizes;
}

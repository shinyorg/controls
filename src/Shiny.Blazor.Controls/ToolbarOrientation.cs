namespace Shiny.Blazor.Controls;

/// <summary>Which way a <see cref="FloatingToolbar"/> lays its items out.</summary>
public enum ToolbarOrientation
{
    /// <summary>Items run left to right. Overflow is decided on width.</summary>
    Horizontal,

    /// <summary>Items run top to bottom. Overflow is decided on height.</summary>
    Vertical
}

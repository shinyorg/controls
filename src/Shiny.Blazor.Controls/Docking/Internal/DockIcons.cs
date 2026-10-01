using Microsoft.AspNetCore.Components;

namespace Shiny.Blazor.Controls.Docking;

/// <summary>
/// Chrome glyphs for the dock host. Drawn as SVG on a 16px grid in <c>currentColor</c>: text glyphs
/// (×, ▾, ⇤) render at whatever size and weight the font gives them, which at chrome sizes is a
/// speck that changes between platforms.
/// </summary>
static class DockIcons
{
    const string Open = "<svg viewBox=\"0 0 16 16\" width=\"16\" height=\"16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">";

    public static readonly MarkupString Close = new(Open + "<path d=\"M4.5 4.5l7 7M11.5 4.5l-7 7\"/></svg>");

    /// <summary>Return a floating window to the layout: a window dropping into a frame.</summary>
    public static readonly MarkupString DockBack = new(Open + "<rect x=\"2.5\" y=\"2.5\" width=\"11\" height=\"11\" rx=\"1.5\"/><path d=\"M2.5 6h11\"/><path d=\"M8 8.2v3.3M6.4 9.9 8 11.5l1.6-1.6\"/></svg>");

    /// <summary>Tear a group's active panel off into a floating window.</summary>
    public static readonly MarkupString Float = new(Open + "<rect x=\"2.5\" y=\"5.5\" width=\"8\" height=\"8\" rx=\"1.5\"/><path d=\"M7 2.5h5a1.5 1.5 0 0 1 1.5 1.5v5\"/></svg>");

    public static MarkupString Chevron(DockArea direction) => direction switch
    {
        DockArea.Left => new(Open + "<path d=\"M10 4 6 8l4 4\"/></svg>"),
        DockArea.Right => new(Open + "<path d=\"M6 4l4 4-4 4\"/></svg>"),
        DockArea.Top => new(Open + "<path d=\"M4 10l4-4 4 4\"/></svg>"),
        _ => new(Open + "<path d=\"M4 6l4 4 4-4\"/></svg>")
    };
}

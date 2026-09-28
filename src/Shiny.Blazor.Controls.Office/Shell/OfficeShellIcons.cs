using Microsoft.AspNetCore.Components;
using Shiny.Controls.Office.Icons;

namespace Shiny.Blazor.Controls.Office;

/// <summary>
/// The shell's icons as inline SVG — the same geometry the MAUI shell paints, rendered through the
/// Office toolbar's SVG writer so the two icon families share one stroke weight.
/// </summary>
/// <remarks>
/// Public because a host wiring the shell into its own ribbon wants the same marks on its buttons:
/// <c>Icon="@OfficeShellIcons.Svg(OfficeShellIcon.Save)"</c> on a <c>RibbonButton</c>.
/// </remarks>
public static class OfficeShellIcons
{
    static readonly Dictionary<(OfficeShellIcon, int), MarkupString> Cache = [];
    static readonly Lock Gate = new();

    /// <summary>The icon as markup, cached per size.</summary>
    public static MarkupString Get(OfficeShellIcon icon, int size = 16)
    {
        lock (Gate)
        {
            if (!Cache.TryGetValue((icon, size), out var markup))
            {
                markup = OfficeToolbarIcons.Get(OfficeIcons.Shapes(icon), size);
                Cache[(icon, size)] = markup;
            }

            return markup;
        }
    }

    /// <summary>The icon as an SVG string — what a ribbon item's <c>Icon</c> parameter takes.</summary>
    public static string Svg(OfficeShellIcon icon, int size = 16) => Get(icon, size).Value;
}

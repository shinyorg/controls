using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Shiny.Blazor.Controls.Cells;

/// <summary>
/// A full-width button-style cell. Ignores icon/description — renders a centered action label.
/// </summary>
public class ButtonCell : ComponentBase
{
    [CascadingParameter] public TableView? ParentTableView { get; set; }

    [Parameter] public string? Title { get; set; }
    [Parameter] public string? ButtonTextColor { get; set; }
    [Parameter] public string TitleAlignment { get; set; } = "center";
    /// <summary>Button label size in px. The default, <c>-1</c>, follows the theme type scale.</summary>
    [Parameter] public double TitleFontSize { get; set; } = -1;
    [Parameter] public bool IsEnabled { get; set; } = true;

    [Parameter] public EventCallback OnClick { get; set; }

    async Task HandleClick(MouseEventArgs e)
    {
        if (!IsEnabled) return;
        if (OnClick.HasDelegate)
            await OnClick.InvokeAsync();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var color = ButtonTextColor ?? ParentTableView?.CellAccentColor ?? "var(--shiny-color-primary, #0055D9)";
        var size = TitleFontSize >= 0
            ? FormattableString.Invariant($"{TitleFontSize}px")
            : "var(--shiny-type-body-large-size, 16px)";
        var style = $"color:{color};text-align:{TitleAlignment};font-size:{size};";
        if (!IsEnabled) style += "opacity:0.4;cursor:not-allowed;";

        builder.OpenElement(0, "button");
        builder.AddAttribute(1, "type", "button");
        builder.AddAttribute(2, "class", "shiny-tv-button");
        builder.AddAttribute(3, "style", style);
        builder.AddAttribute(4, "disabled", !IsEnabled);
        builder.AddAttribute(5, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, HandleClick));
        builder.AddContent(6, Title ?? "");
        builder.CloseElement();
    }
}

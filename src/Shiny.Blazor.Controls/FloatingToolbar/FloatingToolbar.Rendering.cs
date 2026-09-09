using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Shiny.Blazor.Controls;

public partial class FloatingToolbar
{
    const string ChevronDown = "<svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'><path d='M6 9l6 6 6-6'/></svg>";
    const string ChevronRight = "<svg viewBox='0 0 24 24' fill='none' stroke='currentColor' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'><path d='M9 6l6 6-6 6'/></svg>";
    const string EllipsisIcon = "<svg viewBox='0 0 24 24' fill='currentColor'><circle cx='5' cy='12' r='2'/><circle cx='12' cy='12' r='2'/><circle cx='19' cy='12' r='2'/></svg>";


    /// <summary>One button on the bar.</summary>
    RenderFragment Cell(ToolbarItem item, bool isOverflow) => builder =>
    {
        if (item.IsSeparator)
        {
            // A separator on the bar is a rule across it, not a menu divider.
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "class", "shiny-fbar-sep");
            // Measured like any other cell: fitCount is compared against the item count, and a
            // separator missing from the measure makes every bar that has one look overflowing.
            builder.AddAttribute(2, "data-toolbar-cell", true);
            builder.AddAttribute(3, "aria-hidden", "true");
            builder.CloseElement();
            return;
        }

        var icon = isOverflow ? EllipsisIcon : item.Icon;
        var label = this.ShowLabels ? item.Text : null;

        builder.OpenElement(3, "button");
        builder.AddAttribute(4, "type", "button");
        builder.AddAttribute(5, "class", "shiny-fbar-cell");
        builder.AddAttribute(6, "data-toolbar-cell", true);
        builder.AddAttribute(7, "disabled", item.IsDisabled);
        builder.AddAttribute(8, "title", item.Tooltip ?? item.Text);
        builder.AddAttribute(9, "aria-label", item.Tooltip ?? item.Text);

        if (item.HasChildren || isOverflow)
        {
            builder.AddAttribute(10, "aria-haspopup", "true");
            builder.AddAttribute(11, "aria-expanded", this.menus.Any(m => ReferenceEquals(m.Owner, item)));
        }

        if (!String.IsNullOrWhiteSpace(item.IconColor))
            builder.AddAttribute(12, "style", "--shiny-fbar-fg:" + item.IconColor + ";");

        builder.AddAttribute(13, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, () => this.OnCellClicked(item, 0)));
        builder.AddEventStopPropagationAttribute(14, "onclick", true);

        if (!String.IsNullOrWhiteSpace(icon))
        {
            builder.OpenElement(15, "span");
            builder.AddAttribute(16, "class", "shiny-fbar-icon");
            builder.AddMarkupContent(17, icon);
            builder.CloseElement();
        }

        if (!String.IsNullOrWhiteSpace(label))
        {
            builder.OpenElement(18, "span");
            builder.AddAttribute(19, "class", "shiny-fbar-label");
            builder.AddContent(20, label);
            builder.CloseElement();
        }

        if (item.HasChildren || isOverflow)
        {
            builder.OpenElement(21, "span");
            builder.AddAttribute(22, "class", "shiny-fbar-chev");
            builder.AddMarkupContent(
                23,
                this.Orientation == ToolbarOrientation.Vertical ? ChevronRight : ChevronDown
            );
            builder.CloseElement();
        }

        if (!String.IsNullOrWhiteSpace(item.Badge))
        {
            builder.OpenElement(24, "span");
            builder.AddAttribute(25, "class", "shiny-fbar-badge");
            builder.AddContent(26, item.Badge);
            builder.CloseElement();
        }

        builder.CloseElement();
    };


    /// <summary>One row inside a dropdown. Always labelled — a column of bare glyphs is a puzzle.</summary>
    RenderFragment MenuRow(ToolbarItem item, int depth) => builder =>
    {
        if (item.IsSeparator)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "shiny-fbar-menu-sep");
            builder.AddAttribute(2, "role", "separator");
            builder.CloseElement();
            return;
        }

        builder.OpenElement(3, "button");
        builder.AddAttribute(4, "type", "button");
        builder.AddAttribute(5, "class", "shiny-fbar-menu-row");
        builder.AddAttribute(6, "role", "menuitem");
        builder.AddAttribute(7, "disabled", item.IsDisabled);

        if (item.HasChildren)
        {
            builder.AddAttribute(8, "aria-haspopup", "true");
            builder.AddAttribute(9, "aria-expanded", this.menus.Any(m => ReferenceEquals(m.Owner, item)));
        }

        builder.AddAttribute(10, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, () => this.OnCellClicked(item, depth + 1)));
        builder.AddEventStopPropagationAttribute(11, "onclick", true);

        builder.OpenElement(12, "span");
        builder.AddAttribute(13, "class", "shiny-fbar-icon");
        if (!String.IsNullOrWhiteSpace(item.Icon))
            builder.AddMarkupContent(14, item.Icon);
        builder.CloseElement();

        builder.OpenElement(15, "span");
        builder.AddAttribute(16, "class", "shiny-fbar-label");
        builder.AddContent(17, item.Text);
        builder.CloseElement();

        if (item.HasChildren)
        {
            builder.OpenElement(18, "span");
            builder.AddAttribute(19, "class", "shiny-fbar-chev");
            builder.AddMarkupContent(20, ChevronRight);
            builder.CloseElement();
        }

        builder.CloseElement();
    };
}

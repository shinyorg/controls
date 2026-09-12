using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Shiny.Blazor.Controls.Sections;

namespace Shiny.Blazor.Controls.Cells;

/// <summary>
/// Base class for all Shiny Blazor table cells. Renders the icon / title / description / hint /
/// accessory layout shared by every cell type. Derived cells override
/// <see cref="BuildAccessory"/> to provide their right-edge content (and optionally
/// override <see cref="OnTapped"/> for custom tap behaviour).
/// </summary>
public abstract class CellBase : ComponentBase
{
    [CascadingParameter] public TableView? ParentTableView { get; set; }
    [CascadingParameter] public TableSection? ParentSection { get; set; }

    [Parameter] public string? IconSource { get; set; }
    [Parameter] public double IconSize { get; set; } = -1;
    [Parameter] public double IconRadius { get; set; } = -1;

    [Parameter] public string? Title { get; set; }
    [Parameter] public string? TitleColor { get; set; }
    [Parameter] public double TitleFontSize { get; set; } = -1;
    [Parameter] public string? TitleFontFamily { get; set; }
    [Parameter] public bool TitleBold { get; set; }

    [Parameter] public string? Description { get; set; }
    [Parameter] public string? DescriptionColor { get; set; }
    [Parameter] public double DescriptionFontSize { get; set; } = -1;
    [Parameter] public string? DescriptionFontFamily { get; set; }

    [Parameter] public string? HintText { get; set; }
    [Parameter] public string? HintTextColor { get; set; }
    [Parameter] public double HintTextFontSize { get; set; } = -1;

    [Parameter] public string? CellBackgroundColor { get; set; }
    [Parameter] public string? SelectedColor { get; set; }
    [Parameter] public bool IsSelectable { get; set; } = true;
    [Parameter] public bool IsEnabled { get; set; } = true;
    [Parameter] public double CellHeight { get; set; } = -1;

    [Parameter] public string? BorderColor { get; set; }
    [Parameter] public double BorderWidth { get; set; } = -1;
    [Parameter] public double BorderRadius { get; set; } = -1;

    [Parameter] public EventCallback Tapped { get; set; }

    bool isHighlighted;

    protected string ResolveTitleColor()
        => TitleColor ?? ParentTableView?.CellTitleColor ?? "";

    protected string ResolveDescriptionColor()
        => DescriptionColor ?? ParentTableView?.CellDescriptionColor ?? "";

    protected string ResolveHintColor()
        => HintTextColor ?? ParentTableView?.CellHintTextColor ?? "";

    protected string ResolveValueColor()
        => ParentTableView?.CellValueTextColor ?? "";

    protected string ResolveAccentColor()
        => ParentTableView?.CellAccentColor ?? "var(--shiny-color-primary, #0055D9)";

    protected double ResolveDouble(double cellValue, double globalValue, double fallback)
    {
        if (cellValue >= 0) return cellValue;
        if (globalValue >= 0) return globalValue;
        return fallback;
    }

    string CellStyle
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            var bg = isHighlighted
                ? (SelectedColor ?? ParentTableView?.CellSelectedColor)
                : (CellBackgroundColor ?? ParentTableView?.CellBackgroundColor);

            if (!string.IsNullOrEmpty(bg)) sb.Append("background-color:").Append(bg).Append(';');
            if (CellHeight > 0) sb.Append("min-height:").Append(CellHeight).Append("px;");
            if (!IsEnabled) sb.Append("opacity:0.4;pointer-events:none;");

            var bw = ResolveDouble(BorderWidth, ParentTableView?.CellBorderWidth ?? -1, 0);
            var bc = BorderColor ?? ParentTableView?.CellBorderColor;
            var br = ResolveDouble(BorderRadius, ParentTableView?.CellBorderRadius ?? -1, 0);
            if (bw > 0 && !string.IsNullOrEmpty(bc))
                sb.Append("border:").Append(bw).Append("px solid ").Append(bc).Append(';');

            if (br > 0)
                sb.Append("border-radius:").Append(br).Append("px;");

            return sb.ToString();
        }
    }

    string TitleStyle
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            var color = ResolveTitleColor();
            if (!string.IsNullOrEmpty(color)) sb.Append("color:").Append(color).Append(';');
            var size = ResolveDouble(TitleFontSize, ParentTableView?.CellTitleFontSize ?? -1, -1);
            sb.Append("font-size:")
              .Append(size >= 0 ? FormattableString.Invariant($"{size}px") : "var(--shiny-type-body-large-size, 16px)")
              .Append(';');
            if (!string.IsNullOrEmpty(TitleFontFamily)) sb.Append("font-family:").Append(TitleFontFamily).Append(';');
            if (TitleBold) sb.Append("font-weight:600;");
            return sb.ToString();
        }
    }

    string DescriptionStyle
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            var color = ResolveDescriptionColor();
            if (!string.IsNullOrEmpty(color)) sb.Append("color:").Append(color).Append(';');
            var size = ResolveDouble(DescriptionFontSize, ParentTableView?.CellDescriptionFontSize ?? -1, -1);
            sb.Append("font-size:")
              .Append(size >= 0 ? FormattableString.Invariant($"{size}px") : "var(--shiny-type-body-small-size, 12px)")
              .Append(';');
            if (!string.IsNullOrEmpty(DescriptionFontFamily)) sb.Append("font-family:").Append(DescriptionFontFamily).Append(';');
            return sb.ToString();
        }
    }

    string HintStyle
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            var color = ResolveHintColor();
            if (!string.IsNullOrEmpty(color)) sb.Append("color:").Append(color).Append(';');
            var size = ResolveDouble(HintTextFontSize, ParentTableView?.CellHintTextFontSize ?? -1, 12);
            sb.Append("font-size:").Append(size).Append("px;");
            return sb.ToString();
        }
    }

    string IconStyle
    {
        get
        {
            var size = ResolveDouble(IconSize, ParentTableView?.CellIconSize ?? -1, 24);
            var radius = ResolveDouble(IconRadius, ParentTableView?.CellIconRadius ?? -1, 0);
            var sb = new System.Text.StringBuilder();
            sb.Append("width:").Append(size).Append("px;");
            sb.Append("height:").Append(size).Append("px;");
            if (radius > 0) sb.Append("border-radius:").Append(radius).Append("px;overflow:hidden;");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Override this to add accessory content (right-most cell area: switch, picker value, button, etc).
    /// </summary>
    protected virtual void BuildAccessory(RenderTreeBuilder builder, int sequence) { }

    protected virtual Task OnTapped() => Task.CompletedTask;
    protected virtual bool ShouldKeepSelectionHighlight() => false;

    async Task HandleClick(MouseEventArgs e)
    {
        if (!IsEnabled || !IsSelectable) return;

        var hl = SelectedColor ?? ParentTableView?.CellSelectedColor;
        if (!string.IsNullOrEmpty(hl))
        {
            isHighlighted = true;
            StateHasChanged();
            if (!ShouldKeepSelectionHighlight())
            {
                await Task.Delay(150);
                isHighlighted = false;
                StateHasChanged();
            }
        }

        if (Tapped.HasDelegate)
            await Tapped.InvokeAsync();

        await OnTapped();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "shiny-tv-cell");
        builder.AddAttribute(2, "style", CellStyle);
        builder.AddAttribute(3, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, HandleClick));

        // Icon
        if (!string.IsNullOrEmpty(IconSource))
        {
            builder.OpenElement(10, "div");
            builder.AddAttribute(11, "class", "shiny-tv-cell-icon");
            builder.AddAttribute(12, "style", IconStyle);
            builder.OpenElement(13, "img");
            builder.AddAttribute(14, "src", IconSource);
            builder.AddAttribute(15, "alt", "");
            builder.CloseElement();
            builder.CloseElement();
        }

        // Body (title + description)
        builder.OpenElement(20, "div");
        builder.AddAttribute(21, "class", "shiny-tv-cell-body");

        // title row (title left + hint right)
        builder.OpenElement(22, "div");
        builder.AddAttribute(23, "class", "shiny-tv-cell-title-row");

        builder.OpenElement(24, "span");
        builder.AddAttribute(25, "class", "shiny-tv-cell-title");
        builder.AddAttribute(26, "style", TitleStyle);
        builder.AddContent(27, Title ?? "");
        builder.CloseElement();

        if (!string.IsNullOrEmpty(HintText))
        {
            builder.OpenElement(28, "span");
            builder.AddAttribute(29, "class", "shiny-tv-cell-hint");
            builder.AddAttribute(30, "style", HintStyle);
            builder.AddContent(31, HintText);
            builder.CloseElement();
        }
        builder.CloseElement(); // title row

        if (!string.IsNullOrEmpty(Description))
        {
            builder.OpenElement(32, "div");
            builder.AddAttribute(33, "class", "shiny-tv-cell-description");
            builder.AddAttribute(34, "style", DescriptionStyle);
            builder.AddContent(35, Description);
            builder.CloseElement();
        }

        builder.CloseElement(); // body

        // Accessory
        builder.OpenElement(40, "div");
        builder.AddAttribute(41, "class", "shiny-tv-cell-accessory");
        builder.AddAttribute(42, "onclick:stopPropagation", true);
        BuildAccessory(builder, 50);
        builder.CloseElement();

        builder.CloseElement(); // root
    }
}

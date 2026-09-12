using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Shiny.Blazor.Controls.Cells;

/// <summary>
/// Cell that edits a TimeSpan as hours+minutes via two number inputs.
/// </summary>
public class DurationPickerCell : CellBase
{
    [Parameter] public TimeSpan Duration { get; set; }
    [Parameter] public EventCallback<TimeSpan> DurationChanged { get; set; }
    [Parameter] public int MinHours { get; set; }
    [Parameter] public int MaxHours { get; set; } = 999;

    /// <summary>The "hrs"/"min" suffixes. Tokenised so the theme's palette and type scale reach them.</summary>
    const string UnitLabelStyle =
        "color:var(--shiny-color-on-surface-variant, #3B475B);font-size:var(--shiny-type-body-medium-size, 14px);";

    protected override Task OnTapped() => Task.CompletedTask;

    async Task SetHours(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value as string, out var h))
        {
            h = Math.Clamp(h, MinHours, MaxHours);
            Duration = new TimeSpan(h, Duration.Minutes, 0);
            if (DurationChanged.HasDelegate)
                await DurationChanged.InvokeAsync(Duration);
        }
    }

    async Task SetMinutes(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value as string, out var m))
        {
            m = Math.Clamp(m, 0, 59);
            Duration = new TimeSpan((int)Duration.TotalHours, m, 0);
            if (DurationChanged.HasDelegate)
                await DurationChanged.InvokeAsync(Duration);
        }
    }

    protected override void BuildAccessory(RenderTreeBuilder builder, int sequence)
    {
        builder.OpenElement(sequence, "input");
        builder.AddAttribute(sequence + 1, "type", "number");
        builder.AddAttribute(sequence + 2, "class", "shiny-tv-input");
        builder.AddAttribute(sequence + 3, "style", "width:64px;");
        builder.AddAttribute(sequence + 4, "min", MinHours);
        builder.AddAttribute(sequence + 5, "max", MaxHours);
        builder.AddAttribute(sequence + 6, "value", ((int)Duration.TotalHours).ToString());
        builder.AddAttribute(sequence + 7, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, SetHours));
        builder.CloseElement();

        builder.OpenElement(sequence + 8, "span");
        builder.AddAttribute(sequence + 9, "style", UnitLabelStyle);
        builder.AddContent(sequence + 10, "h");
        builder.CloseElement();

        builder.OpenElement(sequence + 11, "input");
        builder.AddAttribute(sequence + 12, "type", "number");
        builder.AddAttribute(sequence + 13, "class", "shiny-tv-input");
        builder.AddAttribute(sequence + 14, "style", "width:64px;");
        builder.AddAttribute(sequence + 15, "min", 0);
        builder.AddAttribute(sequence + 16, "max", 59);
        builder.AddAttribute(sequence + 17, "value", Duration.Minutes.ToString("D2"));
        builder.AddAttribute(sequence + 18, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, SetMinutes));
        builder.CloseElement();

        builder.OpenElement(sequence + 19, "span");
        builder.AddAttribute(sequence + 20, "style", UnitLabelStyle);
        builder.AddContent(sequence + 21, "m");
        builder.CloseElement();
    }
}

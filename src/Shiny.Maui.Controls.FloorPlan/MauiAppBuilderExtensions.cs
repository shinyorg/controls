using SkiaSharp.Views.Maui.Controls.Hosting;

namespace Shiny.Maui.Controls.FloorPlan;

public static class MauiAppBuilderExtensions
{
    /// <summary>
    /// Registers what <see cref="FloorPlanView"/> needs.
    /// </summary>
    /// <remarks>
    /// Only SkiaSharp, and only because MAUI will not hand an <c>SKCanvasView</c> a platform view
    /// without it: a plan on a page in an app that forgot this call is a blank rectangle with nothing
    /// in the log. Safe to call alongside <c>UseShinyOffice</c> or a hand-written
    /// <c>UseSkiaSharp</c> - SkiaSharp's own registration is idempotent.
    /// </remarks>
    public static MauiAppBuilder UseShinyFloorPlan(this MauiAppBuilder builder)
    {
        builder.UseSkiaSharp();
        return builder;
    }
}

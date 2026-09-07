using Microsoft.Maui.Graphics;

namespace Shiny.Maui.Controls.Gantt.Internal;

/// <summary>
/// The two-tier timeline header: a coarse row of months or years above a fine row of days or hours.
/// </summary>
/// <remarks>
/// Two tiers rather than one because a single row of day numbers is unreadable — "14, 15, 16" tells
/// you nothing about which month you are looking at, and a plan is almost always scrolled away from
/// wherever the reader last saw a month name.
/// </remarks>
sealed class GanttHeaderDrawable(GanttView owner) : IDrawable
{
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var palette = owner.Palette;
        if (palette is null || owner.Metrics is null)
            return;

        var upperHeight = dirtyRect.Height / 2;
        var line = owner.GridLineColor ?? palette.OutlineVariant;
        var ink = palette.OnSurface;
        var mutedInk = palette.OnSurfaceVariant;

        canvas.FillColor = palette.Surface;
        canvas.FillRectangle(dirtyRect);

        canvas.FontSize = 11;

        // Upper tier
        canvas.FontColor = ink;
        foreach (var tick in owner.UpperTicks)
        {
            var x = (float)tick.X;
            canvas.StrokeColor = line;
            canvas.StrokeSize = 1;
            canvas.DrawLine(x, 0, x, dirtyRect.Height);

            canvas.DrawString(tick.Label, x + 6, 0, (float)Math.Max(tick.Width - 8, 0), upperHeight,
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }

        // Lower tier
        foreach (var tick in owner.LowerTicks)
        {
            var x = (float)tick.X;
            var w = (float)tick.Width;

            if (tick.IsNonWorking && owner.ShowNonWorkingShading)
            {
                canvas.FillColor = (owner.NonWorkingColor ?? palette.SurfaceContainer).WithAlpha(0.8f);
                canvas.FillRectangle(x, upperHeight, w, upperHeight);
            }

            if (tick.IsToday && owner.ShowTodayMarker)
            {
                canvas.FillColor = (owner.TodayColor ?? palette.Error).WithAlpha(0.18f);
                canvas.FillRectangle(x, upperHeight, w, upperHeight);
            }

            canvas.StrokeColor = line;
            canvas.StrokeSize = 1;
            canvas.DrawLine(x, upperHeight, x, dirtyRect.Height);

            // Below about twenty pixels a label is a smear, and a header of smears reads worse than
            // a header of plain ticks.
            if (w >= 20)
            {
                canvas.FontColor = tick.IsToday ? (owner.TodayColor ?? palette.Error) : mutedInk;
                canvas.DrawString(tick.Label, x, upperHeight, w, upperHeight,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }

        canvas.StrokeColor = line;
        canvas.StrokeSize = 1;
        canvas.DrawLine(0, upperHeight, dirtyRect.Width, upperHeight);
        canvas.DrawLine(0, dirtyRect.Height - 0.5f, dirtyRect.Width, dirtyRect.Height - 0.5f);
    }
}

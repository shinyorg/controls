using Microsoft.Maui.Graphics;
using Shiny.Controls.Gantt;

namespace Shiny.Maui.Controls.Gantt.Internal;

/// <summary>
/// Paints the whole timeline body — shading, grid, bars, progress, baselines, deadlines and
/// dependency arrows — onto a single canvas.
/// </summary>
/// <remarks>
/// <para>
/// One drawable rather than a stack of them, and drawn shapes rather than a view per bar. A plan of
/// any size makes the difference stark: five hundred bars is five hundred native views to create,
/// measure, arrange and recycle on every scroll, against one canvas that redraws in a single pass.
/// It is also the only way to get the shapes right — a summary bracket with its turned-down ends and
/// a milestone diamond are not rectangles with a corner radius.
/// </para>
/// <para>
/// Consumers who need a real view per bar set <see cref="GanttView.BarTemplate"/>, which turns this
/// off for the bars and leaves it drawing the chrome underneath them.
/// </para>
/// </remarks>
sealed class GanttTimelineDrawable(GanttView owner) : IDrawable
{
    const float ArrowSize = 5f;
    const float DeadlineSize = 6f;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var metrics = owner.Metrics;
        var model = owner.PlanModel;

        if (metrics is null || model is null)
            return;

        var palette = owner.Palette;
        if (palette is null)
            return;

        this.DrawChrome(canvas, dirtyRect, metrics, palette);

        if (owner.BarTemplate is null)
            this.DrawBars(canvas, metrics, model, palette);

        if (owner.ShowDependencies)
            this.DrawDependencies(canvas, metrics, model, palette);

        this.DrawLinkAffordances(canvas, metrics, palette);
    }


    /// <summary>
    /// The two connector dots on the selected bar, and the rubber band while a link is being drawn.
    /// </summary>
    /// <remarks>
    /// Only the selected task gets dots. Showing them on every bar puts two more targets on every row
    /// competing with the resize grips for the same handful of pixels, which on a phone means a user
    /// trying to lengthen a task draws a dependency instead.
    /// </remarks>
    void DrawLinkAffordances(ICanvas canvas, GanttLayoutMetrics metrics, GanttPalette palette)
    {
        if (!owner.AllowDependencyEdit || owner.IsReadOnly)
            return;

        var accent = owner.DependencyColor ?? palette.Primary;

        if (owner.SelectedTask is { } selected && metrics.RowIndexOf(selected.Id) >= 0)
        {
            var (start, end) = metrics.ConnectorsOf(selected);
            canvas.FillColor = accent;

            foreach (var dot in new[] { start, end })
                canvas.FillCircle((float)dot.X, (float)dot.Y, 4f);
        }

        if (owner.LinkSource is { } source)
        {
            var (start, end) = metrics.ConnectorsOf(source);
            var from = owner.LinkTarget.X < start.X ? start : end;

            canvas.StrokeColor = accent;
            canvas.StrokeSize = 2;
            canvas.StrokeDashPattern = [4f, 3f];
            canvas.DrawLine((float)from.X, (float)from.Y, (float)owner.LinkTarget.X, (float)owner.LinkTarget.Y);
            canvas.StrokeDashPattern = null;
        }
    }


    // ---------------------------------------------------------------------------------------------
    // Background
    // ---------------------------------------------------------------------------------------------

    void DrawChrome(ICanvas canvas, RectF bounds, GanttLayoutMetrics metrics, GanttPalette palette)
    {
        var height = (float)Math.Max(metrics.ContentHeight, bounds.Height);

        if (owner.ShowNonWorkingShading)
        {
            // A consumer's explicit colour is taken at full strength; the themed default is knocked
            // back so bars still read through the weekend band.
            canvas.FillColor = owner.NonWorkingColor ?? palette.SurfaceContainer.WithAlpha(0.7f);

            if (owner.NonWorkingColor is { } explicitShade)
                canvas.FillColor = explicitShade;

            foreach (var (start, end) in owner.NonWorkingIntervals)
            {
                var x = (float)metrics.XOf(start);
                var w = (float)(metrics.XOf(end) - x);
                if (w > 0)
                    canvas.FillRectangle(x, 0, w, height);
            }
        }

        // Highlight bands sit above the weekend shading and below everything else, so a sprint that
        // spans a weekend reads as one band rather than being cut in half by it.
        foreach (var range in owner.HighlightRanges)
        {
            var x = (float)metrics.XOf(range.Start);
            var w = (float)(metrics.XOf(range.End) - x);
            if (w <= 0)
                continue;

            canvas.FillColor = (range.Color ?? palette.Primary).WithAlpha(0.12f);
            canvas.FillRectangle(x, 0, w, height);
        }

        var gridColor = owner.GridLineColor ?? palette.OutlineVariant;

        if (owner.ShowGridLines)
        {
            canvas.StrokeColor = gridColor;
            canvas.StrokeSize = 1;

            foreach (var tick in owner.LowerTicks)
            {
                var x = (float)tick.X;
                canvas.DrawLine(x, 0, x, height);
            }
        }

        if (owner.ShowRowSeparators)
        {
            canvas.StrokeColor = gridColor.WithAlpha(0.6f);
            canvas.StrokeSize = 1;

            var rowHeight = (float)metrics.Rows.RowHeight;
            for (var row = 1; row <= owner.PlanModel!.Rows.Count; row++)
            {
                var y = row * rowHeight;
                canvas.DrawLine(0, y, bounds.Width, y);
            }
        }

        if (owner.ShowTodayMarker)
        {
            var x = (float)metrics.XOf(owner.Now);
            if (x >= 0 && x <= bounds.Width)
            {
                canvas.StrokeColor = owner.TodayColor ?? palette.Error;
                canvas.StrokeSize = 2;
                canvas.DrawLine(x, 0, x, height);
            }
        }
    }


    // ---------------------------------------------------------------------------------------------
    // Bars
    // ---------------------------------------------------------------------------------------------

    void DrawBars(ICanvas canvas, GanttLayoutMetrics metrics, GanttModel model, GanttPalette palette)
    {
        canvas.FontSize = 12;

        foreach (var row in model.Rows)
        {
            var task = row.Task;
            var rect = metrics.BarOf(task, row.Index);
            if (rect.IsEmpty)
                continue;

            if (owner.ShowBaselines)
                this.DrawBaseline(canvas, metrics, task, palette);

            var fill = this.FillFor(task, palette);

            switch (task.Kind)
            {
                case GanttTaskKind.Milestone:
                    DrawMilestone(canvas, rect, fill);
                    break;

                case GanttTaskKind.Summary:
                case GanttTaskKind.Project:
                    DrawSummary(canvas, rect, fill);
                    break;

                default:
                    this.DrawTaskBar(canvas, rect, task, fill, palette);
                    break;
            }

            if (owner.ShowDeadlines && task.Deadline is { } deadline)
                this.DrawDeadline(canvas, metrics, rect, deadline, palette);

            if (owner.IsSelected(task))
            {
                canvas.StrokeColor = owner.SelectionColor ?? palette.Primary;
                canvas.StrokeSize = 2;
                canvas.DrawRoundedRectangle(Grow(rect, 2), 4);
            }

            this.DrawLabel(canvas, rect, task, fill, palette);
        }
    }


    void DrawTaskBar(ICanvas canvas, GanttRect rect, GanttTask task, Color fill, GanttPalette palette)
    {
        var r = ToRect(rect);
        var radius = (float)Math.Min(4, rect.Height / 2);

        canvas.FillColor = fill.WithAlpha(0.35f);
        canvas.FillRoundedRectangle(r, radius);

        if (owner.ShowProgress && task.Progress > 0)
        {
            var progress = ToRect(rect with { Width = rect.Width * Math.Clamp(task.Progress, 0, 1) });
            canvas.FillColor = owner.ProgressColor ?? fill;

            // Clip so the fill's own rounded corners never poke out past the bar's on a task that is
            // only a few percent done.
            canvas.SaveState();
            canvas.ClipRectangle(progress);
            canvas.FillRoundedRectangle(r, radius);
            canvas.RestoreState();
        }

        canvas.StrokeColor = fill;
        canvas.StrokeSize = task.IsCritical && owner.ShowCriticalPath ? 2 : 1;
        canvas.DrawRoundedRectangle(r, radius);
    }


    /// <summary>
    /// The summary bracket: a flat bar with the two ends turned down, which is how every planning
    /// tool since the eighties has distinguished a phase from the work inside it.
    /// </summary>
    static void DrawSummary(ICanvas canvas, GanttRect rect, Color fill)
    {
        var r = ToRect(rect);
        var tail = (float)Math.Min(6, rect.Width / 2);

        var path = new PathF();
        path.MoveTo(r.Left, r.Top);
        path.LineTo(r.Right, r.Top);
        path.LineTo(r.Right, r.Bottom);
        path.LineTo(r.Right - tail, r.Center.Y);
        path.LineTo(r.Left + tail, r.Center.Y);
        path.LineTo(r.Left, r.Bottom);
        path.Close();

        canvas.FillColor = fill;
        canvas.FillPath(path);
    }


    static void DrawMilestone(ICanvas canvas, GanttRect rect, Color fill)
    {
        var r = ToRect(rect);
        var path = new PathF();
        path.MoveTo(r.Center.X, r.Top);
        path.LineTo(r.Right, r.Center.Y);
        path.LineTo(r.Center.X, r.Bottom);
        path.LineTo(r.Left, r.Center.Y);
        path.Close();

        canvas.FillColor = fill;
        canvas.FillPath(path);
    }


    void DrawBaseline(ICanvas canvas, GanttLayoutMetrics metrics, GanttTask task, GanttPalette palette)
    {
        var baseline = metrics.BaselineOf(task);
        if (baseline.IsEmpty)
            return;

        canvas.FillColor = (owner.BaselineColor ?? palette.OnSurfaceVariant).WithAlpha(0.45f);
        canvas.FillRectangle(ToRect(baseline));
    }


    /// <summary>A downward pointing marker at the deadline, filled when the task has already blown it.</summary>
    void DrawDeadline(ICanvas canvas, GanttLayoutMetrics metrics, GanttRect bar, DateTimeOffset deadline, GanttPalette palette)
    {
        var x = (float)metrics.XOf(deadline);
        var y = (float)bar.CenterY;
        var color = owner.DeadlineColor ?? palette.Error;

        var path = new PathF();
        path.MoveTo(x - DeadlineSize, y - DeadlineSize);
        path.LineTo(x + DeadlineSize, y - DeadlineSize);
        path.LineTo(x, y + DeadlineSize);
        path.Close();

        canvas.FillColor = color;
        canvas.FillPath(path);
    }


    void DrawLabel(ICanvas canvas, GanttRect rect, GanttTask task, Color fill, GanttPalette palette)
    {
        if (owner.BarLabel == GanttBarLabel.None || string.IsNullOrEmpty(task.Name))
            return;

        const float gutter = 6f;
        var r = ToRect(rect);

        // Inside only works while the bar is wide enough to hold the text; below that the label falls
        // out to the right rather than being clipped to an unreadable stub.
        var inside = owner.BarLabel == GanttBarLabel.Inside && rect.Width > task.Name.Length * 7;

        if (inside)
        {
            canvas.FontColor = GanttColors.InkFor(owner.ProgressColor ?? fill);
            canvas.DrawString(task.Name, r.Left + gutter, r.Top, r.Width - (gutter * 2), r.Height,
                HorizontalAlignment.Left, VerticalAlignment.Center);
            return;
        }

        canvas.FontColor = palette.OnSurface;

        if (owner.BarLabel == GanttBarLabel.Left)
        {
            canvas.DrawString(task.Name, r.Left - 200 - gutter, r.Top, 200, r.Height,
                HorizontalAlignment.Right, VerticalAlignment.Center);
        }
        else
        {
            canvas.DrawString(task.Name, r.Right + gutter, r.Top, 240, r.Height,
                HorizontalAlignment.Left, VerticalAlignment.Center);
        }
    }


    Color FillFor(GanttTask task, GanttPalette palette)
    {
        if (owner.ShowCriticalPath && task.IsCritical)
            return owner.CriticalColor ?? palette.Error;

        var themed = task.Kind switch
        {
            GanttTaskKind.Milestone => owner.MilestoneColor ?? palette.Tertiary,
            GanttTaskKind.Summary or GanttTaskKind.Project => owner.SummaryColor ?? palette.OnSurfaceVariant,
            _ => owner.BarColor ?? palette.Primary
        };

        // The task's own colour wins over everything except the critical highlight, which is a
        // statement about the plan rather than about the task.
        return GanttColors.Parse(task.Color, themed);
    }


    // ---------------------------------------------------------------------------------------------
    // Dependencies
    // ---------------------------------------------------------------------------------------------

    void DrawDependencies(ICanvas canvas, GanttLayoutMetrics metrics, GanttModel model, GanttPalette palette)
    {
        var defaultColor = owner.DependencyColor ?? palette.OnSurfaceVariant;
        var criticalColor = owner.CriticalColor ?? palette.Error;

        foreach (var dependency in model.Dependencies)
        {
            var route = metrics.RouteOf(dependency);
            if (route.Count < 2)
                continue;

            var color = GanttColors.Parse(
                dependency.Color,
                owner.ShowCriticalPath && dependency.IsCritical ? criticalColor : defaultColor
            );

            canvas.StrokeColor = color;
            canvas.StrokeSize = owner.ShowCriticalPath && dependency.IsCritical ? 2 : 1;

            var path = new PathF();
            path.MoveTo((float)route[0].X, (float)route[0].Y);
            for (var i = 1; i < route.Count; i++)
                path.LineTo((float)route[i].X, (float)route[i].Y);

            canvas.DrawPath(path);
            DrawArrowHead(canvas, route[^2], route[^1], color);
        }
    }


    /// <summary>
    /// The arrowhead, oriented from the last segment. Every route ends with a horizontal run into the
    /// bar's edge, so the head only ever points left or right — which keeps this to a sign test
    /// instead of a rotation.
    /// </summary>
    static void DrawArrowHead(ICanvas canvas, GanttPoint from, GanttPoint to, Color color)
    {
        var x = (float)to.X;
        var y = (float)to.Y;
        var direction = to.X >= from.X ? 1 : -1;

        var path = new PathF();
        path.MoveTo(x, y);
        path.LineTo(x - (direction * ArrowSize), y - ArrowSize);
        path.LineTo(x - (direction * ArrowSize), y + ArrowSize);
        path.Close();

        canvas.FillColor = color;
        canvas.FillPath(path);
    }


    static RectF ToRect(GanttRect rect) =>
        new((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);

    static RectF Grow(GanttRect rect, float by) =>
        new((float)rect.X - by, (float)rect.Y - by, (float)rect.Width + (by * 2), (float)rect.Height + (by * 2));
}

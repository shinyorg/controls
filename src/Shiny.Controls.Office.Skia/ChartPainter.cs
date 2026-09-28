using System.Globalization;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using SkiaSharp;

namespace Shiny.Controls.Office.Skia;

/// <summary>
/// Draws a spreadsheet chart — column, bar, line, area, pie, scatter — into a rectangle.
/// </summary>
/// <remarks>
/// Deliberately the chart Excel draws by default rather than a charting library's idea of one: white
/// plot area, light horizontal gridlines, the Office accent colours in series order, a legend at the
/// bottom (at the right for a pie). A chart made here and opened in Excel should look like the same
/// chart, and one made in Excel with default styling should look like itself here.
/// </remarks>
public sealed class ChartPainter : IDisposable
{
    readonly SkiaTextMeasurer measurer;
    readonly bool ownsMeasurer;
    readonly SKPaint fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    readonly SKPaint stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };

    /// <summary>Office's six accent colours, the order Excel colours series in.</summary>
    public static readonly IReadOnlyList<SKColor> Palette =
    [
        new(0x44, 0x72, 0xC4), new(0xED, 0x7D, 0x31), new(0xA5, 0xA5, 0xA5),
        new(0xFF, 0xC0, 0x00), new(0x5B, 0x9B, 0xD5), new(0x70, 0xAD, 0x47)
    ];

    public ChartPainter(SkiaTextMeasurer? measurer = null)
    {
        this.ownsMeasurer = measurer is null;
        this.measurer = measurer ?? new SkiaTextMeasurer();
    }

    SKFont Font(float size, bool bold = false)
        => this.measurer.GetFont(TextStyle.Default with { FontFamily = "Calibri", FontSize = size, Bold = bold });

    static SKColor SeriesColor(int index, ArgbColor? color)
        => color is { } c ? new SKColor(c.R, c.G, c.B, c.A) : Palette[index % Palette.Count];

    public void Paint(SKCanvas canvas, SKRect bounds, SheetChart chart, IReadOnlyList<ChartSeriesData> data)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(chart);

        canvas.Save();
        canvas.ClipRect(bounds);

        this.fill.Color = SKColors.White;
        canvas.DrawRect(bounds, this.fill);

        this.stroke.Color = new SKColor(0xD9, 0xD9, 0xD9);
        this.stroke.StrokeWidth = 1;
        canvas.DrawRect(new SKRect(bounds.Left + 0.5f, bounds.Top + 0.5f, bounds.Right - 0.5f, bounds.Bottom - 0.5f), this.stroke);

        var area = new SKRect(bounds.Left + 10, bounds.Top + 8, bounds.Right - 10, bounds.Bottom - 8);

        if (!string.IsNullOrEmpty(chart.Title))
        {
            var titleFont = this.Font(14);
            this.fill.Color = new SKColor(0x59, 0x59, 0x59);
            var width = titleFont.MeasureText(chart.Title);
            canvas.DrawText(chart.Title, area.MidX - width / 2, area.Top - titleFont.Metrics.Ascent, SKTextAlign.Left, titleFont, this.fill);
            area.Top += titleFont.Size + 10;
        }

        if (chart.Kind == ChartKind.Unknown || data.Count == 0)
        {
            this.Placeholder(canvas, area, chart.Kind == ChartKind.Unknown ? "Chart (not previewable)" : "No data");
            canvas.Restore();
            return;
        }

        if (chart.Kind == ChartKind.Pie)
            this.PaintPie(canvas, area, chart, data[0]);
        else
            this.PaintCartesian(canvas, area, chart, data);

        canvas.Restore();
    }

    void Placeholder(SKCanvas canvas, SKRect area, string text)
    {
        var font = this.Font(11);
        this.fill.Color = new SKColor(0x80, 0x80, 0x80);
        var width = font.MeasureText(text);
        canvas.DrawText(text, area.MidX - width / 2, area.MidY, SKTextAlign.Left, font, this.fill);
    }

    // ---- axes charts ----

    void PaintCartesian(SKCanvas canvas, SKRect area, SheetChart chart, IReadOnlyList<ChartSeriesData> data)
    {
        var small = this.Font(9);

        // Legend along the bottom.
        if (chart.ShowLegend && data.Count > 0)
            area.Bottom = this.Legend(canvas, area, data.Select((s, i) => (s.Name, SeriesColor(i, s.Color))).ToList());

        var categories = data.Max(x => Math.Max(x.Values.Count, x.Categories.Count));
        if (categories == 0)
        {
            this.Placeholder(canvas, area, "No data");
            return;
        }

        var labels = data.FirstOrDefault(x => x.Categories.Count > 0)?.Categories ?? [];

        // Value range: stacked charts total per category, the rest take each value on its own.
        double min = 0, max = 0;
        for (var c = 0; c < categories; c++)
        {
            double positive = 0, negative = 0;
            foreach (var series in data)
            {
                if (c >= series.Values.Count || series.Values[c] is not { } v)
                    continue;

                if (chart.Stacked)
                {
                    if (v >= 0) positive += v; else negative += v;
                }
                else
                {
                    min = Math.Min(min, v);
                    max = Math.Max(max, v);
                }
            }

            if (chart.Stacked)
            {
                min = Math.Min(min, negative);
                max = Math.Max(max, positive);
            }
        }

        if (max == min)
            max = min + 1;

        var (axisMin, axisMax, step) = NiceScale(min, max);
        var horizontal = chart.Kind == ChartKind.Bar;

        // Room for the value labels on the value axis and the category labels on the other.
        var valueLabelWidth = Enumerable.Range(0, (int)Math.Round((axisMax - axisMin) / step) + 1)
            .Select(i => small.MeasureText(Format(axisMin + i * step)))
            .DefaultIfEmpty(10)
            .Max();

        var categoryLabelWidth = labels.Count == 0 ? 10 : labels.Take(50).Max(x => small.MeasureText(x));

        var plot = horizontal
            ? new SKRect(area.Left + Math.Min(categoryLabelWidth, area.Width / 3) + 6, area.Top + 4, area.Right - 4, area.Bottom - small.Size - 6)
            : new SKRect(area.Left + valueLabelWidth + 6, area.Top + 4, area.Right - 4, area.Bottom - small.Size - 6);

        if (plot.Width < 10 || plot.Height < 10)
            return;

        float ValueToAxis(double value)
            => horizontal
                ? plot.Left + (float)((value - axisMin) / (axisMax - axisMin)) * plot.Width
                : plot.Bottom - (float)((value - axisMin) / (axisMax - axisMin)) * plot.Height;

        // Gridlines and value labels.
        this.stroke.StrokeWidth = 1;
        this.stroke.Color = new SKColor(0xD9, 0xD9, 0xD9);
        this.fill.Color = new SKColor(0x59, 0x59, 0x59);

        for (var value = axisMin; value <= axisMax + step / 2; value += step)
        {
            var position = ValueToAxis(value);
            var text = Format(value);

            if (horizontal)
            {
                canvas.DrawLine(position, plot.Top, position, plot.Bottom, this.stroke);
                var width = small.MeasureText(text);
                canvas.DrawText(text, position - width / 2, plot.Bottom + small.Size + 2, SKTextAlign.Left, small, this.fill);
            }
            else
            {
                canvas.DrawLine(plot.Left, position, plot.Right, position, this.stroke);
                var width = small.MeasureText(text);
                canvas.DrawText(text, plot.Left - width - 4, position + small.Size / 3, SKTextAlign.Left, small, this.fill);
            }
        }

        // Category axis line and labels.
        var band = (horizontal ? plot.Height : plot.Width) / categories;
        this.stroke.Color = new SKColor(0xBF, 0xBF, 0xBF);

        var zero = ValueToAxis(Math.Clamp(0, axisMin, axisMax));
        if (horizontal)
            canvas.DrawLine(zero, plot.Top, zero, plot.Bottom, this.stroke);
        else
            canvas.DrawLine(plot.Left, zero, plot.Right, zero, this.stroke);

        var labelEvery = Math.Max(1, (int)Math.Ceiling(categoryLabelWidth / Math.Max(1, band - 4)));
        for (var c = 0; c < categories; c++)
        {
            var label = c < labels.Count ? labels[c] : (c + 1).ToString(CultureInfo.CurrentCulture);
            if (horizontal)
            {
                var y = plot.Top + band * (c + 0.5f) + small.Size / 3;
                var width = Math.Min(small.MeasureText(label), plot.Left - area.Left - 6);
                canvas.DrawText(label, plot.Left - width - 4, y, SKTextAlign.Left, small, this.fill);
            }
            else if (c % labelEvery == 0)
            {
                var width = small.MeasureText(label);
                canvas.DrawText(label, plot.Left + band * (c + 0.5f) - width / 2, plot.Bottom + small.Size + 2, SKTextAlign.Left, small, this.fill);
            }
        }

        switch (chart.Kind)
        {
            case ChartKind.Column:
            case ChartKind.Bar:
                this.Bars(canvas, plot, chart, data, categories, band, horizontal, ValueToAxis);
                break;

            case ChartKind.Area:
                this.Areas(canvas, plot, data, categories, band, ValueToAxis, zero);
                break;

            default:
                this.Lines(canvas, data, categories, band, plot, ValueToAxis, markersOnly: chart.Kind == ChartKind.Scatter);
                break;
        }
    }

    void Bars(SKCanvas canvas, SKRect plot, SheetChart chart, IReadOnlyList<ChartSeriesData> data, int categories, float band, bool horizontal, Func<double, float> axis)
    {
        var gap = band * 0.6f / 1.6f;
        var groupWidth = band - gap;
        var barWidth = chart.Stacked ? groupWidth : groupWidth / data.Count;

        for (var c = 0; c < categories; c++)
        {
            double positive = 0, negative = 0;

            for (var s = 0; s < data.Count; s++)
            {
                if (c >= data[s].Values.Count || data[s].Values[c] is not { } value)
                    continue;

                double from, to;
                if (chart.Stacked)
                {
                    if (value >= 0) { from = positive; positive += value; to = positive; }
                    else { from = negative; negative += value; to = negative; }
                }
                else
                {
                    from = 0;
                    to = value;
                }

                var start = band * c + gap / 2 + (chart.Stacked ? 0 : barWidth * s);
                var a = axis(from);
                var b = axis(to);

                var rect = horizontal
                    ? new SKRect(Math.Min(a, b), plot.Top + start, Math.Max(a, b), plot.Top + start + barWidth)
                    : new SKRect(plot.Left + start, Math.Min(a, b), plot.Left + start + barWidth, Math.Max(a, b));

                this.fill.Color = SeriesColor(s, data[s].Color);
                canvas.DrawRect(rect, this.fill);
            }
        }
    }

    void Lines(SKCanvas canvas, IReadOnlyList<ChartSeriesData> data, int categories, float band, SKRect plot, Func<double, float> axis, bool markersOnly)
    {
        for (var s = 0; s < data.Count; s++)
        {
            var color = SeriesColor(s, data[s].Color);
            this.stroke.Color = color;
            this.stroke.StrokeWidth = 2.25f;
            this.stroke.StrokeJoin = SKStrokeJoin.Round;
            this.fill.Color = color;

            using var path = new SKPath();
            var started = false;

            for (var c = 0; c < categories; c++)
            {
                if (c >= data[s].Values.Count || data[s].Values[c] is not { } value)
                {
                    started = false;
                    continue;
                }

                var point = new SKPoint(plot.Left + band * (c + 0.5f), axis(value));
                if (!started)
                    path.MoveTo(point);
                else
                    path.LineTo(point);

                started = true;

                if (markersOnly)
                    canvas.DrawCircle(point, 3.5f, this.fill);
            }

            if (!markersOnly)
                canvas.DrawPath(path, this.stroke);
        }

        this.stroke.StrokeWidth = 1;
    }

    void Areas(SKCanvas canvas, SKRect plot, IReadOnlyList<ChartSeriesData> data, int categories, float band, Func<double, float> axis, float zero)
    {
        for (var s = 0; s < data.Count; s++)
        {
            using var path = new SKPath();
            path.MoveTo(plot.Left + band * 0.5f, zero);

            for (var c = 0; c < categories; c++)
            {
                var value = c < data[s].Values.Count ? data[s].Values[c] ?? 0 : 0;
                path.LineTo(plot.Left + band * (c + 0.5f), axis(value));
            }

            path.LineTo(plot.Left + band * (categories - 0.5f), zero);
            path.Close();

            var color = SeriesColor(s, data[s].Color);
            this.fill.Color = color.WithAlpha(200);
            canvas.DrawPath(path, this.fill);
        }
    }

    // ---- pie ----

    void PaintPie(SKCanvas canvas, SKRect area, SheetChart chart, ChartSeriesData series)
    {
        var values = series.Values.Select(x => Math.Max(0, x ?? 0)).ToList();
        var total = values.Sum();
        if (total <= 0)
        {
            this.Placeholder(canvas, area, "No data");
            return;
        }

        var labels = series.Categories;
        if (chart.ShowLegend)
        {
            var entries = values.Select((_, i) => (i < labels.Count ? labels[i] : (i + 1).ToString(CultureInfo.CurrentCulture), Palette[i % Palette.Count])).ToList();
            area.Right = this.LegendRight(canvas, area, entries);
        }

        var radius = Math.Min(area.Width, area.Height) / 2 - 4;
        if (radius <= 4)
            return;

        var centre = new SKPoint(area.MidX, area.MidY);
        var oval = new SKRect(centre.X - radius, centre.Y - radius, centre.X + radius, centre.Y + radius);
        var angle = -90f;

        this.stroke.Color = SKColors.White;
        this.stroke.StrokeWidth = 1.5f;

        for (var i = 0; i < values.Count; i++)
        {
            var sweep = (float)(values[i] / total * 360);
            if (sweep <= 0)
                continue;

            using var path = new SKPath();
            path.MoveTo(centre);
            path.ArcTo(oval, angle, sweep, false);
            path.Close();

            this.fill.Color = series.Color is { } fixedColor && values.Count == 1
                ? new SKColor(fixedColor.R, fixedColor.G, fixedColor.B)
                : Palette[i % Palette.Count];

            canvas.DrawPath(path, this.fill);
            canvas.DrawPath(path, this.stroke);
            angle += sweep;
        }

        this.stroke.StrokeWidth = 1;
    }

    // ---- legend ----

    /// <summary>Draws a legend along the bottom; returns the new bottom of the plot area above it.</summary>
    float Legend(SKCanvas canvas, SKRect area, IReadOnlyList<(string Name, SKColor Color)> entries)
    {
        var font = this.Font(9);
        var swatch = 7f;
        var widths = entries.Select(x => swatch + 4 + font.MeasureText(x.Name) + 12).ToList();
        var total = widths.Sum();
        var x = area.MidX - Math.Min(total, area.Width) / 2;
        var y = area.Bottom - font.Size / 2;

        for (var i = 0; i < entries.Count; i++)
        {
            if (x + widths[i] > area.Right + 1)
                break;

            this.fill.Color = entries[i].Color;
            canvas.DrawRect(new SKRect(x, y - swatch / 2 - 2, x + swatch, y + swatch / 2 - 2), this.fill);

            this.fill.Color = new SKColor(0x59, 0x59, 0x59);
            canvas.DrawText(entries[i].Name, x + swatch + 4, y + font.Size / 3 - 1, SKTextAlign.Left, font, this.fill);
            x += widths[i];
        }

        return area.Bottom - font.Size - 8;
    }

    /// <summary>Draws a legend down the right; returns the new right edge of the plot area.</summary>
    float LegendRight(SKCanvas canvas, SKRect area, IReadOnlyList<(string Name, SKColor Color)> entries)
    {
        var font = this.Font(9);
        var swatch = 7f;
        var width = Math.Min(area.Width / 3, entries.Select(x => font.MeasureText(x.Name)).DefaultIfEmpty(0).Max() + swatch + 8);
        var left = area.Right - width;
        var lineHeight = font.Size + 5;
        var y = area.MidY - entries.Count * lineHeight / 2 + lineHeight / 2;

        foreach (var (name, color) in entries)
        {
            if (y > area.Bottom)
                break;

            this.fill.Color = color;
            canvas.DrawRect(new SKRect(left, y - swatch / 2 - 2, left + swatch, y + swatch / 2 - 2), this.fill);

            this.fill.Color = new SKColor(0x59, 0x59, 0x59);
            canvas.DrawText(name, left + swatch + 4, y + font.Size / 3 - 1, SKTextAlign.Left, font, this.fill);
            y += lineHeight;
        }

        return left - 8;
    }

    // ---- scale ----

    /// <summary>Round axis bounds and a step of 1, 2 or 5 times a power of ten — about five gridlines.</summary>
    internal static (double Min, double Max, double Step) NiceScale(double min, double max)
    {
        var range = max - min;
        var rough = range / 5;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var residual = rough / magnitude;

        var step = residual switch
        {
            > 5 => 10 * magnitude,
            > 2 => 5 * magnitude,
            > 1 => 2 * magnitude,
            _ => magnitude
        };

        var niceMin = Math.Floor(min / step) * step;
        var niceMax = Math.Ceiling(max / step) * step;
        if (niceMax <= niceMin)
            niceMax = niceMin + step;

        return (niceMin, niceMax, step);
    }

    static string Format(double value)
    {
        var magnitude = Math.Abs(value);
        return magnitude switch
        {
            >= 1_000_000 => (value / 1_000_000).ToString("0.#", CultureInfo.CurrentCulture) + "M",
            >= 10_000 => (value / 1_000).ToString("0.#", CultureInfo.CurrentCulture) + "K",
            _ => value.ToString("#,##0.##", CultureInfo.CurrentCulture)
        };
    }

    public void Dispose()
    {
        if (this.ownsMeasurer)
            this.measurer.Dispose();

        this.fill.Dispose();
        this.stroke.Dispose();
    }
}

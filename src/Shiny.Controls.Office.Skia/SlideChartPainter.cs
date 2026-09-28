using System.Globalization;
using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using SkiaSharp;

namespace Shiny.Controls.Office.Skia;

/// <summary>
/// Draws a slide chart — clustered column or bar, line, or pie — with its title, value axis,
/// gridlines, category labels and legend, the way PowerPoint's default chart style lays one out.
/// </summary>
public sealed class SlideChartPainter(SkiaTextMeasurer measurer) : IDisposable
{
    readonly SKPaint fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    readonly SKPaint stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };

    static readonly ArgbColor Ink = new(255, 0x59, 0x59, 0x59);
    static readonly ArgbColor Grid = new(255, 0xD9, 0xD9, 0xD9);

    static readonly ArgbColor[] Fallback =
    [
        new(255, 0x44, 0x72, 0xC4), new(255, 0xED, 0x7D, 0x31), new(255, 0xA5, 0xA5, 0xA5),
        new(255, 0xFF, 0xC0, 0x00), new(255, 0x5B, 0x9B, 0xD5), new(255, 0x70, 0xAD, 0x47)
    ];

    public void Paint(SKCanvas canvas, SlideChart chart, SKRect bounds)
    {
        canvas.Save();
        canvas.ClipRect(bounds);

        this.fill.Shader = null;
        this.fill.Color = SKColors.White;
        canvas.DrawRect(bounds, this.fill);

        var unit = Math.Max(8, Math.Min(bounds.Width, bounds.Height) / 24);
        var label = TextStyle.Default with { FontSize = unit * 0.9, Color = Ink };
        var titleStyle = label with { FontSize = unit * 1.4 };

        var area = SKRect.Inflate(bounds, -unit, -unit * 0.8f);

        if (!string.IsNullOrWhiteSpace(chart.Title))
        {
            var font = measurer.GetFont(titleStyle);
            this.fill.Color = ToSk(Ink);
            canvas.DrawText(chart.Title, area.MidX, area.Top + (float)titleStyle.FontSize, SKTextAlign.Center, font, this.fill);
            area.Top += (float)titleStyle.FontSize * 1.6f;
        }

        if (chart.ShowLegend && chart.Series.Count > 0)
            area.Bottom -= this.PaintLegend(canvas, chart, area, label);

        if (chart.Kind == SlideChartKind.Pie)
            this.PaintPie(canvas, chart, area);
        else
            this.PaintAxes(canvas, chart, area, label);

        canvas.Restore();
    }

    static ArgbColor ColorOf(SlideChart chart, int series, int point)
        => chart.Kind == SlideChartKind.Pie
            ? Fallback[point % Fallback.Length]
            : chart.Series[series].Color ?? Fallback[series % Fallback.Length];

    float PaintLegend(SKCanvas canvas, SlideChart chart, SKRect area, TextStyle style)
    {
        var font = measurer.GetFont(style);
        var names = chart.Kind == SlideChartKind.Pie ? chart.Categories : chart.Series.Select(x => x.Name).ToList();
        var swatch = (float)style.FontSize * 0.8f;
        var widths = names.Select(x => swatch + 4 + font.MeasureText(x) + 14).ToList();
        var total = widths.Sum();
        var x = area.MidX - total / 2;
        var y = area.Bottom - (float)style.FontSize * 0.4f;

        for (var i = 0; i < names.Count; i++)
        {
            this.fill.Color = ToSk(ColorOf(chart, chart.Kind == SlideChartKind.Pie ? 0 : i, i));
            canvas.DrawRect(new SKRect(x, y - swatch, x + swatch, y), this.fill);

            this.fill.Color = ToSk(Ink);
            canvas.DrawText(names[i], x + swatch + 4, y, SKTextAlign.Left, font, this.fill);
            x += widths[i];
        }

        return (float)style.FontSize * 2;
    }

    void PaintPie(SKCanvas canvas, SlideChart chart, SKRect area)
    {
        if (chart.Series.FirstOrDefault() is not { } series)
            return;

        var values = series.Values.Select(v => Math.Max(0, v)).ToList();
        var total = values.Sum();
        if (total <= 0)
            return;

        var radius = Math.Min(area.Width, area.Height) / 2 * 0.9f;
        var oval = new SKRect(area.MidX - radius, area.MidY - radius, area.MidX + radius, area.MidY + radius);
        var start = -90f;

        this.stroke.Color = SKColors.White;
        this.stroke.StrokeWidth = 1.5f;

        for (var i = 0; i < values.Count; i++)
        {
            var sweep = (float)(values[i] / total * 360);
            using var wedge = new SKPath();
            wedge.MoveTo(oval.MidX, oval.MidY);
            wedge.ArcTo(oval, start, sweep, false);
            wedge.Close();

            this.fill.Color = ToSk(ColorOf(chart, 0, i));
            canvas.DrawPath(wedge, this.fill);
            canvas.DrawPath(wedge, this.stroke);
            start += sweep;
        }
    }

    void PaintAxes(SKCanvas canvas, SlideChart chart, SKRect area, TextStyle style)
    {
        var font = measurer.GetFont(style);
        var horizontal = chart.Kind == SlideChartKind.Bar;

        var (min, max, step) = NiceScale(Math.Min(0, chart.MinValue), Math.Max(0, chart.MaxValue));
        var labels = new List<string>();
        for (var v = min; v <= max + step / 2; v += step)
            labels.Add(Format(v));

        var valueLabelWidth = labels.Select(x => font.MeasureText(x)).DefaultIfEmpty(0).Max() + 6;
        var categoryLabelHeight = (float)style.FontSize * 1.6f;
        var categoryLabelWidth = chart.Categories.Select(x => font.MeasureText(x)).DefaultIfEmpty(0).Max() + 6;

        var plot = horizontal
            ? new SKRect(area.Left + categoryLabelWidth, area.Top, area.Right, area.Bottom - categoryLabelHeight)
            : new SKRect(area.Left + valueLabelWidth, area.Top, area.Right, area.Bottom - categoryLabelHeight);

        if (plot.Width <= 4 || plot.Height <= 4)
            return;

        float ValueToPixel(double value) => horizontal
            ? plot.Left + (float)((value - min) / (max - min) * plot.Width)
            : plot.Bottom - (float)((value - min) / (max - min) * plot.Height);

        // Gridlines and value labels.
        this.stroke.Color = ToSk(Grid);
        this.stroke.StrokeWidth = 1;
        this.fill.Color = ToSk(Ink);

        for (var i = 0; i < labels.Count; i++)
        {
            var value = min + i * step;
            var p = ValueToPixel(value);

            if (horizontal)
            {
                canvas.DrawLine(p, plot.Top, p, plot.Bottom, this.stroke);
                canvas.DrawText(labels[i], p, plot.Bottom + (float)style.FontSize * 1.2f, SKTextAlign.Center, font, this.fill);
            }
            else
            {
                canvas.DrawLine(plot.Left, p, plot.Right, p, this.stroke);
                canvas.DrawText(labels[i], plot.Left - 4, p + (float)style.FontSize * 0.35f, SKTextAlign.Right, font, this.fill);
            }
        }

        var categories = Math.Max(1, chart.Categories.Count);
        var band = (horizontal ? plot.Height : plot.Width) / categories;
        var zero = ValueToPixel(0);

        // Category labels.
        for (var c = 0; c < chart.Categories.Count; c++)
        {
            var middle = (horizontal ? plot.Top : plot.Left) + band * (c + 0.5f);
            this.fill.Color = ToSk(Ink);

            if (horizontal)
                canvas.DrawText(chart.Categories[c], plot.Left - 4, middle + (float)style.FontSize * 0.35f, SKTextAlign.Right, font, this.fill);
            else
                canvas.DrawText(chart.Categories[c], middle, plot.Bottom + (float)style.FontSize * 1.2f, SKTextAlign.Center, font, this.fill);
        }

        if (chart.Kind == SlideChartKind.Line)
        {
            for (var s = 0; s < chart.Series.Count; s++)
            {
                using var path = new SKPath();
                var values = chart.Series[s].Values;
                for (var c = 0; c < Math.Min(values.Count, categories); c++)
                {
                    var x = plot.Left + band * (c + 0.5f);
                    var y = ValueToPixel(values[c]);
                    if (c == 0) path.MoveTo(x, y); else path.LineTo(x, y);
                }

                this.stroke.Color = ToSk(ColorOf(chart, s, 0));
                this.stroke.StrokeWidth = Math.Max(1.5f, band / 30);
                this.stroke.StrokeJoin = SKStrokeJoin.Round;
                canvas.DrawPath(path, this.stroke);
            }

            return;
        }

        // Clustered bars: the band's gap is 219% of a bar, as PowerPoint's default gap width.
        var seriesCount = Math.Max(1, chart.Series.Count);
        var barWidth = band / (seriesCount + 2.19f);
        var offset = barWidth * 2.19f / 2;

        for (var c = 0; c < categories; c++)
        {
            for (var s = 0; s < chart.Series.Count; s++)
            {
                if (c >= chart.Series[s].Values.Count)
                    continue;

                var start = (horizontal ? plot.Top : plot.Left) + band * c + offset + barWidth * s;
                var end = ValueToPixel(chart.Series[s].Values[c]);

                var rect = horizontal
                    ? new SKRect(Math.Min(zero, end), start, Math.Max(zero, end), start + barWidth * 0.95f)
                    : new SKRect(start, Math.Min(zero, end), start + barWidth * 0.95f, Math.Max(zero, end));

                this.fill.Color = ToSk(ColorOf(chart, s, c));
                canvas.DrawRect(rect, this.fill);
            }
        }

        // The category axis line.
        this.stroke.Color = ToSk(Grid);
        this.stroke.StrokeWidth = 1;
        if (horizontal)
            canvas.DrawLine(zero, plot.Top, zero, plot.Bottom, this.stroke);
        else
            canvas.DrawLine(plot.Left, zero, plot.Right, zero, this.stroke);
    }

    /// <summary>An axis scale with round steps — 1, 2 or 5 times a power of ten — about five of them.</summary>
    internal static (double Min, double Max, double Step) NiceScale(double min, double max)
    {
        if (max - min < 1e-9)
            max = min + 1;

        var raw = (max - min) / 5;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var normalized = raw / magnitude;
        var step = (normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10) * magnitude;

        return (Math.Floor(min / step) * step, Math.Ceiling(max / step) * step, step);
    }

    static string Format(double value)
        => Math.Abs(value - Math.Round(value)) < 1e-9
            ? Math.Round(value).ToString("0", CultureInfo.CurrentCulture)
            : value.ToString("0.##", CultureInfo.CurrentCulture);

    public void Dispose()
    {
        this.fill.Dispose();
        this.stroke.Dispose();
    }

    static SKColor ToSk(ArgbColor color) => new(color.R, color.G, color.B, color.A);
}

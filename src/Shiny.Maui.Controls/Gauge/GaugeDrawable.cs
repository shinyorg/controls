using Shiny.Controls.Gauge;

namespace Shiny.Maui.Controls;

/// <summary>Paints a <see cref="Gauge"/> from the outside in: face, track, bands, ticks, text, needle.</summary>
/// <remarks>
/// Every size is a fraction of the fitted radius, so the dial reads the same at 120px on a phone
/// dashboard as at 600px on a desktop. The track and band thicknesses are the exception - they are
/// the caller's own pixel values, as a ring thickness usually is.
/// </remarks>
sealed class GaugeDrawable(Gauge owner) : IDrawable
{
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var scale = owner.DialScale;
        var track = (float)Math.Max(0, owner.TrackThickness);
        // Padding covers the round caps, which run past the ends of the track by half its thickness.
        var layout = GaugeLayout.Fit(dirtyRect.Width, dirtyRect.Height, scale.StartAngle, scale.SweepAngle, Math.Max(2, track / 2), owner.ShowReadout, owner.FaceColor is not null);
        var r = (float)layout.Radius;
        if (r <= 4)
            return;

        var cx = dirtyRect.X + (float)layout.CenterX;
        var cy = dirtyRect.Y + (float)layout.CenterY;
        var position = owner.NeedleValue;

        canvas.Antialias = true;

        if (owner.FaceColor is { } face)
        {
            canvas.FillColor = face;
            canvas.FillCircle(cx, cy, r);
        }

        // Track ring along the outer edge, and the value arc over it.
        var trackRadius = r - track / 2;
        if (track > 0)
        {
            canvas.StrokeSize = track;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeColor = owner.EffectiveTrackColor;
            DrawArc(canvas, cx, cy, trackRadius, scale.StartAngle, scale.EndAngle);

            if (owner.ShowValueArc && position > scale.Minimum)
            {
                var arcColor = owner.EffectiveValueArcColor;
                if (owner.ColorValueArcByRange && owner.RangeAt(position) is { } active)
                    arcColor = active.Color;

                canvas.StrokeColor = arcColor;
                DrawArc(canvas, cx, cy, trackRadius, scale.StartAngle, scale.AngleOf(scale.Clamp(position)));
            }
        }

        var gap = r * 0.035f;
        var inner = r - track - (track > 0 ? gap : 0);

        // Range bands, just inside the track.
        var band = (float)Math.Max(0, owner.RangeThickness);
        if (owner.Ranges.Count > 0 && band > 0)
        {
            canvas.StrokeSize = band;
            canvas.StrokeLineCap = LineCap.Butt;
            foreach (var range in owner.Ranges)
            {
                var start = scale.Clamp(Math.Min(range.Start, range.End));
                var end = scale.Clamp(Math.Max(range.Start, range.End));
                if (end <= start)
                    continue;

                canvas.StrokeColor = range.Color;
                DrawArc(canvas, cx, cy, inner - band / 2, scale.AngleOf(start), scale.AngleOf(end));
            }
            inner -= band + gap;
        }

        // Ticks and their labels.
        var majorLength = r * 0.09f;
        var minorLength = r * 0.05f;
        var tickColor = owner.EffectiveTickColor;
        if (owner.ShowTicks)
        {
            canvas.StrokeColor = tickColor;
            canvas.StrokeLineCap = LineCap.Butt;
            foreach (var tick in owner.Ticks)
            {
                var angle = scale.AngleOf(tick.Value);
                var length = tick.IsMajor ? majorLength : minorLength;
                canvas.StrokeSize = Math.Max(1, r * (tick.IsMajor ? 0.018f : 0.009f));

                var (x1, y1) = GaugeScale.PointAt(cx, cy, inner, angle);
                var (x2, y2) = GaugeScale.PointAt(cx, cy, inner - length, angle);
                canvas.DrawLine((float)x1, (float)y1, (float)x2, (float)y2);
            }
        }

        if (owner.ShowTickLabels)
        {
            var fontSize = Math.Max(8, r * 0.11f);
            var labelRadius = inner - (owner.ShowTicks ? majorLength : 0) - fontSize * 0.95f;
            canvas.FontColor = tickColor;
            canvas.FontSize = fontSize;
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            foreach (var tick in owner.Ticks)
            {
                if (!tick.IsMajor)
                    continue;

                var (x, y) = GaugeScale.PointAt(cx, cy, labelRadius, scale.AngleOf(tick.Value));
                DrawCentred(canvas, owner.FormatTickLabel(tick.Value), (float)x, (float)y, fontSize);
            }
        }

        // Peak marker: a short bar across the track at the highest reading.
        if (owner.ShowPeak)
        {
            var angle = scale.AngleOf(scale.Clamp(owner.PeakValue));
            var (x1, y1) = GaugeScale.PointAt(cx, cy, r, angle);
            var (x2, y2) = GaugeScale.PointAt(cx, cy, r - Math.Max(track, r * 0.06f) - gap, angle);
            canvas.StrokeColor = owner.EffectivePeakColor;
            canvas.StrokeSize = Math.Max(2, r * 0.025f);
            canvas.StrokeLineCap = LineCap.Round;
            canvas.DrawLine((float)x1, (float)y1, (float)x2, (float)y2);
        }

        // Readout opposite the middle of the arc, title towards it.
        var textColor = owner.EffectiveTextColor;
        var readoutAngle = GaugeLayout.ReadoutAngle(scale.StartAngle, scale.SweepAngle);
        if (owner.ShowReadout)
        {
            var (x, y) = GaugeScale.PointAt(cx, cy, r * GaugeLayout.ReadoutDistance, readoutAngle);
            var size = r * 0.24f;
            canvas.FontColor = textColor;
            canvas.FontSize = size;
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            DrawCentred(canvas, owner.FormatReadout(position), (float)x, (float)y, size);

            if (!String.IsNullOrWhiteSpace(owner.Units))
            {
                var unitSize = Math.Max(8, r * 0.09f);
                canvas.FontSize = unitSize;
                canvas.Font = Microsoft.Maui.Graphics.Font.Default;
                DrawCentred(canvas, owner.Units!, (float)x, (float)y + size * 0.62f + unitSize * 0.5f, unitSize);
            }
        }

        if (!String.IsNullOrWhiteSpace(owner.Title))
        {
            var size = Math.Max(8, r * 0.1f);
            var (x, y) = GaugeScale.PointAt(cx, cy, r * 0.36, readoutAngle + 180);
            canvas.FontColor = textColor;
            canvas.FontSize = size;
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            DrawCentred(canvas, owner.Title!, (float)x, (float)y, size);
        }

        DrawNeedle(canvas, cx, cy, r, (float)scale.AngleOf(scale.Clamp(position)));
    }


    void DrawNeedle(ICanvas canvas, float cx, float cy, float r, float angle)
    {
        if (owner.NeedleStyle == GaugeNeedleStyle.None)
            return;

        var length = r * (float)Math.Clamp(owner.NeedleLength, 0.1, 1.1);
        var tail = r * 0.14f;
        var color = owner.EffectiveNeedleColor;

        canvas.SaveState();
        canvas.SetShadow(new SizeF(0, r * 0.01f), r * 0.03f, Colors.Black.WithAlpha(0.25f));

        if (owner.NeedleStyle == GaugeNeedleStyle.Line)
        {
            var (x1, y1) = GaugeScale.PointAt(cx, cy, tail, angle + 180);
            var (x2, y2) = GaugeScale.PointAt(cx, cy, length, angle);
            canvas.StrokeColor = color;
            canvas.StrokeSize = Math.Max(2, r * 0.022f);
            canvas.StrokeLineCap = LineCap.Round;
            canvas.DrawLine((float)x1, (float)y1, (float)x2, (float)y2);
        }
        else
        {
            var half = Math.Max(2, r * 0.035f);
            var path = new PathF();
            Move(path, GaugeScale.PointAt(cx, cy, length, angle));
            Line(path, GaugeScale.PointAt(cx, cy, half, angle + 90));
            Line(path, Offset(GaugeScale.PointAt(cx, cy, tail, angle + 180), angle + 90, half * 0.6f));
            Line(path, Offset(GaugeScale.PointAt(cx, cy, tail, angle + 180), angle - 90, half * 0.6f));
            Line(path, GaugeScale.PointAt(cx, cy, half, angle - 90));
            path.Close();

            canvas.FillColor = color;
            canvas.FillPath(path);
        }
        canvas.RestoreState();

        canvas.FillColor = owner.EffectiveHubColor;
        canvas.FillCircle(cx, cy, Math.Max(3, r * 0.07f));
        canvas.FillColor = color;
        canvas.FillCircle(cx, cy, Math.Max(1.5f, r * 0.03f));
    }


    /// <summary>A clockwise arc between two compass angles.</summary>
    static void DrawArc(ICanvas canvas, float cx, float cy, float radius, double from, double to)
    {
        if (radius <= 0 || to - from <= 0.01)
            return;

        // A full turn drawn as an arc collapses to nothing on some backends; a circle is the same pixels.
        if (to - from >= 359.99)
        {
            canvas.DrawCircle(cx, cy, radius);
            return;
        }

        // DrawArc wants counter-clockwise-from-3-o'clock degrees: 90 - compass.
        canvas.DrawArc(cx - radius, cy - radius, radius * 2, radius * 2, (float)(90 - from), (float)(90 - to), true, false);
    }


    static void DrawCentred(ICanvas canvas, string text, float x, float y, float fontSize)
    {
        var width = Math.Max(fontSize * 8, 40);
        var height = fontSize * 1.6f;
        canvas.DrawString(text, x - width / 2, y - height / 2, width, height, HorizontalAlignment.Center, VerticalAlignment.Center);
    }


    static (double X, double Y) Offset((double X, double Y) point, double angle, double distance)
    {
        var (dx, dy) = GaugeScale.PointAt(0, 0, distance, angle);
        return (point.X + dx, point.Y + dy);
    }


    static void Move(PathF path, (double X, double Y) point) => path.MoveTo((float)point.X, (float)point.Y);

    static void Line(PathF path, (double X, double Y) point) => path.LineTo((float)point.X, (float)point.Y);
}

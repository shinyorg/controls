using Shiny.Controls.Office.Shapes;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;
using SkiaSharp;

namespace Shiny.Controls.Office.Skia;

/// <summary>
/// Paints a <see cref="ShapeTextBody"/> — the text inside a slide shape, a Word drawing, or a
/// notebook container.
/// </summary>
/// <remarks>
/// Shared for the same reason the layout it draws is shared: a caret positioned by
/// <see cref="ShapeTextLayout"/> and glyphs placed by a second, separate painter would drift apart
/// the first time one of them changed, and the symptom of that is a caret that sits half a character
/// away from where typing appears.
/// </remarks>
public static class ShapeTextPainter
{
    /// <summary>Draws a text body into a rectangle, with its own bullets, highlights and decorations.</summary>
    /// <remarks>
    /// The paints are borrowed rather than owned, matching the convention in
    /// <see cref="ShapePainting.DrawShape"/>: a painter already keeps one fill and one stroke for the
    /// whole frame, and handing them in is what keeps this from allocating two per text box.
    /// </remarks>
    /// <param name="defaultInk">
    /// Substituted for any run left at the model's default black. Null leaves every colour alone.
    /// </param>
    public static void Draw(
        SKCanvas canvas,
        SKPaint fill,
        SKPaint stroke,
        SkiaTextMeasurer measurer,
        ShapeTextBody body,
        SKRect bounds,
        ArgbColor? defaultInk = null)
    {
        // Laid out by the shared kernel rather than here, so the editor's caret and this painter's
        // glyphs can never disagree about where a character sits.
        var layout = ShapeTextLayout.Layout(body, bounds.Width, bounds.Height, measurer);
        if (layout.Paragraphs.Count == 0)
            return;

        canvas.Save();
        canvas.ClipRect(bounds);

        foreach (var block in layout.Paragraphs)
        {
            var left = bounds.Left + (float)(layout.Left + block.Indent);
            var top = bounds.Top + (float)(layout.Top + block.Y);

            if (block.Bullet is { } bullet && block.Lines.Count > 0)
            {
                fill.Color = ToSk(Ink(block.BulletStyle.Color, defaultInk));
                fill.Shader = null;

                canvas.DrawText(
                    bullet,
                    left - (float)block.BulletAdvance,
                    top + (float)block.Lines[0].Ascent,
                    SKTextAlign.Left,
                    measurer.GetFont(block.BulletStyle),
                    fill);
            }

            foreach (var line in block.Lines)
            {
                var baseline = top + (float)(line.Y + line.Ascent);

                foreach (var run in line.Runs)
                {
                    if (run.Text.Length == 0)
                        continue;

                    fill.Shader = null;
                    var x = left + (float)run.X;
                    var glyphFont = measurer.GetFont(run.Style);

                    // Behind the glyphs, and sized from the font's own metrics rather than the line's:
                    // a line box is as tall as its tallest run, so measuring the band from it would
                    // give a small highlighted word a stripe the height of the heading beside it.
                    if (run.Style.Highlight is { } runHighlight)
                    {
                        fill.Color = ToSk(runHighlight);
                        var metrics = glyphFont.Metrics;
                        canvas.DrawRect(
                            new SKRect(x, baseline + metrics.Ascent, x + (float)run.Width, baseline + metrics.Descent),
                            fill);
                    }

                    fill.Color = ToSk(Ink(run.Style.Color, defaultInk));
                    canvas.DrawText(run.Text, x, baseline, SKTextAlign.Left, glyphFont, fill);

                    if (run.Style.Underline != UnderlineStyle.None)
                    {
                        stroke.Color = ToSk(Ink(run.Style.Color, defaultInk));
                        stroke.StrokeWidth = Math.Max(1, (float)(run.Style.FontSize / 14));
                        var offset = baseline + (float)(run.Style.FontSize * 0.12);
                        canvas.DrawLine(x, offset, x + (float)run.Width, offset, stroke);
                    }

                    if (run.Style.Strike)
                    {
                        stroke.Color = ToSk(Ink(run.Style.Color, defaultInk));
                        stroke.StrokeWidth = Math.Max(1, (float)(run.Style.FontSize / 14));
                        var middle = baseline - (float)(run.Style.FontSize * 0.28);
                        canvas.DrawLine(x, middle, x + (float)run.Width, middle, stroke);
                    }
                }
            }
        }

        canvas.Restore();
    }

    /// <summary>
    /// The colour to actually paint a run in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A run sitting on the model's default black is text <i>nobody chose a colour for</i> — black is
    /// where <see cref="TextStyle.Default"/> starts, not a decision. That is invisible on a surface
    /// whose ground follows a dark app theme, which is why the notebook passes its theme ink here and
    /// the document and deck, whose paper stays white, pass nothing.
    /// </para>
    /// <para>
    /// Only the exact default is substituted. A run the author actually coloured keeps that colour
    /// even when it reads poorly, because guessing at "close enough to black" would silently repaint
    /// deliberate near-black text — and a contrast rule wide enough to catch that would also catch the
    /// deliberately-subtle grey of a caption.
    /// </para>
    /// </remarks>
    static ArgbColor Ink(ArgbColor color, ArgbColor? defaultInk)
        => defaultInk is { } ink && color == TextStyle.Default.Color ? ink : color;

    static SKColor ToSk(ArgbColor color) => new(color.R, color.G, color.B, color.A);
}

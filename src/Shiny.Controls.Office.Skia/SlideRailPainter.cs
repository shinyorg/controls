using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Spreadsheet;
using SkiaSharp;

namespace Shiny.Controls.Office.Skia;

/// <summary>Colours for the slide rail — chrome, so it follows the app rather than the deck.</summary>
public sealed record SlideRailTheme
{
    public static readonly SlideRailTheme Light = new();

    public static readonly SlideRailTheme Dark = new()
    {
        Background = new(255, 0x1F, 0x21, 0x26),
        Number = new(255, 0xB8, 0xBE, 0xC8),
        Border = new(255, 0x4A, 0x4F, 0x58)
    };

    public ArgbColor Background { get; init; } = new(255, 0xF3, 0xF4, 0xF6);
    public ArgbColor Number { get; init; } = new(255, 0x5B, 0x63, 0x70);
    public ArgbColor Border { get; init; } = new(255, 0xC9, 0xCE, 0xD6);
    public ArgbColor Accent { get; init; } = new(255, 0xC4, 0x3E, 0x1C);
}

/// <summary>
/// Paints the slide rail: numbered thumbnails, the current one outlined, and the insertion line while a
/// slide is dragged.
/// </summary>
/// <remarks>
/// Each thumbnail is the real slide through <see cref="SlidePainter"/> — no cached bitmaps to go stale
/// after an edit — and only the visible ones are painted.
/// </remarks>
public sealed class SlideRailPainter(SlidePainter slides) : IDisposable
{
    readonly SKPaint fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    readonly SKPaint stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
    readonly SKFont font = new(SKTypeface.Default, 11);

    public void Paint(SKCanvas canvas, SlideRailController rail, SlideRailTheme theme, SlideTheme slideTheme, float scale)
    {
        canvas.Save();
        canvas.Scale(scale);
        canvas.Clear(ToSk(theme.Background));

        foreach (var item in rail.VisibleItems())
        {
            var dimmed = item.Index == rail.DraggedIndex;

            this.fill.Color = ToSk(item.IsSelected ? theme.Accent : theme.Number);
            var label = (item.Index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var width = this.font.MeasureText(label);
            canvas.DrawText(label, (float)(item.X - 6 - width), (float)(item.Y + 13), this.font, this.fill);

            canvas.Save();
            if (dimmed)
                canvas.SaveLayer(new SKPaint { Color = SKColors.White.WithAlpha(110) });

            // SlidePainter applies its own scale; the rail's is already on the canvas.
            slides.Paint(canvas, new SlidePaintRequest
            {
                Slide = item.Slide,
                SlideWidth = rail.Editor.Deck.SlideWidth,
                SlideHeight = rail.Editor.Deck.SlideHeight,
                DestinationX = item.X,
                DestinationY = item.Y,
                DestinationWidth = item.Width,
                DestinationHeight = item.Height,
                Theme = slideTheme,
                DrawBorder = false
            });

            if (dimmed)
                canvas.Restore();

            canvas.Restore();

            var frame = new SKRect((float)item.X, (float)item.Y, (float)(item.X + item.Width), (float)(item.Y + item.Height));
            this.stroke.Color = ToSk(item.IsSelected ? theme.Accent : theme.Border);
            this.stroke.StrokeWidth = item.IsSelected ? 2.5f : 1f;

            if (item.IsSelected)
                frame.Inflate(1.5f, 1.5f);

            canvas.DrawRect(frame, this.stroke);
        }

        if (rail.IsDropMeaningful && rail.DropIndicatorY is { } y)
        {
            this.stroke.Color = ToSk(theme.Accent);
            this.stroke.StrokeWidth = 3;
            var left = (float)(rail.Gap + rail.NumberWidth - 4);
            var right = (float)(rail.Gap + rail.NumberWidth + rail.ThumbnailWidth + 4);
            canvas.DrawLine(left, (float)y, right, (float)y, this.stroke);
        }

        canvas.Restore();
    }

    public void Dispose()
    {
        this.fill.Dispose();
        this.stroke.Dispose();
        this.font.Dispose();
    }

    static SKColor ToSk(ArgbColor color) => new(color.R, color.G, color.B, color.A);
}

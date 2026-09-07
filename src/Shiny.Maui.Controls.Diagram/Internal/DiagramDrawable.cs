using Shiny.Controls.Diagramming;

namespace Shiny.Maui.Controls.Diagram.Internal;

/// <summary>
/// Paints the whole diagram: the grid, the connections and their caps and labels, the nodes and their
/// labels, and the selection adorners.
/// </summary>
/// <remarks>
/// <para>
/// One drawable rather than one view per node, for the reason a virtualized list exists: a two
/// hundred node diagram is two hundred native views, each with a handler, a layout pass and a
/// measure - on a phone that is the difference between a diagram that pans at sixty frames and one
/// that stutters. Nodes only become real views when a <c>NodeTemplate</c> asks for it.
/// </para>
/// <para>
/// Nothing here computes geometry. Node boxes, connection routes, cap directions and label positions
/// all come from <c>Shiny.Controls.Diagram.Shared</c>, the same values the Blazor control draws from,
/// so the two hosts cannot drift.
/// </para>
/// </remarks>
sealed class DiagramDrawable(DiagramView view) : IDrawable
{
    /// <summary>How long an arrowhead is.</summary>
    const float CapLength = 10;

    /// <summary>How wide an arrowhead is at its base.</summary>
    const float CapWidth = 7;

    /// <inheritdoc />
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var model = view.Model;
        var palette = view.Palette;

        canvas.FillColor = palette.Surface;
        canvas.FillRectangle(dirtyRect);

        canvas.SaveState();

        // One transform for the whole scene, so everything below works in diagram space and the pan
        // and zoom exist in exactly one place.
        canvas.Translate((float)-view.PanX * (float)view.Zoom, (float)-view.PanY * (float)view.Zoom);
        canvas.Scale((float)view.Zoom, (float)view.Zoom);

        if (view.ShowGrid)
            this.DrawGrid(canvas, dirtyRect);

        foreach (var connection in model.VisibleConnections)
            this.DrawConnection(canvas, connection);

        foreach (var node in model.VisibleNodes)
        {
            // A templated node is a real view in the overlay layer; drawing a shape under it would
            // show through wherever the template is transparent.
            if (view.NodeTemplate is null)
                this.DrawNode(canvas, node);

            if (node.IsSelected)
                this.DrawSelection(canvas, node);
        }

        this.DrawAdorners(canvas);

        canvas.RestoreState();
    }

    void DrawGrid(ICanvas canvas, RectF dirtyRect)
    {
        var step = (float)view.GridSize;

        if (step <= 1)
            return;

        var zoom = (float)view.Zoom;

        // Below about a third of a pixel the grid is a solid wash rather than a grid, and drawing
        // thousands of invisible lines to produce it is the most expensive thing on the frame.
        if (step * zoom < 4)
            return;

        canvas.StrokeColor = view.Palette.OutlineVariant;
        canvas.StrokeSize = 1 / zoom;

        var left = (float)view.PanX;
        var top = (float)view.PanY;
        var right = left + (dirtyRect.Width / zoom);
        var bottom = top + (dirtyRect.Height / zoom);

        var startX = MathF.Floor(left / step) * step;
        var startY = MathF.Floor(top / step) * step;

        for (var x = startX; x <= right; x += step)
            canvas.DrawLine(x, top, x, bottom);

        for (var y = startY; y <= bottom; y += step)
            canvas.DrawLine(left, y, right, y);
    }

    void DrawConnection(ICanvas canvas, DiagramConnection connection)
    {
        var points = connection.Points;

        if (points.Count < 2)
            return;

        var router = connection.Router ?? view.Router;
        var selected = connection.IsSelected;

        var color = selected
            ? view.Palette.Primary
            : DiagramPalette.Parse(connection.Stroke, view.Palette.Outline);

        canvas.StrokeColor = color;
        canvas.StrokeSize = (float)(connection.StrokeThickness ?? (selected ? 2.5 : 1.5));
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.StrokeLineCap = LineCap.Round;

        canvas.StrokeDashPattern = connection.StrokeStyle switch
        {
            DiagramConnectionStroke.Dashed => [6, 4],
            DiagramConnectionStroke.Dotted => [1, 3],
            _ => null
        };

        var path = new PathF();
        path.MoveTo((float)points[0].X, (float)points[0].Y);

        if (router == DiagramConnectionRouter.Bezier && points.Count == 4)
        {
            path.CurveTo(
                (float)points[1].X, (float)points[1].Y,
                (float)points[2].X, (float)points[2].Y,
                (float)points[3].X, (float)points[3].Y
            );
        }
        else
        {
            for (var i = 1; i < points.Count; i++)
                path.LineTo((float)points[i].X, (float)points[i].Y);
        }

        canvas.DrawPath(path);

        // Caps and labels are solid whatever the line is; a dashed arrowhead reads as a rendering
        // fault rather than as a style.
        canvas.StrokeDashPattern = null;

        if (connection.EndCap != DiagramConnectionCap.None)
        {
            var direction = ConnectionRouter.CapDirection(points, router, atEnd: true);
            DrawCap(canvas, connection.EndCap, points[^1], direction, color);
        }

        if (connection.StartCap != DiagramConnectionCap.None)
        {
            var direction = ConnectionRouter.CapDirection(points, router, atEnd: false);
            DrawCap(canvas, connection.StartCap, points[0], direction, color);
        }

        if (!string.IsNullOrWhiteSpace(connection.Text))
            this.DrawConnectionLabel(canvas, connection);
    }

    void DrawConnectionLabel(ICanvas canvas, DiagramConnection connection)
    {
        var position = connection.LabelPosition;
        var fontSize = (float)view.ConnectionFontSize;
        var text = connection.Text!;

        // A pad of surface behind the text, so a label sitting on its own line stays readable. The
        // width is the same crude estimate the layout sizes labels with, which is what keeps the two
        // agreeing about how wide the text is.
        var width = (text.Length * fontSize * 0.6f) + 6;
        var height = fontSize + 4;

        canvas.FillColor = view.Palette.Surface;
        canvas.FillRectangle(
            (float)position.X - (width / 2),
            (float)position.Y - (height / 2),
            width,
            height
        );

        canvas.FontColor = view.Palette.OnSurfaceVariant;
        canvas.FontSize = fontSize;

        canvas.DrawString(
            text,
            (float)position.X - (width / 2),
            (float)position.Y - (height / 2),
            width,
            height,
            HorizontalAlignment.Center,
            VerticalAlignment.Center
        );
    }

    static void DrawCap(ICanvas canvas, DiagramConnectionCap cap, DiagramPoint tip, DiagramPoint direction, Color color)
    {
        var tx = (float)tip.X;
        var ty = (float)tip.Y;
        var dx = (float)direction.X;
        var dy = (float)direction.Y;

        // The perpendicular, for the two base corners.
        var px = -dy;
        var py = dx;

        var baseX = tx - (dx * CapLength);
        var baseY = ty - (dy * CapLength);

        canvas.FillColor = color;
        canvas.StrokeColor = color;

        switch (cap)
        {
            case DiagramConnectionCap.Arrow:
            {
                canvas.DrawLine(baseX + (px * CapWidth / 2), baseY + (py * CapWidth / 2), tx, ty);
                canvas.DrawLine(baseX - (px * CapWidth / 2), baseY - (py * CapWidth / 2), tx, ty);
                break;
            }

            case DiagramConnectionCap.FilledArrow:
            {
                var path = new PathF();
                path.MoveTo(tx, ty);
                path.LineTo(baseX + (px * CapWidth / 2), baseY + (py * CapWidth / 2));
                path.LineTo(baseX - (px * CapWidth / 2), baseY - (py * CapWidth / 2));
                path.Close();
                canvas.FillPath(path);
                break;
            }

            case DiagramConnectionCap.Circle:
            {
                // Sat back along the line by its own radius, so the dot ends where the line ends
                // rather than half of it hanging inside the node.
                var radius = CapWidth / 2;
                canvas.FillCircle(tx - (dx * radius), ty - (dy * radius), radius);
                break;
            }

            case DiagramConnectionCap.Diamond:
            {
                var path = new PathF();
                path.MoveTo(tx, ty);
                path.LineTo(baseX + (px * CapWidth / 2), baseY + (py * CapWidth / 2));
                path.LineTo(tx - (dx * CapLength * 2), ty - (dy * CapLength * 2));
                path.LineTo(baseX - (px * CapWidth / 2), baseY - (py * CapWidth / 2));
                path.Close();
                canvas.FillPath(path);
                break;
            }
        }
    }

    void DrawNode(ICanvas canvas, DiagramNode node)
    {
        var bounds = node.Bounds;
        var fill = DiagramPalette.Parse(node.Fill, view.Palette.SurfaceContainer);
        var stroke = node.IsSelected
            ? view.Palette.Primary
            : DiagramPalette.Parse(node.Stroke, view.Palette.Outline);

        canvas.FillColor = fill;
        canvas.StrokeColor = stroke;
        canvas.StrokeSize = (float)(node.StrokeThickness ?? (node.IsSelected ? 2.5 : 1.5));
        canvas.StrokeDashPattern = null;

        ShapeRenderer.Draw(canvas, node.Shape, bounds, node.CornerRadius ?? -1);

        if (string.IsNullOrWhiteSpace(node.Text))
            return;

        canvas.FontColor = DiagramPalette.Parse(node.TextColor, view.Palette.OnSurface);
        canvas.FontSize = (float)(node.FontSize ?? view.NodeFontSize);

        // Inset so a label cannot sit on the outline. A diamond needs more, because its usable width
        // at the vertical centre is the full box but tapers to nothing at the points.
        var inset = node.Shape == DiagramNodeShape.Diamond ? 0.25f : 0.06f;
        var padX = (float)bounds.Width * inset;
        var padY = (float)bounds.Height * 0.08f;

        canvas.DrawString(
            node.Text,
            (float)bounds.X + padX,
            (float)bounds.Y + padY,
            (float)bounds.Width - (padX * 2),
            (float)bounds.Height - (padY * 2),
            HorizontalAlignment.Center,
            VerticalAlignment.Center,
            TextFlow.ClipBounds
        );
    }

    void DrawSelection(ICanvas canvas, DiagramNode node)
    {
        if (!view.AllowConnectionEdit || !node.CanConnect)
            return;

        var radius = 4 / (float)view.Zoom;

        canvas.FillColor = view.Palette.Surface;
        canvas.StrokeColor = view.Palette.Primary;
        canvas.StrokeSize = 1.5f / (float)view.Zoom;
        canvas.StrokeDashPattern = null;

        foreach (var port in DiagramHitTester.Ports)
        {
            var point = ShapeGeometry.PortPoint(node.Shape, node.Bounds, port, node.CornerRadius ?? -1);
            canvas.FillCircle((float)point.X, (float)point.Y, radius);
            canvas.DrawCircle((float)point.X, (float)point.Y, radius);
        }
    }

    void DrawAdorners(ICanvas canvas)
    {
        var zoom = (float)view.Zoom;

        if (view.PendingLink is { } link)
        {
            canvas.StrokeColor = view.Palette.Primary;
            canvas.StrokeSize = 2 / zoom;
            canvas.StrokeDashPattern = [5, 4];
            canvas.DrawLine((float)link.From.X, (float)link.From.Y, (float)link.To.X, (float)link.To.Y);
            canvas.StrokeDashPattern = null;
        }

        if (view.Marquee is { } area)
        {
            canvas.FillColor = view.Palette.Primary.WithAlpha(0.1f);
            canvas.FillRectangle((float)area.X, (float)area.Y, (float)area.Width, (float)area.Height);

            canvas.StrokeColor = view.Palette.Primary;
            canvas.StrokeSize = 1 / zoom;
            canvas.StrokeDashPattern = [4, 3];
            canvas.DrawRectangle((float)area.X, (float)area.Y, (float)area.Width, (float)area.Height);
            canvas.StrokeDashPattern = null;
        }
    }
}

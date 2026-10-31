namespace Shiny.Maui.Controls;

/// <summary>
/// Which part of a scratch card's cover has been scratched away, kept as a grid of small cells.
/// </summary>
/// <remarks>
/// The grid answers both questions the card asks: how much is gone (a plain count) and what is
/// left to draw. The second answer is a set of disjoint rectangles, because the cover is clipped
/// with MAUI's cross-platform <see cref="VisualElement.Clip"/> and a geometry made of overlapping
/// scratch circles cannot be expressed with either fill rule - every circle that overlaps another
/// would punch the cover back in. Disjoint rectangles fill the same under both rules.
/// </remarks>
sealed class ScratchMask
{
    bool[] scratched = [];
    int scratchedCount;

    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public double CellSize { get; private set; }
    public double Width { get; private set; }
    public double Height { get; private set; }

    public bool IsEmpty => this.Columns == 0 || this.Rows == 0;

    /// <summary>The share of the cover scratched away, 0 to 1.</summary>
    public double ScratchedFraction => this.IsEmpty ? 0 : (double)this.scratchedCount / this.scratched.Length;


    /// <summary>
    /// Sizes the grid to the card. Anything already scratched is carried across by position, so a
    /// rotation or a window resize does not hand the user a fresh cover.
    /// </summary>
    public void Resize(double width, double height, double cellSize)
    {
        if (width <= 0 || height <= 0 || cellSize <= 0)
        {
            this.Width = this.Height = 0;
            this.Columns = this.Rows = 0;
            this.scratched = [];
            this.scratchedCount = 0;
            return;
        }

        var columns = Math.Max(1, (int)Math.Ceiling(width / cellSize));
        var rows = Math.Max(1, (int)Math.Ceiling(height / cellSize));
        if (columns == this.Columns && rows == this.Rows && Math.Abs(this.CellSize - cellSize) < 0.0001)
        {
            this.Width = width;
            this.Height = height;
            return;
        }

        var next = new bool[columns * rows];
        var count = 0;
        if (this.scratchedCount > 0)
        {
            for (var r = 0; r < rows; r++)
            {
                var oldRow = Math.Min(this.Rows - 1, (int)((r + 0.5) / rows * this.Rows));
                for (var c = 0; c < columns; c++)
                {
                    var oldColumn = Math.Min(this.Columns - 1, (int)((c + 0.5) / columns * this.Columns));
                    if (this.scratched[(oldRow * this.Columns) + oldColumn])
                    {
                        next[(r * columns) + c] = true;
                        count++;
                    }
                }
            }
        }

        this.Width = width;
        this.Height = height;
        this.CellSize = cellSize;
        this.Columns = columns;
        this.Rows = rows;
        this.scratched = next;
        this.scratchedCount = count;
    }


    /// <summary>Puts the whole cover back.</summary>
    public void Clear()
    {
        Array.Clear(this.scratched);
        this.scratchedCount = 0;
    }


    /// <summary>Scratches every cell whose centre lies within <paramref name="radius"/> of the segment.</summary>
    /// <returns>Whether anything new was scratched.</returns>
    public bool Scratch(Point from, Point to, double radius)
    {
        if (this.IsEmpty || radius <= 0)
            return false;

        var size = this.CellSize;
        var minC = Clamp((int)Math.Floor((Math.Min(from.X, to.X) - radius) / size), this.Columns);
        var maxC = Clamp((int)Math.Floor((Math.Max(from.X, to.X) + radius) / size), this.Columns);
        var minR = Clamp((int)Math.Floor((Math.Min(from.Y, to.Y) - radius) / size), this.Rows);
        var maxR = Clamp((int)Math.Floor((Math.Max(from.Y, to.Y) + radius) / size), this.Rows);

        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        var radiusSquared = radius * radius;
        var changed = false;

        for (var r = minR; r <= maxR; r++)
        {
            var cy = (r + 0.5) * size;
            for (var c = minC; c <= maxC; c++)
            {
                var index = (r * this.Columns) + c;
                if (this.scratched[index])
                    continue;

                var cx = (c + 0.5) * size;
                var t = lengthSquared <= 0 ? 0 : Math.Clamp((((cx - from.X) * dx) + ((cy - from.Y) * dy)) / lengthSquared, 0, 1);
                var px = from.X + (t * dx) - cx;
                var py = from.Y + (t * dy) - cy;
                if ((px * px) + (py * py) > radiusSquared)
                    continue;

                this.scratched[index] = true;
                this.scratchedCount++;
                changed = true;
            }
        }

        return changed;
    }


    public bool IsScratched(int column, int row) => this.scratched[(row * this.Columns) + column];


    /// <summary>
    /// What is left of the cover, as disjoint rectangles in card coordinates. Runs of untouched
    /// cells along each row are merged with an identical run directly above, so an unscratched
    /// cover is one rectangle and a single stroke costs only a handful.
    /// </summary>
    public List<Rect> RemainingRects()
    {
        var result = new List<Rect>();
        if (this.IsEmpty)
            return result;

        // Rectangles still growing downward, keyed by their column span.
        var open = new Dictionary<(int Start, int End), int>();

        for (var r = 0; r <= this.Rows; r++)
        {
            var runs = new HashSet<(int, int)>();
            if (r < this.Rows)
            {
                var c = 0;
                while (c < this.Columns)
                {
                    if (this.scratched[(r * this.Columns) + c]) { c++; continue; }
                    var start = c;
                    while (c < this.Columns && !this.scratched[(r * this.Columns) + c])
                        c++;
                    runs.Add((start, c));
                }
            }

            // Close every open rectangle whose span did not continue into this row.
            foreach (var span in open.Keys.Where(k => !runs.Contains(k)).ToList())
            {
                result.Add(this.ToRect(span.Start, span.End, open[span], r));
                open.Remove(span);
            }

            foreach (var span in runs)
                open.TryAdd(span, r);
        }

        return result;
    }


    Rect ToRect(int startColumn, int endColumn, int topRow, int bottomRow)
    {
        var size = this.CellSize;
        var x = startColumn * size;
        var y = topRow * size;
        // The last row and column overhang the card; clamp so the clip never reaches past it.
        return new Rect(x, y, Math.Min(this.Width, endColumn * size) - x, Math.Min(this.Height, bottomRow * size) - y);
    }


    static int Clamp(int value, int count) => Math.Clamp(value, 0, count - 1);
}

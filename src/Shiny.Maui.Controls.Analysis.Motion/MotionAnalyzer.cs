using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Shiny.Maui.Controls.Analysis.Motion;

/// <summary>
/// Detects motion by comparing successive frames' luminance. Fully managed and cross-platform — works on
/// every target including those without native ML. Raises <see cref="MotionChanged"/> when motion starts or
/// stops, and draws a box around each distinct region where motion is happening (so movement in two separate
/// spots yields two boxes, not one spanning both). Boxes are cleared when motion stops, and suppressed
/// entirely when <see cref="FrameAnalyzer.ShowBoundingBox"/> is <c>false</c>.
/// </summary>
public class MotionAnalyzer : FrameAnalyzer
{
    byte[]? previous;
    int prevWidth;
    int prevHeight;

    /// <inheritdoc/>
    public override string Id => "shiny.camera.motion";

    /// <summary>Per-pixel luminance delta (0–255) that counts a pixel as changed. Default 25.</summary>
    public int PixelThreshold { get; set; } = 25;

    /// <summary>Fraction of sampled pixels that must change to report motion (0–1). Default 0.04.</summary>
    public double AreaThreshold { get; set; } = 0.04;

    /// <summary>Sampling stride — every Nth pixel on each axis is compared (higher = faster). Default 4.</summary>
    public int SampleStride { get; set; } = 4;

    /// <summary>
    /// Horizontal resolution of the grid used to localize and cluster motion into separate boxes; rows are
    /// derived from this and the frame aspect. Higher = finer (more, smaller boxes). Default 16.
    /// </summary>
    public int GridColumns { get; set; } = 16;

    /// <summary>
    /// Fraction of a single grid cell's sampled pixels that must change for that cell to be part of a motion
    /// region (0–1). Higher = only stronger localized motion is boxed. Default 0.10.
    /// </summary>
    public double CellThreshold { get; set; } = 0.10;

    /// <summary>
    /// Consecutive frames above <see cref="AreaThreshold"/> required before reporting motion <i>started</i>.
    /// Debounces the event so a single noisy frame doesn't fire it. Default 3 (raise it if motion is still too
    /// twitchy). The overlay boxes are not debounced — they track each frame so they stay responsive.
    /// </summary>
    public int EnterFrames { get; set; } = 3;

    /// <summary>
    /// Consecutive frames below <see cref="AreaThreshold"/> required before reporting motion <i>stopped</i>.
    /// Larger than <see cref="EnterFrames"/> by default so brief pauses mid-movement don't end the event. Default 5.
    /// </summary>
    public int ExitFrames { get; set; } = 5;

    /// <summary>Box outline + caption color. Default an amber accent.</summary>
    public Color BoxColor { get; set; } = Color.FromArgb("#F59E0B");

    /// <summary>Caption drawn on the motion box (null/empty for none). Default "Motion".</summary>
    public string? Label { get; set; } = "Motion";

    /// <summary>Command invoked (with the <see cref="MotionEventArgs"/>) when motion starts or stops.</summary>
    public static readonly BindableProperty MotionChangedCommandProperty = BindableProperty.Create(
        nameof(MotionChangedCommand), typeof(ICommand), typeof(MotionAnalyzer));

    /// <inheritdoc cref="MotionChangedCommandProperty"/>
    public ICommand? MotionChangedCommand
    {
        get => (ICommand?)this.GetValue(MotionChangedCommandProperty);
        set => this.SetValue(MotionChangedCommandProperty, value);
    }

    /// <summary>
    /// Optional selector deciding the boxes to draw while in motion; return <c>null</c> for no overlay. When
    /// unset the analyzer draws a single <see cref="BoxColor"/> box over the changed region.
    /// </summary>
    public Func<MotionEventArgs, IReadOnlyList<OverlayBox>?>? OverlayProvider { get; set; }

    /// <summary>
    /// Continuation invoked (on the UI thread) on each motion start/stop while the analyzer is armed; inspect
    /// <see cref="MotionEventArgs.InMotion"/> to tell them apart. Return <c>true</c> to keep watching (stay
    /// armed), <c>false</c> to stop until the next <see cref="CameraView.Scan"/>. When unset, delivery is
    /// single-shot (the first motion start). Bindable so it can target a VM method in XAML.
    /// </summary>
    public static readonly BindableProperty OnDetectedProperty = BindableProperty.Create(
        nameof(OnDetected), typeof(Func<MotionEventArgs, Task<bool>>), typeof(MotionAnalyzer));

    /// <inheritdoc cref="OnDetectedProperty"/>
    public Func<MotionEventArgs, Task<bool>>? OnDetected
    {
        get => (Func<MotionEventArgs, Task<bool>>?)this.GetValue(OnDetectedProperty);
        set => this.SetValue(OnDetectedProperty, value);
    }

    /// <summary>Raised on the UI thread when motion starts or stops, while the analyzer is armed.</summary>
    public event EventHandler<MotionEventArgs>? MotionChanged;

    bool lastMotion;
    int aboveCount;
    int belowCount;

    /// <inheritdoc/>
    public override ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeAsync(VideoFrame frame, CancellationToken ct)
    {
        var lum = frame.GetLuminance();
        int w = frame.Width, h = frame.Height;
        var stride = Math.Max(1, this.SampleStride);

        if (this.previous == null || this.prevWidth != w || this.prevHeight != h || this.previous.Length != lum.Length)
        {
            this.previous = lum.ToArray();
            this.prevWidth = w;
            this.prevHeight = h;
            return default;
        }

        // grid resolution, capped so a cell never spans fewer than one sampled pixel (keeps it sane on tiny frames)
        int sampledCols = Math.Max(1, (w + stride - 1) / stride);
        int sampledRows = Math.Max(1, (h + stride - 1) / stride);
        int cols = Math.Clamp(this.GridColumns, 1, sampledCols);
        int rows = Math.Clamp((int)Math.Round(cols * (double)h / Math.Max(1, w)), 1, sampledRows);
        var cellChanged = new int[cols * rows];
        var cellTotal = new int[cols * rows];

        int changed = 0, total = 0, minX = w, minY = h, maxX = 0, maxY = 0;
        var prev = this.previous;
        for (var y = 0; y < h; y += stride)
        {
            var rowBase = y * w;
            var cellRowBase = Math.Min(rows - 1, y * rows / h) * cols;
            for (var x = 0; x < w; x += stride)
            {
                var i = rowBase + x;
                total++;
                var ci = cellRowBase + Math.Min(cols - 1, x * cols / w);
                cellTotal[ci]++;
                if (Math.Abs(lum[i] - prev[i]) <= this.PixelThreshold)
                    continue;

                changed++;
                cellChanged[ci]++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        // refresh the reference frame
        lum.CopyTo(this.previous);

        var ratio = total == 0 ? 0d : (double)changed / total;
        var motion = ratio >= this.AreaThreshold;

        var regions = new List<RectF>();
        if (motion)
        {
            // mark cells with enough localized change, then group adjacent ones into separate regions
            var active = new bool[cellTotal.Length];
            for (var ci = 0; ci < active.Length; ci++)
                active[ci] = cellTotal[ci] > 0 && (double)cellChanged[ci] / cellTotal[ci] >= this.CellThreshold;

            ClusterRegions(active, cols, rows, frame.Rotation, frame.IsMirrored, regions);

            // diffuse change that tripped the global threshold but never concentrated in a cell -> one box
            if (regions.Count == 0 && maxX >= minX)
            {
                var raw = new RectF((float)minX / w, (float)minY / h, (float)(maxX - minX) / w, (float)(maxY - minY) / h);
                regions.Add(CoordinateTransform.ApplyOrientation(raw, frame.Rotation, frame.IsMirrored));
            }
        }

        // debounce: require sustained change before flipping the reported state so a single noisy frame (or a
        // brief pause mid-movement) doesn't fire the event
        if (motion)
        {
            this.aboveCount++;
            this.belowCount = 0;
        }
        else
        {
            this.belowCount++;
            this.aboveCount = 0;
        }

        RectF? union = regions.Count == 0 ? null : Union(regions);
        var args = new MotionEventArgs(motion, union, (float)Math.Min(1d, ratio), regions);

        if (!this.lastMotion && this.aboveCount >= Math.Max(1, this.EnterFrames))
        {
            this.lastMotion = true;
            // motion started — delivered only while armed
            this.Deliver(args, () => this.MotionChanged?.Invoke(this, args), this.MotionChangedCommand, this.OnDetected);
        }
        else if (this.lastMotion && this.belowCount >= Math.Max(1, this.ExitFrames))
        {
            this.lastMotion = false;
            this.Deliver(args, () => this.MotionChanged?.Invoke(this, args), this.MotionChangedCommand, this.OnDetected);
        }

        if (regions.Count == 0)
            return default; // no motion -> clear the boxes

        var boxes = this.ResolveOverlay(args, this.OverlayProvider, () =>
        {
            var list = new OverlayBox[regions.Count];
            for (var i = 0; i < regions.Count; i++)
                // caption only the first box so multiple regions don't clutter the overlay
                list[i] = new OverlayBox(regions[i], this.BoxColor, i == 0 ? this.Label : null, this.BoxColor);
            return list;
        });
        return new ValueTask<IReadOnlyList<OverlayBox>?>(boxes);
    }


    // 8-connected components over the active-cell grid; appends one oriented, normalized RectF per cluster.
    static void ClusterRegions(bool[] active, int cols, int rows, int rotation, bool mirrored, List<RectF> regions)
    {
        var visited = new bool[active.Length];
        var stack = new Stack<int>();

        for (var start = 0; start < active.Length; start++)
        {
            if (!active[start] || visited[start])
                continue;

            int minCx = cols, minCy = rows, maxCx = -1, maxCy = -1;
            stack.Push(start);
            visited[start] = true;

            while (stack.Count > 0)
            {
                var ci = stack.Pop();
                int cx = ci % cols, cy = ci / cols;
                if (cx < minCx) minCx = cx;
                if (cx > maxCx) maxCx = cx;
                if (cy < minCy) minCy = cy;
                if (cy > maxCy) maxCy = cy;

                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= cols || ny >= rows)
                            continue;
                        int ni = ny * cols + nx;
                        if (active[ni] && !visited[ni])
                        {
                            visited[ni] = true;
                            stack.Push(ni);
                        }
                    }
                }
            }

            var raw = new RectF(
                (float)minCx / cols, (float)minCy / rows,
                (float)(maxCx - minCx + 1) / cols, (float)(maxCy - minCy + 1) / rows);
            regions.Add(CoordinateTransform.ApplyOrientation(raw, rotation, mirrored));
        }
    }


    static RectF Union(IReadOnlyList<RectF> rects)
    {
        float l = float.MaxValue, t = float.MaxValue, r = float.MinValue, b = float.MinValue;
        foreach (var rect in rects)
        {
            if (rect.Left < l) l = rect.Left;
            if (rect.Top < t) t = rect.Top;
            if (rect.Right > r) r = rect.Right;
            if (rect.Bottom > b) b = rect.Bottom;
        }
        return new RectF(l, t, r - l, b - t);
    }
}

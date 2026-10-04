using Shiny.Maui.Controls.MotionIcons;

namespace Shiny.Maui.Controls;

/// <summary>
/// The full-page surface confetti is drawn on. One per page, in that page's confetti layer; it is
/// transparent and input-transparent, and its frame timer only runs while something is in the air.
/// </summary>
sealed class ConfettiCanvas : GraphicsView, IDrawable
{
    /// <summary>canvas-confetti's physics are per frame at 60fps; real time is converted to frames here.</summary>
    const double FrameMilliseconds = 1000.0 / 60;

    /// <summary>
    /// The most frames simulated in one tick. A stall (a GC, a navigation) otherwise fast-forwards
    /// the whole burst out of existence in a single redraw.
    /// </summary>
    const int MaxStepsPerTick = 4;

    readonly ConfettiSimulation simulation;
    readonly List<(TimeSpan Due, ConfettiOptions Options, ConfettiRun Run)> pending = [];
    MotionTicker? ticker;
    TimeSpan clock;
    double accumulator;

    public ConfettiCanvas() : this(Random.Shared) { }

    internal ConfettiCanvas(Random random)
    {
        this.simulation = new ConfettiSimulation(random);
        this.Drawable = this;
        this.InputTransparent = true;
        this.BackgroundColor = Colors.Transparent;
        this.HorizontalOptions = LayoutOptions.Fill;
        this.VerticalOptions = LayoutOptions.Fill;
    }

    internal ConfettiSimulation Simulation => this.simulation;

    internal bool IsRunning => this.ticker is not null;


    public Task Fire(IReadOnlyList<ConfettiShot> shots)
    {
        if (shots.Count == 0)
            return Task.CompletedTask;

        var run = new ConfettiRun(shots.Count);
        foreach (var shot in shots)
            this.pending.Add((this.clock + shot.Delay, shot.Options, run));

        // Immediate bursts launch now, so the very next frame already has them in the air.
        this.LaunchDue();
        this.Start();
        return run.Completion;
    }


    public void Clear()
    {
        foreach (var item in this.pending)
            item.Run.Cancel();

        this.pending.Clear();
        this.simulation.Clear();
        this.Stop();
        this.Invalidate();
    }


    /// <summary>Test seam and the ticker's callback: advance the burst by real time.</summary>
    internal void Advance(TimeSpan delta)
    {
        this.clock += delta;
        this.accumulator += delta.TotalMilliseconds;
        this.LaunchDue();

        var steps = 0;
        while (this.accumulator >= FrameMilliseconds && steps < MaxStepsPerTick)
        {
            this.simulation.Step();
            this.accumulator -= FrameMilliseconds;
            steps++;
        }
        if (steps == MaxStepsPerTick)
            this.accumulator = 0;

        this.Invalidate();

        if (this.simulation.IsEmpty && this.pending.Count == 0)
            this.Stop();
    }


    void LaunchDue()
    {
        // Not laid out yet (the layer was created this very frame): origins are fractions of the
        // page, and a fraction of zero would launch everything from the top-left corner. Wait.
        if (this.Width <= 0 || this.Height <= 0)
            return;

        for (var i = 0; i < this.pending.Count; i++)
        {
            var (due, options, run) = this.pending[i];
            if (due > this.clock)
                continue;

            this.pending.RemoveAt(i--);
            var launched = this.simulation.Emit(
                options,
                options.OriginX * this.Width,
                options.OriginY * this.Height,
                run
            );
            run.ShotLaunched(launched);
        }
    }


    void Start()
    {
        if (this.ticker is not null)
            return;

        this.ticker = MotionTicker.For(this);
        if (this.ticker is not null)
            this.ticker.Tick += this.Advance;
    }


    void Stop()
    {
        if (this.ticker is null)
            return;

        this.ticker.Tick -= this.Advance;
        this.ticker = null;
    }


    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        foreach (var p in this.simulation.Particles)
        {
            canvas.SaveState();
            canvas.Alpha = (float)(1 - p.Progress);

            var x1 = p.X + (p.RandomFlex * p.TiltCos);
            var y1 = p.Y + (p.RandomFlex * p.TiltSin);
            var x2 = p.WobbleX + (p.RandomFlex * p.TiltCos);
            var y2 = p.WobbleY + (p.RandomFlex * p.TiltSin);

            if (p.Text is not null)
            {
                canvas.Translate((float)p.X, (float)p.Y);
                canvas.Rotate((float)(p.Wobble * 18));
                canvas.FontSize = (float)(16 * p.Scalar);
                canvas.FontColor = Colors.Black;
                canvas.DrawString(p.Text, 0, 0, HorizontalAlignment.Center);
            }
            else
            {
                canvas.FillColor = p.Color;
                switch (p.Shape)
                {
                    case ConfettiShape.Circle:
                        var rx = (float)Math.Max(0.5, Math.Abs(x2 - x1) * 0.6);
                        var ry = (float)Math.Max(0.5, Math.Abs(y2 - y1) * 0.6);
                        canvas.Translate((float)p.X, (float)p.Y);
                        canvas.Rotate((float)(p.Wobble * 18));
                        canvas.FillEllipse(-rx, -ry, rx * 2, ry * 2);
                        break;

                    case ConfettiShape.Star:
                        canvas.FillPath(Star(p));
                        break;

                    default:
                        var path = new PathF();
                        path.MoveTo((float)p.X, (float)p.Y);
                        path.LineTo((float)p.WobbleX, (float)y1);
                        path.LineTo((float)x2, (float)y2);
                        path.LineTo((float)x1, (float)p.WobbleY);
                        path.Close();
                        canvas.FillPath(path);
                        break;
                }
            }
            canvas.RestoreState();
        }
    }


    static PathF Star(ConfettiParticle p)
    {
        var path = new PathF();
        var rotation = Math.PI / 2 * 3;
        var inner = 4 * p.Scalar;
        var outer = 8 * p.Scalar;
        const double step = Math.PI / 5;

        for (var i = 0; i < 5; i++)
        {
            var ox = (float)(p.X + (Math.Cos(rotation) * outer));
            var oy = (float)(p.Y + (Math.Sin(rotation) * outer));
            if (i == 0)
                path.MoveTo(ox, oy);
            else
                path.LineTo(ox, oy);
            rotation += step;

            path.LineTo((float)(p.X + (Math.Cos(rotation) * inner)), (float)(p.Y + (Math.Sin(rotation) * inner)));
            rotation += step;
        }
        path.Close();
        return path;
    }
}

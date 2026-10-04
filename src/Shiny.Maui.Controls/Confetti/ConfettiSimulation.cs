namespace Shiny.Maui.Controls;

/// <summary>One piece of confetti. Mutable and pooled by nothing - a burst is a few hundred of them.</summary>
sealed class ConfettiParticle
{
    public double X;
    public double Y;
    public double Wobble;
    public double WobbleSpeed;
    public double Velocity;
    public double Angle2D;
    public double TiltAngle;
    public double TiltSin;
    public double TiltCos;
    public double WobbleX;
    public double WobbleY;
    public double RandomFlex;
    public double Gravity;
    public double Decay;
    public double Drift;
    public double Scalar;
    public bool Flat;
    public int Tick;
    public int TotalTicks;
    public Color Color = Colors.White;
    public ConfettiShape Shape;
    public string? Text;
    public ConfettiRun? Run;

    /// <summary>How far through its life the particle is, 0 to 1. Drives the fade.</summary>
    public double Progress => this.TotalTicks <= 0 ? 1 : Math.Min(1, (double)this.Tick / this.TotalTicks);
}


/// <summary>
/// The bursts started by one fire call. Completes once every burst has launched and every particle
/// it launched has faded out, which is what <c>FireAsync</c> awaits.
/// </summary>
sealed class ConfettiRun(int shots)
{
    readonly TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    int pendingShots = shots;
    int live;

    public Task Completion => this.tcs.Task;

    public void ShotLaunched(int particles)
    {
        this.pendingShots--;
        this.live += particles;
        this.TryComplete();
    }

    public void ParticleDied()
    {
        this.live--;
        this.TryComplete();
    }

    /// <summary>Ends the run now - the confetti was cleared or the page went away.</summary>
    public void Cancel() => this.tcs.TrySetResult();

    void TryComplete()
    {
        if (this.pendingShots <= 0 && this.live <= 0)
            this.tcs.TrySetResult();
    }
}


/// <summary>
/// The physics, frame for frame the same as canvas-confetti so a burst looks the same on MAUI as it
/// does on the web. It advances in fixed 60fps steps; the host converts real time into steps.
/// </summary>
sealed class ConfettiSimulation(Random random)
{
    readonly List<ConfettiParticle> particles = [];

    public IReadOnlyList<ConfettiParticle> Particles => this.particles;

    public bool IsEmpty => this.particles.Count == 0;


    /// <summary>Launches one burst from (<paramref name="x"/>, <paramref name="y"/>) in canvas units.</summary>
    public int Emit(ConfettiOptions options, double x, double y, ConfettiRun? run = null)
    {
        var count = Math.Max(0, options.ParticleCount);
        var radAngle = options.Angle * (Math.PI / 180);
        var radSpread = options.Spread * (Math.PI / 180);
        IReadOnlyList<Color> colors = options.Colors is { Count: > 0 } c ? [.. c] : ConfettiOptions.DefaultColors;
        IReadOnlyList<ConfettiShape> shapes = options.Shapes is { Count: > 0 } s ? [.. s] : [ConfettiShape.Square];
        IReadOnlyList<string>? emoji = options.Emoji is { Count: > 0 } e ? [.. e] : null;

        for (var i = 0; i < count; i++)
        {
            var wobble = options.Flat ? 0 : random.NextDouble() * 10;
            var tiltAngle = ((random.NextDouble() * 0.5) + 0.25) * Math.PI;
            this.particles.Add(new ConfettiParticle
            {
                X = x,
                Y = y,
                Wobble = wobble,
                WobbleSpeed = Math.Min(0.11, (random.NextDouble() * 0.1) + 0.05),
                Velocity = (options.StartVelocity * 0.5) + (random.NextDouble() * options.StartVelocity),
                Angle2D = -radAngle + ((0.5 * radSpread) - (random.NextDouble() * radSpread)),
                TiltAngle = tiltAngle,
                // Seeded with what Update computes: a tick shorter than a frame draws a particle before
                // its first step, and a zero WobbleX/Y stretches its shape to the canvas origin.
                TiltSin = options.Flat ? 0 : Math.Sin(tiltAngle),
                TiltCos = options.Flat ? 0 : Math.Cos(tiltAngle),
                WobbleX = x + (10 * options.Scalar * (options.Flat ? 1 : Math.Cos(wobble))),
                WobbleY = y + (10 * options.Scalar * (options.Flat ? 1 : Math.Sin(wobble))),
                Color = colors[random.Next(colors.Count)],
                Shape = shapes[random.Next(shapes.Count)],
                Text = emoji?[random.Next(emoji.Count)],
                TotalTicks = Math.Max(1, options.Ticks),
                Decay = options.Decay,
                Drift = options.Drift,
                RandomFlex = random.NextDouble() + 2,
                Gravity = options.Gravity * 3,
                Scalar = options.Scalar,
                Flat = options.Flat,
                Run = run
            });
        }
        return count;
    }


    /// <summary>Advances every particle one frame and drops the ones that have lived out their ticks.</summary>
    public void Step()
    {
        for (var i = this.particles.Count - 1; i >= 0; i--)
        {
            var p = this.particles[i];
            Update(p);

            if (p.Tick >= p.TotalTicks)
            {
                this.particles.RemoveAt(i);
                p.Run?.ParticleDied();
            }
        }
    }


    public void Clear()
    {
        foreach (var p in this.particles)
            p.Run?.Cancel();
        this.particles.Clear();
    }


    void Update(ConfettiParticle p)
    {
        p.X += (Math.Cos(p.Angle2D) * p.Velocity) + p.Drift;
        p.Y += (Math.Sin(p.Angle2D) * p.Velocity) + p.Gravity;
        p.Velocity *= p.Decay;

        if (p.Flat)
        {
            p.Wobble = 0;
            p.WobbleX = p.X + (10 * p.Scalar);
            p.WobbleY = p.Y + (10 * p.Scalar);
            p.TiltSin = 0;
            p.TiltCos = 0;
            p.RandomFlex = 1;
        }
        else
        {
            p.Wobble += p.WobbleSpeed;
            p.WobbleX = p.X + (10 * p.Scalar * Math.Cos(p.Wobble));
            p.WobbleY = p.Y + (10 * p.Scalar * Math.Sin(p.Wobble));
            p.TiltAngle += 0.1;
            p.TiltSin = Math.Sin(p.TiltAngle);
            p.TiltCos = Math.Cos(p.TiltAngle);
            p.RandomFlex = random.NextDouble() + 2;
        }

        p.Tick++;
    }
}

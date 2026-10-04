namespace Shiny.Maui.Controls;

/// <summary>One burst in a sequence, launched <see cref="Delay"/> after the sequence starts.</summary>
readonly record struct ConfettiShot(TimeSpan Delay, ConfettiOptions Options);


/// <summary>
/// The presets, as sequences of bursts. Kept in step with the Blazor host's copy - the two should
/// look the same side by side.
/// </summary>
static class ConfettiPresets
{
    /// <summary>
    /// Builds the bursts for <paramref name="preset"/>. <paramref name="origin"/> is normalised
    /// (0-1 of the page) and only used by presets that launch from a point.
    /// </summary>
    public static IReadOnlyList<ConfettiShot> Build(ConfettiPreset preset, Point? origin, Random random)
    {
        var x = origin?.X ?? 0.5;
        var y = origin?.Y ?? 0.6;

        switch (preset)
        {
            case ConfettiPreset.Random:
                return
                [
                    new(TimeSpan.Zero, new ConfettiOptions
                    {
                        Angle = Between(random, 55, 125),
                        Spread = Between(random, 50, 70),
                        ParticleCount = (int)Between(random, 50, 100),
                        OriginX = x,
                        OriginY = y
                    })
                ];

            case ConfettiPreset.Fireworks:
                return Fireworks(random);

            case ConfettiPreset.SideCannons:
                return SideCannons();

            case ConfettiPreset.Stars:
                return Stars(x, y);

            default:
                return [new(TimeSpan.Zero, new ConfettiOptions { ParticleCount = 100, Spread = 70, OriginX = x, OriginY = y })];
        }
    }


    static IReadOnlyList<ConfettiShot> Fireworks(Random random)
    {
        const int durationMs = 5000;
        const int intervalMs = 250;
        var shots = new List<ConfettiShot>();

        for (var at = 0; at < durationMs; at += intervalMs)
        {
            // Tapers off: the last bursts are a few sparks, not a wall of them.
            var count = (int)(50 * ((durationMs - at) / (double)durationMs));
            if (count <= 0)
                break;

            foreach (var (min, max) in new[] { (0.1, 0.3), (0.7, 0.9) })
            {
                shots.Add(new(TimeSpan.FromMilliseconds(at), new ConfettiOptions
                {
                    ParticleCount = count,
                    StartVelocity = 30,
                    Spread = 360,
                    Ticks = 60,
                    OriginX = Between(random, min, max),
                    OriginY = random.NextDouble() - 0.2
                }));
            }
        }
        return shots;
    }


    static IReadOnlyList<ConfettiShot> SideCannons()
    {
        string[] palette = ["#A786FF", "#FD8BBC", "#ECA184", "#F8DEB1"];
        var colors = palette.Select(Color.FromArgb).ToList();
        var shots = new List<ConfettiShot>();

        for (var at = 0; at < 3000; at += 50)
        {
            shots.Add(new(TimeSpan.FromMilliseconds(at), new ConfettiOptions
            {
                ParticleCount = 4, Angle = 60, Spread = 55, StartVelocity = 60,
                OriginX = 0, OriginY = 0.5, Colors = [.. colors]
            }));
            shots.Add(new(TimeSpan.FromMilliseconds(at), new ConfettiOptions
            {
                ParticleCount = 4, Angle = 120, Spread = 55, StartVelocity = 60,
                OriginX = 1, OriginY = 0.5, Colors = [.. colors]
            }));
        }
        return shots;
    }


    static IReadOnlyList<ConfettiShot> Stars(double x, double y)
    {
        string[] palette = ["#FFE400", "#FFBD00", "#E89400", "#FFCA6C", "#FDFFB8"];
        var colors = palette.Select(Color.FromArgb).ToList();
        var shots = new List<ConfettiShot>();

        foreach (var at in new[] { 0, 100, 200 })
        {
            shots.Add(new(TimeSpan.FromMilliseconds(at), StarBase(x, y, colors, 40, 1.2, ConfettiShape.Star)));
            shots.Add(new(TimeSpan.FromMilliseconds(at), StarBase(x, y, colors, 10, 0.75, ConfettiShape.Circle)));
        }
        return shots;
    }


    static ConfettiOptions StarBase(double x, double y, List<Color> colors, int count, double scalar, ConfettiShape shape) => new()
    {
        ParticleCount = count,
        Scalar = scalar,
        Shapes = [shape],
        Spread = 360,
        Ticks = 50,
        Gravity = 0,
        Decay = 0.94,
        StartVelocity = 30,
        OriginX = x,
        OriginY = y,
        Colors = [.. colors]
    };


    static double Between(Random random, double min, double max) => min + (random.NextDouble() * (max - min));
}

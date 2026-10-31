using Shiny.Controls.BarcodeReader.Linear;

namespace Shiny.Controls.BarcodeReader;

/// <summary>How to cut a candidate out for a native decoder.</summary>
/// <param name="Source">The region of the source plane, pixels.</param>
/// <param name="Scale">Resample factor; above 1 enlarges.</param>
public readonly record struct CropPlan(BoxF Source, float Scale);

/// <summary>
/// One frame's pass: locate candidates, then decode the linear ones in managed code. Matrix candidates (and
/// linear ones the engine could not read) are left for a native decoder, which the host feeds through
/// <see cref="PlanCrop"/> and <see cref="LumaImage.CropScaled"/>.
/// </summary>
/// <remarks>Not thread-safe — it reuses its buffers between frames, and the camera pipeline runs one pass at a time.</remarks>
public sealed class BarcodeReaderEngine
{
    readonly LinearReader linear = new();

    /// <summary>The locator, for tuning its thresholds.</summary>
    public BarcodeLocator Locator { get; } = new();

    /// <summary>Symbologies to read. Default all.</summary>
    public FormatSet Formats { get; set; } = FormatSet.All;

    /// <summary>Most scanlines sampled across one linear candidate. Default 15.</summary>
    public int MaxScanlines
    {
        get => this.linear.MaxLines;
        set => this.linear.MaxLines = Math.Max(1, value);
    }

    /// <summary>Locate and decode. <paramref name="region"/> (pixels) limits the search.</summary>
    public List<BarcodeCandidate> Scan(LumaImage image, BoxF? region = null)
    {
        var candidates = this.Locator.Locate(image, region);
        if (!this.Formats.HasAnyLinear)
            return candidates;

        foreach (var c in candidates)
        {
            if (c.Kind == CandidateKind.Linear)
                c.Read = this.linear.Read(image, c, this.Formats);
        }

        // a code broken into two candidates (a scratch, a glare stripe) reads twice — keep the better one
        for (var i = candidates.Count - 1; i >= 0; i--)
        {
            if (candidates[i].Read is not { } read)
                continue;
            for (var j = 0; j < i; j++)
            {
                if (candidates[j].Read is { } other && other.Text == read.Text && other.Symbology == read.Symbology)
                {
                    if (read.Votes > other.Votes)
                        candidates[j] = candidates[i];
                    candidates.RemoveAt(i);
                    break;
                }
            }
        }
        return candidates;
    }

    /// <summary>Read one linear region without locating first — for tests and for a host that knows where the code is.</summary>
    public BarcodeRead? ReadLinear(LumaImage image, OrientedBox box)
        => this.linear.Read(image, new BarcodeCandidate(CandidateKind.Linear, box, 1f), this.Formats);

    /// <summary>
    /// Where to crop a candidate and how much to enlarge it so its longest side reaches about
    /// <paramref name="targetSide"/> pixels, at most <paramref name="maxScale"/>x. A margin is left around
    /// it so the decoder sees the quiet zone and the finder patterns whole.
    /// </summary>
    public static CropPlan PlanCrop(BarcodeCandidate candidate, int imageWidth, int imageHeight, float targetSide = 720f, float maxScale = 4f)
    {
        var b = candidate.Box.Bounds;
        var pad = Math.Max(b.Width, b.Height) * 0.2f + 8f;
        var src = b.Inflate(pad, pad).Clamp(imageWidth, imageHeight);
        var side = Math.Max(1f, Math.Max(src.Width, src.Height));
        var scale = Math.Clamp(targetSide / side, 0.5f, maxScale);
        return new CropPlan(src, scale);
    }
}

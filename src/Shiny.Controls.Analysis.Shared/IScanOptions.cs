using Microsoft.Maui.Graphics;

namespace Shiny.Controls.Analysis;

/// <summary>
/// The options every scan service understands, whatever else its own options carry — the analyzer packages'
/// <c>Scan…</c> extensions read and write only these, so they work with any service's options type.
/// </summary>
public interface IScanOptions
{
    /// <summary>Whether a result whose duplicate key was already yielded in this session is skipped.</summary>
    bool FilterDuplicates { get; set; }

    /// <summary>The normalized region (0..1, upright image space) to scan; null scans the whole frame.</summary>
    RectF? ScanWindow { get; set; }
}

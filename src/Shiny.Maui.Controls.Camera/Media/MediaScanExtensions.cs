namespace Shiny.Maui.Controls.Camera.Media;

/// <summary>Helpers for shaping a scan session.</summary>
/// <remarks>
/// To collapse a session to a single result, use .NET 10's own
/// <c>System.Linq.AsyncEnumerable.FirstOrDefaultAsync</c> — it disposes the enumerator, which is what tears
/// the modal down, and it is what every single-result <c>Scan…Async</c> overload here calls. Collecting a
/// whole session is likewise just <c>ToListAsync</c>. Neither is re-declared here: a duplicate would be
/// ambiguous against the BCL's at every call site.
/// </remarks>
public static class MediaScanExtensions
{
    /// <summary>
    /// Return <paramref name="options"/> (or a fresh default) with
    /// <see cref="MediaScanOptions.FilterDuplicates"/> set. This is how the typed <c>Scan…</c> overloads
    /// honour their explicit <c>filterDuplicates</c> argument without making the caller new up an options
    /// object to set one flag — note that the argument therefore <b>wins</b> over the value on a passed-in
    /// options object.
    /// </summary>
    public static MediaScanOptions WithDuplicateFilter(this MediaScanOptions? options, bool filterDuplicates)
    {
        var resolved = options ?? new MediaScanOptions();
        resolved.FilterDuplicates = filterDuplicates;
        return resolved;
    }
}


namespace Shiny.Maui.Controls.Analysis.Documents;

/// <summary>
/// Helpers for merging a document's reads across frames (see <see cref="IDocumentParser{TDocument}.Merge"/>).
/// Scalar fields are merged with the null-coalescing operator in each parser (first non-null read wins, so the
/// value is stable once seen); these helpers pick the better of two repeating collections.
/// </summary>
static class DocumentMerge
{
    /// <summary>Pick the field list carrying more populated values (ties → the longer, then the accumulated).</summary>
    public static IReadOnlyList<DocumentField> Richer(IReadOnlyList<DocumentField> accumulated, IReadOnlyList<DocumentField> incoming)
    {
        int Populated(IReadOnlyList<DocumentField> f)
        {
            var n = 0;
            foreach (var x in f)
                if (x.Value is not null)
                    n++;
            return n;
        }

        var a = Populated(accumulated);
        var b = Populated(incoming);
        if (b > a)
            return incoming;
        if (b == a && incoming.Count > accumulated.Count)
            return incoming;
        return accumulated;
    }

    /// <summary>Pick the longer of two repeating-row collections (more rows ≈ a more complete read).</summary>
    public static IReadOnlyList<T> Longer<T>(IReadOnlyList<T> accumulated, IReadOnlyList<T> incoming)
        => incoming.Count > accumulated.Count ? incoming : accumulated;
}

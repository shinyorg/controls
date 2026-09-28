using DocumentFormat.OpenXml.Wordprocessing;

namespace Shiny.Controls.Office.Document;

/// <summary>How text lines up against a tab stop.</summary>
public enum DocumentTabAlignment
{
    Left,
    Center,
    Right,
    Decimal
}

/// <summary>
/// A custom tab stop on a paragraph.
/// </summary>
/// <param name="Position">Pixels (96 per inch) from the paragraph's left margin — the unit every
/// other paragraph measurement on the controller uses.</param>
/// <param name="Alignment">How text lines up against the stop.</param>
public readonly record struct DocumentTabStop(double Position, DocumentTabAlignment Alignment = DocumentTabAlignment.Left);

public sealed partial class DocumentEditorController
{
    /// <summary>
    /// The caret paragraph's own tab stops (<c>w:tabs</c>), in position order.
    /// </summary>
    /// <remarks>
    /// Direct formatting only — stops a style defines are not included, and <c>w:tab w:val="clear"</c>
    /// entries (which cancel a style's stop) are skipped. This is what a ruler shows and edits.
    /// </remarks>
    public IReadOnlyList<DocumentTabStop> CurrentTabStops
    {
        get
        {
            if (this.Document.ParagraphElementAt(this.Selection.Focus.Block)?.ParagraphProperties?.Tabs is not { } tabs)
                return [];

            var stops = new List<DocumentTabStop>();
            foreach (var tab in tabs.Elements<TabStop>())
            {
                if (tab.Position?.Value is not { } twips)
                    continue;

                var value = tab.Val?.Value;
                if (value == TabStopValues.Clear || value == TabStopValues.Bar)
                    continue;

                var alignment = value == TabStopValues.Center ? DocumentTabAlignment.Center
                    : value == TabStopValues.Right || value == TabStopValues.End ? DocumentTabAlignment.Right
                    : value == TabStopValues.Decimal ? DocumentTabAlignment.Decimal
                    : DocumentTabAlignment.Left;

                stops.Add(new DocumentTabStop(OoxmlUnits.TwipsToPixels(twips), alignment));
            }

            stops.Sort((a, b) => a.Position.CompareTo(b.Position));
            return stops;
        }
    }

    /// <summary>
    /// Replaces the selected paragraphs' own tab stops. An empty list removes <c>w:tabs</c>. One undo step.
    /// </summary>
    public void SetTabStops(IReadOnlyList<DocumentTabStop> stops)
    {
        ArgumentNullException.ThrowIfNull(stops);

        var copy = stops.OrderBy(x => x.Position).ToList();
        this.ApplyParagraphFormat(new ParagraphFormatChange("Tabs", properties =>
        {
            properties.RemoveAllChildren<Tabs>();
            if (copy.Count == 0)
                return;

            var tabs = new Tabs();
            foreach (var stop in copy)
            {
                tabs.AppendChild(new TabStop
                {
                    Val = stop.Alignment switch
                    {
                        DocumentTabAlignment.Center => TabStopValues.Center,
                        DocumentTabAlignment.Right => TabStopValues.Right,
                        DocumentTabAlignment.Decimal => TabStopValues.Decimal,
                        _ => TabStopValues.Left
                    },
                    Position = OoxmlUnits.PixelsToTwips(Math.Max(0, stop.Position))
                });
            }

            // w:pPr is a sequence: w:tabs sits after w:shd and before w:spacing, or Word calls the file corrupt.
            WordParagraphEditor.InsertOrdered(properties, tabs);
        }));
    }
}

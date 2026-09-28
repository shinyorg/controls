using DocumentFormat.OpenXml;
using Shiny.Controls.Office.Spreadsheet;
using Shiny.Controls.Office.Text;

namespace Shiny.Controls.Office.Document;

/// <summary>A comment, and the text it is anchored to.</summary>
/// <param name="Id">The <c>w:id</c> it has in <c>comments.xml</c>.</param>
/// <param name="Author">Who wrote it.</param>
/// <param name="Initials">Their initials, which is what a balloon shows first.</param>
/// <param name="Date">When, when the document recorded it.</param>
/// <param name="Text">The comment's text, paragraphs joined with newlines.</param>
/// <param name="Range">
/// The commented span in story positions. Empty for a comment anchored to a point, which is what a
/// comment added with nothing selected is.
/// </param>
public sealed record DocumentComment(string Id, string Author, string? Initials, DateTime? Date, string Text, DocumentRange Range);


/// <summary>What a tracked change did.</summary>
public enum RevisionKind
{
    Insertion,
    Deletion
}


/// <summary>One tracked change: a <c>w:ins</c> or <c>w:del</c>, and the text it covers.</summary>
public sealed record DocumentRevision(RevisionKind Kind, string Author, DateTime? Date, DocumentRange Range)
{
    /// <summary>The <c>w:ins</c> or <c>w:del</c> element, so accepting it can act on exactly this one.</summary>
    internal OpenXmlElement? Element { get; init; }
}


/// <summary>A footnote: its number, its text, and the paragraph that cites it.</summary>
/// <param name="Id">The <c>w:id</c> in <c>footnotes.xml</c>.</param>
/// <param name="Number">The number it is drawn with — its order of citation, not its id.</param>
/// <param name="Blocks">The note's own paragraphs, number first, ready to lay out.</param>
/// <param name="Reference">Where in the story it is cited.</param>
public sealed record DocumentNote(int Id, int Number, IReadOnlyList<DocumentBlock> Blocks, DocumentPosition Reference)
{
    /// <summary>The note's text on one line, for a tooltip or a list.</summary>
    public string Text => string.Join(" ", this.Blocks.OfType<DocumentParagraph>().Select(x => x.VisibleText)).Trim();
}


/// <summary>A named place in the document.</summary>
public sealed record DocumentBookmark(string Name, DocumentPosition Position)
{
    /// <summary>True for Word's own hidden bookmarks — the ones a table of contents links to.</summary>
    public bool IsHidden => this.Name.StartsWith('_');
}


/// <summary>A hyperlink in the document and the text it covers.</summary>
/// <param name="Target">An absolute URL, or <c>#name</c> for a bookmark in this document.</param>
/// <param name="Text">What the link reads as.</param>
/// <param name="Range">The linked span in story positions.</param>
public sealed record DocumentHyperlink(string Target, string Text, DocumentRange Range)
{
    /// <summary>True when the link jumps to a bookmark in this document rather than out of it.</summary>
    public bool IsInternal => this.Target.StartsWith('#');
}


/// <summary>A heading, for a navigation pane or a table of contents.</summary>
/// <param name="Level">1 for Heading 1, and so on.</param>
/// <param name="Text">The heading's text.</param>
/// <param name="Paragraph">Its story index, to jump to.</param>
public sealed record DocumentHeading(int Level, string Text, int Paragraph);


/// <summary>The figures the status bar and the Word Count dialog show.</summary>
public sealed record DocumentStatistics
{
    public static readonly DocumentStatistics Empty = new();

    public int Words { get; init; }

    /// <summary>Characters, not counting spaces.</summary>
    public int CharactersNoSpaces { get; init; }

    /// <summary>Characters, spaces included.</summary>
    public int CharactersWithSpaces { get; init; }

    /// <summary>Paragraphs with something in them — an empty line is not a paragraph to Word's count.</summary>
    public int Paragraphs { get; init; }

    public int Lines { get; init; }

    public int Pages { get; init; } = 1;

    /// <summary>The one-based page the caret is on.</summary>
    public int CurrentPage { get; init; } = 1;

    /// <summary>Words in the selection, or zero when nothing is selected.</summary>
    public int SelectedWords { get; init; }

    /// <summary>
    /// Counts words the way Word does: runs of anything that is not whitespace.
    /// </summary>
    /// <remarks>
    /// Punctuation attached to a word is part of it, and "don't" and "e-mail" are one word each. A
    /// stand-alone dash between spaces counts as a word, which is Word's behaviour too.
    /// </remarks>
    public static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;

        foreach (var c in text)
        {
            var word = !char.IsWhiteSpace(c) && c != '￼';
            if (word && !inWord)
                count++;

            inWord = word;
        }

        return count;
    }
}


/// <summary>One entry in the style gallery: a paragraph style, and how its text looks.</summary>
/// <param name="Id">The style id written into <c>w:pStyle</c>.</param>
/// <param name="Name">What the gallery calls it.</param>
public sealed record DocumentStyleInfo(string Id, string Name)
{
    public string FontFamily { get; init; } = "Calibri";

    /// <summary>Size in points.</summary>
    public double FontSize { get; init; } = 11;

    public ArgbColor Color { get; init; } = new(255, 0, 0, 0);
    public bool Bold { get; init; }
    public bool Italic { get; init; }

    /// <summary>1-9 for a heading style, zero otherwise.</summary>
    public int OutlineLevel { get; init; }

    /// <summary>True for one of Word's built-in styles, which is created on first use when the document lacks it.</summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>The same preview as a <see cref="TextStyle"/>, for a host that draws with the document painter.</summary>
    public TextStyle ToTextStyle() => TextStyle.Default with
    {
        FontFamily = this.FontFamily,
        FontSize = OoxmlUnits.PointsToPixels(this.FontSize),
        Color = this.Color,
        Bold = this.Bold,
        Italic = this.Italic
    };
}




/// <summary>The kinds of section break Insert and Layout offer.</summary>
public enum SectionBreakType
{
    NextPage,
    Continuous
}


/// <summary>Where a paragraph border goes.</summary>
public enum ParagraphBorderPreset
{
    None,
    Bottom,
    Top,
    Outside,
    All
}


/// <summary>A named paper size.</summary>
/// <param name="Name">Letter, A4 and so on.</param>
/// <param name="Width">Portrait width in pixels at 96 dpi.</param>
/// <param name="Height">Portrait height in pixels.</param>
public sealed record PaperSize(string Name, double Width, double Height)
{
    public static readonly PaperSize Letter = new("Letter", 816, 1056);
    public static readonly PaperSize Legal = new("Legal", 816, 1344);
    public static readonly PaperSize A4 = new("A4", OoxmlUnits.InchesToPixels(8.27), OoxmlUnits.InchesToPixels(11.69));
    public static readonly PaperSize A5 = new("A5", OoxmlUnits.InchesToPixels(5.83), OoxmlUnits.InchesToPixels(8.27));

    /// <summary>The presets both ribbons offer.</summary>
    public static IReadOnlyList<PaperSize> Presets { get; } = [Letter, A4, Legal, A5];

    /// <summary>A description for a gallery entry, in inches.</summary>
    public string Description => $"{this.Width / 96:0.##}\" × {this.Height / 96:0.##}\"";
}

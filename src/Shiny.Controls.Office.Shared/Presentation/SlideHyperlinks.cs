using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using Shiny.Controls.Office.Editing;
using D = DocumentFormat.OpenXml.Drawing;

namespace Shiny.Controls.Office.Presentation;

/// <summary>
/// A link as the text model carries it: <see cref="Text.TextStyle.Link"/> is a string, so a jump to a
/// slide is spelled <c>#slide=3</c> and a show action is its <c>ppaction://</c> URI.
/// </summary>
public static class SlideHyperlinkCodec
{
    const string SlidePrefix = "#slide=";

    public static string Encode(SlideHyperlink link)
        => link.Slide is { } slide
            ? SlidePrefix + slide.ToString(CultureInfo.InvariantCulture)
            : link.Url ?? link.Action ?? string.Empty;

    public static SlideHyperlink? Decode(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        if (value.StartsWith(SlidePrefix, StringComparison.Ordinal) &&
            int.TryParse(value.AsSpan(SlidePrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var slide))
        {
            return new SlideHyperlink(null, slide);
        }

        return value.StartsWith("ppaction://", StringComparison.Ordinal)
            ? new SlideHyperlink(null, null, value)
            : new SlideHyperlink(value);
    }
}

/// <summary>The show jumps PowerPoint's Link dialog offers under "Place in This Document".</summary>
public static class SlideShowJumps
{
    public const string NextSlide = "ppaction://hlinkshowjump?jump=nextslide";
    public const string PreviousSlide = "ppaction://hlinkshowjump?jump=previousslide";
    public const string FirstSlide = "ppaction://hlinkshowjump?jump=firstslide";
    public const string LastSlide = "ppaction://hlinkshowjump?jump=lastslide";
    public const string EndShow = "ppaction://hlinkshowjump?jump=endshow";
}

/// <summary>Creating the relationship a link needs.</summary>
static class SlideHyperlinks
{
    /// <summary>
    /// The <c>r:id</c> and <c>action</c> to write for a link, creating the slide's relationship to its
    /// target: an external hyperlink relationship for an address, a relationship to the slide part for a
    /// jump to another slide.
    /// </summary>
    public static (string? Id, string? Action)? Relate(SlideDeck deck, SlidePart part, SlideHyperlink link)
    {
        if (link.Slide is { } index)
        {
            if (deck.PartAt(index) is not { } target)
                return null;

            var existing = part.Parts.FirstOrDefault(x => ReferenceEquals(x.OpenXmlPart, target));
            var id = existing.OpenXmlPart is not null ? existing.RelationshipId : part.CreateRelationshipToPart(target);
            return (id, "ppaction://hlinksldjump");
        }

        if (!string.IsNullOrWhiteSpace(link.Url))
        {
            var url = link.Url.Trim();

            // A bare "example.com" is what people type; PowerPoint makes it an http link.
            if (!url.Contains(':', StringComparison.Ordinal) && !url.StartsWith('#'))
                url = "https://" + url;

            var uri = Uri.TryCreate(url, UriKind.Absolute, out var absolute) ? absolute : new Uri(url, UriKind.Relative);
            var relationship = part.HyperlinkRelationships.FirstOrDefault(x => x.Uri == uri)
                ?? part.AddHyperlinkRelationship(uri, true);

            return (relationship.Id, link.Action);
        }

        return link.Action is null ? null : (null, link.Action);
    }
}

/// <summary>
/// Links a run of text, or unlinks it — Insert ▸ Link with text selected.
/// </summary>
/// <remarks>
/// The relationship is created inside the command, so redo recreates what undo put back. An unused
/// hyperlink relationship left behind by an undo is harmless: PowerPoint neither shows nor repairs one.
/// </remarks>
public sealed record SetTextHyperlinkCommand(SlideTextRange Range, SlideHyperlink? Link) : SlideCommand
{
    public override string Name => this.Link is null ? "Remove link" : "Link";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var range = this.Range.Normalized();
        if (context.PartAt(range.Start.Slide) is not { } part)
            return new NoOpSlideCommand();

        string? id = null;
        string? action = null;

        if (this.Link is { } link)
        {
            if (SlideHyperlinks.Relate(context, part, link) is not { } related)
                return new NoOpSlideCommand();

            (id, action) = related;
            id ??= string.Empty;
        }

        return new FormatSlideRunsCommand(range, ShapeTextEditor.SetHyperlink(id, action), this.Name).Apply(context);
    }
}

/// <summary>Inserts text already carrying a link at a position — Insert ▸ Link with nothing selected.</summary>
public sealed record InsertLinkedTextCommand(SlidePosition At, string Text, SlideHyperlink Link) : SlideCommand
{
    public override string Name => "Link";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        var restore = CaptureShape(context, this.At.Slide, this.At.Shape);

        if (new InsertSlideTextCommand(this.At, this.Text).Apply(context) is NoOpSlideCommand)
            return new NoOpSlideCommand();

        var range = new SlideTextRange(this.At, this.At with { Offset = this.At.Offset + this.Text.Length });
        new SetTextHyperlinkCommand(range, this.Link).Apply(context);
        return restore;
    }
}

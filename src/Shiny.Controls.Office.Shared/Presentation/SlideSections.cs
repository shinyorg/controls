using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Presentation;
using Shiny.Controls.Office.Editing;
using P14 = DocumentFormat.OpenXml.Office2010.PowerPoint;

namespace Shiny.Controls.Office.Presentation;

/// <summary>A named run of slides — PowerPoint's sections, which the rail folds and unfolds.</summary>
/// <param name="Name">What the section header says.</param>
/// <param name="FirstSlide">The first slide in the section, or -1 for an empty section.</param>
/// <param name="SlideCount">How many slides it holds.</param>
public sealed record SlideSection(string Name, int FirstSlide, int SlideCount)
{
    /// <summary>The section's GUID, which is what PowerPoint keys it by.</summary>
    public string? Id { get; init; }

    public bool Contains(int slide) => this.SlideCount > 0 && slide >= this.FirstSlide && slide < this.FirstSlide + this.SlideCount;
}

/// <summary>
/// Reads and edits <c>p14:sectionLst</c>, the presentation extension sections live in.
/// </summary>
/// <remarks>
/// Every slide must be in exactly one section, in running order, once a deck has any — a slide in none
/// is a repair prompt. So adding a section splits the one the slide is in, and removing one gives its
/// slides to the section before it (or after, for the first).
/// </remarks>
static class SlideSectionXml
{
    const string SectionUri = "{521415D9-36F7-43E2-AB2F-B90AF26B5E84}";

    public static P14.SectionList? Find(DocumentFormat.OpenXml.Presentation.Presentation presentation)
        => presentation.Descendants<P14.SectionList>().FirstOrDefault();

    public static IReadOnlyList<SlideSection> Read(SlideDeck deck)
    {
        if (deck.PresentationRoot is not { } presentation || Find(presentation) is not { } list)
            return [];

        var order = presentation.SlideIdList?.Elements<SlideId>().Select(x => x.Id?.Value ?? 0).ToList() ?? [];

        // Only slides that are in the running order (and that the deck read) count; a deleted slide's
        // id may linger in a section until the save strips it.
        var shown = new List<uint>();
        for (var i = 0; i < deck.Slides.Count; i++)
        {
            if (SlideStructureEdits.SlideIdOf(deck, i)?.Id?.Value is { } id)
                shown.Add(id);
        }

        var result = new List<SlideSection>();
        foreach (var section in list.Elements<P14.Section>())
        {
            var ids = section.Descendants<P14.SectionSlideIdListEntry>().Select(x => x.Id?.Value ?? 0).ToList();
            var indices = ids.Select(x => shown.IndexOf(x)).Where(x => x >= 0).OrderBy(x => x).ToList();

            result.Add(new SlideSection(section.Name?.Value ?? "Section", indices.Count == 0 ? -1 : indices[0], indices.Count)
            {
                Id = section.Id?.Value
            });
        }

        _ = order;
        return result;
    }

    /// <summary>The section list, created with one "Default Section" holding every slide if there is none.</summary>
    public static P14.SectionList Ensure(SlideDeck deck)
    {
        var presentation = deck.PresentationRoot!;
        if (Find(presentation) is { } existing)
            return existing;

        var list = new P14.SectionList();
        var section = new P14.Section { Name = "Default Section", Id = NewId() };
        var ids = new P14.SectionSlideIdList();
        foreach (var id in presentation.SlideIdList?.Elements<SlideId>() ?? [])
            ids.AppendChild(new P14.SectionSlideIdListEntry { Id = id.Id?.Value ?? 0 });

        section.AppendChild(ids);
        list.AppendChild(section);

        var extensions = presentation.PresentationExtensionList;
        if (extensions is null)
        {
            extensions = new PresentationExtensionList();

            // p:extLst is the presentation's last child.
            presentation.AppendChild(extensions);
        }

        var extension = new PresentationExtension { Uri = SectionUri };
        extension.AppendChild(list);
        extensions.AppendChild(extension);
        return list;
    }

    public static string NewId() => "{" + Guid.NewGuid().ToString().ToUpperInvariant() + "}";
}

/// <summary>What a section edit does.</summary>
public enum SlideSectionEdit
{
    /// <summary>Starts a new section at a slide, splitting the one it was in.</summary>
    Add,
    Rename,

    /// <summary>Removes the section, giving its slides to its neighbour. Deletes no slides.</summary>
    Remove,

    /// <summary>Removes the section and every slide in it.</summary>
    RemoveWithSlides,
    MoveUp,
    MoveDown
}

/// <summary>
/// Adds, renames, removes or reorders a section, restoring the presentation's extension list on undo.
/// </summary>
public sealed record EditSlideSectionCommand(SlideSectionEdit Edit, int Index, string? Title = null) : SlideCommand
{
    public override string Name => this.Edit switch
    {
        SlideSectionEdit.Add => "Add section",
        SlideSectionEdit.Rename => "Rename section",
        SlideSectionEdit.MoveUp or SlideSectionEdit.MoveDown => "Move section",
        _ => "Remove section"
    };

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (context.PresentationRoot is not { } presentation)
            return new NoOpSlideCommand();

        var before = new RestoreSectionsCommand(presentation.PresentationExtensionList?.CloneNode(true), this.Name);

        switch (this.Edit)
        {
            case SlideSectionEdit.Add:
            {
                // Index is a slide: the new section starts there.
                if (SlideStructureEdits.SlideIdOf(context, this.Index)?.Id?.Value is not { } slideId)
                    return new NoOpSlideCommand();

                var list = SlideSectionXml.Ensure(context);
                var entry = list.Descendants<P14.SectionSlideIdListEntry>().FirstOrDefault(x => x.Id?.Value == slideId);
                if (entry?.Parent?.Parent is not P14.Section owner)
                    return new NoOpSlideCommand();

                var fresh = new P14.Section { Name = this.Title ?? "Untitled Section", Id = SlideSectionXml.NewId() };
                var moving = entry.ElementsAfter().Prepend(entry).OfType<P14.SectionSlideIdListEntry>().ToList();
                var ids = new P14.SectionSlideIdList();
                foreach (var item in moving)
                {
                    item.Remove();
                    ids.AppendChild(item);
                }

                fresh.AppendChild(ids);
                owner.InsertAfterSelf(fresh);

                // A section left with no slides at the front is fine - PowerPoint shows it empty.
                break;
            }

            case SlideSectionEdit.Rename:
            {
                if (SlideSectionXml.Find(presentation)?.Elements<P14.Section>().ElementAtOrDefault(this.Index) is not { } section ||
                    string.IsNullOrWhiteSpace(this.Title))
                    return new NoOpSlideCommand();

                section.Name = this.Title;
                break;
            }

            case SlideSectionEdit.Remove:
            case SlideSectionEdit.RemoveWithSlides:
            {
                if (SlideSectionXml.Find(presentation) is not { } list ||
                    list.Elements<P14.Section>().ToList() is not { } sections ||
                    sections.ElementAtOrDefault(this.Index) is not { } section)
                {
                    return new NoOpSlideCommand();
                }

                var entries = section.Descendants<P14.SectionSlideIdListEntry>().ToList();

                if (this.Edit == SlideSectionEdit.RemoveWithSlides)
                {
                    // Slides go through the ordinary delete so each is parked for undo; the section's
                    // own removal is the extension list restored with them.
                    var indices = entries
                        .Select(e => Enumerable.Range(0, context.Slides.Count).FirstOrDefault(i => SlideStructureEdits.SlideIdOf(context, i)?.Id?.Value == e.Id?.Value, -1))
                        .Where(x => x >= 0)
                        .OrderByDescending(x => x)
                        .ToList();

                    var inverses = new List<IEditCommand<SlideDeck>>();
                    section.Remove();
                    foreach (var index in indices)
                        inverses.Add(new DeleteSlideCommand(index).Apply(context));

                    if (!list.Elements<P14.Section>().Any())
                        list.Parent?.Remove();

                    inverses.Reverse();
                    return new CompositeCommand<SlideDeck>(this.Name, [.. inverses, before]);
                }

                if (sections.Count == 1)
                {
                    // The last section: the deck goes back to having none.
                    list.Parent?.Remove();
                    if (presentation.PresentationExtensionList is { HasChildren: false } empty)
                        empty.Remove();

                    break;
                }

                var neighbour = this.Index > 0 ? sections[this.Index - 1] : sections[1];
                var target = neighbour.GetFirstChild<P14.SectionSlideIdList>() ?? neighbour.AppendChild(new P14.SectionSlideIdList());

                foreach (var entry in entries)
                {
                    entry.Remove();
                    if (this.Index > 0)
                        target.AppendChild(entry);
                }

                if (this.Index == 0)
                {
                    foreach (var entry in entries.AsEnumerable().Reverse())
                        target.PrependChild(entry);
                }

                section.Remove();
                break;
            }

            case SlideSectionEdit.MoveUp:
            case SlideSectionEdit.MoveDown:
                return new MoveSlideSectionCommand(this.Index, this.Edit == SlideSectionEdit.MoveUp ? -1 : 1).Apply(context);
        }

        context.MarkStructureDirty();
        context.SectionsChanged();
        return before;
    }
}

/// <summary>
/// Moves a whole section up or down past its neighbour — its slides move with it in the running order.
/// </summary>
sealed record MoveSlideSectionCommand(int Index, int Direction) : SlideCommand
{
    public override string Name => "Move section";

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (context.PresentationRoot is not { } presentation || SlideSectionXml.Find(presentation) is not { } list)
            return new NoOpSlideCommand();

        var sections = list.Elements<P14.Section>().ToList();
        var other = this.Index + this.Direction;
        if (this.Index < 0 || this.Index >= sections.Count || other < 0 || other >= sections.Count)
            return new NoOpSlideCommand();

        var structure = SlideStructure.Capture(context);
        var focus = context.Slides.Count == 0 ? 0 : 0;

        var moving = sections[this.Index];
        moving.Remove();
        if (this.Direction < 0)
            sections[other].InsertBeforeSelf(moving);
        else
            sections[other].InsertAfterSelf(moving);

        // The running order follows the sections: slides listed in section order.
        var ids = context.EnsureSlideIdList();
        var byId = ids.Elements<SlideId>().ToDictionary(x => x.Id?.Value ?? 0);
        var ordered = list.Descendants<P14.SectionSlideIdListEntry>().Select(x => x.Id?.Value ?? 0).Where(byId.ContainsKey).ToList();

        // Parked (deleted) slides are not in the running order but may still be in a section; anything in
        // the running order and in no section keeps its place at the end.
        var rest = byId.Keys.Where(x => !ordered.Contains(x)).ToList();
        var entries = ordered.Concat(rest).Select(x => byId[x]).ToList();

        foreach (var entry in entries)
            entry.Remove();

        foreach (var entry in entries)
            ids.AppendChild(entry);

        var firstMoved = moving.Descendants<P14.SectionSlideIdListEntry>().FirstOrDefault()?.Id?.Value;
        if (firstMoved is { } id)
            focus = Math.Max(0, entries.FindIndex(x => x.Id?.Value == id));

        context.Restructure(focus);
        return new RestoreSlideStructureCommand(this.Name, structure, null, null, focus, focus);
    }
}

/// <summary>Puts the presentation's extension list — where the sections are — back as captured.</summary>
sealed record RestoreSectionsCommand(OpenXmlElement? Extensions, string Label) : SlideCommand
{
    public override string Name => this.Label;

    public override IEditCommand<SlideDeck> Apply(SlideDeck context)
    {
        if (context.PresentationRoot is not { } presentation)
            return new NoOpSlideCommand();

        var inverse = new RestoreSectionsCommand(presentation.PresentationExtensionList?.CloneNode(true), this.Label);
        presentation.PresentationExtensionList?.Remove();

        if (this.Extensions?.CloneNode(true) is PresentationExtensionList restored)
            presentation.AppendChild(restored);

        context.MarkStructureDirty();
        context.SectionsChanged();
        return inverse;
    }
}

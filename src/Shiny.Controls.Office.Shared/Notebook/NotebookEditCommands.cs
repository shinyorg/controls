using Shiny.Controls.Office.Editing;
using Shiny.Controls.Office.Spreadsheet;

namespace Shiny.Controls.Office.Notebook;

/// <summary>
/// Base for every notebook edit.
/// </summary>
/// <remarks>
/// Addresses its page by id rather than by index, because a section can be reordered or a page moved
/// between sections while the command sits on the undo stack. An index captured then points at a
/// different page now, and the edit lands somewhere the user never touched — silently, since both are
/// valid pages.
/// </remarks>
public abstract record NotebookCommand : IEditCommand<NotebookDocument>
{
    public abstract string Name { get; }

    public abstract IEditCommand<NotebookDocument> Apply(NotebookDocument document);

    protected static NotebookPage? PageOf(NotebookDocument document, string pageId)
        => document.PageAt(document.Locate(pageId));
}

/// <summary>What a command returns when the thing it addressed is gone. Applying it does nothing.</summary>
public sealed record NoOpNotebookCommand(string Label = "Edit") : NotebookCommand
{
    public override string Name => this.Label;

    public override IEditCommand<NotebookDocument> Apply(NotebookDocument document) => this;
}

/// <summary>
/// Swaps one item for another — the workhorse behind moving, resizing, restyling and typing.
/// </summary>
/// <remarks>
/// <para>
/// One command rather than a family of them because <see cref="NoteItem"/> is immutable: every edit to
/// an item is already "produce a new record", so the inverse is always "put the old record back". A
/// per-property command would carry the same payload with more code and a worse undo label.
/// </para>
/// <para>
/// <paramref name="Coalesce"/> is what makes a typing run one undo step. It merges only with another
/// coalescing replace of the same item under the same label, so a drag never absorbs the keystroke
/// that follows it and typing never absorbs the resize that follows that.
/// </para>
/// </remarks>
public sealed record ReplaceItemCommand(string PageId, NoteItem Item, string Label = "Edit", bool Coalesce = false)
    : NotebookCommand, IMergeableCommand<NotebookDocument>
{
    public override string Name => this.Label;

    public override IEditCommand<NotebookDocument> Apply(NotebookDocument document)
    {
        if (PageOf(document, this.PageId) is not { } page)
            return new NoOpNotebookCommand(this.Label);

        var index = page.IndexOf(this.Item.Id);
        if (index < 0)
            return new NoOpNotebookCommand(this.Label);

        var previous = page.Items[index];
        page.Items[index] = this.Item;
        document.NotifyContentChanged(page);

        return new ReplaceItemCommand(this.PageId, previous, this.Label, this.Coalesce);
    }

    public bool TryMerge(IEditCommand<NotebookDocument> next, out IEditCommand<NotebookDocument> merged)
    {
        merged = next;

        return this.Coalesce &&
            next is ReplaceItemCommand other &&
            other.Coalesce &&
            other.PageId == this.PageId &&
            other.Item.Id == this.Item.Id &&
            other.Label == this.Label;
    }
}

/// <summary>Adds an item at a z-order position. Index past the end appends, which is the usual case.</summary>
public sealed record InsertItemCommand(string PageId, NoteItem Item, int Index = int.MaxValue, string Label = "Insert")
    : NotebookCommand
{
    public override string Name => this.Label;

    public override IEditCommand<NotebookDocument> Apply(NotebookDocument document)
    {
        if (PageOf(document, this.PageId) is not { } page)
            return new NoOpNotebookCommand(this.Label);

        var index = Math.Clamp(this.Index, 0, page.Items.Count);
        page.Items.Insert(index, this.Item);
        document.NotifyContentChanged(page);

        return new DeleteItemCommand(this.PageId, this.Item.Id, this.Label);
    }
}

public sealed record DeleteItemCommand(string PageId, string ItemId, string Label = "Delete") : NotebookCommand
{
    public override string Name => this.Label;

    public override IEditCommand<NotebookDocument> Apply(NotebookDocument document)
    {
        if (PageOf(document, this.PageId) is not { } page)
            return new NoOpNotebookCommand(this.Label);

        var index = page.IndexOf(this.ItemId);
        if (index < 0)
            return new NoOpNotebookCommand(this.Label);

        var removed = page.Items[index];
        page.Items.RemoveAt(index);
        document.NotifyContentChanged(page);

        // The z-order position goes into the inverse, so undoing a delete puts the item back under
        // whatever was painted over it rather than on top of everything.
        return new InsertItemCommand(this.PageId, removed, index, this.Label);
    }
}

/// <summary>Moves an item through the z-order — bring to front, send to back, and the two one-step forms.</summary>
public sealed record ReorderItemCommand(string PageId, string ItemId, int Index, string Label = "Reorder") : NotebookCommand
{
    public override string Name => this.Label;

    public override IEditCommand<NotebookDocument> Apply(NotebookDocument document)
    {
        if (PageOf(document, this.PageId) is not { } page)
            return new NoOpNotebookCommand(this.Label);

        var from = page.IndexOf(this.ItemId);
        var to = Math.Clamp(this.Index, 0, page.Items.Count - 1);

        if (from < 0 || from == to)
            return new NoOpNotebookCommand(this.Label);

        var item = page.Items[from];
        page.Items.RemoveAt(from);
        page.Items.Insert(to, item);
        document.NotifyContentChanged(page);

        return new ReorderItemCommand(this.PageId, this.ItemId, from, this.Label);
    }
}

/// <summary>The page's own settings — its rule, its background, and the canvas minimum.</summary>
public sealed record SetPageSettingsCommand(
    string PageId,
    PageRule Rule,
    double RuleSpacing,
    ArgbColor? RuleColor,
    ArgbColor? Background,
    string Label = "Page setup") : NotebookCommand
{
    public override string Name => this.Label;

    public override IEditCommand<NotebookDocument> Apply(NotebookDocument document)
    {
        if (PageOf(document, this.PageId) is not { } page)
            return new NoOpNotebookCommand(this.Label);

        var inverse = new SetPageSettingsCommand(
            this.PageId, page.Rule, page.RuleSpacing, page.RuleColor, page.Background, this.Label);

        page.Rule = this.Rule;
        page.RuleSpacing = this.RuleSpacing;
        page.RuleColor = this.RuleColor;
        page.Background = this.Background;
        document.NotifyContentChanged(page);

        return inverse;
    }
}

public sealed record RenamePageCommand(string PageId, string Title) : NotebookCommand
{
    public override string Name => "Rename page";

    public override IEditCommand<NotebookDocument> Apply(NotebookDocument document)
    {
        if (PageOf(document, this.PageId) is not { } page)
            return new NoOpNotebookCommand(this.Name);

        var previous = page.Title;
        page.Title = this.Title;
        document.NotifyStructureChanged();

        return new RenamePageCommand(this.PageId, previous);
    }
}

/// <summary>
/// Adds a page to a section.
/// </summary>
/// <remarks>
/// The page instance is carried on the command rather than created inside <see cref="Apply"/>, so that
/// redoing an undone add restores the very page that was there — with its items — instead of a fresh
/// empty one with a new id.
/// </remarks>
public sealed record AddPageCommand(string SectionId, NotebookPage Page, int Index = int.MaxValue) : NotebookCommand
{
    public override string Name => "Add page";

    public override IEditCommand<NotebookDocument> Apply(NotebookDocument document)
    {
        if (document.Sections.FirstOrDefault(x => x.Id == this.SectionId) is not { } section)
            return new NoOpNotebookCommand(this.Name);

        section.Pages.Insert(Math.Clamp(this.Index, 0, section.Pages.Count), this.Page);
        document.NotifyStructureChanged();

        return new DeletePageCommand(this.Page.Id);
    }
}

public sealed record DeletePageCommand(string PageId) : NotebookCommand
{
    public override string Name => "Delete page";

    public override IEditCommand<NotebookDocument> Apply(NotebookDocument document)
    {
        var address = document.Locate(this.PageId);
        if (document.SectionAt(address.Section) is not { } section)
            return new NoOpNotebookCommand(this.Name);

        var page = section.Pages[address.Page];
        section.Pages.RemoveAt(address.Page);

        // A section with no pages has nowhere to put the caret and no page list row to click, so it
        // refills itself rather than becoming a tab that cannot be opened.
        if (section.Pages.Count == 0)
            section.Pages.Add(new NotebookPage(NotebookDocument.NewId(), "Untitled page"));

        document.NotifyStructureChanged();

        return new AddPageCommand(section.Id, page, address.Page);
    }
}

/// <summary>Moves a page within its section or into another one, which is what dragging a page row does.</summary>
public sealed record MovePageCommand(string PageId, string TargetSectionId, int Index) : NotebookCommand
{
    public override string Name => "Move page";

    public override IEditCommand<NotebookDocument> Apply(NotebookDocument document)
    {
        var address = document.Locate(this.PageId);
        if (document.SectionAt(address.Section) is not { } from)
            return new NoOpNotebookCommand(this.Name);

        if (document.Sections.FirstOrDefault(x => x.Id == this.TargetSectionId) is not { } to)
            return new NoOpNotebookCommand(this.Name);

        var page = from.Pages[address.Page];
        var inverse = new MovePageCommand(this.PageId, from.Id, address.Page);

        from.Pages.RemoveAt(address.Page);
        to.Pages.Insert(Math.Clamp(this.Index, 0, to.Pages.Count), page);

        if (from.Pages.Count == 0 && !ReferenceEquals(from, to))
            from.Pages.Add(new NotebookPage(NotebookDocument.NewId(), "Untitled page"));

        document.NotifyStructureChanged();

        return inverse;
    }
}

public sealed record AddSectionCommand(NotebookSection Section, int Index = int.MaxValue) : NotebookCommand
{
    public override string Name => "Add section";

    public override IEditCommand<NotebookDocument> Apply(NotebookDocument document)
    {
        document.Sections.Insert(Math.Clamp(this.Index, 0, document.Sections.Count), this.Section);
        document.NotifyStructureChanged();

        return new DeleteSectionCommand(this.Section.Id);
    }
}

public sealed record DeleteSectionCommand(string SectionId) : NotebookCommand
{
    public override string Name => "Delete section";

    public override IEditCommand<NotebookDocument> Apply(NotebookDocument document)
    {
        var index = -1;
        for (var i = 0; i < document.Sections.Count; i++)
        {
            if (document.Sections[i].Id == this.SectionId)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return new NoOpNotebookCommand(this.Name);

        var section = document.Sections[index];
        document.Sections.RemoveAt(index);

        if (document.Sections.Count == 0)
            document.Sections.Add(NotebookDocument.NewSection("Section 1"));

        document.NotifyStructureChanged();

        return new AddSectionCommand(section, index);
    }
}

public sealed record RenameSectionCommand(string SectionId, string Title, ArgbColor? Color) : NotebookCommand
{
    public override string Name => "Rename section";

    public override IEditCommand<NotebookDocument> Apply(NotebookDocument document)
    {
        if (document.Sections.FirstOrDefault(x => x.Id == this.SectionId) is not { } section)
            return new NoOpNotebookCommand(this.Name);

        var inverse = new RenameSectionCommand(this.SectionId, section.Title, section.Color);

        section.Title = this.Title;
        section.Color = this.Color;
        document.NotifyStructureChanged();

        return inverse;
    }
}

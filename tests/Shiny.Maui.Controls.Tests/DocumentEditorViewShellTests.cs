using Microsoft.Maui.Controls;
using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Shell;
using Shiny.Maui.Controls.Office;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// <see cref="DocumentEditorView"/> dressed in the Office shell: parts present by default, the
/// shell-less layout, the status bar and ruler following the controller, view modes, comments and
/// the backstage's template fallback.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class DocumentEditorViewShellTests
{
    public DocumentEditorViewShellTests() => TestDispatcherProvider.Install();

    static async Task<DocumentEditorView> ViewAsync(string template = WordTemplates.ReportId)
    {
        var document = await WordTemplates.OpenAsync(WordTemplates.All.Single(x => x.Id == template));
        return new DocumentEditorView { Document = document };
    }

    [Fact]
    public async Task TheShellIsOnByDefaultWithEveryPartInItsSlot()
    {
        var view = await ViewAsync();

        view.ShowShell.ShouldBeTrue();
        view.Content.ShouldBeSameAs(view.Shell);
        view.Shell.TitleBar.ShouldBeSameAs(view.TitleBar);
        view.Shell.StatusBar.ShouldBeSameAs(view.StatusBar);
        view.Shell.Ruler.ShouldBeSameAs(view.Ruler);
        view.Shell.LeftPane.ShouldBeSameAs(view.NavigationPane);
        view.Shell.RightPane.ShouldBeSameAs(view.CommentsPane);
        view.Shell.Backstage.ShouldBeSameAs(view.Backstage);
        view.TitleBar.IsVisible.ShouldBeTrue();
        view.StatusBar.IsVisible.ShouldBeTrue();
        view.Ruler.IsVisible.ShouldBeTrue();
        view.TitleBar.DocumentName.ShouldBe("Document1");

        // Shortcut text moved off the tooltips into the command list.
        view.CommandIndex.Commands.ShouldContain(x => x.Label == "Bold" && x.Shortcut == "Ctrl+B");
        view.CommandIndex.Commands.ShouldContain(x => x.Label == "Export to PDF");

        view.Backstage.Templates!.Select(x => x.Id).ShouldContain(WordTemplates.LetterId);
    }

    [Fact]
    public async Task TurningTheShellOffLeavesTheRibbonOverThePage()
    {
        var view = await ViewAsync();
        view.ShowCommentsPane = true;

        view.ShowShell = false;

        view.TitleBar.IsVisible.ShouldBeFalse();
        view.StatusBar.IsVisible.ShouldBeFalse();
        view.Ruler.IsVisible.ShouldBeFalse();
        view.RibbonActions.IsVisible.ShouldBeFalse();
        view.Shell.IsRightPaneOpen.ShouldBeFalse();
        view.StyleGallery.IsVisible.ShouldBeFalse();

        view.ShowShell = true;
        view.Shell.IsRightPaneOpen.ShouldBeTrue();
        view.TitleBar.IsVisible.ShouldBeTrue();
    }

    [Fact]
    public async Task TheStatusBarFollowsTheDocument()
    {
        var view = await ViewAsync(WordTemplates.BlankId);
        var controller = view.Controller!;

        var words = view.StatusBar.Items.Single(x => x.Id == "words");
        words.Text.ShouldBe("0 words");

        controller.InsertText("Three short words");

        words.Text.ShouldBe(WordShell.WordsText(controller.Statistics));
        words.Text.ShouldBe("3 words");
        view.StatusBar.Items.Single(x => x.Id == "page").Text.ShouldBe("Page 1 of 1");
        view.EffectiveSaveState.ShouldBe(OfficeSaveState.Unsaved);
        view.TitleBar.SaveState.ShouldBe(OfficeSaveState.Unsaved);
        view.TitleBar.CanUndo.ShouldBeTrue();
    }

    [Fact]
    public async Task ARulerDragIndentsTheCaretParagraph()
    {
        var view = await ViewAsync();
        var controller = view.Controller!;
        var body = controller.Document.Paragraphs.ToList().FindIndex(p => p.PlainText.StartsWith("Summarise"));
        controller.Selection.MoveTo(new DocumentPosition(body, 0));

        var ruler = view.Ruler;
        var x = ruler.Model.MarkerX(OfficeRulerMarker.LeftIndent);
        ruler.BeginDrag(x, 0.9).ShouldBe(OfficeRulerMarker.LeftIndent);
        ruler.DragTo(x + (36 * ruler.PixelsPerPoint * ruler.Zoom), 0.9);
        ruler.EndDrag();
        view.CommitRulerEdits();

        controller.CaretFormat.IndentLeft.ShouldBe(48, 0.5);   // half an inch, in pixels
        ruler.Indents.Left.ShouldBe(36, 0.1);

        controller.Undo();
        controller.CaretFormat.IndentLeft.ShouldBe(0, 0.5);
    }

    [Theory]
    [InlineData("web", false, DocumentPageLayout.Reflow)]
    [InlineData("print", false, DocumentPageLayout.Print)]
    [InlineData("read", true, DocumentPageLayout.Reflow)]
    public async Task TheStatusBarViewModesDriveTheEditor(string id, bool readMode, DocumentPageLayout layout)
    {
        var view = await ViewAsync();

        view.StatusBar.SelectViewMode(id);

        view.ReadMode.ShouldBe(readMode);
        view.Editor.PageLayout.ShouldBe(layout);
        view.StatusBar.SelectedViewMode.ShouldBe(id);
        view.Ruler.IsVisible.ShouldBe(id == "print");
    }

    [Fact]
    public async Task TheCommentsButtonOpensAPaneListingTheComments()
    {
        var view = await ViewAsync();
        var controller = view.Controller!;
        controller.Selection.MoveTo(new DocumentPosition(3, 2));
        controller.AddComment("Check this figure");
        view.Editor.Controller!.ShouldBeSameAs(controller);

        view.RibbonActions.ToggleComments();

        view.ShowCommentsPane.ShouldBeTrue();
        view.Shell.IsRightPaneOpen.ShouldBeTrue();

        var list = (VerticalStackLayout)((ScrollView)view.CommentsPane.PaneContent!).Content!;
        list.Children.OfType<Border>().ShouldHaveSingleItem();

        view.CommentsPane.Close();
        view.ShowCommentsPane.ShouldBeFalse();
    }

    [Fact]
    public async Task TheCommentsButtonFollowsThePaneWhateverOpensIt()
    {
        var view = await ViewAsync();

        view.ShowCommentsPane = true;
        view.RibbonActions.IsCommentsOpen.ShouldBeTrue();

        view.CommentsPane.Close();
        view.RibbonActions.IsCommentsOpen.ShouldBeFalse();

        view.Shell.IsRightPaneOpen = true;
        view.RibbonActions.IsCommentsOpen.ShouldBeTrue();

        view.RibbonActions.ToggleComments();
        view.Shell.IsRightPaneOpen.ShouldBeFalse();
        view.ShowCommentsPane.ShouldBeFalse();
    }

    [Fact]
    public async Task APickedTemplateOpensWhenNobodyHandlesIt()
    {
        var view = await ViewAsync(WordTemplates.BlankId);
        var original = view.Document;
        var opened = new TaskCompletionSource<WordDocument>();
        view.DocumentOpened += (_, document) => opened.TrySetResult(document);

        view.Backstage.ChooseTemplate(WordTemplates.All.Single(x => x.Id == WordTemplates.ReportId));

        var document = await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
        document.ShouldNotBeSameAs(original);
        view.Document.ShouldBeSameAs(document);
        view.DocumentName.ShouldBe("Report");
        view.NavigationPane.Headings!.Select(x => x.Text).ShouldContain("Findings");
    }

    [Fact]
    public async Task AHandledTemplateIsTheHostsToOpen()
    {
        var view = await ViewAsync(WordTemplates.BlankId);
        var original = view.Document;
        OfficeTemplate? picked = null;
        view.NewDocumentRequested += (_, template) => picked = template;

        view.Backstage.ChooseTemplate(WordTemplates.All.Single(x => x.Id == WordTemplates.LetterId));

        picked!.Id.ShouldBe(WordTemplates.LetterId);
        view.Document.ShouldBeSameAs(original);
    }

    [Fact]
    public async Task ExportPdfWritesAPdf()
    {
        var view = await ViewAsync();
        using var output = new MemoryStream();

        view.ExportPdf(output).ShouldBeGreaterThanOrEqualTo(1);
        System.Text.Encoding.ASCII.GetString(output.ToArray(), 0, 5).ShouldBe("%PDF-");
    }
}

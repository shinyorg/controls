using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Office.Shell;
using Shiny.Maui.Controls.Office;
using Shiny.Maui.Controls.Ribbons;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The MAUI Office shell parts, pressed through their public seams: the title bar's command search,
/// the status bar's zoom, a ruler drag, the backstage's tasks and the shell's own wiring.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class OfficeShellViewTests
{
    // The shell's zoom dialog binds to itself, and a typed binding asks for a dispatcher.
    public OfficeShellViewTests() => TestDispatcherProvider.Install();

    [Fact]
    public async Task TheTitleBarSearchRunsTheBestMatch()
    {
        var ran = new List<string>();
        var index = new OfficeCommandIndex();
        index.Add("Bold", () => ran.Add("bold"), "Home › Font", "Ctrl+B");
        index.Add("Insert Table", () => ran.Add("table"), "Insert");

        var bar = new OfficeTitleBar { CommandIndex = index };
        bar.Search("tab");
        bar.SearchResults.Single().Command.Label.ShouldBe("Insert Table");

        await bar.SubmitSearchAsync();

        ran.ShouldBe(["table"]);
    }

    [Fact]
    public async Task AnUnmatchedQueryGoesToTheHost()
    {
        string? submitted = null;
        var bar = new OfficeTitleBar { CommandIndex = new OfficeCommandIndex() };
        bar.SearchSubmitted += (_, text) => submitted = text;

        bar.Search("quarterly figures");
        await bar.SubmitSearchAsync();

        submitted.ShouldBe("quarterly figures");
    }

    [Fact]
    public void RenamingIgnoresBlanks()
    {
        var renamed = new List<string>();
        var bar = new OfficeTitleBar { DocumentName = "Draft" };
        bar.DocumentRenamed += (_, name) => renamed.Add(name);

        bar.Rename("  ");
        bar.Rename(" Final ");

        bar.DocumentName.ShouldBe("Final");
        renamed.ShouldBe(["Final"]);
    }

    [Fact]
    public void TheStatusBarZoomStepsSnapsAndClamps()
    {
        var bar = new OfficeStatusBar { Zoom = 1 };

        bar.ZoomIn();
        bar.Zoom.ShouldBe(1.1, 1e-9);

        bar.SetZoomFromSlider(0.505);
        bar.Zoom.ShouldBe(1);

        bar.Zoom = 40;
        bar.Zoom.ShouldBe(5);
    }

    [Fact]
    public void DraggingTheFirstLineIndentReportsNewIndents()
    {
        OfficeIndents? reported = null;
        var ruler = new OfficeRuler { PixelsPerPoint = 1, PageOffset = 0 };
        ruler.IndentsChanged += (_, value) => reported = value;

        var start = ruler.Model.MarkerX(OfficeRulerMarker.FirstLineIndent);
        ruler.BeginDrag(start, 0.2).ShouldBe(OfficeRulerMarker.FirstLineIndent);
        ruler.DragTo(start + 36, 0.2);
        ruler.EndDrag();

        reported.ShouldBe(new OfficeIndents(0, 36, 0));
        ruler.Indents.ShouldBe(new OfficeIndents(0, 36, 0));
    }

    [Fact]
    public void TappingTheTextColumnAddsATab()
    {
        var ruler = new OfficeRuler { PixelsPerPoint = 1, PageOffset = 0 };

        ruler.TapAt(ruler.Model.MarginToView(144));

        ruler.TabStops.Single().Position.ShouldBe(144);
    }

    [Fact]
    public void SaveOnTheRailSavesAndCloses()
    {
        var saved = 0;
        var backstage = new OfficeBackstage { IsOpen = true };
        backstage.SaveRequested += (_, _) => saved++;

        backstage.SelectPage(OfficeBackstagePage.Save);

        saved.ShouldBe(1);
        backstage.IsOpen.ShouldBeFalse();
        backstage.SelectedPage.ShouldBe(OfficeBackstagePage.Home);
    }

    [Fact]
    public void ChoosingAnExportFormatRaisesIt()
    {
        OfficeFileFormat? exported = null;
        var backstage = new OfficeBackstage { App = OfficeApp.Excel, IsOpen = true };
        backstage.ExportRequested += (_, format) => exported = format;

        backstage.ChooseExport(OfficeFileFormats.Csv);

        exported.ShouldBe(OfficeFileFormats.Csv);
        backstage.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public void TheShellOpensTheBackstageFromTheFileButton()
    {
        var ribbon = new Ribbon();
        var backstage = new OfficeBackstage();
        var shell = new OfficeShell { Ribbon = ribbon, Backstage = backstage };

        ribbon.ApplicationButtonText.ShouldBe("File");

        ribbon.InvokeApplicationButton();

        shell.IsBackstageOpen.ShouldBeTrue();
        backstage.IsOpen.ShouldBeTrue();

        backstage.Close();
        shell.IsBackstageOpen.ShouldBeFalse();
    }

    [Fact]
    public void TheStatusBarFocusButtonTogglesFocusMode()
    {
        var status = new OfficeStatusBar();
        var shell = new OfficeShell { StatusBar = status };

        shell.ToggleFocusMode();
        shell.IsFocusMode.ShouldBeTrue();
        shell.ToggleFocusMode();
        shell.IsFocusMode.ShouldBeFalse();
    }

    [Fact]
    public void TheStyleGalleryKeepsTheSelectedIdInStep()
    {
        var gallery = new OfficeStyleGallery();
        OfficeStyleDescriptor? picked = null;
        gallery.StyleSelected += (_, style) => picked = style;

        gallery.SelectedStyleId = "Heading2";
        ((OfficeStyleDescriptor)gallery.SelectedItem!).Id.ShouldBe("Heading2");

        gallery.Pick(OfficeStyleDescriptors.Word[0]);
        gallery.SelectedStyleId.ShouldBe("Normal");
        picked!.Id.ShouldBe("Normal");
    }

    [Fact]
    public void RibbonCommandsFeedTheSearch()
    {
        var group = new RibbonGroup { Title = "Tables" };
        var inserted = 0;
        var table = new RibbonButton { Text = "Table" };
        table.Clicked += (_, _) => inserted++;
        group.Items.Add(table);
        var tab = new RibbonTab { Title = "Insert" };
        tab.Groups.Add(group);
        var ribbon = new Ribbon();
        ribbon.Tabs.Add(tab);

        var index = new OfficeCommandIndex();
        index.AddRibbon(ribbon);

        var hit = index.Search("table").Single();
        hit.Command.Category.ShouldBe("Insert › Tables");
        hit.Command.Execute();
        inserted.ShouldBe(1);
    }
}

using Microsoft.AspNetCore.Components;
using Shiny.Blazor.Controls.Office;
using Shiny.Controls.Office.Shell;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// The Blazor Office shell parts, driven through their C# seams with no renderer: what the title bar's
/// search runs, what the status bar's zoom does, what a ruler drag reports and what the backstage raises.
/// </summary>
public class OfficeShellComponentTests
{
    [Fact]
    public async Task TheSearchRunsTheBestMatch()
    {
        var ran = new List<string>();
        var index = new OfficeCommandIndex();
        index.Add("Bold", () => ran.Add("bold"), "Home › Font", "Ctrl+B");
        index.Add("Insert Table", () => ran.Add("table"), "Insert");

        var bar = new OfficeTitleBar { CommandIndex = index };
        bar.SetQuery("tab");
        bar.Results.Single().Command.Label.ShouldBe("Insert Table");

        await bar.SubmitAsync();

        ran.ShouldBe(["table"]);
        bar.Results.ShouldBeEmpty();
    }

    [Fact]
    public async Task AQueryNothingMatchesGoesToTheHost()
    {
        string? submitted = null;
        var bar = new OfficeTitleBar
        {
            CommandIndex = new OfficeCommandIndex(),
            SearchSubmitted = new EventCallback<string>(null, (Action<string>)(x => submitted = x))
        };

        bar.SetQuery("quarterly figures");
        await bar.SubmitAsync();

        submitted.ShouldBe("quarterly figures");
    }

    [Fact]
    public async Task RenamingIgnoresBlanksAndReportsTheNewName()
    {
        var renamed = new List<string>();
        var bar = new OfficeTitleBar
        {
            DocumentName = "Draft",
            DocumentRenamed = new EventCallback<string>(null, (Action<string>)renamed.Add)
        };

        await bar.RenameAsync("   ");
        await bar.RenameAsync(" Final ");

        bar.DocumentName.ShouldBe("Final");
        renamed.ShouldBe(["Final"]);
    }

    [Fact]
    public void TheTitleBarFallsBackToTheAppsNameAndStatus()
    {
        var bar = new OfficeTitleBar { App = OfficeApp.Excel, SaveState = OfficeSaveState.SavedLocally };

        bar.Info.DefaultDocumentName.ShouldBe("Book1");
        bar.SaveStatus.ShouldBe("Saved locally");
        new OfficeTitleBar { SaveState = OfficeSaveState.Saving, SaveStatusText = "Uploading" }.SaveStatus.ShouldBe("Uploading");
    }

    [Fact]
    public void TheShellGoesCompactBelowTheBreakpointOnlyOnce()
    {
        var shell = new OfficeShell();

        shell.ApplyWidth(1200).ShouldBeFalse();   // already the desktop layout
        shell.ApplyWidth(480).ShouldBeTrue();
        shell.ShellLayout.IsCompact.ShouldBeTrue();
        shell.ApplyWidth(470).ShouldBeFalse();
        shell.ApplyWidth(1000).ShouldBeTrue();
    }

    [Fact]
    public async Task TheStatusBarZoomStepsSnapsAndClamps()
    {
        var reported = new List<double>();
        var bar = new OfficeStatusBar { Zoom = 1, ZoomChanged = new EventCallback<double>(null, (Action<double>)reported.Add) };

        await bar.ZoomInAsync();
        bar.Zoom.ShouldBe(1.1, 1e-9);

        await bar.SetZoomFromSliderAsync(0.505);
        bar.Zoom.ShouldBe(1);

        await bar.SetZoomAsync(40);
        bar.Zoom.ShouldBe(5);

        reported.Count.ShouldBe(3);
    }

    [Fact]
    public async Task TheStatusBarOffersTheAppsViewModes()
    {
        var bar = new OfficeStatusBar { App = OfficeApp.PowerPoint };

        bar.EffectiveViewModes.Select(x => x.Id).ShouldBe(["normal", "sorter", "reading"]);
        bar.EffectiveViewMode.ShouldBe("normal");

        await bar.SelectViewModeAsync("sorter");
        bar.EffectiveViewMode.ShouldBe("sorter");
    }

    [Fact]
    public async Task DraggingTheFirstLineIndentReportsNewIndents()
    {
        OfficeIndents? reported = null;
        var ruler = new OfficeRuler
        {
            PixelsPerPoint = 1,
            PageOffset = 0,
            IndentsChanged = new EventCallback<OfficeIndents>(null, (Action<OfficeIndents>)(x => reported = x))
        };

        var start = ruler.Model.MarkerX(OfficeRulerMarker.FirstLineIndent);
        ruler.Begin(start, 3);
        ruler.Move(start + 36, 3);
        await ruler.FlushAsync(keepDragging: false);

        reported.ShouldBe(new OfficeIndents(0, 36, 0));
        ruler.Indents.ShouldBe(new OfficeIndents(0, 36, 0));
    }

    [Fact]
    public async Task ClickingTheTextColumnAddsATabOfTheSelectedKind()
    {
        IReadOnlyList<OfficeTabStop>? reported = null;
        var ruler = new OfficeRuler
        {
            PixelsPerPoint = 1,
            TabAlignment = OfficeTabAlignment.Right,
            TabStopsChanged = new EventCallback<IReadOnlyList<OfficeTabStop>>(null, (Action<IReadOnlyList<OfficeTabStop>>)(x => reported = x))
        };

        await ruler.AddTabAtAsync(ruler.Model.MarginToView(144));

        reported!.Single().ShouldBe(new OfficeTabStop(144, OfficeTabAlignment.Right));
    }

    [Fact]
    public async Task TheBackstageRaisesTheChosenFormatAndCloses()
    {
        OfficeFileFormat? exported = null;
        var closed = 0;
        var backstage = new OfficeBackstage
        {
            App = OfficeApp.Excel,
            IsOpen = true,
            ExportRequested = new EventCallback<OfficeFileFormat>(null, (Action<OfficeFileFormat>)(x => exported = x)),
            Closed = new EventCallback(null, (Action)(() => closed++))
        };

        await backstage.ChooseExportAsync(OfficeFileFormats.Csv);

        exported.ShouldBe(OfficeFileFormats.Csv);
        backstage.IsOpen.ShouldBeFalse();
        closed.ShouldBe(1);
    }

    [Fact]
    public async Task SaveOnTheRailSavesInsteadOfOpeningAPage()
    {
        var saved = 0;
        var backstage = new OfficeBackstage
        {
            IsOpen = true,
            SaveRequested = new EventCallback(null, (Action)(() => saved++))
        };

        await backstage.SelectPageAsync(OfficeBackstagePage.Save);

        saved.ShouldBe(1);
        backstage.SelectedPage.ShouldBe(OfficeBackstagePage.Home);
        backstage.IsOpen.ShouldBeFalse();

        await backstage.SelectPageAsync(OfficeBackstagePage.Info);
        backstage.SelectedPage.ShouldBe(OfficeBackstagePage.Info);
    }

    [Fact]
    public async Task RibbonCommandsFlowIntoTheSearchIndex()
    {
        var ribbon = new Ribbon();
        var tab = new RibbonTab { Title = "Insert", Key = "insert", Ribbon = ribbon };
        var group = new RibbonGroup { Title = "Tables", Ribbon = ribbon, Tab = tab };
        var inserted = 0;
        var button = new RibbonButton { Text = "Table", Ribbon = ribbon, Group = group, Clicked = new EventCallback(null, (Action)(() => inserted++)) };

        var index = new OfficeCommandIndex();
        using var sync = index.SyncRibbon(ribbon);

        ribbon.IndexCommand(button);   // the tab renders after the sync began

        var hit = index.Search("table").Single();
        hit.Command.Category.ShouldBe("Insert › Tables");
        await hit.Command.Execute();
        inserted.ShouldBe(1);
    }

    [Fact]
    public void StylePreviewsCarryTheStylesLook()
    {
        var css = OfficeStyleGallery.SampleStyle(OfficeStyleDescriptors.Word.Single(x => x.Id == "Heading1"));

        css.ShouldContain("font-size:18pt");
        css.ShouldContain("font-family:'Aptos Display'");
        css.ShouldContain("color:#0F4761");
    }

    [Fact]
    public void TheZoomDialogOffersFitsOnlyWhenItKnowsTheSizes()
    {
        var dialog = new OfficeZoomDialog();
        dialog.IsAvailable(OfficeZoomPreset.PageWidth).ShouldBeFalse();
        dialog.IsAvailable(OfficeZoomPreset.Percent75).ShouldBeTrue();

        dialog.PageWidth = 816;
        dialog.ViewportWidth = 1000;
        dialog.IsAvailable(OfficeZoomPreset.PageWidth).ShouldBeTrue();
        dialog.IsAvailable(OfficeZoomPreset.WholePage).ShouldBeFalse();
    }
}

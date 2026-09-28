using Shiny.Controls.Office.Presentation;
using Shiny.Controls.Office.Shell;
using Shiny.Controls.Office.Skia;
using Shiny.Maui.Controls.Office;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>The MAUI SlideEditorView dressed as PowerPoint: the shell's parts, switches and file requests.</summary>
[Collection(ApplicationResourcesCollection.Name)]
public class SlideEditorShellTests
{
    public SlideEditorShellTests() => TestDispatcherProvider.Install();

    [Fact]
    public void TheShellIsOnByDefault_AndEveryPartCanBeSwitchedOff()
    {
        using var view = new SlideEditorView();

        view.Content.ShouldBe(view.Shell);
        view.Shell.App.ShouldBe(OfficeApp.PowerPoint);
        view.TitleBar.IsVisible.ShouldBeTrue();
        view.StatusBar.IsVisible.ShouldBeTrue();
        view.StatusBar.ShowFitToWindow.ShouldBeTrue();
        view.RibbonActions.ShowComments.ShouldBeFalse();
        view.TitleBar.DocumentName.ShouldBe("Presentation1");

        view.ShowShell = false;

        view.TitleBar.IsVisible.ShouldBeFalse();
        view.StatusBar.IsVisible.ShouldBeFalse();
        view.Ribbon.ApplicationButtonText.ShouldBeNull();
    }

    [Fact]
    public async Task ExportRaisesAFileRequestForTheCurrentSlide()
    {
        using var deck = await SlideTemplates.OpenAsync(SlideTemplates.All.Single(x => x.Id == SlideTemplates.PitchId));
        using var view = new SlideEditorView { Deck = deck, DocumentName = "Pitch" };

        SlideFileRequest? request = null;
        view.FileRequested += (_, r) => request = r;

        view.Export(OfficeFileFormats.Pdf);

        request.ShouldNotBeNull();
        request.FileName.ShouldBe("Pitch.pdf");
        request.Action.ShouldBe(SlideFileAction.Export);
        (await request.ToBytesAsync()).Length.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task ViewingModeReachesTheRibbon_AndTheSorterIsAStatusBarButton()
    {
        using var deck = await SlideTemplates.OpenAsync(SlideTemplates.All.Single(x => x.Id == SlideTemplates.ProjectId));
        using var view = new SlideEditorView { Deck = deck };

        view.EditMode = OfficeEditMode.Viewing;
        view.RibbonActions.EditMode.ShouldBe(OfficeEditMode.Viewing);
        view.TitleBar.CanUndo.ShouldBeFalse();

        view.SelectViewMode(OfficeViewModes.Sorter.Id);
        view.ViewMode.ShouldBe(SlideEditorViewMode.SlideSorter);
        view.StatusBar.SelectedViewMode.ShouldBe(OfficeViewModes.Sorter.Id);
    }
}

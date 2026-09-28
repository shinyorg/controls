using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Controls.Office.Document;
using Shiny.Controls.Office.Shell;
using Shiny.Maui.Controls.Office;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// Leftovers from the Office editors' device pass: status-bar touch targets, the ribbon band with the
/// shell and toolbar off, the opening zoom on a narrow screen, and the status bar's zoom range.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class OfficeDeviceLeftoverTests
{
    public OfficeDeviceLeftoverTests() => TestDispatcherProvider.Install();

    static async Task<DocumentEditorView> ViewAsync()
    {
        var document = await WordTemplates.OpenAsync(WordTemplates.All.Single(x => x.Id == WordTemplates.ReportId));
        return new DocumentEditorView { Document = document };
    }

    [Fact]
    public void StatusBarIconButtonsHitAtLeastThirtyTwoByTwentyEight()
    {
        var bar = new OfficeStatusBar { ShowFitToWindow = true };

        // Every icon button (view modes, zoom − / +, fit) sits in a hit area, its drawn button inside.
        var targets = bar.GetVisualTreeDescendants()
            .OfType<Grid>()
            .Where(x => x.Children.Count == 1 && x.Children[0] is Border { InputTransparent: true })
            .ToList();

        targets.Count.ShouldBe(3 + 2 + 1);
        foreach (var target in targets)
        {
            target.MinimumWidthRequest.ShouldBeGreaterThanOrEqualTo(32);
            target.MinimumHeightRequest.ShouldBeGreaterThanOrEqualTo(28);
            target.GestureRecognizers.OfType<TapGestureRecognizer>().ShouldHaveSingleItem();
            ((Border)target.Children[0]).GestureRecognizers.ShouldBeEmpty();
        }
        bar.HeightRequest.ShouldBeGreaterThanOrEqualTo(28);
    }

    [Fact]
    public void AViewModeHitAreaSelectsTheMode()
    {
        var bar = new OfficeStatusBar { App = OfficeApp.Word };
        var target = bar.GetVisualTreeDescendants()
            .OfType<Grid>()
            .First(x => x.Children.Count == 1 && x.Children[0] is Border { InputTransparent: true });

        target.GestureRecognizers.OfType<TapGestureRecognizer>().Single().Command!.Execute(null);

        bar.SelectedViewMode.ShouldBe(OfficeViewModes.For(OfficeApp.Word)[0].Id);
    }

    [Fact]
    public async Task WithTheShellAndToolbarOffTheRibbonLeavesNoBand()
    {
        var view = await ViewAsync();
        var ribbonHost = (VisualElement)view.Shell.Ribbon!.Parent!;
        ribbonHost.IsVisible.ShouldBeTrue();

        view.ShowShell = false;
        view.ShowToolbar = false;

        ribbonHost.IsVisible.ShouldBeFalse();
        ((VisualElement)view.TitleBar.Parent!).IsVisible.ShouldBeFalse();
        ((VisualElement)view.StatusBar.Parent!).IsVisible.ShouldBeFalse();

        view.ShowToolbar = true;
        ribbonHost.IsVisible.ShouldBeTrue();
    }

    [Fact]
    public async Task WordsStatusBarZoomsOverTheEditorsOwnRange()
    {
        var view = await ViewAsync();

        view.StatusBar.ZoomModel.Minimum.ShouldBe(DocumentController.MinimumZoom);
        view.StatusBar.ZoomModel.Maximum.ShouldBe(DocumentController.MaximumZoom);
    }

    [Fact]
    public async Task ANarrowViewportOpensAtPageWidth()
    {
        var view = await ViewAsync();
        var page = view.Controller!.Document.Page.Width;

        ((IView)view.Editor).Arrange(new Rect(0, 0, 390, 800));

        view.Editor.Width.ShouldBe(390);
        view.Zoom.ShouldBe(view.StatusBar.ZoomModel.OpeningZoom(page, 390));
        view.Zoom.ShouldBeLessThan(1);

        // Widening again (rotation) while the fit is still in charge goes back to 100%.
        ((IView)view.Editor).Arrange(new Rect(0, 0, 1400, 800));
        view.Zoom.ShouldBe(1);
    }

    [Fact]
    public async Task AnExplicitZoomIsNeverOverridden()
    {
        var view = await ViewAsync();
        view.Zoom = 1;

        ((IView)view.Editor).Arrange(new Rect(0, 0, 390, 800));

        view.Zoom.ShouldBe(1);
    }

    [Fact]
    public async Task AZoomChosenAfterTheFitEndsIt()
    {
        var view = await ViewAsync();
        ((IView)view.Editor).Arrange(new Rect(0, 0, 390, 800));
        view.Zoom.ShouldBeLessThan(1);

        view.StatusBar.Zoom = 1.5;
        view.Zoom.ShouldBe(1.5);

        ((IView)view.Editor).Arrange(new Rect(0, 0, 380, 800));
        view.Zoom.ShouldBe(1.5);
    }
}

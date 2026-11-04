using Microsoft.Maui.Controls;
using Panel = Shiny.Maui.Controls.FloatingPanel.FloatingPanel;
using Shiny.Maui.Controls.FloatingPanel;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// Dragging the handle. MAUI ends every pan with TotalY = 0, so the panel has to remember how far the
/// finger went from the Running updates - deciding on the Completed value alone, a swipe down never
/// dismissed and the panel always snapped back.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class FloatingPanelDragTests
{
    public FloatingPanelDragTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }


    [Fact]
    public async Task SwipingTheBottomPanelDownClosesIt()
    {
        var panel = await OpenPanel(FloatingPanelPosition.Bottom);

        panel.Drag(GestureStatus.Started, 0);
        panel.Drag(GestureStatus.Running, 200);
        panel.Drag(GestureStatus.Completed, 0);

        panel.IsOpen.ShouldBeFalse();
    }


    [Fact]
    public async Task SwipingTheTopPanelUpClosesIt()
    {
        var panel = await OpenPanel(FloatingPanelPosition.Top);

        panel.Drag(GestureStatus.Started, 0);
        panel.Drag(GestureStatus.Running, -200);
        panel.Drag(GestureStatus.Completed, 0);

        panel.IsOpen.ShouldBeFalse();
    }


    [Fact]
    public async Task ASmallDragSnapsBackOpen()
    {
        var panel = await OpenPanel(FloatingPanelPosition.Bottom);
        var rest = panel.HeightRequest;

        panel.Drag(GestureStatus.Started, 0);
        panel.Drag(GestureStatus.Running, 30);
        panel.HeightRequest.ShouldBe(rest - 30, 0.001, "the panel follows the finger");
        panel.Drag(GestureStatus.Completed, 0);

        panel.IsOpen.ShouldBeTrue();
    }


    [Fact]
    public async Task ALockedPanelIsNeverDismissedByADrag()
    {
        var panel = await OpenPanel(FloatingPanelPosition.Bottom);
        panel.IsLocked = true;

        panel.Drag(GestureStatus.Started, 0);
        panel.Drag(GestureStatus.Running, 200);
        panel.Drag(GestureStatus.Completed, 0);

        panel.IsOpen.ShouldBeTrue();
    }


    /// <summary>
    /// An open panel at rest. The animator is a frozen one stepped to the end, so the open has really
    /// finished - a panel still opening ignores the handle, and every drag here would be a no-op.
    /// </summary>
    static async Task<Panel> OpenPanel(FloatingPanelPosition position)
    {
        var animations = new FrozenAnimationHost();
        var panel = animations.Attach(new Panel
        {
            Position = position,
            HasBackdrop = false,
            PanelContent = new Label { Text = "body" }
        });
        _ = new OverlayHost { Children = { panel } };

        panel.IsOpen = true;
        await animations.Finish();

        panel.IsOpen.ShouldBeTrue();
        panel.HeightRequest.ShouldBeGreaterThan(0, "the open animation ran to its detent");
        return panel;
    }
}

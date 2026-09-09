using Microsoft.Maui.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// <see cref="ZoomPanView"/> and the <c>ZoomPanController</c> underneath it, which
/// <see cref="ImageViewer"/> also runs on. There is no renderer here, so the gestures themselves are
/// out of scope; what is in scope is the state every gesture writes through — the limits, the
/// two-way <see cref="ZoomPanView.ZoomLevel"/>, and which recognizers are on the view at each point,
/// since that is what decides whether the content keeps its own gestures.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ZoomPanViewTests
{
    public ZoomPanViewTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }


    static bool Has<T>(ZoomPanView view) where T : IGestureRecognizer
        // The surface is the transformed view, so it is also the one that carries the gestures.
        => view.Surface.GestureRecognizers.OfType<T>().Any();


    /// <summary>
    /// Zero-length animations. MAUI resolves an IAnimationManager off the target's handler, and a
    /// headless view has none — so an animated zoom throws before it writes anything.
    /// </summary>
    static ZoomPanView Build() => new() { AnimationLength = 0 };


    [Fact]
    public void StartsAtRestWithNoPanRecognizer()
    {
        var view = new ZoomPanView();

        view.ZoomLevel.ShouldBe(1);
        view.IsZoomed.ShouldBeFalse();

        // The whole reason a live control tree survives inside this: at rest the content owns its
        // gestures, because there is nothing to pan to yet.
        Has<PanGestureRecognizer>(view).ShouldBeFalse();
        Has<PinchGestureRecognizer>(view).ShouldBeTrue();
        Has<TapGestureRecognizer>(view).ShouldBeTrue();
    }


    [Fact]
    public void DoubleTapToZoomOffTakesTheRecognizerBackOff()
    {
        var view = new ZoomPanView { DoubleTapToZoom = false };

        // Content that wants double taps of its own must not be competing with one it cannot see.
        Has<TapGestureRecognizer>(view).ShouldBeFalse();

        view.DoubleTapToZoom = true;
        Has<TapGestureRecognizer>(view).ShouldBeTrue();
    }


    [Fact]
    public async Task ZoomingAttachesThePanRecognizerAndResettingRemovesIt()
    {
        var view = Build();
        view.WidthRequest = 200;
        view.HeightRequest = 200;

        await view.ZoomToAsync(2);
        view.IsZoomed.ShouldBeTrue();
        Has<PanGestureRecognizer>(view).ShouldBeTrue();

        await view.ResetZoomAsync();
        view.IsZoomed.ShouldBeFalse();
        Has<PanGestureRecognizer>(view).ShouldBeFalse();
    }


    [Fact]
    public async Task ZoomIsClampedIntoTheLimits()
    {
        var view = Build();
        view.MinZoom = 0.5;
        view.MaxZoom = 3;

        await view.ZoomToAsync(99);
        view.ZoomLevel.ShouldBe(3);

        await view.ZoomToAsync(0.1);
        view.ZoomLevel.ShouldBe(0.5);
    }


    [Fact]
    public async Task BelowNaturalSizeThereIsNothingToPan()
    {
        var view = Build();
        view.MinZoom = 0.5;

        await view.ZoomToAsync(0.5);

        // The limit is half the overhang, which is negative below natural size. Math.Clamp reads
        // that as min > max and throws, so it has to be caught before it gets there.
        view.Surface.Scale.ShouldBe(0.5);
        view.Surface.TranslationX.ShouldBe(0);
        view.Surface.TranslationY.ShouldBe(0);
        view.IsZoomed.ShouldBeFalse();
        Has<PanGestureRecognizer>(view).ShouldBeFalse();
    }


    [Fact]
    public async Task GesturesReportThroughTheTwoWayZoomLevel()
    {
        var reported = new List<double>();
        var view = Build();
        view.ZoomChanged += (_, e) => reported.Add(e.ZoomLevel);

        await view.ZoomToAsync(2.5);

        view.ZoomLevel.ShouldBe(2.5);
        reported.ShouldContain(2.5);
    }


    [Fact]
    public async Task WritingZoomLevelZooms()
    {
        var view = Build();
        view.MaxZoom = 4;

        view.ZoomLevel = 3;

        // The write goes through the controller, so the transform has to follow the property rather
        // than the property simply holding a number nobody applied.
        await Task.Yield();
        view.Surface.Scale.ShouldBe(3);
        view.IsZoomed.ShouldBeTrue();
    }


    [Fact]
    public async Task DisablingDropsTheZoomAndEveryRecognizer()
    {
        var view = Build();
        await view.ZoomToAsync(2);

        view.IsZoomEnabled = false;

        view.Surface.Scale.ShouldBe(1);
        view.ZoomLevel.ShouldBe(1);
        Has<PinchGestureRecognizer>(view).ShouldBeFalse();
        Has<PanGestureRecognizer>(view).ShouldBeFalse();
        Has<TapGestureRecognizer>(view).ShouldBeFalse();

        view.IsZoomEnabled = true;
        Has<PinchGestureRecognizer>(view).ShouldBeTrue();
    }


    [Fact]
    public void ContentIsClippedSoAZoomedChildDoesNotPaintOverItsNeighbours()
    {
        var view = Build();
        view.IsClippedToBounds.ShouldBeTrue();

        // IsClippedToBounds alone does not hold a scaled child in on iOS, so the clip is also an
        // explicit geometry tracked against the control's size.
        view.Layout(new Rect(0, 0, 200, 120));

        var clip = view.Clip.ShouldBeOfType<Microsoft.Maui.Controls.Shapes.RectangleGeometry>();
        clip.Rect.Width.ShouldBe(200);
        clip.Rect.Height.ShouldBe(120);

        // The layout is what actually holds a scaled child in: IsClippedToBounds is honoured by a
        // Layout and ignored on a ContentView.
        view.Content.ShouldBeOfType<Grid>().IsClippedToBounds.ShouldBeTrue();
    }


    [Fact]
    public void TheTransformGoesOnAnInnerSurface()
    {
        var view = Build();
        var content = new Label { Text = "zoom me" };

        view.Content = content;

        // Scaling the ZoomPanView itself would scale its layout slot with it - the control grows out
        // of the space it was given and paints over its neighbours instead of zooming inside a fixed
        // frame. On iOS that showed up as a blank box at 2x.
        // Content -> clipping Grid -> transformed surface -> the caller's content.
        var host = view.Content.ShouldBeOfType<Grid>();
        host.IsClippedToBounds.ShouldBeTrue();
        host.Children.ShouldContain(view.Surface);
        ((ContentView)view.Surface).Content.ShouldBe(content);
    }
}

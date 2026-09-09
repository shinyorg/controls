using Shiny.Blazor.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// <see cref="ZoomPanView"/>'s .NET half. The gestures live in zoom-pan-core.js and there is no
/// browser here, so what these cover is the boundary the component owns: the options it hands JS,
/// and the notify path back — which is where a re-render on every reported frame turns into a loop
/// that leaves the renderer spinning and the whole page unclickable.
/// </summary>
public class ZoomPanViewTests
{
    [Fact]
    public void DefaultsAreTheUnsurprisingOnes()
    {
        var view = new ZoomPanView();

        view.MinZoom.ShouldBe(1);
        view.MaxZoom.ShouldBe(5);
        view.ZoomLevel.ShouldBe(1);
        view.IsZoomed.ShouldBeFalse();
        view.IsZoomEnabled.ShouldBeTrue();
        view.DoubleTapToZoom.ShouldBeTrue();

        // A plain wheel belongs to the page's scrollbar; claiming it by default would take scrolling
        // away from anyone who put a ZoomPanView in the middle of a long page.
        view.WheelMode.ShouldBe(ZoomPanWheelMode.Modifier);
    }


    [Fact]
    public async Task AReportedZoomReachesBothCallbacks()
    {
        var levels = new List<double>();
        var changes = new List<ZoomPanChangedEventArgs>();
        var view = new ZoomPanView
        {
            ZoomLevelChanged = Callback<double>(levels.Add),
            ZoomChanged = Callback<ZoomPanChangedEventArgs>(changes.Add)
        };

        await view.OnZoomChanged(2.5, true);

        view.ZoomLevel.ShouldBe(2.5);
        view.IsZoomed.ShouldBeTrue();
        levels.ShouldBe([2.5]);
        changes.Single().IsZoomed.ShouldBeTrue();
    }


    [Fact]
    public async Task AReportedZoomThatChangedNothingIsSwallowed()
    {
        var levels = new List<double>();
        var changes = new List<ZoomPanChangedEventArgs>();
        var view = new ZoomPanView
        {
            ZoomLevelChanged = Callback<double>(levels.Add),
            ZoomChanged = Callback<ZoomPanChangedEventArgs>(changes.Add)
        };

        // Every callback re-renders the parent, and every render is another chance to call back into
        // JS - so a frame that moved nothing has to stop here rather than start that round trip.
        await view.OnZoomChanged(1, false);

        levels.ShouldBeEmpty();
        changes.ShouldBeEmpty();
    }


    [Fact]
    public async Task PanningWithinTheSameScaleStillReportsTheFlip()
    {
        var changes = new List<ZoomPanChangedEventArgs>();
        var view = new ZoomPanView { ZoomChanged = Callback<ZoomPanChangedEventArgs>(changes.Add) };

        await view.OnZoomChanged(2, true);
        changes.Clear();

        // Same scale, but the zoomed flag going back down is a real change: it is what puts the
        // content's own gestures back.
        await view.OnZoomChanged(2, false);

        changes.Single().IsZoomed.ShouldBeFalse();
        view.IsZoomed.ShouldBeFalse();
    }


    static Microsoft.AspNetCore.Components.EventCallback<T> Callback<T>(Action<T> handler)
        => Microsoft.AspNetCore.Components.EventCallback.Factory.Create(new object(), handler);
}

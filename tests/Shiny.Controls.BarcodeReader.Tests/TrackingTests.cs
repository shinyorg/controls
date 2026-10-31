namespace Shiny.Controls.BarcodeReader.Tests;

public class TrackingTests
{
    static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(33);

    [Fact]
    public void A_checksummed_read_confirms_at_once()
    {
        var tracker = new BarcodeTracker();
        var t = tracker.Update([new TrackObservation(new BoxF(0.4f, 0.4f, 0.2f, 0.1f), CandidateKind.Linear, "5901234123457", Symbology.Ean13, true)], Frame)[0];
        t.IsConfirmed.ShouldBeTrue();
        t.Text.ShouldBe("5901234123457");
    }

    [Fact]
    public void An_unchecked_read_needs_two_frames()
    {
        var tracker = new BarcodeTracker();
        var box = new BoxF(0.4f, 0.4f, 0.2f, 0.1f);
        var t = tracker.Update([new TrackObservation(box, CandidateKind.Linear, "ABC", Symbology.Code39)], Frame)[0];
        t.IsConfirmed.ShouldBeFalse();

        var t2 = tracker.Update([new TrackObservation(box with { X = 0.41f }, CandidateKind.Linear, "ABC", Symbology.Code39)], Frame * 2)[0];
        t2.ShouldBeSameAs(t);
        t2.IsConfirmed.ShouldBeTrue();
    }

    [Fact]
    public void A_moving_code_keeps_its_track_and_a_lost_one_expires()
    {
        var tracker = new BarcodeTracker();
        var a = tracker.Update([new TrackObservation(new BoxF(0.1f, 0.1f, 0.2f, 0.1f), CandidateKind.Linear)], Frame)[0];
        var b = tracker.Update([new TrackObservation(new BoxF(0.13f, 0.11f, 0.2f, 0.1f), CandidateKind.Linear)], Frame * 2)[0];
        b.ShouldBeSameAs(a);

        tracker.Update([], Frame * 2 + tracker.Linger + Frame);
        tracker.Tracks.ShouldBeEmpty();
    }

    [Fact]
    public void Native_scheduling_visits_matrix_first_and_rotates()
    {
        var tracker = new BarcodeTracker();
        var now = Frame;
        var tracks = tracker.Update(
        [
            new TrackObservation(new BoxF(0.1f, 0.1f, 0.1f, 0.1f), CandidateKind.Matrix),
            new TrackObservation(new BoxF(0.6f, 0.6f, 0.1f, 0.1f), CandidateKind.Matrix),
            new TrackObservation(new BoxF(0.3f, 0.8f, 0.2f, 0.05f), CandidateKind.Linear)
        ], now);

        var first = tracker.SelectForNative(1, now, TimeSpan.FromMilliseconds(100));
        first.Count.ShouldBe(1);
        first[0].Kind.ShouldBe(CandidateKind.Matrix);
        first[0].LastNativeAttempt = now;

        var second = tracker.SelectForNative(1, now, TimeSpan.FromMilliseconds(100));
        second[0].ShouldNotBeSameAs(first[0]);
        tracks[2].Misses.ShouldBe(1); // the linear one waits until the engine has failed it twice
    }

    [Fact]
    public void Auto_zoom_steps_in_on_a_small_unread_code_and_returns_when_the_scene_clears()
    {
        var tracker = new BarcodeTracker { Linger = TimeSpan.FromMilliseconds(100) };
        var zoom = new AutoZoomController();
        var current = 1.0;
        var now = TimeSpan.Zero;

        for (var i = 0; i < 15; i++)
        {
            now += Frame;
            tracker.Update([new TrackObservation(new BoxF(0.47f, 0.48f, 0.06f, 0.04f), CandidateKind.Linear, ElementWidth: 0.9f)], now);
            if (zoom.Update(tracker.Tracks, current, 1, 10, 16f / 9, now) is { } z)
                current = z;
        }
        current.ShouldBeGreaterThan(2.0);
        current.ShouldBeLessThanOrEqualTo(4.0);

        // nothing in view for longer than the return delay -> back to 1
        for (var i = 0; i < 80; i++)
        {
            now += Frame;
            tracker.Update([], now);
            if (zoom.Update(tracker.Tracks, current, 1, 10, 16f / 9, now) is { } z)
                current = z;
        }
        current.ShouldBe(1.0, 0.001);
    }

    [Fact]
    public void Auto_zoom_will_not_push_an_edge_code_out_of_frame()
    {
        var tracker = new BarcodeTracker();
        var zoom = new AutoZoomController();
        var now = TimeSpan.Zero;
        double? result = null;
        for (var i = 0; i < 15; i++)
        {
            now += Frame;
            tracker.Update([new TrackObservation(new BoxF(0.02f, 0.45f, 0.06f, 0.04f), CandidateKind.Linear, ElementWidth: 0.9f)], now);
            result ??= zoom.Update(tracker.Tracks, 1, 1, 10, 16f / 9, now);
        }
        result.ShouldBeNull();
    }

    [Fact]
    public void Auto_zoom_stands_down_when_the_user_zooms()
    {
        var tracker = new BarcodeTracker();
        var zoom = new AutoZoomController();
        var now = TimeSpan.Zero;
        var current = 1.0;
        for (var i = 0; i < 12; i++)
        {
            now += Frame;
            tracker.Update([new TrackObservation(new BoxF(0.47f, 0.48f, 0.06f, 0.04f), CandidateKind.Linear, ElementWidth: 0.9f)], now);
            if (zoom.Update(tracker.Tracks, current, 1, 10, 16f / 9, now) is { } z)
                current = z;
        }
        current.ShouldBeGreaterThan(1.0);

        // a pinch moves it somewhere else
        now += Frame;
        tracker.Update([new TrackObservation(new BoxF(0.47f, 0.48f, 0.06f, 0.04f), CandidateKind.Linear, ElementWidth: 0.9f)], now);
        zoom.Update(tracker.Tracks, 1.3, 1, 10, 16f / 9, now).ShouldBeNull();
    }
}

using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Camera.Media;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Camera.Tests;

/// <summary>
/// The modal camera page's chrome contract. Every mode shares one tree and differs by what is visible, so
/// these pin which controls a mode does — and does not — offer, which is otherwise only discoverable by
/// running the app on a device.
/// </summary>
public class MediaCapturePageTests
{
    // the page marshals its chrome updates, so it needs a dispatcher to exist at all
    public MediaCapturePageTests() => TestDispatcherProvider.Install();

    [Fact]
    public void Scan_mode_has_no_capture_button()
    {
        // The point of a scan modal: the camera is simply "on", and a shutter would invite a tap that has no
        // meaning — there is nothing for a still to be the result of. It is absent, not merely hidden, so no
        // later change to a visibility rule can bring it back.
        var page = new MediaCapturePage(MediaCaptureMode.Scan, new MediaScanOptions());

        page.ShutterButton.ShouldBeNull();
    }


    [Fact]
    public void Capture_modes_have_a_shutter()
    {
        foreach (var (mode, options) in new (MediaCaptureMode, MediaCameraOptions)[]
                 {
                     (MediaCaptureMode.Photo, new PhotoCaptureOptions()),
                     (MediaCaptureMode.Video, new VideoCaptureOptions())
                 })
        {
            var page = new MediaCapturePage(mode, options);

            page.ShutterButton.ShouldNotBeNull();
            page.ShutterButton!.IsRecording.ShouldBeFalse();
        }
    }


    [Fact]
    public void Torch_button_is_offered_by_default_and_toggles_the_camera()
    {
        var page = new MediaCapturePage(MediaCaptureMode.Scan, new MediaScanOptions());

        page.TorchButton.IsVisible.ShouldBeTrue();
        page.TorchButton.Icon.ShouldBe(MediaIcon.TorchOff);
        page.Camera.IsTorchOn.ShouldBeFalse();

        // the gesture's Command is the seam — a TapGestureRecognizer's Tapped event cannot be raised here
        page.TorchButton.Tapped.Execute(null);

        page.Camera.IsTorchOn.ShouldBeTrue();
        page.TorchButton.Icon.ShouldBe(MediaIcon.TorchOn);

        page.TorchButton.Tapped.Execute(null);

        page.Camera.IsTorchOn.ShouldBeFalse();
        page.TorchButton.Icon.ShouldBe(MediaIcon.TorchOff);
    }


    [Fact]
    public void Torch_can_start_lit_and_can_be_suppressed()
    {
        var lit = new MediaCapturePage(MediaCaptureMode.Scan, new MediaScanOptions { IsTorchOn = true });
        lit.Camera.IsTorchOn.ShouldBeTrue();
        lit.TorchButton.Icon.ShouldBe(MediaIcon.TorchOn);

        var hidden = new MediaCapturePage(MediaCaptureMode.Scan, new MediaScanOptions { AllowTorch = false });
        hidden.TorchButton.IsVisible.ShouldBeFalse();
    }


    [Fact]
    public void Flash_button_belongs_to_photo_capture_only()
    {
        // Flash fires for a still. A scan or a recording has no shutter moment for it to fire at, and the
        // torch is the control that actually helps there.
        new MediaCapturePage(MediaCaptureMode.Photo, new PhotoCaptureOptions()).FlashButton.IsVisible.ShouldBeTrue();
        new MediaCapturePage(MediaCaptureMode.Scan, new MediaScanOptions()).FlashButton.IsVisible.ShouldBeFalse();
        new MediaCapturePage(MediaCaptureMode.Video, new VideoCaptureOptions()).FlashButton.IsVisible.ShouldBeFalse();
    }


    [Fact]
    public void Flash_cycles_auto_on_off()
    {
        var page = new MediaCapturePage(MediaCaptureMode.Photo, new PhotoCaptureOptions());

        page.Camera.FlashMode.ShouldBe(CameraFlashMode.Auto);
        page.FlashButton.Icon.ShouldBe(MediaIcon.FlashAuto);

        page.FlashButton.Tapped.Execute(null);
        page.Camera.FlashMode.ShouldBe(CameraFlashMode.On);

        page.FlashButton.Tapped.Execute(null);
        page.Camera.FlashMode.ShouldBe(CameraFlashMode.Off);

        page.FlashButton.Tapped.Execute(null);
        page.Camera.FlashMode.ShouldBe(CameraFlashMode.Auto);
    }


    [Fact]
    public void Flip_swaps_the_lens()
    {
        var page = new MediaCapturePage(MediaCaptureMode.Photo, new PhotoCaptureOptions());

        page.Camera.Facing.ShouldBe(CameraFacing.Back);
        page.FlipButton.Tapped.Execute(null);
        page.Camera.Facing.ShouldBe(CameraFacing.Front);
        page.FlipButton.Tapped.Execute(null);
        page.Camera.Facing.ShouldBe(CameraFacing.Back);
    }


    [Fact]
    public void Done_button_is_scan_only_and_can_be_suppressed()
    {
        new MediaCapturePage(MediaCaptureMode.Scan, new MediaScanOptions()).DoneButton.IsVisible.ShouldBeTrue();
        new MediaCapturePage(MediaCaptureMode.Photo, new PhotoCaptureOptions()).DoneButton.IsVisible.ShouldBeFalse();

        var suppressed = new MediaCapturePage(MediaCaptureMode.Scan, new MediaScanOptions { ShowDoneButton = false });
        suppressed.DoneButton.IsVisible.ShouldBeFalse();
    }


    [Fact]
    public async Task Done_completes_the_session_with_no_result()
    {
        var page = new MediaCapturePage(MediaCaptureMode.Scan, new MediaScanOptions());

        page.DoneButton.Tapped.Execute(null);

        (await page.Completion).ShouldBeNull();
        page.WasCancelled.ShouldBeFalse(); // finishing is not cancelling
    }


    [Fact]
    public async Task Close_cancels()
    {
        var page = new MediaCapturePage(MediaCaptureMode.Photo, new PhotoCaptureOptions());

        page.CloseButton.Tapped.Execute(null);

        (await page.Completion).ShouldBeNull();
        page.WasCancelled.ShouldBeTrue();
    }


    [Fact]
    public void Confirmation_panel_starts_hidden()
    {
        // It is built up front and toggled, never added later: on the AppKit head a child added after layout
        // gets no native view and paints nothing, which would make review silently blank on macOS.
        new MediaCapturePage(MediaCaptureMode.Photo, new PhotoCaptureOptions()).ConfirmPanel.IsVisible.ShouldBeFalse();
    }


    [Fact]
    public void Effect_strip_is_opt_in()
    {
        new MediaCapturePage(MediaCaptureMode.Photo, new PhotoCaptureOptions()).EffectStrip.IsVisible.ShouldBeFalse();

        var withPicker = new MediaCapturePage(MediaCaptureMode.Photo, new PhotoCaptureOptions { ShowEffectPicker = true });
        withPicker.EffectStrip.IsVisible.ShouldBeTrue();
    }


    [Fact]
    public void Scan_results_advance_the_count_and_stop_at_the_cap()
    {
        var page = new MediaCapturePage(MediaCaptureMode.Scan, new MediaScanOptions { MaxResults = 2 });

        page.ReportScanResult("first");
        page.ResultCount.ShouldBe(1);
        page.StatusLabel.IsVisible.ShouldBeTrue();
        page.Completion.IsCompleted.ShouldBeFalse();

        page.ReportScanResult("second");
        page.ResultCount.ShouldBe(2);
        page.Completion.IsCompleted.ShouldBeTrue(); // hit the cap: the session ends itself
    }


    [Fact]
    public void Options_reach_the_camera()
    {
        var page = new MediaCapturePage(MediaCaptureMode.Photo, new PhotoCaptureOptions
        {
            Facing = CameraFacing.Front,
            Zoom = 2d,
            Filter = CameraFilter.Noir,
            Quality = PhotoQuality.Balanced,
            AllowZoom = false,
            ScaleMode = PreviewScaleMode.AspectFit
        });

        page.Camera.Facing.ShouldBe(CameraFacing.Front);
        page.Camera.Filter.ShouldBe(CameraFilter.Noir);
        page.Camera.PhotoQuality.ShouldBe(PhotoQuality.Balanced);
        page.Camera.IsPinchToZoomEnabled.ShouldBeFalse();
        page.Camera.ScaleMode.ShouldBe(PreviewScaleMode.AspectFit);
    }


    [Fact]
    public void Requested_zoom_waits_for_the_lens_range()
    {
        // Zoom coerces against MaxZoom, which is 1 until a handler reports the real range. Applying the
        // request eagerly clamps it to 1 and the caller's zoom is silently gone — so it is held until the
        // range arrives, which is what OnZoomRangeChanged simulates here.
        var page = new MediaCapturePage(MediaCaptureMode.Photo, new PhotoCaptureOptions { Zoom = 2d });

        page.Camera.Zoom.ShouldBe(1d);

        page.Camera.OnZoomRangeChanged(1d, 8d);

        page.Camera.Zoom.ShouldBe(2d);
    }


    [Fact]
    public void Max_zoom_caps_what_the_lens_offers()
    {
        var page = new MediaCapturePage(MediaCaptureMode.Photo, new PhotoCaptureOptions { MaxZoom = 4d, Zoom = 10d });

        page.Camera.OnZoomRangeChanged(1d, 32d); // a lens that goes far past the cap

        page.Camera.MaxZoom.ShouldBe(4d);
        page.Camera.Zoom.ShouldBe(4d); // the over-cap request lands at the ceiling, not at 1
    }


    [Fact]
    public void Max_zoom_is_a_ceiling_and_never_raises_a_weaker_lens()
    {
        var page = new MediaCapturePage(MediaCaptureMode.Photo, new PhotoCaptureOptions { MaxZoom = 10d });

        page.Camera.OnZoomRangeChanged(1d, 3d);

        page.Camera.MaxZoom.ShouldBe(3d); // the device's own limit still wins
    }


    [Fact]
    public void Disallowing_zoom_pins_the_range_shut()
    {
        // Not just the gesture: an empty range is what stops a ConfigureCamera hook or a binding zooming
        // past 1x, which "AllowZoom = false" has to mean or it is only a UI hint.
        var page = new MediaCapturePage(MediaCaptureMode.Scan, new MediaScanOptions { AllowZoom = false });

        page.Camera.IsPinchToZoomEnabled.ShouldBeFalse();

        page.Camera.OnZoomRangeChanged(1d, 16d);

        page.Camera.MaxZoom.ShouldBe(1d);

        page.Camera.Zoom = 8d; // coerced back down: there is nowhere to go
        page.Camera.Zoom.ShouldBe(1d);
    }


    [Fact]
    public void Zoom_range_survives_a_second_publish()
    {
        // A lens switch republishes the range, which would undo the cap if it were applied only once.
        var page = new MediaCapturePage(MediaCaptureMode.Photo, new PhotoCaptureOptions { MaxZoom = 4d });

        page.Camera.OnZoomRangeChanged(1d, 32d);
        page.Camera.MaxZoom.ShouldBe(4d);

        page.Camera.OnZoomRangeChanged(1d, 32d);
        page.Camera.MaxZoom.ShouldBe(4d);
    }


    [Fact]
    public void Configure_hooks_run()
    {
        CameraView? seenCamera = null;
        ContentPage? seenPage = null;

        var page = new MediaCapturePage(MediaCaptureMode.Photo, new PhotoCaptureOptions
        {
            ConfigureCamera = c => seenCamera = c,
            ConfigurePage = p => seenPage = p
        });

        seenCamera.ShouldBeSameAs(page.Camera);
        seenPage.ShouldBeSameAs(page);
    }
}

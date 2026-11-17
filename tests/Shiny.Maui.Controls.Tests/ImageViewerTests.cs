using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.Images;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// <see cref="ImageViewer"/> loads through <see cref="ShinyImage"/> on both of its surfaces. These
/// cover the wiring that is easy to get wrong once there are two of them: which one drives the
/// viewer's state and events, and which one is allowed to hold a decoded bitmap.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ImageViewerTests
{
    const string Uri = "https://example.com/photo.png";

    public ImageViewerTests()
    {
        TestDispatcherProvider.Install();

        // A fresh Application per test - Application.Current is process-wide, so anything one test
        // merges would leak into the rest of the collection.
        _ = new Application();
    }


    static ImageViewer Build(StubImageService service) => new()
    {
        // Zero takes the instant path in ShinyImage.ShowImageAsync. A real fade needs an animation
        // manager, which a headless test host does not have.
        FadeInDuration = 0,
        ImageService = service
    };


    // Up to five seconds: it returns as soon as the condition holds, and a loaded test host (the whole
    // suite running in parallel) can take well over a second to finish a load.
    static async Task WaitFor(Func<bool> condition, Func<string>? describe = null)
    {
        for (var i = 0; i < 500 && !condition(); i++)
            await Task.Delay(10);

        condition().ShouldBeTrue("the load never settled" + (describe is null ? "" : ": " + describe()));
    }

    static string Describe(ImageViewer viewer) =>
        $"State={viewer.State} IsLoading={viewer.IsLoading} Progress={viewer.Progress.State} " +
        $"thumbnail State={viewer.thumbnailImage.State} IsLoading={viewer.thumbnailImage.IsLoading} Progress={viewer.thumbnailImage.Progress.State}";


    [Fact]
    public async Task Uri_LoadsThroughTheService_AndMirrorsStateFromTheThumbnail()
    {
        var service = new StubImageService();
        var viewer = Build(service);

        viewer.Uri = Uri;

        // State, IsLoading and Progress are mirrored one property at a time as the thumbnail sets them,
        // and the load completes off the test's thread, so wait for all of them rather than the first.
        await WaitFor(() => viewer.State == ImageLoadState.Loaded && !viewer.IsLoading && viewer.Progress.State == ImageLoadState.Loaded, () => Describe(viewer));

        service.Requested.ShouldBe([Uri]);
        viewer.IsLoading.ShouldBeFalse();
        viewer.LoadError.ShouldBeNull();
        viewer.Progress.State.ShouldBe(ImageLoadState.Loaded);
    }


    [Fact]
    public async Task AProgressReportArrivingAfterTheLoadIsIgnored()
    {
        var service = new StubImageService();
        var viewer = Build(service);

        viewer.Uri = Uri;
        await WaitFor(() => viewer.State == ImageLoadState.Loaded && !viewer.IsLoading, () => Describe(viewer));

        // Progress<T> posts its reports, so one the service raised before returning can be delivered
        // after the load finished. It must not put a loaded image back to Downloading with a spinner.
        service.LastProgress!.Report(new ImageLoadProgress(ImageLoadState.Downloading, 1, 4));
        await Task.Delay(200);

        viewer.State.ShouldBe(ImageLoadState.Loaded);
        viewer.IsLoading.ShouldBeFalse();
        viewer.Progress.State.ShouldBe(ImageLoadState.Loaded);
    }


    [Fact]
    public async Task Uri_RaisesImageLoadedOnce()
    {
        var service = new StubImageService();
        var viewer = Build(service);

        var loaded = new List<ImageLoadedEventArgs>();
        viewer.ImageLoaded += (_, e) => loaded.Add(e);

        viewer.Uri = Uri;
        await WaitFor(() => loaded.Count > 0);

        // The overlay loads the same URI again when it opens. Only the thumbnail reports, otherwise
        // one image is announced twice.
        await Task.Delay(50);
        loaded.Count.ShouldBe(1);
        loaded[0].Uri.ShouldBe(Uri);
    }


    [Fact]
    public async Task Failure_SetsLoadErrorAndRaisesImageFailed()
    {
        var service = new StubImageService { Error = new HttpRequestException("nope") };
        var viewer = Build(service);

        ImageFailedEventArgs? failure = null;
        viewer.ImageFailed += (_, e) => failure = e;

        viewer.Uri = Uri;
        // the error and the event land after State, from the load's own thread
        await WaitFor(() => viewer.State == ImageLoadState.Failed && viewer.LoadError is not null && failure is not null);

        viewer.LoadError.ShouldBeOfType<HttpRequestException>();
        failure.ShouldNotBeNull();
        failure.Uri.ShouldBe(Uri);
    }


    [Fact]
    public async Task ClosedViewer_LeavesTheOverlayEmpty()
    {
        var service = new StubImageService();
        var viewer = Build(service);

        viewer.Uri = Uri;
        await WaitFor(() => viewer.State == ImageLoadState.Loaded);

        // One decoded bitmap per viewer while closed. Assigning the overlay up front would double
        // that for every cell in a gallery, and the overlay is populated on open anyway.
        viewer.thumbnailImage.Uri.ShouldBe(Uri);
        viewer.overlayImage.Uri.ShouldBeNull();
        service.Requested.Count.ShouldBe(1);
    }


    [Fact]
    public void ExplicitSource_NeverReachesTheService()
    {
        var service = new StubImageService();
        var viewer = Build(service);

        viewer.Source = ImageSource.FromFile("local.png");

        service.Requested.ShouldBeEmpty();
        viewer.thumbnailImage.Source.ShouldBe(viewer.Source);
    }


    [Fact]
    public void InputTransparent_TracksWhetherThereIsAnImage()
    {
        var viewer = Build(new StubImageService());

        viewer.InputTransparent.ShouldBeTrue();

        viewer.Uri = Uri;
        viewer.InputTransparent.ShouldBeFalse();

        viewer.Uri = null;
        viewer.InputTransparent.ShouldBeTrue();

        viewer.Source = ImageSource.FromFile("local.png");
        viewer.InputTransparent.ShouldBeFalse();
    }


    [Fact]
    public void LoadingAppearance_ReachesBothImages()
    {
        var viewer = Build(new StubImageService());

        var placeholder = ImageSource.FromFile("placeholder.png");
        var error = ImageSource.FromFile("broken.png");

        viewer.PlaceholderImage = placeholder;
        viewer.ErrorImage = error;
        viewer.RingSize = 24;
        viewer.RingColor = Colors.Red;
        viewer.ShowProgressText = false;
        viewer.CacheEnabled = false;

        foreach (var image in new[] { viewer.thumbnailImage, viewer.overlayImage })
        {
            image.PlaceholderImage.ShouldBe(placeholder);
            image.ErrorImage.ShouldBe(error);
            image.RingSize.ShouldBe(24);
            image.RingColor.ShouldBe(Colors.Red);
            image.ShowProgressText.ShouldBeFalse();
            image.CacheEnabled.ShouldBeFalse();
        }
    }


    [Fact]
    public void Aspect_AppliesToTheThumbnail_AndOverlayAspectToTheOverlay()
    {
        var viewer = Build(new StubImageService());
        viewer.Aspect = Aspect.AspectFill;
        viewer.OverlayAspect = Aspect.Fill;

        viewer.thumbnailImage.Aspect.ShouldBe(Aspect.AspectFill);
        viewer.overlayImage.Aspect.ShouldBe(Aspect.Fill);
    }


    [Fact]
    public void CloseButton_DefaultsToTheGlyph_AndTakesWhateverTextIsGiven()
    {
        var viewer = Build(new StubImageService());

        viewer.closeView.ShouldBeOfType<Button>().Text.ShouldBe("✕");

        viewer.CloseButtonText = "Done";
        viewer.closeView.ShouldBeOfType<Button>().Text.ShouldBe("Done");
    }


    [Fact]
    public void CloseButtonImage_SwapsTheGlyphForArtwork_AndKeepsTheChip()
    {
        var viewer = Build(new StubImageService());
        var artwork = ImageSource.FromFile("close.png");

        viewer.CloseButtonImage = artwork;

        var button = viewer.closeView.ShouldBeOfType<ImageButton>();
        button.Source.ShouldBe(artwork);

        // the target does not move when the glyph is swapped out
        button.WidthRequest.ShouldBe(40);
        button.HeightRequest.ShouldBe(40);
        button.HorizontalOptions.ShouldBe(LayoutOptions.End);
        button.VerticalOptions.ShouldBe(LayoutOptions.Start);

        // dropping it goes back to the text one rather than leaving an empty chip
        viewer.CloseButtonImage = null;
        viewer.closeView.ShouldBeOfType<Button>();
    }


    [Fact]
    public void CloseButtonTemplate_OutranksBothTextAndImage()
    {
        var viewer = Build(new StubImageService());
        viewer.CloseButtonText = "Done";
        viewer.CloseButtonImage = ImageSource.FromFile("close.png");

        viewer.CloseButtonTemplate = new DataTemplate(() => new Label { Text = "x" });

        viewer.closeView.ShouldBeOfType<Label>();
    }


    sealed class StubImageService : IImageService
    {
        static readonly byte[] Payload = [0x89, 0x50, 0x4E, 0x47];

        public List<string> Requested { get; } = [];
        public Exception? Error { get; init; }

        /// <summary>The progress the last request was given, so a test can report through it late.</summary>
        public IProgress<ImageLoadProgress>? LastProgress { get; private set; }

        public Task<ImageResult> GetAsync(
            ImageRequest request,
            IProgress<ImageLoadProgress>? progress = null,
            CancellationToken cancellationToken = default
        )
        {
            this.Requested.Add(request.Uri);
            this.LastProgress = progress;

            if (this.Error is not null)
                return Task.FromResult(ImageResult.Fail(this.Error));

            progress?.Report(new ImageLoadProgress(ImageLoadState.Downloading, Payload.Length, Payload.Length));
            return Task.FromResult(ImageResult.Ok(ImageOrigin.Network, Payload, null, Payload.Length));
        }

        public Task ClearCacheAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ClearCacheAsync(string uri, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<long> GetCacheSizeAsync(CancellationToken cancellationToken = default) => Task.FromResult(0L);

        public Task PrefetchAsync(IEnumerable<string> uris, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

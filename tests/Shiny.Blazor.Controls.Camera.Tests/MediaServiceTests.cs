using System.Diagnostics.CodeAnalysis;
using Microsoft.JSInterop;
using Shiny.Blazor.Controls.Camera;
using Shiny.Blazor.Controls.Camera.Media;
using Shiny.Controls.Camera;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Camera.Tests;

/// <summary>
/// The session rules of the Blazor IMediaService — everything that is not the browser: when a modal may open,
/// how a scan de-duplicates, stops and times out, and that every exit takes the modal down.
/// </summary>
public class MediaServiceTests
{
    [Fact]
    public async Task ScanWithoutAHostExplainsWhatIsMissing()
    {
        var service = new MediaService(new FakeJs());

        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in service.ScanAsync(Request(new Queue<string[]>())))
            {
            }
        });
        ex.Message.ShouldContain("MediaHost");
    }


    [Fact]
    public async Task BlockedCameraNeverOpensTheModal()
    {
        var service = new MediaService(new FakeJs { PermissionState = "denied" });
        service.AttachHost();

        var results = await service.ScanAsync(Request(new Queue<string[]>([["a"]]))).ToListAsync();

        results.ShouldBeEmpty();
        service.ActiveSession.ShouldBeNull();
        (await service.TakePhotoAsync()).ShouldBeNull();
    }


    [Fact]
    public async Task DuplicatesAreFilteredAndMaxResultsEndsTheSession()
    {
        var service = Started(out _);
        var batches = new Queue<string[]>([["a", "a"], ["b"], ["a", "c"], ["d"]]);

        var results = await Collect(service, Request(batches), new MediaScanOptions { MaxResults = 3 });

        results.ShouldBe(["a", "b", "c"]);
        service.ActiveSession.ShouldBeNull();
    }


    [Fact]
    public async Task DuplicatesPassThroughWhenFilteringIsOff()
    {
        var service = Started(out _);
        var batches = new Queue<string[]>([["a", "a"], ["a"]]);

        var results = await Collect(service, Request(batches), new MediaScanOptions { FilterDuplicates = false, MaxResults = 3 });

        results.ShouldBe(["a", "a", "a"]);
    }


    [Fact]
    public async Task IdleTimeoutEndsTheScanWithWhatWasFound()
    {
        var service = Started(out _);
        var batches = new Queue<string[]>([["a"]]);   // then nothing more — Next waits forever

        var results = await Collect(service, Request(batches), new MediaScanOptions { Timeout = TimeSpan.FromMilliseconds(150) });

        results.ShouldBe(["a"]);
        service.ActiveSession.ShouldBeNull();
    }


    [Fact]
    public async Task ClosingTheModalEndsTheScan()
    {
        var service = Started(out _);
        var results = new List<string>();

        var scan = Task.Run(async () =>
        {
            await foreach (var value in service.ScanAsync(Request(new Queue<string[]>([["a"]]))))
                results.Add(value);
        });

        var session = await WaitForSessionAsync(service);
        service.OnCameraStarted(session, new CameraView());
        await WaitUntilAsync(() => results.Count == 1);

        service.Cancel(session);
        await scan.WaitAsync(TimeSpan.FromSeconds(5));

        results.ShouldBe(["a"]);
        service.ActiveSession.ShouldBeNull();
    }


    [Fact]
    public async Task TakingOneResultClosesTheModal()
    {
        var service = Started(out _);

        var first = Task.Run(() => service.ScanAsync(Request(new Queue<string[]>([["a", "b"]]))).FirstOrDefaultAsync().AsTask());
        var session = await WaitForSessionAsync(service);
        service.OnCameraStarted(session, new CameraView());

        (await first.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe("a");
        service.ActiveSession.ShouldBeNull();
    }


    [Fact]
    public async Task ASecondSessionWhileOneIsOpenThrows()
    {
        var service = Started(out _);
        var photo = service.TakePhotoAsync();
        await WaitForSessionAsync(service);

        await Should.ThrowAsync<InvalidOperationException>(() => service.RecordVideoAsync());

        service.Cancel(service.ActiveSession!);
        (await photo.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeNull();
        service.ActiveSession.ShouldBeNull();
    }


    [Fact]
    public async Task CallerCancellationClosesAPhotoSession()
    {
        var service = Started(out _);
        using var cts = new CancellationTokenSource();

        var photo = service.TakePhotoAsync(ct: cts.Token);
        await WaitForSessionAsync(service);
        cts.Cancel();

        (await photo.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeNull();
        service.ActiveSession.ShouldBeNull();
    }


    [Fact]
    public async Task HouseDefaultsReachEveryModal()
    {
        var service = new MediaService(new FakeJs(), new MediaServiceOptions
        {
            ConfigureDefaults = o => o.Title = "Acme"
        });
        service.AttachHost();

        var photo = service.TakePhotoAsync();
        var session = await WaitForSessionAsync(service);

        session.Options.Title.ShouldBe("Acme");
        service.Cancel(session);
        await photo;
    }


    [Fact]
    public async Task EffectPickerStartsOnTheRequestedFilter()
    {
        var service = Started(out _);

        var photo = service.TakePhotoAsync(new PhotoCaptureOptions { ShowEffectPicker = true, Filter = CameraFilter.Sepia });
        var session = await WaitForSessionAsync(service);

        session.SelectedEffect!.Filter.ShouldBe(CameraFilter.Sepia);
        service.SelectEffect(session, session.EffectChoices.First(c => c.Effect is not null));
        session.Effects.Count.ShouldBe(1);

        service.Cancel(session);
        await photo;
    }


    [Fact]
    public void FlipDropsAnExactDeviceSoTheLensActuallyChanges()
    {
        var service = Started(out _);
        var session = new MediaSession(MediaSessionKind.Photo, new PhotoCaptureOptions { CameraId = "cam-1" });

        service.Flip(session);

        session.CameraId.ShouldBeNull();
        session.Facing.ShouldBe(CameraFacing.Front);
    }


    [Fact]
    public async Task LosingTheLastHostEndsAnOpenSession()
    {
        var service = Started(out _);
        var photo = service.TakePhotoAsync();
        await WaitForSessionAsync(service);

        service.DetachHost();

        (await photo.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBeNull();
    }


    [Theory]
    [InlineData("qr_code", BarcodeFormat.QrCode)]
    [InlineData("ean_13", BarcodeFormat.Ean13)]
    [InlineData("upc_e", BarcodeFormat.UpcE)]
    [InlineData("pdf417", BarcodeFormat.Pdf417)]
    [InlineData("something_new", BarcodeFormat.Unknown)]
    public void BrowserFormatNamesMapToTheSharedEnum(string browser, BarcodeFormat expected)
    {
        var code = new CameraBarcode { Format = browser, Value = "123", X = 0.1f, Y = 0.2f, W = 0.3f, H = 0.4f };

        var detected = code.ToDetectedBarcode();

        detected.Format.ShouldBe(expected);
        detected.Value.ShouldBe("123");
        detected.BoundingBox.Width.ShouldBe(0.3f);
    }


    // --- helpers ---------------------------------------------------------------------------------------

    static MediaService Started(out FakeJs js)
    {
        js = new FakeJs();
        var service = new MediaService(js);
        service.AttachHost();
        return service;
    }


    /// <summary>A request that yields the queued batches, then waits (until cancelled) for more.</summary>
    static MediaScanRequest<string> Request(Queue<string[]> batches) => new()
    {
        Analyzer = new BarcodeAnalyzer(),
        Next = async (_, ct) =>
        {
            if (batches.TryDequeue(out var batch))
                return batch;

            await Task.Delay(Timeout.Infinite, ct);
            return [];
        },
        DuplicateKey = v => v,
        Describe = v => v
    };


    static async Task<List<string>> Collect(MediaService service, MediaScanRequest<string> request, MediaScanOptions options)
    {
        var results = new List<string>();
        var scan = Task.Run(async () =>
        {
            await foreach (var value in service.ScanAsync(request, options))
                results.Add(value);
        });

        var session = await WaitForSessionAsync(service);
        service.OnCameraStarted(session, new CameraView());
        await scan.WaitAsync(TimeSpan.FromSeconds(5));
        return results;
    }


    static async Task<MediaSession> WaitForSessionAsync(MediaService service)
    {
        await WaitUntilAsync(() => service.ActiveSession is not null);
        return service.ActiveSession!;
    }


    static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("condition never became true");
            await Task.Delay(10);
        }
    }


    sealed class FakeJs : IJSRuntime
    {
        public string PermissionState { get; init; } = "granted";

        public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, object?[]? args)
            => this.InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => new((TValue)(object)new FakeModule(this));
    }


    sealed class FakeModule(FakeJs js) : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, object?[]? args)
            => this.InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties)] TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => identifier switch
            {
                "cameraPermissionState" => new((TValue)(object)js.PermissionState),
                _ => new(default(TValue)!)
            };

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

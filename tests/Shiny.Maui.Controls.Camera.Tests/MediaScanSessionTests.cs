using Shiny.Controls.Camera;
using Shiny.Maui.Controls.Camera.Media;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Camera.Tests;

/// <summary>
/// A whole <see cref="IMediaService.ScanAsync{T}"/> session, run headless through the service's platform seams:
/// why it ended (<see cref="MediaScanOptions.OnEnded"/>), that the cap is a cap, and that the modal is only ever
/// touched on the UI thread.
/// </summary>
public class MediaScanSessionTests
{
    readonly TestMediaService media = new();
    readonly List<MediaScanEndReason> ended = [];
    Action<string>? push;

    public MediaScanSessionTests() => TestDispatcherProvider.Install();


    [Fact]
    public async Task Done_button_reports_done()
    {
        var (session, page) = this.Start();

        page.DoneButton.Tapped.Execute(null);

        (await session.MoveNextAsync()).ShouldBeFalse();
        this.ended.ShouldBe([MediaScanEndReason.Done]);
    }


    [Fact]
    public async Task Close_button_reports_dismissed()
    {
        var (session, page) = this.Start();

        page.CloseButton.Tapped.Execute(null);

        (await session.MoveNextAsync()).ShouldBeFalse();
        this.ended.ShouldBe([MediaScanEndReason.Dismissed]);
    }


    [Fact]
    public async Task Idle_timeout_reports_timeout()
    {
        var (session, _) = this.Start(new MediaScanOptions { Timeout = TimeSpan.FromMilliseconds(50) });

        (await session.MoveNextAsync()).ShouldBeFalse();
        this.ended.ShouldBe([MediaScanEndReason.Timeout]);
    }


    [Fact]
    public async Task Caller_cancellation_reports_cancelled()
    {
        using var cts = new CancellationTokenSource();
        var (session, _) = this.Start(ct: cts.Token);

        cts.Cancel();

        (await session.MoveNextAsync()).ShouldBeFalse();
        this.ended.ShouldBe([MediaScanEndReason.Cancelled]);
    }


    [Fact]
    public async Task Caller_stopping_reports_stopped_and_tears_the_modal_down()
    {
        var (session, page) = this.Start();
        this.push!("first");

        (await session.MoveNextAsync()).ShouldBeTrue();
        await session.DisposeAsync(); // what FirstOrDefaultAsync does once it has its result

        this.ended.ShouldBe([MediaScanEndReason.Stopped]);
        this.media.Navigation.ModalStack.ShouldBeEmpty();
        page.Camera.Analyzer.ShouldBeNull(); // the native detector is released
    }


    [Fact]
    public async Task Max_results_is_a_cap_even_with_results_still_buffered()
    {
        // all three arrive before the consumer reads any — the cap closes the page after the second, and the
        // third must not leak out after it
        var (session, _) = this.Start(new MediaScanOptions { MaxResults = 2, FilterDuplicates = false });
        this.push!("a");
        this.push!("b");
        this.push!("c");

        var results = new List<string>();
        while (await session.MoveNextAsync())
            results.Add(session.Current);

        results.ShouldBe(["a", "b"]);
        this.ended.ShouldBe([MediaScanEndReason.MaxResults]);
    }


    [Fact]
    public async Task Refused_permission_reports_without_opening_a_modal()
    {
        this.media.Permission = MediaPermissionStatus.Denied;

        var results = await this.media.ScanAsync(this.Request(), this.Options()).ToListAsync();

        results.ShouldBeEmpty();
        this.ended.ShouldBe([MediaScanEndReason.PermissionDenied]);
        this.media.Navigation.Pushed.ShouldBe(0);
    }


    [Fact]
    public async Task No_window_reports_camera_unavailable()
    {
        this.media.HasWindow = false;

        var results = await this.media.ScanAsync(this.Request(), this.Options()).ToListAsync();

        results.ShouldBeEmpty();
        this.ended.ShouldBe([MediaScanEndReason.CameraUnavailable]);
    }


    [Fact]
    public async Task Modal_is_pushed_and_popped_on_the_ui_thread()
    {
        // TestNavigation throws when touched outside OnUiThreadAsync, so this passing is the assertion — a
        // scan started from a background worker or a server request handler builds no UI off the UI thread
        var (session, page) = this.Start();
        page.DoneButton.Tapped.Execute(null);

        (await session.MoveNextAsync()).ShouldBeFalse();

        this.media.Navigation.Pushed.ShouldBe(1);
        this.media.Navigation.ModalStack.ShouldBeEmpty();
    }


    [Fact]
    public void First_finish_wins()
    {
        var page = new MediaCapturePage(MediaCaptureMode.Scan, new MediaScanOptions());

        page.CloseButton.Tapped.Execute(null);
        page.Complete(null, MediaScanEndReason.Stopped); // the service's teardown, after the user closed it

        page.EndReason.ShouldBe(MediaScanEndReason.Dismissed);
    }


    (IAsyncEnumerator<string> Session, MediaCapturePage Page) Start(MediaScanOptions? options = null, CancellationToken ct = default)
    {
        var session = this.media.ScanAsync(this.Request(), this.Options(options), ct).GetAsyncEnumerator(ct);

        // Nothing has run yet: an async iterator starts on the first MoveNextAsync. Every seam answers
        // synchronously, so the page is on the modal stack by the time this returns the pending read.
        var pending = session.MoveNextAsync();
        pending.IsCompleted.ShouldBeFalse();

        return (new PrimedEnumerator(session, pending), (MediaCapturePage)this.media.Navigation.ModalStack.Single());
    }


    MediaScanRequest<string> Request() => new()
    {
        Analyzer = new NullAnalyzer(),
        Subscribe = push => this.push = push,
        DuplicateKey = x => x
    };


    MediaScanOptions Options(MediaScanOptions? options = null)
    {
        options ??= new MediaScanOptions();
        options.OnEnded = this.ended.Add;
        return options;
    }


    /// <summary>Hands back the read <see cref="Start"/> already started before reading on.</summary>
    sealed class PrimedEnumerator(IAsyncEnumerator<string> inner, ValueTask<bool> first) : IAsyncEnumerator<string>
    {
        bool primed = true;

        public string Current => inner.Current;

        public ValueTask<bool> MoveNextAsync()
        {
            if (!this.primed)
                return inner.MoveNextAsync();

            this.primed = false;
            return first;
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }


    sealed class NullAnalyzer : FrameAnalyzer
    {
        public override string Id => "test";
        public override ValueTask<IReadOnlyList<OverlayBox>?> AnalyzeAsync(CameraFrame frame, CancellationToken ct) => new((IReadOnlyList<OverlayBox>?)null);
    }


    sealed class TestMediaService() : MediaService(new MediaServiceOptions())
    {
        public MediaPermissionStatus Permission { get; set; } = MediaPermissionStatus.Granted;
        public bool HasWindow { get; set; } = true;
        public TestNavigation Navigation { get; } = new();

        internal override Task<MediaPermissionStatus> RequestScanPermissionAsync(CancellationToken ct) => Task.FromResult(this.Permission);

        internal override INavigation? ResolveNavigation() => this.HasWindow ? this.Navigation : null;

        internal override async Task<TResult> OnUiThreadAsync<TResult>(Func<Task<TResult>> work)
        {
            this.Navigation.OnUiThread = true;
            try
            {
                return await work();
            }
            finally
            {
                this.Navigation.OnUiThread = false;
            }
        }
    }


    sealed class TestNavigation : INavigation
    {
        readonly List<Page> modals = [];

        public bool OnUiThread { get; set; }
        public int Pushed { get; private set; }

        public IReadOnlyList<Page> ModalStack => this.modals;

        public Task PushModalAsync(Page page, bool animated)
        {
            this.Guard();
            this.modals.Add(page);
            this.Pushed++;
            return Task.CompletedTask;
        }

        public Task<Page> PopModalAsync(bool animated)
        {
            this.Guard();
            var page = this.modals[^1];
            this.modals.RemoveAt(this.modals.Count - 1);
            return Task.FromResult(page);
        }

        void Guard()
        {
            if (!this.OnUiThread)
                throw new InvalidOperationException("The modal stack was touched off the UI thread");
        }

        public IReadOnlyList<Page> NavigationStack => [];
        public Task PushModalAsync(Page page) => this.PushModalAsync(page, true);
        public Task<Page> PopModalAsync() => this.PopModalAsync(true);
        public void InsertPageBefore(Page page, Page before) => throw new NotSupportedException();
        public Task<Page> PopAsync() => throw new NotSupportedException();
        public Task<Page> PopAsync(bool animated) => throw new NotSupportedException();
        public Task PopToRootAsync() => throw new NotSupportedException();
        public Task PopToRootAsync(bool animated) => throw new NotSupportedException();
        public Task PushAsync(Page page) => throw new NotSupportedException();
        public Task PushAsync(Page page, bool animated) => throw new NotSupportedException();
        public void RemovePage(Page page) => throw new NotSupportedException();
    }
}

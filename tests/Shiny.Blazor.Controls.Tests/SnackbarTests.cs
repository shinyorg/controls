using Shiny.Blazor.Controls.Toast;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// Snackbars ride the toast queue, so what is tested here is what they add to it: why each one closed,
/// Replace jumping the queue, the action never firing from a body click, the hover pause, and the
/// loading handle.
/// </summary>
public class SnackbarTests
{
    static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    static async Task<ToastEntry> Showing(ToastService service, string text)
    {
        for (var i = 0; i < 100; i++)
        {
            if (service.ActiveToasts.FirstOrDefault(x => x.Config.Text == text) is { } entry)
                return entry;
            await Task.Delay(10);
        }
        throw new TimeoutException($"'{text}' never showed");
    }


    [Fact]
    public void AnActionLengthensTheDefaultDurationButConfigureStillWins()
    {
        var service = new ToastService();
        SnackbarConfig? withAction = null, overridden = null, plain = null;

        _ = service.SnackbarAsync("a", "Undo", c => withAction = c);
        _ = service.SnackbarAsync("b", "Undo", c => { c.Duration = TimeSpan.FromSeconds(2); overridden = c; });
        _ = service.SnackbarAsync("c", null, c => plain = c);

        withAction!.Duration.ShouldBe(SnackbarConfig.DefaultActionDuration);
        overridden!.Duration.ShouldBe(TimeSpan.FromSeconds(2));
        plain!.Duration.ShouldBe(SnackbarConfig.DefaultDuration);
        plain.DisplayMode.ShouldBe(ToastDisplayMode.Snackbar);
        plain.QueueMode.ShouldBe(ToastQueueMode.Replace);
        plain.TextOverflow.ShouldBe(ToastTextOverflow.MultiLine);
    }


    [Fact]
    public async Task TheActionRunsItsCallbackAndClosesAsActionInvoked()
    {
        var service = new ToastService();
        var ran = false;
        var task = service.SnackbarAsync("Deleted", "Undo", c => c.ActionCallback = () => ran = true);

        service.HandleAction(await Showing(service, "Deleted"));

        (await task.WaitAsync(Wait)).ShouldBe(SnackbarResult.ActionInvoked);
        ran.ShouldBeTrue();
    }


    [Fact]
    public async Task ABodyClickRunsTheTapCallbackNotTheAction()
    {
        var service = new ToastService();
        var action = false;
        var tap = false;
        var task = service.SnackbarAsync("Deleted", "Undo", c =>
        {
            c.ActionCallback = () => action = true;
            c.TapCallback = () => tap = true;
        });

        service.HandleTap(await Showing(service, "Deleted"));

        (await task.WaitAsync(Wait)).ShouldBe(SnackbarResult.Dismissed);
        tap.ShouldBeTrue();
        action.ShouldBeFalse();
    }


    [Fact]
    public async Task ItTimesOut()
    {
        var service = new ToastService();
        var result = await service.SnackbarAsync("x", configure: c => c.Duration = TimeSpan.FromMilliseconds(50)).WaitAsync(Wait);
        result.ShouldBe(SnackbarResult.TimedOut);
    }


    [Fact]
    public async Task ANewSnackbarReplacesTheOneShowing()
    {
        var service = new ToastService();
        var first = service.SnackbarAsync("first", configure: c => c.Duration = TimeSpan.Zero);
        await Showing(service, "first");

        var second = service.SnackbarAsync("second", configure: c => c.Duration = TimeSpan.Zero);

        (await first.WaitAsync(Wait)).ShouldBe(SnackbarResult.Replaced);
        var entry = await Showing(service, "second");
        service.HandleTap(entry);
        (await second.WaitAsync(Wait)).ShouldBe(SnackbarResult.Dismissed);
    }


    [Fact]
    public async Task CancellingAQueuedSnackbarDropsItWithoutShowingIt()
    {
        var service = new ToastService();
        _ = service.ShowAsync("toast", c => c.Duration = TimeSpan.Zero);
        await Showing(service, "toast");

        using var cts = new CancellationTokenSource();
        var queued = service.SnackbarAsync("queued", configure: c => c.QueueMode = ToastQueueMode.Queue, cancellationToken: cts.Token);
        cts.Cancel();

        (await queued.WaitAsync(Wait)).ShouldBe(SnackbarResult.Dismissed);
        service.ActiveToasts.ShouldNotContain(x => x.Config.Text == "queued");
    }


    [Fact]
    public async Task HoverHoldsTheCountdown()
    {
        var service = new ToastService();
        var task = service.SnackbarAsync("held", configure: c => c.Duration = TimeSpan.FromMilliseconds(150));
        var entry = await Showing(service, "held");

        service.Pause(entry);
        entry.IsPaused.ShouldBeTrue();
        await Task.Delay(400);
        task.IsCompleted.ShouldBeFalse("a paused countdown must not dismiss");

        service.Resume(entry);
        (await task.WaitAsync(Wait)).ShouldBe(SnackbarResult.TimedOut);
    }


    [Fact]
    public async Task APlainToastDoesNotPauseUnlessAskedTo()
    {
        var service = new ToastService();
        _ = service.ShowAsync("t", c => c.Duration = TimeSpan.Zero);
        var entry = await Showing(service, "t");

        service.Pause(entry);
        entry.IsPaused.ShouldBeFalse();
    }


    [Fact]
    public async Task TheLoadingHandleUpdatesInPlaceAndClosesOnDispose()
    {
        var service = new ToastService();
        var changes = 0;
        service.OnChanged += () => changes++;

        var loading = await service.LoadingSnackbarAsync("Uploading");
        var config = (SnackbarConfig)service.ActiveToasts.Single().Config;
        config.IsLoading.ShouldBeTrue();
        config.Duration.ShouldBe(TimeSpan.Zero);
        config.DismissOnTap.ShouldBeFalse();
        loading.Progress.ShouldBeNull();

        var before = changes;
        loading.Progress = 1.5;
        loading.Text = "Almost there";
        loading.Progress.ShouldBe(1);
        config.Text.ShouldBe("Almost there");
        changes.ShouldBeGreaterThan(before);

        loading.Dispose();
        (await loading.Closed.WaitAsync(Wait)).ShouldBe(SnackbarResult.Dismissed);
    }
}

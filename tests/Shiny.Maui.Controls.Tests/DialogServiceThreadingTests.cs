using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.Dialogs;
using Shiny.Maui.Controls.Infrastructure;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// <see cref="IDialogService"/> is injected into view models and services, so it gets called from
/// wherever an await happened to resume - a thread-pool thread after <c>ConfigureAwait(false)</c> or
/// inside <c>Task.Run</c>. From there the dialog has to show and the call has to finish exactly as it
/// does from the UI thread. It used to do neither: the overlay was built on the calling thread, the
/// platform rejected it inside the fire-and-forget queue, and the caller awaited forever.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class DialogServiceThreadingTests : IDisposable
{
    static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    readonly ThreadBoundDispatcher ui = new();
    readonly FrozenAnimationHost animations = new();
    ContentPage page = null!;

    public DialogServiceThreadingTests()
    {
        this.ui.Install();
        this.ui.InvokeAsync(() =>
        {
            this.page = new ContentPage { Content = new Label { Text = "Behind the dialog" } };
            this.animations.Attach(this.page);

            // The platforms refuse a view handed to them off the UI thread; this is that check for a
            // headless test, at the moment the dialog joins the page.
            this.page.DescendantAdded += (_, _) => this.ui.VerifyAccess();

            // Built here so the application's dispatcher is this thread's, as in an app.
            var app = new SinglePageApplication(this.page);
            ((IApplication)app).CreateWindow(null);
        }).Wait(Patience).ShouldBeTrue();
    }


    [Fact]
    public async Task AConfirmCalledFromTheThreadPoolShowsAndReturnsTheAnswer()
    {
        var dialogs = Dialogs();

        var answer = Task.Run(() => dialogs.Confirm("Delete item?", "This cannot be undone."));

        var dialog = await this.WaitForDialog();
        await this.ui.InvokeAsync(() => Context(dialog).ConfirmCommand.Execute(null));

        (await this.Settle(answer)).ShouldBeTrue();
        this.ui.Unhandled.ShouldBeEmpty();
    }


    [Fact]
    public async Task AnActionSheetAwaitedAfterConfigureAwaitFalseReturnsTheChoice()
    {
        var dialogs = Dialogs();

        var answer = Task.Run(async () =>
        {
            await Task.Yield();
            await Task.Delay(1).ConfigureAwait(false);
            return await dialogs.ActionSheet("Photo", ["Take Photo", "Choose from Library"], configure: c => c.Animation = DialogAnimation.None);
        });

        var dialog = await this.WaitForDialog();
        await this.ui.InvokeAsync(() => Context(dialog).SelectCommand.Execute("Choose from Library"));

        (await this.Settle(answer)).ShouldBe("Choose from Library");
        this.ui.Unhandled.ShouldBeEmpty();
    }


    /// <summary>The hop must not change anything for the caller that was already on the UI thread.</summary>
    [Fact]
    public async Task AConfirmCalledOnTheUiThreadStillReturnsTheAnswer()
    {
        var dialogs = Dialogs();

        var answer = await this.ui.InvokeAsync(() => dialogs.Confirm("Delete item?", "This cannot be undone."));

        var dialog = await this.WaitForDialog();
        await this.ui.InvokeAsync(() => Context(dialog).CancelCommand.Execute(null));

        (await this.Settle(answer)).ShouldBeFalse();
        this.ui.Unhandled.ShouldBeEmpty();
    }


    /// <summary>
    /// A dialog that cannot be shown has to fail the call. The queue that shows dialogs runs
    /// fire-and-forget, so an exception it lets escape reaches nobody and the caller hangs instead.
    /// </summary>
    [Fact]
    public async Task ADialogThatCannotBeShownFailsTheCallInsteadOfHangingIt()
    {
        // A bare Page has no content to lay an overlay over.
        await this.ui.InvokeAsync(() => Application.Current!.Windows[0].Page = new Page());
        var dialogs = Dialogs();

        var answer = Task.Run(() => dialogs.Alert("Heads up", "There is nowhere to show this."));

        await Should.ThrowAsync<InvalidOperationException>(answer.WaitAsync(Patience));
    }


    /// <summary>
    /// The dialog layer is one of the page's overlay layers. It used to be a grid of the manager's
    /// own that it wrapped the page in, found only as the page's direct content - so on a page that
    /// arrived already wrapped, every dialog nested another wrapper and moved the whole page again.
    /// </summary>
    [Fact]
    public async Task DialogsOnAPageWrappedBeforeRenderShareOneLayer()
    {
        View content = null!;
        await this.ui.InvokeAsync(() =>
        {
            PageOverlay.WrapBeforeRender();
            content = this.page.Content!;
            // A page handler that runs PageHandler.Mapper, carrying the frozen animation manager the
            // dialog's entrance animation needs.
            var handler = new StubPageHandler();
            handler.SetMauiContext(this.animations.Context);
            this.page.Handler = handler;
        });
        var dialogs = Dialogs();

        for (var i = 0; i < 2; i++)
        {
            var answer = Task.Run(() => dialogs.Confirm("Delete item?", "This cannot be undone."));
            var dialog = await this.WaitForDialog();
            await this.ui.InvokeAsync(() => Context(dialog).CancelCommand.Execute(null));
            await this.Settle(answer);
        }

        await this.ui.InvokeAsync(() =>
        {
            var root = this.page.Content.ShouldBeOfType<PageOverlay.ShinyOverlayRoot>();
            root.Children.OfType<PageOverlay.DialogLayer>().Count().ShouldBe(1);
            root.Children.OfType<PageOverlay.ShinyOverlayRoot>().ShouldBeEmpty();
            PageOverlay.ContentOf(root).ShouldBeSameAs(content);
        });
        this.ui.Unhandled.ShouldBeEmpty();
    }


    public void Dispose()
    {
        TestDispatcherProvider.Install();
        this.ui.Dispose();
    }


    static DialogService Dialogs() => new(new DialogOptions { DefaultAnimation = DialogAnimation.None });

    static DialogContext Context(DialogView dialog)
        => ((Grid)dialog.Content).Children
            .OfType<View>()
            .Select(x => x.BindingContext)
            .OfType<DialogContext>()
            .First();


    async Task<DialogView> WaitForDialog()
    {
        var deadline = DateTime.UtcNow + Patience;
        while (DateTime.UtcNow < deadline)
        {
            var dialog = await this.ui.InvokeAsync(() => (this.page.Content as Grid)?.Children
                .OfType<AbsoluteLayout>()
                .SelectMany(x => x.Children.OfType<DialogView>())
                .FirstOrDefault());

            if (dialog is not null)
                return dialog;

            await Task.Delay(10);
        }

        throw new ShouldAssertException("The dialog never appeared on the page.");
    }


    /// <summary>
    /// Steps the exit animation on the UI thread, where a device's ticker fires, until the call returns.
    /// </summary>
    async Task<T> Settle<T>(Task<T> answer)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!answer.IsCompleted && DateTime.UtcNow < deadline)
        {
            await this.ui.InvokeAsync(this.animations.Tick);
            await Task.Delay(10);
        }

        answer.IsCompleted.ShouldBeTrue("The dialog was answered but the call never returned.");
        return await answer;
    }


    sealed class SinglePageApplication(Page page) : Application
    {
        protected override Window CreateWindow(IActivationState? activationState) => new(page);
    }
}

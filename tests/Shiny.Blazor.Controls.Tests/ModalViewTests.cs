using Microsoft.AspNetCore.Components;
using Shiny.Blazor.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// The modal's state machine, driven through its public surface. There is no renderer here, so
/// anything that needs one — the entry transition, the focus trap, <c>Opened</c> — is out of scope;
/// what is in scope is that opening, closing, vetoing and the footer's buttons agree on the state.
/// </summary>
public class ModalViewTests
{
    static EventCallback<T> Handler<T>(Action<T> handler)
        => EventCallback.Factory.Create(new object(), handler);

    static EventCallback<T> AsyncHandler<T>(Func<T, Task> handler)
        => EventCallback.Factory.Create(new object(), handler);


    [Fact]
    public async Task ShowAndCloseFlipIsOpen()
    {
        var modal = new ModalView();

        await modal.ShowAsync();
        modal.IsOpen.ShouldBeTrue();

        (await modal.CloseAsync()).ShouldBeTrue();
        modal.IsOpen.ShouldBeFalse();
    }


    [Fact]
    public async Task ShowAndCloseReportThroughTheBinding()
    {
        var states = new List<bool>();
        var modal = new ModalView { IsOpenChanged = Handler<bool>(states.Add) };

        await modal.ShowAsync();
        await modal.CloseAsync();

        states.ShouldBe([true, false]);
    }


    [Fact]
    public async Task ClosingAnAlreadyClosedModalIsSilent()
    {
        var closes = 0;
        var modal = new ModalView { Closed = Handler<ModalCloseReason>(_ => closes++) };

        (await modal.CloseAsync()).ShouldBeFalse();
        closes.ShouldBe(0);
    }


    [Fact]
    public async Task OpeningAnOpenModalIsSilent()
    {
        var opens = 0;
        var modal = new ModalView { IsOpenChanged = Handler<bool>(_ => opens++) };

        await modal.ShowAsync();
        await modal.ShowAsync();

        opens.ShouldBe(1);
    }


    [Fact]
    public async Task ToggleGoesBothWays()
    {
        var modal = new ModalView();

        await modal.ToggleAsync();
        modal.IsOpen.ShouldBeTrue();

        await modal.ToggleAsync();
        modal.IsOpen.ShouldBeFalse();
    }


    [Fact]
    public async Task ClosedCarriesTheReason()
    {
        var reasons = new List<ModalCloseReason>();
        var modal = new ModalView { Closed = Handler<ModalCloseReason>(reasons.Add) };

        await modal.ShowAsync();
        await modal.CloseAsync(ModalCloseReason.CloseButton);

        await modal.ShowAsync();
        await modal.OnEscapeJs();

        reasons.ShouldBe([ModalCloseReason.CloseButton, ModalCloseReason.Escape]);
    }


    [Fact]
    public async Task ClosingCanVetoTheClose()
    {
        var closed = 0;
        var modal = new ModalView
        {
            Closing = Handler<ModalClosingEventArgs>(e => e.Cancel = e.Reason == ModalCloseReason.Backdrop),
            Closed = Handler<ModalCloseReason>(_ => closed++)
        };

        await modal.ShowAsync();

        (await modal.CloseAsync(ModalCloseReason.Backdrop)).ShouldBeFalse();
        modal.IsOpen.ShouldBeTrue();
        closed.ShouldBe(0);

        (await modal.CloseAsync(ModalCloseReason.CloseButton)).ShouldBeTrue();
        modal.IsOpen.ShouldBeFalse();
        closed.ShouldBe(1);
    }


    [Fact]
    public async Task AVetoedCloseLeavesTheBindingAlone()
    {
        var states = new List<bool>();
        var modal = new ModalView
        {
            Closing = Handler<ModalClosingEventArgs>(e => e.Cancel = true),
            IsOpenChanged = Handler<bool>(states.Add)
        };

        await modal.ShowAsync();
        await modal.CloseAsync();

        states.ShouldBe([true]);
    }


    [Fact]
    public async Task TheBackdropOnlyClosesWhenItIsAllowedTo()
    {
        var modal = new ModalView { CloseOnBackdropClick = false };
        await modal.ShowAsync();

        await modal.OnBackdropClick();
        modal.IsOpen.ShouldBeTrue();

        modal.CloseOnBackdropClick = true;
        await modal.OnBackdropClick();
        modal.IsOpen.ShouldBeFalse();
    }


    [Fact]
    public async Task EscapeOnlyClosesWhenItIsAllowedTo()
    {
        var modal = new ModalView { CloseOnEscape = false };
        await modal.ShowAsync();

        await modal.OnEscapeJs();
        modal.IsOpen.ShouldBeTrue();
    }


    [Fact]
    public async Task AFooterButtonRunsItsHandlerThenCloses()
    {
        var log = new List<string>();
        var button = new ModalButton("Save") { OnClick = () => { log.Add("click"); return Task.CompletedTask; } };
        var modal = new ModalView
        {
            Buttons = [button],
            Closed = Handler<ModalCloseReason>(r => log.Add("closed:" + r))
        };

        await modal.ShowAsync();
        await modal.OnFooterButtonClick(button);

        log.ShouldBe(["click", "closed:" + ModalCloseReason.Button]);
        modal.IsOpen.ShouldBeFalse();
    }


    [Fact]
    public async Task AFooterButtonThatDoesNotCloseLeavesItOpen()
    {
        var clicks = 0;
        var button = new ModalButton("Apply")
        {
            ClosesModal = false,
            OnClick = () => { clicks++; return Task.CompletedTask; }
        };
        var modal = new ModalView { Buttons = [button] };

        await modal.ShowAsync();
        await modal.OnFooterButtonClick(button);

        clicks.ShouldBe(1);
        modal.IsOpen.ShouldBeTrue();
    }


    [Fact]
    public async Task ADisabledFooterButtonDoesNothing()
    {
        var clicks = 0;
        var button = new ModalButton("Save")
        {
            Disabled = true,
            OnClick = () => { clicks++; return Task.CompletedTask; }
        };
        var modal = new ModalView { Buttons = [button] };

        await modal.ShowAsync();
        await modal.OnFooterButtonClick(button);

        clicks.ShouldBe(0);
        modal.IsOpen.ShouldBeTrue();
    }


    [Fact]
    public async Task AFooterButtonIsSubjectToTheVetoLikeAnythingElse()
    {
        var button = new ModalButton("Save");
        var modal = new ModalView
        {
            Buttons = [button],
            Closing = Handler<ModalClosingEventArgs>(e => e.Cancel = true)
        };

        await modal.ShowAsync();
        await modal.OnFooterButtonClick(button);

        modal.IsOpen.ShouldBeTrue();
    }


    [Fact]
    public async Task AnAwaitedButtonHandlerFinishesBeforeTheCloseRuns()
    {
        var log = new List<string>();
        var button = new ModalButton("Save")
        {
            OnClick = async () =>
            {
                await Task.Yield();
                log.Add("saved");
            }
        };
        var modal = new ModalView
        {
            Buttons = [button],
            Closing = AsyncHandler<ModalClosingEventArgs>(_ => { log.Add("closing"); return Task.CompletedTask; })
        };

        await modal.ShowAsync();
        await modal.OnFooterButtonClick(button);

        log.ShouldBe(["saved", "closing"]);
    }


    [Fact]
    public async Task MaximizingReportsThroughTheBinding()
    {
        var states = new List<bool>();
        var modal = new ModalView { IsMaximizedChanged = Handler<bool>(states.Add) };

        await modal.ShowAsync();
        await modal.OnMaximizeClick();
        await modal.OnMaximizeClick();

        states.ShouldBe([true, false]);
        modal.IsMaximized.ShouldBeFalse();
    }


    [Fact]
    public async Task DoubleClickingTheHeaderMaximizesWhenItIsAllowedTo()
    {
        var modal = new ModalView { AllowMaximize = true };
        await modal.ShowAsync();

        await modal.OnHeaderDoubleClick();
        modal.IsMaximized.ShouldBeTrue();

        await modal.OnHeaderDoubleClick();
        modal.IsMaximized.ShouldBeFalse();
    }


    [Fact]
    public async Task TheMaximizeButtonIsEnoughOnItsOwnToAllowIt()
    {
        var modal = new ModalView { ShowMaximizeButton = true };
        await modal.ShowAsync();

        await modal.OnHeaderDoubleClick();

        modal.IsMaximized.ShouldBeTrue();
    }


    [Fact]
    public async Task DoubleClickingDoesNothingWhenMaximizingIsOff()
    {
        var modal = new ModalView();
        await modal.ShowAsync();

        await modal.OnHeaderDoubleClick();

        modal.IsMaximized.ShouldBeFalse();
    }


    [Fact]
    public async Task DoubleClickingCanBeTurnedOffOnItsOwn()
    {
        var modal = new ModalView { ShowMaximizeButton = true, MaximizeOnHeaderDoubleClick = false };
        await modal.ShowAsync();

        await modal.OnHeaderDoubleClick();
        modal.IsMaximized.ShouldBeFalse();

        // The button still works - only the double-click was turned off.
        await modal.OnMaximizeClick();
        modal.IsMaximized.ShouldBeTrue();
    }


    [Fact]
    public async Task MaximizingToWhereItAlreadyIsIsSilent()
    {
        var changes = 0;
        var modal = new ModalView { IsMaximized = true, IsMaximizedChanged = Handler<bool>(_ => changes++) };

        await modal.SetMaximizedAsync(true);

        changes.ShouldBe(0);
    }


    // ---------------------------------------------------------------------------------------------
    // Long content. The footer holds the buttons that answer the modal, so which element is allowed
    // to scroll is not cosmetic - the class the panel carries decides whether those buttons survive
    // content taller than the screen.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ScrollBodyIsOnByDefaultSoTheFooterStaysPut()
    {
        var modal = new ModalView();

        modal.PanelClasses.ShouldContain("scroll-body");
        modal.RootClasses.ShouldNotContain("grow-body");
    }


    [Fact]
    public void ScrollBodyOffMovesTheScrollToTheLayer()
    {
        var modal = new ModalView { ScrollBody = false };

        // Both halves matter: the panel must stop capping itself, and something else has to scroll -
        // capping without scrolling is how the overflow used to be clipped away silently.
        modal.PanelClasses.ShouldNotContain("scroll-body");
        modal.RootClasses.ShouldContain("grow-body");
    }


    [Fact]
    public void ContentMaxHeightCapsTheBodyRatherThanThePanel()
    {
        var modal = new ModalView { ContentMaxHeight = "320px" };

        modal.PanelStyle.ShouldContain("--shiny-modal-content-max-height:320px");
        // ...and not as the panel's own cap. Checked with the leading ';' so the custom property,
        // which ends in the same characters, does not satisfy it.
        modal.PanelStyle.ShouldNotContain(";max-height:");
        modal.PanelStyle.ShouldNotStartWith("max-height:");
    }


    [Fact]
    public void ContentMaxHeightScrollsEvenWithScrollBodyOff()
    {
        var modal = new ModalView { ScrollBody = false, ContentMaxHeight = "320px" };

        // Asking for a cap is asking for the content to stop there. Honouring the cap without the
        // scroll would just hide everything past it.
        modal.PanelClasses.ShouldContain("scroll-body");
        modal.RootClasses.ShouldNotContain("grow-body");
    }


    [Fact]
    public void MaxHeightStillCapsTheWholePanel()
    {
        var modal = new ModalView { MaxHeight = "50vh" };

        modal.PanelStyle.ShouldContain("max-height:50vh");
    }
}

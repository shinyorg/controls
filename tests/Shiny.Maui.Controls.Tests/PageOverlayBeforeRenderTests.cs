using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Shiny.Maui.Controls.Infrastructure;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The overlay root goes in as the page's handler maps its content, so the first toast (or dialog,
/// or tooltip) on a page that is already showing finds it in place. Installing it at that toast
/// re-parented the page's content: on Android the whole page went through a native detach and
/// re-attach, every binding under it saw a null context and back, and one frame drew without the
/// window insets.
/// </summary>
public class PageOverlayBeforeRenderTests
{
    public PageOverlayBeforeRenderTests() => PageOverlay.WrapBeforeRender();


    [Fact]
    public void TheRootIsInPlaceOnceThePageHasItsHandler()
    {
        var content = new VerticalStackLayout { new Label() };
        var page = new ContentPage { Content = content };

        page.Handler = new StubPageHandler();

        var root = page.Content.ShouldBeOfType<PageOverlay.ShinyOverlayRoot>();
        PageOverlay.ContentOf(root).ShouldBeSameAs(content);
    }


    [Fact]
    public void TheFirstToastOnARenderedPageDoesNotMoveItsContent()
    {
        var content = new VerticalStackLayout { new Label() };
        var page = new ContentPage { Content = content, BindingContext = new object() };
        page.Handler = new StubPageHandler();

        var moves = 0;
        var contextChanges = 0;
        content.ParentChanged += (_, _) => moves++;
        content.BindingContextChanged += (_, _) => contextChanges++;

        // What ToastManager does for the first toast on a page.
        PageOverlay.GetOrCreateLayer<PageOverlay.ToastLayer>(PageOverlay.GetOrCreateRoot(page), 9_999);

        moves.ShouldBe(0);
        contextChanges.ShouldBe(0);
        content.BindingContext.ShouldBeSameAs(page.BindingContext);
    }


    [Fact]
    public void ContentSetAfterTheHandlerIsWrappedToo()
    {
        var page = new ContentPage();
        page.Handler = new StubPageHandler();
        page.Content.ShouldBeNull();

        var content = new Grid();
        page.Content = content;

        var root = page.Content.ShouldBeOfType<PageOverlay.ShinyOverlayRoot>();
        PageOverlay.ContentOf(root).ShouldBeSameAs(content);
    }


    [Fact]
    public void AnExistingRootIsNotWrappedAgain()
    {
        var page = new ContentPage { Content = new Grid() };
        var root = PageOverlay.GetOrCreateRoot(page);

        page.Handler = new StubPageHandler();

        page.Content.ShouldBeSameAs(root);
        root.Children.OfType<PageOverlay.ShinyOverlayRoot>().ShouldBeEmpty();
    }


    sealed class StubPageHandler() : ViewHandler<IContentView, object>(PageHandler.Mapper), IPageHandler
    {
        protected override object CreatePlatformView() => new();
    }
}

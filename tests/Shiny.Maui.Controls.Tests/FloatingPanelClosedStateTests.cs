using Microsoft.Maui.Controls;
using Panel = Shiny.Maui.Controls.FloatingPanel.FloatingPanel;
using Shiny.Maui.Controls.FloatingPanel;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// What a closed panel leaves on the page. A closed panel is either nothing or just its peeking header —
/// never the body. The body left on screen under a peeking header is a panel that covers the page and
/// takes every tap and scroll meant for it, which is what <c>IsContentScrollEnabled="False"</c> did:
/// the closed state hid the ScrollView, and with scrolling off the body is not inside one.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class FloatingPanelClosedStateTests
{
    public FloatingPanelClosedStateTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }


    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AClosedPanelWithAPeekingHeaderShowsOnlyTheHeader(bool contentScrolls)
    {
        var header = new Label { Text = "header" };
        var body = new Label { Text = "body" };

        var panel = new Panel
        {
            Position = FloatingPanelPosition.BottomTabs,
            HeaderTemplate = header,
            ShowHeaderWhenClosed = true,
            PanelContent = body,
            IsContentScrollEnabled = contentScrolls
        };

        panel.IsVisible.ShouldBeTrue("the header peeks");
        IsShowing(header).ShouldBeTrue();
        IsShowing(body).ShouldBeFalse();
    }


    /// <summary>
    /// The order a page sets its properties in is not the panel's business: turning scrolling off before
    /// the header is declared has to land in the same place as turning it off after.
    /// </summary>
    [Fact]
    public void TurningScrollingOffBeforeTheHeaderStillHidesTheBody()
    {
        var header = new Label { Text = "header" };
        var body = new Label { Text = "body" };

        var panel = new Panel
        {
            IsContentScrollEnabled = false,
            PanelContent = body,
            HeaderTemplate = header,
            ShowHeaderWhenClosed = true
        };

        IsShowing(header).ShouldBeTrue();
        IsShowing(body).ShouldBeFalse();
    }


    [Fact]
    public void TogglingScrollingWhileClosedKeepsTheBodyHidden()
    {
        var header = new Label { Text = "header" };
        var body = new Label { Text = "body" };
        var panel = new Panel
        {
            HeaderTemplate = header,
            ShowHeaderWhenClosed = true,
            PanelContent = body
        };

        panel.IsContentScrollEnabled = false;
        IsShowing(body).ShouldBeFalse();

        panel.IsContentScrollEnabled = true;
        IsShowing(body).ShouldBeFalse();
    }


    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AClosedPanelWithoutAHeaderIsNotOnThePage(bool contentScrolls)
    {
        var panel = new Panel
        {
            PanelContent = new Label { Text = "body" },
            IsContentScrollEnabled = contentScrolls
        };

        panel.IsVisible.ShouldBeFalse();
    }


    /// <summary>
    /// Content that scrolls itself sits straight in the panel, not inside a second ScrollView — nested
    /// scroll views collapse the inner one.
    /// </summary>
    [Fact]
    public void WithScrollingOffTheBodyIsNotWrappedInAScrollView()
    {
        var body = new CollectionView();
        _ = new Panel { PanelContent = body, IsContentScrollEnabled = false };

        Ancestors(body).OfType<ScrollView>().ShouldBeEmpty();
    }


    /// <summary>Every ancestor between the view and the panel has to be visible for it to be on screen.</summary>
    static bool IsShowing(View view)
    {
        Element? current = view;
        while (current is not null)
        {
            if (current is View v && !v.IsVisible)
                return false;

            if (current is Panel)
                return true;

            current = current.Parent;
        }
        return true;
    }


    static IEnumerable<Element> Ancestors(Element element)
    {
        var current = element.Parent;
        while (current is not null and not Panel)
        {
            yield return current;
            current = current.Parent;
        }
    }
}

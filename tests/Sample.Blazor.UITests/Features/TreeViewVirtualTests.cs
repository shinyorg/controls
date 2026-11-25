namespace Sample.Blazor.UITests.Features;

/// <summary>
/// The virtualized tree (51,000 rows once expanded) keeps only a window of rows in the DOM, scrolls
/// on its own, and keeps keyboard focus and selection working for rows that are not rendered.
/// </summary>
public class TreeViewVirtualTests : BlazorTest
{
    ILocator Tree => Page.Locator(".shiny-treeview-virtual");
    ILocator Items => Tree.Locator("[role=treeitem]");
    ILocator Item(string name) => Tree.Locator("[role=treeitem]", new() { HasText = name });
    ILocator Status => Page.Locator("p.state");

    async Task ExpandAll()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Expand all", Exact = true }).ClickAsync();
        await Expect(Status).ToContainTextAsync("Expanded 51,000 rows");
    }

    Task<int> ScrollHeight() => Tree.EvaluateAsync<int>("el => el.scrollHeight");

    [Test]
    public async Task ExpandAll_KeepsOnlyAWindowOfRowsInTheDom()
    {
        await Open("treeview-virtual");
        await Expect(Item("Folder 0001")).ToBeVisibleAsync();

        await ExpandAll();
        await Expect(Item("file-0001-01.txt")).ToBeVisibleAsync();

        var rendered = await Items.CountAsync();
        Console.WriteLine($"rendered treeitems after expand-all: {rendered}");
        await Assert.That(rendered).IsLessThan(200);
        // 51,000 rows x 30px: the scroller reserves the full extent.
        await Assert.That(await ScrollHeight()).IsGreaterThan(51_000 * 30 - 100);
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Scrolling_ToTheEnd_RendersTheLastRows()
    {
        await Open("treeview-virtual");
        await ExpandAll();

        await Tree.EvaluateAsync("el => el.scrollTop = el.scrollHeight");
        await Expect(Item("file-1000-50.txt")).ToBeVisibleAsync();
        await Expect(Item("Folder 0001")).ToHaveCountAsync(0);
        await Assert.That(await Items.CountAsync()).IsLessThan(200);

        // The file rows sit one level down.
        await Expect(Item("file-1000-50.txt")).ToHaveAttributeAsync("aria-level", "2");
        await Expect(Item("file-1000-50.txt")).ToHaveAttributeAsync("aria-posinset", "50");
        await Expect(Item("file-1000-50.txt")).ToHaveAttributeAsync("aria-setsize", "50");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Keyboard_End_ScrollsTheFocusedRowIntoView()
    {
        await Open("treeview-virtual");
        await ExpandAll();

        await Tree.FocusAsync();
        await Page.Keyboard.PressAsync("ArrowDown"); // first key focuses the first row
        await Page.Keyboard.PressAsync("End");
        await Expect(Tree.Locator(".shiny-treeview-row.focused")).ToContainTextAsync("file-1000-50.txt");
        await Expect(Tree.Locator(".shiny-treeview-row.focused")).ToBeInViewportAsync();

        await Page.Keyboard.PressAsync("Home");
        await Expect(Tree.Locator(".shiny-treeview-row.focused")).ToContainTextAsync("Folder 0001");
        await Expect(Tree.Locator(".shiny-treeview-row.focused")).ToBeInViewportAsync();
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Selection_SurvivesTheRowScrollingOutAndBack()
    {
        await Open("treeview-virtual");
        await ExpandAll();

        await Item("file-0001-03.txt").Locator(".shiny-treeview-row").ClickAsync();
        await Expect(Item("file-0001-03.txt")).ToHaveAttributeAsync("aria-selected", "true");

        await Tree.EvaluateAsync("el => el.scrollTop = 600000");
        await Expect(Item("file-0001-03.txt")).ToHaveCountAsync(0);
        await Tree.EvaluateAsync("el => el.scrollTop = 0");
        await Expect(Item("file-0001-03.txt")).ToHaveAttributeAsync("aria-selected", "true");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task TheTreeIsTheOnlyScroller()
    {
        await Open("treeview-virtual");
        await ExpandAll();

        // The page itself must not scroll (Jon rule 1): the tree owns the axis.
        var pageScrolls = await Page.EvaluateAsync<bool>(
            "() => { const c = document.querySelector('.shiny-applayout-content') ?? document.scrollingElement; return c.scrollHeight > c.clientHeight + 1; }");
        await Assert.That(pageScrolls).IsFalse();
        await Screenshot("treeview-virtual-expanded");
    }
}

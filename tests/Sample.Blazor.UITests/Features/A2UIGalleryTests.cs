using System.Text.RegularExpressions;

namespace Sample.Blazor.UITests.Features;

/// <summary>
/// The A2UI gallery: each sample surface (the Shiny-catalog dashboard, the basic form, the kitchen
/// sink and every one of its tabs) renders without a console error, and what a surface sends to the
/// agent lands in the outbound log.
/// </summary>
public class A2UIGalleryTests : BlazorTest
{
    ILocator Tab(string name) => Page.Locator(".a2ui-gallery-tabs").GetByRole(AriaRole.Tab, new() { Name = name, Exact = true });
    ILocator Surface => Page.Locator(".a2ui-gallery-surface");
    ILocator Log => Page.Locator(".a2ui-gallery-log");

    async Task OpenGallery()
    {
        // the kitchen sink's media is on the internet; the test never depends on it
        await Page.RouteAsync(new Regex(@"^https://(interactive-examples\.mdn\.mozilla\.net|a2ui\.org|picsum\.photos|images\.unsplash\.com)/"), r => r.AbortAsync());
        await Open("a2ui-gallery");
        await Expect(Surface).ToContainTextAsync("October sales");
    }

    [Test]
    public async Task Every_Sample_Surface_Renders()
    {
        await OpenGallery();

        await Expect(Tab("Dashboard (Shiny catalog)")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(Surface.Locator(".a2ui-datagrid tbody tr")).ToHaveCountAsync(3);

        await Tab("Form").ClickAsync();
        await Expect(Tab("Form")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(Surface).ToContainTextAsync("Shiny newsletter");
        await Expect(Surface.GetByLabel("Email")).ToBeVisibleAsync();

        await Tab("Kitchen sink").ClickAsync();
        await Expect(Surface).ToContainTextAsync("Kitchen sink");
        var inner = Surface.Locator(".a2ui-tabs-strip [role=tab]");
        await Expect(inner).ToHaveTextAsync(["Display", "Layout", "Inputs", "Media"]);
        for (var i = 0; i < 4; i++)
        {
            await inner.Nth(i).ClickAsync();
            await Expect(inner.Nth(i)).ToHaveAttributeAsync("aria-selected", "true");
            await Expect(Surface.Locator(".a2ui-tabs [role=tabpanel], .a2ui-tabs-panel").First).Not.ToBeEmptyAsync();
        }
        await Expect(Surface.Locator("video")).ToHaveCountAsync(1);
        await Expect(Surface.Locator("audio")).ToHaveCountAsync(1);

        // WebKit's media controls log their own icon loads failing when the media is blocked - the browser's chrome, not the page
        await AssertNoConsoleErrors(@"^Button failed to load, iconName = ");
    }

    [Test]
    public async Task Grid_Row_Action_Lands_In_The_Log_And_Reset_Clears_It()
    {
        await OpenGallery();
        await Expect(Log).ToContainTextAsync("Actions and errors the renderer sends");

        await Surface.Locator(".a2ui-datagrid tbody tr", new() { HasTextString = "A-1002" }).ClickAsync();
        await Expect(Log.Locator(".a2ui-gallery-line").First).ToContainTextAsync("openOrder");
        await Expect(Log.Locator(".a2ui-gallery-line").First).ToContainTextAsync("A-1002");

        await Page.Locator(".a2ui-gallery-reset").ClickAsync();
        await Expect(Log.Locator(".a2ui-gallery-line")).ToHaveCountAsync(0);
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Form_Checks_Then_Sends_Subscribe()
    {
        await OpenGallery();
        await Tab("Form").ClickAsync();

        var subscribe = Surface.GetByRole(AriaRole.Button, new() { Name = "Subscribe" });
        var name = Surface.GetByLabel("Name");
        await name.FillAsync("x");
        await name.FillAsync("");
        await Expect(Surface.GetByRole(AriaRole.Alert)).ToHaveTextAsync("Tell me your name");
        await name.FillAsync("Grace");
        await Surface.GetByLabel("Email").FillAsync("grace@example.com");
        await Expect(Surface.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
        await subscribe.ClickAsync();

        await Expect(Log.Locator(".a2ui-gallery-line").First).ToContainTextAsync("subscribe");
        await Expect(Log.Locator(".a2ui-gallery-line").First).ToContainTextAsync("grace@example.com");
        await AssertNoConsoleErrors();
    }
}

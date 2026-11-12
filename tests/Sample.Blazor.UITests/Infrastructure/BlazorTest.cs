using System.Text.RegularExpressions;

namespace Sample.Blazor.UITests;

/// <summary>
/// Base for every Blazor UI test: a fresh page per test in the shared browser, with every console
/// error and uncaught exception recorded so a test can assert the page stayed clean. On failure the
/// page is screenshotted and its console saved next to the results.
/// </summary>
public abstract class BlazorTest
{
    [ClassDataSource<BlazorAppFixture>(Shared = SharedType.PerTestSession)]
    public required BlazorAppFixture App { get; init; }

    readonly List<string> consoleErrors = [];
    readonly List<string> consoleLog = [];

    protected IPage Page { get; private set; } = null!;

    TouchInput? touch;

    /// <summary>A finger: real touch events through the browser's input pipeline (Chromium only).</summary>
    protected TouchInput Touch => touch ??= new TouchInput(Page);

    /// <summary>Console errors and uncaught page exceptions seen so far in this test.</summary>
    protected IReadOnlyList<string> ConsoleErrors => consoleErrors;

    /// <summary>True opens this class's pages on a phone, even in a desktop run.</summary>
    protected virtual bool Phone => false;

    [Before(Test)]
    public async Task OpenPage()
    {
        var context = Phone ? await App.GetPhoneContextAsync() : App.Context;
        Page = await context.NewPageAsync();
        Page.SetDefaultTimeout(15_000);
        Page.Console += (_, msg) =>
        {
            lock (consoleLog)
                consoleLog.Add($"[{msg.Type}] {msg.Text}");

            // "Failed to load resource" carries no URL; the Response handler below reports those with one.
            if (msg.Type == "error" && !msg.Text.StartsWith("Failed to load resource"))
                lock (consoleErrors) consoleErrors.Add(msg.Text);
        };
        Page.Response += (_, response) =>
        {
            if (response.Status < 400)
                return;

            var line = $"HTTP {response.Status} {response.Url}";
            lock (consoleLog)
                consoleLog.Add("[network] " + line);

            // A missing asset of the app's own is a bug; a remote demo image that 404s is the internet.
            // The dev server's own tooling probes (hot reload settings) are neither.
            if (response.Url.StartsWith(App.BaseUrl, StringComparison.OrdinalIgnoreCase)
                && !response.Url.Contains("/_framework/dotnet-browser-tools/", StringComparison.Ordinal))
                lock (consoleErrors) consoleErrors.Add(line);
        };
        Page.PageError += (_, error) =>
        {
            lock (consoleErrors) consoleErrors.Add("Uncaught: " + error);
        };
    }

    [After(Test)]
    public async Task ClosePage(TestContext context)
    {
        if (Page == null)
            return;

        if (context.Execution.Result?.State == TestState.Failed)
        {
            var name = Sanitize(context.Metadata.DisplayName);
            var dir = Path.Combine(BlazorTestSettings.ResultsDirectory, "failures");
            Directory.CreateDirectory(dir);
            try { await Page.ScreenshotAsync(new() { Path = Path.Combine(dir, name + ".png"), FullPage = true }); } catch { }
            lock (consoleLog)
                File.WriteAllLines(Path.Combine(dir, name + ".console.log"), consoleLog);
        }
        await Page.CloseAsync();
    }

    /// <summary>
    /// Loads a gallery route and waits until Blazor has rendered it: the splash is gone, the router
    /// has put content in #app, and the framework hasn't raised its error bar.
    /// </summary>
    protected async Task Open(string route)
    {
        await Page.GotoAsync(route.TrimStart('/'));
        await Page.WaitForFunctionAsync(
            """
            () => {
                const app = document.getElementById('app');
                const splash = document.getElementById('shiny-splash');
                const splashUp = splash && splash.isConnected && getComputedStyle(splash).display !== 'none' && getComputedStyle(splash).opacity !== '0';
                return app && app.children.length > 0 && !splashUp;
            }
            """,
            null,
            new() { Timeout = 60_000 }
        );
        await AssertNoBlazorErrorUi();
    }

    /// <summary>The framework's "An unhandled error has occurred" bar — a render exception.</summary>
    protected async Task AssertNoBlazorErrorUi()
    {
        var bar = Page.Locator("#blazor-error-ui");
        if (await bar.CountAsync() > 0 && await bar.IsVisibleAsync())
            Assert.Fail("Blazor raised its unhandled-error bar.\n" + String.Join("\n", consoleErrors));
    }

    /// <summary>Fails with every console error / uncaught exception since the page opened.</summary>
    protected async Task AssertNoConsoleErrors(params string[] ignorePatterns)
    {
        await AssertNoBlazorErrorUi();

        List<string> errors;
        lock (consoleErrors)
            errors = consoleErrors
                .Where(e => !ignorePatterns.Any(p => Regex.IsMatch(e, p)))
                .ToList();

        if (errors.Count > 0)
            Assert.Fail("The page logged errors:\n" + String.Join("\n", errors.Take(10)));
    }

    /// <summary>
    /// Scrolls the gallery's content region, the way a wheel or a finger would. Mobile WebKit cannot
    /// synthesise a wheel, so this is the one scroll that works in every run.
    /// </summary>
    protected Task ScrollContentAsync(int dy)
        => Page.EvaluateAsync("dy => (document.querySelector('.shiny-applayout-content') ?? document.scrollingElement).scrollBy(0, dy)", dy);

    protected Task Screenshot(string name)
    {
        var path = Path.Combine(BlazorTestSettings.ResultsDirectory, "screenshots", name.EndsWith(".png") ? name : name + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return Page.ScreenshotAsync(new() { Path = path });
    }

    /// <summary>Playwright's retrying assertions (ToBeVisibleAsync, ToHaveTextAsync, …).</summary>
    protected static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    static string Sanitize(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars().Append(' '))
            value = value.Replace(c, '_');

        return value.Length > 120 ? value[..120] : value;
    }
}

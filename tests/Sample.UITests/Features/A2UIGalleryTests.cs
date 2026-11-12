namespace Sample.UITests.Features;

/// <summary>
/// The standalone A2UISurfaceView on the A2UI Gallery page: no agent, just A2UI messages applied to a
/// surface host. The page logs what the surface sends back (actions, errors, function calls) in
/// <c>GalleryLogLabel</c>, which is what these tests read. Components are found inside the view by
/// type and text - the renderers give them no AutomationIds.
/// </summary>
public class A2UIGalleryTests : UITest
{
    const string Route = "a2ui-gallery";
    const string PageType = "A2UIGalleryPage";
    const string View = "SampleA2UISurfaceView";
    const string Log = "GalleryLogLabel";

    async Task Show(string button, string title)
    {
        await Open(Route, PageType);
        await Driver.Tap(button);
        await Driver.WaitUntil(async () => await HasLabel(title), $"the '{title}' surface");
        await Driver.WaitForValue(Log, "Text", "Tap a button or a grid row.");
    }

    async Task<bool> HasLabel(string text)
        => (await Driver.QuerySelector($"#{View} Label")).Any(l => l.IsVisible && l.Text == text);

    async Task TapButton(string text)
    {
        var button = (await Driver.Css($"#{View} Button[Text={MauiDevFlowDriver.Quote(text)}]")).First(b => b.IsVisible);
        await Driver.Tap(elementId: button.Id);
    }

    Task WaitForLog(string fragment)
        => Driver.WaitUntil(async () => (await Driver.GetProperty("Text", automationId: Log) ?? "").Contains(fragment, StringComparison.Ordinal),
            $"'{fragment}' in the outbound log", 15);

    [Test]
    public async Task Dashboard_Renders_The_Shiny_Catalog()
    {
        await Show("GalleryDashboardButton", "October sales");

        await Assert.That((await Driver.QuerySelector($"#{View} Gauge")).Count).IsGreaterThan(0);
        await Assert.That((await Driver.QuerySelector($"#{View} DataGrid")).Count).IsGreaterThan(0);
        await Assert.That((await Driver.QuerySelector($"#{View} ChipView")).Count).IsEqualTo(3);
        await Assert.That((await Driver.QuerySelector($"#{View} Expander")).Count).IsEqualTo(1);
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Dashboard_Refresh_Sends_An_Action_With_Its_Context()
    {
        await Show("GalleryDashboardButton", "October sales");

        // add Asia to the regions, then Refresh: the action's context carries the data model
        await Driver.TapCss($"#{View} ChipView", 2);
        await Driver.WaitForCssValue($"#{View} ChipView", "IsSelected", "True", index: 2);
        await TapButton("Refresh");

        await WaitForLog("refresh");
        await WaitForLog("apac");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Dashboard_Row_Tap_Sends_The_Row_Action()
    {
        await Show("GalleryDashboardButton", "October sales");

        // the grid's cells carry their text; the row is the nearest ancestor with a tap gesture
        string? row = null;
        await Driver.WaitUntil(async () =>
        {
            var all = (await Driver.QuerySelector($"#{View} DataGrid *")).ToDictionary(e => e.Id);
            foreach (var cell in all.Values.Where(e => e.Text == "A-1002" && e.IsVisible))
            {
                for (var current = cell; current != null; current = current.ParentId != null && all.TryGetValue(current.ParentId, out var p) ? p : null)
                {
                    if (current.Gestures?.Contains("tap") == true)
                    {
                        row = current.Id;
                        return true;
                    }
                }
            }
            return false;
        }, "the A-1002 row", 20);
        await Driver.Tap(elementId: row);

        // the relative "id" path in the action's context resolves against the tapped row
        await WaitForLog("openOrder");
        await WaitForLog("A-1002");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Form_Submit_Sends_The_Typed_Values()
    {
        await Show("GalleryFormButton", "Shiny newsletter");

        await Driver.FillCss($"#{View} Entry", "Grace", 0);
        await Driver.FillCss($"#{View} Entry", "grace@example.com", 1);
        await TapButton("Subscribe");

        await WaitForLog("subscribe");
        await WaitForLog("grace@example.com");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Kitchen_Sink_Renders_Every_Basic_Component()
    {
        await Show("GalleryKitchenSinkButton", "Kitchen sink");

        // the Display tab is first: icons, image, video, audio
        await Driver.WaitUntil(() => HasLabel("SoundHelix song 1"), "the AudioPlayer");
        await AssertNoAppErrors();
    }
}

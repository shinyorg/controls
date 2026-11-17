namespace Sample.UITests.Features;

/// <summary>
/// ShinyTabbedPage on <c>tabbedpage</c>: selection, template tabs, the centre button and the bar's
/// look switches. The status label lives on the Home tab, so tests come back to it before reading it.
/// </summary>
[SkipOn(UIPlatform.MacOS, "tabbedpage is not in the macOS head")]
[SkipOn(UIPlatform.Linux, "tabbedpage is not in the Linux head")]
public class TabsTests : UITest
{
    const string Page = "TabsDemoPage";
    const string Status = "TabsStatus";

    async Task OpenOnHome()
    {
        await Open("tabbedpage", Page);
        await Select(0);
    }

    async Task Select(int index)
    {
        await Driver.SetProperty("SelectedIndex", index.ToString(), automationId: Page);
        await Driver.WaitForProperty(Page, "SelectedIndex", index.ToString());
    }

    async Task WaitForStatus(string expected)
        => await Driver.WaitUntil(
            async () => await Driver.GetProperty("Text", automationId: Status) == expected,
            $"tabs status '{expected}'"
        );

    async Task<bool> TextShowing(string text)
        => (await Driver.Query(text: text)).Any(e => e.IsVisible);

    [Test]
    public async Task Selecting_A_Tab_Raises_SelectionChanged_With_The_Tab()
    {
        await OpenOnHome();
        await Select(3);
        await Select(0);

        await WaitForStatus("Selected Home (index 0).");
        await Driver.WaitUntil(
            async () => (await Driver.GetProperty("SelectedItem", automationId: Page)) != null,
            "SelectedItem follows SelectedIndex"
        );
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Template_Tab_Is_Built_When_First_Selected()
    {
        await OpenOnHome();
        await Select(1);
        await Driver.WaitUntil(() => TextShowing("Built on first selection"), "the Chat tab's template content");

        await Select(4);
        await Driver.WaitUntil(() => TextShowing("Five tabs, one centre button"), "the Me tab's template content");
        await Select(0);
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Tab_Bar_Follows_The_Page_Selection()
    {
        await OpenOnHome();
        await Select(2);

        var bar = await Driver.FindByType("ShinyTabBar");
        await Driver.WaitUntil(
            async () => await Driver.GetProperty("SelectedIndex", elementId: bar.Id) == "2",
            "ShinyTabBar.SelectedIndex to follow the page"
        );
        await Select(0);
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Centre_Button_Raises_CenterClicked()
    {
        await OpenOnHome();
        // put a known status line up first, so the assertion below can't pass on a stale one
        await Select(3);
        await Select(0);
        await WaitForStatus("Selected Home (index 0).");
        await Driver.Tap(automationId: "TabsDemoPage_TabBar_Center");

        await WaitForStatus("Centre button pressed.");
        await AssertNoAppErrors();
    }

    [Test]
    [Arguments("IndicatorLine", "IndicatorStyle", "Line")]
    [Arguments("IndicatorDot", "IndicatorStyle", "Dot")]
    [Arguments("IndicatorPill", "IndicatorStyle", "Pill")]
    [Arguments("BarFloating", "BarStyle", "Floating")]
    [Arguments("BarDocked", "BarStyle", "Docked")]
    [Arguments("LabelsSelected", "LabelMode", "SelectedOnly")]
    [Arguments("LabelsAlways", "LabelMode", "Always")]
    [Arguments("TransitionFade", "Transition", "Fade")]
    [Arguments("TransitionSlide", "Transition", "Slide")]
    public async Task Option_Buttons_Restyle_The_Page(string button, string property, string expected)
    {
        await OpenOnHome();
        await Driver.Tap(automationId: button);
        await Driver.WaitForProperty(Page, property, expected);
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Bar_Transparency_Runs_Content_Behind_At_The_Sheerest_Setting()
    {
        await OpenOnHome();
        await Driver.Tap(automationId: "BarSheer");
        await Driver.WaitForProperty(Page, "BarBackgroundOpacity", "0.35");
        await Driver.WaitForProperty(Page, "ContentBehindTabBar", "True");

        await Driver.Tap(automationId: "BarSolid");
        await Driver.WaitForProperty(Page, "BarBackgroundOpacity", "1");
        await Driver.WaitForProperty(Page, "ContentBehindTabBar", "False");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Slow_Indicator_Toggles_The_Travel_Duration()
    {
        await OpenOnHome();
        await Driver.Tap(automationId: "IndicatorSlow");
        await Driver.WaitUntil(async () => (await Driver.GetProperty("Text", automationId: Status))?.StartsWith("Indicator travel over") == true, "slow status");
        var first = await Driver.GetProperty("Text", automationId: Status);

        await Driver.Tap(automationId: "IndicatorSlow");
        await Driver.WaitUntil(async () => await Driver.GetProperty("Text", automationId: Status) != first, "the duration to flip");
        var second = await Driver.GetProperty("Text", automationId: Status);

        var both = new[] { first, second };
        await Assert.That(both.Contains("Indicator travel over 800ms.")).IsTrue();
        await Assert.That(both.Contains("Indicator travel over 200ms.")).IsTrue();
        await AssertNoAppErrors();
    }
}

namespace Sample.UITests.Features;

/// <summary>
/// FloatingToolbar: one bar shared by three cards through AttachTo, item clicks reported through
/// ItemClickedCommand, and the page's orientation/labels/animation options reaching the bar.
/// Not in the macOS or Linux heads' Shells.
/// </summary>
[SkipOn(UIPlatform.MacOS, "floatingtoolbar is not in the macOS head")]
[SkipOn(UIPlatform.Linux, "floatingtoolbar is not in the Linux head")]
public class FloatingToolbarTests : UITest
{
    /// <summary>Shell keeps the page alive between visits, so the bar is closed on the way in.</summary>
    async Task OpenPage()
    {
        await Open("floatingtoolbar", "FloatingToolbarPage");
        await Driver.SetProperty("IsOpen", "False", automationId: "CardToolbar");
        await Driver.WaitForProperty("CardToolbar", "IsOpen", "False");
    }

    /// <summary>
    /// Opens the bar over a card. The card's trigger is a TapGestureRecognizer whose Tapped event the
    /// agent may not be able to raise, so if the tap does not open it the bar is opened directly.
    /// </summary>
    async Task OpenBarOver(string cardId)
    {
        try
        {
            await Driver.Tap(automationId: cardId);
            await Driver.WaitForProperty("CardToolbar", "IsOpen", "True", timeoutSeconds: 3);
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
        {
            await Driver.SetProperty("IsOpen", "True", automationId: "CardToolbar");
            await Driver.WaitForProperty("CardToolbar", "IsOpen", "True");
        }
    }


    [Test]
    public async Task Item_Click_Is_Reported_Through_The_Command()
    {
        await OpenPage();

        await OpenBarOver("CardOne");
        // the card toolbar has an id, so its items are CardToolbar_Item_<index> (Cut 0, Copy 1); they tap through a recognizer Command
        await Driver.Tap(automationId: "CardToolbar_Item_0");

        await Driver.WaitUntil(async () =>
            (await Driver.GetProperty("Text", automationId: "LastActionLabel"))?.StartsWith("Cut on ") == true,
            "the Cut click to be reported");
        await AssertNoAppErrors();
    }


    [Test]
    public async Task Item_Click_Dismisses_The_Bar()
    {
        await OpenPage();
        await OpenBarOver("CardTwo");

        await Driver.Tap(automationId: "CardToolbar_Item_1");

        await Driver.WaitForProperty("CardToolbar", "IsOpen", "False");
        await AssertNoAppErrors();
    }


    [Test]
    public async Task Vertical_Switch_Sets_Orientation()
    {
        await OpenPage();
        await Driver.SetProperty("IsToggled", "False", automationId: "VerticalSwitch");
        await Driver.WaitForProperty("CardToolbar", "Orientation", "Horizontal");

        await Driver.SetProperty("IsToggled", "True", automationId: "VerticalSwitch");
        await Driver.WaitForProperty("CardToolbar", "Orientation", "Vertical");

        await Driver.SetProperty("IsToggled", "True", automationId: "LabelsSwitch");
        await Driver.WaitForProperty("CardToolbar", "ShowLabels", "True");
        await AssertNoAppErrors();
    }


    [Test]
    public async Task Animation_Button_Cycles_The_Animation()
    {
        await OpenPage();
        // the cycle is Scale → Fade → Slide → None → Scale; start from Scale whatever a past visit left
        for (var i = 0; i < 4 && await Driver.GetProperty("Text", automationId: "AnimationButton") != "Animation: Scale"; i++)
            await Driver.Tap(automationId: "AnimationButton");
        await Driver.WaitForProperty("AnimationButton", "Text", "Animation: Scale");
        await Driver.WaitForProperty("CardToolbar", "Animation", "Scale");

        await Driver.Tap(automationId: "AnimationButton");
        await Driver.WaitForProperty("AnimationButton", "Text", "Animation: Fade");
        await Driver.WaitForProperty("CardToolbar", "Animation", "Fade");

        await Driver.Tap(automationId: "AnimationButton");
        await Driver.Tap(automationId: "AnimationButton");
        await Driver.Tap(automationId: "AnimationButton");
        await Driver.WaitForProperty("AnimationButton", "Text", "Animation: Scale");
        await AssertNoAppErrors();
    }
}

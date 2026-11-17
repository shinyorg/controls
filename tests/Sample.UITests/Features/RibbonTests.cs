namespace Sample.UITests.Features;

/// <summary>
/// Ribbon items are descriptors; the ribbon builds a tappable face (a Border with a
/// TapGestureRecognizer command) carrying the item's AutomationId, so faces and tab headers are
/// tapped directly. Shell keeps the page (and its view model) alive across tests, so toggles are
/// asserted relative to the state they started in, and everything a test changes is put back.
/// </summary>
[SkipOn(UIPlatform.Linux, "page not in the Linux head")]
// Evidence (emulator API 35, 2026-10-14): opening //ribbon on a fresh app kills it - java.lang.IllegalStateException
// "The specified child already has a parent" from ViewGroup.AddView <- LayoutHandler.SetVirtualView <- BorderHandler
// <- the Ribbon's root Grid <- ShellFragmentContainer.OnCreateView, i.e. while the page is first realized. It began
// when the sample's command bindings were corrected (OpenFileMenuCommand / OpenFontDialogCommand /
// InsertPictureCommand): each binding that now resolves fires a Redraw property and Ribbon.Rebuild re-adds views
// (Ribbon.Render.cs) during realization. UIKit reparents silently, Android refuses. The page opened fine with the
// old, non-resolving bindings in the earlier sweep.
public class RibbonTests : UITest
{
    const string RibbonId = "SampleRibbon";

    async Task OpenRibbon()
    {
        await Open("ribbon", "RibbonPage");
        await Driver.WaitUntilExists(RibbonId);

        // A phone is narrow enough to fold low-priority groups into buttons, which hides their items.
        // Keep every group open so item faces are always there to tap, and start on Home, expanded.
        await Driver.SetProperty("IsChecked", "False", automationId: "RibbonGroupCollapseCheck");
        await Driver.WaitForProperty(RibbonId, "AllowGroupCollapse", "False");
        await Driver.SetProperty("IsChecked", "False", automationId: "RibbonPictureCheck");
        await Driver.SetProperty("DisplayMode", "Expanded", automationId: RibbonId);
        await Driver.SetProperty("SelectedIndex", "0", automationId: RibbonId);
        await Driver.WaitForProperty(RibbonId, "SelectedIndex", "0");
    }

    async Task RestoreDefaults()
    {
        try
        {
            await Driver.SetProperty("IsChecked", "False", automationId: "RibbonPictureCheck");
            await Driver.SetProperty("IsChecked", "True", automationId: "RibbonGroupCollapseCheck");
            await Driver.SetProperty("DisplayMode", "Expanded", automationId: RibbonId);
            await Driver.SetProperty("SelectedIndex", "0", automationId: RibbonId);
        }
        catch
        {
            // best effort: the next test re-establishes what it needs anyway
        }
    }

    Task WaitForLog(string fragment)
        => Driver.WaitUntil(
            async () => (await Driver.GetProperty("Text", automationId: "RibbonLog"))?.Contains(fragment) == true,
            $"ribbon log to contain '{fragment}'"
        );

    [Test]
    public async Task Command_Button_Runs_Its_Command()
    {
        await OpenRibbon();
        try
        {
            // the log keeps its last line for the session; a fresh "Cut" line has a new timestamp
            var before = await Driver.GetProperty("Text", automationId: "RibbonLog");
            await Driver.Tap("RibbonCut");
            await Driver.WaitUntil(async () => await Driver.GetProperty("Text", automationId: "RibbonLog") is { } t && t != before && t.EndsWith("Cut"),
                "the Cut command to write to the log");
        }
        finally
        {
            await RestoreDefaults();
        }
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Application_Button_Runs_Its_Command()
    {
        await OpenRibbon();
        try
        {
            await Driver.Tap("SampleRibbon_ApplicationButton");
            await WaitForLog("File");
        }
        finally
        {
            await RestoreDefaults();
        }
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Toggle_Button_Flips_Its_Bound_Value()
    {
        await OpenRibbon();
        try
        {
            await Driver.Tap("RibbonBold");
            await Driver.WaitUntil(
                async () => (await Driver.GetProperty("Text", automationId: "RibbonLog")) is { } t && (t.EndsWith("Bold on") || t.EndsWith("Bold off")),
                "the bound Bold property to change"
            );
            var first = (await Driver.GetProperty("Text", automationId: "RibbonLog"))!;
            var expectedNext = first.EndsWith("Bold on") ? "Bold off" : "Bold on";

            await Driver.Tap("RibbonBold");
            await WaitForLog(expectedNext);
        }
        finally
        {
            await RestoreDefaults();
        }
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Turning_On_An_Alignment_Reports_It()
    {
        await OpenRibbon();
        try
        {
            // Centre may already be the checked one (the view model outlives the test), in which case
            // the first tap turns it off; a second tap then turns it back on.
            await Driver.Tap("RibbonAlignCenter");
            await Task.Delay(500);
            if ((await Driver.GetProperty("Text", automationId: "RibbonLog"))?.EndsWith("Align center") != true)
                await Driver.Tap("RibbonAlignCenter");

            await WaitForLog("Align center");
        }
        finally
        {
            await RestoreDefaults();
        }
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Tapping_A_Tab_Selects_It()
    {
        await OpenRibbon();
        try
        {
            await Driver.Tap("RibbonTabInsert");
            await Driver.WaitForProperty(RibbonId, "SelectedIndex", "1");
            await Driver.WaitUntilExists("RibbonInsertPicture");

            await Driver.Tap("RibbonTabHome");
            await Driver.WaitForProperty(RibbonId, "SelectedIndex", "0");
        }
        finally
        {
            await RestoreDefaults();
        }
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Tapping_The_Showing_Tab_Collapses_The_Bar()
    {
        await OpenRibbon();
        try
        {
            await Driver.Tap("RibbonTabHome");
            await Driver.WaitForProperty(RibbonId, "DisplayMode", "Collapsed");
        }
        finally
        {
            await RestoreDefaults();
        }
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Display_Mode_Buttons_Switch_The_Bar()
    {
        await OpenRibbon();
        try
        {
            await Driver.Tap("RibbonModeSimplified");
            await Driver.WaitForProperty(RibbonId, "DisplayMode", "Simplified");
            await WaitForLog("Display mode: Simplified");

            await Driver.Tap("RibbonModeCollapsed");
            await Driver.WaitForProperty(RibbonId, "DisplayMode", "Collapsed");
            await WaitForLog("Display mode: Collapsed");

            await Driver.Tap("RibbonModeExpanded");
            await Driver.WaitForProperty(RibbonId, "DisplayMode", "Expanded");
            await WaitForLog("Display mode: Expanded");
        }
        finally
        {
            await RestoreDefaults();
        }
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Group_Collapse_CheckBox_Drives_AllowGroupCollapse()
    {
        await OpenRibbon();
        try
        {
            await Driver.SetProperty("IsChecked", "True", automationId: "RibbonGroupCollapseCheck");
            await Driver.WaitForProperty(RibbonId, "AllowGroupCollapse", "True");

            await Driver.SetProperty("IsChecked", "False", automationId: "RibbonGroupCollapseCheck");
            await Driver.WaitForProperty(RibbonId, "AllowGroupCollapse", "False");
        }
        finally
        {
            await RestoreDefaults();
        }
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Hidden_Contextual_Tab_Cannot_Be_Selected_And_Falls_Back_When_Hidden()
    {
        await OpenRibbon();
        try
        {
            // tab 3 is the contextual Format tab, hidden while no picture is selected
            await Driver.SetProperty("SelectedIndex", "3", automationId: RibbonId);
            await Task.Delay(500);
            await Assert.That(await Driver.GetProperty("SelectedIndex", automationId: RibbonId)).IsNotEqualTo("3");

            await Driver.SetProperty("IsChecked", "True", automationId: "RibbonPictureCheck");
            await Driver.WaitUntilExists("RibbonTabPicture");
            await Driver.SetProperty("SelectedIndex", "3", automationId: RibbonId);
            await Driver.WaitForProperty(RibbonId, "SelectedIndex", "3");

            // the selection going away moves the ribbon onto a real tab rather than an empty body
            await Driver.SetProperty("IsChecked", "False", automationId: "RibbonPictureCheck");
            await Driver.WaitUntil(
                async () => await Driver.GetProperty("SelectedIndex", automationId: RibbonId) is "0" or "1" or "2",
                "the ribbon to fall back off the hidden contextual tab"
            );
        }
        finally
        {
            await RestoreDefaults();
        }
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Insert_Picture_Brings_Up_The_Contextual_Tab()
    {
        await OpenRibbon();
        try
        {
            await Driver.Tap("RibbonTabInsert");
            await Driver.WaitForProperty(RibbonId, "SelectedIndex", "1");

            await Driver.Tap("RibbonInsertPicture");

            await WaitForLog("Picture Tools tab appeared");
            await Driver.WaitForProperty("RibbonPictureCheck", "IsChecked", "True");
            await Driver.WaitUntilExists("RibbonTabPicture");
        }
        finally
        {
            await RestoreDefaults();
        }
        await AssertNoAppErrors();
    }
}

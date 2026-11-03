namespace Sample.UITests.Features;

/// <summary>TableView pages: basic (cells), dynamic (ItemsSource sections), picker, styling. Replaces the old root TableViewTests.</summary>
public class TableViewTests : UITest
{
    async Task<List<string?>> SwitchStates()
    {
        var states = new List<string?>();
        foreach (var s in await Driver.Query(type: "Switch"))
            states.Add(await Driver.GetProperty("IsToggled", elementId: s.Id));
        return states;
    }

    async Task<double?> FontSizeOfText(string text)
    {
        var el = await Driver.WaitUntilTextExists(text, timeoutSeconds: 10);
        var raw = await Driver.GetProperty("FontSize", elementId: el.Id);
        return Double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }


    // ── basic ───────────────────────────────────────────────────────────────

    [Test]
    public async Task Switch_Cell_On_Drives_Its_Switch()
    {
        await Open("basic", "BasicSettingsPage");
        await Driver.WaitForProperty("WifiCell", "On", "True");
        var before = (await SwitchStates()).Count(x => x == "True");

        await Driver.SetProperty("On", "False", automationId: "WifiCell");
        await Driver.WaitUntil(async () => (await SwitchStates()).Count(x => x == "True") == before - 1, "the Wi-Fi switch to turn off");

        await Driver.SetProperty("On", "True", automationId: "WifiCell");
        await Driver.WaitUntil(async () => (await SwitchStates()).Count(x => x == "True") == before, "the Wi-Fi switch to turn back on");
        await AssertNoAppErrors();
    }


    [Test]
    public async Task Toggling_The_Switch_Updates_The_Cell()
    {
        await Open("basic", "BasicSettingsPage");
        await Driver.WaitForProperty("BluetoothCell", "On", "False");

        // Wi-Fi starts on and Bluetooth off, so the page's only untoggled switch is Bluetooth's.
        // only this page's switches - Shell keeps every visited page (and its switches) alive
        var off = new List<string>();
        foreach (var s in await Driver.QuerySelector("BasicSettingsPage Switch"))
            if (await Driver.GetProperty("IsToggled", elementId: s.Id) == "False")
                off.Add(s.Id);
        await Assert.That(off.Count).IsEqualTo(1);

        await Driver.SetProperty("IsToggled", "True", elementId: off[0]);
        try
        {
            await Driver.WaitForProperty("BluetoothCell", "On", "True");
            await AssertNoAppErrors();
        }
        finally
        {
            await Driver.SetProperty("On", "False", automationId: "BluetoothCell");
        }
    }


    [Test]
    public async Task Real_Click_Toggles_The_Switch_In_A_Cell_That_Cant_Be_Selected()
    {
        // The regression: every cell carried a tap recognizer, and on macOS (AppKit) it took the click
        // from the switch inside and then ignored it, the cell not being selectable - while DevFlow's
        // tap, which sets the switch directly, passed.
        if (!HasRealClick)
            Skip.Test("needs real input (Android, or macOS with the Accessibility permission)");

        await Open("basic", "BasicSettingsPage");
        await Driver.WaitForProperty("LocationCell", "On", "True");

        await TouchTap("LocationCell_Switch");
        await Driver.WaitForProperty("LocationCell", "On", "False", timeoutSeconds: 5);

        await TouchTap("LocationCell_Switch");
        await Driver.WaitForProperty("LocationCell", "On", "True", timeoutSeconds: 5);
        await AssertNoAppErrors();
    }


    [Test]
    public async Task Radio_Cells_In_A_Section_Are_One_Group()
    {
        await Open("basic", "BasicSettingsPage");

        // Light, Dark, System — the section's SelectedValue starts at "System".
        // only this page's radios - other pages Shell keeps alive have RadioButtons too
        const string Radios = "BasicSettingsPage RadioButton";
        await Driver.WaitUntil(async () => (await Driver.QuerySelector(Radios)).Count == 3, "three radio cells");
        var radios = await Driver.QuerySelector(Radios);
        await Driver.WaitUntil(async () => await Driver.GetProperty("IsChecked", elementId: radios[2].Id) == "True", "System to start checked");

        await Driver.SetProperty("IsChecked", "True", elementId: radios[0].Id);
        try
        {
            await Driver.WaitUntil(async () =>
            {
                var r = await Driver.QuerySelector(Radios);
                return await Driver.GetProperty("IsChecked", elementId: r[0].Id) == "True"
                    && await Driver.GetProperty("IsChecked", elementId: r[2].Id) == "False";
            }, "Light checked and System cleared");
            await AssertNoAppErrors();
        }
        finally
        {
            await Driver.SetProperty("IsChecked", "True", elementId: (await Driver.QuerySelector(Radios))[2].Id);
        }
    }


    [Test]
    public async Task Entry_Cell_Mask_Formats_The_Raw_Value()
    {
        await Open("basic", "BasicSettingsPage");

        await Driver.SetProperty("ValueText", "5551234567", automationId: "PhoneCell");
        await Driver.WaitForProperty("PhoneCell", "FormattedValueText", "(555) 123-4567");

        await Driver.SetProperty("ValueText", "122030", automationId: "ExpiryCell");
        await Driver.WaitForProperty("ExpiryCell", "FormattedValueText", "12/2030");
        await AssertNoAppErrors();
    }


    [Test]
    public async Task Number_Picker_Cell_Shows_Value_With_Unit()
    {
        await Open("basic", "BasicSettingsPage");
        await Driver.WaitUntilTextExists("14 pt", timeoutSeconds: 10);

        await Driver.SetProperty("Number", "20", automationId: "FontSizeCell");
        await Driver.WaitUntilTextExists("20 pt", timeoutSeconds: 10);
        await AssertNoAppErrors();
    }


    // ── dynamic ─────────────────────────────────────────────────────────────

    [Test]
    public async Task ItemsSource_Section_Tracks_Adds_And_Removes()
    {
        await Open("dynamic", "DynamicSectionsPage");

        // one static cell + three generated
        await Driver.WaitUntil(async () => (await Driver.Query(type: "LabelCell")).Count == 4, "four label cells");

        await Driver.Tap("DynamicAddButton");
        await Driver.WaitUntil(async () => (await Driver.Query(type: "LabelCell")).Count == 5, "a fifth label cell");
        await Driver.WaitUntilTextExists("Item 4", timeoutSeconds: 10);

        await Driver.Tap("DynamicRemoveButton");
        await Driver.Tap("DynamicRemoveButton");
        await Driver.WaitUntil(async () => (await Driver.Query(type: "LabelCell")).Count == 3, "three label cells");
        await Assert.That((await Driver.Query(text: "Item 3")).Count).IsEqualTo(0);
        await Assert.That((await Driver.Query(text: "This section is static")).Count).IsGreaterThan(0);
        await AssertNoAppErrors();
    }


    // ── picker ──────────────────────────────────────────────────────────────

    [Test]
    public async Task Text_Picker_Cell_Shows_The_Selected_Item()
    {
        await Open("picker", "PickerDemoPage");
        await Driver.WaitUntilTextExists("Red", timeoutSeconds: 10);     // SelectedColorIndex starts at 0

        await Driver.SetProperty("SelectedIndex", "2", automationId: "FavoriteColorCell");
        await Driver.WaitUntilTextExists("Blue", timeoutSeconds: 10);
        await AssertNoAppErrors();
    }


    [Test, OnlyOn(UIPlatform.MacOS)] // drives the AppKit pop-up menu through the window server
    public async Task Real_Click_Opens_The_Text_Picker_And_Picks_An_Item()
    {
        // The regression (many times over): clicking a TextPickerCell on the AppKit head did nothing. The
        // cell's click recognizer took the click, then called Picker.Focus(), which on AppKit only makes
        // the NSPopUpButton first responder - it never opens the menu. The SetProperty test above passes
        // regardless, so this one clicks for real and watches for the menu window itself.
        // A skip here is how it kept coming back: without the Accessibility permission this fails.
        if (!MacMouse.CanPost)
            Assert.Fail("Give the test runner (Terminal / IDE) the Accessibility permission - this test must click for real.");

        var mac = (MacOSAppHost)App.Host;
        await Open("picker", "PickerDemoPage");
        await Driver.SetProperty("SelectedIndex", "0", automationId: "FavoriteColorCell");
        await Driver.WaitUntilTextExists("Red", timeoutSeconds: 10);

        try
        {
            await TouchTap("FavoriteColorCell");
            await Driver.WaitUntil(() => Task.FromResult(mac.HasOpenMenu()), "the picker's menu to open", timeoutSeconds: 5);

            // the menu opens with the current item (Red) under the pointer: two down is Blue
            await MacMouse.Press(MacMouse.KeyDownArrow);
            await MacMouse.Press(MacMouse.KeyDownArrow);
            await MacMouse.Press(MacMouse.KeyReturn);

            await Driver.WaitUntil(() => Task.FromResult(!mac.HasOpenMenu()), "the menu to close", timeoutSeconds: 5);
            await Driver.WaitForProperty("FavoriteColorCell", "SelectedIndex", "2", timeoutSeconds: 5);
            await Driver.WaitUntilTextExists("Blue", timeoutSeconds: 5);
            await AssertNoAppErrors();
        }
        finally
        {
            if (mac.HasOpenMenu())
                await MacMouse.Press(MacMouse.KeyEscape);
            await Driver.SetProperty("SelectedIndex", "0", automationId: "FavoriteColorCell");
        }
    }


    [Test]
    public async Task Number_Picker_Cell_Reflects_Its_Binding()
    {
        await Open("picker", "PickerDemoPage");
        await Driver.WaitUntilTextExists("1 times", timeoutSeconds: 10);

        await Driver.SetProperty("Number", "5", automationId: "RepeatCountCell");
        await Driver.WaitUntilTextExists("5 times", timeoutSeconds: 10);
    }


    // ── styling ─────────────────────────────────────────────────────────────

    [Test]
    public async Task Table_Styles_Cascade_And_Cells_Override_Them()
    {
        await Open("styling", "StylingPage");

        // CellTitleFontSize="17" on the table, TitleFontSize="24" on one cell
        await Assert.That(await FontSizeOfText("Default Styled Cell")).IsEqualTo(17);
        await Assert.That(await FontSizeOfText("Large Font Override")).IsEqualTo(24);

        // ValueTextFontSize="11" set on the cell itself
        await Assert.That(await FontSizeOfText("Small value")).IsEqualTo(11);
    }


    [Test]
    public async Task Table_Value_Text_Styles_Cascade_To_Cells()
    {
        await Open("styling", "StylingPage");

        // CellValueTextFontSize="16" and CellValueTextColor="#007AFF" on the table
        await Assert.That(await FontSizeOfText("Blue value text")).IsEqualTo(16);
        var el = await Driver.WaitUntilTextExists("Blue value text", timeoutSeconds: 10);
        await Assert.That(await Driver.GetProperty("TextColor", elementId: el.Id)).IsEqualTo("#007AFF");
    }


    [Test]
    public async Task Section_Header_Font_Overrides_The_Table()
    {
        await Open("styling", "StylingPage");

        // HeaderFontSize="13" on the table, 15 on one section
        await Assert.That(await FontSizeOfText("GLOBAL STYLES APPLIED")).IsEqualTo(13);
        await Assert.That(await FontSizeOfText("SECTION-LEVEL HEADER STYLE")).IsEqualTo(15);
    }
}

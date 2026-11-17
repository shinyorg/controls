namespace Sample.Blazor.UITests.Features;

// The Office controls draw their content (grid, page, slide, notebook page) on a Skia canvas, so these
// tests drive the DOM chrome around it — formula bar, sheet tabs, status bars, ribbons, the sample
// page's own toolbar — and use real keyboard input for anything that goes to the canvas.

public class SpreadsheetTests : BlazorTest
{
    ILocator NameBox => Page.Locator(".shiny-formula-name");
    ILocator Formula => Page.Locator(".shiny-formula-input");
    ILocator PageStatus => Page.Locator(".toolbar .status");

    async Task GoTo(string reference)
    {
        await NameBox.FillAsync(reference);
        await NameBox.PressAsync("Enter");
    }

    [Test]
    public async Task NameBox_Selects_Cell_And_FormulaBar_Shows_Its_Formula()
    {
        await Open("spreadsheet");
        await Expect(Formula).ToHaveValueAsync("Item");

        await GoTo("D5");
        await Expect(Formula).ToHaveValueAsync("=SUM(D2:D4)");

        await GoTo("C3");
        await Expect(Formula).ToHaveValueAsync("42");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Selecting_A_Range_Shows_Its_Sum_In_The_Status_Bar()
    {
        await Open("spreadsheet");
        await GoTo("D2:D4");
        await Expect(Page.Locator(".office-status__items")).ToContainTextAsync("156.75");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Editing_A_Cell_Recalculates_Dependent_Formulas()
    {
        await Open("spreadsheet");
        await GoTo("B2");
        await Formula.FillAsync("10");
        await Formula.PressAsync("Enter");

        // D2 = B2*C2 = 125; the column total is now 125 + 84 + 22.75.
        await GoTo("D2:D4");
        await Expect(Page.Locator(".office-status__items")).ToContainTextAsync("231.75");

        // The edit is one undo step: the sample's Undo puts the total back.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).First.ClickAsync();
        await GoTo("D2:D4");
        await Expect(Page.Locator(".office-status__items")).ToContainTextAsync("156.75");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Sheet_Tab_Switches_Sheet()
    {
        await Open("spreadsheet");
        var summary = Page.Locator(".sheet-tab", new() { HasText = "Summary" });
        await summary.ClickAsync();
        await Expect(summary).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("sheet-tab-on"));

        await GoTo("B1");
        await Expect(Formula).ToHaveValueAsync("=Budget!D5");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Add_Row_Adds_A_Formula_And_Undo_Redo_Follow()
    {
        await Open("spreadsheet");
        await Expect(PageStatus).ToContainTextAsync("5 formulas");

        await Page.Locator(".toolbar .btn", new() { HasText = "Add row" }).ClickAsync();
        await Expect(PageStatus).ToContainTextAsync("6 formulas");

        await GoTo("A6");
        await Expect(Formula).ToHaveValueAsync("Item 6");

        var redo = Page.Locator(".toolbar .btn", new() { HasText = "Redo" });
        await Expect(redo).ToBeDisabledAsync();
        await Page.Locator(".toolbar .btn", new() { HasText = "Undo" }).ClickAsync();
        await Expect(redo).ToBeEnabledAsync();
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Status_Bar_Zoom_Flows_Back_To_The_Bound_Zoom()
    {
        await Open("spreadsheet");
        await Expect(PageStatus).ToContainTextAsync("zoom 100%");

        await Page.Locator(".office-status").GetByRole(AriaRole.Button, new() { Name = "Zoom In" }).ClickAsync();
        await Expect(Page.Locator(".office-status__percent")).Not.ToHaveTextAsync("100%");
        await Expect(PageStatus).Not.ToContainTextAsync("zoom 100%");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Ribbon_Tab_Switches_Panel()
    {
        await Open("spreadsheet");
        var formulas = Page.Locator(".shiny-ribbon__tab", new() { HasText = "Formulas" });
        await formulas.ClickAsync();
        await Expect(formulas).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(Page.Locator(".shiny-ribbon__panel")).ToHaveAttributeAsync("aria-label", "Formulas");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Theme_Button_Toggles()
    {
        await Open("spreadsheet");
        var button = Page.Locator(".toolbar .btn", new() { HasText = "Force dark" });
        await button.ClickAsync();
        await Expect(Page.Locator(".toolbar .btn", new() { HasText = "Follow page" })).ToBeVisibleAsync();
        await AssertNoConsoleErrors();
    }
}

public class DocumentViewerTests : BlazorTest
{
    ILocator ZoomLabel => Page.Locator(".toolbar .zoom");
    ILocator ZoomIn => Page.Locator(".toolbar .btn", new() { HasText = "+" });
    ILocator ZoomOut => Page.Locator(".toolbar .btn", new() { HasText = "−" });

    [Test]
    public async Task Zoom_Buttons_Step_By_Ten_Percent()
    {
        await Open("document-viewer");
        await Expect(ZoomLabel).ToHaveTextAsync("100 %");
        await ZoomIn.ClickAsync();
        await Expect(ZoomLabel).ToHaveTextAsync("110 %");
        await ZoomOut.ClickAsync();
        await ZoomOut.ClickAsync();
        await Expect(ZoomLabel).ToHaveTextAsync("90 %");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Zoom_Clamps_Between_50_And_200_Percent()
    {
        await Open("document-viewer");
        for (var i = 0; i < 8; i++)
            await ZoomOut.ClickAsync();
        await Expect(ZoomLabel).ToHaveTextAsync("50 %");

        for (var i = 0; i < 18; i++)
            await ZoomIn.ClickAsync();
        await Expect(ZoomLabel).ToHaveTextAsync("200 %");
        await AssertNoConsoleErrors();
    }

    [Test]
    [SkipOnPhone("the sample hides the outline pane below 700px")]
    public async Task Outline_Lists_The_Documents_Headings()
    {
        await Open("document-viewer");
        var items = Page.Locator(".outline-item");
        await Expect(items).ToHaveTextAsync(["Field Notes", "Formatting", "A numbered list", "Spelling", "A table"]);
        await Expect(Page.Locator(".outline-item.level-1")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".toolbar .status")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^\d+ blocks$"));

        // Jumping to a heading must not throw (the canvas scroll itself is not observable from the DOM).
        await items.Nth(4).ClickAsync();
        await items.Nth(0).ClickAsync();
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Keyboard_Scrolling_Does_Not_Error()
    {
        await Open("document-viewer");
        var view = Page.Locator(".shiny-document");
        await view.FocusAsync();
        await Page.Keyboard.PressAsync("PageDown");
        await Page.Keyboard.PressAsync("End");
        await Page.Keyboard.PressAsync("Home");
        await Expect(view).ToBeFocusedAsync();
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Theme_Button_Toggles()
    {
        await Open("document-viewer");
        await Page.Locator(".toolbar .btn", new() { HasText = "Force dark" }).ClickAsync();
        await Expect(Page.Locator(".toolbar .btn", new() { HasText = "Follow page" })).ToBeVisibleAsync();
        await Page.Locator(".toolbar .btn", new() { HasText = "Follow page" }).ClickAsync();
        await Expect(Page.Locator(".toolbar .btn", new() { HasText = "Force dark" })).ToBeVisibleAsync();
        await AssertNoConsoleErrors();
    }
}

public class SlideViewerTests : BlazorTest
{
    ILocator Counter => Page.Locator(".toolbar .counter");
    ILocator NextButton => Page.Locator(".toolbar .btn", new() { HasText = "›" });
    ILocator PreviousButton => Page.Locator(".toolbar .btn", new() { HasText = "‹" });
    ILocator Notes => Page.Locator(".notes p");

    [Test]
    public async Task Next_And_Previous_Move_The_Counter_And_Notes()
    {
        await Open("slide-viewer");
        await Expect(Counter).ToHaveTextAsync("1 / 3");
        await Expect(PreviousButton).ToBeDisabledAsync();
        await Expect(Notes).ToContainTextAsync("embedding PowerPoint");

        await NextButton.ClickAsync();
        await Expect(Counter).ToHaveTextAsync("2 / 3");
        await Expect(PreviousButton).ToBeEnabledAsync();
        await Expect(Notes).ToContainTextAsync("Inheritance");

        await PreviousButton.ClickAsync();
        await Expect(Counter).ToHaveTextAsync("1 / 3");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Last_Slide_Disables_Next_And_Hides_Empty_Notes()
    {
        await Open("slide-viewer");
        await NextButton.ClickAsync();
        await NextButton.ClickAsync();
        await Expect(Counter).ToHaveTextAsync("3 / 3");
        await Expect(NextButton).ToBeDisabledAsync();
        await Expect(Page.Locator(".notes")).ToHaveCountAsync(0);
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Keyboard_Navigation_Flows_Back_To_The_Bound_Index()
    {
        await Open("slide-viewer");
        var view = Page.Locator(".shiny-slides");
        await view.FocusAsync();
        await Page.Keyboard.PressAsync("ArrowRight");
        await Expect(Counter).ToHaveTextAsync("2 / 3");
        await Page.Keyboard.PressAsync("End");
        await Expect(Counter).ToHaveTextAsync("3 / 3");
        await Page.Keyboard.PressAsync("Home");
        await Expect(Counter).ToHaveTextAsync("1 / 3");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Grid_Mode_Toggles_From_Button_And_Escape()
    {
        await Open("slide-viewer");
        await Page.Locator(".toolbar .btn", new() { HasText = "Grid" }).ClickAsync();
        var single = Page.Locator(".toolbar .btn", new() { HasText = "Single" });
        await Expect(single).ToBeVisibleAsync();

        // Escape on the view toggles the mode back, and the two-way binding updates the button.
        await Page.Locator(".shiny-slides").FocusAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator(".toolbar .btn", new() { HasText = "Grid" })).ToBeVisibleAsync();
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task F5_Presents_And_Escape_Leaves()
    {
        await Open("slide-viewer");
        var view = Page.Locator(".shiny-slides").First;
        await view.FocusAsync();
        await Page.Keyboard.PressAsync("F5");
        await Expect(Page.Locator(".shiny-slides-presenting")).ToHaveCountAsync(1);

        // In the show, the arrow keys advance and the bound index follows when the show ends.
        await Page.Keyboard.PressAsync("ArrowRight");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator(".shiny-slides-presenting")).ToHaveCountAsync(0);
        await Expect(Counter).ToHaveTextAsync("2 / 3");
        await AssertNoConsoleErrors();
    }
}

public class DocumentEditorTests : BlazorTest
{
    ILocator PageStatus => Page.Locator(".toolbar .status");
    ILocator Words => Page.Locator(".office-status__item", new() { HasText = "words" });

    async Task TypeIntoDocument(string text)
    {
        var canvas = Page.Locator(".shiny-doc-editor-canvas");
        await canvas.ClickAsync(new() { Position = new() { X = 200, Y = 120 } });
        await Page.Keyboard.PressAsync("End");
        await Page.Keyboard.TypeAsync(text);
    }

    [Test]
    public async Task Typing_Edits_The_Document_And_Updates_Word_Count()
    {
        await Open("document-editor");
        await Expect(Words).ToHaveTextAsync("181 words");

        await TypeIntoDocument(" extra words here");
        await Expect(Words).ToHaveTextAsync("184 words");
        await Expect(PageStatus).Not.ToContainTextAsync("0 edits");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Undo_Reverts_Typing()
    {
        await Open("document-editor");
        await TypeIntoDocument(" undone");
        await Expect(Words).ToHaveTextAsync("182 words");

        await Page.Locator(".office-titlebar").GetByRole(AriaRole.Button, new() { Name = "Undo" }).ClickAsync();
        await Expect(Words).ToHaveTextAsync("181 words");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Save_Is_Handed_To_The_Host()
    {
        // Save goes to the registered document store. On Chromium a new file would open the system save
        // dialog, which nothing can drive; hiding the File System Access picker takes the path Safari and
        // Firefox always take - a download - in every browser.
        await Page.AddInitScriptAsync("window.showSaveFilePicker = undefined;");
        await Open("document-editor");

        var download = await Page.RunAndWaitForDownloadAsync(() =>
            Page.Locator(".office-titlebar").GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync());

        await Assert.That(download.SuggestedFilename).StartsWith("Quarterly Report");
        await Assert.That(download.SuggestedFilename).EndsWith(".docx");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Shell_And_Wrapper_Can_Be_Turned_Off()
    {
        await Open("document-editor");
        await Expect(Page.Locator(".office-titlebar")).ToHaveCountAsync(1);

        await Page.GetByLabel("Office shell").UncheckAsync();
        await Expect(Page.Locator(".office-titlebar")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".shiny-doc-editor-view")).ToHaveCountAsync(1);

        await Page.GetByLabel("Editor wrapper").UncheckAsync();
        await Expect(Page.Locator(".shiny-doc-editor-view")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".shiny-doc-editor")).ToHaveCountAsync(1);
        await AssertNoConsoleErrors();
    }

    [Test]
    [SkipOnPhone("the Office shell goes compact on a phone and drops the view-mode buttons by design")]
    public async Task Status_Bar_View_And_Zoom()
    {
        await Open("document-editor");
        var status = Page.Locator(".office-status");
        var web = status.GetByRole(AriaRole.Radio, new() { Name = "Web Layout" });
        await web.ClickAsync();
        await Expect(web).ToHaveAttributeAsync("aria-checked", "true");
        await Expect(status.GetByRole(AriaRole.Radio, new() { Name = "Print Layout" })).ToHaveAttributeAsync("aria-checked", "false");

        await status.GetByRole(AriaRole.Button, new() { Name = "Zoom In" }).ClickAsync();
        await Expect(Page.Locator(".office-status__percent")).Not.ToHaveTextAsync("100%");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Ribbon_Tab_Switches_Panel()
    {
        await Open("document-editor");
        var insert = Page.Locator(".shiny-ribbon__tab", new() { HasText = "Insert" });
        await insert.ClickAsync();
        await Expect(insert).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(Page.Locator(".shiny-ribbon__panel")).ToHaveAttributeAsync("aria-label", "Insert");
        await AssertNoConsoleErrors();
    }
}

public class SlideEditorTests : BlazorTest
{
    ILocator PageStatus => Page.Locator(".toolbar .status");
    ILocator SlideItem => Page.Locator(".office-status__item", new() { HasText = "Slide" });

    [Test]
    public async Task Status_Reports_Slides_View_And_Zoom()
    {
        await Open("slide-editor");
        await Expect(SlideItem).ToHaveTextAsync("Slide 1 of 3");
        await Expect(PageStatus).ToHaveTextAsync("3 slides · Normal · fit · 0 edits");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Keyboard_Moves_Between_Slides()
    {
        await Open("slide-editor");
        await Page.Locator(".shiny-slide-editor-canvas").ClickAsync(new() { Position = new() { X = 10, Y = 10 } });
        await Page.Keyboard.PressAsync("PageDown");
        await Expect(SlideItem).ToHaveTextAsync("Slide 2 of 3");
        await Page.Keyboard.PressAsync("PageUp");
        await Expect(SlideItem).ToHaveTextAsync("Slide 1 of 3");
        await AssertNoConsoleErrors();
    }

    [Test]
    [SkipOnPhone("the Office shell goes compact on a phone and drops the view-mode buttons by design")]
    public async Task View_Mode_Flows_Back_To_The_Bound_ViewMode()
    {
        await Open("slide-editor");
        var sorter = Page.Locator(".office-status").GetByRole(AriaRole.Radio, new() { Name = "Slide Sorter" });
        await sorter.ClickAsync();
        await Expect(sorter).ToHaveAttributeAsync("aria-checked", "true");
        await Expect(PageStatus).ToContainTextAsync("SlideSorter");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Zoom_In_Leaves_Fit_And_Binds_A_Percentage()
    {
        await Open("slide-editor");
        var fit = Page.GetByRole(AriaRole.Button, new() { Name = "Fit slide to current window" });
        await Expect(fit).ToHaveAttributeAsync("aria-pressed", "true");

        await Page.Locator(".office-status").GetByRole(AriaRole.Button, new() { Name = "Zoom In" }).ClickAsync();
        await Expect(fit).ToHaveAttributeAsync("aria-pressed", "false");
        await Expect(PageStatus).Not.ToContainTextAsync("fit");

        await fit.ClickAsync();
        await Expect(PageStatus).ToContainTextAsync("· fit ·");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Shell_And_Wrapper_Can_Be_Turned_Off()
    {
        await Open("slide-editor");
        await Page.GetByLabel("Office shell").UncheckAsync();
        await Expect(Page.Locator(".office-titlebar")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".shiny-slide-editor-view")).ToHaveCountAsync(1);

        await Page.GetByLabel("Editor wrapper").UncheckAsync();
        await Expect(Page.Locator(".shiny-slide-editor-view")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".shiny-slide-editor")).ToHaveCountAsync(1);
        await AssertNoConsoleErrors();
    }
}

public class NotebookTests : BlazorTest
{
    ILocator PageStatus => Page.Locator(".toolbar .status");

    [Test]
    public async Task Page_List_Selects_A_Page()
    {
        await Open("notebook");
        await Expect(PageStatus).ToHaveTextAsync("2 sections · 3 pages · 0 edits");

        var sketch = Page.Locator(".notebook-page", new() { HasText = "Whiteboard sketch" });
        await sketch.ClickAsync();
        await Expect(sketch).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-current"));
        await Expect(Page.Locator(".notebook-status")).ToContainTextAsync("Whiteboard sketch");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Section_Tab_Switches_The_Page_List()
    {
        await Open("notebook");
        var research = Page.Locator(".notebook-section", new() { HasText = "Research" });
        await research.ClickAsync();
        await Expect(research).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-current"));
        await Expect(Page.Locator(".notebook-page-title")).ToHaveTextAsync(["Reading list"]);
        await Expect(Page.Locator(".notebook-status")).ToContainTextAsync("Reading list");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Page_List_Checkbox_Hides_The_List()
    {
        await Open("notebook");
        await Expect(Page.Locator(".notebook-pages")).ToHaveCountAsync(1);
        await Page.GetByLabel("Page list").UncheckAsync();
        await Expect(Page.Locator(".notebook-pages")).ToHaveCountAsync(0);
        await Page.GetByLabel("Page list").CheckAsync();
        await Expect(Page.Locator(".notebook-pages")).ToHaveCountAsync(1);
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Chrome_Checkbox_Leaves_The_Bare_Editor()
    {
        await Open("notebook");
        await Page.GetByLabel("Ribbon and navigation").UncheckAsync();
        await Expect(Page.Locator(".shiny-notebook-editor-view")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".shiny-ribbon")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".shiny-notebook-editor")).ToHaveCountAsync(1);
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Writing_On_The_Page_Counts_As_An_Edit()
    {
        await Open("notebook");
        var canvas = Page.Locator(".shiny-notebook-editor-canvas");
        await canvas.ScrollIntoViewIfNeededAsync();
        var box = (await canvas.BoundingBoxAsync())!;

        // Double-click empty canvas (bottom right, away from the sample's content) to start writing.
        // (kept on screen: on a phone the canvas runs past the bottom of the viewport)
        var dy = Math.Min(box.Height - 40, Page.ViewportSize!.Height - 140 - box.Y);
        await canvas.DblClickAsync(new() { Position = new() { X = box.Width - 80, Y = dy } });
        await Page.Keyboard.TypeAsync("Hi");
        await Expect(PageStatus).Not.ToContainTextAsync("0 edits");
        await Expect(PageStatus).ToContainTextAsync("unsaved");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Pen_Tool_Draws_A_Stroke_And_Toggles_Off()
    {
        await Open("notebook");
        await Page.Locator(".shiny-ribbon__tab", new() { HasText = "Draw" }).ClickAsync();
        var panel = Page.Locator(".shiny-ribbon__panel");
        await Expect(panel).ToHaveAttributeAsync("aria-label", "Draw");

        var pen = panel.GetByRole(AriaRole.Button, new() { Name = "Pen", Exact = true });
        var select = panel.GetByRole(AriaRole.Button, new() { Name = "Select", Exact = true });
        await pen.ClickAsync();
        await Expect(pen).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(select).ToHaveAttributeAsync("aria-pressed", "false");

        // A mouse drag with the pen down is an ink stroke, and one undo step.
        await Page.Locator(".shiny-notebook-editor-canvas").ScrollIntoViewIfNeededAsync();
        var box = (await Page.Locator(".shiny-notebook-editor-canvas").BoundingBoxAsync())!;
        // near the bottom right, away from the sample's content - but on screen: on a phone the canvas
        // is taller than the viewport and its bottom edge is out of reach of the mouse
        var x = box.X + Math.Max(20, box.Width - 200);
        var y = Math.Min(box.Y + box.Height - 80, Page.ViewportSize!.Height - 140);
        await Page.Mouse.MoveAsync(x, y);
        await Page.Mouse.DownAsync();
        for (var i = 1; i <= 10; i++)
            await Page.Mouse.MoveAsync(x + i * 12, y + (i % 2) * 10);
        await Page.Mouse.UpAsync();
        await Expect(PageStatus).Not.ToContainTextAsync("0 edits");
        await Expect(PageStatus).ToContainTextAsync("unsaved");

        var undo = Page.Locator(".shiny-ribbon__qat").GetByRole(AriaRole.Button, new() { Name = "Undo (Ctrl+Z)" });
        await Expect(undo).ToBeEnabledAsync();
        await undo.ClickAsync();
        await Expect(undo).ToBeDisabledAsync();

        // Clicking the lit tool again puts it down.
        await pen.ClickAsync();
        await Expect(select).ToHaveAttributeAsync("aria-pressed", "true");
        await AssertNoConsoleErrors();
    }
}

using System.Text.RegularExpressions;

namespace Sample.Blazor.UITests.Features;

/// <summary>
/// Every drag-driven control, driven by a finger on a phone rather than a mouse. A mouse never meets
/// the two things that break touch: the browser deciding at touch-down that the gesture is a scroll
/// (touch-action), and the pointercancel it then fires mid-drag. Each test also has the opposite
/// half where it matters — a swipe that should scroll the page still does.
/// </summary>
[RequiresTouchInput]
public class TouchGestureTests : BlazorTest
{
    protected override bool Phone => true;

    ILocator Section(string heading)
        => Page.Locator(".demo-section").Filter(new() { Has = Page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true }) });

    Task<float> ContentScrollTop()
        => Page.EvaluateAsync<float>("() => document.querySelector('.shiny-applayout-content').scrollTop");


    [Test]
    public async Task Slider_Thumb_Follows_A_Finger()
    {
        await Open("slider");
        var section = Section("Commit on Release (DragCompleted)");
        var (x, y) = await TouchInput.CentreOf(section.Locator(".shiny-gs-thumb"));
        var track = await TouchInput.BoxOf(section.Locator(".shiny-gs-track"));

        await Touch.DragAsync(x, y, track.X + track.Width * 0.9f, y);

        await Expect(section.Locator(".state")).ToContainTextAsync("(1 commits)");
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Range_Slider_Thumb_Follows_A_Finger()
    {
        await Open("rangeslider");
        var section = Section("Comfort Band (min 2°, max 8° apart)");
        var (x, y) = await TouchInput.CentreOf(section.Locator(".shiny-rs-thumb[data-thumb='upper']"));
        var track = await TouchInput.BoxOf(section.Locator(".shiny-rs-track"));

        await Touch.DragAsync(x, y, track.X + track.Width, y);

        await Expect(section.Locator(".state")).ToHaveTextAsync("From 22° to 30°");
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Color_Picker_Hue_Follows_A_Finger()
    {
        await Open("colorpicker");
        var value = Page.Locator(".demo-section", new() { Has = Page.Locator(".shiny-colorpickerbutton") }).Locator("code");
        var hue = await TouchInput.BoxOf(Page.Locator(".demo-section .shiny-colorpicker").Last.Locator(".shiny-colorpicker-hue"));
        var y = hue.Y + hue.Height / 2;

        // the listeners come with a JS module after first render, so retry until one lands
        for (var attempt = 0; attempt < 10 && await value.TextContentAsync() == "#6495ED"; attempt++)
        {
            await Touch.DragAsync(hue.X + hue.Width * 0.1f, y, hue.X + hue.Width * 0.5f, y, steps: 6);
            await Page.WaitForTimeoutAsync(300);
        }

        await Expect(value).Not.ToHaveTextAsync("#6495ED");
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Gamepad_Stick_Follows_A_Finger()
    {
        await Open("gamepad");
        var stick = Page.Locator(".shiny-gamepad__element[data-id='ls'] .shiny-gamepad__shape");
        var (x, y) = await TouchInput.CentreOf(stick);
        var axes = Page.Locator(".gp-hud code").Nth(1);

        await Touch.DownAsync(x, y);
        await Touch.MoveAsync(x, y, x + 60, y, steps: 6);
        await Expect(axes).ToHaveTextAsync(new Regex(@"^0\.[1-9]\d*, |^1\.00, "));

        await Touch.UpAsync();
        await Expect(axes).ToHaveTextAsync("0.00, 0.00");
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Signature_Pad_Takes_Ink_From_A_Finger()
    {
        await Open("signaturepad");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Capture Signature" }).ClickAsync();
        var canvas = Page.Locator(".shiny-sigpad-canvas");
        await Expect(canvas).ToBeInViewportAsync();
        await Page.WaitForTimeoutAsync(600); // the sheet slides up

        var box = await TouchInput.BoxOf(canvas);
        var y = box.Y + box.Height / 2;
        await Touch.DragAsync(box.X + box.Width * 0.2f, y, box.X + box.Width * 0.8f, y - box.Height * 0.2f);

        await Expect(Page.Locator(".shiny-sigpad-btn-sign")).ToBeEnabledAsync();
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Scratch_Card_Scratches_Under_A_Finger_Without_Scrolling_The_Page()
    {
        await Open("scratch-card");
        var section = Section("View on view");
        var card = section.Locator(".shiny-scratch-card");
        var status = section.Locator("p.state");
        var box = await TouchInput.BoxOf(card);
        var scrollBefore = await ContentScrollTop();

        // a vertical stroke - exactly the gesture the page would otherwise take as a scroll
        await Touch.DragAsync(box.X + box.Width / 2, box.Y + 10, box.X + box.Width / 2, box.Y + box.Height - 10);

        await Expect(status).Not.ToHaveTextAsync("Scratched: 0%");
        await Assert.That(await ContentScrollTop()).IsEqualTo(scrollBefore);
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Carousel_Swipes_Sideways_And_Lets_The_Page_Scroll()
    {
        await Open("carousel-advanced");
        var carousel = Page.Locator("article.ex", new() { Has = Page.Locator("h3", new() { HasTextRegex = new Regex("^Default$") }) });
        var box = await TouchInput.BoxOf(carousel.Locator(".shiny-carousel-viewport"));
        var y = box.Y + box.Height / 2;

        await Touch.DragAsync(box.X + box.Width * 0.8f, y, box.X + box.Width * 0.1f, y);
        await Expect(carousel.Locator(".shiny-carousel-dot").Nth(1)).ToHaveClassAsync(new Regex(@"\bactive\b"));

        // an up-swipe over the carousel is the page's, not the carousel's
        var before = await ContentScrollTop();
        await Touch.DragAsync(box.X + box.Width / 2, y + 60, box.X + box.Width / 2, y - 120);
        await Page.WaitForTimeoutAsync(300);
        await Assert.That(await ContentScrollTop()).IsGreaterThan(before);
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Cover_Flow_Swipes_Sideways()
    {
        await Open("coverflow");
        var flow = Page.Locator(".shiny-coverflow").First;
        var position = Page.Locator(".cf-controls strong").First;
        await Expect(position).ToHaveTextAsync("4");
        var box = await TouchInput.BoxOf(flow.Locator(".shiny-coverflow-stage"));
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height * 0.35f;

        await Touch.DragAsync(x, y, x - 200, y);

        await Expect(position).Not.ToHaveTextAsync("4");
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Kanban_Card_Drags_After_A_Long_Press()
    {
        await Open("kanban");
        var card = Page.Locator("[data-kanban-card='k1']");
        var from = await TouchInput.BoxOf(card);
        var x = from.X + from.Width / 2;
        var y = from.Y + Math.Min(20, from.Height / 2);

        // press and hold, then carry the card to the edge and wait for the next lane to scroll in
        await Touch.DownAsync(x, y);
        await Page.WaitForTimeoutAsync(600);
        await Touch.MoveAsync(x, y, x + 12, y + 12, steps: 3);
        var vw = await Page.EvaluateAsync<float>("() => window.innerWidth");
        var board = (await Page.Locator(".shiny-kanban").First.BoundingBoxAsync())!;
        var edge = board.X + board.Width - 24;
        await Touch.MoveAsync(x + 12, y + 12, edge, y + 40, steps: 10);

        var ready = Page.Locator("[data-kanban-cell][data-kanban-column='ready']").First;
        for (var i = 0; i < 60 && ((await ready.BoundingBoxAsync())?.X ?? vw) > vw * 0.5f; i++)
            await Page.WaitForTimeoutAsync(30);

        var lane = (await ready.BoundingBoxAsync())!;
        // inside the lane but clear of the edge zone, so the board stops scrolling under the finger
        await Touch.MoveAsync(edge, y + 40, lane.X + Math.Min(60, lane.Width / 2), y + 40, steps: 4);
        await Page.WaitForTimeoutAsync(150);
        await Touch.UpAsync();

        await Expect(Page.Locator("p.status-message")).ToHaveTextAsync("Moved \"Audit the theme tokens\" to Ready");
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Kanban_Quick_Swipe_Scrolls_Instead_Of_Dragging()
    {
        await Open("kanban");
        var card = Page.Locator("[data-kanban-card='k1']");
        var from = await TouchInput.BoxOf(card);
        var board = Page.Locator(".shiny-kanban").First;
        var before = await board.EvaluateAsync<float>("b => b.scrollLeft");

        // a finger that moves straight away is scrolling the board, not picking the card up
        await Touch.DragAsync(from.X + from.Width * 0.8f, from.Y + 20, from.X + 10, from.Y + 24);
        await Page.WaitForTimeoutAsync(300);

        await Assert.That(await board.EvaluateAsync<float>("b => b.scrollLeft")).IsGreaterThan(before);
        await Expect(Page.Locator("p.status-message")).Not.ToHaveTextAsync(new Regex("^Moved "));
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Gantt_Bar_Drags_With_A_Finger()
    {
        await Open("gantt");
        var (x, y) = await TouchInput.VisibleCentreOf(Page.Locator(".shiny-gantt-bar[title^='API']"));

        await Touch.DragAsync(x, y, x + 60, y, steps: 10);

        await Expect(Page.Locator("p.status-message")).ToHaveTextAsync(new Regex(@"^API: Move"));
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Diagram_Node_Drags_With_A_Finger()
    {
        await Open("diagram");
        var label = Page.Locator("text.shiny-diagram-label", new() { HasTextString = "Platform" });
        var before = await label.GetAttributeAsync("x");
        var (x, y) = await TouchInput.CentreOf(label);

        await Touch.DragAsync(x, y, x + 60, y + 40, steps: 8);

        await Expect(Page.Locator("p.status-message")).ToHaveTextAsync(new Regex("^Move"));
        await Expect(label).Not.ToHaveAttributeAsync("x", before!);
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Data_Grid_Column_Resizes_With_A_Finger()
    {
        await Open("datagrid");
        var section = Page.Locator("section.demo-section", new() { Has = Page.Locator("h2", new() { HasTextRegex = new Regex("^Column resizing \\(min / max width\\)$") }) });
        var header = section.Locator("th.shiny-dg-header", new() { Has = Page.Locator(".shiny-dg-header-text", new() { HasTextRegex = new Regex("^First$") }) });
        var (x, y) = await TouchInput.CentreOf(header.Locator(".shiny-dg-resize-handle"));

        await Touch.DragAsync(x, y, x + 40, y, steps: 6);

        await Expect(section.Locator(".status-message")).ToHaveTextAsync(new Regex(@"^FirstName → \d+px$"));
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Sheet_Handle_Drags_Up_A_Detent()
    {
        await Open("sheet");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Open Sheet", Exact = true }).ClickAsync();
        var sheet = Page.Locator(".shiny-sheet-root").Nth(0);
        var container = sheet.Locator(".shiny-sheet-container");
        var viewport = Page.ViewportSize!.Height;
        await Expect(container).ToHaveAttributeAsync("style", new Regex($@"--sheet-visible-height:\s*{viewport / 4}px"));
        await Page.WaitForTimeoutAsync(400); // the sheet slides up

        var (x, y) = await TouchInput.CentreOf(sheet.Locator(".shiny-sheet-handle"));
        await Touch.DragAsync(x, y, x, y - viewport / 4f, steps: 10);

        await Expect(container).ToHaveAttributeAsync("style", new Regex($@"--sheet-visible-height:\s*{viewport / 2}px"));
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Notebook_Pen_Inks_With_A_Finger()
    {
        await Open("notebook");
        await Page.Locator(".shiny-ribbon__tab", new() { HasText = "Draw" }).ClickAsync();
        var pen = Page.Locator(".shiny-ribbon__panel").GetByRole(AriaRole.Button, new() { Name = "Pen", Exact = true });
        await pen.ClickAsync();
        await Expect(pen).ToHaveAttributeAsync("aria-pressed", "true");

        var box = await TouchInput.BoxOf(Page.Locator(".shiny-notebook-editor-canvas"));
        var x = box.X + box.Width * 0.3f;
        var y = box.Y + box.Height * 0.6f;
        await Touch.DragAsync(x, y, x + box.Width * 0.4f, y + 20, steps: 10);

        await Expect(Page.Locator(".toolbar .status")).Not.ToContainTextAsync("0 edits");
        await AssertNoConsoleErrors();
    }
}

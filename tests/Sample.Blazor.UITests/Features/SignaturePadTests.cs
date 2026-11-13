namespace Sample.Blazor.UITests.Features;

/// <summary>
/// SignaturePad: a sheet with a drawing canvas. Sign stays disabled until there is ink; signing
/// exports a PNG to the page, cancelling reports back, and Clear wipes the ink.
/// </summary>
public class SignaturePadTests : BlazorTest
{
    ILocator Pad => Page.Locator(".shiny-sigpad");
    ILocator SignButton => Page.Locator(".shiny-sigpad-btn-sign");
    ILocator Status => Page.Locator(".demo-section .state code");

    async Task OpenPad()
    {
        await Open("signaturepad");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Capture Signature" }).ClickAsync();
        await Expect(Page.Locator(".shiny-sigview-canvas")).ToBeInViewportAsync();

        // The sheet slides up; drawing before it settles lands the stroke off the canvas.
        var canvas = Page.Locator(".shiny-sigview-canvas");
        LocatorBoundingBoxResult? last = null;
        for (var i = 0; i < 40; i++)
        {
            var box = await canvas.BoundingBoxAsync();
            if (box != null && last != null && Math.Abs(box.Y - last.Y) < 0.5 && Math.Abs(box.Height - last.Height) < 0.5)
                return;

            last = box;
            await Page.WaitForTimeoutAsync(100);
        }
        Assert.Fail("The signature sheet never settled");
    }

    async Task DrawStroke()
    {
        var canvas = Page.Locator(".shiny-sigview-canvas");
        var box = (await canvas.BoundingBoxAsync())!;
        var y = box.Y + box.Height / 2;

        await Page.Mouse.MoveAsync(box.X + box.Width * 0.2f, y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(box.X + box.Width * 0.5f, y - box.Height * 0.2f, new() { Steps = 8 });
        await Page.Mouse.MoveAsync(box.X + box.Width * 0.8f, y + box.Height * 0.1f, new() { Steps = 8 });
        await Page.Mouse.UpAsync();
    }


    [Test]
    public async Task Sign_Is_Disabled_Until_Something_Is_Drawn()
    {
        await OpenPad();
        await Expect(SignButton).ToBeDisabledAsync();

        await DrawStroke();

        await Expect(SignButton).ToBeEnabledAsync();
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Signing_Exports_An_Image_And_Closes_The_Sheet()
    {
        await OpenPad();
        await DrawStroke();
        await SignButton.ClickAsync();

        await Expect(Status).ToHaveTextAsync("Signature captured!");
        var image = Page.GetByAltText("Captured signature");
        await Expect(image).ToBeVisibleAsync();
        await Expect(image).ToHaveAttributeAsync("src", new System.Text.RegularExpressions.Regex("^data:image/png;base64,.{100,}"));
        await Expect(Page.Locator(".shiny-sigview-canvas")).Not.ToBeInViewportAsync();
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Clear_Signature_Removes_The_Captured_Image()
    {
        await OpenPad();
        await DrawStroke();
        await SignButton.ClickAsync();
        await Expect(Page.GetByAltText("Captured signature")).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Clear Signature" }).ClickAsync();

        await Expect(Status).ToHaveTextAsync("Signature cleared");
        await Expect(Page.GetByAltText("Captured signature")).ToHaveCountAsync(0);
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Cancel_Reports_Back_And_Captures_Nothing()
    {
        await OpenPad();
        await DrawStroke();

        await Pad.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();

        await Expect(Status).ToHaveTextAsync("Signature cancelled");
        await Expect(Page.GetByAltText("Captured signature")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".shiny-sigview-canvas")).Not.ToBeInViewportAsync();
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Clear_Wipes_The_Ink_And_Disables_Sign()
    {
        await OpenPad();
        await DrawStroke();
        await Expect(SignButton).ToBeEnabledAsync();

        await Pad.GetByRole(AriaRole.Button, new() { Name = "Clear", Exact = true }).ClickAsync();

        await Expect(SignButton).ToBeDisabledAsync();
        await AssertNoConsoleErrors();
    }
}

using System.Text.RegularExpressions;

namespace Sample.Blazor.UITests.Features;

/// <summary>
/// ScratchCard: strokes mask the cover away, the percentage climbs, the threshold fires once, and
/// AutoReveal either clears the cover (and lets the content take clicks) or leaves it alone.
/// </summary>
public class ScratchCardTests : BlazorTest
{
    ILocator Section(string heading)
        => Page.Locator("section.demo-section", new() { Has = Page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true }) });

    ILocator Prize => Section("View on view").Locator(".shiny-scratch-card");
    ILocator PrizeStatus => Section("View on view").Locator("p.state");
    ILocator ImageCard => Section("Image cover, threshold event only").Locator(".shiny-scratch-card");
    ILocator ImageStatus => Section("Image cover, threshold event only").Locator("p.state");

    static Task<string> MaskOf(ILocator card)
        => card.Locator(".shiny-scratch-card__cover").EvaluateAsync<string>("el => el.style.maskImage || el.style.webkitMaskImage || ''");

    /// <summary>Mouse strokes across the cover, top to bottom, <paramref name="rows"/> of them.</summary>
    async Task Scratch(ILocator card, int rows, float coverage = 1f)
    {
        await card.ScrollIntoViewIfNeededAsync();
        var box = (await card.BoundingBoxAsync())!;
        for (var i = 0; i < rows; i++)
        {
            var y = box.Y + box.Height * (i + 0.5f) / rows * coverage;
            await Page.Mouse.MoveAsync(box.X + 4, y);
            await Page.Mouse.DownAsync();
            await Page.Mouse.MoveAsync(box.X + box.Width - 4, y, new() { Steps = 10 });
            await Page.Mouse.UpAsync();
        }
    }

    static async Task<double> Percent(ILocator status)
    {
        var text = await status.InnerTextAsync();
        var m = Regex.Match(text, @"(\d+)%");
        return m.Success ? double.Parse(m.Groups[1].Value) : -1;
    }


    [Test]
    public async Task A_Stroke_Masks_The_Cover_And_Reports_Progress()
    {
        await Open("scratch-card");
        await Expect(PrizeStatus).ToHaveTextAsync("Scratched: 0%");
        await Assert.That(await MaskOf(Prize)).IsEqualTo("");

        await Scratch(Prize, 1);

        await Expect(PrizeStatus).Not.ToHaveTextAsync("Scratched: 0%");
        var percent = await Percent(PrizeStatus);
        await Assert.That(percent).IsGreaterThan(5);
        await Assert.That(percent).IsLessThan(55);
        await Assert.That(await MaskOf(Prize)).StartsWith("url(\"data:image/png");
        await Expect(Prize).Not.ToHaveClassAsync(new Regex("is-revealed"));
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task The_Content_Cannot_Be_Clicked_Through_A_Hole_Before_The_Threshold()
    {
        await Open("scratch-card");
        var content = Prize.Locator(".shiny-scratch-card__content");
        await Expect(content).ToHaveAttributeAsync("inert", "");

        // scratch straight over the Claim button, then click it through the hole
        var claim = Prize.GetByRole(AriaRole.Button, new() { Name = "Claim", IncludeHidden = true });
        var box = (await claim.BoundingBoxAsync())!;
        var y = box.Y + box.Height / 2;
        await Page.Mouse.MoveAsync(box.X - 20, y);
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(box.X + box.Width + 20, y, new() { Steps = 8 });
        await Page.Mouse.UpAsync();
        await Expect(PrizeStatus).Not.ToHaveTextAsync("Scratched: 0%");
        await Assert.That(await Percent(PrizeStatus)).IsLessThan(55);

        await Page.Mouse.ClickAsync(box.X + box.Width / 2, y);
        await Page.WaitForTimeoutAsync(300);
        await Expect(PrizeStatus).Not.ToHaveTextAsync("Claimed!");
        await Expect(content).ToHaveAttributeAsync("inert", "");
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Passing_The_Threshold_Reveals_And_The_Content_Takes_Clicks()
    {
        await Open("scratch-card");

        await Scratch(Prize, 5);

        await Expect(Prize).ToHaveClassAsync(new Regex("is-revealed"));
        await Expect(PrizeStatus).ToHaveTextAsync("Revealed!");

        // the cover fades, then leaves the DOM so nothing is left over the content
        await Expect(Prize.Locator(".shiny-scratch-card__cover")).ToHaveCountAsync(0);
        await Prize.GetByRole(AriaRole.Button, new() { Name = "Claim" }).ClickAsync();
        await Expect(PrizeStatus).ToHaveTextAsync("Claimed!");
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Reset_Puts_A_Fresh_Cover_Back_And_It_Scratches_Again()
    {
        await Open("scratch-card");
        await Scratch(Prize, 5);
        await Expect(Prize.Locator(".shiny-scratch-card__cover")).ToHaveCountAsync(0);

        await Section("View on view").GetByRole(AriaRole.Button, new() { Name = "Reset" }).ClickAsync();

        await Expect(Prize.Locator(".shiny-scratch-card__cover")).ToHaveCountAsync(1);
        await Expect(Prize).Not.ToHaveClassAsync(new Regex("is-revealed"));
        await Expect(PrizeStatus).ToHaveTextAsync("Scratched: 0%");
        await Assert.That(await MaskOf(Prize)).IsEqualTo("");

        await Scratch(Prize, 1);
        await Expect(PrizeStatus).Not.ToHaveTextAsync("Scratched: 0%");
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Reveal_From_Code_Clears_Without_Scratching()
    {
        await Open("scratch-card");

        await Section("View on view").GetByRole(AriaRole.Button, new() { Name = "Reveal" }).ClickAsync();

        await Expect(Prize).ToHaveClassAsync(new Regex("is-revealed"));
        await Expect(PrizeStatus).ToHaveTextAsync("Revealed!");
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task Without_AutoReveal_The_Threshold_Fires_But_The_Cover_Stays()
    {
        await Open("scratch-card");
        await Expect(ImageStatus).ToHaveTextAsync("Scratch 40% to hit the threshold");

        await Scratch(ImageCard, 3);

        await Expect(ImageStatus).ToHaveTextAsync(new Regex(@"OnThresholdReached fired at \d+%"));
        await Assert.That(await Percent(ImageStatus)).IsGreaterThanOrEqualTo(40);
        await Expect(ImageCard).Not.ToHaveClassAsync(new Regex("is-revealed"));
        await Expect(ImageCard.Locator(".shiny-scratch-card__cover")).ToHaveCountAsync(1);

        // the content unlocks at the threshold even though the cover stays painted
        await Expect(ImageCard).ToHaveClassAsync(new Regex("is-unlocked"));
        await Expect(ImageCard.Locator(".shiny-scratch-card__content")).Not.ToHaveAttributeAsync("inert", "");
        var pointerEvents = await ImageCard.Locator(".shiny-scratch-card__cover").EvaluateAsync<string>("el => getComputedStyle(el).pointerEvents");
        await Assert.That(pointerEvents).IsEqualTo("none");
        await AssertNoConsoleErrors();
    }


    [Test]
    public async Task The_Card_Draws_No_Border_Of_Its_Own()
    {
        await Open("scratch-card");

        var width = await Prize.EvaluateAsync<string>("el => getComputedStyle(el).borderTopWidth");
        await Assert.That(width).IsEqualTo("0px");
    }
}

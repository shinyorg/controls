namespace Sample.Blazor.UITests.Features;

public class ChatTests : BlazorTest
{
    static readonly string[] ReplyBodies = ["That's awesome!", "Tell me more!", "Ha, nice one", "Sounds good to me.", "Let's do it!"];

    ILocator Input => Page.Locator("textarea.shiny-chat-input");
    ILocator SendButton => Page.Locator(".shiny-chat-entry button.shiny-chat-send");
    ILocator Mine => Page.Locator(".shiny-chat-bubble-wrap.is-me");
    ILocator Others => Page.Locator(".shiny-chat-bubble-wrap.is-other");
    ILocator MyMessage(string text) => Mine.Filter(new() { Has = Page.Locator(".shiny-chat-text", new() { HasTextString = text }) });

    Task AssertClean() => AssertNoConsoleErrors();

    static string Unique(string prefix) => $"{prefix} {Guid.NewGuid().ToString("N")[..8]}";

    async Task OpenChat()
    {
        await Open("chat");
        await Expect(Page.GetByText("Same here! Really clean API.")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Send_Button_Posts_The_Message_And_Clears_The_Input()
    {
        await OpenChat();
        var text = Unique("hello from the send button");

        await Input.FillAsync(text);
        await SendButton.ClickAsync();

        await Expect(MyMessage(text)).ToHaveCountAsync(1);
        await Expect(Input).ToHaveValueAsync("");
        await AssertClean();
    }

    [Test]
    public async Task Enter_Sends_And_Shift_Enter_Does_Not()
    {
        await OpenChat();
        var text = Unique("enter key");

        await Input.FillAsync(text);
        await Input.PressAsync("Shift+Enter");
        await Page.WaitForTimeoutAsync(300);
        await Expect(MyMessage(text)).ToHaveCountAsync(0);

        await Input.PressAsync("Enter");
        await Expect(MyMessage(text)).ToHaveCountAsync(1);
        await AssertClean();
    }

    [Test]
    public async Task Enter_Send_Leaves_The_Composer_Empty()
    {
        await OpenChat();

        await Input.FillAsync(Unique("enter clears"));
        await Input.PressAsync("Enter");
        await Expect(Input).ToHaveValueAsync("");
        await AssertClean();
    }

    [Test]
    public async Task Blank_Input_Sends_Nothing()
    {
        await OpenChat();
        var before = await Mine.CountAsync();

        await Input.FillAsync("   ");
        await SendButton.ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        await Expect(Mine).ToHaveCountAsync(before);
        await AssertClean();
    }

    [Test]
    public async Task Reply_Arrives_After_A_Typing_Indicator()
    {
        await OpenChat();
        var others = await Others.CountAsync();

        await Input.FillAsync(Unique("anyone there?"));
        await SendButton.ClickAsync();

        await Expect(Page.Locator(".shiny-chat-typing")).ToBeVisibleAsync(new() { Timeout = 5_000 });
        await Expect(Others).ToHaveCountAsync(others + 1, new() { Timeout = 10_000 });
        await Expect(Page.Locator(".shiny-chat-typing")).ToHaveCountAsync(0);

        var reply = await Others.Last.Locator(".shiny-chat-text").InnerTextAsync();
        await Assert.That(ReplyBodies.Any(reply.Contains)).IsTrue();
        await AssertClean();
    }

    ILocator Messages => Page.Locator(".shiny-chat-messages");
    ILocator NewMessagesPill => Page.Locator(".shiny-chat-newmessages");

    Task<double> GapFromBottom() => Messages.EvaluateAsync<double>("el => el.scrollHeight - el.scrollTop - el.clientHeight");

    // the pill scrolls smoothly, so the list reaches the bottom a little after the click
    async Task WaitForBottom()
    {
        var gap = await GapFromBottom();
        for (var i = 0; i < 40 && gap >= 2; i++)
        {
            await Page.WaitForTimeoutAsync(100);
            gap = await GapFromBottom();
        }
        await Assert.That(gap).IsLessThan(2);
    }

    [Test]
    public async Task Opens_At_The_Newest_Message()
    {
        await OpenChat();
        await Page.WaitForTimeoutAsync(1500);

        await Assert.That(await GapFromBottom()).IsLessThan(2);
        await Expect(NewMessagesPill).ToHaveCountAsync(0);
        await AssertClean();
    }

    [Test]
    public async Task A_Reply_While_Scrolled_Up_Waits_Behind_The_New_Messages_Pill()
    {
        await OpenChat();
        var others = await Others.CountAsync();

        await Input.FillAsync(Unique("then scroll away"));
        await SendButton.ClickAsync();
        await Page.WaitForTimeoutAsync(400);
        await Messages.EvaluateAsync("el => el.scrollTop = el.scrollHeight / 2");

        await Expect(Others).ToHaveCountAsync(others + 1, new() { Timeout = 10_000 });
        await Expect(NewMessagesPill).ToHaveTextAsync("1 New Message");
        await Assert.That(await GapFromBottom()).IsGreaterThan(100);

        await NewMessagesPill.ClickAsync();
        await Expect(NewMessagesPill).ToHaveCountAsync(0);
        await WaitForBottom();
        await AssertClean();
    }

    [Test]
    public async Task Scrolling_Back_Down_Clears_The_New_Messages_Pill()
    {
        await OpenChat();
        var others = await Others.CountAsync();

        await Input.FillAsync(Unique("scroll back yourself"));
        await SendButton.ClickAsync();
        await Page.WaitForTimeoutAsync(400);
        await Messages.EvaluateAsync("el => el.scrollTop = el.scrollHeight / 2");
        await Expect(Others).ToHaveCountAsync(others + 1, new() { Timeout = 10_000 });
        await Expect(NewMessagesPill).ToBeVisibleAsync();

        await Messages.EvaluateAsync("el => el.scrollTop = el.scrollHeight");
        await Expect(NewMessagesPill).ToHaveCountAsync(0);
        await AssertClean();
    }

    [Test]
    public async Task Own_Message_Can_Be_Deleted_From_Its_Actions()
    {
        await OpenChat();
        var text = Unique("delete me");
        await Input.FillAsync(text);
        await SendButton.ClickAsync();

        var bubble = MyMessage(text);
        await Expect(bubble).ToHaveCountAsync(1);

        await bubble.Locator(".shiny-chat-actions-btn").ClickAsync();
        var menu = bubble.Locator(".shiny-chat-actions-menu");
        await Expect(menu.GetByRole(AriaRole.Button, new() { Name = "Edit" })).ToBeVisibleAsync();
        await menu.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();

        await Expect(bubble).ToHaveCountAsync(0);
        await AssertClean();
    }

    [Test]
    public async Task Others_Messages_Offer_No_Edit_Or_Delete()
    {
        await OpenChat();

        var bubble = Others.Filter(new() { HasTextString = "Same here! Really clean API." });
        await bubble.Locator(".shiny-chat-actions-btn").ClickAsync();

        var menu = bubble.Locator(".shiny-chat-actions-menu");
        await Expect(menu.GetByRole(AriaRole.Button, new() { Name = "React" })).ToBeVisibleAsync();
        await Expect(menu.GetByRole(AriaRole.Button, new() { Name = "Edit" })).ToHaveCountAsync(0);
        await Expect(menu.GetByRole(AriaRole.Button, new() { Name = "Delete" })).ToHaveCountAsync(0);
        await AssertClean();
    }

    [Test]
    public async Task Clicking_A_Reaction_Badge_Toggles_My_Reaction()
    {
        await OpenChat();

        // seeded: Alice and Bob both gave this one a thumbs up
        var bubble = Mine.Filter(new() { HasTextString = "I agree, the styling system is great." });
        var thumbs = bubble.Locator(".shiny-chat-ack-badge", new() { HasTextString = "\U0001F44D" });
        await Expect(thumbs.Locator(".shiny-chat-ack-count")).ToHaveTextAsync("2");
        await Expect(thumbs).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-mine"));

        await thumbs.ClickAsync();
        await Expect(thumbs.Locator(".shiny-chat-ack-count")).ToHaveTextAsync("3");
        await Expect(thumbs).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-mine"));

        await thumbs.ClickAsync();
        await Expect(thumbs.Locator(".shiny-chat-ack-count")).ToHaveTextAsync("2");
        await Expect(thumbs).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-mine"));
        await AssertClean();
    }

    [Test]
    public async Task Bold_Button_Wraps_Text_And_Renders_Bold()
    {
        await OpenChat();

        await Page.GetByRole(AriaRole.Button, new() { Name = "B", Exact = true }).ClickAsync();
        await Expect(Input).ToHaveValueAsync("**bold**");

        await SendButton.ClickAsync();
        await Expect(Mine.Last.Locator(".shiny-chat-text strong, .shiny-chat-text b")).ToHaveTextAsync("bold");
        await AssertClean();
    }
}

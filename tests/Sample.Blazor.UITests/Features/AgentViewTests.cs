using System.Text.RegularExpressions;

namespace Sample.Blazor.UITests.Features;

/// <summary>
/// The AG-UI agent page end to end: every scripted demo scenario (text and reasoning, the dashboard
/// surface, the trip frontend tool, the newsletter form and its checks, approve / reject, one choice,
/// several choices, free text, the media card, the sub-agent, shared state, error and Retry, Stop),
/// the Recordings source replaying a real server's streams, the Endpoint source's error handling,
/// and "Your key" against a local OpenAI-compatible mock - streamed text and a render_ui surface.
/// </summary>
public class AgentViewTests : BlazorTest
{
    ILocator Chat => Page.Locator(".agent-page-chat");
    ILocator Composer => Chat.Locator("textarea.agent-input-text");
    ILocator SendButton => Chat.Locator("button.agent-send:not(.agent-send--stop)");
    ILocator StopButton => Chat.Locator("button.agent-send--stop");
    ILocator Suggestions => Chat.Locator(".agent-suggestion");
    ILocator UserBubbles => Chat.Locator(".agent-row--user .agent-bubble");
    ILocator Assistant => Chat.Locator(".agent-row--assistant");
    ILocator Activities => Chat.Locator(".agent-activity");
    ILocator Interrupt => Chat.Locator(".agent-interrupt");
    ILocator ToolCards => Chat.Locator(".agent-tool");
    ILocator Errors => Chat.Locator(".agent-error");
    ILocator SourceBar => Page.GetByTestId("agent-source");
    ILocator Status => SourceBar.Locator(".agent-source-status");

    ILocator SourceTab(string name) => SourceBar.GetByRole(AriaRole.Tab, new() { Name = name, Exact = true });

    async Task OpenAgent()
    {
        await Open("agent-view");
        await Expect(Suggestions.First).ToBeVisibleAsync();
    }

    async Task Send(string text)
    {
        await Composer.FillAsync(text);
        await SendButton.ClickAsync();
        await Expect(UserBubbles.Last).ToHaveTextAsync(text);
    }

    /// <summary>Waits for the run to end: the Stop button is back to Send.</summary>
    async Task Idle(int timeout = 20_000)
    {
        await Expect(StopButton).ToHaveCountAsync(0, new() { Timeout = timeout });
        await Expect(Chat.Locator(".agent-working")).ToHaveCountAsync(0, new() { Timeout = timeout });
    }

    async Task Ask(string text)
    {
        await Send(text);
        await Idle();
    }

    Task ExpectReply(string text) => Expect(Assistant.Last).ToContainTextAsync(text, new() { Timeout = 20_000 });

    /// <summary>The media and spec URLs are on the internet; the tests never depend on it.</summary>
    Task BlockRemoteMedia() => Page.RouteAsync(new Regex(@"^https://(interactive-examples\.mdn\.mozilla\.net|a2ui\.org)/"), r => r.AbortAsync());

    // ---- demo scenarios -------------------------------------------------------------------------

    [Test]
    public async Task Suggestion_Chips_Send_Their_Prompt_And_Go_Away()
    {
        await OpenAgent();
        await Expect(Suggestions).ToHaveCountAsync(12);
        await Expect(SourceTab("Demo")).ToHaveAttributeAsync("aria-selected", "true");

        await Suggestions.Filter(new() { HasTextString = "What can you do?" }).ClickAsync();
        await Expect(UserBubbles.Last).ToHaveTextAsync("What can you do?");
        await Expect(Suggestions).ToHaveCountAsync(0);
        await Idle();
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Text_Streams_With_Collapsible_Reasoning()
    {
        await OpenAgent();
        await Ask("What can you do?");

        await ExpectReply("scripted demo agent");
        await Expect(Assistant.Last.Locator("strong").First).ToHaveTextAsync("scripted demo agent");
        await Expect(Assistant.Last.Locator("li")).ToHaveCountAsync(9);

        var toggle = Chat.Locator(".agent-reasoning-toggle").Last;
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await toggle.ClickAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(Chat.Locator(".agent-reasoning-text").Last).ToContainTextAsync("The user wants to know what I can do");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Stop_Ends_A_Streaming_Run()
    {
        await OpenAgent();
        await Send("What can you do?");

        await StopButton.ClickAsync(new() { Timeout = 5_000 });
        await Idle();
        await Expect(SendButton).ToBeVisibleAsync();
        // stopped long before the last bullet
        await Expect(Chat).Not.ToContainTextAsync("a run error with Retry");

        // and the conversation carries on
        await Ask("Order lunch");
        await Expect(Interrupt).ToBeVisibleAsync();
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Dashboard_Grid_Chips_Expander_And_Refresh()
    {
        await OpenAgent();
        await Suggestions.Filter(new() { HasTextString = "dashboard" }).ClickAsync();
        await Idle();

        var surface = Activities.Last;
        await Expect(surface).ToContainTextAsync("October sales");
        await Expect(ToolCards.Locator(".agent-tool-name").First).ToHaveTextAsync("query_sales");
        await ExpectReply("Here's October");

        // a grid row runs rowAction with that row's id
        await surface.Locator(".a2ui-datagrid tbody tr", new() { HasTextString = "A-1001" }).ClickAsync();
        await Idle();
        await ExpectReply("Order A-1001 shipped on time");

        // the chip group writes the selection back to the model, which Refresh sends
        var asia = surface.Locator(".shiny-chips__body", new() { Has = Page.Locator(".shiny-chips__label", new() { HasTextString = "Asia" }) });
        await Expect(asia).ToHaveAttributeAsync("aria-pressed", "false");
        await asia.ClickAsync();
        await Expect(asia).ToHaveAttributeAsync("aria-pressed", "true");

        var expander = surface.Locator(".shiny-expander__header", new() { HasTextString = "How this is calculated" });
        await Expect(expander).ToHaveAttributeAsync("aria-expanded", "false");
        await expander.ClickAsync();
        await Expect(expander).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(surface.GetByText("net of tax")).ToBeVisibleAsync();

        await surface.GetByRole(AriaRole.Button, new() { Name = "Refresh", Exact = true }).ClickAsync();
        await Idle();
        await ExpectReply("in 3 region(s)");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Trip_Frontend_Tool_Is_Answered_By_The_Inline_Date_Range_Prompt()
    {
        await OpenAgent();
        // the run stays busy while the frontend tool waits on the user
        await Send("Book a trip");

        await Expect(ToolCards.Locator(".agent-tool-name").Last).ToHaveTextAsync("pick_date_range");
        var prompt = Chat.Locator(".agent-prompt");
        await Expect(prompt).ToBeVisibleAsync();
        await Expect(prompt.Locator(".agent-prompt-title")).ToHaveTextAsync("When are you travelling?");

        var start = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        var end = start.AddDays(3);
        await PickRange(prompt, start, end);

        await Idle();
        await ExpectReply("Booked!");
        await Expect(Assistant.Last).ToContainTextAsync(start.ToString("yyyy-MM-dd"));
        await Expect(Assistant.Last).ToContainTextAsync(end.ToString("yyyy-MM-dd"));
        await Expect(prompt).ToHaveCountAsync(0);
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Newsletter_Form_Shows_A_Failing_Check_Then_Submits()
    {
        await OpenAgent();
        await Ask("Sign me up for the newsletter");

        var form = Activities.Last;
        await Expect(form).ToContainTextAsync("Shiny newsletter");
        var email = form.GetByLabel("Email");

        await form.GetByLabel("Name").FillAsync("Ada");
        await email.FillAsync("not-an-email");
        // the field's check fails, and says so once the field is touched
        await Expect(email).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(form.GetByRole(AriaRole.Alert)).ToHaveTextAsync("That doesn't look like an email address");

        await email.FillAsync("ada@example.com");
        await Expect(form.GetByRole(AriaRole.Alert)).ToHaveCountAsync(0);
        await Expect(email).Not.ToHaveAttributeAsync("aria-invalid", "true");
        await form.GetByRole(AriaRole.Button, new() { Name = "Subscribe" }).ClickAsync();

        await Idle();
        await ExpectReply("Thanks Ada");
        await Expect(Assistant.Last).ToContainTextAsync("ada@example.com");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Approval_Interrupt_Approve()
    {
        await OpenAgent();
        await Ask("Delete my old drafts");

        await Expect(Interrupt).ToContainTextAsync("Delete 3 drafts from before October?");
        await Interrupt.GetByRole(AriaRole.Button, new() { Name = "Approve" }).ClickAsync();
        await Idle();
        await ExpectReply("I deleted 3 drafts");
        await Expect(Interrupt).ToHaveCountAsync(0);
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Approval_Interrupt_Reject()
    {
        await OpenAgent();
        await Ask("Delete my old drafts");

        await Interrupt.GetByRole(AriaRole.Button, new() { Name = "Reject" }).ClickAsync();
        await Idle();
        await ExpectReply("I left your drafts alone");
        await Expect(Interrupt).ToHaveCountAsync(0);
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Choices_Interrupt_Takes_One()
    {
        await OpenAgent();
        await Ask("Order lunch");

        await Expect(Interrupt).ToContainTextAsync("Pick today's lunch");
        await Expect(Interrupt.Locator(".agent-btn")).ToHaveCountAsync(3);
        await Interrupt.GetByRole(AriaRole.Button, new() { Name = "Falafel wrap" }).ClickAsync();
        await Idle();
        await ExpectReply("Ordered: Falafel wrap");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Choices_Interrupt_Takes_Several()
    {
        await OpenAgent();
        await Ask("Pick pizza toppings");

        await Expect(Interrupt).ToContainTextAsync("Which toppings?");
        var submit = Interrupt.GetByRole(AriaRole.Button, new() { Name = "Submit" });
        await Expect(submit).ToBeDisabledAsync();
        // the chips show each choice's label, not the record
        await Expect(Interrupt.Locator(".shiny-chips__label")).ToHaveTextAsync(["Extra cheese", "Mushroom", "Olive", "Pepper", "Basil"]);

        foreach (var topping in new[] { "Mushroom", "Basil" })
        {
            var chip = Interrupt.Locator(".shiny-chips__body", new() { Has = Page.Locator(".shiny-chips__label", new() { HasTextString = topping }) });
            await chip.ClickAsync();
            await Expect(chip).ToHaveAttributeAsync("aria-pressed", "true");
        }
        await submit.ClickAsync();
        await Idle();
        await ExpectReply("Mushroom, Basil");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Text_Interrupt_Takes_A_Reply()
    {
        await OpenAgent();
        await Ask("Rename my project");

        await Expect(Interrupt).ToContainTextAsync("What should the project be called?");
        var send = Interrupt.GetByRole(AriaRole.Button, new() { Name = "Send" });
        await Expect(send).ToBeDisabledAsync();
        await Interrupt.Locator("input.agent-input").FillAsync("Sparkle");
        await send.ClickAsync();
        await Idle();
        await ExpectReply("Renamed to Sparkle");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Media_Card_Has_Players_And_Opens_The_Spec()
    {
        await BlockRemoteMedia();
        await OpenAgent();
        // record what the surface asks the browser to open, instead of opening it
        await Page.EvaluateAsync("() => { window.__opened = []; window.open = (...args) => { window.__opened.push(args); return null; }; }");

        await Ask("Show me some media");
        var card = Activities.Last;
        await Expect(card.Locator("video")).ToHaveCountAsync(1);
        await Expect(card.Locator("audio")).ToHaveCountAsync(1);
        await Expect(card).ToContainTextAsync("T-Rex roar (CC0)");

        await card.GetByRole(AriaRole.Button, new() { Name = "Read the A2UI spec" }).ClickAsync();
        await Page.WaitForFunctionAsync("() => window.__opened.length > 0");
        var url = await Page.EvaluateAsync<string>("() => window.__opened[0][0]");
        await Assert.That(url).StartsWith("https://a2ui.org");
        await Assert.That(await Page.EvaluateAsync<string>("() => window.__opened[0][1]")).IsEqualTo("_blank");
        await AssertNoConsoleErrors(WebKitMediaControls);
    }

    [Test]
    public async Task Research_Runs_A_Sub_Agent_Inside_The_Turn()
    {
        await OpenAgent();
        await Ask("Research AG-UI for me");

        var group = Chat.Locator(".agent-subagent").First;
        await Expect(group.Locator(".agent-subagent-name")).ToHaveTextAsync("researcher");
        await Expect(group).ToContainTextAsync("event-based protocol");
        await Expect(group).ToHaveClassAsync(new Regex(@"agent-subagent--(succeeded|finished|completed|success)"));
        await ExpectReply("standardises");

        // the group folds away
        var head = group.Locator(".agent-subagent-head");
        await head.ClickAsync();
        await Expect(head).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(group.Locator(".agent-subagent-body")).ToHaveCountAsync(0);
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Reading_List_Is_Shared_State_Both_Ways()
    {
        await OpenAgent();
        var panel = Page.Locator(".agent-page-state");
        await Expect(panel).ToContainTextAsync("Nothing yet.");

        await Ask("Track my reading list");
        await Expect(panel).ToContainTextAsync("Dune");
        await Expect(panel.Locator(".agent-page-progress")).ToHaveTextAsync("1 of 12 read");

        // tick a book on the page; the agent gets it back on the next turn
        var piranesi = panel.GetByLabel("Piranesi");
        await piranesi.CheckAsync();
        await Expect(panel.Locator(".agent-page-progress")).ToHaveTextAsync("2 of 12 read");

        await Ask("Track my reading list");
        await Expect(panel).ToContainTextAsync("The Left Hand of Darkness");
        await Expect(piranesi).ToBeCheckedAsync();
        await Expect(panel.Locator(".agent-page-progress")).ToHaveTextAsync("2 of 12 read");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Run_Error_Offers_Retry()
    {
        await OpenAgent();
        await Ask("Fail on purpose");

        await Expect(Errors.Last).ToContainTextAsync("The demo agent failed on purpose.");
        var retry = Errors.Last.GetByRole(AriaRole.Button, new() { Name = "Retry" });
        await Expect(retry).ToBeVisibleAsync();

        var tries = await Chat.GetByText("Trying something that won't work...").CountAsync();
        await retry.ClickAsync();
        await Idle();
        await Expect(Chat.GetByText("Trying something that won't work...")).ToHaveCountAsync(tries + 1);
        await Expect(Errors.Last.GetByRole(AriaRole.Button, new() { Name = "Retry" })).ToBeVisibleAsync();
        await AssertNoConsoleErrors();
    }

    // ---- recordings -----------------------------------------------------------------------------

    async Task UseRecordings()
    {
        await OpenAgent();
        await SourceTab("Recordings").ClickAsync();
        await Expect(SourceTab("Recordings")).ToHaveAttributeAsync("aria-selected", "true");
        await Expect(Suggestions).ToHaveTextAsync(["a2ui", "backend-state", "error", "frontend-tool", "interrupt", "reasoning", "text"]);
    }

    [Test]
    public async Task Recording_Text_Replays_Markdown()
    {
        await UseRecordings();
        await Suggestions.Filter(new() { HasTextString = "text" }).Last.ClickAsync();
        await Idle();
        await ExpectReply("Markdown works, and so does code.");
        await Expect(Assistant.Last.Locator("strong")).ToHaveTextAsync("Markdown");
        await Expect(Assistant.Last.Locator("li")).ToHaveTextAsync(["one", "two"]);
        // the panel only shows while there is shared state to bind
        await Expect(Page.Locator(".agent-page-state")).ToHaveCountAsync(0);
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Recording_A2UI_Draws_The_Surface()
    {
        await UseRecordings();
        await Ask("a2ui");
        await Expect(ToolCards.Locator(".agent-tool-name").First).ToHaveTextAsync("render_ui");
        var surface = Activities.Last;
        await Expect(surface).ToContainTextAsync("Allan");
        await Expect(surface.GetByRole(AriaRole.Button, new() { Name = "Confirm" })).ToBeVisibleAsync();
        await ExpectReply("that's updated.");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Recording_Frontend_Tool_Plays_Its_Follow_Up()
    {
        await UseRecordings();
        await Send("frontend-tool");
        await ExpectReply("Let me ask you for dates.");

        // the recording's picker allows Nov - Dec 2026
        var prompt = Chat.Locator(".agent-prompt");
        await Expect(prompt).ToBeVisibleAsync();
        await PickRange(prompt, new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 24));

        await Idle();
        await ExpectReply("Done — that's updated.");
        await Expect(prompt).ToHaveCountAsync(0);
        await AssertNoConsoleErrors();
    }

    // ---- endpoint -------------------------------------------------------------------------------

    [Test]
    public async Task Endpoint_Rejects_A_Bad_Url_And_Reports_An_Unreachable_One()
    {
        await OpenAgent();
        await SourceTab("Endpoint").ClickAsync();
        await Expect(Status).ToHaveTextAsync("Not connected");
        await Expect(Chat.GetByText("No agent session.")).ToBeVisibleAsync();

        var url = SourceBar.GetByLabel("AG-UI endpoint URL");
        var connect = SourceBar.GetByRole(AriaRole.Button, new() { Name = "Connect" });
        await Expect(connect).ToBeDisabledAsync();

        await url.FillAsync("not a url");
        await connect.ClickAsync();
        await Expect(Status).ToHaveTextAsync("Enter an http(s) URL");
        await Expect(Status).ToHaveClassAsync(new Regex(@"\bis-error\b"));

        await url.FillAsync("ftp://example.com/agent");
        await url.PressAsync("Enter");
        await Expect(Status).ToHaveTextAsync("Enter an http(s) URL");
        await Expect(Chat.GetByText("No agent session.")).ToBeVisibleAsync();

        // well-formed, but nothing listens there: the run fails in the conversation, with Retry
        var closed = ClosedPort();
        await url.FillAsync($"http://127.0.0.1:{closed}/agent");
        await connect.ClickAsync();
        await Expect(Status).ToHaveTextAsync($"Connected to 127.0.0.1:{closed}");
        await Expect(Suggestions.First).ToHaveTextAsync("text");
        await Ask("text");
        await Expect(Errors.Last).ToBeVisibleAsync();
        await Expect(Errors.Last.GetByRole(AriaRole.Button, new() { Name = "Retry" })).ToBeVisibleAsync();
        // each browser reports the refused connection in its own words (WebKit's is "access control checks")
        await AssertNoConsoleErrors(@"ERR_CONNECTION_REFUSED", @"Failed to fetch", @"Load failed", @"Could not connect", @"Fetch API cannot load .* due to access control checks");
    }

    // ---- your key -------------------------------------------------------------------------------

    async Task ConnectMock(MockOpenAIServer mock, string key)
    {
        await OpenAgent();
        await SourceTab("Your key").ClickAsync();
        await Expect(SourceBar.GetByLabel("Base URL")).ToHaveValueAsync("https://api.openai.com/v1");
        await Expect(SourceBar.GetByLabel("Model")).ToHaveValueAsync("gpt-4.1-mini");
        await Expect(SourceBar.GetByLabel("API key")).ToHaveAttributeAsync("type", "password");
        await Expect(SourceBar).ToContainTextAsync("goes straight from your browser");

        var connect = SourceBar.GetByRole(AriaRole.Button, new() { Name = "Connect" });
        await Expect(connect).ToBeDisabledAsync();

        await SourceBar.GetByLabel("Base URL").FillAsync(mock.BaseUrl);
        await SourceBar.GetByLabel("Model").FillAsync("mock-model");
        await SourceBar.GetByLabel("API key").FillAsync(key);
        await connect.ClickAsync();
        await Expect(Status).ToHaveTextAsync("mock-model at 127.0.0.1");
        await Expect(Suggestions.First).ToHaveTextAsync("Show me a dashboard of this week's support tickets");
    }

    [Test]
    public async Task Your_Key_Streams_Text_From_An_OpenAI_Compatible_Endpoint()
    {
        await using var mock = new MockOpenAIServer();
        const string key = "sk-mock-streaming-123";
        await ConnectMock(mock, key);

        await Send("hello there");

        // the first half is on screen while the mock still holds the second: the browser streams
        await Expect(Assistant.Last).ToContainTextAsync(MockOpenAIServer.PlainFirstHalf.Trim(), new() { Timeout = 20_000 });
        await Expect(StopButton).ToBeVisibleAsync();
        await Expect(Assistant.Last).Not.ToContainTextAsync(MockOpenAIServer.PlainSecondHalf);

        mock.Release();
        await Idle();
        await ExpectReply(MockOpenAIServer.PlainFirstHalf + MockOpenAIServer.PlainSecondHalf);

        await Assert.That(mock.Authorizations.First()).IsEqualTo("Bearer " + key);
        var request = mock.Requests.First();
        await Assert.That(request["model"]?.GetValue<string>()).IsEqualTo("mock-model");
        await Assert.That(request["stream"]?.GetValue<bool>()).IsTrue();

        // the key lives only in component memory
        var stored = await Page.EvaluateAsync<string>("() => JSON.stringify(localStorage) + JSON.stringify(sessionStorage) + document.cookie");
        await Assert.That(stored.Contains(key)).IsFalse();
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Your_Key_Renders_A_Model_Drawn_Surface()
    {
        await using var mock = new MockOpenAIServer();
        await ConnectMock(mock, "sk-mock-ui-456");

        await Ask("show me some ui");

        await Expect(ToolCards.Locator(".agent-tool-name").First).ToHaveTextAsync("render_ui");
        var surface = Activities.Last;
        await Expect(surface).ToContainTextAsync(MockOpenAIServer.SurfaceText);
        await ExpectReply(MockOpenAIServer.AfterToolText);

        // two model calls: the tool call, then the turn that carries its result
        await Assert.That(mock.Requests.Count).IsEqualTo(2);
        var second = mock.Requests.Last()["messages"]!.AsArray();
        await Assert.That(second.Last()?["role"]?.GetValue<string>()).IsEqualTo("tool");
        await AssertNoConsoleErrors();
    }

    [Test]
    public async Task Switching_Sources_Starts_A_New_Conversation()
    {
        await OpenAgent();
        await Ask("Order lunch");
        await Expect(UserBubbles).ToHaveCountAsync(1);

        await SourceTab("Recordings").ClickAsync();
        await Expect(UserBubbles).ToHaveCountAsync(0);
        await Expect(Interrupt).ToHaveCountAsync(0);

        await SourceTab("Demo").ClickAsync();
        await Expect(Suggestions).ToHaveCountAsync(12);
        await Expect(Page.Locator(".agent-page-state")).ToBeVisibleAsync();
        await AssertNoConsoleErrors();
    }

    // ---- helpers --------------------------------------------------------------------------------

    /// <summary>
    /// WebKit's media controls log their own icon loads failing when the media can't load (it is
    /// blocked here) - the browser's chrome, not the page.
    /// </summary>
    const string WebKitMediaControls = @"^Button failed to load, iconName = ";

    /// <summary>A port nothing listens on (and not one the browsers refuse outright, like 9).</summary>
    static int ClosedPort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>Picks a range in an inline DateRangeCalendar, paging months until each day shows, then submits.</summary>
    async Task PickRange(ILocator prompt, DateOnly start, DateOnly end)
    {
        await ClickDay(prompt, start);
        await ClickDay(prompt, end);
        var submit = prompt.Locator(".agent-btn--primary");
        await Expect(submit).ToBeEnabledAsync();
        await submit.ClickAsync();
    }

    async Task ClickDay(ILocator prompt, DateOnly date)
    {
        var day = prompt.Locator($".shiny-rangecal-day[data-date='{date:yyyy-MM-dd}']");
        for (var i = 0; i < 36 && !await day.IsVisibleAsync(); i++)
        {
            var shown = await prompt.Locator(".shiny-rangecal-day[data-date]").First.GetAttributeAsync("data-date");
            var forward = shown is null || DateOnly.Parse(shown) < date;
            await prompt.Locator(forward ? ".shiny-rangecal-nav.is-next:not(.is-inner)" : ".shiny-rangecal-nav.is-prev:not(.is-inner)").First.ClickAsync();
        }
        await day.ClickAsync();
    }
}

/// <summary>
/// The agent page's source bar on a phone: every source's form fits the width, the chat keeps room,
/// and nothing breaks the Jon rules (the route sweep only ever sees the Demo source).
/// </summary>
public class AgentViewPhoneTests : BlazorTest
{
    protected override bool Phone => true;

    public static IEnumerable<Func<string>> SourceNames()
        => new[] { "Demo", "Recordings", "Endpoint", "Your key" }.Select(s => (Func<string>)(() => s));

    [Test]
    [MethodDataSource(nameof(SourceNames))]
    public async Task Source_Fits_A_Phone(string source)
    {
        await Open("agent-view");
        var tab = Page.GetByTestId("agent-source").GetByRole(AriaRole.Tab, new() { Name = source, Exact = true });
        await tab.ClickAsync();
        await Expect(tab).ToHaveAttributeAsync("aria-selected", "true");

        await JonRules.AssertAsync(Page, "agent-view-" + source.Replace(' ', '-').ToLowerInvariant(), "phone");
        await Screenshot(Path.Combine("phone", "agent-view-" + source.Replace(' ', '-').ToLowerInvariant()));

        // no sideways scroll, every source control inside the viewport, and the conversation still has room
        var width = await Page.EvaluateAsync<int>("() => document.documentElement.clientWidth");
        await Assert.That(await Page.EvaluateAsync<int>("() => document.documentElement.scrollWidth")).IsLessThanOrEqualTo(width);
        foreach (var el in await Page.GetByTestId("agent-source").Locator("input, button").AllAsync())
        {
            var box = await el.BoundingBoxAsync();
            await Assert.That(box).IsNotNull();
            await Assert.That(box!.X).IsGreaterThanOrEqualTo(0);
            await Assert.That(box.X + box.Width).IsLessThanOrEqualTo(width + 0.5f);
        }
        var chat = await Page.Locator(".agent-page-chat").BoundingBoxAsync();
        await Assert.That(chat!.Height).IsGreaterThanOrEqualTo(220);
        await AssertNoConsoleErrors();
    }
}

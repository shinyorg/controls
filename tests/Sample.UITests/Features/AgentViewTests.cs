using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Maui.DevFlow.Driver;

namespace Sample.UITests.Features;

/// <summary>
/// AgentView on the Agent View page, end to end against each of the page's agent sources: the scripted
/// <c>DemoAgentSession</c> (every scenario it scripts - streamed markdown, reasoning, tool cards, the
/// A2UI dashboard / form / media surfaces, the frontend date-range tool, every interrupt shape,
/// sub-agents with and without a <c>SubagentTemplate</c>, shared state, an error with Retry), the
/// recorded AGUI.Server streams, and "Your key" pointed at a mock OpenAI-compatible server this test
/// starts on the loopback. Plus the multi-line composer and SendOnEnter.
/// <para>
/// Everything is scoped to the AgentView by its AutomationId. The conversation's rows carry no
/// AutomationIds of their own (they are drawn by the control), so the assertions read the rows'
/// labels and the assistant markdown text. Shell caches the page, so each test picks its source
/// and clears the conversation first.
/// </para>
/// </summary>
public class AgentViewTests : UITest
{
    const string Route = "agent-view";
    const string PageType = "AgentViewPage";
    const string View = "SampleAgentView";
    const string Composer = "AgentViewComposer";
    const string SendButton = "AgentViewSendButton";

    const int Demo = 0, Recordings = 1, YourKey = 3;

    // ── helpers ──────────────────────────────────────────────────────────────────

    async Task UseSource(int source)
    {
        await Open(Route, PageType);
        if (await Driver.GetProperty("SelectedIndex", automationId: "AgentSourcePicker") != source.ToString(CultureInfo.InvariantCulture))
        {
            await Driver.SetProperty("SelectedIndex", source.ToString(CultureInfo.InvariantCulture), automationId: "AgentSourcePicker");
            await Driver.WaitForProperty("AgentSourcePicker", "SelectedIndex", source.ToString(CultureInfo.InvariantCulture));
        }
        if (source == Demo && await Driver.GetProperty("IsToggled", automationId: "AgentSubagentTemplateSwitch") == "True")
            await Driver.SetProperty("IsToggled", "False", automationId: "AgentSubagentTemplateSwitch");

        await Driver.Tap("AgentClearButton");
        await WaitIdle();
        await Driver.WaitUntil(async () => !(await Driver.QuerySelector($"#{View} MarkdownView")).Any(m => m.IsVisible),
            "the conversation to be cleared");
    }

    Task WaitIdle(int timeoutSeconds = 30) => Driver.WaitForProperty(View, "IsBusy", "False", timeoutSeconds);

    async Task Send(string text)
    {
        await WaitIdle();
        await Driver.SetProperty("Text", text, automationId: Composer);
        await Driver.Tap(SendButton);
    }

    /// <summary>Every assistant markdown text and every label in the conversation, one per line.</summary>
    async Task<string> Transcript()
    {
        var parts = new List<string>();
        foreach (var md in (await Driver.QuerySelector($"#{View} MarkdownView")).Where(m => m.IsVisible))
            parts.Add(await Driver.GetProperty("Markdown", elementId: md.Id) ?? "");
        parts.AddRange((await Driver.QuerySelector($"#{View} Label")).Where(l => l.IsVisible).Select(l => l.Text ?? ""));
        return String.Join("\n", parts);
    }

    async Task<int> MarkdownCount(string fragment)
    {
        var count = 0;
        foreach (var md in (await Driver.QuerySelector($"#{View} MarkdownView")).Where(m => m.IsVisible))
        {
            if ((await Driver.GetProperty("Markdown", elementId: md.Id) ?? "").Contains(fragment, StringComparison.OrdinalIgnoreCase))
                count++;
        }
        return count;
    }

    Task WaitForText(string fragment, int timeoutSeconds = 30)
        => Driver.WaitUntil(async () => (await Transcript()).Contains(fragment, StringComparison.OrdinalIgnoreCase),
            $"'{fragment}' in the conversation", timeoutSeconds);

    async Task<List<ElementInfo>> Buttons(string text)
        => (await Driver.QuerySelector($"#{View} Button[Text={MauiDevFlowDriver.Quote(text)}]"))
            .Where(b => b.IsVisible && b.AutomationId != SendButton)
            .ToList();

    /// <summary>Taps the newest visible button with this text inside the conversation (not the composer's Send).</summary>
    async Task TapButton(string text, int timeoutSeconds = 30)
    {
        string? id = null;
        await Driver.WaitUntil(async () => (id = (await Buttons(text)).LastOrDefault()?.Id) != null, $"a '{text}' button", timeoutSeconds);
        await Driver.Tap(elementId: id);
    }

    /// <summary>Taps a start and an end day in the inline DateRangeCalendar a date-range prompt drew, then "Use these dates".</summary>
    async Task AnswerDateRange(DateOnly min)
    {
        var cells = new List<(string Id, DateOnly Date)>();
        await Driver.WaitUntil(async () =>
        {
            cells.Clear();
            foreach (var cell in await Driver.QuerySelector($"#{View} RangeDayCell"))
            {
                if (TryParseDate(await Driver.GetProperty("Date", elementId: cell.Id), out var d))
                    cells.Add((cell.Id, d));
            }
            return cells.Count > 0;
        }, "the date range calendar's day cells", 30);

        // two days after the earliest allowed date, inside the month the calendar shows
        var shownMonth = cells.GroupBy(c => (c.Date.Year, c.Date.Month)).OrderByDescending(g => g.Count()).First().Key;
        var picks = cells
            .Where(c => c.Date > min && c.Date > DateOnly.FromDateTime(DateTime.Today) && (c.Date.Year, c.Date.Month) == shownMonth)
            .OrderBy(c => c.Date)
            .ToList();
        if (picks.Count < 2)
            picks = cells.Where(c => c.Date > min && c.Date > DateOnly.FromDateTime(DateTime.Today)).OrderBy(c => c.Date).ToList();
        if (picks.Count < 2)
            throw new InvalidOperationException("Not enough selectable days on the calendar");

        await Driver.Tap(elementId: picks[0].Id);
        await Driver.Tap(elementId: picks[Math.Min(3, picks.Count - 1)].Id);
        await Driver.WaitUntil(async () => (await Driver.QuerySelector($"#{View} Button[Text=\"Use these dates\"]")).Any(b => b.IsVisible && b.IsEnabled),
            "'Use these dates' to enable after a range is picked");
        await TapButton("Use these dates");
    }

    static bool TryParseDate(string? raw, out DateOnly date)
    {
        date = default;
        if (String.IsNullOrWhiteSpace(raw))
            return false;
        foreach (var culture in new[] { CultureInfo.InvariantCulture, CultureInfo.GetCultureInfo("en-US"), CultureInfo.CurrentCulture })
        {
            if (DateOnly.TryParse(raw, culture, DateTimeStyles.None, out date))
                return true;
            if (DateTime.TryParse(raw, culture, DateTimeStyles.None, out var dt))
            {
                date = DateOnly.FromDateTime(dt);
                return true;
            }
        }
        return false;
    }

    /// <summary>The DataGrid row (its tappable container) holding the cell showing <paramref name="cellText"/>.</summary>
    async Task<string?> GridRowWith(string cellText)
    {
        // one selector query returns every element in the grid with its parent id
        var all = (await Driver.QuerySelector($"#{View} DataGrid *")).ToDictionary(e => e.Id);
        foreach (var cell in all.Values.Where(e => e.Text == cellText && e.IsVisible))
        {
            for (ElementInfo? current = cell; current != null; current = current.ParentId != null && all.TryGetValue(current.ParentId, out var p) ? p : null)
            {
                if (current.Gestures?.Contains("tap") == true)
                    return current.Id;
            }
        }
        return null;
    }

    // ── demo agent ───────────────────────────────────────────────────────────────

    [Test]
    public async Task Help_Streams_Reasoning_And_Markdown()
    {
        await UseSource(Demo);
        await Send("What can you do?");

        await WaitForText("scripted demo agent");
        await WaitIdle();
        // the reasoning collapses to a "Thought for Ns" row once it ends
        await Driver.WaitUntil(async () => (await Driver.QuerySelector($"#{View} Label")).Any(l => l.IsVisible && (l.Text ?? "").Contains("Thought", StringComparison.Ordinal)),
            "the reasoning row");
        // the user's own bubble
        await Assert.That(await Transcript()).Contains("What can you do?");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Dashboard_Draws_A_Tool_Card_And_The_A2UI_Surface()
    {
        await UseSource(Demo);
        await Send("Show me the October sales dashboard");

        await WaitForText("Here's October");
        await WaitIdle();
        var transcript = await Transcript();
        await Assert.That(transcript).Contains("query_sales");      // the backend tool call card
        await Assert.That(transcript).Contains("October sales");    // the surface's title
        await Assert.That(transcript).Contains("Live");             // its Pill
        await Assert.That((await Driver.QuerySelector($"#{View} Gauge")).Count).IsGreaterThan(0);
        await Assert.That((await Driver.QuerySelector($"#{View} DataGrid")).Count).IsGreaterThan(0);
        await Assert.That((await Driver.QuerySelector($"#{View} DateRangePicker")).Count).IsGreaterThan(0);
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Dashboard_Row_Tap_Sends_The_Row_Action()
    {
        await UseSource(Demo);
        await Send("Show me the October sales dashboard");
        await WaitForText("Here's October");
        await WaitIdle();

        string? row = null;
        await Driver.WaitUntil(async () => (row = await GridRowWith("A-1001")) != null, "the A-1001 row", 20);
        await Driver.Tap(elementId: row);

        // the rowAction's context resolves the relative path against the tapped row
        await WaitForText("Order **A-1001** shipped");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Dashboard_Chips_Toggle_And_Refresh_Sends_The_Data_Model()
    {
        await UseSource(Demo);
        await Send("Show me the October sales dashboard");
        await WaitForText("Here's October");
        await WaitIdle();

        // Europe and North America start selected; add Asia
        var chips = $"#{View} ChipView";
        await Driver.WaitForCssValue(chips, "IsSelected", "True", index: 0);
        await Driver.WaitForCssValue(chips, "IsSelected", "False", index: 2);
        await Driver.TapCss(chips, 2);
        await Driver.WaitForCssValue(chips, "IsSelected", "True", index: 2);

        await TapButton("Refresh");
        await WaitForText("in 3 region(s)");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Dashboard_Expander_Opens()
    {
        await UseSource(Demo);
        await Send("Show me the October sales dashboard");
        await WaitForText("Here's October");
        await WaitIdle();

        var expander = await Driver.CssAt($"#{View} Expander");
        await Assert.That(await Driver.GetProperty("IsExpanded", elementId: expander.Id)).IsEqualTo("False");
        await Driver.SetCss($"#{View} Expander", "IsExpanded", "True");
        await Driver.WaitForCssValue($"#{View} Expander", "IsExpanded", "True");
        await Driver.WaitUntil(async () => await MarkdownCount("net of tax") > 0, "the expander's markdown");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Trip_Frontend_Tool_Is_Answered_By_The_Date_Range_Prompt()
    {
        await UseSource(Demo);
        await Send("Book a trip");

        await WaitForText("pick your dates");
        await AnswerDateRange(DateOnly.FromDateTime(DateTime.Today).AddDays(-1));
        // the tool result goes back and AgentRunner starts the follow-up run
        await WaitForText("Booked!");
        await WaitIdle();
        await Assert.That(await Transcript()).Contains("pick_date_range");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Newsletter_Form_Submits_Its_Action()
    {
        await UseSource(Demo);
        await Send("Sign me up for the newsletter");
        await WaitForText("Fill this in");
        await WaitIdle();

        await Driver.FillCss($"#{View} Entry", "Ada", 0);
        await Driver.FillCss($"#{View} Entry", "ada@example.com", 1);
        await TapButton("Subscribe");

        await WaitForText("Thanks **Ada**");
        await WaitForText("`ada@example.com`"); // the action's context carried the typed values
        await WaitIdle();
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Approval_Interrupt_Approve()
    {
        await UseSource(Demo);
        await Send("Delete my old drafts");
        await TapButton("Approve");
        await WaitForText("I deleted **3 drafts**");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Approval_Interrupt_Reject()
    {
        await UseSource(Demo);
        await Send("Delete my old drafts");
        await TapButton("Reject");
        await WaitForText("I left your drafts alone");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Choices_Interrupt_Picks_One()
    {
        await UseSource(Demo);
        await Send("Order lunch");
        await TapButton("Greek salad");
        await WaitForText("Ordered: **Greek salad**");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Choices_Interrupt_Picks_Several()
    {
        await UseSource(Demo);
        await Send("Pick pizza toppings");
        // the card's chips come before the (hidden) suggestion chips in the tree
        await WaitForText("Which toppings?");
        var chips = $"#{View} ChipView";
        await Driver.TapCss(chips, 1); // Mushroom
        await Driver.TapCss(chips, 4); // Basil
        await Driver.WaitForCssValue(chips, "IsSelected", "True", index: 1);
        await Driver.WaitForCssValue(chips, "IsSelected", "True", index: 4);
        await TapButton("Submit");

        await WaitForText("**Mushroom, Basil**");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Text_Interrupt_Sends_The_Reply()
    {
        await UseSource(Demo);
        await Send("Rename my project");
        await WaitForText("What should the project be called?");

        var entry = $"#{View} Entry";
        await Driver.FillCss(entry, "Shiny Agents");
        await TapButton("Send"); // the card's Send, not the composer's
        await WaitForText("Renamed to **Shiny Agents**");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Media_Card_Shows_Video_Audio_And_A_Link()
    {
        await UseSource(Demo);
        await Send("Show me some media");
        await WaitForText("A short film");
        await WaitIdle();

        var transcript = await Transcript();
        await Assert.That(transcript).Contains("Media");
        await Assert.That(transcript).Contains("T-Rex roar (CC0)"); // the AudioPlayer
        await Assert.That(transcript).Contains("▶");                // the Video's play glyph
        await Assert.That((await Buttons("Read the A2UI spec")).Count).IsEqualTo(1);

        // openUrl goes to the system browser. On a phone that backgrounds the app under test, so the
        // tap (which only has to not throw) is made on the desktop heads.
        if (IsDesktop)
        {
            await TapButton("Read the A2UI spec");
            await Task.Delay(1500);
        }
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Research_Shows_The_Subagent_Group()
    {
        await UseSource(Demo);
        await Send("Research AG-UI for me");
        await WaitForText("My researcher says");
        await WaitIdle();

        await Driver.WaitUntil(async () => (await Driver.QuerySelector($"#{View} Label")).Any(l => l.IsVisible && (l.Text ?? "").Contains("researcher · done", StringComparison.Ordinal)),
            "the sub-agent group's header to read done");
        await Assert.That(await Transcript()).Contains("delegate_research");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Research_Uses_The_Custom_Subagent_Template()
    {
        await UseSource(Demo);
        await Driver.SetProperty("IsToggled", "True", automationId: "AgentSubagentTemplateSwitch");
        try
        {
            await Send("Research AG-UI for me");
            await WaitForText("My researcher says");
            await WaitIdle();

            await Driver.WaitForProperty("CustomSubagentName", "Text", "🧪 researcher", 15);
            await Driver.WaitForProperty("CustomSubagentStatus", "Text", "Completed", 15);
            // the template replaces the whole default group
            await Assert.That((await Driver.QuerySelector($"#{View} Label")).Any(l => l.IsVisible && (l.Text ?? "").Contains("researcher · ", StringComparison.Ordinal))).IsFalse();
            await AssertNoAppErrors();
        }
        finally
        {
            await Driver.SetProperty("IsToggled", "False", automationId: "AgentSubagentTemplateSwitch");
        }
    }

    [Test]
    public async Task Reading_List_Shared_State_Fills_The_Panel()
    {
        await UseSource(Demo);
        await Send("Track my reading list");
        await WaitForText("started your reading list");
        await Driver.WaitForProperty("ReadingListGoal", "Text", "1 of 12 read this year", 15);

        await WaitIdle();
        await Send("Track my reading list");
        await WaitForText("Added *The Left Hand of Darkness*");
        await Driver.WaitUntil(async () => (await Driver.QuerySelector("#ReadingListBooks Label")).Count == 3, "three books in the panel");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Error_Shows_And_Retry_Runs_Again()
    {
        await UseSource(Demo);
        await Send("Fail on purpose");
        await WaitForText("The demo agent failed on purpose.");
        await WaitIdle();
        await Assert.That(await MarkdownCount("Trying something")).IsEqualTo(1);

        await TapButton("Retry");
        await Driver.WaitUntil(async () => await MarkdownCount("Trying something") >= 2, "the retried run", 20);
        await WaitIdle();
        // the run error is shown in the conversation, not logged as an app error
        await AssertNoAppErrors();
    }

    // ── composer ─────────────────────────────────────────────────────────────────

    [Test]
    public async Task Composer_Keeps_Pasted_Lines_And_Sends_Them()
    {
        await UseSource(Demo);
        var original = await Driver.GetProperty("SendOnEnter", automationId: View);
        await Driver.SetProperty("SendOnEnter", "True", automationId: View);
        try
        {
            // several characters at once (a paste) keep their line breaks even with SendOnEnter
            await Driver.SetProperty("Text", "first line\nsecond line", automationId: Composer);
            await Task.Delay(500);
            await Assert.That(await Driver.GetProperty("Text", automationId: Composer)).IsEqualTo("first line\nsecond line");

            await Driver.Tap(SendButton);
            await Driver.WaitUntil(async () => (await Driver.QuerySelector($"#{View} Label")).Any(l => l.Text == "first line\nsecond line"),
                "the user bubble with both lines");
            await Driver.WaitForValue(Composer, "Text", "");
            await WaitIdle();
            await AssertNoAppErrors();
        }
        finally
        {
            await Driver.SetProperty("SendOnEnter", original ?? "False", automationId: View);
        }
    }

    [Test]
    public async Task Composer_Enter_Sends_When_SendOnEnter()
    {
        await UseSource(Demo);
        var original = await Driver.GetProperty("SendOnEnter", automationId: View);
        await Driver.SetProperty("SendOnEnter", "True", automationId: View);
        try
        {
            await Driver.SetProperty("Text", "What can you do?", automationId: Composer);
            // one typed line break: removed, and the message goes
            await Driver.SetProperty("Text", "What can you do?\n", automationId: Composer);
            await Driver.WaitUntil(async () => (await Driver.QuerySelector($"#{View} Label")).Any(l => l.Text == "What can you do?"),
                "the message sent by Enter");
            await Driver.WaitForValue(Composer, "Text", "");
            await WaitForText("scripted demo agent");
            await WaitIdle();

            // with it off, a line break is just a line break
            await Driver.SetProperty("SendOnEnter", "False", automationId: View);
            await Driver.SetProperty("Text", "draft", automationId: Composer);
            await Driver.SetProperty("Text", "draft\n", automationId: Composer);
            await Task.Delay(800);
            await Assert.That(await Driver.GetProperty("Text", automationId: Composer)).IsEqualTo("draft\n");
            await Driver.SetProperty("Text", "", automationId: Composer);
            await AssertNoAppErrors();
        }
        finally
        {
            await Driver.SetProperty("SendOnEnter", original ?? "False", automationId: View);
        }
    }

    // ── recordings ───────────────────────────────────────────────────────────────

    [Test]
    public async Task Recordings_Play_The_Text_Stream()
    {
        await UseSource(Recordings);
        await Send("text");
        await WaitForText("**Markdown** works");
        await WaitIdle();
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Recordings_Frontend_Tool_Plays_Its_Followup()
    {
        await UseSource(Recordings);
        await Send("frontend-tool");
        await WaitForText("Let me ask you for dates.");
        // the recording asks for dates from 2026-11-01
        await AnswerDateRange(new DateOnly(2026, 11, 1));
        await WaitForText("that's updated", 30);
        await WaitIdle();
        await AssertNoAppErrors();
    }

    // ── your key (a mock OpenAI-compatible server) ───────────────────────────────

    [Test]
    public async Task Your_Key_Talks_To_An_OpenAI_Compatible_Endpoint()
    {
        await using var mock = MockOpenAI.Start();
        await UseSource(YourKey);

        var host = await App.Host.ExposeHostPortAsync(mock.Port);
        await Driver.SetProperty("Text", $"http://{host}:{mock.Port}/v1", automationId: "AgentBaseUrlEntry");
        await Driver.SetProperty("Text", "mock-model", automationId: "AgentModelEntry");
        await Driver.SetProperty("Text", "sk-test-key", automationId: "AgentApiKeyEntry");
        await Driver.Tap("AgentKeyConnectButton");
        await Driver.WaitUntil(async () => (await Driver.GetProperty("Session", automationId: View) ?? "").Contains("ChatClientAgentSession", StringComparison.Ordinal), "the session to connect");

        await Send("Say hello");
        await WaitForText("Hello from the mock model.");
        await WaitIdle();

        // a prompt mentioning UI gets a render_ui tool call; the session draws the surface and the
        // model answers the tool result in the same run
        await Send("Show me some ui");
        await WaitForText("Mock surface");
        await WaitForText("There's your card.");
        await WaitIdle();

        await Assert.That(mock.Authorization).IsEqualTo("Bearer sk-test-key");
        await Assert.That(mock.Models).Contains("mock-model");
        await Assert.That(mock.Requests).IsGreaterThanOrEqualTo(3);
        await AssertNoAppErrors();
    }

    /// <summary>
    /// A loopback OpenAI-compatible chat completions endpoint: <c>POST /v1/chat/completions</c> answered
    /// as a <c>text/event-stream</c> of <c>chat.completion.chunk</c>s. A plain prompt gets text; a prompt
    /// with "ui" in it gets a <c>render_ui</c> tool call, and the request carrying its tool result gets text.
    /// </summary>
    sealed class MockOpenAI : IAsyncDisposable
    {
        readonly HttpListener listener = new();
        readonly CancellationTokenSource cts = new();
        readonly Task loop;

        public int Port { get; }
        public string? Authorization { get; private set; }
        public List<string> Models { get; } = [];
        public int Requests { get; private set; }

        MockOpenAI(int port)
        {
            Port = port;
            // loopback only; the Android head reaches it through adb reverse (see ExposeHostPortAsync)
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            loop = Task.Run(Serve);
        }

        public static MockOpenAI Start()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return new MockOpenAI(port);
        }

        async Task Serve()
        {
            while (!cts.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch
                {
                    return;
                }
                try
                {
                    await Answer(context);
                }
                catch
                {
                    // a cancelled stream; the next request is unaffected
                }
            }
        }

        async Task Answer(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;
            if (request.HttpMethod != "POST" || request.Url?.AbsolutePath.TrimEnd('/') != "/v1/chat/completions")
            {
                response.StatusCode = 404;
                response.Close();
                return;
            }

            Requests++;
            Authorization = request.Headers["Authorization"];
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            var body = JsonNode.Parse(await reader.ReadToEndAsync()) as JsonObject;
            if (body?["model"]?.GetValue<string>() is { } model)
                Models.Add(model);

            var messages = body?["messages"] as JsonArray ?? [];
            var hasToolResult = messages.Any(m => m?["role"]?.GetValue<string>() == "tool");
            var lastUser = messages.LastOrDefault(m => m?["role"]?.GetValue<string>() == "user");
            var prompt = lastUser?["content"] switch
            {
                JsonValue v => v.GetValue<string>(),
                JsonArray parts => String.Join(" ", parts.Select(p => p?["text"]?.GetValue<string>())),
                _ => ""
            };

            response.StatusCode = 200;
            response.ContentType = "text/event-stream";
            response.SendChunked = true;
            response.Headers["Cache-Control"] = "no-cache";
            var output = response.OutputStream;

            async Task Chunk(object delta, string? finish = null)
            {
                var chunk = new JsonObject
                {
                    ["id"] = "chatcmpl-mock",
                    ["object"] = "chat.completion.chunk",
                    ["created"] = 1_760_000_000,
                    ["model"] = "mock-model",
                    ["choices"] = new JsonArray(new JsonObject
                    {
                        ["index"] = 0,
                        ["delta"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(delta)),
                        ["finish_reason"] = finish
                    })
                };
                var bytes = Encoding.UTF8.GetBytes("data: " + chunk.ToJsonString() + "\n\n");
                await output.WriteAsync(bytes);
                await output.FlushAsync();
                await Task.Delay(30);
            }

            if (!hasToolResult && prompt.Contains("ui", StringComparison.OrdinalIgnoreCase))
            {
                var operations = """
                    [{"version":"1.0","createSurface":{"surfaceId":"mock-card","catalogId":"https://shinylib.net/a2ui/shiny.controls/v1.json",
                      "dataModel":{},"components":[
                        {"id":"root","component":"Card","child":"body"},
                        {"id":"body","component":"Column","children":["title","note"]},
                        {"id":"title","component":"Text","variant":"h4","text":"Mock surface"},
                        {"id":"note","component":"Text","text":"Drawn from a render_ui tool call."}]}}]
                    """;
                var args = new JsonObject { ["operations"] = JsonNode.Parse(operations) }.ToJsonString();
                await Chunk(new { role = "assistant", tool_calls = new[] { new { index = 0, id = "call_mock_1", type = "function", function = new { name = "render_ui", arguments = "" } } } });
                await Chunk(new { tool_calls = new[] { new { index = 0, function = new { arguments = args[..(args.Length / 2)] } } } });
                await Chunk(new { tool_calls = new[] { new { index = 0, function = new { arguments = args[(args.Length / 2)..] } } } });
                await Chunk(new { }, "tool_calls");
            }
            else
            {
                var text = hasToolResult ? "There's your card." : "Hello from the mock model.";
                await Chunk(new { role = "assistant", content = "" });
                foreach (var word in text.Split(' '))
                    await Chunk(new { content = word + (word == text.Split(' ')[^1] ? "" : " ") });
                await Chunk(new { }, "stop");
            }

            var done = Encoding.UTF8.GetBytes("data: [DONE]\n\n");
            await output.WriteAsync(done);
            response.Close();
        }

        public async ValueTask DisposeAsync()
        {
            cts.Cancel();
            try { listener.Stop(); } catch { }
            try { await loop.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            listener.Close();
        }
    }
}

using System.Text.Json.Nodes;
using Shiny.Controls.AgentUI;

namespace Sample.Features.Agents;

public partial class AgentViewPage : ContentPage
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public AgentViewPage()
    {
        InitializeComponent();

        // the built-in UI tools: the agent asks, a real control answers inline in the conversation
        var prompts = new AgentPromptQueue();
        Agent.Prompts = prompts;
        Agent.Tools = new AgentToolRegistry().AddAll(prompts);
        Agent.Suggestions = DemoAgentSession.Suggestions;
        Agent.Session = new DemoAgentSession();
        Agent.StateChanged += (_, state) => ShowReadingList(state);

        SizeChanged += (_, _) => PlaceStatePanel();
        SampleSourceCode.Attach(this);
    }

    void OnDemo(object? sender, EventArgs e)
    {
        Agent.Suggestions = DemoAgentSession.Suggestions;
        Agent.Session = new DemoAgentSession();
    }

    async void OnConnect(object? sender, EventArgs e)
    {
        var text = EndpointEntry.Text?.Trim();
        if (string.IsNullOrEmpty(text))
            text = EndpointEntry.Placeholder;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
        {
            await DisplayAlertAsync("Endpoint", "Enter an http(s) URL of an AG-UI endpoint.", "OK");
            return;
        }

        // the scripted test server answers these prompts; a real agent takes anything
        Agent.Suggestions = ["text", "reasoning", "frontend-tool", "a2ui", "interrupt", "error"];
        Agent.Session = new HttpAgentSession(Http, endpoint);
    }

    void OnClear(object? sender, EventArgs e) => Agent.Clear();

    // the panel is bound to the same shared state the agent edits; tapping a book edits it back
    void ShowReadingList(JsonNode? state)
    {
        BooksStack.Children.Clear();
        try
        {
            FillReadingList(state);
        }
        finally
        {
            PlaceStatePanel();
        }
    }

    void FillReadingList(JsonNode? state)
    {
        if (state?["books"] is not JsonArray books)
        {
            GoalLabel.Text = "Shared state - ask the agent to “track my reading list”.";
            return;
        }

        var done = books.Count(b => b?["done"]?.GetValue<bool>() == true);
        GoalLabel.Text = $"{done} of {state["goal"]?.ToString() ?? "?"} read this year";
        for (var i = 0; i < books.Count; i++)
        {
            var index = i;
            var book = books[i];
            var isDone = book?["done"]?.GetValue<bool>() == true;
            var row = new Label
            {
                Text = $"{(isDone ? "✓" : "○")}  {book?["title"]}",
                FontSize = 14,
                Padding = new Thickness(0, 4)
            };
            SemanticProperties.SetDescription(row, $"{book?["title"]}, {(isDone ? "read" : "not read")}. Tap to toggle.");
            row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => ToggleBook(index)) });
            BooksStack.Children.Add(row);
        }
    }

    void ToggleBook(int index)
    {
        if (Agent.State?.DeepClone() is not JsonObject state || state["books"]?[index] is not JsonObject book)
            return;
        book["done"] = !(book["done"]?.GetValue<bool>() ?? false);
        Agent.State = state; // goes to the agent with the next run
    }

    void PlaceStatePanel()
    {
        var wide = Width >= 760;
        Grid.SetRow(StatePanel, wide ? 1 : 2);
        Grid.SetColumn(StatePanel, wide ? 1 : 0);
        StatePanel.WidthRequest = wide ? 260 : -1;
        StatePanel.Margin = wide ? new Thickness(0, 0, 12, 12) : new Thickness(12, 0, 12, 12);
        // on a phone the panel only takes room once there is something in it
        StatePanel.IsVisible = wide || BooksStack.Children.Count > 0;
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using Shiny.Maui.Controls.Markdown;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Agents;

/// <summary>One conversation row. <see cref="Refresh"/> redraws it from its item (throttled while streaming).</summary>
abstract class AgentItemView : ContentView
{
    public abstract void Refresh();

    // a template's binding context is the item, which mutates in place: re-set it so bindings re-read
    protected static void Rebind(View view, object item)
    {
        view.BindingContext = null;
        view.BindingContext = item;
    }
}

/// <summary>The user's message: a right-aligned bubble.</summary>
sealed class AgentUserMessageView : AgentItemView
{
    readonly AgentMessageItem item;
    readonly Label? text;
    readonly View? custom;

    public AgentUserMessageView(AgentView owner, AgentMessageItem item)
    {
        this.item = item;
        if (owner.UserMessageTemplate is { } template)
        {
            this.custom = AgentView.Inflate(template, item, owner);
            this.Content = this.custom;
            return;
        }

        this.text = AgentTheme.Text("", ShinyThemeKeys.Type.BodyLargeSize, ShinyThemeKeys.Color.OnPrimaryContainer);
        this.Content = new Border
        {
            Padding = new Thickness(14, 10),
            Margin = new Thickness(48, 0, 0, 0),
            StrokeThickness = 0,
            HorizontalOptions = LayoutOptions.End,
            StrokeShape = AgentTheme.Corner(ShinyThemeKeys.Shape.CornerLargeRadius),
            Content = this.text
        }.Themed(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.PrimaryContainer);
    }

    public override void Refresh()
    {
        if (this.custom is not null)
        {
            Rebind(this.custom, this.item);
            return;
        }
        this.text!.Text = this.item.Text;
        SemanticProperties.SetDescription(this, "You said: " + this.item.Text);
    }
}

/// <summary>The agent's message: full-width markdown with a caret while it streams.</summary>
sealed class AgentAssistantMessageView : AgentItemView
{
    const string Caret = " ▍";

    readonly AgentMessageItem item;
    readonly MarkdownView? markdown;
    readonly View? custom;
    string? shown;

    public AgentAssistantMessageView(AgentView owner, AgentMessageItem item)
    {
        this.item = item;
        if (owner.AssistantMessageTemplate is { } template && item.IsAssistant)
        {
            this.custom = AgentView.Inflate(template, item, owner);
            this.Content = this.custom;
            return;
        }

        // the markdown add-on rebuilds only the blocks that changed, so setting it per flush is cheap
        this.markdown = new MarkdownView { IsScrollEnabled = false };
        this.Content = this.markdown;
        if (!item.IsAssistant)
            this.Opacity = 0.7; // system / developer messages restored from history
    }

    public override void Refresh()
    {
        if (this.custom is not null)
        {
            Rebind(this.custom, this.item);
            return;
        }

        var text = this.item.IsStreaming ? this.item.Text + Caret : this.item.Text;
        if (text == this.shown)
            return;
        this.shown = text;
        this.markdown!.Markdown = text;
        this.IsVisible = text.Length > 0;
        SemanticProperties.SetDescription(this, this.item.Text);
    }
}

/// <summary>Reasoning: a "Thought for 4s" row that expands to the dimmed text.</summary>
sealed class AgentReasoningView : AgentItemView
{
    readonly AgentReasoningItem item;
    readonly Label header;
    readonly Label body;
    bool expanded;

    public AgentReasoningView(AgentReasoningItem item, bool expanded)
    {
        this.item = item;
        this.expanded = expanded;
        this.header = AgentTheme.Text("", ShinyThemeKeys.Type.LabelLargeSize, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.body = AgentTheme.Text("", ShinyThemeKeys.Type.BodyMediumSize, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.body.FontAttributes = FontAttributes.Italic;
        this.body.Margin = new Thickness(12, 4, 0, 0);

        var rail = new BoxView { WidthRequest = 2, HorizontalOptions = LayoutOptions.Start }
            .Themed(BoxView.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);
        var bodyRow = new Grid { Children = { rail, this.body } };
        this.bodyRow = bodyRow;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            this.expanded = !this.expanded;
            this.Refresh();
        };
        this.header.GestureRecognizers.Add(tap);
        this.Content = new VerticalStackLayout { Spacing = 0, Children = { this.header, bodyRow } };
    }

    readonly Grid bodyRow;

    public override void Refresh()
    {
        var chevron = this.expanded ? "▾" : "▸";
        string title;
        if (this.item.IsStreaming)
            title = "Thinking…";
        else if (this.item.StartedAt is { } start && this.item.EndedAt is { } end)
            title = $"Thought for {Math.Max(1, (int)Math.Round((end - start).TotalSeconds))}s";
        else
            title = "Thought";

        this.header.Text = $"{chevron} {title}";
        this.body.Text = this.item.Text;
        this.bodyRow.IsVisible = this.expanded && this.item.Text.Length > 0;
        this.IsVisible = this.item.IsStreaming || this.item.Text.Length > 0;
        SemanticProperties.SetDescription(this.header, title + (this.expanded ? ", expanded" : ", collapsed"));
    }
}

/// <summary>
/// A tool call: name, live arguments, a spinner until the result, expandable JSON - and the pending
/// prompt of a frontend UI tool, drawn inline so the user answers right where the agent asked.
/// </summary>
sealed class AgentToolCallView : AgentItemView
{
    readonly AgentView owner;
    readonly AgentToolCallItem item;
    readonly View? custom;
    readonly ActivityIndicator spinner = new() { WidthRequest = 14, HeightRequest = 14, VerticalOptions = LayoutOptions.Center };
    readonly Label status;
    readonly Label name;
    readonly Label args;
    readonly Label chevron;
    readonly Label details;
    readonly ContentView promptHost = new() { IsVisible = false };
    readonly BoxView promptDivider;
    AgentPrompt? shownPrompt;
    bool expanded;

    public AgentToolCallView(AgentView owner, AgentToolCallItem item)
    {
        this.owner = owner;
        this.item = item;
        this.spinner.Themed(ActivityIndicator.ColorProperty, ShinyThemeKeys.Color.Primary);
        this.status = new Label { VerticalOptions = LayoutOptions.Center, FontSize = 14 };
        this.name = AgentTheme.Text("", ShinyThemeKeys.Type.LabelLargeSize);
        this.name.FontAttributes = FontAttributes.Bold;
        this.name.VerticalOptions = LayoutOptions.Center;
        this.args = AgentTheme.Text("", ShinyThemeKeys.Type.BodySmallSize, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.args.LineBreakMode = LineBreakMode.TailTruncation;
        this.args.MaxLines = 1;
        this.args.VerticalOptions = LayoutOptions.Center;
        this.chevron = AgentTheme.Text("▸", ShinyThemeKeys.Type.LabelLargeSize, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.chevron.VerticalOptions = LayoutOptions.Center;
        this.details = AgentTheme.Text("", ShinyThemeKeys.Type.BodySmallSize, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.details.FontFamily = DeviceInfo.Platform == DevicePlatform.Android ? "monospace" : "Menlo";
        this.details.IsVisible = false;
        this.promptDivider = new BoxView { HeightRequest = 1, IsVisible = false }.Themed(BoxView.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);

        View top;
        if (owner.ToolCallTemplates.TryGetValue(item.Name, out var template))
        {
            this.custom = AgentView.Inflate(template, item, owner);
            top = this.custom;
        }
        else
        {
            var header = new Grid
            {
                ColumnSpacing = 8,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                }
            };
            header.Add(new Grid { Children = { this.spinner, this.status } }, 0, 0);
            header.Add(this.name, 1, 0);
            header.Add(this.args, 2, 0);
            header.Add(this.chevron, 3, 0);
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                this.expanded = !this.expanded;
                this.Refresh();
            };
            header.GestureRecognizers.Add(tap);
            top = new VerticalStackLayout { Spacing = 6, Children = { header, this.details } };
        }

        this.Subagents = new VerticalStackLayout { Spacing = 8 };
        this.Content = new Border
        {
            Padding = new Thickness(12, 10),
            StrokeShape = AgentTheme.Corner(ShinyThemeKeys.Shape.CornerMediumRadius),
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children = { top, this.Subagents, this.promptDivider, this.promptHost }
            }
        }.Themed(Border.StrokeProperty, ShinyThemeKeys.Brush.OutlineVariant)
            .Themed(Border.StrokeThicknessProperty, ShinyThemeKeys.Border.Thin)
            .Themed(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerLow);
    }

    /// <summary>Where sub-agents this call started draw.</summary>
    public VerticalStackLayout Subagents { get; }

    public override void Refresh()
    {
        this.RefreshPrompt();
        if (this.custom is not null)
        {
            Rebind(this.custom, this.item);
            return;
        }

        var done = this.item.HasResult;
        this.spinner.IsRunning = this.spinner.IsVisible = !done;
        this.status.IsVisible = done;
        this.status.Text = this.item.Status == AgentToolCallStatus.Failed ? "✕" : "✓";
        this.status.Themed(Label.TextColorProperty, this.item.Status == AgentToolCallStatus.Failed ? ShinyThemeKeys.Color.Error : ShinyThemeKeys.Color.Success);
        this.name.Text = this.item.Name;
        this.args.Text = Compact(this.item.ArgumentsText);
        this.chevron.Text = this.expanded ? "▾" : "▸";

        this.details.IsVisible = this.expanded;
        if (this.expanded)
        {
            var text = "Arguments\n" + Pretty(this.item.ArgumentsText);
            if (this.item.Status == AgentToolCallStatus.Failed)
                text += "\n\nError\n" + this.item.Error;
            else if (this.item.Result is { } result)
                text += "\n\nResult\n" + Pretty(result);
            this.details.Text = text;
        }

        var state = this.item.Status switch
        {
            AgentToolCallStatus.Completed => "done",
            AgentToolCallStatus.Failed => "failed",
            AgentToolCallStatus.Running => "running",
            _ => "waiting"
        };
        SemanticProperties.SetDescription(this, $"Tool {this.item.Name}, {state}");
    }

    public void RefreshPrompt()
    {
        var prompt = this.owner.FindPrompt(this.item.ToolCallId);
        if (ReferenceEquals(prompt, this.shownPrompt))
            return;
        this.shownPrompt = prompt;
        this.promptHost.Content = prompt is null ? null : AgentPromptViews.Create(prompt);
        this.promptHost.IsVisible = this.promptDivider.IsVisible = prompt is not null;
    }

    static string Compact(string json) => json.Replace("\n", " ").Replace("\r", "");

    internal static string Pretty(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "";
        try
        {
            var node = JsonNode.Parse(json);
            if (node is null)
                return json;
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                node.WriteTo(writer);
            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return json; // still streaming, or not JSON
        }
    }
}

/// <summary>An activity: an A2UI surface (drawn by <see cref="A2UISurfaceView"/>), a template, or its JSON.</summary>
sealed class AgentActivityView : AgentItemView
{
    readonly AgentView owner;
    readonly AgentActivityItem item;
    readonly A2UIAgentBinding? binding;
    readonly VerticalStackLayout surfaces = new() { Spacing = 12 };
    readonly View? custom;
    readonly Label? json;

    public AgentActivityView(AgentView owner, AgentActivityItem item, A2UIAgentBinding? binding)
    {
        this.owner = owner;
        this.item = item;
        this.binding = binding;

        if (binding?.Handles(item) == true)
        {
            // the message is the container: it draws the one edge, and the surface's outermost Card drops its own
            this.Content = new Border
            {
                Padding = 14,
                StrokeShape = AgentTheme.Corner(ShinyThemeKeys.Shape.CornerMediumRadius),
                Content = this.surfaces
            }.Themed(Border.StrokeProperty, ShinyThemeKeys.Brush.OutlineVariant)
                .Themed(Border.StrokeThicknessProperty, ShinyThemeKeys.Border.Thin)
                .Themed(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerLow);
            // surfaces the binding applies after this row is created
            binding.Host.SurfaceCreated += this.OnSurfaceCreated;
            this.Unloaded += (_, _) => binding.Host.SurfaceCreated -= this.OnSurfaceCreated;
        }
        else if (owner.ActivityTemplates.TryGetValue(item.ActivityType, out var template))
        {
            this.custom = AgentView.Inflate(template, item, owner);
            this.Content = this.custom;
        }
        else
        {
            this.json = AgentTheme.Text("", ShinyThemeKeys.Type.BodySmallSize, ShinyThemeKeys.Color.OnSurfaceVariant);
            this.json.FontFamily = DeviceInfo.Platform == DevicePlatform.Android ? "monospace" : "Menlo";
            this.Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    AgentTheme.Text(item.ActivityType, ShinyThemeKeys.Type.LabelLargeSize),
                    this.json
                }
            };
        }
    }

    void OnSurfaceCreated(object? sender, A2UISurface surface) => this.Dispatcher.Dispatch(this.Refresh);

    public override void Refresh()
    {
        if (this.custom is not null)
        {
            Rebind(this.custom, this.item);
            return;
        }
        if (this.json is not null)
        {
            this.json.Text = AgentToolCallView.Pretty(this.item.Content?.ToJsonString());
            return;
        }
        if (this.binding is null)
            return;

        var wanted = this.binding.GetSurfaces(this.item.Id);
        var shown = this.surfaces.Children.OfType<A2UISurfaceView>().ToList();
        if (wanted.Count == shown.Count && wanted.Zip(shown).All(p => ReferenceEquals(p.First, p.Second.Surface)))
            return;

        this.surfaces.Children.Clear();
        foreach (var surface in wanted)
        {
            this.surfaces.Children.Add(new A2UISurfaceView
            {
                IsScrollEnabled = false,
                ShowBorder = false,
                Renderers = this.owner.Renderers,
                Surface = surface
            });
        }
        this.IsVisible = wanted.Count > 0;
    }
}

/// <summary>An error with Retry.</summary>
sealed class AgentErrorView : AgentItemView
{
    readonly AgentView owner;
    readonly AgentErrorItem item;
    readonly View? custom;
    readonly Button? retry;

    public AgentErrorView(AgentView owner, AgentErrorItem item)
    {
        this.owner = owner;
        this.item = item;
        if (owner.ErrorTemplate is { } template)
        {
            this.custom = AgentView.Inflate(template, item, owner);
            this.Content = this.custom;
            return;
        }

        var message = AgentTheme.Text($"⚠  {item.Message}", ShinyThemeKeys.Type.BodyMediumSize, ShinyThemeKeys.Color.OnErrorContainer);
        message.VerticalOptions = LayoutOptions.Center;
        this.retry = AgentTheme.LinkButton("Retry");
        this.retry.Themed(Button.TextColorProperty, ShinyThemeKeys.Color.OnErrorContainer);
        this.retry.FontAttributes = FontAttributes.Bold;
        this.retry.Command = owner.RetryCommand;
        var row = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        row.Add(message, 0, 0);
        row.Add(this.retry, 1, 0);
        this.Content = new Border
        {
            Padding = new Thickness(14, 8, 6, 8),
            StrokeThickness = 0,
            StrokeShape = AgentTheme.Corner(ShinyThemeKeys.Shape.CornerMediumRadius),
            Content = row
        }.Themed(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.ErrorContainer);
        SemanticProperties.SetDescription(this, "Error: " + item.Message);
    }

    public override void Refresh()
    {
        if (this.custom is not null)
            Rebind(this.custom, this.item);
    }
}

/// <summary>A sub-agent's items, indented under a name + status header that collapses.</summary>
sealed class AgentSubagentView : ContentView
{
    readonly Label header;
    readonly string fallbackName;
    bool collapsed;
    AgentSubagent? subagent;

    public AgentSubagentView(AgentSubagent? subagent, string id)
    {
        this.subagent = subagent;
        this.fallbackName = subagent?.Name ?? id;
        this.header = AgentTheme.Text("", ShinyThemeKeys.Type.LabelLargeSize, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.Items = new VerticalStackLayout { Spacing = 8, Padding = new Thickness(12, 0, 0, 0) };

        var rail = new BoxView { WidthRequest = 2, HorizontalOptions = LayoutOptions.Start }
            .Themed(BoxView.ColorProperty, ShinyThemeKeys.Color.Tertiary);
        var body = new Grid { Children = { rail, this.Items } };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            this.collapsed = !this.collapsed;
            body.IsVisible = !this.collapsed;
            this.Refresh(this.subagent);
        };
        this.header.GestureRecognizers.Add(tap);
        this.Content = new VerticalStackLayout { Spacing = 6, Children = { this.header, body } };
        this.Refresh(subagent);
    }

    public VerticalStackLayout Items { get; }

    public void Refresh(AgentSubagent? subagent)
    {
        this.subagent = subagent ?? this.subagent;
        var status = this.subagent?.Status switch
        {
            AgentSubagentStatus.Completed => "done",
            AgentSubagentStatus.Failed => "failed",
            AgentSubagentStatus.Suspended => "waiting for you",
            _ => "working…"
        };
        this.header.Text = $"{(this.collapsed ? "▸" : "▾")} 🤖 {this.subagent?.Name ?? this.fallbackName} · {status}";
        SemanticProperties.SetDescription(this.header, $"Sub-agent {this.subagent?.Name ?? this.fallbackName}, {status}");
    }
}

/// <summary>The default interrupt card: approve / reject, choices, or free text, per <see cref="AgentInterruptKind"/>.</summary>
static class AgentInterruptView
{
    public static View Create(AgentInterruptRequest request)
    {
        var message = AgentTheme.Text(request.Message, ShinyThemeKeys.Type.BodyLargeSize);
        var stack = new VerticalStackLayout { Spacing = 10, Children = { message } };
        var answered = AgentTheme.Text("Sent", ShinyThemeKeys.Type.BodySmallSize, ShinyThemeKeys.Color.OnSurfaceVariant);
        answered.IsVisible = false;

        View actions;
        switch (request.Kind)
        {
            case AgentInterruptKind.Choices when request.AllowMultiple:
            {
                var chips = new ChipGroup
                {
                    SelectionMode = ChipSelectionMode.Multiple,
                    ShowSelectionCheck = true,
                    ItemsSource = request.Choices.Select(c => c.Label).ToList()
                };
                var submit = AgentTheme.TonalButton("Submit", primary: true);
                submit.Clicked += (_, _) =>
                {
                    var labels = chips.SelectedItems?.Cast<object>().Select(o => o.ToString()).ToHashSet() ?? [];
                    request.Choose(request.Choices.Where(c => labels.Contains(c.Label)).Select(c => c.Id));
                };
                actions = new VerticalStackLayout { Spacing = 10, Children = { chips, AgentPromptViews.Actions(submit, ("Skip", false, request.Reject)) } };
                break;
            }
            case AgentInterruptKind.Choices:
            {
                var flex = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
                foreach (var choice in request.Choices)
                {
                    var button = AgentTheme.TonalButton(choice.Label);
                    button.Margin = new Thickness(0, 0, 8, 8);
                    button.Clicked += (_, _) => request.Choose([choice.Id]);
                    flex.Children.Add(button);
                }
                actions = flex;
                break;
            }
            case AgentInterruptKind.Text:
            {
                var entry = new Entry { Placeholder = "Your answer", ReturnType = ReturnType.Send }
                    .FontToken(ShinyThemeKeys.Type.BodyLargeSize)
                    .Themed(Entry.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
                SemanticProperties.SetDescription(entry, request.Message);
                var send = AgentTheme.TonalButton("Send", primary: true);
                void Reply()
                {
                    if (!string.IsNullOrWhiteSpace(entry.Text))
                        request.Reply(entry.Text);
                }
                send.Clicked += (_, _) => Reply();
                entry.Completed += (_, _) => Reply();
                actions = new VerticalStackLayout { Spacing = 10, Children = { entry, AgentPromptViews.Actions(send, ("Skip", false, request.Reject)) } };
                break;
            }
            default:
                actions = AgentPromptViews.Actions(null, ("Approve", true, request.Approve), ("Reject", false, request.Reject));
                break;
        }

        stack.Children.Add(actions);
        stack.Children.Add(answered);

        // once answered, the card stays as a record but can't be answered twice
        void Lock()
        {
            actions.IsEnabled = false;
            actions.Opacity = 0.5;
            answered.IsVisible = true;
        }
        foreach (var button in Descendants(actions).OfType<Button>())
            button.Clicked += (_, _) => { if (request.IsAnswered) Lock(); };

        var card = new Border
        {
            Padding = 14,
            StrokeShape = AgentTheme.Corner(ShinyThemeKeys.Shape.CornerMediumRadius),
            Content = stack
        }.Themed(Border.StrokeProperty, ShinyThemeKeys.Brush.Tertiary)
            .Themed(Border.StrokeThicknessProperty, ShinyThemeKeys.Border.Thin)
            .Themed(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.TertiaryContainer);
        message.Themed(Label.TextColorProperty, ShinyThemeKeys.Color.OnTertiaryContainer);
        SemanticProperties.SetDescription(card, "The agent needs your approval: " + request.Message);
        return card;
    }

    static IEnumerable<IView> Descendants(IView view)
    {
        yield return view;
        if (view is Layout layout)
        {
            foreach (var child in layout.Children)
            {
                foreach (var d in Descendants(child))
                    yield return d;
            }
        }
    }
}

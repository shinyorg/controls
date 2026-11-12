using System.Text.Json.Nodes;
using System.Windows.Input;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Agents;

/// <summary>
/// A complete agent conversation over AG-UI: streaming markdown, reasoning, tool call cards with live
/// arguments, A2UI surfaces drawn natively, sub-agents, approvals, frontend tools answered by real
/// Shiny controls inline, shared state, errors with Retry, and a composer whose Send turns into Stop.
/// </summary>
/// <remarks>
/// <para>
/// Give it a <see cref="Session"/> (or a <see cref="Runner"/> you own). Register frontend tools in
/// <see cref="Tools"/>; the built-in UI tools (<see cref="AgentUiTools"/>) need <see cref="Prompts"/>
/// too, so their questions draw inline.
/// </para>
/// <para>
/// The view fills its parent and owns the vertical scroll: never put it in a <c>ScrollView</c>. It
/// sticks to the bottom while the agent streams unless the user scrolls up, and offers a "jump to
/// latest" pill until they come back. Streamed text is flushed at about 30 frames a second rather
/// than per token.
/// </para>
/// </remarks>
public partial class AgentView : ContentView
{
    const double StickThreshold = 48;
    // about five lines of body text, plus the editor's own insets
    const double ComposerMaxHeight = 140;

    readonly Grid root;
    readonly Label stepsLabel;
    readonly ScrollView scroller;
    readonly VerticalStackLayout itemsStack;
    readonly VerticalStackLayout interruptsStack;
    readonly HorizontalStackLayout busyRow;
    readonly Button jumpButton;
    readonly ChipGroup suggestionChips;
    readonly Editor input;
    readonly Button sendButton;

    readonly Dictionary<AgentItem, AgentItemView> views = new(ReferenceEqualityComparer.Instance);
    readonly Dictionary<string, AgentSubagentView> subagentViews = new(StringComparer.Ordinal);
    readonly HashSet<AgentItemView> dirty = [];
    IDispatcherTimer? flushTimer;

    AgentRunner? attached;
    AgentRunner? ownedRunner;
    A2UIAgentBinding? binding;
    AgentPromptQueue? subscribedPrompts;
    CancellationTokenSource? turnCts;
    bool stickToBottom = true;
    bool syncingState;
    bool settingOwnedRunner;

    /// <summary>Creates an agent view. Set <see cref="Session"/> or <see cref="Runner"/> to start.</summary>
    public AgentView()
    {
        this.RetryCommand = new Command(this.Retry, () => this.attached is { IsBusy: false });

        this.stepsLabel = new Label
        {
            IsVisible = false,
            Padding = new Thickness(16, 6),
            LineBreakMode = LineBreakMode.TailTruncation
        }.FontToken(ShinyThemeKeys.Type.LabelMediumSize).Themed(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);

        this.itemsStack = new VerticalStackLayout { Spacing = 12 };
        this.interruptsStack = new VerticalStackLayout { Spacing = 12 };
        this.busyRow = new HorizontalStackLayout
        {
            Spacing = 8,
            IsVisible = false,
            Children =
            {
                new ActivityIndicator { IsRunning = true, WidthRequest = 16, HeightRequest = 16 }
                    .Themed(ActivityIndicator.ColorProperty, ShinyThemeKeys.Color.Primary),
                AgentTheme.Text("Working…", ShinyThemeKeys.Type.BodySmallSize, ShinyThemeKeys.Color.OnSurfaceVariant)
            }
        };
        SemanticProperties.SetDescription(this.busyRow, "The agent is working");

        var content = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(16, 12, 16, 16),
            Children = { this.itemsStack, this.interruptsStack, this.busyRow }
        };
        content.SizeChanged += (_, _) => this.OnContentGrew();

        this.scroller = new ScrollView { Orientation = ScrollOrientation.Vertical, Content = content };
        this.scroller.Scrolled += (_, _) => this.OnScrolled();

        this.jumpButton = AgentTheme.TonalButton("↓ Jump to latest");
        this.jumpButton.HorizontalOptions = LayoutOptions.Center;
        this.jumpButton.VerticalOptions = LayoutOptions.End;
        this.jumpButton.Margin = new Thickness(0, 0, 0, 10);
        this.jumpButton.IsVisible = false;
        this.jumpButton.Shadow = new Shadow { Radius = 8, Opacity = 0.25f, Offset = new Point(0, 2), Brush = Colors.Black };
        this.jumpButton.Clicked += (_, _) =>
        {
            this.stickToBottom = true;
            this.ScrollToEnd(true);
        };

        var conversation = new Grid { Children = { this.scroller, this.jumpButton } };

        this.suggestionChips = new ChipGroup
        {
            SelectionMode = ChipSelectionMode.None,
            Margin = new Thickness(16, 0, 16, 8),
            IsVisible = false
        };
        this.suggestionChips.ChipTapped += (_, e) => _ = this.SendAsync(e.Item?.ToString() ?? "");

        // a multi-line composer: grows with its text up to about five lines, then scrolls inside
        // itself (its own row, so it never nests in the conversation's scroller)
        this.input = new Editor
        {
            Placeholder = this.PlaceholderText,
            AutoSize = EditorAutoSizeOption.TextChanges,
            MaximumHeightRequest = ComposerMaxHeight,
            VerticalOptions = LayoutOptions.Center
        }.FontToken(ShinyThemeKeys.Type.BodyLargeSize)
            .Themed(Editor.TextColorProperty, ShinyThemeKeys.Color.OnSurface)
            .Themed(Editor.PlaceholderColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        this.input.TextChanged += this.OnComposerTextChanged;
        this.input.AutomationId = "AgentViewComposer";
        SemanticProperties.SetDescription(this.input, "Message");

        this.sendButton = new Button
        {
            Text = "Send",
            CornerRadius = 18,
            HeightRequest = 36,
            Padding = new Thickness(16, 0),
            VerticalOptions = LayoutOptions.Center
        }.FontToken(ShinyThemeKeys.Type.LabelLargeSize)
            .Themed(Button.BackgroundColorProperty, ShinyThemeKeys.Color.Primary)
            .Themed(Button.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);
        this.sendButton.AutomationId = "AgentViewSendButton";
        this.sendButton.Clicked += (_, _) =>
        {
            if (this.IsBusy)
                this.Cancel();
            else
                this.SendFromComposer();
        };

        var composerRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 8
        };
        composerRow.Add(ComposerField(this.input), 0, 0);
        composerRow.Add(this.sendButton, 1, 0);
        composerRow.Margin = new Thickness(12, 4, 12, 12);
        var composer = composerRow;

        this.root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };
        this.root.Add(this.stepsLabel, 0, 0);
        this.root.Add(conversation, 0, 1);
        this.root.Add(this.suggestionChips, 0, 2);
        this.root.Add(composer, 0, 3);
        this.root.Themed(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);

        this.Content = this.root;
        this.Unloaded += (_, _) => this.flushTimer?.Stop();
    }

    /// <summary>Re-runs the last turn (what an error row's Retry does).</summary>
    public ICommand RetryCommand { get; }


    // ── public actions ───────────────────────────────────────────────────────────

    /// <summary>Sends a user message. Ignored while a turn is running.</summary>
    public async Task SendAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || this.attached is not { IsBusy: false } runner)
            return;

        this.stickToBottom = true;
        try
        {
            await runner.SendAsync(text.Trim(), this.NewTurnToken());
        }
        catch (OperationCanceledException)
        {
            // Stop while a frontend tool was waiting on the user
        }
        catch (Exception ex)
        {
            // transport failures are recorded on the thread; anything else shows the same way
            if (runner.Thread.RunStatus != AgentRunStatus.Failed)
                runner.Thread.MarkFailed(ex.Message);
        }
    }

    /// <summary>Stops the running turn (Stop). Pending prompts are withdrawn.</summary>
    public void Cancel()
    {
        // AgentRunner.Cancel stops the turn, a waiting frontend tool included; the view's own turn
        // token also covers a turn started before the runner was attached
        this.turnCts?.Cancel();
        this.attached?.Cancel();
    }

    internal CancellationToken NewTurnToken()
    {
        this.turnCts?.Dispose();
        this.turnCts = new CancellationTokenSource();
        return this.turnCts.Token;
    }

    /// <summary>Stops any turn and clears the conversation, its state and its surfaces.</summary>
    public void Clear()
    {
        if (this.attached is not { } runner)
            return;
        runner.Cancel();
        runner.Thread.Clear();
        // a fresh surface host, so old surface ids can be reused by the agent
        this.Reattach();
        this.stickToBottom = true;
        this.jumpButton.IsVisible = false;
        this.ShrinkContent();
    }

    // iOS and Mac Catalyst editors are bare (a transparent UITextView), so a filled, unstroked field
    // shows where to type. Everywhere else the editor draws its own field (Android's underline,
    // WinUI's box, AppKit's and GTK's text view background), and that is the composer's edge - a
    // Border around it would stack two boxes.
    static View ComposerField(Editor editor)
    {
        var platform = DevicePlatformSafe();
        if (platform != DevicePlatform.iOS && platform != DevicePlatform.MacCatalyst)
            return editor;

        editor.BackgroundColor = Colors.Transparent;
        return new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 },
            Padding = new Thickness(10, 2),
            Content = editor
        }.Themed(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SurfaceContainerHigh);
    }

    static DevicePlatform DevicePlatformSafe()
    {
        try
        {
            return DeviceInfo.Current.Platform;
        }
        catch
        {
            return DevicePlatform.Unknown;
        }
    }

    bool trimmingNewline;

    // with SendOnEnter, a typed Enter arrives as exactly one inserted line break: drop it and send.
    // Pasted text (several characters at once) keeps its line breaks.
    void OnComposerTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (this.trimmingNewline || !this.SendOnEnter)
            return;
        if (!IsSingleNewlineInsert(e.OldTextValue ?? "", e.NewTextValue ?? "", out var without))
            return;

        // changing Text inside its own TextChanged is unsafe on some platforms, so do it next
        this.Dispatcher.Dispatch(() =>
        {
            this.trimmingNewline = true;
            this.input.Text = without;
            this.trimmingNewline = false;
            this.SendFromComposer();
        });
    }

    internal static bool IsSingleNewlineInsert(string oldText, string newText, out string without)
    {
        without = oldText;
        var inserted = newText.Length - oldText.Length;
        if (inserted is not (1 or 2))
            return false;

        var prefix = 0;
        while (prefix < oldText.Length && oldText[prefix] == newText[prefix])
            prefix++;
        if (!newText.EndsWith(oldText[prefix..], StringComparison.Ordinal))
            return false;

        var added = newText.Substring(prefix, inserted);
        return added is "\n" or "\r" or "\r\n";
    }

    void SendFromComposer()
    {
        var text = this.input.Text;
        if (string.IsNullOrWhiteSpace(text) || this.IsBusy)
            return;
        this.input.Text = "";
        _ = this.SendAsync(text);
    }

    void Retry() => _ = this.RetryAsync();

    /// <summary>Re-runs the conversation from where it failed (an error row's Retry).</summary>
    public Task RetryAsync()
    {
        if (this.attached is not { IsBusy: false } runner)
            return Task.CompletedTask;
        this.stickToBottom = true;
        return Swallow(runner.RetryAsync(this.NewTurnToken()));
    }

    internal static async Task Swallow(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // stopped by the user
        }
    }

    // ── wiring ───────────────────────────────────────────────────────────────────

    void OnSessionChanged(IAgentSession? session)
    {
        if (this.Runner is not null && !ReferenceEquals(this.Runner, this.ownedRunner))
            return; // the app owns the runner; Session is ignored

        var old = this.ownedRunner;
        this.ownedRunner = session is null ? null : new AgentRunner(session, tools: this.Tools ?? new AgentToolRegistry());
        this.settingOwnedRunner = true;
        this.Runner = this.ownedRunner;
        this.settingOwnedRunner = false;
        this.Attach(this.ownedRunner);
        old?.Dispose();

        if (session is IAgentHistorySession && this.ownedRunner is { } runner)
            _ = runner.LoadAsync();
    }

    void OnRunnerChanged(AgentRunner? runner)
    {
        if (this.settingOwnedRunner)
            return;
        if (!ReferenceEquals(runner, this.ownedRunner))
        {
            this.ownedRunner?.Dispose();
            this.ownedRunner = null;
        }
        this.MergeTools();
        this.Attach(runner);
    }

    void MergeTools()
    {
        if (this.attached is not { } runner || this.Tools is not { } tools || ReferenceEquals(runner.Tools, tools))
            return;
        foreach (var tool in tools.Tools)
        {
            if (!runner.Tools.Contains(tool.Name))
                runner.Tools.Add(tool);
        }
    }

    void OnPromptsChanged(AgentPromptQueue? old, AgentPromptQueue? prompts)
    {
        if (old is not null)
            old.Changed -= this.OnPromptsQueueChanged;
        this.subscribedPrompts = prompts;
        if (prompts is not null)
            prompts.Changed += this.OnPromptsQueueChanged;
        this.OnPromptsQueueChanged(this, EventArgs.Empty);
    }

    void OnPromptsQueueChanged(object? sender, EventArgs e)
    {
        foreach (var view in this.views.Values.OfType<AgentToolCallView>())
            view.RefreshPrompt();
    }

    internal AgentPrompt? FindPrompt(string toolCallId) => this.subscribedPrompts?.Find(toolCallId);

    void Reattach() => this.Attach(this.attached);

    void Attach(AgentRunner? runner)
    {
        if (this.attached is not null)
        {
            this.attached.Thread.Changed -= this.OnThreadChanged;
            this.attached.BusyChanged -= this.OnBusyChanged;
        }
        this.binding?.Dispose();
        this.binding = null;

        this.attached = runner;
        if (runner is not null)
        {
            // the binding subscribes to the thread first, so a surface exists before its activity row draws
            this.binding = new A2UIAgentBinding(runner, new A2UISurfaceHost(this.Catalogs));
            runner.Thread.Changed += this.OnThreadChanged;
            runner.BusyChanged += this.OnBusyChanged;
            this.MergeTools();
        }

        this.RebuildItems();
        this.OnBusyChanged(this, EventArgs.Empty);
        this.SyncState();
    }

    void OnThreadChanged(object? sender, AgentThreadChange change)
    {
        switch (change.Kind)
        {
            case AgentThreadChangeKind.ItemAdded when change.Item is { } item:
                this.AddItem(item);
                this.UpdateSuggestions();
                break;

            case AgentThreadChangeKind.ItemUpdated when change.Item is { } item:
                if (this.views.TryGetValue(item, out var view))
                    this.MarkDirty(view);
                break;

            case AgentThreadChangeKind.ItemsReset:
                this.RebuildItems();
                break;

            case AgentThreadChangeKind.StateChanged:
                this.SyncState();
                break;

            case AgentThreadChangeKind.RunStatusChanged:
                this.UpdateInterrupts();
                this.UpdateSteps();
                break;

            case AgentThreadChangeKind.StepsChanged:
                this.UpdateSteps();
                break;

            case AgentThreadChangeKind.SubagentsChanged:
                this.UpdateSubagents();
                break;
        }
    }

    void OnBusyChanged(object? sender, EventArgs e)
    {
        var busy = this.attached?.IsBusy ?? false;
        this.SetValue(IsBusyPropertyKey, busy);
        this.sendButton.Text = busy ? "Stop" : "Send";
        SemanticProperties.SetDescription(this.sendButton, busy ? "Stop the agent" : "Send message");
        this.busyRow.IsVisible = busy;
        ((Command)this.RetryCommand).ChangeCanExecute();
        foreach (var view in this.views.Values.OfType<AgentErrorView>())
            view.Refresh();
        this.UpdateSuggestions();
        this.UpdateSteps();
    }

    // ── items ────────────────────────────────────────────────────────────────────

    void RebuildItems()
    {
        this.FlushNow();
        this.views.Clear();
        this.subagentViews.Clear();
        this.dirty.Clear();
        this.itemsStack.Children.Clear();

        if (this.attached is { } runner)
        {
            foreach (var item in runner.Thread.Items)
                this.AddItem(item);
        }
        this.UpdateInterrupts();
        this.UpdateSteps();
        this.UpdateSuggestions();
    }

    void AddItem(AgentItem item)
    {
        if (this.views.ContainsKey(item))
            return;

        AgentItemView? view = item switch
        {
            AgentMessageItem { IsUser: true } m => new AgentUserMessageView(this, m),
            AgentMessageItem m => new AgentAssistantMessageView(this, m),
            AgentReasoningItem r when this.ShowReasoning != AgentReasoningDisplay.Hidden => new AgentReasoningView(r, this.ShowReasoning == AgentReasoningDisplay.Expanded),
            AgentToolCallItem t => new AgentToolCallView(this, t),
            AgentActivityItem a => new AgentActivityView(this, a, this.binding),
            AgentErrorItem e => new AgentErrorView(this, e),
            _ => null
        };
        if (view is null)
            return;

        this.views[item] = view;
        this.ContainerFor(item.SubagentRunId).Children.Add(view);
        view.Refresh();
    }

    // sub-agent items go into an indented group, under the tool call that started it when we know it
    Layout ContainerFor(string? subagentRunId)
    {
        if (subagentRunId is null || this.attached is not { } runner)
            return this.itemsStack;

        if (this.subagentViews.TryGetValue(subagentRunId, out var group))
            return group.Items;

        var subagent = runner.Thread.FindSubagent(subagentRunId);
        group = new AgentSubagentView(this, subagent, subagentRunId);
        this.subagentViews[subagentRunId] = group;

        Layout parent = this.itemsStack;
        if (subagent?.ParentSubagentRunId is { } outer)
            parent = this.ContainerFor(outer);
        else if (subagent?.ParentToolCallId is { } callId
                 && runner.Thread.FindToolCall(callId) is { } call
                 && this.views.TryGetValue(call, out var callView)
                 && callView is AgentToolCallView toolView)
            parent = toolView.Subagents;
        parent.Children.Add(group);
        return group.Items;
    }

    void UpdateSubagents()
    {
        if (this.attached is not { } runner)
            return;
        foreach (var subagent in runner.Thread.Subagents)
        {
            if (this.subagentViews.TryGetValue(subagent.Id, out var group))
                group.Refresh(subagent);
        }
    }

    // ── throttled streaming ──────────────────────────────────────────────────────

    void MarkDirty(AgentItemView view)
    {
        this.dirty.Add(view);
        if (this.flushTimer is null)
        {
            this.flushTimer = this.Dispatcher.CreateTimer();
            this.flushTimer.Interval = TimeSpan.FromMilliseconds(33);
            this.flushTimer.Tick += (_, _) => this.FlushNow();
        }
        if (!this.flushTimer.IsRunning)
            this.flushTimer.Start();
    }

    void FlushNow()
    {
        this.flushTimer?.Stop();
        if (this.dirty.Count == 0)
            return;
        var pending = this.dirty.ToList();
        this.dirty.Clear();
        foreach (var view in pending)
            view.Refresh();
    }

    // ── interrupts, steps, suggestions, state ────────────────────────────────────

    internal void UpdateInterrupts()
    {
        this.interruptsStack.Children.Clear();
        if (this.attached is not { } runner
            || runner.Thread.RunStatus != AgentRunStatus.Interrupted
            || runner.Thread.PendingInterrupts.Count == 0)
            return;

        var batch = new AgentInterruptBatch(runner, runner.Thread.PendingInterrupts, this.NewTurnToken);
        foreach (var request in batch.Requests)
        {
            this.interruptsStack.Children.Add(this.InterruptTemplate is { } template
                ? Inflate(template, request, this)
                : AgentInterruptView.Create(request));
        }
    }

    internal void UpdateSteps()
    {
        var steps = this.attached?.Thread.Steps ?? [];
        var show = this.ShowSteps && this.attached is { IsBusy: true } && steps.Count > 0;
        this.stepsLabel.IsVisible = show;
        if (show)
        {
            this.stepsLabel.Text = string.Join("  ›  ", steps.Select(s => s.Name));
            SemanticProperties.SetDescription(this.stepsLabel, "Running: " + this.stepsLabel.Text);
        }
    }

    void UpdateSuggestions()
    {
        var items = this.Suggestions?.ToList() ?? [];
        var empty = this.attached is null || this.attached.Thread.Items.Count == 0;
        var show = items.Count > 0 && empty && !this.IsBusy;
        if (show && !ReferenceEquals(this.suggestionChips.ItemsSource, this.Suggestions))
            this.suggestionChips.ItemsSource = items;
        this.suggestionChips.IsVisible = show;
    }

    void SyncState()
    {
        var state = this.attached?.Thread.State;
        this.syncingState = true;
        this.State = state;
        this.syncingState = false;
        this.StateChanged?.Invoke(this, state);
    }

    void OnStatePropertyChanged(JsonNode? state)
    {
        if (this.syncingState || this.attached is not { } runner || ReferenceEquals(runner.Thread.State, state))
            return;
        runner.Thread.SetState(state); // raises StateChanged, which syncs back here
    }

    // ── scrolling ────────────────────────────────────────────────────────────────

    // Where the content really ends (its last visible row plus padding), not its arranged height or
    // ScrollView.ContentSize: on Android the ScrollView keeps its content at least viewport + offset
    // tall, so after a Clear the arranged height still allowed the old offset and "scroll to the end"
    // went right back to it, leaving a short new conversation above the top edge. Read off the rows'
    // frames, so it costs nothing per scroll event.
    double ScrollMax
    {
        get
        {
            if (this.scroller.Content is not Layout content)
                return 0;
            var bottom = content.Children.OfType<View>().Where(v => v.IsVisible).Select(v => v.Frame.Bottom).DefaultIfEmpty(0).Max();
            return Math.Max(0, bottom + content.Padding.Bottom - this.scroller.Height);
        }
    }

    void OnScrolled()
    {
        var max = this.ScrollMax;
        var atBottom = max <= 0 || this.scroller.ScrollY >= max - StickThreshold;
        this.stickToBottom = atBottom;
        this.jumpButton.IsVisible = !atBottom;
    }

    // after a Clear the new conversation is shorter: go to its (measured) end, which also drops the old offset
    void ShrinkContent() => this.ScrollToEnd(false);

    void OnContentGrew()
    {
        if (this.stickToBottom)
            this.ScrollToEnd(false);
        else
            this.jumpButton.IsVisible = true;
    }

    void ScrollToEnd(bool animated)
        => this.Dispatcher.Dispatch(async () =>
        {
            var max = this.ScrollMax;
            // also when the content fits: after Clear, Android keeps the old (now out-of-range) offset,
            // which left a short new conversation scrolled out of sight above the viewport
            if (max > 0 || this.scroller.ScrollY > 0)
                await this.scroller.ScrollToAsync(0, max, animated && max > 0);
            this.jumpButton.IsVisible = false;
        });

    // ── templates ────────────────────────────────────────────────────────────────

    internal static View Inflate(DataTemplate template, object item, BindableObject container)
    {
        var chosen = template is DataTemplateSelector selector ? selector.SelectTemplate(item, container) : template;
        var content = chosen.CreateContent();
        var view = content as View
            ?? throw new InvalidOperationException("An AgentView template must create a View.");
        view.BindingContext = item;
        return view;
    }
}

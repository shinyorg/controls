using System.Text.Json.Nodes;

namespace Shiny.Maui.Controls.Agents;

/// <summary>Templates keyed by a name: <see cref="AgentView.ToolCallTemplates"/> by tool name, <see cref="AgentView.ActivityTemplates"/> by activity type.</summary>
public sealed class AgentTemplateDictionary : Dictionary<string, DataTemplate>
{
    /// <summary>Creates an empty, case-sensitive dictionary.</summary>
    public AgentTemplateDictionary() : base(StringComparer.Ordinal) { }
}

public partial class AgentView
{
    // ── connection ───────────────────────────────────────────────────────────────

    /// <summary>The agent to talk to. The view creates (and owns) an <see cref="AgentRunner"/> over it.</summary>
    public static readonly BindableProperty SessionProperty = BindableProperty.Create(
        nameof(Session), typeof(IAgentSession), typeof(AgentView), null,
        propertyChanged: (b, _, n) => ((AgentView)b).OnSessionChanged(n as IAgentSession));

    /// <inheritdoc cref="SessionProperty"/>
    public IAgentSession? Session
    {
        get => (IAgentSession?)this.GetValue(SessionProperty);
        set => this.SetValue(SessionProperty, value);
    }

    /// <summary>
    /// The runner the view draws and drives. Set it to own the conversation yourself (the view never
    /// disposes a runner you gave it); otherwise it is the one created for <see cref="Session"/>.
    /// </summary>
    public static readonly BindableProperty RunnerProperty = BindableProperty.Create(
        nameof(Runner), typeof(AgentRunner), typeof(AgentView), null,
        propertyChanged: (b, _, n) => ((AgentView)b).OnRunnerChanged(n as AgentRunner));

    /// <inheritdoc cref="RunnerProperty"/>
    public AgentRunner? Runner
    {
        get => (AgentRunner?)this.GetValue(RunnerProperty);
        set => this.SetValue(RunnerProperty, value);
    }

    /// <summary>Frontend tools offered to the agent on every run (merged into the runner's registry).</summary>
    public static readonly BindableProperty ToolsProperty = BindableProperty.Create(
        nameof(Tools), typeof(AgentToolRegistry), typeof(AgentView), null,
        propertyChanged: (b, _, _) => ((AgentView)b).MergeTools());

    /// <inheritdoc cref="ToolsProperty"/>
    public AgentToolRegistry? Tools
    {
        get => (AgentToolRegistry?)this.GetValue(ToolsProperty);
        set => this.SetValue(ToolsProperty, value);
    }

    /// <summary>
    /// The prompts the built-in UI tools (<see cref="AgentUiTools"/>) wait on. The view draws each pending
    /// prompt inline in its tool call's card.
    /// </summary>
    public static readonly BindableProperty PromptsProperty = BindableProperty.Create(
        nameof(Prompts), typeof(AgentPromptQueue), typeof(AgentView), null,
        propertyChanged: (b, o, n) => ((AgentView)b).OnPromptsChanged(o as AgentPromptQueue, n as AgentPromptQueue));

    /// <inheritdoc cref="PromptsProperty"/>
    public AgentPromptQueue? Prompts
    {
        get => (AgentPromptQueue?)this.GetValue(PromptsProperty);
        set => this.SetValue(PromptsProperty, value);
    }

    /// <summary>The A2UI catalogs the agent's surfaces may use. Defaults to basic + Shiny.</summary>
    public static readonly BindableProperty CatalogsProperty = BindableProperty.Create(
        nameof(Catalogs), typeof(A2UICatalogRegistry), typeof(AgentView), null,
        defaultValueCreator: _ => new A2UICatalogRegistry().Add(A2UICatalog.CreateShiny()),
        propertyChanged: (b, _, _) => ((AgentView)b).Reattach());

    /// <inheritdoc cref="CatalogsProperty"/>
    public A2UICatalogRegistry Catalogs
    {
        get => (A2UICatalogRegistry)this.GetValue(CatalogsProperty);
        set => this.SetValue(CatalogsProperty, value);
    }

    /// <summary>The A2UI component renderers. Defaults to basic + Shiny; register your own for app catalogs.</summary>
    public static readonly BindableProperty RenderersProperty = BindableProperty.Create(
        nameof(Renderers), typeof(A2UIRendererRegistry), typeof(AgentView), null,
        defaultValueCreator: _ => A2UIRendererRegistry.CreateDefault(),
        propertyChanged: (b, _, _) => ((AgentView)b).Reattach());

    /// <inheritdoc cref="RenderersProperty"/>
    public A2UIRendererRegistry Renderers
    {
        get => (A2UIRendererRegistry)this.GetValue(RenderersProperty);
        set => this.SetValue(RenderersProperty, value);
    }

    // ── display ──────────────────────────────────────────────────────────────────

    /// <summary>How the model's reasoning shows: hidden, a collapsed "Thought for 4s" row (default), or expanded.</summary>
    public static readonly BindableProperty ShowReasoningProperty = BindableProperty.Create(
        nameof(ShowReasoning), typeof(AgentReasoningDisplay), typeof(AgentView), AgentReasoningDisplay.Collapsed,
        propertyChanged: (b, _, _) => ((AgentView)b).RebuildItems());

    /// <inheritdoc cref="ShowReasoningProperty"/>
    public AgentReasoningDisplay ShowReasoning
    {
        get => (AgentReasoningDisplay)this.GetValue(ShowReasoningProperty);
        set => this.SetValue(ShowReasoningProperty, value);
    }

    /// <summary>Whether the running step names show as a breadcrumb above the conversation (default true).</summary>
    public static readonly BindableProperty ShowStepsProperty = BindableProperty.Create(
        nameof(ShowSteps), typeof(bool), typeof(AgentView), true,
        propertyChanged: (b, _, _) => ((AgentView)b).UpdateSteps());

    /// <inheritdoc cref="ShowStepsProperty"/>
    public bool ShowSteps
    {
        get => (bool)this.GetValue(ShowStepsProperty);
        set => this.SetValue(ShowStepsProperty, value);
    }

    /// <summary>The composer's placeholder.</summary>
    public static readonly BindableProperty PlaceholderTextProperty = BindableProperty.Create(
        nameof(PlaceholderText), typeof(string), typeof(AgentView), "Ask anything…",
        propertyChanged: (b, _, n) => ((AgentView)b).input.Placeholder = n as string);

    /// <inheritdoc cref="PlaceholderTextProperty"/>
    public string? PlaceholderText
    {
        get => (string?)this.GetValue(PlaceholderTextProperty);
        set => this.SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>Prompts offered as chips while the conversation is empty. Tapping one sends it.</summary>
    public static readonly BindableProperty SuggestionsProperty = BindableProperty.Create(
        nameof(Suggestions), typeof(IEnumerable<string>), typeof(AgentView), null,
        propertyChanged: (b, _, _) => ((AgentView)b).UpdateSuggestions());

    /// <inheritdoc cref="SuggestionsProperty"/>
    public IEnumerable<string>? Suggestions
    {
        get => (IEnumerable<string>?)this.GetValue(SuggestionsProperty);
        set => this.SetValue(SuggestionsProperty, value);
    }

    // ── templates ────────────────────────────────────────────────────────────────

    /// <summary>Replaces the user bubble. Binding context: the <see cref="AgentMessageItem"/>, re-set on every change.</summary>
    public static readonly BindableProperty UserMessageTemplateProperty = BindableProperty.Create(
        nameof(UserMessageTemplate), typeof(DataTemplate), typeof(AgentView), null,
        propertyChanged: (b, _, _) => ((AgentView)b).RebuildItems());

    /// <inheritdoc cref="UserMessageTemplateProperty"/>
    public DataTemplate? UserMessageTemplate
    {
        get => (DataTemplate?)this.GetValue(UserMessageTemplateProperty);
        set => this.SetValue(UserMessageTemplateProperty, value);
    }

    /// <summary>Replaces the assistant's markdown. Binding context: the <see cref="AgentMessageItem"/>, re-set as text streams (throttled).</summary>
    public static readonly BindableProperty AssistantMessageTemplateProperty = BindableProperty.Create(
        nameof(AssistantMessageTemplate), typeof(DataTemplate), typeof(AgentView), null,
        propertyChanged: (b, _, _) => ((AgentView)b).RebuildItems());

    /// <inheritdoc cref="AssistantMessageTemplateProperty"/>
    public DataTemplate? AssistantMessageTemplate
    {
        get => (DataTemplate?)this.GetValue(AssistantMessageTemplateProperty);
        set => this.SetValue(AssistantMessageTemplateProperty, value);
    }

    /// <summary>
    /// Per-tool renderers by tool name - generative UI through tool calls. Binding context: the
    /// <see cref="AgentToolCallItem"/>. A pending prompt for the call still draws under it.
    /// </summary>
    public static readonly BindableProperty ToolCallTemplatesProperty = BindableProperty.Create(
        nameof(ToolCallTemplates), typeof(AgentTemplateDictionary), typeof(AgentView), null,
        defaultValueCreator: _ => new AgentTemplateDictionary(),
        propertyChanged: (b, _, _) => ((AgentView)b).RebuildItems());

    /// <inheritdoc cref="ToolCallTemplatesProperty"/>
    public AgentTemplateDictionary ToolCallTemplates
    {
        get => (AgentTemplateDictionary)this.GetValue(ToolCallTemplatesProperty);
        set => this.SetValue(ToolCallTemplatesProperty, value);
    }

    /// <summary>
    /// Renderers for activity types other than A2UI surfaces (which always draw with
    /// <see cref="A2UISurfaceView"/>). Binding context: the <see cref="AgentActivityItem"/>. Unknown
    /// types show their JSON.
    /// </summary>
    public static readonly BindableProperty ActivityTemplatesProperty = BindableProperty.Create(
        nameof(ActivityTemplates), typeof(AgentTemplateDictionary), typeof(AgentView), null,
        defaultValueCreator: _ => new AgentTemplateDictionary(),
        propertyChanged: (b, _, _) => ((AgentView)b).RebuildItems());

    /// <inheritdoc cref="ActivityTemplatesProperty"/>
    public AgentTemplateDictionary ActivityTemplates
    {
        get => (AgentTemplateDictionary)this.GetValue(ActivityTemplatesProperty);
        set => this.SetValue(ActivityTemplatesProperty, value);
    }

    /// <summary>Replaces an interrupt card. Binding context: an <see cref="AgentInterruptRequest"/> (use its commands to answer).</summary>
    public static readonly BindableProperty InterruptTemplateProperty = BindableProperty.Create(
        nameof(InterruptTemplate), typeof(DataTemplate), typeof(AgentView), null,
        propertyChanged: (b, _, _) => ((AgentView)b).UpdateInterrupts());

    /// <inheritdoc cref="InterruptTemplateProperty"/>
    public DataTemplate? InterruptTemplate
    {
        get => (DataTemplate?)this.GetValue(InterruptTemplateProperty);
        set => this.SetValue(InterruptTemplateProperty, value);
    }

    /// <summary>Replaces the error row. Binding context: the <see cref="AgentErrorItem"/>; retry with <see cref="RetryCommand"/>.</summary>
    public static readonly BindableProperty ErrorTemplateProperty = BindableProperty.Create(
        nameof(ErrorTemplate), typeof(DataTemplate), typeof(AgentView), null,
        propertyChanged: (b, _, _) => ((AgentView)b).RebuildItems());

    /// <inheritdoc cref="ErrorTemplateProperty"/>
    public DataTemplate? ErrorTemplate
    {
        get => (DataTemplate?)this.GetValue(ErrorTemplateProperty);
        set => this.SetValue(ErrorTemplateProperty, value);
    }

    // ── state ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The conversation's shared state (AG-UI <c>STATE_SNAPSHOT</c> / <c>STATE_DELTA</c>). Setting it
    /// replaces the state; it goes to the agent with the next run.
    /// </summary>
    public static readonly BindableProperty StateProperty = BindableProperty.Create(
        nameof(State), typeof(JsonNode), typeof(AgentView), null, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => ((AgentView)b).OnStatePropertyChanged(n as JsonNode));

    /// <inheritdoc cref="StateProperty"/>
    public JsonNode? State
    {
        get => (JsonNode?)this.GetValue(StateProperty);
        set => this.SetValue(StateProperty, value);
    }

    static readonly BindablePropertyKey IsBusyPropertyKey = BindableProperty.CreateReadOnly(
        nameof(IsBusy), typeof(bool), typeof(AgentView), false);

    /// <summary>True while a turn runs (including frontend tool round trips). The Send button is Stop meanwhile.</summary>
    public static readonly BindableProperty IsBusyProperty = IsBusyPropertyKey.BindableProperty;

    /// <inheritdoc cref="IsBusyProperty"/>
    public bool IsBusy => (bool)this.GetValue(IsBusyProperty);

    /// <summary>Raised when the shared state changes - from the agent or from <see cref="State"/>.</summary>
    public event EventHandler<JsonNode?>? StateChanged;
}

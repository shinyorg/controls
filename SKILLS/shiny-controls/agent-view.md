# AgentView (AG-UI agent conversation)

NuGet: `Shiny.Maui.Controls.Agents` / `Shiny.Blazor.Controls.Agents`. The engine `Shiny.Controls.AgentUI.Shared` comes with them.

- Namespaces: `Shiny.Controls.AgentUI` (sessions, runner, tools, prompts, items) and `Shiny.Controls.AgentUI.A2UI`.
- MAUI XAML: `xmlns:shiny="http://shiny.net/maui/controls"` (`shiny:AgentView`).
- Blazor: `@using Shiny.Blazor.Controls.Agents`.

AgentView renders an AG-UI event stream: streaming markdown, reasoning, tool call cards, A2UI surfaces, sub-agents, steps, interrupts (approvals and questions), errors with Retry, and a composer whose Send turns into Stop.

## When to use
- **AgentView**: a user talking to an AI agent, with tools, generative UI or approvals.
- **ChatView**: people talking to people (read receipts, typing, reactions).
- **AiChatView** (Shiny.AiConversation.Maui): feature-frozen. Use AgentView for new agent UIs.

## Rules for generated code
- The view takes an **`IAgentSession`**, never an `IChatClient` or `HttpClient`:
  - From Shiny.AiConversation: `AgentSession.Create(session.SessionId, session.RunAgUiAsync)` (package `Shiny.AiConversation.AgUi`).
  - From any delegate with the shape `(RunAgentInput, CancellationToken) → IAsyncEnumerable<BaseEvent>`: `AgentSession.Create(threadId, run)`.
  - From an AG-UI HTTP endpoint: POST `RunAgentInput` (JSON via `AGUIJsonSerializerContext.Default.RunAgentInput`, `Accept: text/event-stream`) and return `response.ReadAGUIEventStreamAsync(ct)` from `AGUI.Client`.
- **It fills its parent and owns vertical scroll.** MAUI: put it in a `*` Grid row, never in a `ScrollView`. Blazor: give the parent a height (`height:100%` or `flex:1; min-height:0`) and no `overflow:auto`.
- **UI tools.** For the agent to ask the user something, create one `AgentPromptQueue`, then set `Prompts` to it and `Tools = new AgentToolRegistry().AddAll(prompts)`. That adds the tools `confirm`, `choose_one`, `choose_many`, `pick_date`, `pick_date_range` and `ask_text`. The prompts are drawn inline under the tool card. **Set `Prompts` to the same queue**, or the tool waits forever.
- **Typed frontend tools without UI:** `AgentTool.Create<TArgs,TResult>(name, description, MyJsonContext.Default.TArgs, MyJsonContext.Default.TResult, handler, schema)`. Use source-generated `JsonTypeInfo`; no reflection.
- **Default catalogs are basic + Shiny.** To add your own A2UI component, register a renderer in `Renderers` and add a definition to a catalog in `Catalogs`.
- **Shared state.** Bind `State` (`JsonNode?`) or handle `StateChanged`. App writes go out with the next run.
- **Don't** turn on `Runner.SendActivityMessages` / `SendReasoningMessages` for the official .NET `AGUI.Server`: it rejects those roles. They are for TypeScript or Python agents that accept them.

## MAUI
```xml
<Grid RowDefinitions="*">
    <shiny:AgentView x:Name="Agent" PlaceholderText="Ask anything…" ShowReasoning="Collapsed" />
</Grid>
```
```csharp
var prompts = new AgentPromptQueue();
Agent.Prompts = prompts;
Agent.Tools = new AgentToolRegistry().AddAll(prompts);
Agent.Suggestions = ["Show me the sales dashboard", "Book a trip"];
Agent.Session = AgentSession.Create(aiSession.SessionId, aiSession.RunAgUiAsync);
Agent.StateChanged += (_, _) => Refresh(Agent.State);
```
- Templates are `DataTemplate`s whose binding context is the item:
  - `UserMessageTemplate` and `AssistantMessageTemplate`: `AgentMessageItem`.
  - `ToolCallTemplates` / `ActivityTemplates` (`AgentTemplateDictionary`, keyed by tool name or activity type): `AgentToolCallItem` / `AgentActivityItem`.
  - `InterruptTemplate`: `AgentInterruptRequest`, with `Kind`, `Message`, `Choices`, `AllowMultiple`, and `ApproveCommand` / `RejectCommand` / `ChooseCommand` / `ReplyCommand`.
  - `ErrorTemplate`: `AgentErrorItem`; retry with `RetryCommand`.
  - `SubagentTemplate`: `AgentSubagent` (`Name`, `Status`, `Description`, `Error`). Replaces the whole sub-agent group; `AgentSubagent` isn't observable, so the view re-sets the binding context when it changes.
- The composer is a multi-line `Editor` (grows to ~5 lines, then scrolls). `SendOnEnter` (default true on Desktop idiom, false on phone/tablet): Enter sends; pasted multi-line text is kept. Don't wrap the view to add your own composer border.
- Methods: `SendAsync`, `Cancel`, `Clear`, `RetryAsync`. `IsBusy` is read-only.

## Blazor
```razor
@using Shiny.Blazor.Controls.Agents
@using Shiny.Controls.AgentUI

<div style="height:100%; display:flex; flex-direction:column; min-height:0">
    <AgentView Session="@session" Tools="@tools" Prompts="@prompts"
               Suggestions="@suggestions" StateChanged="OnState" />
</div>

@code {
    readonly AgentPromptQueue prompts = new();
    AgentToolRegistry tools = default!;
    IAgentSession session = default!;
    readonly string[] suggestions = ["Show me the sales dashboard", "Book a trip"];

    protected override void OnInitialized()
    {
        tools = new AgentToolRegistry().AddAll(prompts);
        session = AgentSession.Create("thread-1", myTransport.SendAsync);
    }

    void OnState(System.Text.Json.Nodes.JsonNode? state) { /* bind a dashboard to it */ }
}
```
- Templates are `RenderFragment<T>`:
  - `UserMessageTemplate` / `AssistantMessageTemplate`: `AgentMessageItem`.
  - `ToolCallTemplates`: `IReadOnlyDictionary<string, RenderFragment<AgentToolCallItem>>`.
  - `ActivityTemplates`: `IReadOnlyDictionary<string, RenderFragment<AgentActivityItem>>`.
  - `InterruptTemplate`: `AgentInterruptContext`, with `Requests` (each `AgentInterruptRequest` has `Kind`, `Approve()` / `Reject()` / `Choose(ids)` / `Reply(text)`) and `SubmitAsync()`.
  - `ErrorTemplate`: `AgentErrorItem`.
  - `SubagentTemplate`: `AgentSubagent`.
- Methods (via `@ref`): `SendAsync`, `Cancel`, `Clear`, `RetryAsync`.

## Interrupt shapes (both hosts, `AgentInterruptKind`)
- **`Choices`**: `metadata.choices` (`[{id,label}]`, `allowMultiple`). Resumes with `{"choiceIds":[...]}`. AiConversation `ask_user` questions arrive this way.
- **`Text`**: `responseSchema` has a string `text` property. Resumes with `{"text":"..."}`.
- **`Approval`**: anything else. Approve resumes with `{"approved":true}`; Reject resumes with `ResumeStatus.Cancelled`.

## Demo without an API key
`samples/Sample/Features/Agents/DemoAgentSession.cs` is a scripted AG-UI agent. Copy the pattern for previews and UI tests.

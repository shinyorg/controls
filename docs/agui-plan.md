# Agent UI — AG-UI + A2UI

A native agent surface for MAUI and Blazor: an `AgentView` that renders an AG-UI event stream
(streamed text, reasoning, tool calls, activity, sub-agents, approvals), and an `A2UISurface` that
renders agent-described UI (A2UI v1.0) with real Shiny controls.

**Status:** Phases 0–5 built. The engine, both hosts, the Shiny catalog and the AiConversation bridge are in; see each phase's notes.
**Hosts:** MAUI and Blazor, at parity. The engine is shared, and only the renderers differ per host.

## Context

### The protocol stack

| Protocol | Connects | Our role |
|---|---|---|
| **MCP** | agent ↔ tools/context | None here (Shiny.Net.HttpServer / EF Core MCP already cover servers) |
| **A2A** | agent ↔ agent | **None.** Server-side plumbing. AG-UI clients front for A2A and MCP agents via handshakes, so supporting AG-UI properly covers A2A agents for free |
| **AG-UI** | agent ↔ user-facing app | **Consume.** The event stream `AgentView` renders |
| **A2UI** (and MCP-UI, Open JSON UI) | agent-described UI carried over AG-UI/A2A/MCP | **Render.** A2UI first; MCP-UI deferred (see [Out of scope](#what-this-is-not)) |

### AG-UI in one table

Run input is a POST (`RunAgentInput`: `threadId`, `runId`, `messages`, `tools`, `state`, `context`,
`forwardedProps`, `resume`). The run streams back over SSE (default) or length-prefixed protobuf.

| Family | Events | What `AgentView` does with it |
|---|---|---|
| Lifecycle | `RunStarted` / `RunFinished` (`outcome`: `success` or `interrupt` + `interrupts[]`) / `RunError`, `StepStarted` / `StepFinished` | Busy state, stop button, step breadcrumb, error row, interrupt prompts |
| Text | `TextMessageStart` / `Content` / `End`, `TextMessageChunk` | Streaming markdown bubble |
| Tool calls | `ToolCallStart` / `Args` / `End` / `Result`, `ToolCallChunk` | Tool-call card; runs **frontend tools** locally |
| State | `StateSnapshot`, `StateDelta` (JSON Patch), `MessagesSnapshot` | Shared state store bindable from the app |
| Activity | `ActivitySnapshot`, `ActivityDelta` (JSON Patch) — keyed by `activityType` | Activity widget in the message flow (A2UI rides here) |
| Reasoning | `ReasoningStart` / `End`, `ReasoningMessage*`, `ReasoningEncryptedValue` | Collapsible "thinking" block; encrypted values round-tripped, never shown |
| Sub-agents | `SubagentStarted` / `Finished` / `Error` | Nested, attributed groups |
| Special | `Custom`, `Raw` | App hook; not rendered by default |

### A2UI v1.0 in one table

Server → renderer: `createSurface` (`surfaceId`, `catalogId`, `sendDataModel`, `components`,
`dataModel`), `updateComponents` (flat adjacency list, `root` required), `updateDataModel`
(JSON Pointer upsert, `null` removes), `deleteSurface`, `callRendererFunction`,
`agentFunctionResponse`.
Renderer → server: `action` (name, source component, resolved `context`, optional data model),
`callAgentFunction`, `rendererFunctionResponse`, `error` (`VALIDATION_FAILED`, `UNALLOWED_PARENT`, …).

Basic catalog: Text, Image, Icon, Video, AudioPlayer, Row, Column, List, Card, Tabs, Divider,
Modal, Button, CheckBox, TextField, DateTimeInput, ChoicePicker, Slider. Plus functions
(`required`, `regex`, `length`, `email`, `formatString/Number/Currency/Date`, `pluralize`,
`openUrl`, `and/or/not`, `@index`).

Bindings are `{"@path": "/pointer"}`, relative inside list templates. Inputs write to the local
model straight away, and the model goes back to the agent only with an action. **It is declarative
JSON only, never executable code, and agents can only place components the renderer registered.**
That is the security model we inherit and must enforce.

### What .NET already ships

The official AG-UI .NET SDK (ag-ui-protocol, 1.0.1, Oct 2026, all marked `IsTrimmable`, with an
STJ source-gen context):

| Package | What it gives us |
|---|---|
| `AGUI.Abstractions` | Every event type (`BaseEvent` hierarchy), `RunAgentInput`, messages, tools, `AGUIInterrupt` / `AGUIResume`, capabilities, `AGUIJsonSerializerContext`. Depends only on `Microsoft.Extensions.AI.Abstractions` |
| `AGUI.Client` | `AGUIChatClient : IChatClient`, `IAGUITransport.SendAsync(RunAgentInput) → IAsyncEnumerable<BaseEvent>`, `ReadAGUIEventStreamAsync` |
| `AGUI.Server` | `IAsyncEnumerable<ChatResponseUpdate>.AsAGUIEventStreamAsync(ctx)` — any `IChatClient` becomes an AG-UI run; `AGUIStreamOptions.MapResult` / `MapResultAsStateSnapshot` / `MapInterrupt` / `MapContent` |
| `AGUI.Formatting` | `SseEventStreamFormatter` read/write |

### Phase 0 findings (spike: `spikes/AgUiSpike`, report: `spikes/AgUiSpike/fixtures/REPORT.md`)

A scripted `IChatClient` (+ `FunctionInvokingChatClient`) hosted with `AGUI.Server`, recorded over
real HTTP/SSE, then read back two ways: raw events, and through `AGUIChatClient`.

| Scenario | Wire events | Through `AGUIChatClient` |
|---|---|---|
| text | `RUN_STARTED` → `TEXT_MESSAGE_START/CONTENT…/END` → `RUN_FINISHED` | `TextContent` per delta |
| reasoning | `REASONING_START` → `REASONING_MESSAGE_*` + `REASONING_ENCRYPTED_VALUE` → `REASONING_END` → text | `TextReasoningContent` (incl. protected data) |
| frontend tool | `TOOL_CALL_START/ARGS/END`, run ends — no result | `FunctionCallContent` |
| backend tool → state | `TOOL_CALL_*` → `TOOL_CALL_RESULT` → `STATE_SNAPSHOT` → text | call + result content; state as an empty update |
| backend tool → A2UI | `TOOL_CALL_RESULT` → `ACTIVITY_SNAPSHOT` (`activityType: a2ui`) → text | activity as an empty update |
| interrupt | text → `RUN_FINISHED { outcome: interrupt, interrupts[] }` | `InterruptRequestContent` |
| error | `RUN_ERROR` | `ErrorContent` |

Conclusions:

1. **Nothing is lost through `AGUIChatClient`.** Every update carries the original `BaseEvent` in
   `RawRepresentation`, including the events with no MEAI equivalent (state, activity, step,
   reasoning start/end). So AiConversation can keep `AGUIChatClient` as its `IChatClient` **and**
   feed `AgentView` the raw events. No custom SSE reader is needed.
2. **The `IChatClient` → AG-UI host path covers everything we render**: state comes from
   `MapResultAsStateSnapshot`, A2UI from `MapResult` → `ActivitySnapshotEvent`, interrupts from
   `InterruptRequestContent`. The sample's agent host needs nothing beyond `AGUI.Server`.
3. **Use the SDK's event types instead of hand-rolling them.** `AgentUI.Shared` takes a dependency
   on `AGUI.Abstractions` (and transitively MEAI.Abstractions, already in
   `Directory.Packages.props`). That gives up "dependency-free", but in return there is one
   canonical set of event types that can't drift from the spec. Verify an AOT publish in Phase 1.
4. Server quirk: `AGUI.Server` echoes the whole `ChatResponseUpdate` into `rawEvent` on text events.
   That's harmless to us but inflates the stream; tell AiConversation's hosting to strip it.
5. The recorded `.sse` files become the reducer's golden fixtures and the sample's replay mode.

### What this is NOT

- **Not an A2A implementation.** No agent cards, no task protocol.
- **Not an agent runtime or LLM client.** No prompts, no model calls.
- **Not a transport.** This repo does not open HTTP connections to agents. See
  [Package placement](#package-placement).
- **Not MCP-UI / arbitrary HTML.** That needs a sandboxed iframe, which is easy on Blazor but
  means a WebView per surface on MAUI. Deferred until someone asks.
- **Not a replacement for `ChatView`.** `ChatView` stays the person-to-person chat control
  (read receipts, invites, typing, reactions). `AgentView` sits beside it.

## Guiding principle

**The AG-UI event is the contract.** The controls never see an `IChatClient`, an `HttpClient` or a
model. They take an `IAsyncEnumerable<BaseEvent>` (`AGUI.Abstractions`) per run and emit run input back. Whoever produces
the events (Shiny.AiConversation, the MS client, a test fixture, a Blazor Server hub) is somebody
else's concern. That keeps this repo protocol-shaped rather than vendor-shaped, and lets
AiConversation evolve its transport without touching a control.

**Our controls are the catalog.** A2UI's basic catalog is the floor. The reason to do this here and
not in a generic renderer is the **Shiny catalog**: DataGrid, Gauge, Gantt, RangePickers, ChipGroup,
Kanban, Timeline and the rest, described to the agent as A2UI components and rendered natively on
both hosts.

## Package placement

| Package | Contents | Depends on |
|---|---|---|
| `Shiny.Controls.AgentUI.Shared` | Reducer over the SDK's AG-UI events, JSON Patch, A2UI document model, binding/function evaluator, catalog registry, frontend-tool registry. System.Text.Json source-gen, AOT/trim-clean | `AGUI.Abstractions` |
| `Shiny.Maui.Controls.Agents` | `AgentView`, `A2UISurface`, basic + Shiny catalog renderers for MAUI | core, `.Markdown`, `AgentUI.Shared` |
| `Shiny.Blazor.Controls.Agents` | Same for Blazor | core, `.Markdown`, `AgentUI.Shared` |

Why an add-on and not core: the message bubbles need the Markdown add-on (Markdig), and core must
not take that dependency. Catalog components that live in *other* add-ons (Diagram, Mermaid,
Office, FloorPlan) register from those add-ons through the catalog registry, so `.Agents` never
references them.

**Transport lives in Shiny.AiConversation**, as an `IAgentSession` over the SDK's `AGUIChatClient`
/ `IAGUITransport` (Phase 0 showed nothing is lost). The controls depend only on
`AGUI.Abstractions` for the event types, never on `AGUI.Client` or AiConversation.

## The contract (as built in Phase 1)

```csharp
// Shiny.Controls.AgentUI.Shared
public interface IAgentSession
{
    string ThreadId { get; }

    // One call = one AG-UI run. Same shape as the SDK's IAGUITransport.SendAsync, so
    // AgentSession.Create(threadId, transport.SendAsync) wraps the SDK client in one line.
    IAsyncEnumerable<BaseEvent> RunAsync(RunAgentInput input, CancellationToken ct = default);
}

public interface IAgentHistorySession : IAgentSession   // optional: restore a thread
{
    Task<IList<AGUIMessage>?> LoadAsync(CancellationToken ct = default);
}
```

The session takes the SDK's own `RunAgentInput`, not a wrapper type. A separate request record
added nothing. `AgentRunner` fills the input with the whole conversation
(`AgentThread.ToAGUIMessages()`), the frontend tools, the context, the shared state, any resume, and
forwarded properties contributed through its `RunPreparing` event. A session that keeps its own
history (AiConversation) may replace `Messages` with it.

Events are the SDK's `BaseEvent` hierarchy. The reducer ignores event types it doesn't know, so
new spec events never throw.

### What Phase 1 built

| Type | Role |
|---|---|
| `AgentThread` | The reducer. `Apply(BaseEvent)` folds events into `Items` (`AgentMessageItem`, `AgentReasoningItem`, `AgentToolCallItem`, `AgentActivityItem`, `AgentErrorItem`), `State`, `Steps`, `Subagents`, `RunStatus`, `PendingInterrupts`. Raises `Changed` with the item and its index. `ToAGUIMessages()` goes back the other way. |
| `AgentRunner` | One turn: add the user message → run → apply events → run frontend tools → follow-up run (capped by `MaxToolRounds`) → stop on interrupt. `ResumeAsync` / `ResumeAllAsync`, `RunAsync(forwardedProps)` for UI actions, `Cancel`. UI-affine: it never leaves its caller's synchronization context, so hosts need no marshalling. Transport failures are recorded on the thread, not thrown. |
| `AgentTool` / `AgentToolRegistry` | Frontend tools; typed `Create<TArgs,TResult>` takes source-generated `JsonTypeInfo` (AOT-safe). A tool failure goes back with `error` set. |
| `JsonPointer` / `JsonPatch` | RFC 6901 / 6902 over `JsonNode`; atomic patch, `replace` keeps member order. |
| `A2UISurfaceHost` | Applies agent messages (envelope and version checks, `INTEGRITY_ERROR` on reused ids, no `theme`), answers `callRendererFunction` (enforcing `allowedCallers`), resolves `agentFunctionResponse`, and raises `Outbound` for actions, errors and function messages. `GetRendererCapabilities()` / `GetRendererDataModel()`. |
| `A2UISurface` | Components (buffered until `root`), data model, and everything a renderer draws from them: `Evaluate*`, `Write` (two-way binding, creates missing paths), `GetChildren` (static lists and templates, with item scopes), `EvaluateChecks` (spec coercion), `GetAccessibility`, `DispatchAction`. Validates required properties and `allowedParents`/`allowedChildren`, reporting each error once. Agent-side functions share one in-flight call; actions always send a new one. |
| `A2UIScope` | Collection scope (relative paths, innermost `@index`), user activation (gates `openUrl`), and dependency tracking (`Tracking(set)` records every path read so a renderer re-evaluates only what a data change touches). |
| `A2UICatalog` / `A2UICatalogRegistry` | Catalogs parsed from the catalog JSON schema itself, so what the agent reads and what the renderer enforces are one document. Child-reference properties are found from the schema (`Child`, `ChildList`, `tabs[].child`), so custom catalogs compose without code. `CreateBasic()` embeds the pinned v1.0 basic catalog with every function implemented. |
| Basic functions | `required`, `regex`, `length`, `numeric`, `email`, `formatString` (compiled `${...}` interpolation, nested calls in the enclosing catalog), `formatNumber` / `formatCurrency` (round half away from zero like `Intl`, ISO 4217 minor units), `formatDate` (Unicode TR35 patterns, ISO strings or epoch ms), `pluralize` (CLDR cardinal rules for ~40 languages), `openUrl`, `and` / `or` / `not` (short-circuit). |
| `A2UIRegex` | The A2UI regex dialect: parsed to an AST, dialect errors and all four safety rules checked statically, then translated to .NET with JavaScript semantics (`^`/`$` only at the ends, ASCII `\d \w \b`, ECMAScript `\s`, astral code points whole), plus a 100 ms timeout as a backstop. |
| `A2UIUrlPolicy` | The mandatory media URL rules (scheme allowlist, `scheme://host`, no protocol-relative paths, tab/CR/LF stripped as browsers do, prefix-only inspection) and `openUrl` resolution. |
| `A2UIAgentBinding` | A2UI over AG-UI (below). |

### A2UI over AG-UI: the convention we follow

The A2UI v1.0 spec names AG-UI as a binding but doesn't define its wire shape. We follow the AG-UI
a2ui-middleware convention, which is in use by other renderers:

- **Inbound:** `ACTIVITY_SNAPSHOT` with `activityType: "a2ui-surface"` (configurable) and the
  messages in `content.a2ui_operations`; `replace: true` rebuilds per message id. We apply only the
  new tail when a snapshot extends the previous one, so a re-sent snapshot doesn't wipe user input.
- **Outbound:** an action starts a run with no new message and
  `forwardedProps.a2uiAction.userAction`. Every run carries `forwardedProps.a2uiRendererCapabilities`,
  plus `a2uiRendererDataModel` when a surface set `sendDataModel`.
- **Our own addition:** errors, `callAgentFunction` and `rendererFunctionResponse` ride in
  `forwardedProps.a2uiClientMessages` (an array of full renderer-to-agent envelopes). The middleware
  has no slot for these yet. Function messages trigger a run immediately; errors wait for the next one.

Revisit when A2UI publishes a normative AG-UI binding.

### Interop finding: what goes back in the history

The official .NET server (`AGUI.Server` 1.0.1, `ToChatRequestContext`) throws `Unknown chat role` for
`activity` and `reasoning` messages in `RunAgentInput.messages`. Found end to end against the spike's
`serve` mode. `AgentRunner` therefore leaves both out of the history by default.
`SendActivityMessages` and `SendReasoningMessages` turn them back on for agents that accept them; the
TypeScript and Python SDKs model both roles. A surface's state still reaches the agent: its data model
travels with each action and in `a2uiRendererDataModel`.

### Phase 5 as built (Shiny.AiConversation, `~/Desktop/dev/shinyorg/speech`)

New package `Shiny.AiConversation.AgUi` (depends on `AGUI.Abstractions` + `AGUI.Client`, **not** on
this repo):

- `IAiSession.RunAgUiAsync(RunAgentInput, ct)` runs a session as an AG-UI run. Its shape matches
  `IAgentSession.RunAsync`, so the one-line setup is
  `AgentSession.Create(session.SessionId, session.RunAgUiAsync)`.
  - The session keeps its own history, so each run reads only its newest input: the last user
    message, `forwardedProps.a2uiAction`, or `resume`.
  - Output maps as follows:
    - text → text messages;
    - tool calls → tool events with results;
    - `ask_user` → a `RUN_FINISHED` interrupt, one per question, with `metadata.choices`. Resuming
      with `{choiceIds}` or `{text}` becomes `AiInput.Answer`;
    - interrupted → `cancelled`;
    - failed → `RUN_ERROR`.
- `AgUiRenderTool` / `opts.AddAgUiRenderTool()`: a silent `render_ui` tool that lets a real model
  draw A2UI surfaces (Shiny catalog by default). Each call becomes an `a2ui-surface` activity.
- `opts.AddAgUiChatClient(uri)`: a remote AG-UI agent as the session's `IChatClient` (`AGUIChatClient`).
- 7 TUnit tests (149 total pass). README, skill (`ag-ui.md`), docs page `aiconversation/ag-ui` and the
  sidebar node are written, and the release note is under Speech 3.1.

**AiChatView decision:** it stays, feature-frozen, as the voice-first `ChatView` screen. AgentView is
the path for agent UIs, which the docs say on both sides. Retiring it is revisited once AgentView has
voice input through SpeechAddins.

**Not done:** the AiConversation sample apps don't host AgentView yet, because they consume this repo
from NuGet and `*.Agents` isn't published. Add the one-liner to `samples/MauiSample` /
`samples/BlazorSample` after the first release.

### Known gaps (by design, for now)

- Component and argument validation checks presence (required properties, required and unknown
  arguments), not full JSON Schema types. Unknown components still store and render as placeholders.
- `pluralize` covers the common CLDR families, not every locale's edge rules (e.g. French "many").
- The AG-UI protobuf transport isn't ours: it's the session's job.

## Host API contract (Phases 2–4, both hosts)

Both hosts expose the same names. The engine type `A2UISurface` (one surface's model) keeps its
name, so the control is **`A2UISurfaceView`**, which avoids an ambiguous type in any file that
imports both namespaces.

**Added to the engine for the hosts:**
- `A2UICatalog.CreateShiny()` / `ShinyCatalogId`: the Shiny catalog (basic plus 12 Shiny
  components) as one self-contained schema.
- `AgentPromptQueue` / `AgentPrompt` / `AgentUiTools` / `AgentPromptKinds`: the built-in UI tools.
  A tool call waits on a prompt, and the host draws it inline under the tool card.
- Markdown add-on, both hosts: incremental rendering that rebuilds only changed top-level blocks.
  `IsScrollEnabled=false` (MAUI) now has no scroller at all.

| `A2UISurfaceView` | MAUI (`ContentView`, bindable) / Blazor (parameter) |
|---|---|
| `Surface` | The engine `A2UISurface` to draw |
| `Host` + `SurfaceId` | Alternative to `Surface`. A null `SurfaceId` draws every surface on the host in creation order |
| `Renderers` | `A2UIRendererRegistry`: component type → renderer. Defaults to basic + Shiny; apps and add-ons register their own |
| `IsScrollEnabled` | True when standalone (the view owns vertical scroll). `AgentView` sets it false, so the view sizes to its content |
| `ShowBorder` | Whether the outermost Card draws its edge (`AgentView` turns it off inside a message) |

| `AgentView` | MAUI / Blazor |
|---|---|
| `Session` (`IAgentSession`) or `Runner` (`AgentRunner`) | Session creates a runner; Runner lets the app own it |
| `Tools` (`AgentToolRegistry`), `Prompts` (`AgentPromptQueue`) | Frontend tools; the view always draws `Prompts.Pending` inline |
| `Catalogs` (`A2UICatalogRegistry`) | Defaults to basic + Shiny |
| `ShowReasoning` (`AgentReasoningDisplay` Hidden / Collapsed / Expanded), `ShowSteps`, `PlaceholderText`, `Suggestions` | |
| Templates | MAUI `DataTemplate` / Blazor `RenderFragment<T>`: `UserMessageTemplate`, `AssistantMessageTemplate`, `ToolCallTemplates[name]`, `ActivityTemplates[type]`, `InterruptTemplate`, `ErrorTemplate` |
| `State`, `StateChanged` | The thread's shared state (`JsonNode`) |
| `SendAsync(text)`, `Cancel()`, `Clear()` | |

## Surface 1 — `AgentView`

```xml
<agents:AgentView Session="{Binding Session}"
                  FrontendTools="{Binding Tools}"
                  ShowReasoning="Collapsed"
                  PlaceholderText="Ask anything…" />
```

```razor
<AgentView Session="@session" FrontendTools="@tools" ShowReasoning="ReasoningDisplay.Collapsed" />
```

### What it renders

| Item | Default look | Override |
|---|---|---|
| User message | Right-aligned bubble (shares `ChatView` bubble styling tokens) | `UserMessageTemplate` |
| Assistant message | Full-width markdown, token-streamed, with a caret while open | `AssistantMessageTemplate` |
| Reasoning | Collapsed "Thought for 4s" row, expands to dimmed text | `ShowReasoning` = Hidden / Collapsed / Expanded |
| Tool call | Compact card: name, live args, spinner then result; expands to JSON | `ToolCallTemplates[name]` — per-tool custom renderer (the "generative UI via tool renderers" pattern) |
| Activity | Looked up by `activityType` in `ActivityRenderers`; `a2ui` is pre-registered to `A2UISurface` | `ActivityRenderers[type]` |
| Sub-agent | Indented group with name + status, collapsible | `SubagentTemplate` |
| Step | Breadcrumb in the header while running | `ShowSteps` |
| Interrupt | Inline approval card (approve / reject / edit) built from the interrupt payload | `InterruptTemplate` |
| Error | Inline error row with Retry | `ErrorTemplate` |

Composer: reuses `ChatEntryView` (MAUI) / the `ChatView` composer markup (Blazor) — attachments,
input actions and SpeechAddins tools work unchanged. Send turns into Stop while a run is live (cancels
the token, which ends the stream).

Scroll: stick-to-bottom while streaming unless the user scrolled up; "jump to latest" pill. Reuse the
`ChatView.Scroll` logic and its iOS keyboard handling instead of forking them.

### Frontend tools

The human-in-the-loop showcase: the agent asks, a real Shiny control answers.

```csharp
tools.Add(AgentTool.Create<DateRangeArgs, DateRange>(
    "pick_date_range",
    "Ask the user to choose a date range",
    async (args, ctx) => await ctx.Ui.ShowRangePickerAsync(args.Min, args.Max)));   // FloatingPanel + RangePicker
```

- Definitions go out in `RunAgentInput.tools` with JSON Schema generated from the args type
  (source-gen, AOT-safe — reuse whatever Shiny.AiConversation uses for its tool schemas).
- On `ToolCallEnd` for a frontend tool, `AgentView` runs it, appends a `role: tool` message (with
  `error` set on failure, which the spec requires so a failure isn't read as success) and starts the
  follow-up run automatically.
- Built-in, opt-in tool pack (`AgentTools.Ui`): confirm, choose-one, choose-many (ChipGroup), date /
  date range / time range (RangePickers), duration (DurationPicker), text prompt, capture photo
  (Camera add-on registers it). Each is one line to add.

### Interrupts

`RunFinished { outcome: interrupt }` → `AgentView` shows each interrupt with `InterruptTemplate`
(default: approve / reject with optional note), collects responses, and starts the next run with
`resume`. Same path for sub-agent `suspended`.

### Shared state

`AgentView.State` exposes the reducer's state as an observable `JsonNode` with typed helpers
(`State.Get<T>("/cart/items")`, `State.Changed`). Apps bind dashboards next to the chat to it,
which is the AG-UI "shared state" demo. Writes from the app go out as `state` on the next run.

## Surface 2 — `A2UISurface`

Usable on its own (any page, any transport) or hosted by `AgentView` as the `a2ui` activity
renderer.

```csharp
surface.Apply(a2uiMessage);                 // createSurface / updateComponents / updateDataModel / …
surface.ActionRaised += (s, a) => …;        // A2UI action → caller sends it to the agent
```

### Renderer rules (from the spec — conformance checklist)

- Buffer until `root` exists; placeholders for missing child references; render progressively.
- Apply messages in order per surface; reject `surfaceId` reuse without delete (`INTEGRITY_ERROR`).
- Resolve catalog per component (`@catalogId` → surface default → error). **No fallback** — an
  unknown component renders an "unsupported component" placeholder and reports
  `VALIDATION_FAILED`, never a guess.
- Enforce `allowedParents` / `allowedChildren`, `allowedCallers` and `requiresUserActivation`
  (`openUrl` only from a tap). Regex limits per spec (no backrefs/lookbehind, 256-char pattern,
  4096-char input) — implement on `RegexOptions.NonBacktracking` with a pre-validator.
- Answer every `callRendererFunction`.
- Map `@accessibility` to `SemanticProperties` (MAUI) / ARIA (Blazor); explicit labels win.
- Inputs write to the local model immediately; data model goes out only with an action and only
  when `sendDataModel` is true.

### Catalogs

| Catalog | Id | Contents |
|---|---|---|
| Basic | A2UI basic catalog id | The 18 spec components → native MAUI views / plain HTML+CSS on Blazor, themed through the existing style tokens |
| **Shiny** | `shiny.controls/v1` (published JSON schema) | DataGrid, Gauge, Gantt, Timeline, Kanban, ChipGroup, TagEntry, RangePickers (date / time / date-time range), DurationPicker, Rating-style Slider/RangeSlider, ProgressBar/ProgressLine, PillView, Avatar, Badge, Markdown, ImageViewer, CarouselGallery, Expander |
| Add-on | `shiny.controls.<addon>/v1` | Registered by the add-on: MermaidDiagrams, Diagram, Barcodes, MediaElement, FloorPlan (read-only) |

- Registry: `catalog.Register("Gauge", GaugeDescriptor, GaugeRenderer)`; the descriptor emits the
  catalog JSON so the agent's catalog and our renderers can't drift. `supportedCatalogIds` is
  derived from the registry and sent in run context.
- App catalogs: apps register their own components the same way — that's the "locked catalog" model.
- Every Shiny catalog entry gets a JSON fixture and a rendered test on both hosts.

### Layout and the nitpick checklist

- An A2UI surface inside `AgentView` **sizes to its content** — it never scrolls on the vertical
  axis. A2UI `List` inside a message renders non-virtualised and content-sized; `DataGrid` in a
  message gets a max height with its own scroll **only** on the horizontal axis, or is shown as a
  preview with "Open" into a FloatingPanel/modal where it fills and scrolls.
- A standalone `A2UISurface` filling a page owns vertical scroll; the page must not wrap it in a
  `ScrollView` / `overflow:auto`.
- A2UI `Card` hosted in a message drops the message container's chrome (no card-in-bubble double
  border, no stacked radii). Every renderer that draws an edge takes `ShowBorder`.

## Platform notes

| Host | Notes |
|---|---|
| MAUI iOS / Catalyst / Android / Windows / macOS / GTK | Pure MAUI views, no platform code expected beyond what `ChatView` already has |
| Blazor WASM / Server / Hybrid | Pure Razor. Server: the event stream is consumed server-side and the circuit pushes renders — throttle `StateHasChanged` during token streaming (frame-coalesce, ~30 fps) |
| All | Throttle text re-render on both hosts — append deltas to a buffer and flush on a tick, never re-parse markdown per token. Markdown add-on needs an incremental/"streaming tail" mode (re-parse only the last block); that is a prerequisite change to `.Markdown` |

## Phasing

| Phase | Deliverable | Exit criteria |
|---|---|---|
| **0 — Spike** ✅ done | `spikes/AgUiSpike`: `IChatClient` hosted with `AGUI.Server`, 7 scenarios recorded to `.sse`, compared raw vs `AGUIChatClient` | See [Phase 0 findings](#phase-0-findings-spike-spikesaguispike-report-spikesaguispikefixturesreportmd). The A2UI v1.0 basic catalog is pinned in Phase 1 |
| **1 — Engine** ✅ done | `Shiny.Controls.AgentUI.Shared` + `tests/Shiny.Controls.AgentUI.Tests` (219 tests: the recorded AG-UI streams, the 43 official A2UI basic-catalog examples resolving with zero errors, regex accept/reject/semantics, locale formatting, host integrity, the AG-UI binding). Native AOT publish (`spikes/AgentUIAot`) has zero trim/AOT warnings, SDK included | See [What Phase 1 built](#what-phase-1-built) |
| **2 — A2UISurface, basic catalog** ✅ done | Both hosts, standalone, all 18 basic components + functions + validation | Spec sample surfaces render identically on MAUI and Blazor; conformance checklist ticked |
| **3 — AgentView** ✅ done | Messages, streaming markdown, reasoning, tool cards, activity (A2UI hosted), sub-agents, interrupts, stop, frontend tools + `AgentTools.Ui` pack, shared state | Sample talks to a real AG-UI endpoint end-to-end on both hosts |
| **4 — Shiny catalog** ✅ done | `shiny.controls/v1` schema + renderers; add-on catalogs | Agent in the sample builds a dashboard (DataGrid + Gauge + RangePicker) from a prompt |
| **5 — AiConversation integration** (other repo) ✅ done, see [Phase 5 as built](#phase-5-as-built-shinyaiconversation-shinyorgspeech) | `IAgentSession` over AG-UI in Shiny.AiConversation; its `AiChatView` story reconciled with `AgentView` | One-line setup in the sample |

Phases 2 and 3 can run in parallel once Phase 1 lands.

## Sample

`samples/Sample/Features/Agents/`:

- **AgentView** against a local AG-UI endpoint hosted in the sample's server (MS AG-UI hosting over
  any `IChatClient`), with a canned/replay mode that plays recorded event streams so the page works
  with no API key — also what the screenshot and UI tests use.
- **A2UI gallery**: every catalog component from fixtures, no agent needed.
- **Frontend tools**: agent asks for a date range → RangePicker in a FloatingPanel.
- **Shared state**: chat on one side, a bound Gauge/DataGrid dashboard on the other.

## Testing

- Reducer: golden tests over recorded event streams (including chunk forms, out-of-order tool
  results, interrupt/resume, sub-agent nesting, unknown events).
- A2UI: spec examples + our fixtures → model assertions; evaluator tests for every function and
  regex limit; malicious-input tests (unknown components, disallowed parents, `openUrl` without
  gesture, oversized regex, deep nesting, huge lists).
- Renderers: snapshot tests per catalog entry on both hosts.

## Decisions

1. **Package names:** `Shiny.Maui.Controls.Agents`, `Shiny.Blazor.Controls.Agents`,
   `Shiny.Controls.AgentUI.Shared`.
2. **AiConversation's `AiChatView`:** frozen — no new features. Whether it becomes a thin wrapper
   over `AgentView` or is retired is decided in Phase 5.
3. **Shiny catalog id:** resolvable, hosted on the docs site
   (`https://shinylib.net/a2ui/shiny.controls/v1.json`), generated from the catalog registry at
   build time so the published schema can't drift from the renderers. Add-on catalogs follow the
   same pattern (`shiny.controls.<addon>/v1.json`).
4. **Phase 0 scope:** client spike **plus recorded event streams** from a real endpoint, checked in
   as test fixtures and reused as the sample's no-API-key replay mode. No live sample server yet.
5. **Tool-call templates vs A2UI:** keep both. Tool renderers are the simple path for app authors;
   A2UI is for agent-composed UI. The docs say when to pick which.

## Open questions

1. **Theming** — A2UI v1.0 forbids `theme` on `createSurface`, so surfaces inherit the host's
   Shiny theme packs. Verify in Phase 2 that the Aurora/Material/Ocean/Terminal packs cover every
   basic-catalog renderer.
2. **Protobuf transport** — out of scope for the controls (transport isn't ours); note it for
   AiConversation.

## Where it stands (after Phases 2–5 and the follow-up round)

- **Hosts:** `src/Shiny.Maui.Controls.Agents` and `src/Shiny.Blazor.Controls.Agents` render all 18 basic and all 12
  Shiny components and every AgentView item kind. Both add-ons (and `Shiny.Maui.Controls.Markdown`) target core's
  frameworks, so a platform head gets core's platform assembly.
- **Follow-up round:**
  - MAUI composer is a growing `Editor` with `SendOnEnter`.
  - `SubagentTemplate` is on both hosts.
  - Fixed the Blazor multi-choice interrupt chips showing the record instead of the label.
  - The Mac Catalyst sample adopts the UIScene lifecycle, so it launches on macOS 27.
- **Samples:** the Agent View page on both hosts has four sources:
  - Demo: `DemoAgentSession`, which also covers the choice, multi-choice and text interrupts and the media card
    (Video, AudioPlayer, `openUrl`).
  - Recordings: `ReplayAgentSession` plays the spike's `.sse` fixtures.
  - Endpoint: `HttpAgentSession`.
  - Your key: `ChatClientAgentSession` runs `AGUI.Server` in-process over an OpenAI-compatible `IChatClient`,
    with a `render_ui` tool. It runs in the browser on the WebAssembly sample; a trimmed WASM app needs
    `<TrimmerRootAssembly Include="AGUI.Abstractions" />`.
- **Speech repo:** the BlazorSample has an Agent page over a real `IAiSession`. It references the controls Agents
  project only when a sibling checkout exists, until the package ships.
- **Tests:**
  - 263 engine tests, covering every demo scenario, the replay session and the chat-client session through
    `AGUI.Server`.
  - Playwright: `AgentViewTests`, `AgentViewPhoneTests` and `A2UIGalleryTests`, green in Chromium, WebKit,
    iPhone and the trimmed publish. The Your key tests run against a mock OpenAI server.
  - DevFlow: `AgentViewTests` and `A2UIGalleryTests`, green on the iOS simulator, the Android emulator and
    the macOS head. The Your key test reaches its mock through `adb reverse` on Android.
  - An opt-in live test (`AGUI_ENDPOINT`) runs against `spikes/AgUiSpike -- serve`.
- **Known gaps:**
  - MAUI can't see Shift, so a desktop composer with `SendOnEnter` gets new lines only by pasting.
  - A MAUI DataGrid in a message caps at 12 rows, with "Show all" opening a modal.
  - The core MAUI DataGrid can't read JSON rows, so the renderer uses cell templates.

## Docs & release obligations

New packages and new controls, so the full checklist in `CLAUDE.md` applies:

- `docs/controls/agent-view.md`, `docs/controls/a2ui-surface.md` + README index rows; README
  summary paragraph and NuGet badges for the three packages.
- Skill: `SKILLS/shiny-controls/agent-view.md`, `a2ui-surface.md`, referenced from `SKILL.md`.
- `Shiny.Controls.slnx`, `Build.slnf`, both issue-template package dropdowns, CLAUDE.md repo layout.
- Docs site: `controls/agent-view/` and `controls/a2ui/` folders, release notes, sidebar nodes,
  homepage "UI Controls" card (Flagship group).
- Jon Dick nitpick checklist on both hosts (see [Layout](#layout-and-the-nitpick-checklist)).
- Screenshots: `public/images/agent-view/` and `public/images/a2ui/` in the docs repo (MAUI on the Android
  emulator via DevFlow, Blazor via Playwright), shown on both pages and as `ControlsMarquee` tiles.

**Status:** all of the above are done.

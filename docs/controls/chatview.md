# ChatView

[← All Shiny Controls](../../README.md)

> **v1 beta** — the API may still change.

A modern, **provider-driven** chat UI control with message bubbles, typing indicators, cursor-based load-more paging, reactions, read receipts, a markdown composition toolbar, image attachments, and custom message templates. The control is *styles + layout only* — all data, lifecycle, permissions, and real-time behavior live behind an `IChatSessionProvider` you implement (the same integration pattern as the Scheduler control). You give the control a `Provider` and a `SessionId`; it resolves an `IChatSession`, subscribes to its events, and renders.

![ChatView](../../assets/chat1.png)

```xml
<shiny:ChatView Provider="{Binding Provider}"
                SessionId="{Binding SessionId}"
                MyBubbleColor="#DCF8C6"
                OtherBubbleColor="White"
                PlaceholderText="Type a message..." />
```

```csharp
public interface IChatSessionProvider
{
    Task<IChatSession> CreateSessionAsync(string[] userIds, CancellationToken ct = default);
    Task<IChatSession> GetSessionAsync(string sessionId, CancellationToken ct = default); // throws ChatSessionException
}

// IChatSession (IAsyncDisposable) exposes: Info, CurrentUserId, GetMessagesAsync (cursor paging),
// SendMessageAsync/ResendMessageAsync/EditMessageAsync/DeleteMessageAsync, ReactToMessageAsync,
// MarkReadAsync, ToggleTypingAsync, InviteUserAsync, LeaveAsync, RenameAsync, and live events
// (MessageReceived, MessageUpdated, MessageDeleted, UserTyping, UserJoined/Left, SessionUpdated, ConnectionStateChanged).
```

| Property | Type | Default | Description |
|---|---|---|---|
| Provider | IChatSessionProvider? | null | The integration provider |
| SessionId | string? | null | Session to resolve via `GetSessionAsync` |
| PageSize | int | 30 | Messages fetched per page |
| OpenImagesInViewer | bool | true | Tapping an image bubble opens the built-in ImageViewer |
| MyBubbleColor | Color | #DCF8C6 | Local user bubble color |
| MyTextColor | Color | Black | Local user text color |
| OtherBubbleColor | Color | White | Default other-user bubble color (overridden by user's BubbleColor) |
| OtherTextColor | Color | Black | Other-user text color |
| ChatBackgroundColor | Color? | null | Background color for the messages area |
| BubbleFontSize | double | 15 | Font size for bubble text (MAUI) |
| BubbleFontFamily | string? | null | Font family for bubble text (MAUI) |
| TimestampFontSize | double | 11 | Font size for timestamps (MAUI) |
| BubbleCornerRadius | double | 18 | Corner radius for bubbles (tail stays at 4) (MAUI) |
| PlaceholderText | string | "Type a message..." | Input placeholder |
| SendButtonText | string | "Send" | Send button label |
| InputBar | ChatEntryView | built-in | The hosted composer — assign your own to replace it, or read it to tweak (`InputBar.MaxLines = 3`) (MAUI only) |
| InputBarBackgroundColor | Color? | theme | Area behind the composer |
| InputBarBorderColor | Color? | theme | Outline of the rounded composer |
| MaxInputRows | int | 6 | How tall the composer grows before it scrolls (Blazor only) |
| InputTemplate | RenderFragment? | null | Replaces the built-in composer entirely (Blazor only) |
| InputLeftToolbar | RenderFragment? | null | Markup added to the left of the composer's control row (Blazor only) |
| InputRightToolbar | RenderFragment? | null | Markup added right of the control row, before send (Blazor only) |
| IsInputBarVisible | bool | true | Show/hide the input bar (set false for read-only chats) |
| ShowTypingIndicator | bool | true | Enable typing indicators |
| ScrollToFirstUnread | bool | false | Anchor initial scroll at the first unread instead of the end |
| InputActions | IList\<ChatInputAction\> | [] | Custom input-bar actions (MAUI only) |
| CustomBubbleActions | IList\<ChatBubbleAction\> | [] | Custom bubble actions appended to the permission-driven set (MAUI only) |
| MessageTemplate | DataTemplate? | null | Single template for all message content (MAUI only) |
| MessageTemplateSelector | DataTemplateSelector? | null | Per-type template selector (MAUI only) |
| UseFeedback | bool | true | Haptic feedback on interactions (MAUI only) |
| AdjustForKeyboard | bool | true | Keeps the composer above the on-screen keyboard: iOS pads the chat by the keyboard overlap; Android switches the window to `adjustResize` while the chat is on screen (the app's own mode comes back when it leaves) so the composer rises with the keyboard instead of the window panning it half under. Leave on inside a `FloatingPanel` — the panel's `ExpandOnInputFocus` raises the sheet, but only this padding lifts the composer clear of the keyboard once the panel is at its top detent. Set false only when something else already handles the overlap (MAUI only) |

**Methods (MAUI):** `ScrollToEnd(bool animate)`, `ScrollToMessage(string messageId, bool animate)`, `SubmitEntry()`, `EntryText` (get/set), `MessageTapped` event (non-image bubble taps).

## The composer — `ChatEntryView` (MAUI) / `ChatEntry` (Blazor)

The message composer is its own control, laid out as a single rounded card in the AI-chat idiom —
the formatting toolbar sits along the top, the **multiline** auto-growing entry spans the full width
beneath it, and every other control sits on a row below that:

```
┌────────────────────────────────────────────┐
│  B  I  U  S  </>  🔗                       │   ← formatting (only if permitted)
│  How can I help you today?                 │
│  +  [Chat]                Model  🎤   ↑    │   ← LeftToolbar … RightToolbar + send
└────────────────────────────────────────────┘
```

`ChatView` builds and hosts one automatically, so nothing changes for the common case — supply your
own only when you want a different shape, or use it standalone (an AI prompt box, a comment field)
and handle `SendRequested` yourself. It knows nothing about `IChatSessionProvider`: `ChatView`
remains the only thing that talks to the session, pushing state in (`SetBodyPermissions`,
`ShowAttachButton`, `SetInputEnabled`) and listening to events out.

`LeftToolbar` and `RightToolbar` are the slots on that control row — drop a mode picker, a model
label or a mic button into either side of the send button:

```xml
<shiny:ChatView Provider="{Binding Provider}" SessionId="{Binding SessionId}">
    <shiny:ChatView.InputBar>
        <shiny:ChatEntryView PlaceholderText="How can I help you today?"
                             SendButtonText="↑"
                             MaxLines="5">
            <shiny:ChatEntryView.LeftToolbar>
                <Border StrokeShape="RoundRectangle 14" Padding="10,4">
                    <Label Text="Chat" FontSize="13" />
                </Border>
            </shiny:ChatEntryView.LeftToolbar>
            <shiny:ChatEntryView.RightToolbar>
                <Label Text="Model" FontSize="13" VerticalOptions="Center" />
            </shiny:ChatEntryView.RightToolbar>
        </shiny:ChatEntryView>
    </shiny:ChatView.InputBar>
</shiny:ChatView>
```

`ChatEntryView` properties: `Text`, `PlaceholderText`, `MaxLines` (6), `FontSize`, `FontFamily`,
`SendButtonText`/`SendButtonBackgroundColor`/`SendButtonTextColor`, `BarBackgroundColor`,
`ComposerBackgroundColor`, `BorderColor`, `BorderThickness`, `CornerRadius` (24), `ShowAttachButton`,
`ShowActionsButton`, `LeftToolbar`/`RightToolbar` (`IList<IView>`). Events: `SendRequested`,
`AttachRequested`, `ActionsRequested`, `LinkRequested`, `EditCancelled`, `TextChanged`. Methods:
`Submit()`, `ClearText()`, `FocusInput()`, `SetInputEnabled(bool)`,
`EnterEditMode(string)`/`ExitEditMode()`, `SetBodyPermissions(...)`, `ApplyWrap(...)`,
`InsertLink(...)`.

Blazor's `ChatEntry` mirrors it as parameters — `@bind-Text`, `Placeholder`, `SendButtonText`,
`IsEnabled`, `ShowAttach`, `BodyPermissions`, `MaxRows` (6), `SendOnEnter` (true; Shift+Enter inserts
a newline), `LeftToolbar`/`RightToolbar` (`RenderFragment`), plus `OnSend`, `OnAttach` and
`OnTyping`. `ChatView` surfaces the two slots directly as `InputLeftToolbar` / `InputRightToolbar`,
or drop a whole `ChatEntry` into `ChatView.InputTemplate` to replace the built-in composer.

On MAUI the entry is an `Editor`, so **Enter inserts a newline** and sending is the button's job —
matching how AI chat composers behave. There is no hairline rule between the message list and the
composer; the rounded outline is the edge.

**Permissions:** every action affordance is derived from `ChatSessionPermissions` on `ChatSessionInfo` + ownership — `CanSendMessages`, `CanEditMessages`, `CanDeleteMessages`, `CanReactToMessages`, `CanInviteUsers`, `CanLeaveSession`, `CanChangeSessionName`, `CanSendImages`. `MessageBodyPermissions` drives the markdown composition toolbar (Bold/Italics/Underline/Strikethrough/Codeblocks/Links).

**Send results:** sends are optimistic. A transient failure → `MessageStatus.Failed` + retry (`ResendMessageAsync`); a provider rejection (`ChatSendRejectedException`) → `MessageStatus.Rejected` + reason, no retry. Validation (size, image count, content policy) lives in the provider, not the control.

**Custom actions (MAUI):** the old `ChatEntryTool`/`ChatBubbleTool` FAB tool tree is replaced by permission-driven built-in actions (react/edit/delete/copy) plus two lightweight hooks — `ChatInputAction` (input bar) and `ChatBubbleAction` (bubble menu). `SpeechToTextTool : ChatInputAction` and `TextToSpeechBubbleTool : ChatBubbleAction` ship in `Shiny.Maui.Controls.SpeechAddins`.

```xml
<shiny:ChatView Provider="{Binding Provider}" SessionId="{Binding SessionId}">
    <shiny:ChatView.InputActions>
        <speech:SpeechToTextTool AutoSend="False" SilenceTimeout="00:00:03" />
    </shiny:ChatView.InputActions>
    <shiny:ChatView.CustomBubbleActions>
        <speech:TextToSpeechBubbleTool />
    </shiny:ChatView.CustomBubbleActions>
</shiny:ChatView>
```

**Features:**
- Provider-driven: bind a `Provider` + `SessionId`, implement the rest server-side
- Chat bubbles with left/right alignment (by `CurrentUserId`) and per-user colors/avatars
- Visual grouping by sender and minute; timestamps on last message in each group
- Typing indicators with animated dots and a scroll-aware toast pill (debounced + auto-expiring)
- Opens at the newest message and follows the live edge: new messages scroll into view while you are at the bottom (including a photo or custom bubble that grows after it loads); once you scroll up into history they wait behind an "N New Messages" pill instead (tap it, or scroll back down, to clear it). On the macOS AppKit head the list raises no scroll events, so the chat can't tell you scrolled up there: older history doesn't page in on scroll and the pill doesn't appear. The list also stays on the newest message when the keyboard opens or the composer grows, and on AppKit a photo gets a fixed 250×166 box, because that list measures a row only once (the GTK head shares the build and gets the same box) `ScrollToFirstUnread` opens at the first unread message instead
- Reactions (emoji badges grouped by glyph), gated by `CanReactToMessages` + `PermittedEmojis`
- Per-user read receipts; per-message edit/delete gated by permission + ownership
- Optimistic send with `Sending`/`Failed`/`Rejected` states and retry
- Markdown composition toolbar + inline bubble rendering (self-contained, no Markdown-package dependency)
- Image attachments from gallery or camera (camera shown only where the platform supports capture); tap an image to open the ImageViewer. On MAUI the lightbox is drawn on the page's own overlay layer, not inside the chat, so it works on any `ContentPage` and covers the page rather than the conversation
- Cursor-based load-more paging (stable under live inserts); changing `SessionId` cancels an in-flight history page and discards its late result, so one conversation's history never leaks into the next
- Connection banner that disables input while offline/reconnecting
- Custom message templates via `Identifier`/`Metadata` discriminator
- Entire input bar can be hidden for read-only use

<!-- TODO: capture screenshots for chatview (provider, markdown toolbar, attachment picker) -->

## Dark mode

`MyBubbleColor`, `MyTextColor`, `OtherBubbleColor` and `OtherTextColor` default to theme tokens
(`primary-container` / `surface-container-high` and their `on-` pairs), so bubbles follow the app's
scheme. They previously defaulted to a fixed pale green and white, which stayed exactly as light in
dark mode. Set them to pin the classic messenger look — and set the matching text colour whenever you
set a bubble colour. See [Styling & theming](styling.md#dark-mode).

### Automation ids (MAUI)

Set `AutomationId` on the control and its parts are named after it, for UI tests (Appium, UI Automation, DevFlow). Without an id the parts get none. Bubble ids end in the message's `MessageId` rather than a row index, because loading older messages shifts every index. The attach, message-action and reaction menus are native action sheets and cannot be named.

| Part | Id |
|---|---|
| Built-in composer (a `ChatEntryView`) | `<id>_Composer` |
| Composer text editor / send / attach / overflow | `<id>_Composer_Entry` / `_Composer_Send` / `_Composer_Attach` / `_Composer_Actions` |
| Composer format buttons | `<id>_Composer_Format_Bold` (`_Italic`, `_Underline`, `_Strikethrough`, `_Code`, `_Link`) |
| Edit banner label / its cancel button | `<id>_Composer_EditBanner` / `<id>_Composer_EditCancel` |
| A message bubble | `<id>_Message_<MessageId>` |
| Its text / image / actions (⋮) / status | `<id>_MessageText_<MessageId>` / `_MessageImage_` / `_MessageActions_` / `_MessageStatus_` |
| "New messages" pill | `<id>_NewMessages` |
| Connection banner | `<id>_ConnectionBanner` |
| Load error text / retry button | `<id>_Error` / `<id>_Retry` |
| Reaction picker / each glyph | `<id>_ReactionPicker` / `<id>_Reaction_<index>` |
| Photo lightbox | `<id>_ImageViewer_Popup`, close `<id>_ImageViewer_Close` |

A standalone `ChatEntryView` names its parts after its own id the same way (`<id>_Entry`, `<id>_Send`, …); a composer you assign to `InputBar` keeps the id you give it.

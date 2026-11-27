# ChatView

A modern chat UI control with message bubbles, typing indicators, cursor-based load-more paging, reactions, read receipts, a markdown composition toolbar, and image attachments.

ChatView is **provider-driven**: the control is *styles + layout only*. All data, lifecycle, permissions, and real-time behavior live behind an `IChatSessionProvider` you implement (the same integration pattern as the Scheduler control). You do **not** bind a `Messages` collection or wire `SendCommand`/`LoadMoreCommand` — you give the control a `Provider` and a `SessionId`, and it does the rest.

> ChatView is **v1 beta** — the API may still change.

## Basic Usage

```xml
<shiny:ChatView Provider="{Binding Provider}"
                SessionId="{Binding SessionId}"
                MyBubbleColor="#DCF8C6"
                OtherBubbleColor="White" />
```

```csharp
public partial class ChatViewModel : ObservableObject
{
    public ChatViewModel(IChatSessionProvider provider)
    {
        this.Provider = provider;
        this.SessionId = "demo";
    }

    public IChatSessionProvider Provider { get; }
    public string SessionId { get; }
}
```

Blazor:

```razor
<ChatView Provider="provider" SessionId="@sessionId" />

@code {
    [Inject] public IChatSessionProvider provider { get; set; } = default!;
    string sessionId = "demo";
}
```

## The integration interface

You implement `IChatSessionProvider`, which returns a session-scoped `IChatSession` handle. The control subscribes to the session's events on attach and disposes it on detach.

```csharp
public interface IChatSessionProvider
{
    Task<IChatSession> CreateSessionAsync(string[] userIds, CancellationToken cancellationToken = default);

    // throws ChatSessionException if missing or no access
    Task<IChatSession> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);
}

public interface IChatSession : IAsyncDisposable
{
    ChatSessionInfo Info { get; }     // always current — refreshed before SessionUpdated fires
    string CurrentUserId { get; }     // drives bubble alignment + ownership checks

    // cursor-based paging (stable under live inserts); null + Older = newest page
    // ct is cancelled when SessionId changes mid-load - honour it; a late result is discarded either way
    Task<MessagePage> GetMessagesAsync(string? cursorMessageId, MessagePageDirection direction, int count, CancellationToken ct = default);

    Task<ChatMessage> SendMessageAsync(OutgoingMessage message, CancellationToken ct = default);
    Task<ChatMessage> ResendMessageAsync(string clientMessageId, CancellationToken ct = default);
    Task EditMessageAsync(string messageId, string body, CancellationToken ct = default);
    Task DeleteMessageAsync(string messageId, CancellationToken ct = default);

    // add == true toggles the emoji on; add == false removes it
    Task ReactToMessageAsync(string messageId, string emoji, bool add, CancellationToken ct = default);

    Task MarkReadAsync(string[] messageIds, CancellationToken ct = default);   // control passes only visible, not-mine, unread ids
    Task ToggleTypingAsync(bool isTyping, CancellationToken ct = default);
    Task InviteUserAsync(string userId, CancellationToken ct = default);
    Task LeaveAsync(CancellationToken ct = default);
    Task RenameAsync(string sessionName, CancellationToken ct = default);

    event EventHandler<ChatMessage> MessageReceived;       // includes echoes of own sends (multi-device)
    event EventHandler<MessageChanged> MessageUpdated;     // carries WHAT changed
    event EventHandler<string> MessageDeleted;
    event EventHandler<UserTypingEvent> UserTyping;
    event EventHandler<ChatSessionUserInfo> UserJoined;
    event EventHandler<ChatSessionUserInfo> UserLeft;
    event EventHandler<ChatSessionInfo> SessionUpdated;
    event EventHandler<ChatConnectionState> ConnectionStateChanged;
}
```

Events may fire off the UI thread — the control marshals them. The control merges/dedups messages by `MessageId` and reconciles optimistic sends by `ClientMessageId`. Ownership is `message.SenderId == session.CurrentUserId`.

## Data Models

These records are identical on MAUI and Blazor **except** `ChatSessionUserInfo` (MAUI uses `ImageSource`/`Color`; Blazor uses `string` URL / CSS color).

```csharp
public record ChatMessage(
    string MessageId,
    string? ClientMessageId,                 // matches OutgoingMessage.ClientMessageId for echo reconciliation
    string SenderId,
    string? Body,                            // markdown
    string? ImageUrl,
    MessageStatus Status,                    // Sending/Sent/Delivered/Read/Failed/Rejected
    string? StatusReason,                    // shown on Failed/Rejected bubbles
    DateTimeOffset Timestamp,
    DateTimeOffset? EditedTimestamp,
    IReadOnlyList<Reaction> Reactions,
    IReadOnlyList<ReadReceipt> ReadReceipts, // per-user; control collapses to a "Read" hint for 1:1
    string? Identifier = null,               // template-selector discriminator
    IReadOnlyDictionary<string, string>? Metadata = null   // custom payload for templates
);

public enum MessageStatus { Sending, Sent, Delivered, Read, Failed, Rejected }

public record Reaction(string UserId, string Emoji, DateTimeOffset Timestamp);
public record ReadReceipt(string UserId, DateTimeOffset Timestamp);

public record MessageChanged(ChatMessage Message, MessageChangeKind Change);
public enum MessageChangeKind { Edited, ReactionChanged, ReadReceiptChanged, StatusChanged }

public record MessagePage(IReadOnlyList<ChatMessage> Messages, bool HasMore);  // Messages always chronological asc
public enum MessagePageDirection { Older, Newer }

public record OutgoingMessage(string? Body, OutgoingAttachment? Attachment = null, string ClientMessageId = "");
public record OutgoingAttachment(ChatAttachmentKind Kind, Stream Content, string FileName, string ContentType); // provider OWNS + DISPOSES Content
public enum ChatAttachmentKind { Image }

public record ChatSessionInfo(
    string SessionId,
    string SessionName,
    ChatSessionUserInfo[] Users,
    string[]? PermittedEmojis,               // null => control default set; empty => no reactions
    MessageBodyPermissions BodyPermissions,  // drives the markdown toolbar
    ChatSessionPermissions Permissions,      // drives every action affordance
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastReadDate,
    int UnreadMessageCount
);

// MAUI
public record ChatSessionUserInfo(string UserId, string DisplayName, ImageSource? Avatar, Color? BubbleColor, DateTimeOffset JoinedDate);
// Blazor
public record ChatSessionUserInfo(string UserId, string DisplayName, string? AvatarUrl, string? BubbleColor, DateTimeOffset JoinedDate);

public record UserTypingEvent(string UserId, bool IsTyping, DateTimeOffset Timestamp);
public enum ChatConnectionState { Connected, Reconnecting, Offline }
```

### Permissions

The control derives every action affordance from `Info.Permissions` + ownership — there is no per-tool wiring anymore.

```csharp
[Flags]
public enum ChatSessionPermissions
{
    None = 0,
    CanSendMessages = 1,
    CanEditMessages = 2,    // own messages only
    CanDeleteMessages = 4,  // own messages only
    CanReactToMessages = 8,
    CanInviteUsers = 16,
    CanLeaveSession = 32,
    CanChangeSessionName = 64,
    CanSendImages = 128,    // gates the gallery/camera attach affordance

    Default = CanSendMessages | CanSendImages | CanReactToMessages | CanInviteUsers | CanLeaveSession,
    All = CanSendMessages | CanEditMessages | CanDeleteMessages | CanReactToMessages
        | CanInviteUsers | CanLeaveSession | CanChangeSessionName | CanSendImages
}

[Flags]
public enum MessageBodyPermissions
{
    None = 0,
    Links = 1, Bold = 2, Italics = 4, Underline = 8, Strikethrough = 16, Codeblocks = 32,
    All = Links | Bold | Italics | Underline | Strikethrough | Codeblocks
}
```

| Permission | Surfaced as |
|---|---|
| `CanSendMessages` | input bar enabled; `SendMessageAsync` |
| `CanEditMessages` | "Edit" on own bubbles → `EditMessageAsync` |
| `CanDeleteMessages` | "Delete" on own bubbles → `DeleteMessageAsync` |
| `CanReactToMessages` | reaction picker (filtered to `PermittedEmojis`) → `ReactToMessageAsync` |
| `CanInviteUsers` | invite affordance → `InviteUserAsync` |
| `CanLeaveSession` | leave affordance → `LeaveAsync` |
| `CanChangeSessionName` | rename affordance → `RenameAsync` |
| `CanSendImages` | gallery/camera attach affordance |

### Exceptions — provider rejects, control reacts

Validation that depends on server/business rules lives in the **provider**; the control attempts optimistically and renders the verdict — it never pre-checks size/count.

```csharp
// GetSessionAsync: session missing or no access
public class ChatSessionException : Exception { ... }

// SendMessageAsync/ResendMessageAsync/EditMessageAsync: content refused (too big, too many images, etc.)
// -> control flips the optimistic bubble to MessageStatus.Rejected, sets StatusReason, no retry.
public class ChatSendRejectedException : Exception { public SendRejectionKind Kind { get; } }
public enum SendRejectionKind { MessageTooLarge, TooManyAttachments, AttachmentTooLarge, UnsupportedContent, NotPermitted, Other }
```

A *transient* failure (no exception of this type) → `MessageStatus.Failed` + retry via `ResendMessageAsync`. A *rejection* → `MessageStatus.Rejected` + reason, no retry.

## ChatView Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Provider` | `IChatSessionProvider?` | `null` | The integration provider |
| `SessionId` | `string?` | `null` | Session to resolve via `GetSessionAsync` |
| `PageSize` | `int` | `30` | Messages fetched per page |
| `OpenImagesInViewer` | `bool` | `true` | Tapping an image bubble opens the built-in `ImageViewer` |
| `MyBubbleColor` | `Color`/`string` | `#DCF8C6` | Local user bubble color |
| `MyTextColor` | `Color`/`string` | `Black` | Local user text color |
| `OtherBubbleColor` | `Color`/`string` | `White` | Default other-user bubble color (overridden by user's `BubbleColor`) |
| `OtherTextColor` | `Color`/`string` | `Black` | Other-user text color |
| `ChatBackgroundColor` | `Color?`/`string?` | `null` | Messages area background |
| `BubbleFontSize` | `double` | `15` | Bubble text size (MAUI) |
| `BubbleFontFamily` | `string?` | `null` | Bubble font family (MAUI) |
| `TimestampFontSize` | `double` | `11` | Timestamp size (MAUI) |
| `BubbleCornerRadius` | `double` | `18` | Bubble corner radius (tail corner remains 4) (MAUI) |
| `PlaceholderText` | `string` | `"Type a message..."` | Input placeholder |
| `SendButtonText` | `string` | `"Send"` | Send button label |
| `SendButtonBackgroundColor` | `Color`/`string` | `#007AFF` | Send button background (MAUI) |
| `SendButtonTextColor` | `Color`/`string` | `White` | Send button text (MAUI) |
| `InputBarBackgroundColor` | `Color?` | theme | Area behind the composer |
| `InputBarBorderColor` | `Color?` | theme | Outline of the rounded composer (there is no separator rule) |
| `InputBar` | `ChatEntryView` | built-in | The hosted composer. Read it to tweak (`InputBar.MaxLines = 3`) or assign your own to replace it (MAUI only) |
| `MaxInputRows` | `int` | `6` | How tall the composer grows before it scrolls (Blazor only) |
| `InputTemplate` | `RenderFragment?` | `null` | Replaces the built-in composer entirely (Blazor only) |
| `IsInputBarVisible` | `bool` | `true` | Show/hide the input bar (set `false` for read-only chats) |
| `ShowTypingIndicator` | `bool` | `true` | Enable typing indicators |
| `ScrollToFirstUnread` | `bool` | `false` | Anchor initial scroll at the first unread (via `Info.LastReadDate`) instead of the end |
| `InputActions` | `IList<ChatInputAction>` | `[]` | Custom input-bar actions (MAUI only) |
| `CustomBubbleActions` | `IList<ChatBubbleAction>` | `[]` | Custom bubble actions appended to the permission-driven set (MAUI only) |
| `MessageTemplate` | `DataTemplate?` | `null` | Single template for all message content (MAUI only) |
| `MessageTemplateSelector` | `DataTemplateSelector?` | `null` | Per-type template selector (MAUI only) |
| `UseFeedback` | `bool` | `true` | Haptic feedback on interactions (MAUI only) |
| `AdjustForKeyboard` | `bool` | `true` | Keeps the composer above the on-screen keyboard: iOS pads the chat by the keyboard overlap; Android switches the window to `adjustResize` while the chat is on screen (the app's own mode comes back when it leaves) so the composer rises with the keyboard instead of the window panning it half under. **Leave it on inside a FloatingPanel** — once `ExpandOnInputFocus` has taken the panel to its top detent, this padding is the only thing lifting the composer clear of the keyboard. The panel already guards against the competing height animation that used to drop keystrokes. (MAUI only) |

## Methods (MAUI only)

| Member | Description |
|---|---|
| `ScrollToEnd(bool animate)` | Scroll to the latest message |
| `ScrollToMessage(string messageId, bool animate)` | Scroll to a message by id |
| `SubmitEntry()` | Programmatically submit the input text |
| `EntryText` (property) | Get/set the input field text |
| `MessageTapped` (event) | Fires for non-image bubble taps |

## The composer — `ChatEntryView` (MAUI) / `ChatEntry` (Blazor)

The composer is its own public control, laid out as a single rounded card in the AI-chat idiom — the
formatting toolbar runs along the top, the **multiline** auto-growing entry spans the full width
beneath it, and the remaining controls sit on a row below that:

```
┌────────────────────────────────────────────┐
│  B  I  U  S  </>  🔗                       │   ← formatting (only if permitted)
│  How can I help you today?                 │
│  +  [Chat]                Model  🎤   ↑    │   ← LeftToolbar … RightToolbar + send
└────────────────────────────────────────────┘
```

`ChatView` builds and hosts one automatically — **generate `<shiny:ChatView …/>` on its own for the
normal case** and only supply a composer when the shape needs to change.

It has **no dependency on `IChatSessionProvider` or `IChatSession`**. `ChatView` stays the only thing
that talks to the session; it pushes state into the composer (`SetBodyPermissions`,
`ShowAttachButton`, `SetInputEnabled`) and subscribes to its events. That is what makes the composer
usable standalone — an AI prompt box or a comment field — with no chat session in sight.

```xml
<shiny:ChatView Provider="{Binding Provider}" SessionId="{Binding SessionId}">
    <shiny:ChatView.InputBar>
        <shiny:ChatEntryView PlaceholderText="How can I help you today?"
                             SendButtonText="↑"
                             MaxLines="5"
                             CornerRadius="24">
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

| `ChatEntryView` member | Type | Default | Notes |
|---|---|---|---|
| `Text` | `string` | `""` | Two-way by default |
| `PlaceholderText` | `string` | `"Type a message..."` | |
| `MaxLines` | `int` | `6` | Grows to this many lines, then scrolls internally |
| `FontSize` / `FontFamily` | `double` / `string?` | `15` / `null` | `MaxLines` is recomputed from these |
| `SendButtonText` | `string` | `"Send"` | |
| `SendButtonBackgroundColor` / `SendButtonTextColor` | `Color?` | theme | |
| `BarBackgroundColor` | `Color?` | theme | Area behind the composer |
| `ComposerBackgroundColor` | `Color?` | theme | Fill inside the rounded outline |
| `BorderColor` / `BorderThickness` | `Color?` / `double` | theme / `1` | |
| `CornerRadius` | `double` | `24` | |
| `ShowAttachButton` / `ShowActionsButton` | `bool` | `false` | `ChatView` drives these from permissions |
| `LeftToolbar` / `RightToolbar` | `IList<IView>` | `[]` | Views added to either side of the control row; right sits before send |

Events: `SendRequested` (`EventHandler<string>`), `AttachRequested`, `ActionsRequested`,
`LinkRequested`, `EditCancelled`, `TextChanged`.
Methods: `Submit()`, `ClearText()`, `FocusInput()`, `SetInputEnabled(bool)`,
`EnterEditMode(string)`/`ExitEditMode()`, `SetBodyPermissions(MessageBodyPermissions)`,
`ApplyWrap(prefix, suffix, placeholder)`, `InsertLink(displayText, url)`.

Blazor's `ChatEntry` mirrors it as parameters: `@bind-Text`, `Placeholder`, `SendButtonText`,
`IsEnabled`, `ShowAttach`, `BodyPermissions`, `MaxRows` (6), `SendOnEnter` (true),
`LeftToolbar`/`RightToolbar` (`RenderFragment`), plus `OnSend`, `OnAttach`, `OnTyping`, and a
`FocusAsync()` method. `ChatView` surfaces the two slots directly as `InputLeftToolbar` /
`InputRightToolbar` — reach for those before `InputTemplate`, which replaces the whole composer:

```razor
<ChatView Provider="Provider" SessionId="@SessionId">
    <InputRightToolbar>
        <span style="font-size:13px;font-weight:600;">Model</span>
    </InputRightToolbar>
</ChatView>
```

**Enter key:** MAUI uses an `Editor`, so Enter inserts a newline and sending is the button's job (the
AI-composer convention). Blazor sends on Enter and inserts a newline on Shift+Enter; set
`SendOnEnter="false"` to match MAUI.

## What the control handles for you

- **Optimistic send:** generates `ClientMessageId`, shows a `Sending` bubble, reconciles with the echo. `ChatSendRejectedException` → `Rejected` + reason (no retry); other failure → `Failed` + retry (`ResendMessageAsync`). No offline queue — send is only attempted while `Connected`.
- **Cursor paging:** initial `GetMessagesAsync(null, Older, PageSize)`; loads older on scroll-to-top using the oldest `MessageId`; stops when `HasMore` is false.
- **Live-edge scrolling:** opens at the newest message (or the first unread with `ScrollToFirstUnread`) and keeps new messages in view while the reader is at the bottom, re-pinning when a photo or custom bubble grows after it loads. Scrolled up into history, new messages wait behind an "N New Messages" pill on both hosts (Blazor: `.shiny-chat-newmessages`). The list re-pins when the keyboard opens or the composer grows. Don't set the Android `WindowSoftInputMode` for a chat page - ChatView switches to `adjustResize` itself while on screen. AppKit limits: no scroll events (no paging-on-scroll, no pill) and photos sit in a fixed 250×166 box. There is no `ScrollToLast`/auto-follow property to set - don't generate one; call `ScrollToEnd()` to jump back programmatically.
- **Reactions:** picker filtered to `Info.PermittedEmojis`; `null` falls back to the built-in default set (👍 👎 ❤️ 😂 😮 😢 😡 🔥 👏 🙏 💯 🎉); empty array = no reactions.
- **Read receipts:** marks only visible, not-mine, unread ids; ignores inbound `ReadReceiptChanged` for the current user (no loops).
- **Typing:** debounces `ToggleTypingAsync`; expires stale inbound typing indicators.
- **Connection:** shows a banner and disables the input bar while not `Connected`.
- **Markdown toolbar:** shows formatting buttons per `Info.BodyPermissions`; renders the markdown subset (`**bold**`, `*italic*`, `~~strike~~`, `` `code` ``, underline, `[text](url)`) in bubbles. Self-contained — no Markdown-package dependency.
- **Images:** an attach affordance (shown when `CanSendImages`) offers Gallery, plus Camera when the platform supports capture (MAUI `MediaPicker.IsCaptureSupported`; Blazor `<InputFile capture>`). Tapping an image bubble opens the `ImageViewer` when `OpenImagesInViewer`. On MAUI the viewer the chat hosts is deliberately invisible — it exists to raise the lightbox onto the page's overlay layer, not to draw anything inside the conversation. Nothing about the host page is required; any `ContentPage` works.

## Custom actions (MAUI only)

The old `ChatEntryTool`/`ChatBubbleTool` FAB tool tree is gone. Built-in actions (react/edit/delete/copy) are derived from permissions. For app-specific verbs, add lightweight actions:

```csharp
public class ChatInputAction : BindableObject
{
    public string? Text { get; set; }
    public ImageSource? Icon { get; set; }
    public Func<ChatView, Task>? Handler { get; set; }
    public event EventHandler<ChatView>? Clicked;
    public virtual Task InvokeAsync(ChatView chatView);   // overridable
}

public class ChatBubbleAction : BindableObject
{
    public string? Text { get; set; }
    public ImageSource? Icon { get; set; }
    public Func<ChatMessage, Task>? Handler { get; set; }
    public event EventHandler<ChatMessage>? Clicked;
    public virtual Task InvokeAsync(ChatMessage message);  // overridable
}
```

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

`SpeechToTextTool : ChatInputAction` and `TextToSpeechBubbleTool : ChatBubbleAction` ship in `Shiny.Maui.Controls.SpeechAddins`.

## Custom message templates (MAUI only)

Use `ChatMessage.Identifier` or `Metadata` as the discriminator in a `DataTemplateSelector`:

```csharp
public class ChatMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TextTemplate { get; set; }
    public DataTemplate? ActionTemplate { get; set; }

    protected override DataTemplate? OnSelectTemplate(object item, BindableObject container)
        => item is ChatMessage { Identifier: "action" } ? ActionTemplate : TextTemplate;
}
```

```xml
<shiny:ChatView.MessageTemplateSelector>
    <local:ChatMessageTemplateSelector>
        <local:ChatMessageTemplateSelector.TextTemplate>
            <DataTemplate x:DataType="shiny:ChatMessage">
                <Label Text="{Binding Body}" />
            </DataTemplate>
        </local:ChatMessageTemplateSelector.TextTemplate>
    </local:ChatMessageTemplateSelector>
</shiny:ChatView.MessageTemplateSelector>
```

## Code Generation Guidance

- Implement `IChatSessionProvider` to back the chat — **do not** bind a `Messages` collection or wire `SendCommand`/`LoadMoreCommand`/`AttachImageCommand` (those no longer exist). Give the control a `Provider` + `SessionId`.
- Put all validation (size caps, image counts, content policy) in the provider; throw `ChatSendRejectedException` to reject a send and `ChatSessionException` for an unknown/forbidden session. Never reimplement those checks in the UI.
- Gate features with `ChatSessionPermissions` on `ChatSessionInfo` — the control shows/hides affordances automatically. Set `MessageBodyPermissions` to control the markdown toolbar; set `CanSendImages` to allow attachments.
- Raise `MessageReceived` for inbound messages and echoes of the sender's own messages; raise `MessageUpdated` with the right `MessageChangeKind` for edits/reactions/receipts; the control updates by `MessageId`.
- `GetMessagesAsync` is cursor-based — return `HasMore=false` when there's no more history. Messages within a page must be chronological ascending.
- For reactions, expose `PermittedEmojis` (or `null` for the default set); the control toggles via `ReactToMessageAsync(id, emoji, add)`.
- Set `IsInputBarVisible = false` for read-only chats. Leave `AdjustForKeyboard` at its default inside a FloatingPanel — see the property table.
- When hosting a ChatView in a `FloatingPanel`, set `IsContentScrollEnabled="False"` on the panel and give the ChatView **no** `HeightRequest`: the chat owns its own scrolling and should fill whatever detent the panel is at. A fixed height either overflows the panel (hiding the composer) or forces `FitContent="True"`, which collapses the panel to a single non-draggable detent.
- On Blazor, put the `ChatView` in a wrapper with `height: 100%` inside `<SheetContent>`; the sheet sizes its body to the visible detent band, so the chat fills it and its input bar stays on screen.
- Don't hand-build a composer. `ChatView` already hosts a `ChatEntryView`/`ChatEntry`; set the pass-through properties, or supply your own composer via `InputBar` / `InputTemplate`. Never wire a composer to `IChatSession` directly — `ChatView` owns that.
- For app-specific verbs (MAUI), add `ChatInputAction`/`ChatBubbleAction` — don't try to recreate the removed tool classes.

### Dark mode

Leave `MyBubbleColor` / `MyTextColor` / `OtherBubbleColor` / `OtherTextColor` unset — they default to
theme tokens and follow the scheme. Set all four (not just the bubbles) if you want the classic
messenger green-and-white look.

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

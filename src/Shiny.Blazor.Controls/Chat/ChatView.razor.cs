using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

using Shiny.Blazor.Controls.Infrastructure;
using Shiny.Blazor.Controls.Theming;

namespace Shiny.Blazor.Controls.Chat;

public partial class ChatView : IAsyncDisposable
{
    static readonly string[] DefaultEmojis =
        ["\U0001F44D", "\U0001F44E", "❤️", "\U0001F602", "\U0001F62E", "\U0001F622",
         "\U0001F621", "\U0001F525", "\U0001F44F", "\U0001F64F", "\U0001F4AF", "\U0001F389"];

    const long MaxImageBytes = 20 * 1024 * 1024;
    static readonly TimeSpan TypingExpiry = TimeSpan.FromSeconds(6);
    static readonly TimeSpan TypingOutIdle = TimeSpan.FromSeconds(3);

    IJSObjectReference? module;
    bool disposed;
    DotNetObjectReference<ChatView>? selfRef;
    ElementReference messagesEl;

    IChatSession? session;
    string? boundSessionId;          // tracks the (provider, session) we resolved against
    IChatSessionProvider? boundProvider;

    readonly List<ChatMessage> messages = new();
    readonly Dictionary<string, long> typing = new();  // userId -> timestamp of the last typing event
    TimeProvider time = TimeProvider.System;

    [Inject] IServiceProvider Services { get; set; } = default!;

    string? inputText;
    string? errorMessage;
    bool loading;
    bool loadingOlder;
    CancellationTokenSource? olderCts;   // the in-flight LoadOlder - cancelled when the session changes
    bool hasMoreOlder;
    bool pendingScrollToEnd;
    int unreadCount;                 // messages that arrived while the reader was scrolled up
    bool initialized;
    bool typingSent;

    ChatConnectionState connectionState = ChatConnectionState.Connected;

    // transient UI state
    string? actionsMessageId;        // bubble whose action menu is open
    string? reactionPickerMessageId; // bubble whose emoji picker is open
    string? editingMessageId;
    string? editText;

    // image viewer
    bool viewerOpen;
    string? viewerSource;

    Timer? typingTimer;

    // ---- Parameters: provider-driven ----
    [Parameter] public IChatSessionProvider? Provider { get; set; }
    [Parameter] public string? SessionId { get; set; }
    [Parameter] public int PageSize { get; set; } = 30;
    [Parameter] public bool OpenImagesInViewer { get; set; } = true;

    // ---- Parameters: styling (kept) ----
    // Bubbles are chrome, so they follow the theme by default rather than shipping a fixed pale
    // green and white - both of which stayed exactly as light in dark mode, since these land as
    // inline styles. An app that wants the messenger look still sets them and that still pins.
    // Mirrored in ChatView.razor.css on .is-me/.is-other; only a caller-chosen value inlines.
    internal const string DefaultMyBubbleColor = "var(--shiny-color-primary-container, #C2D8FA)";
    internal const string DefaultMyTextColor = "var(--shiny-color-on-primary-container, #124294)";
    internal const string DefaultOtherBubbleColor = "var(--shiny-color-surface-container-high, #E2E6EA)";
    internal const string DefaultOtherTextColor = "var(--shiny-color-on-surface, #161C23)";

    [Parameter] public string MyBubbleColor { get; set; } = DefaultMyBubbleColor;
    [Parameter] public string MyTextColor { get; set; } = DefaultMyTextColor;
    [Parameter] public string OtherBubbleColor { get; set; } = DefaultOtherBubbleColor;
    [Parameter] public string OtherTextColor { get; set; } = DefaultOtherTextColor;
    [Parameter] public string PlaceholderText { get; set; } = "Type a message...";
    [Parameter] public string SendButtonText { get; set; } = "Send";
    [Parameter] public bool IsInputBarVisible { get; set; } = true;
    [Parameter] public bool ShowTypingIndicator { get; set; } = true;

    /// <summary>How tall the composer may grow before it scrolls internally, in lines of text.</summary>
    [Parameter] public int MaxInputRows { get; set; } = 6;

    /// <summary>
    /// Replaces the built-in <see cref="ChatEntry"/> composer entirely. You own wiring your markup up
    /// to the session - the simplest route is to render a <see cref="ChatEntry"/> of your own with the
    /// parameters you want.
    /// </summary>
    [Parameter] public RenderFragment? InputTemplate { get; set; }

    /// <summary>Markup added to the left of the composer's control row, after the built-in buttons.</summary>
    [Parameter] public RenderFragment? InputLeftToolbar { get; set; }

    /// <summary>Markup added to the right of the composer's control row, before the send button.</summary>
    [Parameter] public RenderFragment? InputRightToolbar { get; set; }

    // ---- derived ----
    string CurrentUserId => session?.CurrentUserId ?? string.Empty;
    bool IsMultiPerson => (session?.Info.Users.Length ?? 0) > 2;
    bool IsConnected => connectionState == ChatConnectionState.Connected;

    ChatSessionPermissions Permissions => session?.Info.Permissions ?? ChatSessionPermissions.None;
    MessageBodyPermissions BodyPermissions => session?.Info.BodyPermissions ?? MessageBodyPermissions.None;

    bool CanSend => Permissions.HasFlag(ChatSessionPermissions.CanSendMessages) && IsConnected;
    bool CanSendImages => Permissions.HasFlag(ChatSessionPermissions.CanSendImages) && IsConnected;
    bool CanReact => Permissions.HasFlag(ChatSessionPermissions.CanReactToMessages);
    bool CanEdit => Permissions.HasFlag(ChatSessionPermissions.CanEditMessages);
    bool CanDelete => Permissions.HasFlag(ChatSessionPermissions.CanDeleteMessages);

    string[] ReactionEmojis => session?.Info.PermittedEmojis ?? DefaultEmojis;

    bool IsMine(ChatMessage m) => session is not null && m.SenderId == session.CurrentUserId;
    ChatSessionUserInfo? GetUser(string userId) => session?.Info.Users.FirstOrDefault(u => u.UserId == userId);


    protected override void OnInitialized()
        => time = ShinyTime.From(Services);

    protected override async Task OnParametersSetAsync()
    {
        if (!ReferenceEquals(Provider, boundProvider) || SessionId != boundSessionId)
        {
            await TeardownSessionAsync();

            boundProvider = Provider;
            boundSessionId = SessionId;

            if (Provider is not null && !string.IsNullOrEmpty(SessionId))
                await InitSessionAsync(Provider, SessionId);
        }
    }


    async Task InitSessionAsync(IChatSessionProvider provider, string sessionId)
    {
        loading = true;
        errorMessage = null;
        StateHasChanged();
        try
        {
            var opened = await provider.GetSessionAsync(sessionId);

            // Disposed (or re-bound) while the provider was connecting: TeardownSessionAsync has
            // already run and found no session, so subscribing now would hang this component off the
            // session's events and start a typing timer that ticks against it forever.
            if (disposed || !ReferenceEquals(provider, boundProvider) || sessionId != boundSessionId)
            {
                try { await opened.DisposeAsync(); } catch { }
                return;
            }

            session = opened;
            Subscribe(session);
            connectionState = ChatConnectionState.Connected;

            var page = await session.GetMessagesAsync(null, MessagePageDirection.Older, PageSize);
            messages.Clear();
            messages.AddRange(page.Messages.OrderBy(m => m.Timestamp));
            hasMoreOlder = page.HasMore;
            pendingScrollToEnd = true;

            EnsureTypingTimer();
        }
        catch (ChatSessionException ex)
        {
            errorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }
        finally
        {
            loading = false;
            StateHasChanged();
        }
    }


    async Task TeardownSessionAsync()
    {
        typingTimer?.Dispose();
        typingTimer = null;
        typing.Clear();

        // A history page still in flight belongs to the session being torn down. Left running it lands
        // in the next conversation's message list and overwrites its hasMoreOlder.
        olderCts?.Cancel();
        olderCts = null;
        loadingOlder = false;

        if (session is not null)
        {
            Unsubscribe(session);
            try { await session.DisposeAsync(); } catch { }
            session = null;
        }

        messages.Clear();
        unreadCount = 0;
        hasMoreOlder = false;
        errorMessage = null;
        typingSent = false;
        connectionState = ChatConnectionState.Connected;
    }


    void Subscribe(IChatSession s)
    {
        s.MessageReceived += OnMessageReceived;
        s.MessageUpdated += OnMessageUpdated;
        s.MessageDeleted += OnMessageDeleted;
        s.UserTyping += OnUserTyping;
        s.UserJoined += OnUserChanged;
        s.UserLeft += OnUserChanged;
        s.SessionUpdated += OnSessionUpdated;
        s.ConnectionStateChanged += OnConnectionStateChanged;
    }

    void Unsubscribe(IChatSession s)
    {
        s.MessageReceived -= OnMessageReceived;
        s.MessageUpdated -= OnMessageUpdated;
        s.MessageDeleted -= OnMessageDeleted;
        s.UserTyping -= OnUserTyping;
        s.UserJoined -= OnUserChanged;
        s.UserLeft -= OnUserChanged;
        s.SessionUpdated -= OnSessionUpdated;
        s.ConnectionStateChanged -= OnConnectionStateChanged;
    }


    // ---- event handlers (marshalled to the UI thread) ----

    void OnMessageReceived(object? sender, ChatMessage m)
        => _ = InvokeAsync(async () =>
        {
            var mine = IsMine(m);
            var nearBottom = !mine && module is not null
                ? await module.InvokeAsync<bool>("isNearBottom", messagesEl)
                : true;

            var isNew = messages.FindIndex(x => x.MessageId == m.MessageId) < 0
                && (string.IsNullOrEmpty(m.ClientMessageId) || messages.FindIndex(x => x.ClientMessageId == m.ClientMessageId) < 0);

            Merge(m);
            if (mine || nearBottom)
            {
                pendingScrollToEnd = true;
                unreadCount = 0;
            }
            else if (isNew)
            {
                // The reader is back in history: leave the view where it is and count what arrived.
                unreadCount++;
            }
            StateHasChanged();
        });

    void OnMessageUpdated(object? sender, MessageChanged change)
        => _ = InvokeAsync(() =>
        {
            // Replace by MessageId. Self read-receipts don't loop back (we never re-mark our own).
            var idx = messages.FindIndex(x => x.MessageId == change.Message.MessageId);
            if (idx >= 0)
                messages[idx] = change.Message;
            StateHasChanged();
        });

    void OnMessageDeleted(object? sender, string messageId)
        => _ = InvokeAsync(() =>
        {
            messages.RemoveAll(x => x.MessageId == messageId);
            StateHasChanged();
        });

    void OnUserTyping(object? sender, UserTypingEvent e)
        => _ = InvokeAsync(() =>
        {
            if (e.UserId == CurrentUserId)
                return;

            if (e.IsTyping)
                typing[e.UserId] = time.GetTimestamp();
            else
                typing.Remove(e.UserId);

            StateHasChanged();
        });

    void OnUserChanged(object? sender, ChatSessionUserInfo user)
        => _ = InvokeAsync(StateHasChanged);

    void OnSessionUpdated(object? sender, ChatSessionInfo info)
        => _ = InvokeAsync(StateHasChanged);

    void OnConnectionStateChanged(object? sender, ChatConnectionState state)
        => _ = InvokeAsync(() =>
        {
            connectionState = state;
            StateHasChanged();
        });


    // ---- merge / reconcile ----

    void Merge(ChatMessage m)
    {
        var idx = -1;
        if (!string.IsNullOrEmpty(m.ClientMessageId))
            idx = messages.FindIndex(x => x.ClientMessageId == m.ClientMessageId);
        if (idx < 0)
            idx = messages.FindIndex(x => x.MessageId == m.MessageId);

        if (idx >= 0)
            messages[idx] = m;
        else
            InsertSorted(m);
    }

    void InsertSorted(ChatMessage m)
    {
        var i = messages.Count - 1;
        while (i >= 0 && messages[i].Timestamp > m.Timestamp)
            i--;
        messages.Insert(i + 1, m);
    }

    void ReplaceByClientId(string clientId, ChatMessage m)
    {
        var idx = messages.FindIndex(x => x.ClientMessageId == clientId);
        if (idx >= 0)
            messages[idx] = m;
        else
            Merge(m);
    }

    void SetStatus(string clientId, MessageStatus status, string? reason)
    {
        var idx = messages.FindIndex(x => x.ClientMessageId == clientId);
        if (idx >= 0)
            messages[idx] = messages[idx] with { Status = status, StatusReason = reason };
    }


    // ---- render lifecycle ----

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var loaded = await JS.InvokeAsync<IJSObjectReference>(
                "import", "./_content/Shiny.Blazor.Controls/chat.js");
            if (disposed) { await loaded.ReleaseLateAsync(); return; }
            module = loaded;
            selfRef = DotNetObjectReference.Create(this);
            await module.InvokeVoidAsync("init", messagesEl, selfRef);
            initialized = true;
        }

        if (initialized && module is not null && pendingScrollToEnd)
        {
            pendingScrollToEnd = false;
            await module.InvokeVoidAsync("scrollToEnd", messagesEl, false);
        }
    }


    // ---- paging (older) ----

    [JSInvokable]
    public Task OnScrolledToTop() => LoadOlderAsync();

    /// <summary>The reader scrolled back down to the newest message, which reads whatever had queued.</summary>
    [JSInvokable]
    public void OnScrolledToBottom()
    {
        if (unreadCount == 0)
            return;

        unreadCount = 0;
        StateHasChanged();
    }

    async Task OnNewMessagesClicked()
    {
        unreadCount = 0;
        if (module is not null)
            await module.InvokeVoidAsync("scrollToEnd", messagesEl, true);
    }

    async Task LoadOlderAsync()
    {
        if (loadingOlder || !hasMoreOlder || session is null || module is null || messages.Count == 0)
            return;

        loadingOlder = true;
        var s = session;
        var cts = new CancellationTokenSource();
        olderCts = cts;
        var token = cts.Token;
        StateHasChanged();

        try
        {
            var prevHeight = await module.InvokeAsync<double>("getScrollHeight", messagesEl);
            if (token.IsCancellationRequested || !ReferenceEquals(s, session) || messages.Count == 0)
                return;

            var oldest = messages[0].MessageId;
            var page = await s.GetMessagesAsync(oldest, MessagePageDirection.Older, PageSize, token);

            // The provider may ignore the token - the session can still have changed underneath us.
            if (token.IsCancellationRequested || !ReferenceEquals(s, session))
                return;

            foreach (var m in page.Messages)
            {
                if (!messages.Any(x => x.MessageId == m.MessageId))
                    InsertSorted(m);
            }
            hasMoreOlder = page.HasMore;

            loadingOlder = false;
            StateHasChanged();
            await Task.Yield();
            if (!disposed && ReferenceEquals(s, session))
                await module.InvokeVoidAsync("maintainScrollPosition", messagesEl, prevHeight);
        }
        catch
        {
            // leave hasMoreOlder so the user can retry on next scroll
        }
        finally
        {
            // A stale request must not clear the flag of the load that replaced it.
            if (ReferenceEquals(olderCts, cts))
            {
                olderCts = null;
                if (loadingOlder)
                {
                    loadingOlder = false;
                    if (!disposed)
                        StateHasChanged();
                }
            }
            cts.Dispose();
        }
    }


    // ---- sending ----

    async Task SendTextAsync(string text)
    {
        var body = text.Trim();
        if (string.IsNullOrEmpty(body) || session is null || !CanSend)
            return;

        await StopTypingAsync();

        var clientId = Guid.NewGuid().ToString();
        var optimistic = new ChatMessage(
            clientId, clientId, CurrentUserId, body, null,
            MessageStatus.Sending, null, time.GetUtcNow(), null,
            Array.Empty<Reaction>(), Array.Empty<ReadReceipt>());
        messages.Add(optimistic);
        pendingScrollToEnd = true;
        StateHasChanged();

        try
        {
            var result = await session.SendMessageAsync(new OutgoingMessage(body, null, clientId));
            ReplaceByClientId(clientId, result);
        }
        catch (ChatSendRejectedException ex)
        {
            SetStatus(clientId, MessageStatus.Rejected, ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus(clientId, MessageStatus.Failed, ex.Message);
        }
        StateHasChanged();
    }

    async Task ResendAsync(ChatMessage m)
    {
        if (session is null || string.IsNullOrEmpty(m.ClientMessageId))
            return;

        SetStatus(m.ClientMessageId, MessageStatus.Sending, null);
        StateHasChanged();
        try
        {
            var result = await session.ResendMessageAsync(m.ClientMessageId);
            ReplaceByClientId(m.ClientMessageId, result);
        }
        catch (ChatSendRejectedException ex)
        {
            SetStatus(m.ClientMessageId, MessageStatus.Rejected, ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus(m.ClientMessageId, MessageStatus.Failed, ex.Message);
        }
        StateHasChanged();
    }


    // ---- image attachment ----

    async Task OnImageSelected(InputFileChangeEventArgs e)
    {
        var file = e.File;
        if (file is null || session is null || !CanSendImages)
            return;

        byte[] bytes;
        await using (var stream = file.OpenReadStream(MaxImageBytes))
        await using (var ms = new MemoryStream())
        {
            await stream.CopyToAsync(ms);
            bytes = ms.ToArray();
        }

        var contentType = string.IsNullOrEmpty(file.ContentType) ? "image/png" : file.ContentType;
        var dataUrl = $"data:{contentType};base64,{Convert.ToBase64String(bytes)}";

        var clientId = Guid.NewGuid().ToString();
        var optimistic = new ChatMessage(
            clientId, clientId, CurrentUserId, null, dataUrl,
            MessageStatus.Sending, null, time.GetUtcNow(), null,
            Array.Empty<Reaction>(), Array.Empty<ReadReceipt>());
        messages.Add(optimistic);
        pendingScrollToEnd = true;
        StateHasChanged();

        // Provider owns + disposes this stream.
        var sendStream = new MemoryStream(bytes);
        var attachment = new OutgoingAttachment(ChatAttachmentKind.Image, sendStream, file.Name, contentType);
        try
        {
            var result = await session.SendMessageAsync(new OutgoingMessage(null, attachment, clientId));
            ReplaceByClientId(clientId, result);
        }
        catch (ChatSendRejectedException ex)
        {
            SetStatus(clientId, MessageStatus.Rejected, ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus(clientId, MessageStatus.Failed, ex.Message);
        }
        StateHasChanged();
    }

    void OnImageTap(ChatMessage m)
    {
        if (!OpenImagesInViewer || m.ImageUrl is null)
            return;

        viewerSource = m.ImageUrl;
        viewerOpen = true;
    }


    // ---- reactions ----

    bool HasAnyAction(ChatMessage m)
        => (CanReact && ReactionEmojis.Length > 0)
        || m.Body is not null
        || (CanEdit && IsMine(m) && m.ImageUrl is null)
        || (CanDelete && IsMine(m));

    void ToggleActions(ChatMessage m)
    {
        actionsMessageId = actionsMessageId == m.MessageId ? null : m.MessageId;
        reactionPickerMessageId = null;
    }

    void OpenReactionPicker(ChatMessage m)
    {
        reactionPickerMessageId = m.MessageId;
        actionsMessageId = null;
    }

    async Task ToggleReactionAsync(ChatMessage m, string emoji)
    {
        reactionPickerMessageId = null;
        if (session is null || !CanReact)
            return;

        var add = !m.Reactions.Any(r => r.UserId == CurrentUserId && r.Emoji == emoji);
        try { await session.ReactToMessageAsync(m.MessageId, emoji, add); }
        catch { /* provider re-validates; updates arrive via MessageUpdated */ }
    }


    // ---- edit / delete / copy ----

    void BeginEdit(ChatMessage m)
    {
        editingMessageId = m.MessageId;
        editText = m.Body;
        actionsMessageId = null;
    }

    void CancelEdit()
    {
        editingMessageId = null;
        editText = null;
    }

    async Task SaveEditAsync(ChatMessage m)
    {
        var body = editText?.Trim();
        editingMessageId = null;
        if (session is null || string.IsNullOrEmpty(body) || body == m.Body)
        {
            editText = null;
            return;
        }
        editText = null;
        try { await session.EditMessageAsync(m.MessageId, body); }
        catch (ChatSendRejectedException ex)
        {
            var idx = messages.FindIndex(x => x.MessageId == m.MessageId);
            if (idx >= 0)
                messages[idx] = messages[idx] with { Status = MessageStatus.Rejected, StatusReason = ex.Message };
        }
        catch { /* surfaced via provider events */ }
    }

    async Task DeleteAsync(ChatMessage m)
    {
        actionsMessageId = null;
        if (session is null)
            return;
        try { await session.DeleteMessageAsync(m.MessageId); }
        catch { }
    }

    async Task CopyAsync(ChatMessage m)
    {
        actionsMessageId = null;
        if (module is not null && m.Body is not null)
            await module.InvokeVoidAsync("copyText", m.Body);
    }


    // ---- typing (outgoing) ----

    async Task OnInputChanged()
    {
        if (session is null)
            return;

        if (!typingSent && !string.IsNullOrEmpty(inputText))
        {
            typingSent = true;
            try { await session.ToggleTypingAsync(true); } catch { }
        }
        lastInputAt = time.GetTimestamp();
    }

    long lastInputAt;

    async Task StopTypingAsync()
    {
        if (session is null || !typingSent)
            return;
        typingSent = false;
        try { await session.ToggleTypingAsync(false); } catch { }
    }


    // ---- typing (incoming) expiry + outgoing idle timer ----

    void EnsureTypingTimer()
    {
        typingTimer?.Dispose();
        typingTimer = new Timer(_ => _ = InvokeAsync(TickTypingAsync), null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    async Task TickTypingAsync()
    {
        var now = time.GetTimestamp();

        var expired = typing.Where(kv => time.GetElapsedTime(kv.Value, now) >= TypingExpiry).Select(kv => kv.Key).ToList();
        var changed = expired.Count > 0;
        foreach (var id in expired)
            typing.Remove(id);

        if (typingSent && time.GetElapsedTime(lastInputAt, now) > TypingOutIdle)
            await StopTypingAsync();

        if (changed)
            StateHasChanged();
    }

    string? BuildTypingText()
    {
        if (!ShowTypingIndicator || typing.Count == 0)
            return null;

        var names = typing.Keys
            .Select(id => GetUser(id)?.DisplayName ?? "Someone")
            .ToList();

        return names.Count switch
        {
            1 => $"{names[0]} is typing…",
            2 => $"{names[0]}, {names[1]} are typing…",
            3 => $"{names[0]}, {names[1]}, {names[2]} are typing…",
            _ => "Multiple people are typing…"
        };
    }


    // ---- connection banner text ----

    string? ConnectionBannerText => connectionState switch
    {
        ChatConnectionState.Reconnecting => "Reconnecting…",
        ChatConnectionState.Offline => "You are offline",
        _ => null
    };


    public async ValueTask DisposeAsync()
    {
        disposed = true;
        await TeardownSessionAsync();

        if (module is not null)
        {
            try { await module.InvokeVoidAsync("dispose", messagesEl); } catch { }
            try { await module.DisposeAsync(); } catch { }
        }
        selfRef?.Dispose();
    }
}

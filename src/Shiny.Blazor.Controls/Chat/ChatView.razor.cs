using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

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
    DotNetObjectReference<ChatView>? selfRef;
    ElementReference messagesEl;

    IChatSession? session;
    string? boundSessionId;          // tracks the (provider, session) we resolved against
    IChatSessionProvider? boundProvider;

    readonly List<ChatMessage> messages = new();
    readonly Dictionary<string, DateTimeOffset> typing = new();  // userId -> expires-at

    string? inputText;
    string? errorMessage;
    bool loading;
    bool loadingOlder;
    bool hasMoreOlder;
    bool pendingScrollToEnd;
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
            session = await provider.GetSessionAsync(sessionId);
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

        if (session is not null)
        {
            Unsubscribe(session);
            try { await session.DisposeAsync(); } catch { }
            session = null;
        }

        messages.Clear();
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

            Merge(m);
            if (mine || nearBottom)
                pendingScrollToEnd = true;
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
                typing[e.UserId] = DateTimeOffset.UtcNow.Add(TypingExpiry);
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
            module = await JS.InvokeAsync<IJSObjectReference>(
                "import", "./_content/Shiny.Blazor.Controls/chat.js");
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

    async Task LoadOlderAsync()
    {
        if (loadingOlder || !hasMoreOlder || session is null || module is null || messages.Count == 0)
            return;

        loadingOlder = true;
        StateHasChanged();

        var prevHeight = await module.InvokeAsync<double>("getScrollHeight", messagesEl);
        var oldest = messages[0].MessageId;
        try
        {
            var page = await session.GetMessagesAsync(oldest, MessagePageDirection.Older, PageSize);
            foreach (var m in page.Messages)
            {
                if (!messages.Any(x => x.MessageId == m.MessageId))
                    InsertSorted(m);
            }
            hasMoreOlder = page.HasMore;
        }
        catch
        {
            // leave hasMoreOlder so the user can retry on next scroll
        }
        finally
        {
            loadingOlder = false;
            StateHasChanged();
            await Task.Yield();
            await module.InvokeVoidAsync("maintainScrollPosition", messagesEl, prevHeight);
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
            MessageStatus.Sending, null, DateTimeOffset.UtcNow, null,
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
            MessageStatus.Sending, null, DateTimeOffset.UtcNow, null,
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
        lastInputAt = DateTimeOffset.UtcNow;
    }

    DateTimeOffset lastInputAt;

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
        var now = DateTimeOffset.UtcNow;

        var expired = typing.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList();
        var changed = expired.Count > 0;
        foreach (var id in expired)
            typing.Remove(id);

        if (typingSent && now - lastInputAt > TypingOutIdle)
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
        await TeardownSessionAsync();

        if (module is not null)
        {
            try { await module.InvokeVoidAsync("dispose", messagesEl); } catch { }
            try { await module.DisposeAsync(); } catch { }
        }
        selfRef?.Dispose();
    }
}

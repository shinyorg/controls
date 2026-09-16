using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.Chat;

public partial class ChatView
{
    ChatConnectionState connectionState = ChatConnectionState.Connected;

    void OnLoaded()
    {
        // The viewer is hidden, so its own Loaded is not something to rely on - the chat asks for the
        // page's overlay root here instead. Installing it costs a re-parent of the page's content;
        // done now it is invisible, done on the tap that opens a photo it resets the message list's
        // scroll position.
        this.imageViewer.InstallOverlayRoot();

        // Keyboard observers are process-wide (NSNotificationCenter) and would root the chat - and its
        // page - if left behind when the view leaves the tree without its handler being disconnected.
        if (this.Handler is not null)
            this.HookKeyboard();

        if (this.session is null && this.Provider is not null && !string.IsNullOrEmpty(this.SessionId))
            this.ReloadSession();
    }

    void OnUnloaded()
    {
        this.UnhookKeyboard();
        this.loadCts?.Cancel();
        _ = this.TeardownSessionAsync();
    }

    void OnProviderOrSessionChanged()
    {
        if (this.Provider is not null && !string.IsNullOrEmpty(this.SessionId))
            this.ReloadSession();
    }

    void ReloadSession() => _ = this.ReloadSessionAsync();

    async Task ReloadSessionAsync()
    {
        await this.TeardownSessionAsync();

        var provider = this.Provider;
        var sessionId = this.SessionId;
        if (provider is null || string.IsNullOrEmpty(sessionId))
            return;

        this.loadCts?.Cancel();
        this.loadCts = new CancellationTokenSource();
        var token = this.loadCts.Token;

        this.ShowError(null);
        this.ShowLoader(true);
        this.isReloading = true;
        try
        {
            var s = await provider.GetSessionAsync(sessionId, token);
            if (token.IsCancellationRequested)
            {
                await s.DisposeAsync();
                return;
            }

            this.session = s;
            this.Subscribe(s);

            // ApplyInfo runs BEFORE the first page loads. It calls RefreshBubbles, which tears the
            // items source down and rebuilds it on a dispatch - do that after the initial load and the
            // rebuild lands between the load and the initial ScrollTo, resetting the offset to the top
            // and leaving the scroll to fire against a collection that has not laid out yet. That
            // ordering was why the view never opened at the newest message.
            this.ApplyInfo();
            this.UpdateConnectionUi(this.connectionState);
            await this.InitialLoadAsync(token);
        }
        catch (TaskCanceledException) { }
        catch (ChatSessionException ex)
        {
            this.ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            this.ShowError(ex.Message);
        }
        finally
        {
            this.isReloading = false;
            if (!token.IsCancellationRequested)
                this.ShowLoader(false);
        }
    }

    async Task TeardownSessionAsync()
    {
        this.StopTypingTimer();

        if (this.session is not null)
        {
            this.Unsubscribe(this.session);
            var s = this.session;
            this.session = null;
            try { await s.DisposeAsync(); }
            catch { /* swallow dispose errors */ }
        }

        this.messages.Clear();
        this.typingUsers.Clear();
        this.markReadRequested.Clear();
        this.unreadCount = 0;
        this.isNearBottom = true;
        this.hasMoreOlder = true;
        this.editingMessageId = null;
        this.inputBar.ExitEditMode();
        this.SyncScrollMode();
        this.SyncTypingBubbles();
        this.UpdateToastPill();
    }

    // ------- subscriptions -------

    void Subscribe(IChatSession s)
    {
        s.MessageReceived += this.OnMessageReceivedEvt;
        s.MessageUpdated += this.OnMessageUpdatedEvt;
        s.MessageDeleted += this.OnMessageDeletedEvt;
        s.UserTyping += this.OnUserTypingEvt;
        s.UserJoined += this.OnUserJoinedEvt;
        s.UserLeft += this.OnUserLeftEvt;
        s.SessionUpdated += this.OnSessionUpdatedEvt;
        s.ConnectionStateChanged += this.OnConnectionStateChangedEvt;
    }

    void Unsubscribe(IChatSession s)
    {
        s.MessageReceived -= this.OnMessageReceivedEvt;
        s.MessageUpdated -= this.OnMessageUpdatedEvt;
        s.MessageDeleted -= this.OnMessageDeletedEvt;
        s.UserTyping -= this.OnUserTypingEvt;
        s.UserJoined -= this.OnUserJoinedEvt;
        s.UserLeft -= this.OnUserLeftEvt;
        s.SessionUpdated -= this.OnSessionUpdatedEvt;
        s.ConnectionStateChanged -= this.OnConnectionStateChangedEvt;
    }

    void OnMessageReceivedEvt(object? sender, ChatMessage m) => this.Dispatcher.Dispatch(() => this.HandleMessageReceived(m));
    void OnMessageUpdatedEvt(object? sender, MessageChanged ch) => this.Dispatcher.Dispatch(() => this.HandleMessageUpdated(ch));
    void OnMessageDeletedEvt(object? sender, string id) => this.Dispatcher.Dispatch(() => this.HandleMessageDeleted(id));
    void OnUserTypingEvt(object? sender, UserTypingEvent e) => this.Dispatcher.Dispatch(() => this.HandleUserTyping(e));
    void OnUserJoinedEvt(object? sender, ChatSessionUserInfo u) => this.Dispatcher.Dispatch(this.ApplyInfo);
    void OnUserLeftEvt(object? sender, ChatSessionUserInfo u) => this.Dispatcher.Dispatch(this.ApplyInfo);
    void OnSessionUpdatedEvt(object? sender, ChatSessionInfo info) => this.Dispatcher.Dispatch(this.ApplyInfo);
    void OnConnectionStateChangedEvt(object? sender, ChatConnectionState state) => this.Dispatcher.Dispatch(() => this.UpdateConnectionUi(state));

    // ------- event handlers (UI thread) -------

    void HandleMessageReceived(ChatMessage m)
    {
        var isNew = this.IndexByMessageId(m.MessageId) < 0
            && (string.IsNullOrEmpty(m.ClientMessageId) || this.IndexByClientId(m.ClientMessageId!) < 0);

        this.MergeMessage(m);

        if (isNew)
        {
            var own = this.IsOwnMessage(m);
            if (this.UseFeedback)
                FeedbackHelper.Execute(this, own ? "MessageSent" : "MessageReceived", m);

            if (own || this.isNearBottom)
            {
                this.unreadCount = 0;
                this.UpdateToastPill();
                this.Dispatcher.Dispatch(() => this.ScrollToEnd(true));
            }
            else
            {
                this.unreadCount++;
                this.UpdateToastPill();
            }
            this.MarkVisibleRead();
        }
    }

    void HandleMessageUpdated(MessageChanged ch)
    {
        var idx = this.IndexByMessageId(ch.Message.MessageId);
        if (idx >= 0)
            this.messages[idx] = ch.Message;
        else
            this.MergeMessage(ch.Message);
    }

    void HandleMessageDeleted(string id)
    {
        var idx = this.IndexByMessageId(id);
        if (idx >= 0)
            this.messages.RemoveAt(idx);
    }

    void HandleUserTyping(UserTypingEvent e)
    {
        if (e.UserId == this.CurrentUserId)
            return;

        if (e.IsTyping)
            this.typingUsers[e.UserId] = DateTimeOffset.UtcNow;
        else
            this.typingUsers.Remove(e.UserId);

        this.EnsureTypingTimer();
        this.SyncTypingBubbles();
    }

    // ------- info / connection -------

    void ApplyInfo()
    {
        var info = this.session?.Info;
        if (info is null)
            return;

        this.inputBar.SetBodyPermissions(info.BodyPermissions);
        this.inputBar.ShowAttachButton = info.Permissions.HasFlag(ChatSessionPermissions.CanSendImages);
        this.SyncInputActionsVisibility();
        this.RefreshBubbles();
    }

    void UpdateConnectionUi(ChatConnectionState state)
    {
        this.connectionState = state;
        var connected = state == ChatConnectionState.Connected;

        this.connectionBanner.IsVisible = !connected;
        this.connectionBannerLabel.Text = state == ChatConnectionState.Reconnecting ? "Reconnecting…" : "Offline";
        this.inputBar.SetInputEnabled(connected);
    }

    void ShowLoader(bool show) => this.loaderOverlay.IsVisible = show;

    void ShowError(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            this.errorOverlay.IsVisible = false;
            return;
        }
        this.errorLabel.Text = message;
        this.errorOverlay.IsVisible = true;
    }

    // ------- paging -------

    async Task InitialLoadAsync(CancellationToken token)
    {
        if (this.session is null)
            return;

        var page = await this.session.GetMessagesAsync(null, MessagePageDirection.Older, this.PageSize, token);
        if (token.IsCancellationRequested)
            return;

        this.messages.Clear();
        foreach (var m in page.Messages)
            this.messages.Add(m);

        this.hasMoreOlder = page.HasMore;
        this.markReadRequested.Clear();

        this.PerformInitialScroll();
        this.MarkVisibleRead();
    }

    internal void LoadOlder() => _ = this.LoadOlderAsync();

    async Task LoadOlderAsync()
    {
        if (this.isLoadingOlder || !this.hasMoreOlder || this.session is null || this.messages.Count == 0)
            return;

        this.isLoadingOlder = true;
        this.SyncScrollMode();   // prepending must not drag the view to the newest message

        var cts = new CancellationTokenSource();
        var token = cts.Token;
        try
        {
            var cursor = this.messages[0].MessageId;
            var page = await this.session.GetMessagesAsync(cursor, MessagePageDirection.Older, this.PageSize, token);
            if (token.IsCancellationRequested)
                return;

            var insertAt = 0;
            foreach (var m in page.Messages)
            {
                if (this.IndexByMessageId(m.MessageId) >= 0)
                    continue;
                this.messages.Insert(insertAt++, m);
            }
            this.hasMoreOlder = page.HasMore;

            // KeepScrollOffset holds the numeric offset, not the visual position, so the page the user
            // was reading slides down by however much history arrived. Re-anchor on what used to be the
            // first row.
            if (insertAt > 0)
                this.ScrollWhenMeasured(insertAt, ScrollToPosition.Start, animate: false);
        }
        catch (TaskCanceledException) { }
        catch (ChatSessionException) { }
        catch (Exception) { }
        finally
        {
            this.isLoadingOlder = false;
            this.SyncScrollMode();
        }
    }

    // ------- read receipts -------

    void MarkVisibleRead()
    {
        if (this.session is null)
            return;

        var me = this.session.CurrentUserId;
        var ids = new List<string>();

        foreach (var m in this.messages)
        {
            if (m.SenderId == me || string.IsNullOrEmpty(m.MessageId))
                continue;
            if (this.markReadRequested.Contains(m.MessageId))
                continue;

            var readByMe = false;
            foreach (var r in m.ReadReceipts)
            {
                if (r.UserId == me) { readByMe = true; break; }
            }

            this.markReadRequested.Add(m.MessageId);
            if (!readByMe)
                ids.Add(m.MessageId);
        }

        if (ids.Count > 0)
            _ = this.MarkReadSafeAsync(ids.ToArray());
    }

    async Task MarkReadSafeAsync(string[] ids)
    {
        try
        {
            if (this.session is not null)
                await this.session.MarkReadAsync(ids);
        }
        catch { /* swallow */ }
    }

    // ------- typing expiry -------

    void EnsureTypingTimer()
    {
        if (this.typingUsers.Count == 0)
        {
            this.StopTypingTimer();
            return;
        }
        if (this.typingExpiryTimer is not null)
            return;

        var t = this.Dispatcher.CreateTimer();
        t.Interval = TimeSpan.FromSeconds(1);
        t.Tick += this.OnTypingExpiryTick;
        this.typingExpiryTimer = t;
        t.Start();
    }

    void StopTypingTimer()
    {
        if (this.typingExpiryTimer is not null)
        {
            this.typingExpiryTimer.Tick -= this.OnTypingExpiryTick;
            this.typingExpiryTimer.Stop();
            this.typingExpiryTimer = null;
        }
    }

    void OnTypingExpiryTick(object? sender, EventArgs e)
    {
        var now = DateTimeOffset.UtcNow;
        var stale = this.typingUsers
            .Where(kv => (now - kv.Value).TotalSeconds > 6)
            .Select(kv => kv.Key)
            .ToList();

        if (stale.Count > 0)
        {
            foreach (var k in stale)
                this.typingUsers.Remove(k);
            this.SyncTypingBubbles();
        }
        if (this.typingUsers.Count == 0)
            this.StopTypingTimer();
    }

    // ------- merge helpers -------

    void MergeMessage(ChatMessage m)
    {
        var idx = this.IndexByMessageId(m.MessageId);
        if (idx >= 0)
        {
            this.messages[idx] = m;
            return;
        }
        if (!string.IsNullOrEmpty(m.ClientMessageId))
        {
            var cidx = this.IndexByClientId(m.ClientMessageId!);
            if (cidx >= 0)
            {
                this.messages[cidx] = m;
                return;
            }
        }
        this.InsertSorted(m);
    }

    void InsertSorted(ChatMessage m)
    {
        var i = this.messages.Count;
        while (i > 0 && this.messages[i - 1].Timestamp > m.Timestamp)
            i--;
        this.messages.Insert(i, m);
    }

    int IndexByMessageId(string id)
    {
        for (var i = 0; i < this.messages.Count; i++)
        {
            if (this.messages[i].MessageId == id)
                return i;
        }
        return -1;
    }

    int IndexByClientId(string clientId)
    {
        for (var i = 0; i < this.messages.Count; i++)
        {
            if (this.messages[i].ClientMessageId == clientId)
                return i;
        }
        return -1;
    }
}

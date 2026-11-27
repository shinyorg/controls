using Shiny.Maui.Controls.Chat.Internal;

namespace Shiny.Maui.Controls.Chat;

public partial class ChatView
{
    void OnCollectionViewScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        this.firstVisibleIndex = e.FirstVisibleItemIndex;
        this.lastVisibleIndex = e.LastVisibleItemIndex;

        // While the opening scroll is still landing, the list reports wherever it happens to be on the
        // way - usually offset 0, the top of the first page. Acting on that would page in older history
        // and re-anchor on it, and would drop auto-follow, so the chat opened in the middle of the
        // conversation. The state is evaluated once the scroll has settled.
        if (!this.isSettlingScroll)
            this.ApplyScrollPosition();
    }

    void ApplyScrollPosition()
    {
        var lastIndex = this.messages.Count - 1;
        if (lastIndex < 0)
        {
            this.isNearBottom = true;
            this.SyncScrollMode();
            return;
        }

        // near-top → load older history. Driven off the scroll position rather than
        // RemainingItemsThresholdReached, which fires at the *end* of the list.
        if (this.firstVisibleIndex >= 0 && this.firstVisibleIndex <= 2)
            this.LoadOlder();

        var wasNearBottom = this.isNearBottom;
        this.isNearBottom = this.lastVisibleIndex >= lastIndex - 1;

        if (this.isNearBottom)
        {
            if (this.unreadCount > 0)
            {
                this.unreadCount = 0;
                this.UpdateToastPill();
            }
            this.MarkVisibleRead();
        }

        if (wasNearBottom != this.isNearBottom)
        {
            this.SyncScrollMode();
            this.typingBubbleHost.IsVisible = this.isNearBottom && this.typingBubbleHost.Children.Count > 0;
            this.UpdateToastPill();
        }
    }

    /// <summary>
    /// Chooses how the CollectionView reacts to its own collection changes. Pinning to the last item
    /// is what makes new messages land in view - the handler runs the scroll after the layout pass, so
    /// unlike ScrollTo it cannot fire against an unmeasured item. It is switched off while the user is
    /// reading back through history, or while older messages are being prepended.
    /// </summary>
    void SyncScrollMode()
    {
        var mode = this.isNearBottom && !this.isLoadingOlder
            ? ItemsUpdatingScrollMode.KeepLastItemInView
            : ItemsUpdatingScrollMode.KeepScrollOffset;

        if (this.collectionView.ItemsUpdatingScrollMode != mode)
            this.collectionView.ItemsUpdatingScrollMode = mode;
    }

    void OnToastPillTapped(object? sender, TappedEventArgs e)
    {
        if (this.unreadCount <= 0)
            return;

        this.unreadCount = 0;
        this.UpdateToastPill();
        this.ScrollToEnd(true);
    }

    void UpdateToastPill()
    {
        var hasUnread = this.unreadCount > 0;
        var hasTyping = !this.isNearBottom && this.ShowTypingIndicator && this.typingUsers.Count > 0;

        if (!hasUnread && !hasTyping)
        {
            this.toastPill.IsVisible = false;
            this.toastNewMessagesLabel.IsVisible = false;
            this.toastTypingLabel.IsVisible = false;
            return;
        }

        if (hasUnread)
        {
            this.toastNewMessagesLabel.Text = this.unreadCount == 1 ? "1 New Message" : $"{this.unreadCount} New Messages";
            this.toastNewMessagesLabel.IsVisible = true;
        }
        else
        {
            this.toastNewMessagesLabel.IsVisible = false;
        }

        if (hasTyping)
        {
            this.toastTypingLabel.Text = this.GetTypingText();
            this.toastTypingLabel.IsVisible = true;
        }
        else
        {
            this.toastTypingLabel.IsVisible = false;
        }

        this.toastPill.IsVisible = true;
    }

    string GetTypingText()
    {
        var names = this.typingUsers.Keys
            .Select(id => this.GetUser(id)?.DisplayName ?? "Someone")
            .ToList();

        return names.Count switch
        {
            0 => string.Empty,
            1 => $"{names[0]} is typing…",
            2 => $"{names[0]}, {names[1]} are typing…",
            3 => $"{names[0]}, {names[1]}, {names[2]} are typing…",
            _ => "Multiple users are typing…"
        };
    }

    void SyncTypingBubbles()
    {
        this.typingBubbleHost.Children.Clear();

        if (this.ShowTypingIndicator && this.typingUsers.Count > 0)
        {
            foreach (var userId in this.typingUsers.Keys)
            {
                var bubble = new ChatTypingBubbleView(this, userId);
                this.typingBubbleHost.Children.Add(bubble);
            }
            this.typingBubbleHost.IsVisible = this.isNearBottom;
        }
        else
        {
            this.typingBubbleHost.IsVisible = false;
        }

        this.UpdateToastPill();
    }

    void PerformInitialScroll()
    {
        if (this.messages.Count == 0)
            return;

        if (this.ScrollToFirstUnread && this.session?.Info.LastReadDate is DateTimeOffset last)
        {
            for (var i = 0; i < this.messages.Count; i++)
            {
                if (this.messages[i].Timestamp > last)
                {
                    // Landing mid-history means the user is not at the bottom, so incoming
                    // messages must queue behind the unread pill instead of yanking the view.
                    this.isNearBottom = i >= this.messages.Count - 2;
                    this.SyncScrollMode();
                    this.ScrollWhenMeasured(i, ScrollToPosition.Start, animate: false, settle: true);
                    return;
                }
            }
        }
        this.ScrollToEnd(false, settle: true);
    }

    /// <summary>Scrolls to the newest message and re-arms auto-follow.</summary>
    public void ScrollToEnd(bool animate = false) => this.ScrollToEnd(animate, settle: false);

    void ScrollToEnd(bool animate, bool settle)
    {
        this.isNearBottom = true;
        this.SyncScrollMode();
        this.ScrollWhenMeasured(this.messages.Count - 1, ScrollToPosition.End, animate, settle);
    }

    double listHeight = -1;

    /// <summary>
    /// The list got shorter or taller - the keyboard opening under the composer, the composer growing a
    /// line, a rotation. The platform keeps the top offset, so the newest messages slide out of view
    /// behind the composer. A reader following the live edge is put back on it.
    /// </summary>
    void OnCollectionViewSizeChanged(object? sender, EventArgs e)
    {
        var height = this.collectionView.Height;
        var changed = this.listHeight > 0 && Math.Abs(height - this.listHeight) > 0.5;
        this.listHeight = height;

        if (changed && height > 0 && this.isNearBottom && !this.isLoadingOlder && this.messages.Count > 0)
            this.ScrollWhenMeasured(this.messages.Count - 1, ScrollToPosition.End, animate: false, settle: false);
    }

    /// <summary>
    /// A bubble changed height after it was laid out - a photo or a custom template finishing its load.
    /// KeepLastItemInView only reacts to the collection changing, not to a row growing, so without this
    /// the newest message's photo pushes its own bottom - and the timestamp under it - out of view.
    /// Only the last couple of rows matter, and only while the reader is following the live edge.
    /// </summary>
    internal void OnBubbleResized(ChatMessage? message)
    {
        var count = this.messages.Count;
        if (message is null || count == 0 || !this.isNearBottom || this.isLoadingOlder)
            return;

        var isTail = ReferenceEquals(message, this.messages[count - 1])
            || (count > 1 && ReferenceEquals(message, this.messages[count - 2]));

        if (isTail && !this.repinQueued)
        {
            this.repinQueued = true;
            this.Dispatcher.Dispatch(() =>
            {
                this.repinQueued = false;
                if (this.isNearBottom && !this.isLoadingOlder)
                    this.ScrollWhenMeasured(this.messages.Count - 1, ScrollToPosition.End, animate: false, settle: false);
            });
        }
    }

    /// <summary>Scrolls the given message into view, or to the end when it is not loaded.</summary>
    public void ScrollToMessage(string messageId, bool animate = true)
    {
        for (var i = 0; i < this.messages.Count; i++)
        {
            if (this.messages[i].MessageId == messageId)
            {
                this.isNearBottom = i >= this.messages.Count - 2;
                this.SyncScrollMode();
                this.ScrollWhenMeasured(i, ScrollToPosition.Start, animate, settle: false);
                return;
            }
        }
        this.ScrollToEnd(animate);
    }

    const int ScrollAttempts = 8;
    const int ScrollMinAttempts = 3;
    static readonly TimeSpan ScrollRetryDelay = TimeSpan.FromMilliseconds(80);

    /// <summary>
    /// ScrollTo is dropped on the floor when the target item has not been measured yet, which is the
    /// normal state for a frame or two after the items source is populated or replaced - the single
    /// biggest reason a chat opens somewhere other than the bottom. Re-issue across the next layout
    /// passes until the target is reported visible.
    ///
    /// Each call supersedes the one before it, so two scrolls never fight over the offset (the opening
    /// scroll to the newest message against the re-anchor after a history page, say). With
    /// <paramref name="settle"/>, scroll positions reported along the way are ignored until the loop
    /// finishes - see <see cref="OnCollectionViewScrolled"/>.
    /// </summary>
    void ScrollWhenMeasured(int index, ScrollToPosition position, bool animate, bool settle)
    {
        if (index < 0)
            return;

        var generation = ++this.scrollGeneration;
        if (settle)
        {
            this.isSettlingScroll = true;
            this.firstVisibleIndex = -1;
            this.lastVisibleIndex = -1;
        }

        var attempt = 0;

        void Try()
        {
            // superseded by a newer scroll, which now owns the settle as well
            if (generation != this.scrollGeneration)
                return;

            if (index >= this.messages.Count)
            {
                this.EndSettle();
                return;
            }

            this.collectionView.ScrollTo(index, position: position, animate: animate && attempt == 0);
            attempt++;

            var landed = attempt >= ScrollMinAttempts && this.IsIndexVisible(index, position);
            if (landed || attempt >= ScrollAttempts)
                this.Dispatcher.DispatchDelayed(ScrollRetryDelay, () =>
                {
                    if (generation == this.scrollGeneration)
                        this.EndSettle();
                });
            else
                this.Dispatcher.DispatchDelayed(ScrollRetryDelay, Try);
        }

        this.Dispatcher.Dispatch(Try);
    }

    bool IsIndexVisible(int index, ScrollToPosition position)
    {
        if (this.lastVisibleIndex < 0)
            return false;

        return position == ScrollToPosition.End
            ? this.lastVisibleIndex >= index
            : this.firstVisibleIndex <= index && this.lastVisibleIndex >= index;
    }

    void EndSettle()
    {
        if (!this.isSettlingScroll)
            return;

        this.isSettlingScroll = false;

        // No Scrolled event while settling means the list never moved - everything fits, or it was
        // already where it was asked to be - so the state set by the scroll request still stands.
        if (this.lastVisibleIndex >= 0)
            this.ApplyScrollPosition();
    }
}

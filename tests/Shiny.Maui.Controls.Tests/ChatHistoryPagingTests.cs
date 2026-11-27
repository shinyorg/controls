using System.Reflection;
using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.Chat;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// Switching conversations while an older-history page is still in flight let the late answer land
/// in the new conversation: the previous chat's messages were prepended to it, and its HasMore
/// overwrote the new one's, which could stop history loading for good.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class ChatHistoryPagingTests
{
    public ChatHistoryPagingTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }


    static T Field<T>(ChatView chat, string name) => (T)typeof(ChatView)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(chat)!;


    [Fact]
    public async Task ALateHistoryPageFromThePreviousConversationIsDropped()
    {
        var a = new PagingSession("a", "a1", "a2");
        var b = new PagingSession("b", "b1", "b2");
        var chat = new ChatView { Provider = new Provider(a, b), SessionId = "a" };

        chat.Items.Select(m => m.MessageId).ShouldBe(["a1", "a2"]);

        chat.LoadOlder();
        await a.OlderRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

        chat.SessionId = "b";
        chat.Items.Select(m => m.MessageId).ShouldBe(["b1", "b2"]);

        a.ReleaseOlder(new MessagePage([PagingSession.Message("a0", 60)], false));
        await Task.Delay(50);

        chat.Items.Select(m => m.MessageId).ShouldBe(["b1", "b2"], "a's history must not reach b");
        Field<bool>(chat, "hasMoreOlder").ShouldBeTrue("a's HasMore=false must not end b's paging");
        Field<bool>(chat, "isLoadingOlder").ShouldBeFalse();

        // And b can page straight away rather than waiting on a's request.
        chat.LoadOlder();
        b.OlderRequestCount.ShouldBe(1);
    }


    static void Invoke(ChatView chat, string name, params object?[] args) => typeof(ChatView)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(chat, args);

    static void SetField(ChatView chat, string name, object value) => typeof(ChatView)
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
        .SetValue(chat, value);


    /// <summary>
    /// The chat often opened in the middle of the conversation. Before the opening scroll to the newest
    /// message had landed, the list reported the position it was drawn at - the top of the first page -
    /// and that report paged in older history, re-anchored on it, and dropped auto-follow.
    /// </summary>
    [Fact]
    public void AScrollReportedWhileTheOpeningScrollLandsDoesNotPageOrDropAutoFollow()
    {
        var session = new PagingSession("a", "a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8");
        var chat = new ChatView { Provider = new Provider(session), SessionId = "a" };
        Field<bool>(chat, "isSettlingScroll").ShouldBeFalse("the opening scroll must settle, not leave the flag up");

        // as it is while the retries are still running on a real dispatcher
        SetField(chat, "isSettlingScroll", true);
        Invoke(chat, "OnCollectionViewScrolled", null, new ItemsViewScrolledEventArgs { FirstVisibleItemIndex = 0, LastVisibleItemIndex = 3 });

        session.OlderRequestCount.ShouldBe(0, "the top-of-list report came from the unsettled layout");
        Field<bool>(chat, "isNearBottom").ShouldBeTrue();
        chat.Items.Select(m => m.MessageId).Last().ShouldBe("a8");

        // the scroll lands at the newest message; settling evaluates where it ended up
        Invoke(chat, "OnCollectionViewScrolled", null, new ItemsViewScrolledEventArgs { FirstVisibleItemIndex = 4, LastVisibleItemIndex = 7 });
        Invoke(chat, "EndSettle");

        Field<bool>(chat, "isSettlingScroll").ShouldBeFalse();
        Field<bool>(chat, "isNearBottom").ShouldBeTrue();
        session.OlderRequestCount.ShouldBe(0);
    }


    [Fact]
    public void ASettleWithNoScrollReportKeepsAutoFollow()
    {
        // everything fits on screen, so the list never moves and never reports a position
        var session = new PagingSession("a", "a1", "a2", "a3");
        var chat = new ChatView { Provider = new Provider(session), SessionId = "a" };

        SetField(chat, "isSettlingScroll", true);
        SetField(chat, "lastVisibleIndex", -1);
        Invoke(chat, "EndSettle");

        Field<bool>(chat, "isNearBottom").ShouldBeTrue("no report is not the same as being scrolled away");
        session.OlderRequestCount.ShouldBe(0);
    }


    [Fact]
    public void OnceSettledAScrollToTheTopStillPagesOlderHistory()
    {
        var session = new PagingSession("a", "a1", "a2", "a3", "a4", "a5", "a6", "a7", "a8");
        var chat = new ChatView { Provider = new Provider(session), SessionId = "a" };

        Invoke(chat, "OnCollectionViewScrolled", null, new ItemsViewScrolledEventArgs { FirstVisibleItemIndex = 0, LastVisibleItemIndex = 3 });

        session.OlderRequestCount.ShouldBe(1);
        Field<bool>(chat, "isNearBottom").ShouldBeFalse();
    }


    /// <summary>
    /// Answers the first (cursor-less) page at once and holds every older-history page until the test
    /// releases it.
    /// </summary>
    sealed class PagingSession(string sessionId, params string[] initialIds) : IChatSession
    {
        readonly TaskCompletionSource<MessagePage> olderGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource OlderRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int OlderRequestCount;

        public void ReleaseOlder(MessagePage page) => this.olderGate.TrySetResult(page);

        public static ChatMessage Message(string id, int minutesAgo) => new(
            id, null, "them", id, null, MessageStatus.Sent, null,
            DateTimeOffset.UtcNow.AddMinutes(-minutesAgo), null, [], []);

        public ChatSessionInfo Info { get; } = new(
            sessionId, sessionId, [], null, MessageBodyPermissions.None, ChatSessionPermissions.None,
            DateTimeOffset.UtcNow, null, 0);

        public string CurrentUserId => "me";

        public Task<MessagePage> GetMessagesAsync(string? cursorMessageId, MessagePageDirection direction, int count, CancellationToken cancellationToken = default)
        {
            if (cursorMessageId is null)
                return Task.FromResult(new MessagePage(initialIds.Select((id, i) => Message(id, initialIds.Length - i)).ToList(), true));

            Interlocked.Increment(ref this.OlderRequestCount);
            this.OlderRequested.TrySetResult();

            // Deliberately ignores the token - a provider is not obliged to honour it, and the late
            // answer is exactly what the view has to survive.
            return this.olderGate.Task;
        }

        public Task<ChatMessage> SendMessageAsync(OutgoingMessage message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ChatMessage> ResendMessageAsync(string clientMessageId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task EditMessageAsync(string messageId, string body, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteMessageAsync(string messageId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ReactToMessageAsync(string messageId, string emoji, bool add, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkReadAsync(string[] messageIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ToggleTypingAsync(bool isTyping, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task InviteUserAsync(string userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LeaveAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RenameAsync(string sessionName, CancellationToken cancellationToken = default) => Task.CompletedTask;

#pragma warning disable CS0067
        public event EventHandler<ChatMessage>? MessageReceived;
        public event EventHandler<MessageChanged>? MessageUpdated;
        public event EventHandler<string>? MessageDeleted;
        public event EventHandler<UserTypingEvent>? UserTyping;
        public event EventHandler<ChatSessionUserInfo>? UserJoined;
        public event EventHandler<ChatSessionUserInfo>? UserLeft;
        public event EventHandler<ChatSessionInfo>? SessionUpdated;
        public event EventHandler<ChatConnectionState>? ConnectionStateChanged;
#pragma warning restore CS0067

        public ValueTask DisposeAsync() => default;
    }


    sealed class Provider(params PagingSession[] sessions) : IChatSessionProvider
    {
        public Task<IChatSession> CreateSessionAsync(string[] userIds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IChatSession> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<IChatSession>(sessions.Single(s => s.Info.SessionId == sessionId));
    }
}

#pragma warning disable BL0006 // RenderTree types: a minimal interactive renderer is the only way to reach OnAfterRender
using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Shiny.Blazor.Controls.Chat;
using Shiny.Blazor.Controls.Docking;
using Shiny.Blazor.Controls.FileDrop;
using Shiny.Blazor.Controls.Gantt;
using Shiny.Blazor.Controls.OnScreenKeyboard;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// A component imports its JS module in OnAfterRenderAsync, and the host can dispose it while that
/// import is still in flight - a route change, an @if flipping. DisposeAsync then found no module
/// and released nothing, and the continuation went on to call init: a fresh DotNetObjectReference
/// handed to the browser (tracked, so the component is rooted for the life of the circuit or page),
/// plus whatever window/document listeners the module wires. Nothing throws, so nothing shows it.
/// </summary>
public class LateImportLeakTests
{
    public static TheoryData<Type> Components =>
    [
        typeof(RangeSlider),
        typeof(Slider),
        typeof(ColorPicker),
        typeof(ImageViewer),
        typeof(SheetView),
        typeof(ZoomPanView),
        typeof(SignaturePad),
        typeof(MediaPickerButton),
        typeof(AppLayout),
        typeof(Ribbon),
        typeof(GanttView),
        typeof(Tooltip),
        typeof(Walkthrough),
        typeof(OnScreenKeyboardHost),
        typeof(DockHost),
        typeof(ChatView)
    ];


    [Theory]
    [MemberData(nameof(Components))]
    public async Task AnImportThatLandsAfterDisposeIsReleasedNotInitialised(Type componentType)
    {
        var js = new GatedJs();
        var renderer = new TestRenderer(Services(js));

        await renderer.Dispatcher.InvokeAsync(() =>
        {
            var component = renderer.InstantiateComponent(componentType);
            var id = renderer.AssignRootComponentId(component);
            _ = renderer.RenderRootComponentAsync(id, ParameterView.Empty);
        });

        // Stuck in OnAfterRenderAsync, waiting on the import.
        await js.ImportRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await renderer.DisposeAsync();

        var module = new RecordingModule();
        js.Release(module);

        await module.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        module.Calls.ShouldBeEmpty();
        module.ReferencesHanded.ShouldBe(0);
    }


    [Fact]
    public async Task ChatSessionThatOpensAfterDisposeIsNotSubscribed()
    {
        var js = new GatedJs();
        var provider = new GatedProvider();
        var renderer = new TestRenderer(Services(js));

        await renderer.Dispatcher.InvokeAsync(() =>
        {
            var component = renderer.InstantiateComponent(typeof(ChatView));
            var id = renderer.AssignRootComponentId(component);
            _ = renderer.RenderRootComponentAsync(id, ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ChatView.Provider)] = provider,
                [nameof(ChatView.SessionId)] = "s1"
            }));
        });

        await provider.Requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await renderer.DisposeAsync();

        var session = new CountingSession();
        provider.Release(session);

        await session.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.SubscriberCount.ShouldBe(0);
    }


    [Fact]
    public async Task FileDropStoppedDuringImportAttachesNothing()
    {
        var js = new GatedJs();
        var service = new FileDropService(js, new FileDropOptions());

        var start = service.StartAsync();
        await js.ImportRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await service.StopAsync();

        var module = new RecordingModule();
        js.Release(module);
        await start.WaitAsync(TimeSpan.FromSeconds(5));

        module.Calls.ShouldBeEmpty();
        module.Disposed.Task.IsCompleted.ShouldBeTrue();
        service.IsRunning.ShouldBeFalse();
    }


    [Fact]
    public async Task OverlappingFileDropStartsShareOneImport()
    {
        var js = new GatedJs();
        var service = new FileDropService(js, new FileDropOptions());

        var first = service.StartAsync();
        var second = service.StartAsync();

        var module = new RecordingModule();
        js.Release(module);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));

        js.ImportCount.ShouldBe(1);
        module.ReferencesHanded.ShouldBe(1);

        await service.StopAsync();
    }


    static ServiceProvider Services(IJSRuntime js)
    {
        var services = new ServiceCollection()
            .AddSingleton(js)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddShinyControls();
        return services.BuildServiceProvider();
    }


    sealed class TestRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

        public new IComponent InstantiateComponent(Type type) => base.InstantiateComponent(type);

        public new int AssignRootComponentId(IComponent component) => base.AssignRootComponentId(component);

        public new Task RenderRootComponentAsync(int id, ParameterView parameters)
            => base.RenderRootComponentAsync(id, parameters);

        protected override void HandleException(Exception exception)
        {
            // A disposed component's continuation may still fault on its way out; that is not what
            // these tests are about.
        }

        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
    }


    sealed class GatedJs : IJSRuntime
    {
        readonly TaskCompletionSource<IJSObjectReference> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ImportRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ImportCount;

        public void Release(IJSObjectReference module) => this.gate.TrySetResult(module);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier != "import")
                return default;

            Interlocked.Increment(ref this.ImportCount);
            this.ImportRequested.TrySetResult();
            return new ValueTask<TValue>(this.Await<TValue>());
        }

        async Task<TValue> Await<TValue>() => (TValue)await this.gate.Task;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => this.InvokeAsync<TValue>(identifier, args);
    }


    sealed class RecordingModule : IJSObjectReference
    {
        public List<string> Calls { get; } = new();

        public int ReferencesHanded { get; private set; }

        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            this.Calls.Add(identifier);
            this.ReferencesHanded += args?.Count(a => a is not null && a.GetType().IsGenericType &&
                a.GetType().GetGenericTypeDefinition() == typeof(DotNetObjectReference<>)) ?? 0;
            return default;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => this.InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync()
        {
            this.Disposed.TrySetResult();
            return default;
        }
    }


    sealed class GatedProvider : IChatSessionProvider
    {
        readonly TaskCompletionSource<IChatSession> gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release(IChatSession session) => this.gate.TrySetResult(session);

        public Task<IChatSession> CreateSessionAsync(string[] userIds, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IChatSession> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            this.Requested.TrySetResult();
            return this.gate.Task;
        }
    }


    sealed class CountingSession : IChatSession
    {
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SubscriberCount =>
            Count(this.MessageReceived) + Count(this.MessageUpdated) + Count(this.MessageDeleted) +
            Count(this.UserTyping) + Count(this.UserJoined) + Count(this.UserLeft) +
            Count(this.SessionUpdated) + Count(this.ConnectionStateChanged);

        static int Count(Delegate? d) => d?.GetInvocationList().Length ?? 0;

        public ChatSessionInfo Info => throw new NotSupportedException();
        public string CurrentUserId => "me";

        public Task<MessagePage> GetMessagesAsync(string? cursorMessageId, MessagePageDirection direction, int count, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
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

        public event EventHandler<ChatMessage>? MessageReceived;
        public event EventHandler<MessageChanged>? MessageUpdated;
        public event EventHandler<string>? MessageDeleted;
        public event EventHandler<UserTypingEvent>? UserTyping;
        public event EventHandler<ChatSessionUserInfo>? UserJoined;
        public event EventHandler<ChatSessionUserInfo>? UserLeft;
        public event EventHandler<ChatSessionInfo>? SessionUpdated;
        public event EventHandler<ChatConnectionState>? ConnectionStateChanged;

        public ValueTask DisposeAsync()
        {
            this.Disposed.TrySetResult();
            return default;
        }
    }
}

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Shiny.Blazor.Controls.Camera;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Camera.Tests;

/// <summary>
/// The camera is hardware: a disposed view must release it even when <c>start</c> has not returned yet (the
/// permission prompt can hold it open indefinitely), and must never begin a start after it is gone.
/// </summary>
public class CameraViewDisposalTests
{
    [Fact]
    public async Task DisposeReleasesTheCameraEvenBeforeStartReturns()
    {
        var js = new RecordingJSRuntime(gateImport: false, gateStart: true);
        var (host, view) = await MountAsync(js);
        using var _ = host;

        await Task.Delay(50);
        js.Module.Calls.ShouldContain("start");   // parked on getUserMedia

        await view.DisposeAsync();

        js.Module.Calls.ShouldContain("dispose");
        js.Module.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task DisposedDuringModuleImportNeverStarts()
    {
        var js = new RecordingJSRuntime(gateImport: true, gateStart: false);
        var (host, view) = await MountAsync(js);
        using var _ = host;

        await view.DisposeAsync();
        js.Release();
        await Task.Delay(50);

        js.Module.Calls.ShouldNotContain("start");
        js.Module.Disposed.ShouldBeTrue();
    }

    static async Task<(Host, CameraView)> MountAsync(RecordingJSRuntime js)
    {
        var services = new ServiceCollection().AddSingleton<IJSRuntime>(js).BuildServiceProvider();
        var host = new Host(services);
        var view = (CameraView)host.Instantiate(typeof(CameraView));
        var id = host.Assign(view);
        _ = host.Dispatcher.InvokeAsync(() => host.Render(id, ParameterView.Empty));
        await Task.Delay(20);
        return (host, view);
    }

    sealed class Host(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public IComponent Instantiate(Type type) => this.InstantiateComponent(type);
        public int Assign(IComponent component) => this.AssignRootComponentId(component);
        public Task Render(int id, ParameterView parameters) => this.RenderRootComponentAsync(id, parameters);
        protected override void HandleException(Exception exception) { }
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
    }

    sealed class RecordingJSRuntime(bool gateImport, bool gateStart) : IJSRuntime
    {
        readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RecordingModule Module { get; } = new(gateStart);

        public void Release() => this.gate.TrySetResult();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => this.InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier != "import")
                return default!;

            if (gateImport)
                await this.gate.Task;

            return (TValue)(object)this.Module;
        }
    }

    sealed class RecordingModule(bool gateStart) : IJSObjectReference
    {
        readonly TaskCompletionSource never = new();
        readonly object sync = new();
        readonly List<string> calls = [];

        public IReadOnlyList<string> Calls
        {
            get
            {
                lock (this.sync)
                    return this.calls.ToList();
            }
        }

        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            this.Disposed = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => this.InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            lock (this.sync)
                this.calls.Add(identifier);

            if (gateStart && identifier == "start")
                await this.never.Task;

            return default!;
        }
    }
}

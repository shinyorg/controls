using System.Collections.ObjectModel;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls.Diagram.Tests;

/// <summary>
/// A view disposed while its JS module is still loading must not attach afterwards: attach adds window
/// scroll/resize listeners and a ResizeObserver that only detach removes, and detach would never come.
/// </summary>
public class DiagramViewDisposalTests
{
    [Fact]
    public async Task DisposedDuringModuleImportNeverAttaches()
    {
        var js = new GatedJSRuntime();
        var services = new ServiceCollection().AddSingleton<IJSRuntime>(js).BuildServiceProvider();
        using var host = new Host(services);

        var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(DiagramView.Nodes)] = new ObservableCollection<DiagramNode>(),
            [nameof(DiagramView.Connections)] = new ObservableCollection<DiagramConnection>()
        });

        var view = (DiagramView)host.Instantiate(typeof(DiagramView));
        var id = host.Assign(view);
        await host.Dispatcher.InvokeAsync(() => host.Render(id, parameters));

        // First render has started the import and is parked on it.
        js.ImportRequested.ShouldBeTrue();

        await view.DisposeAsync();
        js.ReleaseImport();
        await Task.Delay(50);

        js.Module.Calls.ShouldNotContain("attach");
        js.Module.Disposed.ShouldBeTrue();
    }

    sealed class Host(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public IComponent Instantiate(Type type) => this.InstantiateComponent(type);
        public int Assign(IComponent component) => this.AssignRootComponentId(component);
        public Task Render(int id, ParameterView parameters) => this.RenderRootComponentAsync(id, parameters);
        protected override void HandleException(Exception exception) => throw exception;
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
    }

    sealed class GatedJSRuntime : IJSRuntime
    {
        readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RecordingModule Module { get; } = new();
        public bool ImportRequested { get; private set; }
        public void ReleaseImport() => this.gate.TrySetResult();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => this.InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "import")
            {
                this.ImportRequested = true;
                await this.gate.Task;
                return (TValue)(object)this.Module;
            }
            return default!;
        }
    }

    sealed class RecordingModule : IJSObjectReference
    {
        public List<string> Calls { get; } = [];
        public bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            this.Disposed = true;
            return ValueTask.CompletedTask;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            this.Calls.Add(identifier);
            return ValueTask.FromResult<TValue>(default!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => this.InvokeAsync<TValue>(identifier, args);
    }
}

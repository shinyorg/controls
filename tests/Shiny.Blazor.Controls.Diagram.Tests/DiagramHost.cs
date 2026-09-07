using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls.Diagram.Tests;

/// <summary>
/// The smallest renderer that will run a component's real lifecycle and hand back the instance.
/// </summary>
/// <remarks>
/// <para>
/// A component needs a render handle before <c>StateHasChanged</c> is legal, and a handle can only
/// come from a renderer - constructing one by reflection produces an uninitialized struct that throws
/// "the render handle is not yet assigned" from inside the framework, which reads like a bug in the
/// component. <c>HtmlRenderer</c>, which the other Blazor tests here use, renders to a string and
/// never exposes the instance, and these tests need to keep setting parameters on the same one.
/// </para>
/// <para>
/// Nothing is displayed: <c>UpdateDisplayAsync</c> discards the batch. What is being tested is what
/// the component does during its lifecycle, not the markup it produces.
/// </para>
/// </remarks>
sealed class DiagramHost : Renderer
{
    DiagramHost(IServiceProvider services) : base(services, NullLoggerFactory.Instance)
    {
    }

    /// <inheritdoc />
    public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

    /// <summary>Creates a host with the services a <see cref="DiagramView"/> injects.</summary>
    public static DiagramHost Create()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IJSRuntime>(new StubJSRuntime());

        return new DiagramHost(services.BuildServiceProvider());
    }

    /// <summary>
    /// Creates a component, runs its first lifecycle pass, and hands it back.
    /// </summary>
    /// <typeparam name="T">The component type.</typeparam>
    /// <param name="parameters">Its initial parameters.</param>
    /// <remarks>
    /// The renderer has to be the one that creates it: <c>[Inject]</c> properties are filled during
    /// construction by the renderer's component factory, and an instance handed in from outside is
    /// simply never injected - which surfaces later as a null <c>IJSRuntime</c> inside the component
    /// rather than as anything pointing at the harness.
    /// </remarks>
    public async Task<T> MountAsync<T>(ParameterView parameters) where T : IComponent
    {
        var component = (T)this.InstantiateComponent(typeof(T));
        var id = this.AssignRootComponentId(component);

        await this.Dispatcher.InvokeAsync(() => this.RenderRootComponentAsync(id, parameters));
        return component;
    }

    /// <summary>Sets parameters again, the way a parent re-render would.</summary>
    /// <param name="component">The mounted component.</param>
    /// <param name="parameters">The parameters to apply.</param>
    public Task SetParametersAsync(IComponent component, ParameterView parameters) =>
        this.Dispatcher.InvokeAsync(() => component.SetParametersAsync(parameters));

    /// <inheritdoc />
    protected override void HandleException(Exception exception) =>
        throw new InvalidOperationException("The component threw during rendering.", exception);

    /// <inheritdoc />
    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;

    /// <summary>
    /// Stands in for the browser. The component imports its JS module on first render and calls into
    /// it for measurement and pointer capture; there is no browser here, so every call is a no-op.
    /// </summary>
    sealed class StubJSRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult(Result<TValue>());

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => ValueTask.FromResult(Result<TValue>());

        static TValue Result<TValue>() =>
            typeof(TValue) == typeof(IJSObjectReference)
                ? (TValue)(object)new StubModule()
                : default!;
    }

    sealed class StubModule : IJSObjectReference
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult<TValue>(default!);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => ValueTask.FromResult<TValue>(default!);
    }
}

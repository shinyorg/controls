#pragma warning disable BL0006 // RenderTree types: a minimal interactive renderer is the only way to reach OnAfterRender
using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// The sheet is moved by sheet.js, so what matters is which calls reach it. Every close has to reach
/// <c>close</c> - including one that starts inside the sheet (a backdrop tap or a swipe), whose new state
/// is already recorded by the time the parent hands the same value back.
/// </summary>
public class SheetViewTests
{
    [Fact]
    public async Task ClosingFromInsideTheSheetMovesIt()
    {
        var (renderer, js, sheet, id) = await RenderOpenSheet();

        // What a backdrop tap and a swipe past the lowest detent both call.
        await renderer.Dispatcher.InvokeAsync(() => sheet.OnOpenChanged(false));

        js.Module.Calls.Count(c => c == "close").ShouldBe(1);
    }


    [Fact]
    public async Task ParentHandingBackTheClosedStateDoesNotCloseTwice()
    {
        var (renderer, js, sheet, id) = await RenderOpenSheet();

        await renderer.Dispatcher.InvokeAsync(() => sheet.OnOpenChanged(false));
        await renderer.Dispatcher.InvokeAsync(() => renderer.Render(id, open: false));

        js.Module.Calls.Count(c => c == "close").ShouldBe(1);
    }


    [Fact]
    public async Task ClosingFromTheParentMovesIt()
    {
        var (renderer, js, _, id) = await RenderOpenSheet();

        await renderer.Dispatcher.InvokeAsync(() => renderer.Render(id, open: false));

        js.Module.Calls.Count(c => c == "close").ShouldBe(1);
    }


    [Fact]
    public async Task ClosingFromInsideTheSheetReportsThroughTheBinding()
    {
        var states = new List<bool>();
        var (renderer, _, sheet, _) = await RenderOpenSheet(states.Add);

        await renderer.Dispatcher.InvokeAsync(() => sheet.OnOpenChanged(false));

        states.ShouldBe([false]);
        sheet.IsOpen.ShouldBeFalse();
    }


    static async Task<(TestRenderer Renderer, RecordingJs Js, SheetView Sheet, int Id)> RenderOpenSheet(Action<bool>? changed = null)
    {
        var js = new RecordingJs();
        var services = new ServiceCollection()
            .AddSingleton<IJSRuntime>(js)
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();

        var renderer = new TestRenderer(services, changed);
        SheetView sheet = null!;
        var id = 0;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            // Through the renderer, so the component gets its IJSRuntime the way a hosted one does.
            (id, sheet) = renderer.Attach();
            await renderer.Render(id, open: true);
        });

        js.Module.Calls.ShouldContain("open");
        return (renderer, js, sheet, id);
    }


    sealed class TestRenderer(IServiceProvider services, Action<bool>? changed)
        : Renderer(services, NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

        public (int Id, SheetView Sheet) Attach()
        {
            var sheet = (SheetView)this.InstantiateComponent(typeof(SheetView));
            return (this.AssignRootComponentId(sheet), sheet);
        }

        public Task Render(int id, bool open)
        {
            var parameters = new Dictionary<string, object?>
            {
                [nameof(SheetView.IsOpen)] = open,
                [nameof(SheetView.IsOpenChanged)] = EventCallback.Factory.Create<bool>(new object(), v => changed?.Invoke(v))
            };
            return this.RenderRootComponentAsync(id, ParameterView.FromDictionary(parameters));
        }

        protected override void HandleException(Exception exception)
            => ExceptionDispatchInfo.Capture(exception).Throw();

        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
    }


    sealed class RecordingJs : IJSRuntime
    {
        public RecordingModule Module { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => identifier == "import"
                ? ValueTask.FromResult((TValue)(object)this.Module)
                : default;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => this.InvokeAsync<TValue>(identifier, args);
    }


    sealed class RecordingModule : IJSObjectReference
    {
        public List<string> Calls { get; } = new();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            this.Calls.Add(identifier);
            return default;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => this.InvokeAsync<TValue>(identifier, args);

        public ValueTask DisposeAsync() => default;
    }
}

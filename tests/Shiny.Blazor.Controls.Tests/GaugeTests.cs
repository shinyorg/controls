using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Shiny.Controls.Gauge;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// The Blazor gauge as a static render - which is also what a prerendered or static-SSR page shows
/// before (or without) gauge.js, so the needle must already be at the value.
/// </summary>
public class GaugeTests
{
    [Fact]
    public async Task AStaticRenderDrawsTheNeedleAtTheValue()
    {
        var html = await RenderAsync(b =>
        {
            b.OpenComponent<Gauge>(0);
            b.AddAttribute(1, nameof(Gauge.Value), 50.0);
            b.CloseComponent();
        });

        // 50 of 0-100 on the default -135..135 dial is straight up.
        html.ShouldContain("class=\"shiny-gauge-needle\" transform=\"rotate(0)\"");
        html.ShouldContain(">50</text>");
    }


    [Fact]
    public async Task ItIsAnAccessibleMeter()
    {
        var html = await RenderAsync(b =>
        {
            b.OpenComponent<Gauge>(0);
            b.AddAttribute(1, nameof(Gauge.Value), 88.0);
            b.AddAttribute(2, nameof(Gauge.Maximum), 240.0);
            b.AddAttribute(3, nameof(Gauge.Units), "km/h");
            b.AddAttribute(4, nameof(Gauge.Title), "Speed");
            b.CloseComponent();
        });

        html.ShouldContain("role=\"meter\"");
        html.ShouldContain("aria-valuemax=\"240\"");
        html.ShouldContain("aria-valuenow=\"88\"");
        html.ShouldContain("aria-valuetext=\"88 km/h\"");
        html.ShouldContain("aria-label=\"Speed\"");
    }


    [Fact]
    public async Task ValuesAreClampedToTheScale()
    {
        var html = await RenderAsync(b =>
        {
            b.OpenComponent<Gauge>(0);
            b.AddAttribute(1, nameof(Gauge.Value), 500.0);
            b.CloseComponent();
        });

        html.ShouldContain("aria-valuenow=\"100\"");
        html.ShouldContain("transform=\"rotate(135)\"");
    }


    [Fact]
    public async Task TheArcTakesTheColourOfTheRangeTheNeedleIsIn()
    {
        var html = await RenderAsync(b =>
        {
            b.OpenComponent<Gauge>(0);
            b.AddAttribute(1, nameof(Gauge.Value), 7.0);
            b.AddAttribute(2, nameof(Gauge.Maximum), 8.0);
            b.AddAttribute(3, nameof(Gauge.Ranges), new List<GaugeRange> { new(6.5, 8, "crimson") });
            b.CloseComponent();
        });

        html.ShouldContain("class=\"shiny-gauge-range\"");
        html.ShouldContain("stroke: crimson;");
    }


    [Fact]
    public async Task NoNeedleStyleLeavesARing()
    {
        var html = await RenderAsync(b =>
        {
            b.OpenComponent<Gauge>(0);
            b.AddAttribute(1, nameof(Gauge.NeedleStyle), GaugeNeedleStyle.None);
            b.CloseComponent();
        });

        html.ShouldNotContain("shiny-gauge-needle");
        html.ShouldContain("shiny-gauge-value-arc");
    }


    [Fact]
    public async Task ACallersStyleIsAppendedNotSubstituted()
    {
        var html = await RenderAsync(b =>
        {
            b.OpenComponent<Gauge>(0);
            b.AddAttribute(1, nameof(Gauge.NeedleColor), "orange");
            b.AddAttribute(2, "style", "margin: 4px;");
            b.CloseComponent();
        });

        html.ShouldContain("style=\"--shiny-gauge-needle: orange;margin: 4px;\"");
    }


    [Fact]
    public void AFullTurnArcIsTwoHalves()
        => Gauge.Arc(50, 0, 360).Split(" A ").Length.ShouldBe(3);


    [Fact]
    public void ALongArcSetsTheLargeArcFlag()
    {
        Gauge.Arc(50, -135, 135).ShouldContain(" 0 1 1 ");
        Gauge.Arc(50, -90, 0).ShouldContain(" 0 0 1 ");
    }


    static async Task<string> RenderAsync(Action<RenderTreeBuilder> content)
    {
        var services = new ServiceCollection()
            .AddSingleton<IJSRuntime, NoJSRuntime>()
            .BuildServiceProvider();

        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderComponentAsync<FragmentHost>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(FragmentHost.Content)] = (RenderFragment)(b => content(b)) }));
            await root.QuiescenceTask;
            return System.Net.WebUtility.HtmlDecode(root.ToHtmlString());
        });
    }


    sealed class FragmentHost : ComponentBase
    {
        [Parameter] public RenderFragment? Content { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, Content);
    }


    /// <summary>A static render never reaches OnAfterRender, so nothing here is ever called.</summary>
    sealed class NoJSRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => throw new InvalidOperationException("No JS in a static render.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => throw new InvalidOperationException("No JS in a static render.");
    }
}

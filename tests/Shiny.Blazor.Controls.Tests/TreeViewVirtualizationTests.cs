using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// Virtualize="true" renders the rows as a flat list through &lt;Virtualize&gt;, so only a window of
/// rows reaches the DOM, and the hierarchy moves into aria-level / setsize / posinset.
/// </summary>
public class TreeViewVirtualizationTests
{
    record Node(string Name);

    sealed class NoJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => default;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => default;
    }

    static readonly List<Node> Roots = Enumerable.Range(0, 10_000).Select(i => new Node($"node{i}")).ToList();

    static async Task<string> RenderAsync(bool virtualize)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IJSRuntime>(new NoJs());
        var provider = services.BuildServiceProvider();

        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(TreeView<Node>.ItemsSource)] = Roots,
                [nameof(TreeView<Node>.Virtualize)] = virtualize,
                [nameof(TreeView<Node>.RowHeight)] = 28f
            });
            var output = await renderer.RenderComponentAsync<TreeView<Node>>(parameters);
            return output.ToHtmlString();
        });
    }

    static int CountTreeItems(string html) => Regex.Matches(html, "role=\"treeitem\"").Count;

    [Fact]
    public async Task WithoutVirtualize_EveryRowIsRendered()
    {
        var html = await RenderAsync(virtualize: false);
        CountTreeItems(html).ShouldBe(10_000);
    }

    [Fact]
    public async Task WithVirtualize_NoRowsArePrerendered_ButTheFullExtentIsReserved()
    {
        // <Virtualize> needs the browser to measure the viewport before it renders any rows (the
        // windowing itself is covered by the Playwright test); statically it only reserves the
        // scroll extent: every visible row at RowHeight.
        var html = await RenderAsync(virtualize: true);

        CountTreeItems(html).ShouldBe(0);
        html.ShouldContain($"data-blazor-virtualize-reserved-height=\"{10_000 * 28}\"");
        html.ShouldContain("shiny-treeview-virtual");
        html.ShouldContain("--shiny-treeview-row-height:28px");
        html.ShouldNotContain("role=\"group\"");
    }
}

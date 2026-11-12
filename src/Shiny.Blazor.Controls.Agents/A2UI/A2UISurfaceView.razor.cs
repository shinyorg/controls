using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Shiny.Controls.AgentUI.A2UI;

namespace Shiny.Blazor.Controls.Agents;

/// <summary>
/// Renders agent-described UI (A2UI v1.0) with plain HTML and the Shiny Blazor controls: the full basic
/// catalog plus the Shiny catalog (DataGrid, Gauge, ChipGroup, DateRangePicker and more).
/// </summary>
/// <remarks>
/// <para>
/// Give it a <see cref="Surface"/>, or a <see cref="Host"/> and optionally a <see cref="SurfaceId"/>
/// (no id draws every surface on the host in creation order, and picks up surfaces as they arrive).
/// </para>
/// <para>
/// Standalone, the view fills its parent and owns vertical scroll (<see cref="IsScrollEnabled"/>), so
/// the page must not wrap it in another scroller. <c>AgentView</c> turns scrolling off and the view sizes
/// to its content inside the conversation.
/// </para>
/// <para>
/// Surface changes re-render the view, coalesced to about 30 per second so a stream of data model
/// updates doesn't flood a Blazor Server circuit. Inputs write to the model on every keystroke and
/// keep their focus and caret across those renders.
/// </para>
/// </remarks>
public partial class A2UISurfaceView : IDisposable
{
    internal const int MaxDepth = 64;
    static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(33);

    readonly List<A2UISurface> subscribed = [];
    A2UISurfaceHost? subscribedHost;
    bool renderScheduled;
    bool disposed;

    [Inject] IJSRuntime JS { get; set; } = default!;

    /// <summary>The engine surface to draw. Takes precedence over <see cref="Host"/>.</summary>
    [Parameter] public A2UISurface? Surface { get; set; }

    /// <summary>A surface host: draws <see cref="SurfaceId"/>, or every surface on it when that is null.</summary>
    [Parameter] public A2UISurfaceHost? Host { get; set; }

    /// <summary>Which of <see cref="Host"/>'s surfaces to draw. Null draws all of them in creation order.</summary>
    [Parameter] public string? SurfaceId { get; set; }

    /// <summary>Component type to renderer. Defaults to the basic and Shiny catalogs (<see cref="A2UIRendererRegistry.CreateDefault"/>).</summary>
    [Parameter] public A2UIRendererRegistry? Renderers { get; set; }

    /// <summary>
    /// True (the default) when the view fills its parent and scrolls vertically. False sizes the view to
    /// its content and never scrolls, for hosting inside another scroller such as a conversation.
    /// </summary>
    [Parameter] public bool IsScrollEnabled { get; set; } = true;

    /// <summary>Whether an outermost Card draws its edge. Turn it off when the host already draws one, so the two don't double up.</summary>
    [Parameter] public bool ShowBorder { get; set; } = true;

    /// <summary>The landmark's accessible name.</summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Extra CSS classes on the root element.</summary>
    [Parameter] public string? CssClass { get; set; }

    internal A2UIRendererRegistry EffectiveRenderers => Renderers ?? A2UIRendererRegistry.Default;

    string RootClass
    {
        get
        {
            var c = "a2ui-view";
            c += IsScrollEnabled ? " a2ui-view--scroll" : " a2ui-view--fit";
            if (!ShowBorder)
                c += " a2ui-view--borderless";
            if (!string.IsNullOrWhiteSpace(CssClass))
                c += " " + CssClass;
            return c;
        }
    }

    IEnumerable<A2UISurface> VisibleSurfaces()
    {
        if (Surface is not null)
            return Surface.IsDeleted ? [] : [Surface];
        if (Host is null)
            return [];
        if (SurfaceId is not null)
            return Host.GetSurface(SurfaceId) is { } s ? [s] : [];
        return Host.Surfaces.Values;
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var host = Surface?.Host ?? Host;
        if (host is not null && host.OpenUrl is null)
        {
            // openUrl only ever hands us http(s) inside a user gesture; a new tab keeps the app where it is
            host.OpenUrl = uri => _ = JS.InvokeVoidAsync("open", uri.ToString(), "_blank", "noopener,noreferrer").AsTask();
        }
        if (host is not null && host.BaseUri is null && NavigationBase() is { } baseUri)
            host.BaseUri = baseUri;

        Resubscribe();
    }

    [Inject] NavigationManager? Navigation { get; set; }

    Uri? NavigationBase()
    {
        try
        {
            return Navigation is null ? null : new Uri(Navigation.BaseUri);
        }
        catch (InvalidOperationException)
        {
            // not initialised (prerendering outside a router)
            return null;
        }
    }

    void Resubscribe()
    {
        var wanted = VisibleSurfaces().ToList();
        var host = Surface is null ? Host : null;

        foreach (var s in subscribed.Except(wanted).ToList())
        {
            s.Changed -= OnSurfaceChanged;
            subscribed.Remove(s);
        }
        foreach (var s in wanted.Except(subscribed).ToList())
        {
            s.Changed += OnSurfaceChanged;
            subscribed.Add(s);
        }

        if (!ReferenceEquals(host, subscribedHost))
        {
            if (subscribedHost is not null)
            {
                subscribedHost.SurfaceCreated -= OnHostSurfacesChanged;
                subscribedHost.SurfaceDeleted -= OnHostSurfacesChanged;
            }
            subscribedHost = host;
            if (host is not null)
            {
                host.SurfaceCreated += OnHostSurfacesChanged;
                host.SurfaceDeleted += OnHostSurfacesChanged;
            }
        }
    }

    void OnHostSurfacesChanged(object? sender, A2UISurface e)
    {
        Resubscribe();
        ScheduleRender();
    }

    void OnSurfaceChanged(object? sender, A2UISurfaceChange e) => ScheduleRender();

    // Coalesce: many changes inside one frame become one render.
    void ScheduleRender()
    {
        if (renderScheduled || disposed)
            return;
        renderScheduled = true;
        _ = RenderSoonAsync();
    }

    async Task RenderSoonAsync()
    {
        try
        {
            await Task.Delay(Frame);
            await InvokeAsync(() =>
            {
                renderScheduled = false;
                if (!disposed)
                    StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
        }
    }

    static bool IsCycle(A2UIRenderContext? parent, A2UIChild child)
    {
        for (var p = parent; p is not null; p = p.Parent)
        {
            if (p.Component.Id == child.Id && p.Scope.BasePath == child.Scope.BasePath)
                return true;
        }
        return false;
    }

    static string? SlotStyle(A2UIComponent component)
    {
        if (component["weight"] is not JsonValue v || A2UIValues.ToNumber(v) is not { } w || w <= 0)
            return null;
        // weight is the CSS flex-grow of the child within its Row or Column
        return "flex:" + w.ToString(CultureInfo.InvariantCulture) + " 1 0%;min-width:0";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        disposed = true;
        foreach (var s in subscribed)
            s.Changed -= OnSurfaceChanged;
        subscribed.Clear();
        if (subscribedHost is not null)
        {
            subscribedHost.SurfaceCreated -= OnHostSurfacesChanged;
            subscribedHost.SurfaceDeleted -= OnHostSurfacesChanged;
            subscribedHost = null;
        }
    }
}

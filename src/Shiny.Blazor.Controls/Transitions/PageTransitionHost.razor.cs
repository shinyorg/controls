using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

/// <summary>
/// Animates navigation between pages with the browser's View Transitions API. Wrap the router's
/// <c>@Body</c> in it (in the layout); each page then says how it arrives with a
/// <see cref="PageTransition"/>.
/// </summary>
/// <remarks>
/// <para>Navigation itself is untouched: <c>NavigationManager.NavigateTo</c>, links and the browser's
/// back and forward buttons all go through the router's location-changing hook, where the outgoing
/// page is captured before the router renders the incoming one. Going back to the previous page
/// counts as a pop, so it runs the page's transition in reverse.</para>
/// <para>Browsers without view transitions, and users who prefer reduced motion, navigate without
/// animation. Elements with a <c>data-shiny-hero="name"</c> attribute fly between pages that share the
/// name.</para>
/// </remarks>
public partial class PageTransitionHost
{
    [Inject] NavigationManager Nav { get; set; } = default!;
    [Inject] IJSRuntime JS { get; set; } = default!;

    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>The transition for pages that do not set one. Default means no animation.</summary>
    [Parameter] public PageTransitionType DefaultTransition { get; set; }

    /// <summary>How long a transition runs, unless the page sets its own.</summary>
    [Parameter] public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(320);

    [Parameter] public string? Class { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    IJSObjectReference? module;
    IDisposable? locationChanging;
    bool disposed;

    // The pages visited, so going back to the one before is recognised as a pop.
    readonly List<string> history = [];
    readonly List<PageTransition> registered = [];

    string? pendingTarget;
    string? pendingSource;
    bool pendingIsPop;
    PageTransition? pendingOutgoing;


    string MergedStyle
    {
        get
        {
            var own = "view-transition-name:shiny-page;";
            return this.AdditionalAttributes?.TryGetValue("style", out var style) == true
                ? own + style
                : own;
        }
    }

    IReadOnlyDictionary<string, object>? OtherAttributes
        => this.AdditionalAttributes?.Where(a => a.Key != "style").ToDictionary(a => a.Key, a => a.Value);


    internal void Register(PageTransition page) => this.registered.Add(page);
    internal void Unregister(PageTransition page) => this.registered.Remove(page);

    // The page showing is the one registered last; the outgoing page unregisters once the new one
    // has rendered, so this is the incoming page by the time the transition finishes.
    PageTransition? Current => this.registered.Count > 0 ? this.registered[^1] : null;


    protected override void OnInitialized()
    {
        this.history.Add(this.Nav.Uri);
        this.Nav.LocationChanged += this.OnLocationChanged;
    }


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var loaded = await this.JS.InvokeAsync<IJSObjectReference>("import", "./_content/Shiny.Blazor.Controls/page-transitions.js");
            if (this.disposed)
            {
                await loaded.ReleaseLateAsync();
                return;
            }
            this.module = loaded;
            this.locationChanging = this.Nav.RegisterLocationChangingHandler(this.OnLocationChanging);
            return;
        }

        if (this.pendingTarget is { } target && Same(this.Nav.Uri, target))
            await this.FinishAsync();
    }


    async ValueTask OnLocationChanging(LocationChangingContext context)
    {
        if (this.module is null)
            return;

        var target = this.Nav.ToAbsoluteUri(context.TargetLocation).ToString();
        var source = this.Nav.Uri;

        // A fragment jump on the same page is not a page change.
        if (Same(StripFragment(target), StripFragment(source)))
            return;

        this.pendingIsPop = this.history.Count >= 2 && Same(this.history[^2], target);
        this.pendingOutgoing = this.Current;
        this.pendingSource = source;

        try
        {
            // Resolves once the browser has captured the outgoing page; the navigation then goes
            // ahead and the incoming page is rendered underneath the captured picture.
            var started = await this.module.InvokeAsync<bool>("begin", context.CancellationToken);
            this.pendingTarget = started ? target : null;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException or OperationCanceledException)
        {
            this.pendingTarget = null;
        }
    }


    void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        var uri = e.Location;
        if (this.history.Count >= 2 && Same(this.history[^2], uri))
            this.history.RemoveAt(this.history.Count - 1);
        else if (this.history.Count == 0 || !Same(this.history[^1], uri))
            this.history.Add(uri);

        // The router re-renders the layout for the new page, which re-renders this host; if it did
        // not (the same component for a new route parameter, say) this makes sure it does.
        if (this.pendingTarget is not null)
            _ = this.InvokeAsync(this.StateHasChanged);
    }


    async Task FinishAsync()
    {
        var module = this.module;
        var target = this.pendingTarget!;
        this.pendingTarget = null;
        if (module is null)
            return;

        var isPop = this.pendingIsPop;
        var page = isPop ? this.pendingOutgoing : this.Current;
        var (type, x, y, duration) = Resolve(page?.Push ?? default, page?.Pop ?? default, page?.Direction ?? default, page?.Duration, isPop, this.DefaultTransition, this.Duration);

        try
        {
            // Primitives only: anonymous types and records do not survive trimming over interop.
            await module.InvokeVoidAsync("finish", type.ToString().ToLowerInvariant(), isPop, x, y, duration, this.pendingSource ?? "", target);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or TaskCanceledException)
        {
        }
    }


    /// <summary>
    /// What runs for a navigation: the page's own setting (its Pop on the way back, its Push
    /// otherwise, an unset Pop mirroring the Push), then the host's default. The vector points at the
    /// edge the incoming page comes from (+1 = right/bottom).
    /// </summary>
    internal static (PageTransitionType Type, int X, int Y, double DurationMs) Resolve(
        PageTransitionType push,
        PageTransitionType pop,
        PageTransitionDirection direction,
        TimeSpan? duration,
        bool isPop,
        PageTransitionType hostDefault,
        TimeSpan hostDuration)
    {
        var type = isPop && pop != PageTransitionType.Default ? pop : push;
        if (type == PageTransitionType.Default)
            type = hostDefault;
        if (type == PageTransitionType.NativeZoom)
            type = PageTransitionType.Zoom;

        if (direction == PageTransitionDirection.Default)
            direction = type == PageTransitionType.SharedAxisY ? PageTransitionDirection.FromBottom : PageTransitionDirection.FromRight;

        var (x, y) = direction switch
        {
            PageTransitionDirection.FromLeft => (-1, 0),
            PageTransitionDirection.FromBottom => (0, 1),
            PageTransitionDirection.FromTop => (0, -1),
            _ => (1, 0)
        };
        return (type, x, y, (duration ?? hostDuration).TotalMilliseconds);
    }


    static bool Same(string a, string b) => string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.Ordinal);

    static string StripFragment(string uri)
    {
        var hash = uri.IndexOf('#');
        return hash < 0 ? uri : uri[..hash];
    }


    public async ValueTask DisposeAsync()
    {
        this.disposed = true;
        this.Nav.LocationChanged -= this.OnLocationChanged;
        this.locationChanging?.Dispose();
        await this.module.ReleaseLateAsync();
    }
}

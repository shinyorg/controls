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
/// <para>Browsers without view transitions navigate without animation; users who prefer reduced
/// motion get <see cref="ReducedMotion"/> (no animation by default, or a crossfade). Elements with a
/// <c>data-shiny-hero="name"</c> attribute fly between pages that share the name.</para>
/// <para>A single navigation can carry its own transition - <c>Nav.NavigateTo(uri,
/// PageTransitionType.Cover)</c> or <see cref="NavigateTo(string, PageTransitionOverride, bool)"/> -
/// and going back from the page it opened reverses it. Custom transitions built from keyframes are
/// registered with <see cref="RegisterCustomTransition"/> and picked by name.</para>
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

    /// <summary>What happens when the user prefers reduced motion: no animation (the default), a crossfade, or the transitions anyway.</summary>
    [Parameter] public PageTransitionReducedMotion ReducedMotion { get; set; } = PageTransitionReducedMotion.Instant;

    [Parameter] public string? Class { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    IJSObjectReference? module;
    IDisposable? locationChanging;
    bool disposed;

    // The pages visited, so going back to the one before is recognised as a pop, and the one-off
    // transition (if any) each was opened with, so going back from it reverses that transition.
    readonly List<string> history = [];
    readonly List<PageTransitionOverride?> arrivals = [];
    readonly List<PageTransition> registered = [];
    readonly Dictionary<string, CustomPageTransitionDefinition> customs = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> definedInBrowser = new(StringComparer.OrdinalIgnoreCase);

    PageTransitionOverride? nextOverride;
    PageTransitionOverride? pendingOverride;
    PageTransitionOverride? arrivalOverride;

    string? pendingTarget;
    string? pendingSource;
    bool pendingIsPop;
    PageTransition? pendingOutgoing;


    internal void Register(PageTransition page) => this.registered.Add(page);
    internal void Unregister(PageTransition page) => this.registered.Remove(page);

    /// <summary>Makes a keyframe-built transition available by name, to pages (<c>PageTransition Custom="..."</c>) and to one-off navigations.</summary>
    public void RegisterCustomTransition(CustomPageTransitionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);
        this.customs[definition.Name] = definition;
        this.definedInBrowser.Remove(definition.Name);
    }

    /// <summary>Removes a custom transition.</summary>
    public void UnregisterCustomTransition(string name)
    {
        this.customs.Remove(name);
        this.definedInBrowser.Remove(name);
    }

    /// <summary>The transition the next navigation uses, whoever starts it, instead of the pages' own.</summary>
    public void SetNextOverride(PageTransitionOverride? transition) => this.nextOverride = transition;

    /// <summary>Navigates with a one-off transition. Going back from the page it opens reverses it.</summary>
    public void NavigateTo(string uri, PageTransitionOverride transition, bool replace = false)
        => this.Nav.NavigateTo(uri, transition, replace);

    // The page showing is the one registered last; the outgoing page unregisters once the new one
    // has rendered, so this is the incoming page by the time the transition finishes.
    PageTransition? Current => this.registered.Count > 0 ? this.registered[^1] : null;


    protected override void OnInitialized()
    {
        this.history.Add(this.Nav.Uri);
        this.arrivals.Add(null);
        this.Nav.LocationChanged += this.OnLocationChanged;
        PageTransitionNavigationExtensions.Attach(this.Nav, this);
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

        // The one-off transition for this navigation, if there is one: it is spent here either way.
        var requested = this.nextOverride;
        this.nextOverride = null;

        // A fragment jump on the same page is not a page change.
        if (Same(StripFragment(target), StripFragment(source)))
            return;

        this.pendingIsPop = this.history.Count >= 2 && Same(this.history[^2], target);
        this.pendingOutgoing = this.Current;
        this.pendingSource = source;

        // Going back from a page that was opened with a one-off transition reverses that transition.
        this.pendingOverride = requested ?? (this.pendingIsPop ? this.arrivals[^1] : null);
        this.arrivalOverride = this.pendingIsPop ? null : requested;

        try
        {
            // Resolves once the browser has captured the outgoing page; the navigation then goes
            // ahead and the incoming page is rendered underneath the captured picture.
            var started = await this.module.InvokeAsync<bool>("begin", context.CancellationToken, ReducedMotionText(this.ReducedMotion));
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
        {
            this.history.RemoveAt(this.history.Count - 1);
            this.arrivals.RemoveAt(this.arrivals.Count - 1);
        }
        else if (this.history.Count == 0 || !Same(this.history[^1], uri))
        {
            this.history.Add(uri);
            this.arrivals.Add(this.arrivalOverride);
        }
        this.arrivalOverride = null;

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
        var chosen = this.pendingOverride;
        this.pendingOverride = null;

        var (type, x, y, duration) = chosen is null
            ? Resolve(page?.Push ?? default, page?.Pop ?? default, page?.Direction ?? default, page?.Duration, isPop, this.DefaultTransition, this.Duration)
            : Resolve(chosen.Type, PageTransitionType.Default, chosen.Direction, chosen.Duration ?? page?.Duration, isPop, this.DefaultTransition, this.Duration);

        var customName = chosen is null ? page?.Custom : chosen.Custom;
        var custom = ResolveCustom(customName is null ? null : this.customs.GetValueOrDefault(customName), isPop);

        try
        {
            if (custom is { } c)
            {
                await this.DefineAsync(module, customName!);
                // Primitives only: anonymous types and records do not survive trimming over interop.
                await module.InvokeVoidAsync("finishCustom", isPop, duration, c.OldNames, c.OldDirection, c.NewNames, c.NewDirection, ReducedMotionText(this.ReducedMotion));
                return;
            }

            await module.InvokeVoidAsync("finish", type.ToString().ToLowerInvariant(), isPop, x, y, duration, this.pendingSource ?? "", target, ReducedMotionText(this.ReducedMotion));
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


    /// <summary>
    /// What a custom transition runs on each picture: forward, Exit on the old page and Enter on the
    /// new; back, PopExit and PopEnter, or Enter and Exit played in reverse when they are not set.
    /// </summary>
    internal static (string OldNames, string OldDirection, string NewNames, string NewDirection)? ResolveCustom(CustomPageTransitionDefinition? definition, bool isPop)
    {
        if (definition is null)
            return null;

        if (!isPop)
            return (Names(definition.Exit), "normal", Names(definition.Enter), "normal");

        var (oldAnimation, oldDirection) = definition.PopExit is { } popExit ? (popExit, "normal") : (definition.Enter, "reverse");
        var (newAnimation, newDirection) = definition.PopEnter is { } popEnter ? (popEnter, "normal") : (definition.Exit, "reverse");
        return (Names(oldAnimation), oldDirection, Names(newAnimation), newDirection);

        static string Names(PageTransitionKeyframes? keyframes) => keyframes is null || keyframes.Names.Count == 0 ? "none" : string.Join(", ", keyframes.Names);
    }

    async Task DefineAsync(IJSObjectReference module, string name)
    {
        if (this.definedInBrowser.Contains(name) || !this.customs.TryGetValue(name, out var definition))
            return;

        var css = string.Join("\n", new[] { definition.Enter, definition.Exit, definition.PopEnter, definition.PopExit }
            .Where(k => k is not null)
            .Select(k => k!.Css));

        try
        {
            await module.InvokeVoidAsync("defineCustom", name, css);
            this.definedInBrowser.Add(name);
        }
        catch (JSException)
        {
            // The rules did not apply; the transition then runs with no animation rather than failing the navigation.
        }
    }

    internal static string ReducedMotionText(PageTransitionReducedMotion mode) => mode switch
    {
        PageTransitionReducedMotion.Crossfade => "crossfade",
        PageTransitionReducedMotion.Play => "play",
        _ => "instant"
    };


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
        PageTransitionNavigationExtensions.Detach(this.Nav, this);
        this.locationChanging?.Dispose();
        await this.module.ReleaseLateAsync();
    }
}

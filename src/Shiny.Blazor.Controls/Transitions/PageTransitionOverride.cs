using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;

namespace Shiny.Blazor.Controls;

/// <summary>A one-off transition for a single navigation, which wins over the pages' own settings and the host's default.</summary>
/// <param name="Type">The transition. Ignored when <paramref name="Custom"/> names a custom transition.</param>
/// <param name="Direction">The edge the incoming page comes in from.</param>
/// <param name="Duration">How long, or null for the host's duration.</param>
/// <param name="Custom">The name of a transition registered with <see cref="PageTransitionHost.RegisterCustomTransition"/>.</param>
public sealed record PageTransitionOverride(
    PageTransitionType Type,
    PageTransitionDirection Direction = PageTransitionDirection.Default,
    TimeSpan? Duration = null,
    string? Custom = null);


/// <summary>
/// One keyframe animation of a custom page transition: CSS <c>@keyframes</c> rules and the names to
/// run, played on the outgoing or incoming page's <c>::view-transition</c> picture.
/// </summary>
/// <param name="Names">The <c>@keyframes</c> names to run together (one per animated property, typically).</param>
/// <param name="Css">The <c>@keyframes</c> rules themselves.</param>
public sealed record PageTransitionKeyframes(IReadOnlyList<string> Names, string Css);

/// <summary>
/// A page transition built from keyframes. Enter runs on the incoming page and Exit on the outgoing
/// one when navigating forward; going back runs PopEnter and PopExit, or, left null, Exit and Enter
/// played backwards - so defining a push gives a matching pop. Shiny.Blazor.Controls.Keyframe's
/// <c>CustomPageTransition</c> builds these from Keyframes markup.
/// </summary>
public sealed record CustomPageTransitionDefinition(
    string Name,
    PageTransitionKeyframes? Enter,
    PageTransitionKeyframes? Exit,
    PageTransitionKeyframes? PopEnter = null,
    PageTransitionKeyframes? PopExit = null);


/// <summary>Navigation with a one-off page transition.</summary>
public static class PageTransitionNavigationExtensions
{
    static readonly ConditionalWeakTable<NavigationManager, PageTransitionHost> Hosts = new();

    internal static void Attach(NavigationManager navigation, PageTransitionHost host) => Hosts.AddOrUpdate(navigation, host);

    internal static void Detach(NavigationManager navigation, PageTransitionHost host)
    {
        if (Hosts.TryGetValue(navigation, out var current) && ReferenceEquals(current, host))
            Hosts.Remove(navigation);
    }

    /// <summary>Navigates with this transition instead of the pages' own. Going back from the page it opens reverses it.</summary>
    public static void NavigateTo(this NavigationManager navigation, string uri, PageTransitionType transition,
        PageTransitionDirection direction = PageTransitionDirection.Default, TimeSpan? duration = null, bool replace = false)
        => navigation.NavigateTo(uri, new PageTransitionOverride(transition, direction, duration), replace);

    /// <summary>Navigates with a custom transition, by the name it was registered under.</summary>
    public static void NavigateTo(this NavigationManager navigation, string uri, string customTransition,
        TimeSpan? duration = null, bool replace = false)
        => navigation.NavigateTo(uri, new PageTransitionOverride(PageTransitionType.Default, PageTransitionDirection.Default, duration, customTransition), replace);

    /// <summary>Navigates with a one-off transition.</summary>
    public static void NavigateTo(this NavigationManager navigation, string uri, PageTransitionOverride transition, bool replace = false)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        if (Hosts.TryGetValue(navigation, out var host))
            host.SetNextOverride(transition);

        navigation.NavigateTo(uri, new NavigationOptions { ReplaceHistoryEntry = replace });
    }
}

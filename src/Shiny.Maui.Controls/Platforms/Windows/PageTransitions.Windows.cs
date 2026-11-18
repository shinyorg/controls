using Microsoft.Maui.Handlers;
using ShellSectionHandler = Microsoft.Maui.Controls.Handlers.ShellSectionHandler;

namespace Shiny.Maui.Controls;

static partial class ManagedPageTransitions
{
    /// <summary>
    /// WinUI's Frame slides each NavigationPage and Shell push in by itself. While a managed
    /// transition is about to run, the request reaching the handler is made non-animated, so the
    /// two never play on top of each other.
    /// </summary>
    static partial void InstallHeadHooks()
    {
        NavigationViewHandler.CommandMapper.ModifyMapping(
            nameof(IStackNavigation.RequestNavigation),
            (handler, view, arg, previous) => previous?.Invoke(handler, view, Adjust(arg))
        );
        ShellSectionHandler.CommandMapper.ModifyMapping(
            nameof(IStackNavigation.RequestNavigation),
            (handler, view, arg, previous) => previous?.Invoke(handler, view, Adjust(arg))
        );
    }


    static object? Adjust(object? arg)
    {
        if (arg is not NavigationRequest request)
            return arg;

        return OnHeadNavigation(request.Animated) && request.Animated
            ? new NavigationRequest(request.NavigationStack, false)
            : arg;
    }
}

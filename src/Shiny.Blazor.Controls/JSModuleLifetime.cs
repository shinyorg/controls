using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

/// <summary>
/// Teardown for a JS module import that finished after its component was disposed.
/// </summary>
/// <remarks>
/// A component imports its module in OnAfterRenderAsync, and the host can dispose it while that
/// import is still in flight (a route change, an @if flipping). DisposeAsync then sees no module and
/// releases nothing, and the continuation goes on to call init - which hands the browser a fresh
/// DotNetObjectReference and wires window/document listeners for a component that no longer exists.
/// The reference is tracked by the JS runtime until it is disposed, so the component is rooted for the
/// life of the circuit (Server) or the page (WebAssembly). Callers check their disposed flag straight
/// after the import and hand the late module here instead of initialising it.
/// </remarks>
static class JSModuleLifetime
{
    internal static async ValueTask ReleaseLateAsync(this IJSObjectReference? module)
    {
        if (module is null)
            return;

        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (JSException)
        {
        }
    }
}

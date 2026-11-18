using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls.Keyframe;

/// <summary>
/// The keyframe script, imported once per JS runtime (a WebAssembly app, or one Server circuit) and
/// shared by every component, rather than one import per animated element.
/// </summary>
internal static class KeyframeModule
{
    const string Path = "./_content/Shiny.Blazor.Controls.Keyframe/keyframe.js";

    static readonly ConditionalWeakTable<IJSRuntime, Task<IJSObjectReference>> Modules = new();
    static readonly Lock Gate = new();

    public static async Task<IJSObjectReference?> GetAsync(IJSRuntime js)
    {
        try
        {
            return await Import(js);
        }
        catch (Exception ex) when (IsGone(ex) || ex is JSException)
        {
            // Forget the failed import so the next caller tries again (a reconnected circuit, say).
            lock (Gate)
                Modules.Remove(js);
            return null;
        }
    }

    static Task<IJSObjectReference> Import(IJSRuntime js)
    {
        lock (Gate)
        {
            if (Modules.TryGetValue(js, out var existing))
                return existing;

            try
            {
                var task = js.InvokeAsync<IJSObjectReference>("import", Path).AsTask();
                Modules.AddOrUpdate(js, task);
                return task;
            }
            catch (InvalidOperationException ex)
            {
                return Task.FromException<IJSObjectReference>(ex);
            }
        }
    }

    /// <summary>Whether reduced motion is on in the browser right now (<c>prefers-reduced-motion: reduce</c>).</summary>
    public static async Task<bool> IsReducedMotionAsync(IJSRuntime js)
    {
        var module = await GetAsync(js);
        if (module is null)
            return false;

        try
        {
            return await module.InvokeAsync<bool>("isReducedMotion");
        }
        catch (Exception ex) when (IsGone(ex))
        {
            return false;
        }
    }

    /// <summary>The ways a JS call fails because the page, circuit or component went away - never worth surfacing.</summary>
    public static bool IsGone(Exception ex) => ex switch
    {
        JSDisconnectedException or OperationCanceledException or ObjectDisposedException => true,
        // Server prerendering: "JavaScript interop calls cannot be issued at this time".
        InvalidOperationException invalid => invalid.Message.Contains("JavaScript interop", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    public static async ValueTask DisposeQuietlyAsync(this IJSObjectReference? reference)
    {
        if (reference is null)
            return;

        try
        {
            await reference.DisposeAsync();
        }
        catch (Exception ex) when (IsGone(ex) || ex is JSException)
        {
        }
    }
}


/// <summary>The elements handed to the script alongside a program, which items point at by index.</summary>
internal sealed class ElementCollector
{
    readonly List<ElementReference> elements = [];

    public int Add(ElementReference element)
    {
        var index = this.elements.FindIndex(e => e.Id == element.Id);
        if (index >= 0)
            return index;

        this.elements.Add(element);
        return this.elements.Count - 1;
    }

    public ElementReference[] ToArray() => [.. this.elements];
}

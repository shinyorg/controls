using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

/// <summary>
/// Throws confetti when whatever it wraps is clicked or tapped.
/// </summary>
/// <example>
/// <code>
/// &lt;Confetti Preset="ConfettiPreset.Stars"&gt;
///     &lt;ShinyButton Text="Ship it" /&gt;
/// &lt;/Confetti&gt;
/// </code>
/// </example>
/// <remarks>
/// The burst starts at the pointer, or at the centre of the control when it was clicked from the
/// keyboard. The click itself is left alone - it still reaches the wrapped control's own handler.
/// The listener lives in JavaScript, so nothing round-trips through .NET before the confetti flies.
/// </remarks>
public partial class Confetti
{
    readonly string id = Guid.NewGuid().ToString("N");
    ElementReference anchor;
    IJSObjectReference? module;
    bool disposed;
    string? lastSignature;
    bool listening;

    /// <summary>The element that fires the confetti.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>The gesture that fires it. <see cref="ConfettiTrigger.None"/> leaves the wrapper inert.</summary>
    [Parameter] public ConfettiTrigger Trigger { get; set; } = ConfettiTrigger.Tap;

    /// <summary>Which preset to fire. Ignored when <see cref="Options"/> is set.</summary>
    [Parameter] public ConfettiPreset Preset { get; set; } = ConfettiPreset.Burst;

    /// <summary>A custom burst. Its origin is replaced by the point that was clicked.</summary>
    [Parameter] public ConfettiOptions? Options { get; set; }


    string Signature => $"{this.Trigger}|{this.Preset}|{this.Options?.ToJson()}";


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            if (firstRender)
            {
                var loaded = await this.JS.InvokeAsync<IJSObjectReference>("import", ConfettiService.ModulePath);
                if (this.disposed) { await loaded.ReleaseLateAsync(); return; }
                this.module = loaded;
            }
            if (this.module is null)
                return;

            // Options is a mutable object, so compare what would be sent rather than the reference.
            var signature = this.Signature;
            if (signature == this.lastSignature)
                return;

            this.lastSignature = signature;

            if (this.Trigger == ConfettiTrigger.None)
            {
                if (this.listening)
                    await this.module.InvokeVoidAsync("detach", this.id);
                this.listening = false;
            }
            else if (this.listening)
                await this.module.InvokeVoidAsync("update", this.id, this.Trigger.ToString(), this.Preset.ToString(), this.Options?.ToJson());
            else
            {
                await this.module.InvokeVoidAsync("attach", this.id, this.anchor, this.Trigger.ToString(), this.Preset.ToString(), this.Options?.ToJson());
                this.listening = true;
            }
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
        catch (JSException) { }
    }


    public async ValueTask DisposeAsync()
    {
        this.disposed = true;
        if (this.module is null)
            return;

        try
        {
            await this.module.InvokeVoidAsync("detach", this.id);
            await this.module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
        catch (JSException) { }
        GC.SuppressFinalize(this);
    }
}

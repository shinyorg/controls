using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls.Captchas;

/// <summary>
/// The shared driver for the hosted providers: loads the API script once per document, renders the
/// widget explicitly into an element Blazor then leaves alone, and forwards the provider's
/// callbacks. Rendered by <see cref="RemoteCaptchaProvider"/> — not meant to be placed by hand.
/// </summary>
public partial class RemoteCaptchaWidget
{
    [Inject] IJSRuntime JS { get; set; } = null!;

    [Parameter, EditorRequired] public RemoteCaptchaDescriptor Descriptor { get; set; } = null!;
    [Parameter, EditorRequired] public CaptchaRenderContext Context { get; set; } = null!;

    IJSObjectReference? module;
    bool disposed;
    DotNetObjectReference<RemoteCaptchaWidget>? selfRef;
    ElementReference hostEl;
    bool rendered;

    protected override void OnInitialized()
        => this.Context.OnWidgetReady(this);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || this.rendered)
            return;

        this.rendered = true;
        this.selfRef = DotNetObjectReference.Create(this);

        try
        {
            var loaded = await this.JS.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/Shiny.Blazor.Controls/captcha.js"
            );
            if (this.disposed) { await loaded.ReleaseLateAsync(); return; }
            this.module = loaded;

            // a named DTO, not an anonymous type: trimmed/AOT publish strips anonymous-type
            // constructor parameter names, which the JS interop serializer requires
            await this.module.InvokeVoidAsync("renderRemote", this.hostEl, this.selfRef, new RemoteJsOptions
            {
                ScriptUrl = this.Descriptor.ScriptUrl,
                GlobalName = this.Descriptor.GlobalName,
                SiteKey = this.Descriptor.SiteKey,
                UseReady = this.Descriptor.UseReadyCallback,
                SupportsBadge = this.Descriptor.SupportsBadge,
                LanguageAsRenderOption = this.Descriptor.LanguageAsRenderOption,
                SupportedSizes = this.Descriptor.SupportedSizes,
                Theme = this.Context.Theme.ToString().ToLowerInvariant(),
                Size = this.Context.Size.ToString().ToLowerInvariant(),
                Badge = BadgeValue(this.Context.BadgePosition),
                Language = this.Context.LanguageCode
            });
        }
        catch (JSException ex)
        {
            await this.Context.OnErrored(ex.Message);
        }
    }

    static string BadgeValue(CaptchaBadgePosition position) => position switch
    {
        CaptchaBadgePosition.BottomStart => "bottomleft",
        CaptchaBadgePosition.Inline => "inline",
        _ => "bottomright"
    };

    /// <summary>Called by the provider script when the user clears the challenge.</summary>
    [JSInvokable]
    public Task OnSolvedFromJs(string token) => this.Context.OnSolved(token);

    /// <summary>Called by the provider script when the token goes stale.</summary>
    [JSInvokable]
    public Task OnExpiredFromJs() => this.Context.OnExpired();

    /// <summary>Called by the provider script — and by the driver — when the widget cannot run.</summary>
    [JSInvokable]
    public Task OnErrorFromJs(string message) => this.Context.OnErrored(message);

    /// <inheritdoc />
    public async ValueTask ResetAsync()
    {
        if (this.module == null)
            return;

        try
        {
            await this.module.InvokeVoidAsync("resetRemote", this.hostEl);
        }
        catch (JSException)
        {
            // widget already gone — nothing to reset
        }
    }

    /// <inheritdoc />
    public async ValueTask ExecuteAsync()
    {
        if (this.module == null)
            return;

        try
        {
            await this.module.InvokeVoidAsync("executeRemote", this.hostEl);
        }
        catch (JSException ex)
        {
            await this.Context.OnErrored(ex.Message);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        this.disposed = true;
        this.Context.OnWidgetReady(null);

        if (this.module != null)
        {
            try
            {
                await this.module.InvokeVoidAsync("disposeRemote", this.hostEl);
                await this.module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
            catch (JSException)
            {
            }
        }

        this.selfRef?.Dispose();
    }

    sealed class RemoteJsOptions
    {
        public string? ScriptUrl { get; set; }
        public string? GlobalName { get; set; }
        public string? SiteKey { get; set; }
        public bool UseReady { get; set; }
        public bool SupportsBadge { get; set; }
        public bool LanguageAsRenderOption { get; set; }
        public string[]? SupportedSizes { get; set; }
        public string? Theme { get; set; }
        public string? Size { get; set; }
        public string? Badge { get; set; }
        public string? Language { get; set; }
    }
}

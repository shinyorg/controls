using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

/// <summary>
/// An application shell: header, footer, a left and a right <see cref="AppLayoutPanel"/>, and the
/// content between them. Regions are placed by CSS grid areas, so they can appear in any order in
/// the markup, and each one owns its own scroll region.
/// </summary>
public partial class AppLayout : ComponentBase, IAsyncDisposable
{
    readonly List<AppLayoutPanel> panels = new();
    ElementReference rootRef;
    IJSObjectReference? module;
    bool disposed;
    DotNetObjectReference<AppLayout>? selfRef;

    [Inject] IJSRuntime JS { get; set; } = null!;

    /// <summary>CSS height of the shell. Defaults to <c>100%</c>; use <c>100vh</c> / <c>100dvh</c> for a full-page shell.</summary>
    [Parameter] public string Height { get; set; } = "100%";

    /// <summary>Whether the header runs the full width or is inset between the panels.</summary>
    [Parameter] public LayoutSpan HeaderSpan { get; set; } = LayoutSpan.Full;

    /// <summary>Whether the footer runs the full width or is inset between the panels.</summary>
    [Parameter] public LayoutSpan FooterSpan { get; set; } = LayoutSpan.Full;

    /// <summary>Default border width, in pixels, for every region that does not override it.</summary>
    [Parameter] public double BorderWidth { get; set; } = 1;

    /// <summary>Default border colour for every region that does not override it.</summary>
    [Parameter] public string? BorderColor { get; set; }

    /// <summary>CSS background shorthand for the shell.</summary>
    [Parameter] public string? Background { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }

    IDictionary<string, object>? ExtraAttributes;
    string? UserClass;
    string RootStyle = string.Empty;

    /// <summary>Last measured width of the shell, in CSS pixels. Zero until the first measurement lands.</summary>
    public double HostWidth { get; private set; }

    protected override void OnParametersSet()
    {
        this.ExtraAttributes = LayoutAttributes.Split(this.AdditionalAttributes, out var userClass, out var userStyle);
        this.UserClass = userClass;

        // Single quotes: the areas are string tokens, and Razor would encode double quotes
        // inside the style attribute.
        var header = this.HeaderSpan == LayoutSpan.Full ? "'header header header'" : "'left header right'";
        var footer = this.FooterSpan == LayoutSpan.Full ? "'footer footer footer'" : "'left footer right'";

        var style =
            $"height:{this.Height};" +
            $"grid-template-areas:{header} 'left content right' {footer};" +
            $"--shiny-layout-border-width:{LayoutAttributes.Px(this.BorderWidth)};";

        if (!string.IsNullOrWhiteSpace(this.BorderColor))
            style += $"--shiny-layout-border-color:{this.BorderColor};";

        if (!string.IsNullOrWhiteSpace(this.Background))
            style += $"background:{this.Background};";

        this.RootStyle = LayoutAttributes.Append(style, userStyle);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        var loaded = await this.JS.InvokeAsync<IJSObjectReference>(
            "import",
            "./_content/Shiny.Blazor.Controls/app-layout.js"
        );
        if (this.disposed) { await loaded.ReleaseLateAsync(); return; }
        this.module = loaded;
        this.selfRef = DotNetObjectReference.Create(this);
        await this.module.InvokeVoidAsync("observeHost", this.rootRef, this.selfRef);
    }

    internal void Register(AppLayoutPanel panel)
    {
        this.panels.Add(panel);
        if (this.HostWidth > 0)
            _ = panel.OnHostWidthChangedAsync(this.HostWidth);
    }

    internal void Unregister(AppLayoutPanel panel)
        => this.panels.Remove(panel);

    [JSInvokable]
    public async Task OnHostResizedJs(double width)
    {
        this.HostWidth = width;
        foreach (var panel in this.panels.ToArray())
            await panel.OnHostWidthChangedAsync(width);
    }

    public async ValueTask DisposeAsync()
    {
        this.disposed = true;
        if (this.module is not null)
        {
            try
            {
                await this.module.InvokeVoidAsync("disposeHost", this.rootRef);
                await this.module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
        }
        this.selfRef?.Dispose();
    }
}

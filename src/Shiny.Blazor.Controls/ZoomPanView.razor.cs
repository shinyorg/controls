using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

/// <summary>
/// Wraps any content in pinch-to-zoom, pan, double-tap and wheel zoom — a stack of controls, a
/// chart, a form, a table, not only a picture.
/// </summary>
/// <remarks>
/// This is <see cref="ImageViewer"/>'s zoom machinery with the picture taken out: both drive the
/// same <c>zoom-pan-core.js</c>, so the pan limits and the gesture arbitration are one implementation
/// rather than two. What differs is the purpose — the viewer is a full-screen lightbox you open, and
/// this is an inline surface that stays where you put it and clips to its own bounds.
/// <para>
/// Zoom is a CSS transform, so the content keeps its laid-out size and nothing re-flows while a
/// pinch runs. What is inside stays live: a gesture that starts on a button, link or input is left
/// to that control until the surface is actually zoomed, and a pan only takes the pointer capture
/// once the pointer has travelled far enough to be a drag rather than a click.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// &lt;ZoomPanView MaxZoom="4"&gt;
///     &lt;div&gt;
///         &lt;h3&gt;Pinch me&lt;/h3&gt;
///         &lt;button&gt;Still clickable&lt;/button&gt;
///     &lt;/div&gt;
/// &lt;/ZoomPanView&gt;
/// </code>
/// </example>
public partial class ZoomPanView : IAsyncDisposable
{
    ElementReference hostEl;
    ElementReference surfaceEl;
    IJSObjectReference? module;
    DotNetObjectReference<ZoomPanView>? selfRef;

    /// <summary>
    /// What was last handed to zoom-pan.js. Re-sending it on every render is what turns a notify
    /// back into .NET into a render into another notify — the loop that leaves the renderer spinning
    /// and every click on the page landing nowhere.
    /// </summary>
    ZoomPanJsOptions? lastOptions;

    /// <summary>
    /// The scale JS and this component last agreed on. A <see cref="ZoomLevel"/> that differs from it
    /// came from the page and has to be pushed; one that matches came back from a gesture and must
    /// not be pushed again.
    /// </summary>
    double lastZoom = 1;

    /// <summary>
    /// A component that has not rendered has no render handle, and asking it to re-render throws.
    /// In the browser JS cannot call in before the first render puts the module there — the guard is
    /// for everywhere else that constructs the component directly.
    /// </summary>
    bool hasRendered;

    [Inject] IJSRuntime JS { get; set; } = default!;


    /// <summary>The content that zooms and pans.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Floor for <see cref="ZoomLevel"/>. Below 1 the content may sit smaller than its box.</summary>
    [Parameter] public double MinZoom { get; set; } = 1;

    /// <summary>Ceiling for <see cref="ZoomLevel"/>.</summary>
    [Parameter] public double MaxZoom { get; set; } = 5;

    /// <summary>The scale on the content. Two-way bindable — every gesture reports back through it.</summary>
    [Parameter] public double ZoomLevel { get; set; } = 1;

    /// <summary>Two-way binding hook for <see cref="ZoomLevel"/>.</summary>
    [Parameter] public EventCallback<double> ZoomLevelChanged { get; set; }

    /// <summary>Raised whenever the scale changes, mid-pinch included.</summary>
    [Parameter] public EventCallback<ZoomPanChangedEventArgs> ZoomChanged { get; set; }

    /// <summary>
    /// Turns every gesture off without unbinding anything, handing the content's own gestures
    /// straight back. Any zoom already applied is dropped.
    /// </summary>
    [Parameter] public bool IsZoomEnabled { get; set; } = true;

    /// <summary>Double tap to zoom in, and again to come back out.</summary>
    [Parameter] public bool DoubleTapToZoom { get; set; } = true;

    /// <summary>Where a double tap zooms to, capped by <see cref="MaxZoom"/>.</summary>
    [Parameter] public double DoubleTapZoom { get; set; } = 2.5;

    /// <summary>
    /// Whether the mouse wheel zooms. <see cref="ZoomPanWheelMode.Modifier"/> by default, because a
    /// plain wheel belongs to the page's scrollbar — and Ctrl/Cmd + wheel is what a trackpad pinch
    /// already sends.
    /// </summary>
    [Parameter] public ZoomPanWheelMode WheelMode { get; set; } = ZoomPanWheelMode.Modifier;

    /// <summary>Length of the double-tap and programmatic animations, in milliseconds.</summary>
    [Parameter] public int AnimationLength { get; set; } = 250;

    /// <summary>Extra classes for the host element.</summary>
    [Parameter] public string? CssClass { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }

    IDictionary<string, object>? ExtraAttributes { get; set; }
    string? UserClass { get; set; }
    string? UserStyle { get; set; }


    /// <summary>True while the content is scaled past its natural size — which is when it pans.</summary>
    public bool IsZoomed { get; private set; }


    // ---------------------------------------------------------------------------------------------
    // Public surface
    // ---------------------------------------------------------------------------------------------

    /// <summary>Animates the content back to its natural size and position.</summary>
    public async Task ResetZoomAsync()
    {
        if (this.module is not null)
            await this.module.InvokeVoidAsync("reset", this.hostEl);
    }


    /// <summary>Animates to <paramref name="zoom"/>, clamped into <see cref="MinZoom"/>..<see cref="MaxZoom"/>.</summary>
    public async Task ZoomToAsync(double zoom)
    {
        if (this.module is not null)
            await this.module.InvokeVoidAsync("zoomTo", this.hostEl, zoom);
    }


    /// <summary>Called from zoom-pan.js on every change of scale.</summary>
    [JSInvokable]
    public async Task OnZoomChanged(double zoom, bool isZoomed)
    {
        var flipped = this.IsZoomed != isZoomed;
        var moved = Math.Abs(this.ZoomLevel - zoom) > 0.0001;

        // A callback that fires for a scale nothing moved to re-renders the parent for nothing, and
        // every one of those renders comes back through here.
        if (!flipped && !moved)
            return;

        this.IsZoomed = isZoomed;
        this.lastZoom = zoom;

        if (moved)
        {
            this.ZoomLevel = zoom;
            await this.ZoomLevelChanged.InvokeAsync(zoom);
        }

        await this.ZoomChanged.InvokeAsync(new ZoomPanChangedEventArgs(zoom, isZoomed));

        // Only the zoomed/not flip changes what is rendered; re-rendering on every pinch frame would
        // put the renderer in the middle of a gesture for no visible gain.
        if (flipped && this.hasRendered)
            this.StateHasChanged();
    }


    // ---------------------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------------------

    string HostClasses
    {
        get
        {
            var builder = new StringBuilder("shiny-zoompan");

            if (this.IsZoomed)
                builder.Append(" is-zoomed");
            if (!String.IsNullOrWhiteSpace(this.CssClass))
                builder.Append(' ').Append(this.CssClass);
            if (!String.IsNullOrWhiteSpace(this.UserClass))
                builder.Append(' ').Append(this.UserClass);

            return builder.ToString();
        }
    }


    protected override void OnParametersSet()
    {
        this.ExtraAttributes = LayoutAttributes.Split(this.AdditionalAttributes, out var userClass, out var userStyle);
        this.UserClass = userClass;
        this.UserStyle = userStyle;
    }


    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        this.hasRendered = true;

        if (firstRender)
        {
            this.module = await this.JS.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/Shiny.Blazor.Controls/zoom-pan.js"
            );
            this.selfRef = DotNetObjectReference.Create(this);
            this.lastOptions = this.Options();
            await this.module.InvokeVoidAsync("init", this.hostEl, this.surfaceEl, this.selfRef, this.lastOptions);
            return;
        }

        if (this.module is null)
            return;

        // Both of these cross into JS only on a real change. Every render calls this method, and a
        // call that notifies back is a render that causes the next one.
        var options = this.Options();
        if (!options.Equals(this.lastOptions))
        {
            this.lastOptions = options;
            await this.module.InvokeVoidAsync("update", this.hostEl, options);
        }

        if (Math.Abs(this.ZoomLevel - this.lastZoom) > 0.0001)
        {
            this.lastZoom = this.ZoomLevel;
            await this.module.InvokeVoidAsync("zoomTo", this.hostEl, this.ZoomLevel);
        }
    }


    /// <summary>A named type: an anonymous one does not survive trimming over JS interop.</summary>
    ZoomPanJsOptions Options() => new(
        this.MinZoom,
        this.MaxZoom,
        this.DoubleTapZoom,
        this.DoubleTapToZoom,
        this.AnimationLength,
        this.WheelMode switch
        {
            ZoomPanWheelMode.Disabled => "disabled",
            ZoomPanWheelMode.Always => "always",
            _ => "modifier"
        },
        this.IsZoomEnabled
    );


    sealed record ZoomPanJsOptions(
        double MinZoom,
        double MaxZoom,
        double DoubleTapZoom,
        bool DoubleTapToZoom,
        int AnimationLength,
        string WheelMode,
        bool Enabled
    );


    public async ValueTask DisposeAsync()
    {
        if (this.module is not null)
        {
            try
            {
                await this.module.InvokeVoidAsync("dispose", this.hostEl);
                await this.module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The circuit is already gone; there is nothing left to tear down on the other side.
            }
        }

        this.selfRef?.Dispose();
    }
}


/// <summary>When the mouse wheel zooms a <see cref="ZoomPanView"/>.</summary>
public enum ZoomPanWheelMode
{
    /// <summary>Never. The wheel belongs to the page.</summary>
    Disabled,

    /// <summary>Only with Ctrl or Cmd held — which is also what a trackpad pinch sends.</summary>
    Modifier,

    /// <summary>Always, taking the wheel off the page while the pointer is over the surface.</summary>
    Always
}


/// <summary>Carries the scale a <see cref="ZoomPanView"/> has arrived at.</summary>
/// <param name="ZoomLevel">The scale now on the content.</param>
/// <param name="IsZoomed">Whether that is past the content's natural size.</param>
public record ZoomPanChangedEventArgs(double ZoomLevel, bool IsZoomed);

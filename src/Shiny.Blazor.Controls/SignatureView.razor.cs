using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using Shiny.Blazor.Controls.Theming;

namespace Shiny.Blazor.Controls;

/// <summary>
/// An inline signature surface: draw with a finger, pen or mouse anywhere on a page - a form, a card,
/// a table row. <see cref="SignaturePad"/> is this component inside a sheet with Sign and Cancel buttons.
/// </summary>
/// <example>
/// <code>
/// &lt;SignatureView @ref="signature" Height="180px" PlaceholderText="Sign here" @bind-HasSignature="signed" /&gt;
///
/// var png = await signature.ExportPngAsync();
/// </code>
/// </example>
/// <remarks>
/// Strokes are drawn by a small script on a canvas with <c>touch-action: none</c>, so a stroke never
/// scrolls the page; a touch outside the surface still does. Strokes are kept relative to the surface,
/// so they follow it when it resizes.
/// </remarks>
public partial class SignatureView : IAsyncDisposable
{
    ElementReference host;
    ElementReference canvasEl;
    IJSObjectReference? module;
    DotNetObjectReference<SignatureView>? selfRef;
    bool disposed;
    bool initialized;
    int strokeCount;
    string? lastOptions;

    // Mirrored in SignatureView.razor.css; only a caller-chosen value inlines. See StyleDefaults.
    internal const string DefaultBorderColor = "var(--shiny-color-outline-variant, #BBC7DF)";
    internal const double DefaultCornerRadius = 8;

    /// <summary>The ink. Any CSS colour.</summary>
    [Parameter] public string StrokeColor { get; set; } = "#000000";

    /// <summary>Stroke width in CSS pixels.</summary>
    [Parameter] public double StrokeWidth { get; set; } = 3;

    /// <summary>The paper. White by default, and not themed: it is part of the exported image.</summary>
    [Parameter] public string SignatureBackgroundColor { get; set; } = "#FFFFFF";

    /// <summary>Hint shown while the surface is empty ("Sign here"). Never exported.</summary>
    [Parameter] public string? PlaceholderText { get; set; }

    /// <summary>Draws a "✕ ____" baseline to sign on. Never exported.</summary>
    [Parameter] public bool ShowSignatureLine { get; set; }

    /// <summary>Shows a Clear button in the top corner once something is drawn.</summary>
    [Parameter] public bool ShowClearButton { get; set; } = true;

    [Parameter] public string ClearButtonText { get; set; } = "Clear";

    /// <summary>Shows the signature but takes no strokes, and hides the clear button.</summary>
    [Parameter] public bool IsReadOnly { get; set; }

    /// <summary>
    /// Draws the outline. Turn it off when the surface fills a container that already draws one (a card,
    /// a demo section, a <see cref="SignaturePad"/>), so the two edges do not double up.
    /// </summary>
    [Parameter] public bool ShowBorder { get; set; } = true;

    /// <summary>Leave unset to follow the theme's outline-variant colour.</summary>
    [Parameter] public string BorderColor { get; set; } = DefaultBorderColor;

    [Parameter] public double CornerRadius { get; set; } = DefaultCornerRadius;

    /// <summary>
    /// The surface's height as a CSS length. Defaults to 160px; pass <c>100%</c> to fill a parent that
    /// has a height of its own.
    /// </summary>
    [Parameter] public string Height { get; set; } = "160px";

    /// <summary>The default width of <see cref="ExportPngAsync"/>, in pixels.</summary>
    [Parameter] public int ExportWidth { get; set; } = 600;

    /// <summary>The default height of <see cref="ExportPngAsync"/>, in pixels.</summary>
    [Parameter] public int ExportHeight { get; set; } = 200;

    /// <summary>Whether at least one stroke has been drawn. Bind it (<c>@bind-HasSignature</c>) to enable a Submit button.</summary>
    [Parameter] public bool HasSignature { get; set; }

    [Parameter] public EventCallback<bool> HasSignatureChanged { get; set; }

    /// <summary>A stroke was finished and kept, with the number of strokes now drawn.</summary>
    [Parameter] public EventCallback<int> StrokeCompleted { get; set; }

    /// <summary>The surface was emptied - by the clear button, <see cref="ClearAsync"/> or undoing the last stroke.</summary>
    [Parameter] public EventCallback Cleared { get; set; }

    [Parameter] public string AriaLabel { get; set; } = "Signature";

    [Parameter] public string? Class { get; set; }

    [Parameter] public string? Style { get; set; }

    /// <summary>How many strokes are drawn - what <see cref="UndoAsync"/> walks back through.</summary>
    public int StrokeCount => this.strokeCount;

    string RootStyle
        => $"height:{Height};"
         + StyleDefaults.Override("--shiny-sigview-border-color", BorderColor, DefaultBorderColor)
         + StyleDefaults.Override("--shiny-sigview-radius", CornerRadius, DefaultCornerRadius)
         + Style;


    /// <summary>Removes every stroke.</summary>
    public async Task ClearAsync()
    {
        var had = this.strokeCount > 0;
        if (this.module is not null)
            await this.module.InvokeVoidAsync("clear", this.host);

        await this.SetStrokeCountAsync(0);
        if (had)
            await this.Cleared.InvokeAsync();
    }

    /// <summary>Removes the last stroke.</summary>
    /// <returns>False when there was nothing to undo.</returns>
    public async Task<bool> UndoAsync()
    {
        if (this.module is null || this.strokeCount == 0)
            return false;

        var left = await this.module.InvokeAsync<int>("undo", this.host);
        await this.SetStrokeCountAsync(left);
        if (left == 0)
            await this.Cleared.InvokeAsync();
        return true;
    }

    /// <summary>
    /// Renders the signature to PNG bytes of <paramref name="width"/> x <paramref name="height"/> (defaults
    /// <see cref="ExportWidth"/> x <see cref="ExportHeight"/>), scaled to fit and centred on
    /// <see cref="SignatureBackgroundColor"/>. Empty when nothing is drawn. The placeholder and signature
    /// line are never exported.
    /// </summary>
    public async Task<byte[]> ExportPngAsync(int? width = null, int? height = null)
    {
        if (this.module is null || this.strokeCount == 0)
            return [];

        return await this.module.InvokeAsync<byte[]>("exportPng", this.host, width ?? this.ExportWidth, height ?? this.ExportHeight);
    }


    protected override async Task OnParametersSetAsync()
    {
        // HasSignature is an output; a parent setting it false (a reset) clears the surface.
        if (!this.HasSignature && this.strokeCount > 0 && this.initialized)
            await this.ClearAsync();

        await this.PushOptionsAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        var loaded = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Shiny.Blazor.Controls/signature-pad.js");
        if (this.disposed)
        {
            await loaded.ReleaseLateAsync();
            return;
        }

        this.module = loaded;
        this.selfRef = DotNetObjectReference.Create(this);
        var options = this.Options();
        this.lastOptions = options.Key;
        await this.module.InvokeVoidAsync("init", this.host, this.canvasEl, this.selfRef, options);
        this.initialized = true;
    }

    async Task PushOptionsAsync()
    {
        if (this.module is null || !this.initialized)
            return;

        var options = this.Options();
        if (options.Key == this.lastOptions)
            return;

        this.lastOptions = options.Key;
        try
        {
            await this.module.InvokeVoidAsync("setOptions", this.host, options);
        }
        catch (JSDisconnectedException)
        {
        }
    }

    SignatureViewJsOptions Options() => new()
    {
        StrokeColor = this.StrokeColor,
        StrokeWidth = this.StrokeWidth,
        BackgroundColor = this.SignatureBackgroundColor,
        ReadOnly = this.IsReadOnly
    };

    async Task SetStrokeCountAsync(int count)
    {
        var had = this.strokeCount > 0;
        this.strokeCount = count;
        if (had != count > 0)
        {
            this.HasSignature = count > 0;
            await this.HasSignatureChanged.InvokeAsync(this.HasSignature);
        }
        this.StateHasChanged();
    }

    [JSInvokable]
    public async Task OnStrokeCommitted(int count)
    {
        await this.SetStrokeCountAsync(count);
        await this.StrokeCompleted.InvokeAsync(count);
    }

    public async ValueTask DisposeAsync()
    {
        this.disposed = true;
        if (this.module is not null)
        {
            try
            {
                await this.module.InvokeVoidAsync("dispose", this.host);
                await this.module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
        }
        this.selfRef?.Dispose();
    }

    // a named DTO, not an anonymous type: trimmed/AOT publish strips anonymous-type constructor
    // parameter names, which the JS interop serializer requires
    sealed class SignatureViewJsOptions
    {
        public string? StrokeColor { get; set; }
        public double StrokeWidth { get; set; }
        public string? BackgroundColor { get; set; }
        public bool ReadOnly { get; set; }

        internal string Key => $"{StrokeColor}|{StrokeWidth}|{BackgroundColor}|{ReadOnly}";
    }
}

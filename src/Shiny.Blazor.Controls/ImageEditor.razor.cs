using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

public partial class ImageEditor : IAsyncDisposable
{
    IJSObjectReference? module;
    DotNetObjectReference<ImageEditor>? selfRef;

    // Set the moment teardown starts, before the module is released, so anything still in flight
    // stops rather than racing the disposal.
    bool disposed;
    ElementReference rootEl;
    ElementReference canvasEl;
    ElementReference textInputEl;
    bool initialized;
    bool canUndo;
    bool canRedo;
    string currentMode = "none";
    string activeColor = "#ffffff";
    double zoomLevel = 1;

    // Shape fill. `shapeFill` is the composed rgba() the canvas gets, and null means "no fill" —
    // the hex and the alpha are kept alongside it so turning fill back on restores what was set
    // rather than resetting to white.
    string? shapeFill;
    string shapeFillHex = "#ffffff";
    double shapeFillAlpha = 0.35;

    // Inline text input state
    bool isTextInputVisible;
    string textInputValue = "";
    double textInputLeft;
    double textInputTop;
    double textInputNormX;
    double textInputNormY;
    double textInputScale = 1;

    static readonly System.Globalization.CultureInfo Culture = System.Globalization.CultureInfo.InvariantCulture;

    [Parameter] public string? Source { get; set; }
    [Parameter] public byte[]? ImageData { get; set; }
    [Parameter] public bool AllowCrop { get; set; } = true;
    [Parameter] public bool AllowRotate { get; set; } = true;
    [Parameter] public bool AllowDraw { get; set; } = true;
    [Parameter] public bool AllowTextAnnotation { get; set; } = true;
    [Parameter] public bool AllowLine { get; set; } = true;
    [Parameter] public bool AllowArrow { get; set; } = true;
    [Parameter] public bool AllowRectangle { get; set; } = true;
    [Parameter] public bool AllowEllipse { get; set; } = true;
    [Parameter] public bool AllowCircle { get; set; } = true;
    /// <summary>
    /// Interior colour for the shape tools as a <c>#rrggbb</c> hex string. Null or empty — the
    /// default — leaves shapes unfilled, which is what you want for a highlight box over a photo;
    /// a solid colour turns the same tool into a redaction block.
    /// </summary>
    [Parameter] public string? ShapeFillColor { get; set; }
    /// <summary>
    /// Opacity of <see cref="ShapeFillColor"/>, 0-1. It is separate from the colour because
    /// <c>&lt;input type="color"&gt;</c> cannot express alpha (MAUI carries it in the Color itself).
    /// </summary>
    [Parameter] public double ShapeFillOpacity { get; set; } = 0.35;
    /// <summary>Shows the fill swatch, opacity slider and fill on/off toggle while a shape tool is active.</summary>
    [Parameter] public bool ShowShapeFillPicker { get; set; } = true;
    [Parameter] public bool AllowZoom { get; set; } = true;
    /// <summary>Lower zoom bound. 1.0 is fit-to-view.</summary>
    [Parameter] public double MinZoom { get; set; } = 1;
    /// <summary>Upper zoom bound. 8x by default, enough for per-pixel touch-ups.</summary>
    [Parameter] public double MaxZoom { get; set; } = 8;
    /// <summary>Shows the zoom out / percentage / zoom in / fit cluster in the default toolbar.</summary>
    [Parameter] public bool ShowZoomControls { get; set; } = true;
    /// <summary>Shows a caption under each tool icon. Turn off for a compact icon-only bar.</summary>
    [Parameter] public bool ShowToolLabels { get; set; } = true;
    /// <summary>Shows the pen-weight presets next to the colour swatch for the ink tools.</summary>
    [Parameter] public bool ShowStrokeWidthPicker { get; set; } = true;
    /// <summary>Pen weights offered by the stroke-width picker.</summary>
    [Parameter] public IEnumerable<double> StrokeWidthPresets { get; set; } = [2, 4, 8];
    /// <summary>Extra content rendered at the trailing edge of the toolbar (a save button, say).</summary>
    [Parameter] public RenderFragment? ToolbarActions { get; set; }
    [Parameter] public string CropApplyText { get; set; } = "Apply";
    [Parameter] public string CropCancelText { get; set; } = "Cancel";
    [Parameter] public EventCallback<double> ZoomLevelChanged { get; set; }
    [Parameter] public bool AllowFontSelection { get; set; }
    [Parameter] public bool AllowFontSizeSelection { get; set; }
    // The ribbon's selected tab, held here so a re-render (a colour pick, an undo) does not throw the
    // user back to Home. Bound with @bind-SelectedKey.
    string? ribbonTabKey = "home";

    /// <summary>
    /// Below this width the ribbon runs dense - one row, every item small, group titles dropped.
    /// </summary>
    /// <remarks>
    /// An expanded ribbon is about a quarter of a phone screen, and this control's whole job is to show
    /// the picture underneath it. The MAUI editor uses the same 600 and the same reasoning.
    /// </remarks>
    const double SimplifiedBreakpoint = 600;

    /// <summary>
    /// CSS decides this on the Blazor side rather than a measured width: a container query on the
    /// editor is the same rule without a resize observer, and it cannot fall out of step with layout.
    /// The parameter stays honest for a host that wants to pin one.
    /// </summary>
    [Parameter] public RibbonDisplayMode? ToolbarDisplayMode { get; set; }

    // Two-way bound to the ribbon so the collapse chevron sticks: the editor re-renders on every tool
    // change, and a one-way DisplayMode would push the old value straight back over the user's choice.
    RibbonDisplayMode ribbonDisplayMode = RibbonDisplayMode.Expanded;

    protected override void OnParametersSet()
    {
        // A host that pins a mode wins; otherwise the field holds whatever the user last chose.
        if (this.ToolbarDisplayMode is { } pinned)
            this.ribbonDisplayMode = pinned;
    }

    [Parameter] public string DrawStrokeColor { get; set; } = "#ffffff";
    [Parameter] public double DrawStrokeWidth { get; set; } = 3;
    [Parameter] public double TextFontSize { get; set; } = 16;
    [Parameter] public string TextColor { get; set; } = "#ffffff";
    [Parameter] public string TextFontFamily { get; set; } = "Arial";
    [Parameter] public IEnumerable<string>? AvailableFonts { get; set; }
    [Parameter] public IEnumerable<double>? AvailableFontSizes { get; set; }
    [Parameter] public EventCallback<string> TextFontFamilyChanged { get; set; }
    [Parameter] public EventCallback<double> TextFontSizeChanged { get; set; }
    /// <summary>Where the toolbar sits: <c>"top"</c> (the default) or <c>"bottom"</c>.</summary>
    /// <remarks>
    /// Top, since the toolbar became a ribbon: a ribbon is top-of-window chrome, and read upside down -
    /// tab strip above a body of groups, pinned to the floor - it stops looking like one.
    /// </remarks>
    [Parameter] public string ToolbarPosition { get; set; } = "top";
    [Parameter] public RenderFragment? ToolbarTemplate { get; set; }
    [Parameter] public EventCallback<bool> CanUndoChanged { get; set; }
    [Parameter] public EventCallback<bool> CanRedoChanged { get; set; }

    string? previousSource;
    byte[]? previousImageData;
    string? previousShapeFillColor;
    double previousShapeFillOpacity;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            activeColor = DrawStrokeColor;

            shapeFillAlpha = Math.Clamp(ShapeFillOpacity, 0, 1);
            previousShapeFillColor = ShapeFillColor;
            previousShapeFillOpacity = ShapeFillOpacity;
            if (!string.IsNullOrWhiteSpace(ShapeFillColor))
            {
                shapeFillHex = ShapeFillColor;
                shapeFill = ComposeFill();
            }

            var loaded = await JS.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/Shiny.Blazor.Controls/image-editor.js");

            // The host can remove this component while the import is still in flight - a view
            // switched, a window closed. Nothing has been wired up yet, so the module is simply
            // released and initialisation abandoned; going on would attach handlers and a
            // DotNetObjectReference to a component that is already gone.
            if (disposed)
            {
                await ReleaseAsync(loaded);
                return;
            }

            module = loaded;
            selfRef = DotNetObjectReference.Create(this);

            // a named DTO, not an anonymous type: trimmed/AOT publish strips anonymous-type
            // constructor parameter names, which the JS interop serializer requires
            await module.InvokeVoidAsync("init", rootEl, canvasEl, selfRef, new ImageEditorJsOptions
            {
                DrawColor = activeColor,
                DrawWidth = DrawStrokeWidth,
                TextColor = activeColor,
                TextSize = TextFontSize,
                TextFont = TextFontFamily,
                AllowZoom = AllowZoom,
                MinZoom = MinZoom,
                MaxZoom = MaxZoom,
                ShapeFill = shapeFill
            });

            initialized = true;
            await LoadImageAsync();
        }
        else if (initialized && !disposed)
        {
            await SyncParametersAsync();

            if (isTextInputVisible)
            {
                try
                {
                    await textInputEl.FocusAsync();
                }
                catch { }
            }
        }
    }

    async Task SyncParametersAsync()
    {
        if (disposed || module == null)
            return;

        // Check if source changed
        if (Source != previousSource || ImageData != previousImageData)
            await LoadImageAsync();

        await CallAsync("updateDrawSettings", rootEl, activeColor, DrawStrokeWidth);
        await CallAsync("updateTextSettings", rootEl, activeColor, TextFontSize, TextFontFamily);
        await CallAsync("updateAllowZoom", rootEl, AllowZoom);
        await CallAsync("updateZoomLimits", rootEl, MinZoom, MaxZoom);

        // Re-derive the fill only when the host actually changed it, so a re-render does not
        // stomp on what the toolbar's own swatch and slider set
        if (ShapeFillColor != previousShapeFillColor || Math.Abs(ShapeFillOpacity - previousShapeFillOpacity) > 0.0001)
        {
            previousShapeFillColor = ShapeFillColor;
            previousShapeFillOpacity = ShapeFillOpacity;
            shapeFillAlpha = Math.Clamp(ShapeFillOpacity, 0, 1);

            if (string.IsNullOrWhiteSpace(ShapeFillColor))
            {
                shapeFill = null;
            }
            else
            {
                shapeFillHex = ShapeFillColor;
                shapeFill = ComposeFill();
            }
        }

        await CallAsync("updateShapeSettings", rootEl, shapeFill);
    }

    async Task LoadImageAsync()
    {
        if (disposed || module == null) return;

        previousSource = Source;
        previousImageData = ImageData;

        if (ImageData is { Length: > 0 })
            await CallAsync("loadImageData", rootEl, ImageData);
        else if (!string.IsNullOrEmpty(Source))
            await CallAsync("loadImage", rootEl, Source);
    }

    // Public methods callable via @ref
    public async ValueTask UndoAsync()
    {
        await CallAsync("undo", rootEl);
    }

    public async ValueTask RedoAsync()
    {
        await CallAsync("redo", rootEl);
    }

    public async ValueTask RotateAsync(float degrees)
    {
        await CallAsync("rotate", rootEl, degrees);
    }

    public async ValueTask ResetAsync()
    {
        if (!await CallAsync("reset", rootEl))
            return;

        currentMode = "none";
        DismissTextInput();
        StateHasChanged();
    }

    public async ValueTask SetModeAsync(string mode)
    {
        DismissTextInput();

        if (!await CallAsync("setMode", rootEl, mode))
            return;

        currentMode = mode;
        StateHasChanged();
    }

    /// <summary>Current zoom factor, where 1.0 is fit-to-view.</summary>
    public double ZoomLevel => zoomLevel;

    public async ValueTask ZoomInAsync()
    {
        await CallAsync("zoomIn", rootEl);
    }

    public async ValueTask ZoomOutAsync()
    {
        await CallAsync("zoomOut", rootEl);
    }

    public async ValueTask ZoomToFitAsync()
    {
        await CallAsync("zoomToFit", rootEl);
    }

    /// <summary>Sets an explicit zoom factor, anchored on the centre of the view.</summary>
    public async ValueTask SetZoomAsync(double scale)
    {
        await CallAsync("setZoom", rootEl, scale);
    }

    public async ValueTask ApplyCropAsync()
    {
        if (!await CallAsync("applyCrop", rootEl))
            return;

        currentMode = "none";
        StateHasChanged();
    }

    public async Task<byte[]> ExportAsync(string format = "png", double quality = 0.92, int? width = null, int? height = null)
    {
        // An empty array rather than a throw when there is nothing to export from: a host calling
        // this while tearing down should get "no image", not an exception out of a teardown path.
        return await CallAsync<byte[]>("exportImage", rootEl, format, quality, width ?? 0, height ?? 0) ?? [];
    }

    // Toolbar actions
    async Task ToggleCrop()
    {
        var newMode = currentMode == "crop" ? "none" : "crop";
        await SetModeAsync(newMode);
    }

    async Task ToggleDraw()
    {
        var newMode = currentMode == "draw" ? "none" : "draw";
        await SetModeAsync(newMode);
    }

    async Task ToggleText()
    {
        var newMode = currentMode == "text" ? "none" : "text";
        await SetModeAsync(newMode);
    }

    async Task ToggleLine()
    {
        var newMode = currentMode == "line" ? "none" : "line";
        await SetModeAsync(newMode);
    }

    async Task ToggleArrow()
    {
        var newMode = currentMode == "arrow" ? "none" : "arrow";
        await SetModeAsync(newMode);
    }

    async Task ToggleRectangle()
    {
        var newMode = currentMode == "rect" ? "none" : "rect";
        await SetModeAsync(newMode);
    }

    async Task ToggleEllipse()
    {
        var newMode = currentMode == "ellipse" ? "none" : "ellipse";
        await SetModeAsync(newMode);
    }

    async Task ToggleCircle()
    {
        var newMode = currentMode == "circle" ? "none" : "circle";
        await SetModeAsync(newMode);
    }

    async Task OnFillColorChanged(ChangeEventArgs e)
    {
        shapeFillHex = e.Value?.ToString() ?? "#ffffff";
        ShapeFillColor = shapeFillHex;
        shapeFill = ComposeFill();
        await PushShapeFillAsync();
    }

    async Task OnFillOpacityChanged(ChangeEventArgs e)
    {
        if (!double.TryParse(e.Value?.ToString(), System.Globalization.NumberStyles.Any, Culture, out var percent))
            return;

        shapeFillAlpha = Math.Clamp(percent / 100d, 0, 1);
        ShapeFillOpacity = shapeFillAlpha;

        // Dragging the slider while fill is off is a request for fill, not a no-op
        ShapeFillColor = shapeFillHex;
        shapeFill = ComposeFill();
        await PushShapeFillAsync();
    }

    async Task ToggleShapeFill()
    {
        shapeFill = shapeFill == null ? ComposeFill() : null;
        ShapeFillColor = shapeFill == null ? null : shapeFillHex;
        await PushShapeFillAsync();
    }

    async Task PushShapeFillAsync()
    {
        await CallAsync("updateShapeSettings", rootEl, shapeFill);
    }

    /// <summary>Folds the opacity into the hex swatch, since a colour input can't carry alpha.</summary>
    string ComposeFill()
    {
        var hex = shapeFillHex.TrimStart('#');
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, Culture, out var rgb))
            return shapeFillHex;

        var r = (rgb >> 16) & 0xFF;
        var g = (rgb >> 8) & 0xFF;
        var b = rgb & 0xFF;
        return $"rgba({r},{g},{b},{shapeFillAlpha.ToString("0.###", Culture)})";
    }

    string FillOpacityPercent => Math.Round(shapeFillAlpha * 100).ToString("0", Culture);

    async Task OnColorChanged(ChangeEventArgs e)
    {
        var color = e.Value?.ToString() ?? "#ffffff";
        activeColor = color;
        DrawStrokeColor = color;
        TextColor = color;

        await CallAsync("updateDrawSettings", rootEl, color, DrawStrokeWidth);
        await CallAsync("updateTextSettings", rootEl, color, TextFontSize, TextFontFamily);
    }

    async Task OnFontFamilySelected(ChangeEventArgs e)
    {
        var value = e.Value?.ToString() ?? string.Empty;
        TextFontFamily = value;
        await TextFontFamilyChanged.InvokeAsync(value);
        await CallAsync("updateTextSettings", rootEl, activeColor, TextFontSize, TextFontFamily);
    }

    async Task OnFontSizeSelected(ChangeEventArgs e)
    {
        if (double.TryParse(e.Value?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var size))
        {
            TextFontSize = size;
            await TextFontSizeChanged.InvokeAsync(size);
            await CallAsync("updateTextSettings", rootEl, activeColor, TextFontSize, TextFontFamily);
        }
    }

    Task CancelCrop() => SetModeAsync("none").AsTask();

    // Named handlers rather than inline lambdas: the razor attribute delimiter is a double quote,
    // so a mode string can't be written inline, and returning the Task keeps Blazor awaiting it
    Task SelectMoveTool() => SetModeAsync("none").AsTask();

    Task RotateClockwise() => RotateAsync(90).AsTask();

    Task ApplyCrop() => ApplyCropAsync().AsTask();

    Task Undo() => UndoAsync().AsTask();

    Task Redo() => RedoAsync().AsTask();

    Task Reset() => ResetAsync().AsTask();

    Task ZoomIn() => ZoomInAsync().AsTask();

    Task ZoomOut() => ZoomOutAsync().AsTask();

    Task ZoomToFit() => ZoomToFitAsync().AsTask();

    async Task SetStrokeWidthAsync(double width)
    {
        DrawStrokeWidth = width;
        await CallAsync("updateDrawSettings", rootEl, activeColor, width);
    }

    bool IsSelectedWidth(double width) => Math.Abs(DrawStrokeWidth - width) < 0.01;

    bool IsInkMode => currentMode is "draw" or "line" or "arrow";

    bool IsShapeMode => currentMode is "rect" or "ellipse" or "circle";

    string ToolbarOrderClass => ToolbarPosition == "top" ? "shiny-imgeditor-toolbar--top" : string.Empty;

    string ZoomText => $"{Math.Round(zoomLevel * 100)}%";

    string Active(string mode) => currentMode == mode ? "active" : string.Empty;

    // Inline text input
    async Task OnTextInputKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
            await CommitTextInput();
        else if (e.Key == "Escape")
            DismissTextInput();
    }

    async Task CommitTextInput()
    {
        if (!isTextInputVisible) return;

        var text = textInputValue?.Trim();
        isTextInputVisible = false;
        textInputValue = "";

        if (!string.IsNullOrEmpty(text))
        {
            await CallAsync("addTextAnnotation", rootEl, text, textInputNormX, textInputNormY);
        }

        StateHasChanged();
    }

    void DismissTextInput()
    {
        isTextInputVisible = false;
        textInputValue = "";
    }

    /// <summary>
    /// Calls into the module, or does nothing once the component is on its way out.
    /// </summary>
    /// <returns>False when the call did not run, so a caller does not record state it never set.</returns>
    /// <remarks>
    /// Every one of these can be reached after teardown has begun - a queued render, a callback JS
    /// already had in flight, a host that switched views while an operation was awaiting. Checking
    /// the field for null does not cover it: the reference can be disposed while the call is on the
    /// wire, and <see cref="IJSObjectReference"/> throws <see cref="ObjectDisposedException"/>
    /// rather than returning. Escaping that far it is fatal - an unhandled exception from a
    /// component's async work tears down the whole renderer, so one editor being closed at the
    /// wrong moment takes every other component on the page with it.
    /// </remarks>
    async ValueTask<bool> CallAsync(string identifier, params object?[] args)
    {
        if (disposed || module is not { } target)
            return false;

        try
        {
            await target.InvokeVoidAsync(identifier, args);
            return true;
        }
        catch (Exception e) when (IsTeardown(e))
        {
            return false;
        }
    }

    /// <summary>The same guard for a call that returns something.</summary>
    async ValueTask<T?> CallAsync<T>(string identifier, params object?[] args)
    {
        if (disposed || module is not { } target)
            return default;

        try
        {
            return await target.InvokeAsync<T>(identifier, args);
        }
        catch (Exception e) when (IsTeardown(e))
        {
            return default;
        }
    }

    /// <summary>
    /// Whether an exception is the component going away rather than the editor failing.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow. A <see cref="JSException"/> is a real fault in the editor's own script
    /// and still surfaces - swallowing those would turn a bug into an editor that silently stops
    /// responding, which is far harder to report than a crash.
    /// </remarks>
    static bool IsTeardown(Exception e)
        => e is JSDisconnectedException or ObjectDisposedException or OperationCanceledException;

    // JS callbacks
    /// <remarks>
    /// JS holds a DotNetObjectReference to this component and can call back after it has been
    /// removed - a pointer gesture that lands as the view is switching. Raising the callback then
    /// hands the host an event for a component it has already dropped, and StateHasChanged on a
    /// disposed component throws into the renderer.
    /// </remarks>
    [JSInvokable]
    public async Task OnCanUndoChanged(bool value)
    {
        if (disposed)
            return;

        canUndo = value;
        await CanUndoChanged.InvokeAsync(value);
        StateHasChanged();
    }

    [JSInvokable]
    public async Task OnCanRedoChanged(bool value)
    {
        if (disposed)
            return;

        canRedo = value;
        await CanRedoChanged.InvokeAsync(value);
        StateHasChanged();
    }

    [JSInvokable]
    public Task OnRequestTextInput(double canvasX, double canvasY, double normX, double normY, double scale)
    {
        if (disposed)
            return Task.CompletedTask;

        textInputLeft = canvasX;
        textInputTop = canvasY;
        textInputNormX = normX;
        textInputNormY = normY;
        textInputScale = scale;
        textInputValue = "";
        isTextInputVisible = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    [JSInvokable]
    public async Task OnZoomChanged(double value)
    {
        if (disposed)
            return;

        zoomLevel = value;
        await ZoomLevelChanged.InvokeAsync(value);
        StateHasChanged();
    }

    public async ValueTask DisposeAsync()
    {
        // Flagged before anything is released, so a render or a JS callback that arrives during the
        // teardown below stops at the guards rather than reaching a half-disposed component.
        if (disposed)
            return;

        disposed = true;

        // Taken and cleared together: leaving the field pointing at a disposed reference is what
        // turns every later guard into a call on dead interop.
        var target = module;
        module = null;

        await ReleaseAsync(target);

        selfRef?.Dispose();
        selfRef = null;
    }

    /// <summary>
    /// Tells the script to let go of its handlers, then releases the module.
    /// </summary>
    /// <param name="target">The module being released.  Passed in because the field is cleared first.</param>
    /// <remarks>
    /// Nothing here may throw. This runs from <see cref="DisposeAsync"/>, and an exception escaping
    /// a disposal is not caught by whatever closed the view - it reaches the renderer unhandled and
    /// takes the circuit down, so closing an editor would crash the page it was on. The failure
    /// modes are all the same shape anyway: the circuit is gone, the reference is already disposed,
    /// or the call was cancelled - in every one of them the browser is discarding this page's state
    /// regardless, so there is nothing left to salvage by reporting it.
    /// <para>
    /// The script is keyed on <c>rootEl</c>, so that is passed through - without it the handlers
    /// and the resize observer it attached outlive the component.  Where the import was abandoned
    /// before <c>init</c> ran there is no state for that root and the script simply returns.
    /// </para>
    /// </remarks>
    async ValueTask ReleaseAsync(IJSObjectReference? target)
    {
        if (target is null)
            return;

        try
        {
            await target.InvokeVoidAsync("dispose", rootEl);
        }
        catch (Exception e) when (IsTeardown(e) || e is JSException)
        {
        }

        try
        {
            await target.DisposeAsync();
        }
        catch (Exception e) when (IsTeardown(e) || e is JSException)
        {
        }
    }

    sealed class ImageEditorJsOptions
    {
        public string? DrawColor { get; set; }
        public double DrawWidth { get; set; }
        public string? TextColor { get; set; }
        public double TextSize { get; set; }
        public string? TextFont { get; set; }
        public bool AllowZoom { get; set; }
        public double MinZoom { get; set; }
        public double MaxZoom { get; set; }
        public string? ShapeFill { get; set; }
    }
}

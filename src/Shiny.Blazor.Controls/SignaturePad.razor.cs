using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

using Shiny.Blazor.Controls.Theming;

namespace Shiny.Blazor.Controls;

public partial class SignaturePad : IAsyncDisposable
{
    IJSObjectReference? module;
    DotNetObjectReference<SignaturePad>? selfRef;
    ElementReference canvasHost;
    ElementReference canvasEl;
    bool initialized;
    bool isOpen;
    bool hasSignature;
    bool isSyncing;
    IList<DetentValue> resolvedDetents = new List<DetentValue>();

    [Parameter] public bool IsOpen { get; set; }
    [Parameter] public EventCallback<bool> IsOpenChanged { get; set; }

    [Parameter] public SheetDirection Direction { get; set; } = SheetDirection.Bottom;
    [Parameter] public bool IsLocked { get; set; } = true;
    [Parameter] public DetentValue Detent { get; set; } = DetentValue.Half;

    [Parameter] public string StrokeColor { get; set; } = "#000000";
    [Parameter] public string SignatureBackgroundColor { get; set; } = "#FFFFFF";
    [Parameter] public double StrokeWidth { get; set; } = 3;

    [Parameter] public string SignButtonText { get; set; } = "Sign";
    [Parameter] public string CancelButtonText { get; set; } = "Cancel";
    // Mirrored in SignaturePad.razor.css; only a caller-chosen value inlines. See StyleDefaults.
    internal const string DefaultSignButtonColor = "var(--shiny-color-primary, #0055D9)";
    internal const string DefaultCancelButtonColor = "var(--shiny-color-on-surface-variant, #3B475B)";
    internal const string DefaultPanelBackgroundColor = "var(--shiny-color-surface, #F6FAFE)";

    [Parameter] public string SignButtonColor { get; set; } = DefaultSignButtonColor;
    [Parameter] public string CancelButtonColor { get; set; } = DefaultCancelButtonColor;
    [Parameter] public bool ShowCancelButton { get; set; } = true;

    [Parameter] public string PanelBackgroundColor { get; set; } = DefaultPanelBackgroundColor;
    [Parameter] public double PanelCornerRadius { get; set; } = 16;
    [Parameter] public bool HasBackdrop { get; set; } = true;

    [Parameter] public int ExportWidth { get; set; } = 600;
    [Parameter] public int ExportHeight { get; set; } = 200;

    [Parameter] public EventCallback<byte[]> Signed { get; set; }
    [Parameter] public EventCallback Cancelled { get; set; }

    protected override void OnParametersSet()
    {
        resolvedDetents = new List<DetentValue> { Detent };

        if (IsOpen != isOpen)
        {
            isOpen = IsOpen;
            if (isOpen && initialized)
                _ = InitCanvasAsync();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            module = await JS.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/Shiny.Blazor.Controls/signature-pad.js");
            selfRef = DotNetObjectReference.Create(this);
        }

        if (isOpen && module != null && !initialized)
        {
            // Small delay to let the sheet animate and canvas get layout
            await Task.Delay(100);
            await InitCanvasAsync();
        }
    }

    async Task InitCanvasAsync()
    {
        if (module == null || selfRef == null) return;

        try
        {
            // a named DTO, not an anonymous type: trimmed/AOT publish strips anonymous-type
            // constructor parameter names, which the JS interop serializer requires
            await module.InvokeVoidAsync("init", canvasHost, canvasEl, selfRef, new SignaturePadJsOptions
            {
                StrokeColor = StrokeColor,
                StrokeWidth = StrokeWidth,
                BackgroundColor = SignatureBackgroundColor
            });
            initialized = true;
        }
        catch (JSException)
        {
            // Canvas may not be laid out yet
        }
    }

    async Task OnSign()
    {
        if (module == null || !hasSignature) return;

        var pngBytes = await module.InvokeAsync<byte[]>("exportPng", canvasHost, ExportWidth, ExportHeight);

        await CleanupAndClose();
        await Signed.InvokeAsync(pngBytes);
    }

    async Task OnCancel()
    {
        await CleanupAndClose();
        await Cancelled.InvokeAsync();
    }

    async Task OnClear()
    {
        if (module != null)
            await module.InvokeVoidAsync("clear", canvasHost);
        hasSignature = false;
    }

    async Task CleanupAndClose()
    {
        if (module != null)
            await module.InvokeVoidAsync("clear", canvasHost);
        hasSignature = false;
        initialized = false;

        if (module != null)
        {
            try { await module.InvokeVoidAsync("dispose", canvasHost); }
            catch (JSException) { }
        }

        isOpen = false;
        IsOpen = false;
        await IsOpenChanged.InvokeAsync(false);
    }

    [JSInvokable]
    public Task OnHasSignatureChanged(bool value)
    {
        hasSignature = value;
        StateHasChanged();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (module != null)
        {
            try
            {
                await module.InvokeVoidAsync("dispose", canvasHost);
                await module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
        }
        selfRef?.Dispose();
    }

    sealed class SignaturePadJsOptions
    {
        public string? StrokeColor { get; set; }
        public double StrokeWidth { get; set; }
        public string? BackgroundColor { get; set; }
    }
}

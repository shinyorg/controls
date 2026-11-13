using Microsoft.AspNetCore.Components;

using Shiny.Blazor.Controls.Theming;

namespace Shiny.Blazor.Controls;

/// <summary>
/// A <see cref="SignatureView"/> in a sheet, with Sign and Cancel buttons. Use <see cref="SignatureView"/>
/// directly to put the surface on the page itself.
/// </summary>
public partial class SignaturePad
{
    SignatureView? signatureView;
    bool isOpen;
    bool hasSignature;
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
        isOpen = IsOpen;
    }

    async Task OnSign()
    {
        if (signatureView is null || !hasSignature) return;

        var pngBytes = await signatureView.ExportPngAsync(ExportWidth, ExportHeight);

        await CleanupAndClose();
        await Signed.InvokeAsync(pngBytes);
    }

    async Task OnCancel()
    {
        await CleanupAndClose();
        await Cancelled.InvokeAsync();
    }

    async Task CleanupAndClose()
    {
        if (signatureView is not null)
            await signatureView.ClearAsync();
        hasSignature = false;

        isOpen = false;
        IsOpen = false;
        await IsOpenChanged.InvokeAsync(false);
    }
}

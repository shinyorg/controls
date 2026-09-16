using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

public partial class ColorPicker : IAsyncDisposable
{
    IJSObjectReference? module;
    bool disposed;
    DotNetObjectReference<ColorPicker>? selfRef;
    ElementReference rootEl;
    ElementReference spectrumEl;
    ElementReference hueEl;
    ElementReference opacityEl;
    bool initialized;

    string currentHex = "#FF0000";
    string currentCss = "#FF0000";

    [Parameter] public string SelectedColor { get; set; } = "#FF0000";
    [Parameter] public EventCallback<string> SelectedColorChanged { get; set; }
    [Parameter] public bool ShowOpacity { get; set; } = true;
    [Parameter] public bool ShowHexInput { get; set; } = true;
    [Parameter] public bool ShowPreview { get; set; } = true;

    protected override void OnInitialized()
    {
        // Seeded here rather than after the first render. These two only feed the preview swatch and
        // the hex box, and assigning them in OnAfterRenderAsync leaves both showing the field's
        // compile-time #FF0000 - that method does not queue a render, so nothing redraws them until
        // the user drags the spectrum. The canvases were right and the readout beside them was red.
        currentHex = SelectedColor;
        currentCss = SelectedColor;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            var loaded = await JS.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/Shiny.Blazor.Controls/color-picker.js");
            if (disposed) { await loaded.ReleaseLateAsync(); return; }
            module = loaded;

            selfRef = DotNetObjectReference.Create(this);
            await module.InvokeVoidAsync("init", spectrumEl, hueEl, opacityEl, selfRef, SelectedColor, ShowOpacity);
            initialized = true;

            if (currentHex != SelectedColor)
            {
                // A SelectedColor that arrived between OnInitialized and the first render.
                currentHex = SelectedColor;
                currentCss = SelectedColor;
                this.StateHasChanged();
            }
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!initialized || module is null)
            return;

        if (currentHex != SelectedColor)
        {
            currentHex = SelectedColor;
            currentCss = SelectedColor;
            await module.InvokeVoidAsync("setColor", spectrumEl, SelectedColor);
        }
    }

    [JSInvokable]
    public async Task OnColorChanged(string hex, string css)
    {
        currentHex = hex;
        currentCss = css;

        if (SelectedColorChanged.HasDelegate)
            await SelectedColorChanged.InvokeAsync(hex);

        StateHasChanged();
    }

    async Task OnHexInput(ChangeEventArgs e)
    {
        var text = e.Value?.ToString()?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        if (!text.StartsWith('#'))
            text = "#" + text;

        if (text.Length is 7 or 9)
        {
            currentHex = text;
            currentCss = text;

            if (module is not null)
                await module.InvokeVoidAsync("setColor", spectrumEl, text);

            if (SelectedColorChanged.HasDelegate)
                await SelectedColorChanged.InvokeAsync(text);
        }
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        if (module is not null)
        {
            try { await module.InvokeVoidAsync("dispose", spectrumEl); } catch { }
            try { await module.DisposeAsync(); } catch { }
        }
        selfRef?.Dispose();
    }
}

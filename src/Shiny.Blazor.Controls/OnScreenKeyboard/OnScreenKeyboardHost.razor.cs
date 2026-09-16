using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls.OnScreenKeyboard;

public partial class OnScreenKeyboardHost : IAsyncDisposable
{
    [Inject] IJSRuntime JS { get; set; } = default!;
    [Inject] IOnScreenKeyboardService Keyboard { get; set; } = default!;
    [Inject] OnScreenKeyboardOptions Options { get; set; } = default!;

    /// <summary>Extra classes on the keyboard container, for host-specific styling.</summary>
    [Parameter] public string? CssClass { get; set; }

    IJSObjectReference? module;
    bool disposed;
    DotNetObjectReference<OnScreenKeyboardHost>? selfRef;
    CancellationTokenSource? repeatCts;

    bool shift;
    bool caps;
    bool symbols;
    bool appliedVisible;
    double appliedInset = -1;

    // ---- rendering -------------------------------------------------------------------------

    IEnumerable<IReadOnlyList<OnScreenKey>> Rows
    {
        get
        {
            var layer = this.symbols ? OnScreenKeyboardLayout.Symbols : OnScreenKeyboardLayout.Letters;
            foreach (var row in layer)
                yield return row;

            yield return OnScreenKeyboardLayout.BottomRow;
        }
    }

    string ThemeClass => this.Options.Theme switch
    {
        OnScreenKeyboardTheme.Light => "theme-light",
        OnScreenKeyboardTheme.Dark => "theme-dark",
        _ => "theme-auto"
    };

    string RootStyle
    {
        get
        {
            var height = this.Options.HeightPx.ToString(CultureInfo.InvariantCulture);
            return $"--shiny-osk-height: {height}px;";
        }
    }

    static string Flex(OnScreenKey key)
        => $"{key.Width.ToString(CultureInfo.InvariantCulture)} 1 0";

    /// <summary>
    /// What the key types right now. Shift wins where a key has a shifted face; Caps Lock only
    /// raises letters, which is why it cannot be modelled as a third layer.
    /// </summary>
    string Resolve(OnScreenKey key)
    {
        if (this.shift && key.ShiftValue is not null)
            return key.ShiftValue;

        if (this.caps && key.Value.Length == 1 && char.IsLetter(key.Value[0]))
            return key.Value.ToUpperInvariant();

        return key.Value;
    }

    string FaceFor(OnScreenKey key) => key.Kind switch
    {
        OnScreenKeyKind.Character => this.Resolve(key),
        OnScreenKeyKind.Space => "",
        OnScreenKeyKind.Layer => this.symbols ? "ABC" : "123",
        _ => key.Glyph ?? ""
    };

    string AriaFor(OnScreenKey key) => key.Kind switch
    {
        OnScreenKeyKind.Character => this.Resolve(key),
        OnScreenKeyKind.Layer => this.symbols ? "Letters" : "Numbers and symbols",
        _ => key.AriaLabel ?? key.Value
    };

    /// <summary>Only the latching keys report a pressed state; everything else must not claim one.</summary>
    string? AriaPressedFor(OnScreenKey key) => key.Kind switch
    {
        OnScreenKeyKind.Shift => this.shift ? "true" : "false",
        OnScreenKeyKind.CapsLock => this.caps ? "true" : "false",
        OnScreenKeyKind.Layer => this.symbols ? "true" : "false",
        _ => null
    };

    string? StateClass(OnScreenKey key) => key.Kind switch
    {
        OnScreenKeyKind.Shift when this.shift => "is-latched",
        OnScreenKeyKind.CapsLock when this.caps => "is-latched",
        OnScreenKeyKind.Layer when this.symbols => "is-latched",
        OnScreenKeyKind.Space => "is-space",
        _ => null
    };

    // ---- input -----------------------------------------------------------------------------

    async Task PressAsync(OnScreenKey key)
    {
        this.CancelRepeat();

        // Resolve before Shift is consumed, so a held key keeps repeating the character it typed
        // first rather than dropping to lowercase on the second repeat.
        var text = key.Kind == OnScreenKeyKind.Character ? this.Resolve(key) : key.Value;
        await this.ApplyAsync(key, text);

        if (!key.Repeats)
            return;

        var cts = new CancellationTokenSource();
        this.repeatCts = cts;
        try
        {
            await Task.Delay(this.Options.AutoRepeatDelay, cts.Token);
            while (!cts.IsCancellationRequested)
            {
                await this.ApplyAsync(key, text);
                await Task.Delay(this.Options.AutoRepeatInterval, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // the finger came off the key
        }
    }

    void Release() => this.CancelRepeat();

    void CancelRepeat()
    {
        this.repeatCts?.Cancel();
        this.repeatCts?.Dispose();
        this.repeatCts = null;
    }

    async Task ApplyAsync(OnScreenKey key, string text)
    {
        if (this.module is null)
            return;

        try
        {
            switch (key.Kind)
            {
                case OnScreenKeyKind.Character:
                    await this.module.InvokeAsync<bool>("insert", text);
                    this.ConsumeShift();
                    break;

                case OnScreenKeyKind.Space:
                    await this.module.InvokeAsync<bool>("insert", " ");
                    this.ConsumeShift();
                    break;

                case OnScreenKeyKind.Backspace:
                    await this.module.InvokeAsync<bool>("backspace");
                    break;

                case OnScreenKeyKind.Enter:
                    await this.module.InvokeAsync<bool>("enter", this.Options.EnterInsertsNewLine);
                    break;

                case OnScreenKeyKind.Tab:
                    await this.module.InvokeAsync<bool>("tab", this.shift);
                    this.ConsumeShift();
                    break;

                case OnScreenKeyKind.Arrow:
                    await this.module.InvokeAsync<bool>("move", key.Value);
                    break;

                case OnScreenKeyKind.Shift:
                    this.shift = !this.shift;
                    this.StateHasChanged();
                    break;

                case OnScreenKeyKind.CapsLock:
                    this.caps = !this.caps;
                    this.shift = false;
                    this.StateHasChanged();
                    break;

                case OnScreenKeyKind.Layer:
                    this.symbols = !this.symbols;
                    this.shift = false;
                    this.StateHasChanged();
                    break;

                case OnScreenKeyKind.Hide:
                    await this.module.InvokeAsync<bool>("dismiss");
                    this.Keyboard.Hide();
                    break;
            }
        }
        catch (JSDisconnectedException)
        {
            // the circuit/page went away mid-keystroke
        }
    }

    /// <summary>Shift is momentary — one character and it drops. Caps Lock is the sticky one.</summary>
    void ConsumeShift()
    {
        if (!this.shift)
            return;

        this.shift = false;
        this.StateHasChanged();
    }

    // ---- lifecycle -------------------------------------------------------------------------

    protected override void OnInitialized()
        => this.Keyboard.VisibilityChanged += this.OnVisibilityChanged;

    void OnVisibilityChanged(object? sender, bool visible)
        => _ = this.InvokeAsync(this.StateHasChanged);

    /// <summary>
    /// Called from the browser when a text field gains or loses focus. The JS half reports the raw
    /// event and leaves the policy here, so flipping <c>AutoShowOnFocus</c> at runtime takes effect
    /// without re-registering the listeners.
    /// </summary>
    [JSInvokable]
    public Task OnFocusChangedJs(bool focused)
    {
        if (focused)
        {
            if (this.Options.AutoShowOnFocus)
                this.Keyboard.Show();
        }
        else if (this.Options.AutoHideOnBlur)
        {
            this.Keyboard.Hide();
        }

        return Task.CompletedTask;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            if (firstRender)
            {
                var loaded = await this.JS.InvokeAsync<IJSObjectReference>(
                    "import",
                    "./_content/Shiny.Blazor.Controls/onscreen-keyboard.js"
                );
                if (this.disposed) { await loaded.ReleaseLateAsync(); return; }
                this.module = loaded;
                this.selfRef = DotNetObjectReference.Create(this);
                await this.module.InvokeVoidAsync("observe", this.selfRef);
            }

            if (this.module is null)
                return;

            var inset = this.Keyboard.IsVisible && this.Options.PushContent ? this.Options.HeightPx : 0;
            if (Math.Abs(inset - this.appliedInset) > 0.5)
            {
                this.appliedInset = inset;
                await this.module.InvokeVoidAsync("setInset", inset);
            }

            if (this.Keyboard.IsVisible != this.appliedVisible)
            {
                this.appliedVisible = this.Keyboard.IsVisible;

                // Padding the body is not enough on its own — the field may already be sitting in
                // the region the keys are about to cover.
                if (this.appliedVisible)
                    await this.module.InvokeVoidAsync("reveal", this.Options.HeightPx);
            }
        }
        catch (JSDisconnectedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        this.disposed = true;
        this.Keyboard.VisibilityChanged -= this.OnVisibilityChanged;
        this.CancelRepeat();

        if (this.module is not null)
        {
            try
            {
                await this.module.InvokeVoidAsync("unobserve");
                await this.module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        this.selfRef?.Dispose();
    }
}

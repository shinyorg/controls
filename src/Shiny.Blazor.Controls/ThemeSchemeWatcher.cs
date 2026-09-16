using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls;

/// <summary>
/// Reports which colour scheme - light or dark - is in force at a given element, and raises
/// <see cref="Changed"/> when that flips.
/// </summary>
/// <remarks>
/// <para>
/// Controls whose surface is CSS can simply consume the <c>--shiny-color-*</c> tokens and never
/// need this. It exists for the ones that paint their own pixels - the Skia-backed spreadsheet,
/// document and slide surfaces - where the theme has to arrive as a value rather than as an
/// inherited colour.
/// </para>
/// <para>
/// The scheme is read from the element's computed <c>color-scheme</c>, which the generated theme
/// sets on the very scope that carries the colour tokens. That is deliberately not
/// <c>matchMedia</c>: an app that flips the theme by putting <c>.shiny-theme-dark</c> on a
/// container - which is the common case, since a Blazor app rarely owns <c>&lt;html&gt;</c> - never
/// changes the OS preference, so a media query would report the wrong answer forever.
/// </para>
/// </remarks>
public sealed partial class ThemeSchemeWatcher : IAsyncDisposable
{
    const string ModulePath = "./_content/Shiny.Blazor.Controls/theme.js";

    readonly IJSRuntime js;
    readonly string token = Guid.NewGuid().ToString("N");
    readonly Func<Task> onChanged;

    IJSObjectReference? module;
    DotNetObjectReference<ThemeSchemeWatcher>? self;
    ElementReference element;
    bool started;
    bool disposed;

    public ThemeSchemeWatcher(IJSRuntime js, Func<Task> onChanged)
    {
        this.js = js;
        this.onChanged = onChanged;
    }

    /// <summary>True once the element has been observed to sit in a dark scope.</summary>
    public bool IsDark { get; private set; }

    /// <summary>
    /// The app's neutral colour tokens as they resolve at the watched element, or null before the
    /// first read (and wherever the tokens are not in play).
    /// </summary>
    /// <remarks>
    /// Read alongside the scheme rather than separately: a painted surface needs both at the same
    /// moment, and the scheme flipping is exactly when the tokens change.
    /// </remarks>
    public SurfaceTokens? Surface { get; private set; }

    /// <summary>The resolved colours, as the browser reports them.</summary>
    public sealed record SurfaceTokens(
        string Surface,
        string OnSurface,
        string SurfaceContainer,
        string SurfaceContainerLow,
        string OnSurfaceVariant,
        string Outline,
        string OutlineVariant);

    /// <summary>
    /// Begins watching <paramref name="element"/>. Safe to call from every <c>OnAfterRenderAsync</c>;
    /// only the first call does anything.
    /// </summary>
    public async Task StartAsync(ElementReference element)
    {
        if (this.started)
            return;

        this.started = true;

        try
        {
            var loaded = await this.js.InvokeAsync<IJSObjectReference>("import", ModulePath);

            // Disposed while the import was in flight: a watch started now would hang a MutationObserver
            // on <html> and a media-query listener that call back into this watcher forever.
            if (this.disposed)
            {
                await loaded.ReleaseLateAsync();
                return;
            }

            this.module = loaded;
            this.self = DotNetObjectReference.Create(this);

            this.element = element;

            var scheme = await this.module.InvokeAsync<string>("watchScheme", this.token, element, this.self, nameof(OnSchemeChanged));
            await this.OnSchemeChanged(scheme);
        }
        catch (JSDisconnectedException)
        {
            // circuit went away mid-start; nothing to clean up that DisposeAsync will not handle
        }
        catch (OperationCanceledException)
        {
        }
    }

    [JSInvokable]
    public async Task OnSchemeChanged(string scheme)
    {
        var dark = string.Equals(scheme, "dark", StringComparison.OrdinalIgnoreCase);

        // Not short-circuited on `dark == IsDark`: the very first call arrives with whichever value
        // the element already had, and returning early there would leave Surface null forever.
        this.IsDark = dark;

        try
        {
            if (this.module is not null)
                this.Surface = await this.module.InvokeAsync<SurfaceTokens>("readSurface", this.element);
        }
        catch (JSException)
        {
            // The tokens are optional - a host that never referenced the theme stylesheet still gets
            // a working control, drawn in the painter's own palette.
        }
        catch (JSDisconnectedException)
        {
            return;
        }

        await this.onChanged();
    }

    /// <summary>
    /// Parses one of the browser's <c>rgb()</c> / <c>rgba()</c> strings.
    /// </summary>
    /// <remarks>
    /// Only those two forms are handled, and that is enough: these come from
    /// <c>getComputedStyle().color</c>, which normalises every notation - hex, hsl, a named colour -
    /// to one of them before it is read.
    /// </remarks>
    public static bool TryParseColor(string? value, out byte a, out byte r, out byte g, out byte b)
    {
        a = 255;
        r = g = b = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = ColorParts().Matches(value);
        if (parts.Count < 3)
            return false;

        r = Channel(parts[0].Value);
        g = Channel(parts[1].Value);
        b = Channel(parts[2].Value);

        if (parts.Count > 3 && double.TryParse(parts[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var alpha))
            a = (byte)Math.Clamp(Math.Round(alpha * 255), 0, 255);

        return true;

        static byte Channel(string text)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? (byte)Math.Clamp(Math.Round(v), 0, 255)
                : (byte)0;
    }

    [GeneratedRegex(@"[\d.]+")]
    private static partial Regex ColorParts();

    public async ValueTask DisposeAsync()
    {
        this.disposed = true;
        try
        {
            if (this.module is not null)
            {
                await this.module.InvokeVoidAsync("unwatchScheme", this.token);
                await this.module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
        }
        catch (OperationCanceledException)
        {
        }

        this.self?.Dispose();
    }
}

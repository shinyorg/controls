using Microsoft.JSInterop;

namespace Shiny.Blazor.Controls.Theming;

/// <inheritdoc cref="IShinyThemeService"/>
public sealed class ShinyThemeService(IJSRuntime js) : IShinyThemeService, IAsyncDisposable
{
    readonly Dictionary<string, string> overrides = new(StringComparer.Ordinal);
    IJSObjectReference? module;

    public string? Pack { get; private set; }

    public ShinyThemeMode Mode { get; private set; } = ShinyThemeMode.System;

    public IReadOnlyDictionary<string, string> Overrides => this.overrides;

    public event EventHandler? Changed;


    public async Task SetPackAsync(string? pack)
    {
        var slug = String.IsNullOrWhiteSpace(pack) ? null : pack.Trim().ToLowerInvariant();
        if (slug == this.Pack)
            return;

        this.Pack = slug;

        // The stylesheet itself is rendered by ShinyThemeHost - a <link> belongs in the markup, where
        // Blazor can take it away again, rather than being appended to <head> by hand and left there.
        this.Raise();
        await Task.CompletedTask;
    }


    public async Task SetModeAsync(ShinyThemeMode mode)
    {
        if (mode == this.Mode)
            return;

        this.Mode = mode;
        await this.ApplyAsync();
        this.Raise();
    }


    public async Task SetOverridesAsync(IReadOnlyDictionary<string, string>? tokens)
    {
        this.overrides.Clear();

        if (tokens is not null)
        {
            foreach (var (token, value) in tokens)
                this.overrides[Normalize(token)] = value;
        }

        await this.ApplyAsync();
        this.Raise();
    }


    public async Task SetOverrideAsync(string token, string? value)
    {
        var key = Normalize(token);

        if (String.IsNullOrWhiteSpace(value))
            this.overrides.Remove(key);
        else
            this.overrides[key] = value;

        await this.ApplyAsync();
        this.Raise();
    }


    public async Task ResetAsync()
    {
        this.Pack = null;
        this.Mode = ShinyThemeMode.System;
        this.overrides.Clear();

        await this.ApplyAsync();
        this.Raise();
    }


    /// <summary>
    /// The scheme class and the token overrides both go on the document root.
    /// </summary>
    /// <remarks>
    /// On <c>&lt;html&gt;</c> rather than on a container, because <c>color-scheme</c> is declared
    /// alongside the colour tokens and that is what makes the browser's own widgets — selects,
    /// scrollbars, date pickers — follow the theme. A class on a div themes everything except those.
    /// </remarks>
    async Task ApplyAsync()
    {
        try
        {
            this.module ??= await js.InvokeAsync<IJSObjectReference>(
                "import",
                "./_content/Shiny.Blazor.Controls/theme.js"
            );

            await this.module.InvokeVoidAsync(
                "applyTheme",
                this.Mode switch
                {
                    ShinyThemeMode.Light => "light",
                    ShinyThemeMode.Dark => "dark",
                    _ => "system"
                },
                this.overrides
            );
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone; there is no document left to theme.
        }
        catch (OperationCanceledException)
        {
        }
    }


    /// <summary>Accepts <c>primary</c>, <c>--shiny-primary</c> or <c>shiny-primary</c> alike.</summary>
    static string Normalize(string token)
    {
        var name = token.Trim();

        if (name.StartsWith("--", StringComparison.Ordinal))
            name = name[2..];

        if (name.StartsWith("shiny-", StringComparison.Ordinal))
            name = name["shiny-".Length..];

        return name;
    }


    void Raise() => this.Changed?.Invoke(this, EventArgs.Empty);


    public async ValueTask DisposeAsync()
    {
        if (this.module is null)
            return;

        try
        {
            await this.module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }
}

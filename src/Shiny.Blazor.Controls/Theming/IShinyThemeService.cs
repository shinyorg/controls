namespace Shiny.Blazor.Controls.Theming;

/// <summary>
/// The theme in force: which pack, which colour scheme, and any tokens the app is overriding.
/// </summary>
/// <remarks>
/// <para>
/// Before this existed, "switching theme" on Blazor meant injecting a <c>&lt;link&gt;</c> element and
/// relying on source order, and "dark mode" meant putting a class on a div by hand — every app
/// reinvented both, and the sample's version was the reference implementation by accident.
/// </para>
/// <para>
/// <b>Scoped, not singleton.</b> The chosen theme is per-user state. Under WebAssembly the two are
/// the same thing, so a singleton would only show up on Blazor Server — where it would have given
/// every connected user whichever theme the last one picked.
/// </para>
/// </remarks>
public interface IShinyThemeService
{
    /// <summary>The theme pack slug (<c>ocean</c>, <c>terminal</c>, …), or null for the built-in Basic.</summary>
    string? Pack { get; }

    /// <summary>The colour scheme in force.</summary>
    ShinyThemeMode Mode { get; }

    /// <summary>
    /// Authoring tokens the app is overriding, without the <c>--shiny-</c> prefix
    /// (<c>primary</c>, <c>radius</c>, …). Written onto the document root, so they beat the
    /// stylesheet without any specificity fight.
    /// </summary>
    IReadOnlyDictionary<string, string> Overrides { get; }

    /// <summary>Raised whenever any of the above changes.</summary>
    event EventHandler? Changed;

    /// <summary>Switches theme pack. Null selects the built-in Basic.</summary>
    Task SetPackAsync(string? pack);

    /// <summary>Switches the colour scheme.</summary>
    Task SetModeAsync(ShinyThemeMode mode);

    /// <summary>
    /// Replaces the token overrides wholesale. Pass null or an empty map to drop back to the pack's
    /// own values.
    /// </summary>
    Task SetOverridesAsync(IReadOnlyDictionary<string, string>? tokens);

    /// <summary>Overrides one token, leaving the rest alone. A null value removes it.</summary>
    Task SetOverrideAsync(string token, string? value);

    /// <summary>Back to the built-in theme, the system scheme and no overrides.</summary>
    Task ResetAsync();
}

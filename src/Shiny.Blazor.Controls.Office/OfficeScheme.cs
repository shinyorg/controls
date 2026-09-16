using Shiny.Blazor.Controls;
using Shiny.Controls.Office.Skia;
using Shiny.Controls.Office.Spreadsheet;

namespace Shiny.Blazor.Controls.Office;

/// <summary>
/// Turns what a <see cref="ThemeSchemeWatcher"/> has read off the page into the palette a painted
/// Office surface draws with.
/// </summary>
/// <remarks>
/// The MAUI package resolves the same thing out of <c>Application.Current.Resources</c>. Both end at
/// <see cref="OfficeSurface"/>, which is where the decision about what a token means to a grid or a
/// page actually lives — so the two hosts cannot drift into theming these differently.
/// </remarks>
static class OfficeScheme
{
    public static SpreadsheetTheme Resolve(ThemeSchemeWatcher? scheme, SpreadsheetTheme? explicitTheme)
    {
        if (explicitTheme is { } given)
            return given;

        var baseline = scheme?.IsDark == true ? SpreadsheetTheme.Dark : SpreadsheetTheme.Light;
        return Surface(scheme) is { } surface ? surface.Apply(baseline) : baseline;
    }

    public static DocumentTheme Resolve(ThemeSchemeWatcher? scheme, DocumentTheme? explicitTheme)
    {
        if (explicitTheme is { } given)
            return given;

        var baseline = scheme?.IsDark == true ? DocumentTheme.Dark : DocumentTheme.Light;
        return Surface(scheme) is { } surface ? surface.Apply(baseline) : baseline;
    }

    public static SlideTheme Resolve(ThemeSchemeWatcher? scheme, SlideTheme? explicitTheme)
    {
        if (explicitTheme is { } given)
            return given;

        var baseline = scheme?.IsDark == true ? SlideTheme.Dark : SlideTheme.Light;
        return Surface(scheme) is { } surface ? surface.Apply(baseline) : baseline;
    }

    public static NotebookTheme Resolve(ThemeSchemeWatcher? scheme, NotebookTheme? explicitTheme)
    {
        if (explicitTheme is { } given)
            return given;

        var baseline = scheme?.IsDark == true ? NotebookTheme.Dark : NotebookTheme.Light;
        return Surface(scheme) is { } surface ? surface.Apply(baseline) : baseline;
    }

    /// <summary>
    /// The theme scoping class a view's root carries while its <c>Theme</c> is pinned, or null while it
    /// follows the page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A pinned theme used to repaint only what is drawn from it. The canvas went dark, while the ribbon,
    /// its pickers, the formula bar and the sheet tabs - all plain CSS over the <c>--shiny-color-*</c>
    /// tokens - stayed on the page's light palette. The theme stylesheets already honour
    /// <c>.shiny-theme-dark</c> / <c>.shiny-theme-light</c> on any container, re-deriving every token
    /// under it, so putting the matching class on the view's root re-themes all of that chrome without
    /// any of it knowing. This is the Blazor twin of the MAUI toolbar merging the matching token
    /// dictionary over its subtree.
    /// </para>
    /// <para>
    /// Whether the pin is light or dark is read off the theme's ground rather than compared by reference,
    /// so a custom theme derived from <c>Dark</c> scopes the same way. <c>shiny-office-scoped</c> lets the
    /// view give the root the scope's own ink and ground - text that only <c>inherit</c>s its colour would
    /// otherwise keep the page's dark ink on the scoped dark surface.
    /// </para>
    /// <para>
    /// The canvas's <see cref="ThemeSchemeWatcher"/> sits inside the root, so it sees the class come and
    /// go; that is harmless while pinned (the pin wins) and is exactly what re-reads the page's palette
    /// once the pin is removed.
    /// </para>
    /// </remarks>
    public static string? ScopeClass(SpreadsheetTheme? pinned)
        => pinned is null ? null : ScopeFor(pinned.Background);

    public static string? ScopeClass(DocumentTheme? pinned)
        => pinned is null ? null : ScopeFor(pinned.PageBackground);

    public static string? ScopeClass(NotebookTheme? pinned)
        => pinned is null ? null : ScopeFor(pinned.Paper);

    /// <remarks>
    /// Read off the slide border rather than the surround: a slide theme darkens only its surround, and
    /// even the light theme's surround is a dark grey, so the surround cannot tell the two apart.
    /// </remarks>
    public static string? ScopeClass(SlideTheme? pinned)
        => pinned is null ? null : ScopeFor(pinned.Border);

    internal const string ScopedMarker = "shiny-office-scoped";

    static string ScopeFor(ArgbColor ground)
        => IsLightGround(ground)
            ? "shiny-theme-light " + ScopedMarker
            : "shiny-theme-dark " + ScopedMarker;

    static bool IsLightGround(ArgbColor ground)
        => (0.299 * ground.R + 0.587 * ground.G + 0.114 * ground.B) / 255d >= 0.5;

    /// <summary>
    /// The palette, or null when any one token could not be read.
    /// </summary>
    /// <remarks>
    /// All or nothing: a half-resolved palette puts two of the app's neutrals beside four of the
    /// painter's defaults, which reads far worse than either set on its own.
    /// </remarks>
    static OfficeSurface? Surface(ThemeSchemeWatcher? scheme)
    {
        if (scheme?.Surface is not { } tokens)
            return null;

        if (Parse(tokens.Surface) is not { } surface ||
            Parse(tokens.OnSurface) is not { } onSurface ||
            Parse(tokens.SurfaceContainer) is not { } container ||
            Parse(tokens.SurfaceContainerLow) is not { } containerLow ||
            Parse(tokens.OnSurfaceVariant) is not { } onSurfaceVariant ||
            Parse(tokens.Outline) is not { } outline ||
            Parse(tokens.OutlineVariant) is not { } outlineVariant)
        {
            return null;
        }

        return new OfficeSurface(
            surface,
            onSurface,
            container,
            containerLow,
            onSurfaceVariant,
            outline,
            outlineVariant);
    }

    static ArgbColor? Parse(string? value)
        => ThemeSchemeWatcher.TryParseColor(value, out var a, out var r, out var g, out var b)
            ? new ArgbColor(a, r, g, b)
            : null;
}

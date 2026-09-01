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

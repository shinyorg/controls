using Shiny.Blazor.Controls;
using Shiny.Controls.FloorPlan;

namespace Shiny.Blazor.Controls.FloorPlan;

/// <summary>
/// Turns what a <see cref="ThemeSchemeWatcher"/> has read off the page into the palette the plan is
/// painted with.
/// </summary>
/// <remarks>
/// The MAUI package resolves the same thing out of <c>Application.Current.Resources</c>. Both end at
/// <see cref="FloorPlanSurface"/>, which is where the decision about what a token means to a plan
/// actually lives - so the two hosts cannot drift into theming this differently.
/// </remarks>
static class FloorPlanScheme
{
    public static FloorPlanTheme Resolve(ThemeSchemeWatcher? scheme, FloorPlanTheme? explicitTheme)
    {
        if (explicitTheme is { } given)
            return given;

        var baseline = scheme?.IsDark == true ? FloorPlanTheme.Dark : FloorPlanTheme.Light;
        return Surface(scheme) is { } surface ? surface.Apply(baseline) : baseline;
    }

    /// <summary>
    /// The palette, or null when any one token could not be read.
    /// </summary>
    /// <remarks>
    /// All or nothing: a half-resolved palette puts two of the app's neutrals beside four of the
    /// painter's defaults, which reads far worse than either set on its own.
    /// </remarks>
    static FloorPlanSurface? Surface(ThemeSchemeWatcher? scheme)
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

        return new FloorPlanSurface(
            surface,
            onSurface,
            container,
            containerLow,
            onSurfaceVariant,
            outline,
            outlineVariant
        );
    }

    static PlanColor? Parse(string? value)
        => ThemeSchemeWatcher.TryParseColor(value, out var a, out var r, out var g, out var b)
            ? new PlanColor(a, r, g, b)
            : null;
}

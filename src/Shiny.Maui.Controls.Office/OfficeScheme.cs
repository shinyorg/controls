using Shiny.Controls.Office.Skia;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// Resolves the spreadsheet chrome theme for a control that has not been given one, and keeps it
/// in step with the OS appearance.
/// </summary>
/// <remarks>
/// The grid, its headers and its toolbars are drawn rather than composed from themed views, so the
/// scheme has to arrive as a value. Defaulting that value to <see cref="SpreadsheetTheme.Light"/>
/// meant a workbook in a dark app rendered a stark white sheet with a white toolbar above it, and no
/// amount of app-level theming could correct it - the host had to notice and set <c>Theme</c> by hand.
/// </remarks>
static class OfficeScheme
{
    static bool IsDark => Application.Current?.RequestedTheme == AppTheme.Dark;

    /// <summary>The theme to draw with when the control's own <c>Theme</c> is unset.</summary>
    public static SpreadsheetTheme Default
    {
        get
        {
            var baseline = IsDark ? SpreadsheetTheme.Dark : SpreadsheetTheme.Light;
            return Surface() is { } surface ? surface.Apply(baseline) : baseline;
        }
    }

    /// <summary>Document chrome to draw with when the control's own <c>Theme</c> is unset.</summary>
    public static DocumentTheme DefaultDocument
    {
        get
        {
            var baseline = IsDark ? DocumentTheme.Dark : DocumentTheme.Light;
            return Surface() is { } surface ? surface.Apply(baseline) : baseline;
        }
    }

    /// <summary>
    /// The app's neutral palette, or null when the theme resources are not in play.
    /// </summary>
    /// <remarks>
    /// All or nothing, deliberately. A half-resolved palette would put two of the app's neutrals
    /// beside four of the painter's defaults, which looks far worse than either set on its own —
    /// so a single missing key falls the whole surface back to the built-in pair.
    /// </remarks>
    static OfficeSurface? Surface()
    {
        if (Application.Current?.Resources is not { } resources)
            return null;

        if (Lookup(ShinyThemeKeys.Color.Surface) is not { } surface ||
            Lookup(ShinyThemeKeys.Color.OnSurface) is not { } onSurface ||
            Lookup(ShinyThemeKeys.Color.SurfaceContainer) is not { } container ||
            Lookup(ShinyThemeKeys.Color.SurfaceContainerLow) is not { } containerLow ||
            Lookup(ShinyThemeKeys.Color.OnSurfaceVariant) is not { } onSurfaceVariant ||
            Lookup(ShinyThemeKeys.Color.Outline) is not { } outline ||
            Lookup(ShinyThemeKeys.Color.OutlineVariant) is not { } outlineVariant)
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

        ArgbColor? Lookup(string key)
            => resources.TryGetValue(key, out var value) && value is Color color
                ? new ArgbColor(
                    (byte)(color.Alpha * 255),
                    (byte)(color.Red * 255),
                    (byte)(color.Green * 255),
                    (byte)(color.Blue * 255))
                : null;
    }

    /// <summary>Deck chrome to draw with when the control's own <c>Theme</c> is unset.</summary>
    /// <remarks>
    /// <see cref="SlideTheme.Dark"/> darkens the surround and leaves the slide itself alone, so
    /// following the app here does not misrepresent the deck's authored colours.
    /// </remarks>
    public static SlideTheme DefaultSlide
    {
        get
        {
            var baseline = IsDark ? SlideTheme.Dark : SlideTheme.Light;
            return Surface() is { } surface ? surface.Apply(baseline) : baseline;
        }
    }

    /// <summary>Notebook colours to draw with when the control's own <c>Theme</c> is unset.</summary>
    /// <remarks>
    /// Unlike the document and the deck, the page itself follows the app — see
    /// <see cref="OfficeSurface.Apply(NotebookTheme)"/> for why a notebook page is the one surface
    /// here that should.
    /// </remarks>
    public static NotebookTheme DefaultNotebook
    {
        get
        {
            var baseline = IsDark ? NotebookTheme.Dark : NotebookTheme.Light;
            return Surface() is { } surface ? surface.Apply(baseline) : baseline;
        }
    }

    /// <summary>
    /// Re-runs <paramref name="onChanged"/> against <paramref name="owner"/> whenever the OS
    /// appearance flips. The callback takes the owner rather than closing over it so that callers
    /// can pass a <c>static</c> lambda - <c>Application</c> outlives every page, and a handler that
    /// captured the control would pin every spreadsheet ever navigated to for the life of the
    /// process. Nothing to unsubscribe: the handler detaches itself once the owner is collected.
    /// </summary>
    public static void FollowAppTheme<T>(this T owner, Action<T> onChanged)
        where T : VisualElement
    {
        var app = Application.Current;
        if (app is null)
            return;

        var reference = new WeakReference<T>(owner);

        void Handler(object? sender, AppThemeChangedEventArgs args)
        {
            if (reference.TryGetTarget(out var target))
                onChanged(target);
            else
                app.RequestedThemeChanged -= Handler;
        }

        app.RequestedThemeChanged += Handler;
    }
}

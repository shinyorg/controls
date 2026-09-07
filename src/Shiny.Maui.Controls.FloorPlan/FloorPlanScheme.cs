using Shiny.Controls.FloorPlan;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.FloorPlan;

/// <summary>
/// Resolves the theme a <see cref="FloorPlanView"/> draws with when it has not been given one, and
/// keeps it in step with the OS appearance.
/// </summary>
/// <remarks>
/// The plan is painted rather than composed from themed views, so the theme has to arrive as a value.
/// Leaving that value at <see cref="FloorPlanTheme.Light"/> meant a plan in a dark app rendered a
/// stark white sheet no amount of app-level theming could correct. Mirrors
/// <c>Shiny.Blazor.Controls.FloorPlan</c>'s resolver, and both end at <see cref="FloorPlanSurface"/>.
/// </remarks>
static class FloorPlanScheme
{
    static bool IsDark => Application.Current?.RequestedTheme == AppTheme.Dark;

    /// <summary>The theme to draw with when the control's own <c>Theme</c> is unset.</summary>
    public static FloorPlanTheme Default
    {
        get
        {
            var baseline = IsDark ? FloorPlanTheme.Dark : FloorPlanTheme.Light;
            return Surface() is { } surface ? surface.Apply(baseline) : baseline;
        }
    }

    /// <summary>
    /// The app's neutral palette, or null when the theme resources are not in play.
    /// </summary>
    /// <remarks>
    /// All or nothing. A half-resolved palette puts two of the app's neutrals beside four of the
    /// painter's defaults, which reads worse than either set on its own.
    /// </remarks>
    static FloorPlanSurface? Surface()
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

        return new FloorPlanSurface(
            surface,
            onSurface,
            container,
            containerLow,
            onSurfaceVariant,
            outline,
            outlineVariant
        );

        PlanColor? Lookup(string key)
            => resources.TryGetValue(key, out var value) && value is Color color
                ? new PlanColor(
                    (byte)(color.Alpha * 255),
                    (byte)(color.Red * 255),
                    (byte)(color.Green * 255),
                    (byte)(color.Blue * 255))
                : null;
    }

    /// <summary>
    /// Re-runs <paramref name="onChanged"/> against <paramref name="owner"/> whenever the OS
    /// appearance flips.
    /// </summary>
    /// <remarks>
    /// The callback takes the owner so that callers can pass a <c>static</c> lambda: Application
    /// outlives every page, and a handler that captured the view would pin every plan ever navigated
    /// to for the life of the process. Nothing to unsubscribe - the handler detaches itself once the
    /// owner is collected.
    /// </remarks>
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

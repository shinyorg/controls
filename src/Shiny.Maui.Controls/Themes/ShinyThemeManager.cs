using Microsoft.Maui.Handlers;

namespace Shiny.Maui.Controls.Themes;

/// <summary>
/// Applies the active <see cref="IShinyTheme"/> to the running application and keeps it in
/// sync with the OS light/dark appearance. Controls consume the merged token resources via
/// <c>SetDynamicResource</c>, so swapping the dictionary here restyles the whole control set
/// live — no per-control work required.
/// </summary>
public static class ShinyThemeManager
{
    const string MapperKey = "ShinyThemeApply";

    static ResourceDictionary? applied;
    static bool hookedAppearance;
    static bool hookedHandlers;

    /// <summary>The currently selected theme (may not yet be applied if the app has not started).</summary>
    public static IShinyTheme? CurrentTheme { get; private set; }

    /// <summary>
    /// Select the active theme. Safe to call during <c>UseShinyControls</c> (before the app
    /// exists) — application is deferred until startup — or at any time afterwards to switch live.
    /// </summary>
    public static void SetTheme(IShinyTheme theme)
    {
        CurrentTheme = theme;
        if (Application.Current is not null)
            EnsureApplied();
    }

    /// <summary>
    /// Applies the theme as the app and its windows get their handlers. Controls bind token resources
    /// at construction; if the dictionary is merged after that, their brushes stay unresolved (which
    /// crashes the Windows stroke mapper).
    /// </summary>
    /// <remarks>
    /// On <see cref="ElementHandler.ElementMapper"/>, not <c>ApplicationHandler.Mapper</c>: that one
    /// only serves MAUI's own backends. The maui-labs AppKit and GTK backends bring application and
    /// window handlers of their own, which chain <see cref="ElementHandler.ElementMapper"/> but not
    /// MAUI's, so a hook there never ran and every token-bound colour stayed unset - a quick entry
    /// card with no fill, text floating on nothing.
    /// </remarks>
    internal static void HookHandlers()
    {
        if (hookedHandlers)
            return;

        hookedHandlers = true;
        ElementHandler.ElementMapper.PrependToMapping(MapperKey, (_, element) =>
        {
            if (element is IApplication or IWindow)
                EnsureApplied();
        });
    }

    /// <summary>
    /// Applies the current theme if the application is available. Invoked at startup once the app
    /// or a window gets its handler (when <see cref="Application.Current"/> is guaranteed) and again
    /// whenever the theme or OS appearance changes.
    /// </summary>
    internal static void EnsureApplied()
    {
        var app = Application.Current;
        if (app is null || CurrentTheme is null)
            return;

        if (!hookedAppearance)
        {
            app.RequestedThemeChanged += (_, _) => ApplyScheme();
            hookedAppearance = true;
        }

        ApplyScheme();
    }

    static void ApplyScheme()
    {
        var app = Application.Current;
        if (app is null || CurrentTheme is null)
            return;

        var next = app.RequestedTheme == AppTheme.Dark
            ? CurrentTheme.Dark
            : CurrentTheme.Light;

        var merged = app.Resources.MergedDictionaries;
        if (ReferenceEquals(applied, next))
            return;

        if (applied is not null)
            merged.Remove(applied);

        merged.Add(next);
        applied = next;
    }
}

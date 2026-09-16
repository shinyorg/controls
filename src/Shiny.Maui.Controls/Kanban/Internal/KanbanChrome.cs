using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Kanban.Internal;

/// <summary>
/// The small shared paint jobs the Kanban's own views need: turning the model's colour strings into
/// <see cref="Color"/>s, and picking readable ink to sit on one.
/// </summary>
/// <remarks>
/// The model carries colours as strings so <c>KanbanCard</c> and friends are one file, mirrored
/// verbatim into the Blazor control. That leaves exactly one place - here - where a string becomes a
/// <see cref="Color"/>, and one place to get the parse failure right: a colour nobody can parse falls
/// back rather than throwing, because a typo in a hex string should cost you a tint, not the page.
/// </remarks>
static class KanbanChrome
{
    public static Color? Parse(string? value)
    {
        if (String.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            return Color.Parse(value);
        }
        catch (Exception)
        {
            return null;
        }
    }


    /// <summary>Black or white, whichever is readable on the given fill.</summary>
    public static Color InkOn(Color fill)
        => (0.299 * fill.Red) + (0.587 * fill.Green) + (0.114 * fill.Blue) > 0.6
            ? Colors.Black
            : Colors.White;


    /// <summary>
    /// Binds a colour property to a theme token, with a literal fallback applied first.
    /// </summary>
    /// <remarks>
    /// The fallback is not belt and braces. A dynamic resource whose key is not in the app's
    /// dictionaries leaves the property completely untouched, so an app that never installed a Shiny
    /// theme would render the board in whatever the property happened to default to - which for a
    /// card background is transparent.
    /// </remarks>
    public static void Token(VisualElement element, BindableProperty property, string key, Color fallback)
    {
        element.SetValue(property, fallback);
        element.SetDynamicResource(property, key);
    }


    /// <summary>
    /// The unthemed fallback for a token: that token's own value in the Basic theme, for the current
    /// system scheme.
    /// </summary>
    /// <remarks>
    /// Taken from the generated <c>BasicLightTheme.xaml</c>/<c>BasicDarkTheme.xaml</c> rather than
    /// invented, for the same reason the Blazor stylesheet's <c>var()</c> fallbacks are: a fallback
    /// only paints in an app that never installed a theme, so a wrong one is invisible everywhere it
    /// is tested and load-bearing in the one place it is not.
    /// </remarks>
    public static Color Fallback(string key)
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;

        return key switch
        {
            ShinyThemeKeys.Color.Background => dark ? Color.FromRgb(0x0F, 0x14, 0x1A) : Color.FromRgb(0xF6, 0xFA, 0xFE),
            ShinyThemeKeys.Color.Surface => dark ? Color.FromRgb(0x0F, 0x14, 0x1A) : Color.FromRgb(0xF6, 0xFA, 0xFE),
            ShinyThemeKeys.Color.SurfaceContainerLow => dark ? Color.FromRgb(0x16, 0x1C, 0x23) : Color.FromRgb(0xEF, 0xF4, 0xFB),
            ShinyThemeKeys.Color.SurfaceContainer => dark ? Color.FromRgb(0x1A, 0x20, 0x28) : Color.FromRgb(0xE8, 0xEE, 0xF6),
            ShinyThemeKeys.Color.SurfaceContainerHigh => dark ? Color.FromRgb(0x1C, 0x21, 0x28) : Color.FromRgb(0xE2, 0xE6, 0xEA),
            ShinyThemeKeys.Color.OnSurface => dark ? Color.FromRgb(0xDB, 0xE3, 0xEE) : Color.FromRgb(0x16, 0x1C, 0x23),
            ShinyThemeKeys.Color.OnSurfaceVariant => dark ? Color.FromRgb(0xBB, 0xC7, 0xDF) : Color.FromRgb(0x3B, 0x47, 0x5B),
            ShinyThemeKeys.Color.Outline => dark ? Color.FromRgb(0x85, 0x91, 0xA7) : Color.FromRgb(0x6C, 0x78, 0x8D),
            ShinyThemeKeys.Color.OutlineVariant => dark ? Color.FromRgb(0x3B, 0x47, 0x5B) : Color.FromRgb(0xBB, 0xC7, 0xDF),
            ShinyThemeKeys.Color.Primary => dark ? Color.FromRgb(0xA3, 0xBA, 0xFF) : Color.FromRgb(0x00, 0x55, 0xD9),
            ShinyThemeKeys.Color.Error => dark ? Color.FromRgb(0xFF, 0x8A, 0x73) : Color.FromRgb(0xC2, 0x00, 0x14),
            ShinyThemeKeys.Color.ErrorContainer => dark ? Color.FromRgb(0x3D, 0x2C, 0x2D) : Color.FromRgb(0xF2, 0xCD, 0xCB),
            ShinyThemeKeys.Color.OnErrorContainer => dark ? Color.FromRgb(0xF7, 0xAB, 0x9F) : Color.FromRgb(0x83, 0x23, 0x23),
            ShinyThemeKeys.Color.Warning => dark ? Color.FromRgb(0xFF, 0xAC, 0x48) : Color.FromRgb(0x9C, 0x45, 0x00),
            ShinyThemeKeys.Color.SecondaryContainer => dark ? Color.FromRgb(0x30, 0x36, 0x3E) : Color.FromRgb(0xD0, 0xD6, 0xDC),
            ShinyThemeKeys.Color.OnSecondaryContainer => dark ? Color.FromRgb(0xC7, 0xD1, 0xDF) : Color.FromRgb(0x3E, 0x46, 0x52),
            _ => dark ? Colors.White : Colors.Black
        };
    }


    /// <summary>Token plus its own fallback - the shape nearly every call site wants.</summary>
    public static void Token(VisualElement element, BindableProperty property, string key)
        => Token(element, property, key, Fallback(key));
}

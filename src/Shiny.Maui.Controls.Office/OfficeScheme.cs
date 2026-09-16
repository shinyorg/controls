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

        // Application raises RequestedThemeChanged through a WeakEventManager, which holds only a weak
        // reference to the handler's target - here the closure above, which nothing else references.
        // Without this the handler worked until the first GC and then silently stopped, so the control
        // kept the appearance it had when that collection happened. Tying the closure's lifetime to the
        // owner keeps it exactly as long as there is something to repaint.
        ThemeSubscriptions.GetOrCreateValue(owner).Add(Handler);
    }

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, List<EventHandler<AppThemeChangedEventArgs>>> ThemeSubscriptions = new();

    /// <summary>
    /// Scopes the app theme's light or dark token set to <paramref name="owner"/>'s subtree so the
    /// themed views inside it - a ribbon, its pickers, the find box - match a pinned chrome theme.
    /// </summary>
    /// <param name="owner">The view whose descendants should follow <paramref name="pinned"/>.</param>
    /// <param name="pinned">
    /// The control's own <c>Theme</c>, or null when it follows the app - which removes the scope.
    /// </param>
    /// <remarks>
    /// A pinned <see cref="SpreadsheetTheme"/> used to repaint only what is drawn from it: the grid went
    /// dark while the ribbon above it, built from theme tokens, stayed on the app's light palette. Dynamic
    /// resources resolve up the element tree, so merging the matching token dictionary into the owner's
    /// own resources re-themes every token-driven descendant without any of them knowing. Whether the
    /// pin is light or dark is read off its background rather than compared by reference, so a custom
    /// theme built from <see cref="SpreadsheetTheme.Dark"/> scopes the same way.
    /// </remarks>
    static ResourceDictionary InheritedValues(VisualElement owner, IEnumerable<string> keys)
    {
        var values = new ResourceDictionary();
        foreach (var key in keys)
        {
            if (TryFindAbove(owner, key, out var value))
                values[key] = value;
        }
        return values;
    }

    static bool TryFindAbove(Element owner, string key, out object value)
    {
        for (var element = owner.Parent; element is not null; element = element.Parent)
        {
            if (element is VisualElement { Resources: { } resources } && resources.TryGetValue(key, out value!))
                return true;
            if (element is Application app && app.Resources.TryGetValue(key, out value!))
                return true;
        }

        // A view not yet in a window has no Application ancestor; the app's resources are still what it
        // will inherit once attached.
        if (Application.Current is { } current && current.Resources.TryGetValue(key, out value!))
            return true;

        value = null!;
        return false;
    }


    public static void ScopeTokens(VisualElement owner, SpreadsheetTheme? pinned)
    {
        var palette = pinned is null || ShinyThemeManager.CurrentTheme is not { } current
            ? null
            : IsLightGround(pinned.Background) ? current.Light : current.Dark;

        ScopedPalettes.TryGetValue(owner, out var applied);
        if (ReferenceEquals(applied, palette))
            return;

        // The tokens are written into the owner's *own* dictionary, not merged into it. MAUI filters a
        // parent's resource change against a child only through Resources.Count and the dictionary's
        // own entries, and neither sees MergedDictionaries - so a merged scope let the app's next
        // light/dark flip straight through it, and a pinned dark ribbon went light around its dark tab.
        // Rebuilt and assigned in one go so the subtree re-resolves once rather than once per token.
        var next = new ResourceDictionary();
        foreach (var entry in owner.Resources)
        {
            if (applied is null || !applied.ContainsKey(entry.Key))
                next[entry.Key] = entry.Value;
        }
        foreach (var dictionary in owner.Resources.MergedDictionaries)
            next.MergedDictionaries.Add(dictionary);

        if (palette is not null)
        {
            foreach (var entry in palette)
                next[entry.Key] = entry.Value;
        }

        owner.Resources = next;

        if (palette is null)
            ScopedPalettes.Remove(owner);
        else
            ScopedPalettes.AddOrUpdate(owner, palette);

        // Assigning resources only announces the values the new dictionary holds. Unpinned, it holds
        // none of the scoped tokens, so every descendant kept the pinned palette's value: the ribbon
        // body stayed dark and the selected tab's ink went light-on-light. Pushing the values the owner
        // now inherits through an add-then-remove re-resolves them, and leaves the owner defining
        // nothing, so later app theme flips flow down from above again.
        if (palette is null && applied is not null && InheritedValues(owner, applied.Keys) is { Count: > 0 } inherited)
        {
            owner.Resources.MergedDictionaries.Add(inherited);
            owner.Resources.MergedDictionaries.Remove(inherited);
        }
    }

    static bool IsLightGround(ArgbColor background)
        => (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255d >= 0.5;

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, ResourceDictionary> ScopedPalettes = new();

    /// <summary>MAUI colours are floats in 0..1; the Office kernel stores bytes.</summary>
    public static Color ToMauiColor(ArgbColor value)
        => Color.FromRgba(value.R / 255f, value.G / 255f, value.B / 255f, value.A / 255f);
}

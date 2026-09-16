using System.Reflection;
using Shiny.Controls.Office.Skia;
using Shiny.Maui.Controls.Office;
using Shiny.Maui.Controls.Ribbons;
using Shiny.Maui.Controls.Themes;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The spreadsheet's formula bar and ribbon follow dark mode along with the grid.
/// </summary>
/// <remarks>
/// <para>
/// Two faults, both reported as "the grid goes dark, the chrome above it does not":
/// </para>
/// <list type="number">
/// <item>The Office views subscribe to <c>Application.RequestedThemeChanged</c>, which MAUI raises
/// through a <c>WeakEventManager</c>. The handler was a closure nothing else referenced, so the first
/// GC collected it and every later light/dark flip was silently missed.</item>
/// <item>A pinned <c>Theme</c> repainted only what is drawn from it. The formula bar's boxes kept the
/// app's ink on the theme's ground, and the ribbon - built from theme tokens - stayed on the app's
/// palette.</item>
/// </list>
/// </remarks>
[Collection(ApplicationResourcesCollection.Name)]
public class SpreadsheetChromeThemeTests
{
    public SpreadsheetChromeThemeTests()
    {
        TestDispatcherProvider.Install();
    }

    static Entry[] Boxes(FormulaBar bar)
        => ((Grid)((Border)bar.Content).Content).Children.OfType<Entry>().ToArray();

    static Color Maui(Shiny.Controls.Office.Spreadsheet.ArgbColor c)
        => Color.FromRgba(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

    static void Collect()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    [Fact]
    public void FormulaBarStillFollowsTheAppAfterAGarbageCollection()
    {
        var app = new Application { UserAppTheme = AppTheme.Light };
        var bar = new FormulaBar();
        bar.Refresh();

        Boxes(bar)[0].TextColor.ShouldBe(Maui(SpreadsheetTheme.Light.CellText));

        // Unfixed, the subscription's closure was only weakly held and this collected it.
        Collect();

        app.UserAppTheme = AppTheme.Dark;

        foreach (var box in Boxes(bar))
        {
            box.TextColor.ShouldBe(Maui(SpreadsheetTheme.Dark.CellText));
            box.BackgroundColor.ShouldBe(Maui(SpreadsheetTheme.Dark.Background));
        }

        GC.KeepAlive(bar);
    }

    [Fact]
    public void APinnedDarkThemeInksTheFormulaBarForItsOwnGround()
    {
        _ = new Application { UserAppTheme = AppTheme.Light };
        var bar = new FormulaBar { Theme = SpreadsheetTheme.Dark };

        foreach (var box in Boxes(bar))
        {
            box.BackgroundColor.ShouldBe(Maui(SpreadsheetTheme.Dark.Background));
            box.TextColor.ShouldBe(Maui(SpreadsheetTheme.Dark.CellText), "the app is light, but the bar's ground is the pinned dark theme's");
        }
    }

    [Fact]
    public void APinnedDarkThemeScopesTheDarkPaletteOverTheRibbon()
    {
        var app = new Application { UserAppTheme = AppTheme.Light };
        var theme = new BasicTheme();
        app.Resources.MergedDictionaries.Add(theme.Light);

        var current = typeof(ShinyThemeManager).GetProperty(nameof(ShinyThemeManager.CurrentTheme))!;
        var previous = current.GetValue(null);
        current.SetValue(null, theme);

        try
        {
            var toolbar = new SpreadsheetToolbar { Theme = SpreadsheetTheme.Dark };
            toolbar.Resources.ContainsKey(ShinyThemeKeys.Color.SurfaceContainerLow).ShouldBeTrue();

            var ribbon = (Ribbon)toolbar.Content;
            var body = (Border)typeof(Ribbon).GetField("bodyFrame", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(ribbon)!;
            body.BackgroundColor.ShouldBe((Color)theme.Dark[ShinyThemeKeys.Color.SurfaceContainerLow]);

            // The selected tab's label carries a resolved colour rather than a token, so it has to be
            // re-inked when the palette under it changes - otherwise it keeps the light palette's dark
            // ink on the now-dark body.
            var home = ((IVisualTreeElement)ribbon).GetVisualTreeDescendants().OfType<Label>().First(x => x.Text == "Home");
            home.TextColor.ShouldBe((Color)theme.Dark[ShinyThemeKeys.Color.OnSurface]);

            // Unpinned, the scope comes off and the bar is back on whatever the app is using.
            toolbar.Theme = null;
            toolbar.Resources.ContainsKey(ShinyThemeKeys.Color.SurfaceContainerLow).ShouldBeFalse();

            // Removing a merged dictionary raises nothing in MAUI, so without a re-publish the body kept
            // the dark ground and the tab kept light ink on the light chip (seen on iOS).
            body.BackgroundColor.ShouldBe((Color)theme.Light[ShinyThemeKeys.Color.SurfaceContainerLow]);
            home.TextColor.ShouldBe((Color)theme.Light[ShinyThemeKeys.Color.OnSurface]);

            // And back again: a second pin must re-ink everything, not only what the first one touched.
            toolbar.Theme = SpreadsheetTheme.Dark;
            body.BackgroundColor.ShouldBe((Color)theme.Dark[ShinyThemeKeys.Color.SurfaceContainerLow]);
            var cut = ((IVisualTreeElement)toolbar).GetVisualTreeDescendants().First(x => x.GetType().Name == "OfficeToolbarButton");
            ((Color)cut.GetType().GetProperty("IconColor")!.GetValue(cut)!).ShouldBe((Color)theme.Dark[ShinyThemeKeys.Color.OnSurface]);

            // The ribbon's own icons (cut, copy, AutoSum...) are template-built GraphicsViews. They copied
            // the app's ink once at inflation, so a pinned dark ribbon drew them dark on dark on iOS.
            var ribbonIcons = ((IVisualTreeElement)ribbon).GetVisualTreeDescendants().Where(x => x.GetType().Name == "OfficeRibbonIconView").ToList();
            ribbonIcons.ShouldNotBeEmpty();
            foreach (var icon in ribbonIcons)
                ((Color)icon.GetType().GetProperty("IconColor")!.GetValue(icon)!).ShouldBe((Color)theme.Dark[ShinyThemeKeys.Color.OnSurfaceVariant]);

            toolbar.Theme = null;
            foreach (var icon in ribbonIcons)
                ((Color)icon.GetType().GetProperty("IconColor")!.GetValue(icon)!).ShouldBe((Color)theme.Light[ShinyThemeKeys.Color.OnSurfaceVariant]);
        }
        finally
        {
            current.SetValue(null, previous);
        }
    }

    [Fact]
    public void APinnedDarkRibbonStaysDarkWhenTheAppFlipsToLight()
    {
        var app = new Application { UserAppTheme = AppTheme.Dark };
        var theme = new BasicTheme();
        app.Resources.MergedDictionaries.Add(theme.Dark);

        var current = typeof(ShinyThemeManager).GetProperty(nameof(ShinyThemeManager.CurrentTheme))!;
        var previous = current.GetValue(null);
        current.SetValue(null, theme);

        try
        {
            var toolbar = new SpreadsheetToolbar { Theme = SpreadsheetTheme.Dark };
            var page = new ContentPage { Content = toolbar };
            page.Parent = app;

            var ribbon = (Ribbon)toolbar.Content;
            var body = (Border)typeof(Ribbon).GetField("bodyFrame", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(ribbon)!;
            body.BackgroundColor.ShouldBe((Color)theme.Dark[ShinyThemeKeys.Color.SurfaceContainerLow]);

            // The app swaps its palette the way ShinyThemeManager does. MAUI filters that change against
            // the toolbar only through its own entries - a merged scope was invisible to the filter, so
            // the light values went straight through and the pinned ribbon turned light (seen on iOS).
            app.Resources.MergedDictionaries.Remove(theme.Dark);
            app.Resources.MergedDictionaries.Add(theme.Light);

            body.BackgroundColor.ShouldBe((Color)theme.Dark[ShinyThemeKeys.Color.SurfaceContainerLow]);
        }
        finally
        {
            current.SetValue(null, previous);
        }
    }

    [Fact]
    public void RibbonTabInkFollowsAPaletteSwapWithoutARebuild()
    {
        var app = new Application { UserAppTheme = AppTheme.Light };
        var theme = new BasicTheme();
        app.Resources.MergedDictionaries.Add(theme.Light);

        var ribbon = new Ribbon();
        var tab = new RibbonTab { Title = "Home" };
        tab.Groups.Add(new RibbonGroup { Title = "Clipboard", Items = { new RibbonButton { Text = "Paste" } } });
        ribbon.Tabs.Add(tab);

        var home = ((IVisualTreeElement)ribbon).GetVisualTreeDescendants().OfType<Label>().First(x => x.Text == "Home");
        home.TextColor.ShouldBe((Color)theme.Light[ShinyThemeKeys.Color.OnSurface]);

        // What the app flipping to dark, or a host scoping a pinned palette, looks like to the ribbon:
        // the tokens change underneath it and nothing asks it to rebuild.
        ribbon.Resources.MergedDictionaries.Add(theme.Dark);

        home.TextColor.ShouldBe((Color)theme.Dark[ShinyThemeKeys.Color.OnSurface]);
    }
}

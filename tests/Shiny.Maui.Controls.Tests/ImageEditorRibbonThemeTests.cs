using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Ribbons;
using Shiny.Maui.Controls.Themes;
using Shouldly;
using Xunit;
using Editor = Shiny.Maui.Controls.ImageEditor.ImageEditor;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The image editor's ribbon icons follow the theme after they are built.
/// </summary>
/// <remarks>
/// The icons copied the on-surface-variant ink out of <c>Application.Current.Resources</c> once, when
/// the ribbon template was inflated, so a light/dark flip left them in the old ink - dark icons on the
/// now-dark ribbon - and a palette scoped over the editor was never seen at all.
/// </remarks>
[Collection(ApplicationResourcesCollection.Name)]
public class ImageEditorRibbonThemeTests
{
    public ImageEditorRibbonThemeTests()
    {
        TestDispatcherProvider.Install();
    }

    static List<ThemedIconView> RibbonIcons(Editor editor)
    {
        var ribbon = ((IVisualTreeElement)editor).GetVisualTreeDescendants().OfType<Ribbon>().Single();
        return ((IVisualTreeElement)ribbon).GetVisualTreeDescendants().OfType<ThemedIconView>().ToList();
    }

    [Fact]
    public void RibbonIconsFollowAnAppPaletteSwap()
    {
        var app = new Application { UserAppTheme = AppTheme.Light };
        var theme = new BasicTheme();
        app.Resources.MergedDictionaries.Add(theme.Light);

        var editor = new Editor();
        var page = new ContentPage { Content = editor };
        page.Parent = app;

        var icons = RibbonIcons(editor);
        icons.ShouldNotBeEmpty();
        foreach (var icon in icons)
            icon.IconColor.ShouldBe((Color)theme.Light[ShinyThemeKeys.Color.OnSurfaceVariant]);

        // The swap ShinyThemeManager makes on a light/dark flip. Nothing rebuilds the ribbon.
        app.Resources.MergedDictionaries.Remove(theme.Light);
        app.Resources.MergedDictionaries.Add(theme.Dark);

        foreach (var icon in icons)
            icon.IconColor.ShouldBe((Color)theme.Dark[ShinyThemeKeys.Color.OnSurfaceVariant], "unfixed, the icons kept the light palette's dark ink");
    }

    [Fact]
    public void RibbonIconsHonourAPaletteScopedOverTheEditor()
    {
        var app = new Application { UserAppTheme = AppTheme.Light };
        var theme = new BasicTheme();
        app.Resources.MergedDictionaries.Add(theme.Light);

        var editor = new Editor();
        editor.Resources.MergedDictionaries.Add(theme.Dark);

        var icons = RibbonIcons(editor);
        icons.ShouldNotBeEmpty();
        foreach (var icon in icons)
        {
            icon.IconColor.ShouldBe((Color)theme.Dark[ShinyThemeKeys.Color.OnSurfaceVariant]);
            var drawable = (Shiny.Maui.Controls.ImageEditor.ImageEditorIconDrawable)icon.Drawable;
            drawable.Color.ShouldBe(icon.IconColor, "the drawable is what actually paints");
        }
    }
}

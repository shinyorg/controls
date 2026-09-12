using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The two ways a consumer's XAML could be ignored by a pill, both of which looked like the style had
/// simply not been written.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class PillViewStyleTests
{
    public PillViewStyleTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
        Application.Current!.Resources.MergedDictionaries.Add(new Themes.BasicLightTheme());
    }


    static Style PillStyle(double fontSize) => new(typeof(PillView))
    {
        Setters = { new Setter { Property = PillView.FontSizeProperty, Value = fontSize } }
    };


    /// <summary>
    /// A pill applies its type at construction, and that used to write <c>Style = null</c> — so an
    /// explicit <c>Style</c> was wiped before the control had even been measured.
    /// </summary>
    [Fact]
    public void AnExplicitStyleSurvivesTheTypeBeingApplied()
    {
        var pill = new PillView { Text = "Live", Style = PillStyle(19) };

        pill.Type = PillType.Success;
        pill.Type = PillType.None;

        pill.Style.ShouldNotBeNull();
        pill.FontSize.ShouldBe(19d);
    }


    /// <summary>
    /// The documented per-type override, scoped to a page's resources rather than App.xaml — which is
    /// the ordinary place to put one, and which the old application-only lookup could not see.
    /// </summary>
    [Fact]
    public void APageScopedTypeStyleIsFound()
    {
        var pill = new PillView { Text = "Live" };
        var page = new ContentPage { Content = pill };
        page.Resources.Add(PillView.SuccessStyleKey, PillStyle(23));
        page.Parent = Application.Current;

        pill.Type = PillType.Success;

        pill.FontSize.ShouldBe(23d);
    }


    [Fact]
    public void AnApplicationScopedTypeStyleIsStillFound()
    {
        Application.Current!.Resources.Add(PillView.CriticalStyleKey, PillStyle(27));

        var pill = new PillView { Text = "Live" };
        var page = new ContentPage { Content = pill };
        page.Parent = Application.Current;

        pill.Type = PillType.Critical;

        pill.FontSize.ShouldBe(27d);
    }
}

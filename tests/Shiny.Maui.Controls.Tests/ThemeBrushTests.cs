using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Shiny.Maui.Controls.Themes;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// The outline used to be the hardest colour in the library to get onto the screen, and this is the
/// mechanism that replaced three workarounds for it.
/// </summary>
/// <remarks>
/// <para>
/// <c>Border.Stroke</c> is <c>Brush</c>-typed and the theme's colour tokens are <c>Color</c>s. Handing
/// one to the other compiles — <c>Brush</c> converts from <c>Color</c> implicitly — and then silently
/// resolves to nothing, leaving the stroke null. The library carried three separate versions of a
/// workaround for that, and a hidden zero-sized <c>BoxView</c> inside every outlined control whose only
/// job was to resolve the resource on its behalf.
/// </para>
/// <para>
/// The theme dictionaries now ship a <c>Brush</c> twin of every colour, so the property gets a token of
/// its own type. These tests are the proof, because the failure mode is invisible: nothing throws, and
/// a control with the right stroke thickness simply has no border.
/// </para>
/// </remarks>
[Collection(ApplicationResourcesCollection.Name)]
public class ThemeBrushTests
{
    public ThemeBrushTests()
    {
        TestDispatcherProvider.Install();
        TestDispatcherProvider.Instance.Timers.Clear();
        _ = new Application();
        Application.Current!.Resources.MergedDictionaries.Add(new BasicLightTheme());
    }


    /// <summary>
    /// Puts a control where a dynamic resource has a chain to resolve through. Parenting the page to
    /// the application is the part that matters: dynamic resources only re-resolve for elements in the
    /// application's element tree, so a control held on its own resolves once and never hears about a
    /// theme swap — the same note <c>ThemeGeometryCoverageTests</c> carries.
    /// </summary>
    static T OnPage<T>(T view) where T : View
    {
        var page = new ContentPage { Content = view };
        page.Parent = Application.Current;
        return view;
    }


    [Fact]
    public void EveryColourHasABrushTwin()
    {
        var theme = new BasicLightTheme();

        foreach (var key in theme.Keys.Where(k => k.StartsWith("Shiny.Color.", StringComparison.Ordinal)))
        {
            var brushKey = key.Replace(".Color.", ".Brush.");

            theme.ContainsKey(brushKey).ShouldBeTrue($"{key} has no Brush twin");
            ((SolidColorBrush)theme[brushKey]).Color.ShouldBe((Color)theme[key], $"{brushKey} does not match {key}");
        }
    }


    [Fact]
    public void AnOutlinedButtonActuallyGetsAStroke()
    {
        var button = OnPage(new ShinyButton { Text = "Outlined", Appearance = ButtonAppearance.Outlined });
        var border = (Border)button.Content!;

        border.StrokeThickness.ShouldBe(1d);
        border.Stroke.ShouldBeOfType<SolidColorBrush>()
            .Color.ShouldBe((Color)Application.Current!.Resources["Shiny.Color.Outline"]);
    }


    [Fact]
    public void AnExplicitBorderColourWins_AndClearingItGoesBackToTheTheme()
    {
        var button = OnPage(new ShinyButton { Text = "Outlined", Appearance = ButtonAppearance.Outlined });
        var border = (Border)button.Content!;

        button.BorderColor = Colors.Magenta;
        border.Stroke.ShouldBeOfType<SolidColorBrush>().Color.ShouldBe(Colors.Magenta);

        // The round trip is the part that used to break: a local value outranks a dynamic resource, so
        // going back to the theme has to clear the slot first or the control keeps the explicit colour.
        button.ClearValue(ShinyButton.BorderColorProperty);
        border.Stroke.ShouldBeOfType<SolidColorBrush>()
            .Color.ShouldBe((Color)Application.Current!.Resources["Shiny.Color.Outline"]);
    }


    [Fact]
    public void AThemeSwapRepaintsTheStroke()
    {
        var button = OnPage(new ShinyButton { Text = "Outlined", Appearance = ButtonAppearance.Outlined });
        var border = (Border)button.Content!;

        var light = ((SolidColorBrush)border.Stroke).Color;

        // Exactly what ShinyThemeManager does - remove the applied dictionary, add the next one.
        // Clear() plus Add() is NOT the same thing: the clear does not raise the resource-changed
        // notification, so nothing re-resolves and the swap looks like it did nothing.
        var app = Application.Current!;
        var current = app.Resources.MergedDictionaries.First();
        app.Resources.MergedDictionaries.Remove(current);
        app.Resources.MergedDictionaries.Add(new BasicDarkTheme());

        ((SolidColorBrush)border.Stroke).Color.ShouldNotBe(light);
        ((SolidColorBrush)border.Stroke).Color.ShouldBe((Color)app.Resources["Shiny.Color.Outline"]);
    }
}

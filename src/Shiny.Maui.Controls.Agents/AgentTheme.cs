using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Agents;

/// <summary>
/// Theme plumbing for the agent views. Everything is painted from the Shiny theme tokens with
/// <c>SetDynamicResource</c>, so the theme packs (Aurora, Material, Ocean, Terminal) restyle the agent
/// UI and an A2UI surface live, the same way they restyle the rest of the suite.
/// </summary>
static class AgentTheme
{
    public static T Themed<T>(this T element, BindableProperty property, string key) where T : Element
    {
        element.SetDynamicResource(property, key);
        return element;
    }

    public static T FontToken<T>(this T element, string sizeKey) where T : Element
    {
        element.SetDynamicResource(Label.FontSizeProperty, sizeKey);
        return element;
    }

    public static Label Text(string? text, string sizeKey, string colorKey = ShinyThemeKeys.Color.OnSurface) => new Label
    {
        Text = text,
        LineBreakMode = LineBreakMode.WordWrap
    }.FontToken(sizeKey).Themed(Label.TextColorProperty, colorKey);

    public static RoundRectangle Corner(string radiusKey)
    {
        var shape = new RoundRectangle();
        shape.SetDynamicResource(RoundRectangle.CornerRadiusProperty, radiusKey);
        return shape;
    }

    /// <summary>A theme colour read once (for values that aren't bindable, like a gauge band).</summary>
    public static Color Resolve(string key, Color fallback)
        => Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color ? color : fallback;

    /// <summary>A small pill-shaped button in the theme's tonal style.</summary>
    public static Button TonalButton(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            Padding = new Thickness(14, 6),
            MinimumHeightRequest = 34,
            CornerRadius = 17
        }.FontToken(ShinyThemeKeys.Type.LabelLargeSize);

        if (primary)
        {
            button.Themed(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Primary)
                .Themed(Button.TextColorProperty, ShinyThemeKeys.Color.OnPrimary);
        }
        else
        {
            button.Themed(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.SecondaryContainer)
                .Themed(Button.TextColorProperty, ShinyThemeKeys.Color.OnSecondaryContainer);
        }
        return button;
    }

    /// <summary>A text-only button.</summary>
    public static Button LinkButton(string text)
        => new Button
        {
            Text = text,
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(8, 4),
            MinimumHeightRequest = 30,
            BorderWidth = 0
        }.FontToken(ShinyThemeKeys.Type.LabelLargeSize).Themed(Button.TextColorProperty, ShinyThemeKeys.Color.Primary);
}

/// <summary>What a surface draws for a component it can't draw.</summary>
static class A2UIPlaceholders
{
    /// <summary>A referenced child that hasn't arrived yet (A2UI renders progressively).</summary>
    public static View Missing() => new BoxView { HeightRequest = 0, WidthRequest = 0, Color = Colors.Transparent };

    /// <summary>A component type with no renderer or no catalog definition. The engine already reported it to the agent.</summary>
    public static View Unsupported(string type)
    {
        var border = new Border
        {
            Padding = new Thickness(8, 4),
            StrokeThickness = 1,
            StrokeDashArray = [4, 3],
            StrokeShape = AgentTheme.Corner(ShinyThemeKeys.Shape.CornerSmallRadius),
            HorizontalOptions = LayoutOptions.Start,
            Content = AgentTheme.Text($"Unsupported component: {type}", ShinyThemeKeys.Type.BodySmallSize, ShinyThemeKeys.Color.OnSurfaceVariant)
        }.Themed(Border.StrokeProperty, ShinyThemeKeys.Brush.OutlineVariant);
        SemanticProperties.SetDescription(border, $"Unsupported component {type}");
        return border;
    }
}

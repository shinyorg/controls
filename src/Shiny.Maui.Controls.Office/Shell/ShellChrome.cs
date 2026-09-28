using Microsoft.Maui.Controls.Shapes;
using Shiny.Controls.Office.Icons;
using Shiny.Controls.Office.Shell;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Office;

/// <summary>
/// The small builders every shell component shares, so an icon button in the title bar and one in the
/// status bar cannot come out as two different things.
/// </summary>
internal static class ShellChrome
{
    /// <summary>A host-agnostic colour as a MAUI one. Float channels, deliberately: the int/float
    /// <c>FromRgba</c> overloads resolve differently for bytes and one of them reads 0-255 as 0-1.</summary>
    public static Color ToColor(this ArgbColor c) => Color.FromRgba(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);


    /// <summary>
    /// An entry with no platform chrome and no platform padding, for a box that draws its own frame at a
    /// fixed height (the title bar's search). Android's EditText keeps the padding of the underline
    /// drawable after the background is cleared, which puts ~26dp of padding in a 30dp box and crops the
    /// text to its top half.
    /// </summary>
    public static Entry FlatEntry()
    {
        var entry = new BorderlessEntry();
#if ANDROID
        entry.HandlerChanged += (_, _) =>
        {
            if (entry.Handler?.PlatformView is Android.Widget.EditText native)
                native.SetPadding(0, 0, 0, 0);
        };
#endif
        return entry;
    }


    /// <summary>
    /// An icon-only button: a <see cref="Border"/> with a tap on its <c>Command</c> (a MAUI Button ignores
    /// gesture recognizers, and a Command is the seam a test can press).
    /// </summary>
    public static Border IconButton(OfficeShellIcon icon, string hint, Action action, Color? ink = null, double size = 16)
    {
        var view = new OfficeShellIconView { Icon = icon, WidthRequest = size, HeightRequest = size };
        if (ink is not null)
            view.Color = ink;

        var border = new Border
        {
            Content = view,
            Padding = new Thickness(6),
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 4 }
        };

        Hint(border, hint);
        border.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(action) });
        return border;
    }


    /// <summary>A tappable text-and-icon pill.</summary>
    public static Border TextButton(string text, Action action, OfficeShellIcon? icon, Color? ink, out Label label)
    {
        var row = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
        if (icon is { } i)
        {
            var view = new OfficeShellIconView { Icon = i, WidthRequest = 16, HeightRequest = 16 };
            if (ink is not null)
                view.Color = ink;
            row.Children.Add(view);
        }

        label = new Label { Text = text, FontSize = 13, VerticalTextAlignment = TextAlignment.Center };
        if (ink is not null)
            label.TextColor = ink;
        else
            label.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurface);
        row.Children.Add(label);

        var border = new Border
        {
            Content = row,
            Padding = new Thickness(10, 5),
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 4 }
        };

        Hint(border, text);
        border.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(action) });
        return border;
    }


    public static void Hint(View view, string? hint)
    {
        SemanticProperties.SetDescription(view, hint);
        ToolTipProperties.SetText(view, hint);
    }


    /// <summary>Dims and deadens a hand-built button without touching a bound IsEnabled on anything else.</summary>
    public static void SetEnabled(View view, bool enabled)
    {
        view.IsEnabled = enabled;
        view.Opacity = enabled ? 1 : 0.4;
    }


    public static Label Text(string? text, double size = 13, string colorKey = ShinyThemeKeys.Color.OnSurface, FontAttributes attributes = FontAttributes.None)
    {
        var label = new Label { Text = text, FontSize = size, FontAttributes = attributes, VerticalTextAlignment = TextAlignment.Center };
        label.SetDynamicResource(Label.TextColorProperty, colorKey);
        return label;
    }


    public static BoxView Rule(bool vertical = false)
    {
        var rule = vertical ? new BoxView { WidthRequest = 1 } : new BoxView { HeightRequest = 1 };
        rule.SetDynamicResource(BoxView.ColorProperty, ShinyThemeKeys.Color.OutlineVariant);
        return rule;
    }


    /// <summary>A card: surface ground, outline-variant stroke, small corners.</summary>
    public static Border Card(View content, double padding = 12)
    {
        var card = new Border
        {
            Content = content,
            Padding = padding,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 }
        };
        card.SetDynamicResource(Border.StrokeProperty, ShinyThemeKeys.Brush.OutlineVariant);
        card.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Surface);
        return card;
    }


    public static void OnTap(View view, Action action)
        => view.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(action) });


    /// <summary>The nearest ancestor of a type, walking <c>Parent</c>.</summary>
    public static T? Ancestor<T>(Element? element) where T : Element
    {
        var node = element?.Parent;
        while (node is not null)
        {
            if (node is T found)
                return found;

            node = node.Parent;
        }

        return null;
    }


    /// <summary>X of <paramref name="view"/> relative to <paramref name="root"/>, summing the parent chain.</summary>
    public static Point OffsetWithin(VisualElement view, Element root)
    {
        double x = 0, y = 0;
        Element? node = view;
        while (node is VisualElement v && !ReferenceEquals(node, root))
        {
            x += v.X + v.TranslationX;
            y += v.Y + v.TranslationY;
            node = node.Parent;
        }

        return new Point(x, y);
    }
}

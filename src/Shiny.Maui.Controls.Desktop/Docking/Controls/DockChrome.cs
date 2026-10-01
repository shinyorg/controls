using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Keys = Shiny.Maui.Controls.Themes.ShinyThemeKeys;

namespace Shiny.Maui.Controls.Desktop.Docking;

/// <summary>
/// Theme-following chrome for the dock host: icons, small square buttons and the docking guides.
/// </summary>
/// <remarks>
/// <para>
/// Every colour is a dynamic resource on a theme key, never a literal, so the dock follows
/// <c>ShinyThemeManager.SetTheme</c> and dark mode. Brush-typed properties (<c>Border.Stroke</c>,
/// <c>Shape.Stroke</c>) take the <c>Shiny.Brush.*</c> twin of a colour — a <c>Shiny.Color.*</c> key on
/// a Brush property is silently dropped.
/// </para>
/// <para>
/// Icons are drawn from <see cref="Line"/> / <see cref="Polyline"/> with explicit coordinates rather
/// than text glyphs (which render as a speck at chrome sizes and differ per platform) or SVG path
/// strings (which MAUI's parser can silently truncate).
/// </para>
/// </remarks>
static class DockChrome
{
    public const double ButtonSize = 22;
    public const double GuideSize = 34;
    public const double GuideGap = 4;
    public const double CompassPad = 7;
    public const double CompassSize = GuideSize * 3 + GuideGap * 2 + CompassPad * 2;

    public static T Tint<T>(this T element, BindableProperty property, string key) where T : Element
    {
        element.SetDynamicResource(property, key);
        return element;
    }

    /// <summary>The <c>Shiny.Color.*</c> key behind a <c>Shiny.Brush.*</c> key.</summary>
    public static string ColorKeyOf(string brushKey) => brushKey.Replace("Shiny.Brush.", "Shiny.Color.");

    // ------------------------------------------------------------------ icons
    static Grid Icon(string brushKey, params Shape[] shapes)
    {
        var grid = new Grid
        {
            WidthRequest = 16,
            HeightRequest = 16,
            InputTransparent = true,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        foreach (var shape in shapes)
        {
            shape.StrokeThickness = 1.5;
            shape.StrokeLineCap = PenLineCap.Round;
            shape.StrokeLineJoin = PenLineJoin.Round;
            shape.Tint(Shape.StrokeProperty, brushKey);
            grid.Add(shape);
        }
        return grid;
    }

    /// <summary>Re-points every stroke of an icon built here at another brush key.</summary>
    public static void Recolor(View icon, string brushKey)
    {
        if (icon is not Microsoft.Maui.Controls.Layout layout) return;
        foreach (var shape in layout.Children.OfType<Shape>())
            shape.Tint(Shape.StrokeProperty, brushKey);
    }

    static Line L(double x1, double y1, double x2, double y2) => new() { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };

    static Polyline P(params double[] xy)
    {
        var points = new PointCollection();
        for (var i = 0; i + 1 < xy.Length; i += 2)
            points.Add(new Point(xy[i], xy[i + 1]));
        return new Polyline { Points = points };
    }

    public static View CloseIcon(string brushKey)
        => Icon(brushKey, L(4.5, 4.5, 11.5, 11.5), L(11.5, 4.5, 4.5, 11.5));

    public static View ChevronIcon(DockArea direction, string brushKey) => direction switch
    {
        DockArea.Left => Icon(brushKey, P(10, 4, 6, 8, 10, 12)),
        DockArea.Right => Icon(brushKey, P(6, 4, 10, 8, 6, 12)),
        DockArea.Top => Icon(brushKey, P(4, 10, 8, 6, 12, 10)),
        _ => Icon(brushKey, P(4, 6, 8, 10, 12, 6))
    };

    /// <summary>Return a floating window to the layout: a window with an arrow dropping into it.</summary>
    public static View DockBackIcon(string brushKey) => Icon(brushKey,
        P(2.5, 2.5, 13.5, 2.5, 13.5, 13.5, 2.5, 13.5, 2.5, 2.5),
        L(2.5, 6, 13.5, 6),
        L(8, 8.2, 8, 11.5),
        P(6.4, 9.9, 8, 11.5, 9.6, 9.9));

    /// <summary>Tear the active panel off into a floating window.</summary>
    public static View FloatIcon(string brushKey) => Icon(brushKey,
        P(2.5, 5.5, 10.5, 5.5, 10.5, 13.5, 2.5, 13.5, 2.5, 5.5),
        P(7, 2.5, 13.5, 2.5, 13.5, 9));

    // ------------------------------------------------------------------ buttons
    public static DockChromeButton ChromeButton(Func<string, View> iconFactory, string iconBrushKey, string name, Action onTap, bool danger = false)
        => new(iconFactory, iconBrushKey, name, onTap, danger);

    // ------------------------------------------------------------------ guides
    /// <summary>
    /// The highlighted part of the little window a guide draws, in a 22px icon box whose frame runs
    /// 3..19 with a title line at y=6. Edge guides draw a thin rail; compass guides draw a half.
    /// </summary>
    static Rect FillFor(DockZone zone, bool edge) => (zone, edge) switch
    {
        (DockZone.Left, true) => new Rect(3, 6, 4, 13),
        (DockZone.Right, true) => new Rect(15, 6, 4, 13),
        (DockZone.Top, true) => new Rect(3, 6, 16, 3),
        (DockZone.Bottom, true) => new Rect(3, 16, 16, 3),
        (DockZone.Left, _) => new Rect(3, 6, 8, 13),
        (DockZone.Right, _) => new Rect(11, 6, 8, 13),
        (DockZone.Top, _) => new Rect(3, 6, 16, 6.5),
        (DockZone.Bottom, _) => new Rect(3, 12.5, 16, 6.5),
        _ => new Rect(3, 6, 16, 13)
    };

    public static DockGuide Guide(DockZone zone, bool edge) => new(zone, FillFor(zone, edge));
}

/// <summary>One docking guide button: a small framed window with the target region highlighted.</summary>
sealed class DockGuide : Grid
{
    readonly Border frame;
    readonly Border window;
    readonly BoxView titleLine;
    readonly BoxView fill;
    bool hot;

    public DockZone Zone { get; }

    public DockGuide(DockZone zone, Rect fillRect)
    {
        Zone = zone;
        InputTransparent = true;
        WidthRequest = DockChrome.GuideSize;
        HeightRequest = DockChrome.GuideSize;

        frame = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 7 },
            Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.22f, Radius = 8, Offset = new Point(0, 2) }
        };

        var icon = new Grid { WidthRequest = 22, HeightRequest = 22, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        window = new Border
        {
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 1.5 },
            Background = Brush.Transparent,
            Margin = new Thickness(3),
        };
        titleLine = new BoxView { HeightRequest = 1.5, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(3, 5.5, 3, 0) };
        fill = new BoxView
        {
            Opacity = 0.55,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            WidthRequest = fillRect.Width,
            HeightRequest = fillRect.Height,
            Margin = new Thickness(fillRect.X, fillRect.Y, 0, 0)
        };
        icon.Add(fill);
        icon.Add(window);
        icon.Add(titleLine);

        Add(frame);
        Add(icon);
        Apply();
    }

    public bool IsHot
    {
        get => hot;
        set
        {
            if (hot == value) return;
            hot = value;
            Apply();
        }
    }

    void Apply()
    {
        frame.Tint(BackgroundColorProperty, hot ? Keys.Color.Primary : Keys.Color.Surface);
        frame.Tint(Border.StrokeProperty, hot ? Keys.Brush.Primary : Keys.Brush.OutlineVariant);
        var ink = hot ? Keys.Color.OnPrimary : Keys.Color.Primary;
        window.Tint(Border.StrokeProperty, hot ? Keys.Brush.OnPrimary : Keys.Brush.Primary);
        titleLine.Tint(BoxView.ColorProperty, ink);
        fill.Tint(BoxView.ColorProperty, ink);
        Scale = hot ? 1.08 : 1;
    }
}

/// <summary>
/// A 22px square chrome button. Not a <see cref="Button"/>: a Button would swallow the hover
/// recognizer, and its own padding and minimum sizes differ on every head.
/// </summary>
sealed class DockChromeButton : Grid
{
    readonly View icon;
    readonly BoxView hover;
    readonly bool danger;
    string iconBrushKey;
    bool over;

    public DockChromeButton(Func<string, View> iconFactory, string iconBrushKey, string name, Action onTap, bool danger)
    {
        this.iconBrushKey = iconBrushKey;
        this.danger = danger;
        icon = iconFactory(iconBrushKey);
        icon.Opacity = 0.75;
        hover = new BoxView { CornerRadius = 4, Opacity = 0, InputTransparent = true };
        WidthRequest = DockChrome.ButtonSize;
        HeightRequest = DockChrome.ButtonSize;
        VerticalOptions = LayoutOptions.Center;
        Add(hover);
        Add(icon);
        ApplyHoverColor();
        SemanticProperties.SetDescription(this, name);
        ToolTipProperties.SetText(this, name);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onTap();
        GestureRecognizers.Add(tap);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => { over = true; ApplyState(); };
        pointer.PointerExited += (_, _) => { over = false; ApplyState(); };
        GestureRecognizers.Add(pointer);
    }

    /// <summary>Re-keys the glyph colour, e.g. when the title bar it sits on turns accent.</summary>
    public string IconBrushKey
    {
        get => iconBrushKey;
        set
        {
            iconBrushKey = value;
            ApplyHoverColor();
            ApplyState();
        }
    }

    void ApplyHoverColor()
        => hover.Tint(BoxView.ColorProperty, danger ? Keys.Color.Error : DockChrome.ColorKeyOf(iconBrushKey));

    void ApplyState()
    {
        hover.Opacity = over ? (danger ? 1 : 0.14) : 0;
        icon.Opacity = over ? 1 : 0.75;
        DockChrome.Recolor(icon, over && danger ? Keys.Brush.OnError : iconBrushKey);
    }
}

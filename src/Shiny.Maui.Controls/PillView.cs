using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

public class PillView : ContentView
{
    public const string NoneStyleKey = "ShinyPillNoneStyle";
    public const string SuccessStyleKey = "ShinyPillSuccessStyle";
    public const string InfoStyleKey = "ShinyPillInfoStyle";
    public const string WarningStyleKey = "ShinyPillWarningStyle";
    public const string CautionStyleKey = "ShinyPillCautionStyle";
    public const string CriticalStyleKey = "ShinyPillCriticalStyle";

    readonly Border border;
    readonly Label label;

    bool isUpdatingFromType;

    public PillView()
    {
        label = new Label
        {
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap
        }.WithFontSize(ShinyThemeKeys.Type.BodySmallSize);

        border = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
().WithCornerRadius(ShinyThemeKeys.Shape.CornerMediumRadius),
            Padding = new Thickness(12, 4),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = label
        }.WithStrokeThickness(ShinyThemeKeys.Border.Thin);

        Content = border;

        // Apply default (None) styling
        ApplyPillType(PillType.None);

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(PillView));
    }

    static readonly Dictionary<PillType, string> StyleKeys = new()
    {
        [PillType.None] = NoneStyleKey,
        [PillType.Success] = SuccessStyleKey,
        [PillType.Info] = InfoStyleKey,
        [PillType.Warning] = WarningStyleKey,
        [PillType.Caution] = CautionStyleKey,
        [PillType.Critical] = CriticalStyleKey,
    };

    // Theme token keys per pill type: (container background, on-container text, role border).
    static readonly Dictionary<PillType, (string Bg, string Text, string Border)> TypeTokens = new()
    {
        [PillType.None] = (ShinyThemeKeys.Color.SurfaceContainerHigh, ShinyThemeKeys.Color.OnSurfaceVariant, ShinyThemeKeys.Color.OutlineVariant),
        [PillType.Success] = (ShinyThemeKeys.Color.SuccessContainer, ShinyThemeKeys.Color.OnSuccessContainer, ShinyThemeKeys.Color.Success),
        [PillType.Info] = (ShinyThemeKeys.Color.InfoContainer, ShinyThemeKeys.Color.OnInfoContainer, ShinyThemeKeys.Color.Info),
        [PillType.Warning] = (ShinyThemeKeys.Color.WarningContainer, ShinyThemeKeys.Color.OnWarningContainer, ShinyThemeKeys.Color.Warning),
        [PillType.Caution] = (ShinyThemeKeys.Color.CautionContainer, ShinyThemeKeys.Color.OnCautionContainer, ShinyThemeKeys.Color.Caution),
        [PillType.Critical] = (ShinyThemeKeys.Color.CriticalContainer, ShinyThemeKeys.Color.OnCriticalContainer, ShinyThemeKeys.Color.Critical),
    };


    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(PillView),
        string.Empty,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(PillView), () =>
            {
                ((PillView)b).label.Text = (string)n;
            }));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    // NOTE: the field MUST be named "TypeProperty" to match the CLR property "Type" — MAUI's XAML
    // binding resolver looks up "{PropertyName}Property", so a mismatched name makes Type un-bindable.
    public static readonly BindableProperty TypeProperty = BindableProperty.Create(
        nameof(Type),
        typeof(PillType),
        typeof(PillView),
        PillType.None,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(PillView), () => OnPillTypeChanged(b, o, n)));

    public PillType Type
    {
        get => (PillType)GetValue(TypeProperty);
        set => SetValue(TypeProperty, value);
    }

    public static readonly BindableProperty PillColorProperty = BindableProperty.Create(
        nameof(PillColor),
        typeof(Color),
        typeof(PillView),
        null,
        propertyChanged: (b, o, n) => StyleGuard.WhenReady(b, typeof(PillView), () => OnPillColorChanged(b, o, n)));

    public Color? PillColor
    {
        get => (Color?)GetValue(PillColorProperty);
        set => SetValue(PillColorProperty, value);
    }

    public static readonly BindableProperty PillTextColorProperty = BindableProperty.Create(
        nameof(PillTextColor),
        typeof(Color),
        typeof(PillView),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(PillView), () =>
            {
            var pill = (PillView)b;
            if (n is Color c)
                pill.label.TextColor = c;
        }));

    public Color? PillTextColor
    {
        get => (Color?)GetValue(PillTextColorProperty);
        set => SetValue(PillTextColorProperty, value);
    }

    public static readonly BindableProperty PillBorderColorProperty = BindableProperty.Create(
        nameof(PillBorderColor),
        typeof(Color),
        typeof(PillView),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(PillView), () =>
            {
            var pill = (PillView)b;
            if (n is Color c)
                pill.border.Stroke = c;
        }));

    public Color? PillBorderColor
    {
        get => (Color?)GetValue(PillBorderColorProperty);
        set => SetValue(PillBorderColorProperty, value);
    }

    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize),
        typeof(double),
        typeof(PillView),
        ThemeTokens.Unset,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(PillView), () =>
            {
                ((PillView)b).label.SetTokenOrValue(Label.FontSizeProperty, (double)n, ShinyThemeKeys.Type.BodySmallSize);
            }));

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(
        nameof(CornerRadius),
        typeof(double),
        typeof(PillView),
        ThemeTokens.Unset,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(PillView), () =>
            {
                var pill = (PillView)b;
                var shape = new Microsoft.Maui.Controls.Shapes.RoundRectangle();
                shape.SetCornerTokenOrValue((double)n, ShinyThemeKeys.Shape.CornerMediumRadius);
                pill.border.StrokeShape = shape;
            }));

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly BindableProperty FontAttributesProperty = BindableProperty.Create(
        nameof(FontAttributes),
        typeof(FontAttributes),
        typeof(PillView),
        Microsoft.Maui.Controls.FontAttributes.None,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(PillView), () =>
            {
                ((PillView)b).label.FontAttributes = (FontAttributes)n;
            }));

    public FontAttributes FontAttributes
    {
        get => (FontAttributes)GetValue(FontAttributesProperty);
        set => SetValue(FontAttributesProperty, value);
    }



    static void OnPillTypeChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var pill = (PillView)bindable;
        pill.ApplyPillType((PillType)newValue);
    }

    static void OnPillColorChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var pill = (PillView)bindable;
        if (pill.isUpdatingFromType) return;

        if (newValue is Color baseColor)
            pill.ApplyBaseColor(baseColor);
    }



    void ApplyPillType(PillType type)
    {
        // Try to find a user-defined style for this pill type.
        // The style sets PillColor/PillTextColor/PillBorderColor which
        // flow through the normal property-changed handlers to the visuals.
        if (StyleKeys.TryGetValue(type, out var key) && TryFindStyle(key, out var style))
        {
            Style = style;
            return;
        }

        // Fall back to theme tokens — bound via dynamic resources so a runtime theme/appearance
        // switch restyles the pill automatically. Explicit Pill*Color properties still win.
        Style = null;
        var (bgKey, textKey, borderKey) = TypeTokens[type];

        isUpdatingFromType = true;
        border.SetDynamicResource(VisualElement.BackgroundColorProperty, bgKey);

        if (PillBorderColor is Color borderColor)
        {
            border.Stroke = borderColor;
        }
        else
        {
            // Stroke is a Brush; drive its Color from the token so theme swaps propagate.
            border.Stroke = ThemeBrush.FromToken(borderKey);
        }

        if (PillTextColor is Color textColor)
            label.TextColor = textColor;
        else
            label.SetDynamicResource(Label.TextColorProperty, textKey);

        isUpdatingFromType = false;
    }

    bool TryFindStyle(string key, out Style style)
    {
        style = null!;
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Style s && s.TargetType == typeof(PillView))
        {
            style = s;
            return true;
        }
        return false;
    }

    void ApplyBaseColor(Color baseColor)
    {
        border.BackgroundColor = baseColor;

        if (PillBorderColor is null)
            border.Stroke = DarkenColor(baseColor, 0.2f);

        if (PillTextColor is null)
            label.TextColor = GetContrastTextColor(baseColor);
    }

    static Color DarkenColor(Color color, float amount)
    {
        color.ToHsl(out var h, out var s, out var l);
        l = Math.Max(0, l - amount);
        return Color.FromHsla(h, s, l);
    }

    /// <summary>
    /// Readable ink for an arbitrary fill, by WCAG relative luminance. Internal because
    /// <see cref="TagChipView"/> needs the same answer for a caller-picked chip colour: two copies of a
    /// contrast rule is how one of them drifts into being subtly wrong.
    /// </summary>
    internal static Color GetContrastTextColor(Color bgColor)
    {
        // Relative luminance per WCAG
        var luminance = 0.2126 * Linearize(bgColor.Red)
                      + 0.7152 * Linearize(bgColor.Green)
                      + 0.0722 * Linearize(bgColor.Blue);

        // Light backgrounds get dark text, dark backgrounds get white text
        if (luminance > 0.4)
            return DarkenColor(bgColor, 0.5f);

        return Colors.White;
    }

    static double Linearize(float channel)
    {
        return channel <= 0.04045
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

}
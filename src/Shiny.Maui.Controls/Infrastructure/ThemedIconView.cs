using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls.Infrastructure;

/// <summary>
/// A drawn icon that inks itself from a theme colour token, and keeps following it.
/// </summary>
/// <remarks>
/// <para>
/// A vector icon's colour lives on its <see cref="IDrawable"/>, which is a plain object: whatever colour
/// it is handed is the colour it draws forever. The ribbon icons used to be handed a colour copied out of
/// <c>Application.Current.Resources</c> when their template was inflated, so a light/dark flip left every
/// icon in the old palette's ink, and a palette scoped over part of the tree (a pinned theme) was never
/// seen at all - dark icons on a dark ribbon.
/// </para>
/// <para>
/// Here the colour is a bindable property fed by <see cref="Element.SetDynamicResource"/>, which resolves
/// up the element tree (so a scoped palette wins over the application's) and re-fires on every swap. The
/// change is pushed into the drawable and the view is invalidated.
/// </para>
/// </remarks>
internal class ThemedIconView : GraphicsView
{
    readonly Action<Color> applyColor;

    public ThemedIconView(IDrawable drawable, Action<Color> applyColor, double size, string token = ShinyThemeKeys.Color.OnSurfaceVariant)
    {
        this.applyColor = applyColor;
        this.Drawable = drawable;
        this.HeightRequest = size;
        this.WidthRequest = size;
        this.InputTransparent = true;
        this.HorizontalOptions = LayoutOptions.Center;
        this.VerticalOptions = LayoutOptions.Center;

        this.SetDynamicResource(IconColorProperty, token);

        // The property-changed callback only runs when the resolved value differs from the default, so
        // the drawable is seeded explicitly either way.
        this.applyColor(this.IconColor);
    }

    /// <summary>What the icon draws in until the token resolves - no theme merged, or a pack lacking the key.</summary>
    static readonly Color MissingTokenFallback = Colors.Gray;

    public static readonly BindableProperty IconColorProperty = BindableProperty.Create(
        nameof(IconColor),
        typeof(Color),
        typeof(ThemedIconView),
        MissingTokenFallback,
        propertyChanged: static (b, _, n) =>
        {
            var view = (ThemedIconView)b;
            view.applyColor((Color)n);
            view.Invalidate();
        });

    public Color IconColor
    {
        get => (Color)this.GetValue(IconColorProperty);
        set => this.SetValue(IconColorProperty, value);
    }
}

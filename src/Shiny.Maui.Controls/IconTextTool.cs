using System.Windows.Input;
using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

/// <summary>
/// The shared shape behind the small icon/label buttons Shiny docks around text input — the tools
/// inside a <see cref="TextEntry"/> and the items on a keyboard accessory bar. Icon, optional label,
/// a tap that raises <see cref="Clicked"/> and runs <see cref="Command"/>.
/// </summary>
public abstract class IconTextTool : ContentView
{
    private protected readonly Image iconImage;
    private protected readonly Label textLabel;

    protected IconTextTool()
    {
        this.iconImage = new Image
        {
            WidthRequest = 20,
            HeightRequest = 20,
            Aspect = Aspect.AspectFit,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            IsVisible = false
        };

        this.textLabel = new Label
        {
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false
        }.WithFontSize(ShinyThemeKeys.Type.BodyMediumSize);

        var layout = new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Children = { this.iconImage, this.textLabel }
        };

        // The gesture goes on the tool itself, not the inner layout, so the whole padded hit target
        // is tappable rather than just the glyph.
        var tap = new TapGestureRecognizer();
        tap.Tapped += this.OnTapped;
        this.GestureRecognizers.Add(tap);

        this.Content = layout;
        this.VerticalOptions = LayoutOptions.Fill;
        this.ApplyToolColor();

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(IconTextTool));
    }

    public static readonly BindableProperty IconProperty = BindableProperty.Create(
        nameof(Icon), typeof(ImageSource), typeof(IconTextTool), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(IconTextTool), () =>
        {
            var t = (IconTextTool)b;
            t.iconImage.Source = n as ImageSource;
            t.iconImage.IsVisible = n is not null;
            t.ApplyToolColor();
        }));
    public ImageSource? Icon { get => (ImageSource?)GetValue(IconProperty); set => SetValue(IconProperty, value); }

    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text), typeof(string), typeof(IconTextTool), null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(IconTextTool), () =>
        {
            var t = (IconTextTool)b;
            t.textLabel.Text = n as string;
            t.textLabel.IsVisible = !string.IsNullOrEmpty(n as string);
        }));
    public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// <summary>
    /// Tint for the label and — when <see cref="Icon"/> is a <see cref="FontImageSource"/> — the
    /// glyph. Unset follows the on-surface-variant theme token.
    /// </summary>
    public static readonly BindableProperty ToolColorProperty = BindableProperty.Create(
        nameof(ToolColor), typeof(Color), typeof(IconTextTool), null,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady(b, typeof(IconTextTool), () =>
        {
            ((IconTextTool)b).ApplyToolColor();
        }));
    public Color? ToolColor { get => (Color?)GetValue(ToolColorProperty); set => SetValue(ToolColorProperty, value); }

    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize), typeof(double), typeof(IconTextTool), 14.0,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(IconTextTool), () =>
        {
            ((IconTextTool)b).textLabel.FontSize = (double)n;
        }));
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

    /// <summary>
    /// Bold/italic for the label. The reason it exists is glyph labels that mean their own styling —
    /// a markdown bar's <c>B</c> and <c>I</c> read as broken drawn in the regular face.
    /// </summary>
    public static readonly BindableProperty FontAttributesProperty = BindableProperty.Create(
        nameof(FontAttributes), typeof(FontAttributes), typeof(IconTextTool), FontAttributes.None,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(IconTextTool), () =>
        {
            ((IconTextTool)b).textLabel.FontAttributes = (FontAttributes)n;
        }));
    public FontAttributes FontAttributes { get => (FontAttributes)GetValue(FontAttributesProperty); set => SetValue(FontAttributesProperty, value); }

    public static readonly BindableProperty IconSizeProperty = BindableProperty.Create(
        nameof(IconSize), typeof(double), typeof(IconTextTool), 20.0,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(IconTextTool), () =>
        {
            var t = (IconTextTool)b;
            t.iconImage.WidthRequest = (double)n;
            t.iconImage.HeightRequest = (double)n;
        }));
    public double IconSize { get => (double)GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(IconTextTool));
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
        nameof(CommandParameter), typeof(object), typeof(IconTextTool));
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    public event EventHandler? Clicked;

    // A MAUI Image cannot be tinted, so only a FontImageSource glyph follows ToolColor. That covers
    // the icon fonts everything in this repo uses; a PNG stays whatever colour it was drawn.
    private protected void ApplyToolColor()
    {
        if (this.ToolColor is Color c)
        {
            this.textLabel.TextColor = c;
            if (this.iconImage.Source is FontImageSource f)
                f.Color = c;
        }
        else
        {
            this.textLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
            if (this.iconImage.Source is FontImageSource f)
                f.SetDynamicResource(FontImageSource.ColorProperty, ShinyThemeKeys.Color.OnSurfaceVariant);
        }
    }

    internal void Invoke()
    {
        if (!this.IsEnabled)
            return;

        this.Clicked?.Invoke(this, EventArgs.Empty);
        if (this.Command?.CanExecute(this.CommandParameter) == true)
            this.Command.Execute(this.CommandParameter);
    }

    void OnTapped(object? sender, TappedEventArgs e)
    {
        this.Invoke();
        if (this.IsEnabled)
            this.FlashPress();
    }

    // Fire-and-forget press feedback. Deliberately runs after Invoke so the command is not delayed by
    // the animation, and touches Opacity only - swapping Shadow or ZIndex here would cancel the
    // focus/gesture the user is in the middle of.
    async void FlashPress()
    {
        try
        {
            await this.FadeToAsync(0.45, 60);
            await this.FadeToAsync(1, 90);
        }
        catch
        {
            this.Opacity = 1;
        }
    }
}

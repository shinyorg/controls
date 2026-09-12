using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;

namespace Shiny.Maui.Controls;

public partial class ShinyButton
{
    /// <summary>
    /// The four theme tokens a <see cref="ButtonType"/> draws from — the Material 3 colour-role
    /// quartet.
    /// </summary>
    readonly record struct TypeTokens(string Base, string On, string Container, string OnContainer);

    static TypeTokens TokensFor(ButtonType type) => type switch
    {
        ButtonType.Secondary => new(
            ShinyThemeKeys.Color.Secondary,
            ShinyThemeKeys.Color.OnSecondary,
            ShinyThemeKeys.Color.SecondaryContainer,
            ShinyThemeKeys.Color.OnSecondaryContainer),

        ButtonType.Success => new(
            ShinyThemeKeys.Color.Success,
            ShinyThemeKeys.Color.OnSuccess,
            ShinyThemeKeys.Color.SuccessContainer,
            ShinyThemeKeys.Color.OnSuccessContainer),

        ButtonType.Warning => new(
            ShinyThemeKeys.Color.Warning,
            ShinyThemeKeys.Color.OnWarning,
            ShinyThemeKeys.Color.WarningContainer,
            ShinyThemeKeys.Color.OnWarningContainer),

        ButtonType.Critical => new(
            ShinyThemeKeys.Color.Critical,
            ShinyThemeKeys.Color.OnCritical,
            ShinyThemeKeys.Color.CriticalContainer,
            ShinyThemeKeys.Color.OnCriticalContainer),

        ButtonType.Info => new(
            ShinyThemeKeys.Color.Info,
            ShinyThemeKeys.Color.OnInfo,
            ShinyThemeKeys.Color.InfoContainer,
            ShinyThemeKeys.Color.OnInfoContainer),

        _ => new(
            ShinyThemeKeys.Color.Primary,
            ShinyThemeKeys.Color.OnPrimary,
            ShinyThemeKeys.Color.PrimaryContainer,
            ShinyThemeKeys.Color.OnPrimaryContainer)
    };

    /// <summary>
    /// The surface tokens for the current appearance. A null background means "paint nothing" —
    /// transparent is a real answer for Outlined and Text, not a missing token.
    /// </summary>
    (string? Background, string Foreground, string? Stroke, bool Shadow) ResolveTokens()
    {
        var t = TokensFor(this.Type);

        return this.Appearance switch
        {
            ButtonAppearance.Tonal => (t.Container, t.OnContainer, null, false),
            ButtonAppearance.Outlined => (null, t.Base, ShinyThemeKeys.Color.Outline, false),
            ButtonAppearance.Text => (null, t.Base, null, false),
            ButtonAppearance.Elevated => (ShinyThemeKeys.Color.SurfaceContainerLow, t.Base, null, true),
            _ => (t.Base, t.On, null, false)
        };
    }

    /// <summary>
    /// Repaints the surface. Every explicit colour property short-circuits its token, so a caller who
    /// sets one colour keeps the theme for the rest.
    /// </summary>
    void ApplyAppearance()
    {
        var (bgToken, fgToken, strokeToken, wantsShadow) = this.ResolveTokens();

        // Background. Every one of these goes through ThemeProbe.Tint rather than a plain assignment,
        // because a *local* value outranks a dynamic resource and nothing here is written once: an
        // appearance can change at runtime, and a ButtonGroup in a selection mode changes it on every
        // tap. Going Outlined -> Filled writes the token while the transparent local value from the
        // outlined pass is still sitting there, and the fill silently never appears - a segmented
        // picker whose selected segment is an invisible gap with white text on white.
        var explicitBackground = this.ButtonBackgroundColor ?? (bgToken is null ? Colors.Transparent : null);
        ThemeProbe.Tint(
            this.border,
            VisualElement.BackgroundColorProperty,
            explicitBackground,
            bgToken ?? ShinyThemeKeys.Color.Surface
        );

        // Stroke. BorderThickness defaults to -1, meaning "the appearance decides".
        var thickness = this.BorderThickness >= 0
            ? this.BorderThickness
            : strokeToken is null ? 0d : 1d;

        this.border.StrokeThickness = thickness;

        if (thickness > 0)
        {
            // The stroke colour goes onto the probe, not the brush - see BuildStroke for why a
            // DynamicResource cannot reach Border.Stroke or a bare SolidColorBrush.
            ThemeProbe.Tint(
                this.strokeProbe,
                BoxView.ColorProperty,
                this.BorderColor,
                strokeToken ?? ShinyThemeKeys.Color.Outline
            );

            this.border.Stroke = this.strokeBrush;
        }
        else
        {
            this.border.Stroke = null;
        }

        // Shadow. Built once in the constructor and attached/detached here rather than being rebuilt:
        // assigning a new Shadow while the user is interacting unfocuses the content inside it on
        // Android.
        this.border.Shadow = (this.HasShadow ?? wantsShadow) ? this.shadow : null!;

        this.foregroundToken = fgToken;
        this.ApplyForeground();
        this.ApplyEnabledState();
    }

    string foregroundToken = ShinyThemeKeys.Color.OnPrimary;

    /// <summary>
    /// Tints the label and every icon the button built. Applied by token rather than by resolved
    /// <see cref="Color"/> so a live <c>ShinyThemeManager.SetTheme</c> restyles it — a
    /// <c>DynamicResource</c> keeps tracking, a snapshot colour does not.
    /// </summary>
    void ApplyForeground()
    {
        ThemeProbe.Tint(this.textLabel, Label.TextColorProperty, this.TextColor, this.foregroundToken);

        var iconColor = this.IconColor ?? this.TextColor;

        foreach (var view in this.AllSlotViews())
            this.TintIcon(view, iconColor);
    }

    void TintIcon(View? view, Color? explicitColor)
    {
        switch (view)
        {
            case null:
                return;

            case Image image when image.Source is FontImageSource glyph:
                // A MAUI Image cannot be tinted, so only a FontImageSource glyph follows the
                // foreground. A PNG stays whatever colour it was drawn - same as IconTextTool.
                ThemeProbe.Tint(glyph, FontImageSource.ColorProperty, explicitColor, this.foregroundToken);
                return;

            case ActivityIndicator spinner:
                ThemeProbe.Tint(spinner, ActivityIndicator.ColorProperty, explicitColor, this.foregroundToken);
                return;

            default:
                this.TintMotionIcon(view, explicitColor);
                return;
        }
    }

    /// <summary>
    /// Dims the painted surface when the button is disabled — including the two reasons of its own,
    /// a non-executable <see cref="Command"/> and <see cref="DisableWhileBusy"/>.
    /// </summary>
    /// <remarks>
    /// The dim lands on the inner <see cref="Border"/> rather than on the button, so it composes with
    /// the press flash (which fades the button) instead of the two fighting over one
    /// <see cref="VisualElement.Opacity"/>.
    /// </remarks>
    void ApplyEnabledState()
        => this.border.Opacity = this.IsEnabled ? 1d : this.DisabledOpacity;

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName == IsEnabledProperty.PropertyName)
            StyleGuard.WhenReady(this, typeof(ShinyButton), this.ApplyEnabledState);
    }
}

using Microsoft.Maui.Controls.Shapes;
using Shiny.Maui.Controls.Themes;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

/// <summary>
/// Wraps a single content view and overlays a small badge at one of the four corners.
/// The badge is hidden whenever <see cref="Text"/> is empty (and <see cref="IsDot"/> is false).
/// Supports a dot indicator, MaxCount overflow (e.g. "99+"), corner offsets, scale-in animation,
/// and an optional continuous pulse animation.
/// </summary>
[ContentProperty(nameof(Content))]
public partial class BadgeView : Grid, IDisposable
{
    readonly ContentView contentHost;
    readonly Border badgeBorder;
    readonly Label badgeLabel;

    bool isPulseRunning;
    bool currentlyVisible;

    public BadgeView()
    {
        this.contentHost = new ContentView();

        this.badgeLabel = new Label
        {
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap,
            FontSize = 10,
            FontAttributes = Microsoft.Maui.Controls.FontAttributes.Bold
        };

        this.badgeBorder = new Border
        {
            Padding = new Thickness(6, 2),
            StrokeThickness = 1.5,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            StrokeShape = new RoundRectangle().WithCornerRadius(ShinyThemeKeys.Shape.CornerFullRadius),
            InputTransparent = true,
            IsVisible = false,
            Scale = 0,
            Opacity = 0,
            Content = this.badgeLabel
        };

        this.Children.Add(this.contentHost);
        this.Children.Add(this.badgeBorder);

        this.ApplyBadgeVisual(animate: false);

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(BadgeView));
    }

    void OnContentChanged() => this.contentHost.Content = this.Content;

    void OnBadgeVisualChanged() => this.ApplyBadgeVisual(animate: true);

    void OnPulsingChanged()
    {
        if (this.IsPulsing && this.currentlyVisible)
            this.StartPulse();
        else
            this.StopPulse();
    }

    void ApplyBadgeVisual(bool animate)
    {
        // Colors / stroke — fall back to theme tokens when the explicit color is unset.
        if (this.BadgeColor is Color badgeColor)
            this.badgeBorder.BackgroundColor = badgeColor;
        else
            this.badgeBorder.SetDynamicResource(VisualElement.BackgroundColorProperty, ShinyThemeKeys.Color.Error);

        if (this.BadgeBorderColor is Color borderColor)
        {
            this.badgeBorder.Stroke = borderColor;
        }
        else
        {
            // Stroke is Brush-typed, so it takes the Brush twin of the token rather than the Color.
            ThemeBrush.Apply(this.badgeBorder, Border.StrokeProperty, ShinyThemeKeys.Brush.Surface);
        }
        this.badgeBorder.StrokeThickness = this.BadgeBorderThickness;
        this.badgeBorder.StrokeShape = new RoundRectangle { CornerRadius = this.CornerRadius };

        // Font / text color
        if (this.BadgeTextColor is Color textColor)
            this.badgeLabel.TextColor = textColor;
        else
            this.badgeLabel.SetDynamicResource(Label.TextColorProperty, ShinyThemeKeys.Color.OnError);
        this.badgeLabel.FontSize = this.FontSize;
        this.badgeLabel.FontAttributes = this.FontAttributes;

        // Dot vs text mode
        if (this.IsDot)
        {
            this.badgeLabel.IsVisible = false;
            this.badgeBorder.Padding = 0;
            this.badgeBorder.HeightRequest = this.DotSize;
            this.badgeBorder.WidthRequest = this.DotSize;
        }
        else
        {
            this.badgeLabel.IsVisible = true;
            this.badgeBorder.Padding = this.BadgePadding;
            this.badgeBorder.HeightRequest = -1;
            this.badgeBorder.WidthRequest = -1;
            this.badgeLabel.Text = this.ResolveDisplayText();
        }

        // Position the badge at the chosen corner with the offset.
        var (hOpts, vOpts) = this.Position switch
        {
            BadgePosition.TopLeft => (LayoutOptions.Start, LayoutOptions.Start),
            BadgePosition.TopRight => (LayoutOptions.End, LayoutOptions.Start),
            BadgePosition.BottomLeft => (LayoutOptions.Start, LayoutOptions.End),
            BadgePosition.BottomRight => (LayoutOptions.End, LayoutOptions.End),
            _ => (LayoutOptions.End, LayoutOptions.Start)
        };
        this.badgeBorder.HorizontalOptions = hOpts;
        this.badgeBorder.VerticalOptions = vOpts;

        // Offset interpretation: positive X pushes outward horizontally; sign on Y as-given (negative goes up).
        var dx = this.OffsetX;
        var dy = this.OffsetY;
        var signedX = (this.Position == BadgePosition.TopLeft || this.Position == BadgePosition.BottomLeft) ? -dx : dx;
        this.badgeBorder.TranslationX = signedX;
        this.badgeBorder.TranslationY = dy;

        // Show or hide the badge based on text and dot mode.
        var shouldShow = this.IsDot || !string.IsNullOrEmpty(this.Text);
        this.SetBadgeVisible(shouldShow, animate);
    }

    string ResolveDisplayText()
    {
        var text = this.Text ?? string.Empty;
        if (this.MaxCount > 0 && int.TryParse(text, out var n) && n > this.MaxCount)
            return $"{this.MaxCount}+";
        return text;
    }

    void SetBadgeVisible(bool show, bool animate)
    {
        if (show == this.currentlyVisible && this.badgeBorder.IsVisible == show)
        {
            if (show && this.IsPulsing && !this.isPulseRunning)
                this.StartPulse();
            return;
        }

        this.currentlyVisible = show;

        if (show)
        {
            this.badgeBorder.IsVisible = true;
            if (animate && this.IsAnimated)
            {
                _ = this.AnimateInAsync();
            }
            else
            {
                this.badgeBorder.Scale = 1;
                this.badgeBorder.Opacity = 1;
            }

            if (this.IsPulsing)
                this.StartPulse();
        }
        else
        {
            this.StopPulse();
            if (animate && this.IsAnimated)
            {
                _ = this.AnimateOutAsync();
            }
            else
            {
                this.badgeBorder.Scale = 0;
                this.badgeBorder.Opacity = 0;
                this.badgeBorder.IsVisible = false;
            }
        }
    }

    async Task AnimateInAsync()
    {
        Microsoft.Maui.Controls.ViewExtensions.CancelAnimations(this.badgeBorder);
        this.badgeBorder.Scale = 0;
        this.badgeBorder.Opacity = 0;
        try
        {
            await Task.WhenAll(
                this.badgeBorder.ScaleToAsync(1, 180, Easing.SpringOut),
                this.badgeBorder.FadeToAsync(1, 150)
            );
        }
        catch
        {
            // View can be detached mid-animation; snap to the final state below.
        }
        this.badgeBorder.Scale = 1;
        this.badgeBorder.Opacity = 1;
    }

    async Task AnimateOutAsync()
    {
        Microsoft.Maui.Controls.ViewExtensions.CancelAnimations(this.badgeBorder);
        try
        {
            await Task.WhenAll(
                this.badgeBorder.ScaleToAsync(0, 140, Easing.CubicIn),
                this.badgeBorder.FadeToAsync(0, 140)
            );
        }
        catch
        {
            // See note in AnimateInAsync.
        }
        this.badgeBorder.Scale = 0;
        this.badgeBorder.Opacity = 0;
        this.badgeBorder.IsVisible = false;
    }

    async void StartPulse()
    {
        if (this.isPulseRunning)
            return;

        this.isPulseRunning = true;
        try
        {
            while (this.isPulseRunning && this.IsPulsing && this.currentlyVisible)
            {
                try
                {
                    await this.badgeBorder.ScaleToAsync(1.18, 500, Easing.SinInOut);
                    if (!this.isPulseRunning || !this.IsPulsing || !this.currentlyVisible)
                        break;
                    await this.badgeBorder.ScaleToAsync(1.0, 500, Easing.SinInOut);
                }
                catch
                {
                    // Mid-animation detach — exit the loop cleanly.
                    break;
                }
            }
        }
        finally
        {
            this.isPulseRunning = false;
            if (this.currentlyVisible)
                this.badgeBorder.Scale = 1;
        }
    }

    void StopPulse()
    {
        this.isPulseRunning = false;
        Microsoft.Maui.Controls.ViewExtensions.CancelAnimations(this.badgeBorder);
        if (this.currentlyVisible)
            this.badgeBorder.Scale = 1;
    }

    public void Dispose()
    {
        this.StopPulse();
        GC.SuppressFinalize(this);
    }
}

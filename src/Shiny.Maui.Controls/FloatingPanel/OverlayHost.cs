using Shiny.Maui.Controls.Infrastructure;
using Shiny.Maui.Controls.Themes;
namespace Shiny.Maui.Controls.FloatingPanel;

public class OverlayHost : Grid
{
    const double DefaultBackdropMaxOpacity = 0.5;

    readonly BoxView backdrop;
    readonly List<object> activeClients = new();

    public OverlayHost()
    {
        InputTransparent = true;
        CascadeInputTransparent = false;

        backdrop = new BoxView
        {
            Opacity = 0,
            IsVisible = false,
            InputTransparent = true
        };
        FloatingPanel.Tint(backdrop, BoxView.ColorProperty, this.BackdropColor, ShinyThemeKeys.Color.Scrim);

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnBackdropTapped;
        backdrop.GestureRecognizers.Add(tap);
        Children.Add(backdrop);

        // Last line: replays any styled property that was applied before the
        // children existed. See StyleGuard.
        StyleGuard.MarkReady(this, typeof(OverlayHost));
    }

    public static readonly BindableProperty BackdropColorProperty = BindableProperty.Create(
        nameof(BackdropColor),
        typeof(Color),
        typeof(OverlayHost),
        null,
        propertyChanged: (b, _, n) => StyleGuard.WhenReady(b, typeof(OverlayHost), () =>
            {
                var host = (OverlayHost)b;
                FloatingPanel.Tint(host.backdrop, BoxView.ColorProperty, (Color?)n, ShinyThemeKeys.Color.Scrim);
            }));

    /// <summary>Leave unset to follow the active theme's scrim.</summary>
    public Color? BackdropColor
    {
        get => (Color?)GetValue(BackdropColorProperty);
        set => SetValue(BackdropColorProperty, value);
    }

    public static readonly BindableProperty BackdropMaxOpacityProperty = BindableProperty.Create(
        nameof(BackdropMaxOpacity),
        typeof(double),
        typeof(OverlayHost),
        DefaultBackdropMaxOpacity);

    public double BackdropMaxOpacity
    {
        get => (double)GetValue(BackdropMaxOpacityProperty);
        set => SetValue(BackdropMaxOpacityProperty, value);
    }

    internal async void ShowBackdrop(object client, uint animationDuration)
    {
        if (!activeClients.Contains(client))
            activeClients.Add(client);

        backdrop.InputTransparent = false;
        backdrop.IsVisible = true;
        await backdrop.FadeToAsync(BackdropMaxOpacity, animationDuration);
    }

    internal async void HideBackdrop(object client, uint animationDuration)
    {
        activeClients.Remove(client);

        if (activeClients.Count > 0)
            return;

        // ⚠️ Stop taking touches now, not when the fade ends. A fading scrim is still a full-page view
        // above the content, and if the fade never completes — the page leaves the screen mid-animation,
        // and animations only advance on a page that is being drawn — it would sit over the page for good,
        // invisible, eating every tap and scroll meant for what is underneath.
        backdrop.InputTransparent = true;
        await backdrop.FadeToAsync(0, animationDuration);

        // Another client may have asked for the backdrop while it faded; that one owns it now.
        if (activeClients.Count > 0)
            return;

        backdrop.IsVisible = false;
    }

    void OnBackdropTapped(object? sender, TappedEventArgs e)
    {
        foreach (var client in activeClients.ToList())
        {
            if (client is FloatingPanel panel)
            {
                if (panel.CloseOnBackdropTap && !panel.IsLocked)
                    panel.IsOpen = false;
            }
            else if (client is Overlay overlay)
            {
                if (overlay.CloseOnBackdropTap)
                    overlay.IsShown = false;
            }
        }
    }
}

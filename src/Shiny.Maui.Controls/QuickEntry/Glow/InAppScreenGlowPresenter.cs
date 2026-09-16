using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls.QuickEntry;

/// <summary>
/// Draws the screen-edge glow as an overlay on the current page — the only way to get it on iOS and
/// Android, and what desktop falls back to where a click-through overlay above other windows is not
/// allowed.
/// </summary>
/// <remarks>
/// <para>
/// It rims the page rather than the display, which is the same thing on a phone and not on a desktop
/// with the app in a window. That difference is the entire reason the desktop presenter exists.
/// </para>
/// <para>
/// This is an app-lifetime singleton, so it lets go of the page (and the glow view it put there) once
/// a hide has finished. Holding on until the next show kept the last page it glowed over, and every
/// view under it, alive in the meantime.
/// </para>
/// </remarks>
sealed class InAppScreenGlowPresenter : IScreenGlowPresenter
{
    readonly Func<ContentPage?> currentPage;
    ContentPage? page;
    PageOverlay.ScreenGlowLayer? layer;
    ScreenGlowView? glow;
    bool visible;

    public InAppScreenGlowPresenter() : this(PageOverlay.CurrentPage) { }

    /// <summary>Test seam: the page the glow draws on, in place of the application's current window.</summary>
    internal InAppScreenGlowPresenter(Func<ContentPage?> currentPage)
        => this.currentPage = currentPage;

    public QuickEntryPresentation Kind => QuickEntryPresentation.InApp;

    public bool IsSupported => this.currentPage() != null;

    public async Task ShowAsync(ScreenGlowOptions options)
    {
        this.Attach(options);
        var glow = this.glow;
        if (this.layer == null || glow == null)
            return;

        this.visible = true;
        this.layer.IsVisible = true;
        glow.Opacity = 0;
        glow.Start();

        // Guarded the same way as the popup's entrance: a host whose ticker never ticks would leave
        // the glow at zero opacity, present and invisible.
        var fade = glow.FadeToAsync(1, (uint)options.FadeDuration.TotalMilliseconds);
        await Task.WhenAny(fade, Task.Delay(options.FadeDuration + TimeSpan.FromMilliseconds(250))).ConfigureAwait(true);

        // A hide that finished during the fade has already released this glow.
        if (this.visible && ReferenceEquals(this.glow, glow))
            glow.Opacity = 1;
    }

    public async Task HideAsync(ScreenGlowOptions options)
    {
        var glow = this.glow;
        if (this.layer == null || glow == null)
            return;

        this.visible = false;
        var fade = glow.FadeToAsync(0, (uint)options.FadeDuration.TotalMilliseconds);
        await Task.WhenAny(fade, Task.Delay(options.FadeDuration + TimeSpan.FromMilliseconds(250))).ConfigureAwait(true);

        // A Show that landed during the fade must not be undone by this Hide, and a newer glow (shown
        // on another page meanwhile) is not this Hide's to release.
        if (this.visible || !ReferenceEquals(this.glow, glow))
            return;

        this.Detach();
    }

    public void Teardown()
    {
        this.visible = false;
        this.Detach();
    }

    /// <summary>
    /// Takes the glow off the page and forgets the page. The layer itself stays: it belongs to the
    /// page's overlay root, lives and dies with the page, and re-creating it re-parents nothing.
    /// </summary>
    void Detach()
    {
        this.glow?.Stop();
        if (this.layer != null)
        {
            if (this.glow != null)
                this.layer.Children.Remove(this.glow);
            this.layer.IsVisible = false;
        }

        this.layer = null;
        this.glow = null;
        this.page = null;
    }

    void Attach(ScreenGlowOptions options)
    {
        var current = this.currentPage();
        if (current == null)
            return;

        if (ReferenceEquals(current, this.page) && this.layer != null)
            return;

        this.Detach();

        this.page = current;
        this.layer = PageOverlay.GetOrCreateLayer<PageOverlay.ScreenGlowLayer>(current, PageOverlay.Layers.ScreenGlow);
        this.layer!.IsVisible = false;
        this.layer.Children.Clear();

        // InputTransparent all the way down: the glow is decoration over live content and must never
        // take a tap meant for what is underneath it.
        this.glow = new ScreenGlowView(options)
        {
            Opacity = 0,
            InputTransparent = true
        };
        AbsoluteLayout.SetLayoutBounds(this.glow, new Rect(0, 0, 1, 1));
        AbsoluteLayout.SetLayoutFlags(this.glow, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.All);
        this.layer.Children.Add(this.glow);
    }
}

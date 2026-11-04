using Shiny.Maui.Controls.Infrastructure;

namespace Shiny.Maui.Controls;

/// <summary>
/// The animated half of hiding the bar: <see cref="IsBarVisible"/> slides (or fades) it away and
/// back, where <see cref="VisualElement.IsVisible"/> would make it vanish in one frame. Scroll-driven
/// hiding (<see cref="ShinyTabs.HideOnScrollProperty"/>) feeds the same animation through a second,
/// internal flag, so a page that hid the bar on purpose is never re-shown by a scroll.
/// </summary>
public partial class ShinyTabBar
{
    const string VisibilityAnimationName = "ShinyTabBar.Visibility";

    /// <summary>
    /// Extra distance past the bar's own height when sliding it away, so its shadow does not leave a
    /// grey line along the bottom of the screen.
    /// </summary>
    const double SlideOvershoot = 12;

    /// <summary>Backing store for <see cref="IsBarVisible"/>.</summary>
    public static readonly BindableProperty IsBarVisibleProperty = BindableProperty.Create(
        nameof(IsBarVisible), typeof(bool), typeof(ShinyTabBar), true,
        propertyChanged: (b, _, _) => StyleGuard.WhenReady<ShinyTabBar>(b, bar => bar.ApplyBarVisibility(animate: true)));

    /// <summary>Backing store for <see cref="HideAnimation"/>.</summary>
    public static readonly BindableProperty HideAnimationProperty = BindableProperty.Create(
        nameof(HideAnimation), typeof(TabBarHideAnimation), typeof(ShinyTabBar), TabBarHideAnimation.Slide);

    /// <summary>Backing store for <see cref="HideAnimationDuration"/>.</summary>
    public static readonly BindableProperty HideAnimationDurationProperty = BindableProperty.Create(
        nameof(HideAnimationDuration), typeof(uint), typeof(ShinyTabBar), 250u);

    /// <summary>
    /// Shows or hides the bar with an animation (see <see cref="HideAnimation"/>). Unlike
    /// <see cref="VisualElement.IsVisible"/>, which removes the bar in one frame, this slides it off
    /// the bottom of the screen and gives the space back to the page once it has gone.
    /// </summary>
    public bool IsBarVisible
    {
        get => (bool)this.GetValue(IsBarVisibleProperty);
        set => this.SetValue(IsBarVisibleProperty, value);
    }

    /// <summary>How the bar leaves and comes back when <see cref="IsBarVisible"/> changes. Defaults to <see cref="TabBarHideAnimation.Slide"/>.</summary>
    public TabBarHideAnimation HideAnimation
    {
        get => (TabBarHideAnimation)this.GetValue(HideAnimationProperty);
        set => this.SetValue(HideAnimationProperty, value);
    }

    /// <summary>Length of the hide/show animation in milliseconds. Zero hides and shows instantly.</summary>
    public uint HideAnimationDuration
    {
        get => (uint)this.GetValue(HideAnimationDurationProperty);
        set => this.SetValue(HideAnimationDurationProperty, value);
    }

    bool scrollHidden;
    bool shownState = true;

    /// <summary>
    /// True when the bar is fully on screen and its host should keep a row for it. False from the
    /// first frame of a hide to the last frame of a show.
    /// </summary>
    /// <remarks>
    /// The asymmetry is the whole trick that makes a docked bar hide without the page jumping. On a
    /// hide the content is given the bar's row before the bar starts moving, so it grows underneath
    /// a bar that still covers the difference, and the bar slides away to reveal it. On a show the
    /// bar slides back over the content first, and only then does the content give the row back -
    /// again under cover. A row resized at the other end of either animation would visibly snap.
    /// </remarks>
    internal bool OccupiesRow { get; private set; } = true;

    /// <summary>The visibility the bar is showing, or heading towards: the page's flag and the scroll's, combined.</summary>
    internal bool EffectiveBarVisible => this.IsBarVisible && !this.scrollHidden;

    /// <summary>
    /// The scroll-driven half. Kept apart from <see cref="IsBarVisible"/> so it never writes the
    /// consumer's property: a binding on it would otherwise be overwritten by the first scroll.
    /// </summary>
    internal void SetScrollHidden(bool hidden)
    {
        if (this.scrollHidden == hidden)
            return;

        this.scrollHidden = hidden;
        this.ApplyBarVisibility(animate: true);
    }


    /// <summary>
    /// Brings a scroll-hidden bar back - on a tab change, and when the bar is moved to another page.
    /// A new screen starts with its chrome showing, whatever the last one was scrolled to.
    /// </summary>
    internal void ResetScrollHidden() => this.SetScrollHidden(false);


    /// <inheritdoc/>
    protected override void OnParentSet()
    {
        base.OnParentSet();

        // Under Shell the one bar follows navigation from page to page.
        this.ResetScrollHidden();
    }


    internal void ApplyBarVisibility(bool animate)
    {
        var show = this.EffectiveBarVisible;
        if (this.shownState == show)
            return;

        this.shownState = show;

        if (!show)
        {
            // Both menus hang off the bar; one left open would float over a page with no bar under it.
            this.IsMenuOpen = false;
        }

        this.AbortAnimation(VisibilityAnimationName);

        var mode = this.HideAnimation;
        var duration = this.HideAnimationDuration;

        // No handler means no animation ticker (and nothing on screen to animate), so the end state
        // is applied directly - which is also what keeps a bar declared hidden in XAML from animating
        // off a screen it was never on.
        var instant = !animate
            || mode == TabBarHideAnimation.None
            || duration == 0
            || this.Handler is null;

        if (show)
        {
            this.IsVisible = true;
            this.InputTransparent = false;

            if (instant)
            {
                this.TranslationY = 0;
                this.Opacity = 1;
                this.SetOccupiesRow(true);
                return;
            }

            if (mode == TabBarHideAnimation.Slide && this.TranslationY <= 0)
                this.TranslationY = this.HiddenOffset();
            else if (mode == TabBarHideAnimation.Fade && this.Opacity >= 1)
                this.Opacity = 0;

            var fromY = this.TranslationY;
            var fromOpacity = this.Opacity;
            new Animation(t =>
            {
                this.TranslationY = fromY * (1 - t);
                this.Opacity = fromOpacity + (1 - fromOpacity) * t;
            }).Commit(this, VisibilityAnimationName, 16, duration, Easing.CubicOut, (_, cancelled) =>
            {
                if (!cancelled)
                    this.SetOccupiesRow(true);
            });
        }
        else
        {
            // Hidden from input at once: a tap landing on a bar that is on its way out would select a
            // tab the user can no longer see.
            this.InputTransparent = true;
            this.SetOccupiesRow(false);

            if (instant)
            {
                this.TranslationY = mode == TabBarHideAnimation.Slide ? this.HiddenOffset() : 0;
                this.Opacity = mode == TabBarHideAnimation.Slide ? 1 : 0;
                this.IsVisible = false;
                return;
            }

            var fromY = this.TranslationY;
            var toY = mode == TabBarHideAnimation.Slide ? this.HiddenOffset() : fromY;
            var fromOpacity = this.Opacity;
            var toOpacity = mode == TabBarHideAnimation.Fade ? 0d : fromOpacity;
            new Animation(t =>
            {
                this.TranslationY = fromY + (toY - fromY) * t;
                this.Opacity = fromOpacity + (toOpacity - fromOpacity) * t;
            }).Commit(this, VisibilityAnimationName, 16, duration, Easing.CubicIn, (_, cancelled) =>
            {
                // Out of the layout once it is off screen, so a docked bar's Auto row collapses - the
                // content was already spanning it, so nothing on screen moves.
                if (!cancelled)
                    this.IsVisible = false;
            });
        }
    }


    double HiddenOffset()
    {
        // Height covers the overhang row the centre button rises into; the margin is the gap a
        // floating capsule keeps from the screen edge.
        var height = this.Height > 0 ? this.Height : this.BarHeight + this.bottomInset;
        return height + this.Margin.Bottom + SlideOvershoot;
    }


    void SetOccupiesRow(bool value)
    {
        if (this.OccupiesRow == value)
            return;

        this.OccupiesRow = value;
        this.BarPlacementChanged?.Invoke(this, EventArgs.Empty);
    }
}

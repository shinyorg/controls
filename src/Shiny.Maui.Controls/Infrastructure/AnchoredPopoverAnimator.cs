namespace Shiny.Maui.Controls.Infrastructure;

/// <summary>
/// Runs a <see cref="TooltipAnimation"/> on anything anchored to a target.
/// </summary>
/// <remarks>
/// Extracted from <see cref="Tooltip"/> so <see cref="FloatingToolbar"/> animates identically rather
/// than approximately. The only thing a bubble knows that a toolbar does not is where its tail sits,
/// and that arrives as <c>along</c> — the fraction of the leading edge the thing should grow out of.
/// </remarks>
static class AnchoredPopoverAnimator
{
    /// <summary>Where a scaled entry starts from. Small enough to read as growth, large enough not to pop.</summary>
    const double ScaleFrom = 0.9;

    /// <summary>How far a sliding entry travels.</summary>
    const double SlideBy = 10;


    public static async Task InAsync(
        View view,
        TooltipPlacement placement,
        TooltipAnimation animation,
        uint duration,
        double along = 0.5
    )
    {
        if (duration == 0)
            animation = TooltipAnimation.None;

        try
        {
            switch (animation)
            {
                case TooltipAnimation.None:
                    view.Opacity = 1;
                    break;

                case TooltipAnimation.Scale:
                    // Grow out of the edge nearest the target, so it reads as coming from the thing
                    // it belongs to rather than swelling out of its own middle.
                    SetGrowthAnchor(view, placement, along);
                    view.Scale = ScaleFrom;
                    await Task.WhenAll(
                        view.FadeToAsync(1, duration, Easing.CubicOut),
                        view.ScaleToAsync(1, duration, Easing.CubicOut)
                    );
                    break;

                case TooltipAnimation.Slide:
                    var (dx, dy) = SlideFrom(placement);
                    view.TranslationX = dx;
                    view.TranslationY = dy;
                    await Task.WhenAll(
                        view.FadeToAsync(1, duration, Easing.CubicOut),
                        view.TranslateToAsync(0, 0, duration, Easing.CubicOut)
                    );
                    break;

                default:
                    await view.FadeToAsync(1, duration, Easing.CubicOut);
                    break;
            }
        }
        catch
        {
            // An animation on a view detached mid-flight (the page navigated away) throws rather than
            // completing. The end state is snapped below either way.
        }

        view.Opacity = 1;
        view.Scale = 1;
        view.TranslationX = 0;
        view.TranslationY = 0;
    }


    public static async Task OutAsync(View view, TooltipAnimation animation, uint duration)
    {
        if (duration == 0)
            animation = TooltipAnimation.None;

        try
        {
            switch (animation)
            {
                case TooltipAnimation.None:
                    view.Opacity = 0;
                    break;

                case TooltipAnimation.Scale:
                    await Task.WhenAll(
                        view.FadeToAsync(0, duration, Easing.CubicIn),
                        view.ScaleToAsync(ScaleFrom, duration, Easing.CubicIn)
                    );
                    break;

                default:
                    await view.FadeToAsync(0, duration, Easing.CubicIn);
                    break;
            }
        }
        catch
        {
            // See InAsync.
        }

        view.Opacity = 0;
    }


    static void SetGrowthAnchor(View view, TooltipPlacement placement, double along)
    {
        var fraction = Math.Clamp(along, 0, 1);

        (view.AnchorX, view.AnchorY) = placement switch
        {
            TooltipPlacement.Top => (fraction, 1d),
            TooltipPlacement.Bottom => (fraction, 0d),
            TooltipPlacement.Left => (1d, fraction),
            TooltipPlacement.Right => (0d, fraction),
            _ => (0.5d, 0.5d)
        };
    }


    /// <summary>A slide comes in from the target's side, so the motion points away from it.</summary>
    static (double X, double Y) SlideFrom(TooltipPlacement placement) => placement switch
    {
        TooltipPlacement.Top => (0, SlideBy),
        TooltipPlacement.Bottom => (0, -SlideBy),
        TooltipPlacement.Left => (SlideBy, 0),
        TooltipPlacement.Right => (-SlideBy, 0),
        _ => (0, 0)
    };
}

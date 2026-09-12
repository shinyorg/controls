using Shiny.Controls.MotionIcons;
using Shiny.Maui.Controls.MotionIcons;

namespace Shiny.Maui.Controls;

/// <summary>
/// The button's motion-icon seam. Everything that knows <see cref="MotionIconView"/> exists lives
/// here, so the rest of the control is written against plain <see cref="View"/> slots.
/// </summary>
/// <remarks>
/// The button owns playback rather than letting the icons trigger themselves. Two hit targets
/// competing for the same tap is the bug that would otherwise show up — an icon with
/// <see cref="MotionTrigger.Press"/> inside a button swallows or doubles the press depending on the
/// platform. So every icon the button builds is <see cref="MotionTrigger.Manual"/>, and the button
/// calls <see cref="MotionIconView.Play"/> from its own gesture.
/// </remarks>
public partial class ShinyButton
{
    static bool IsMotionIcon(View? view) => view is MotionIconView;

    /// <summary>
    /// Builds an icon for a slot. <paramref name="looping"/> is for the busy indicator, which has to
    /// keep going rather than run a fixed number of cycles.
    /// </summary>
    MotionIconView CreateMotionIcon(string name, bool looping = false)
    {
        var icon = new MotionIconView
        {
            Icon = name,
            // Manual: the button drives playback. See the note on the class.
            Trigger = MotionTrigger.Manual,
            // Zero or less means "repeat forever" to MotionIconView.
            RepeatCount = looping ? 0 : 1,
            StrokeWidth = this.MotionIconStrokeWidth,
            WidthRequest = this.IconSize,
            HeightRequest = this.IconSize,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            // The icon is decoration inside a button that already has a label, so it must not eat the
            // tap. Without this the GraphicsView swallows presses over the glyph and the button only
            // responds around it.
            InputTransparent = true
        };

        // The button carries the accessible name; a nested node would make a screen reader announce
        // the label twice.
        AutomationProperties.SetIsInAccessibleTree(icon, false);
        return icon;
    }

    void TintMotionIcon(View? view, Color? explicitColor)
    {
        if (view is not MotionIconView icon)
            return;

        // Tint rather than a plain assignment: a local value outranks a dynamic resource, so an icon
        // that once carried an explicit colour would keep it after the colour was cleared - and an
        // appearance change rewrites the token every time. See ApplyAppearance.
        Infrastructure.ThemeProbe.Tint(icon, MotionIconView.ColorProperty, explicitColor, this.foregroundToken);
    }

    void ApplyMotionStroke()
    {
        foreach (var view in this.AllSlotViews())
        {
            if (view is MotionIconView icon)
                icon.StrokeWidth = this.MotionIconStrokeWidth;
        }
    }

    /// <summary>Starts or stops a looping icon — the busy indicator's on/off switch.</summary>
    void SetMotionPlaying(View? view, bool playing)
    {
        if (view is not MotionIconView icon)
            return;

        if (playing && this.CanAnimate)
            icon.Play();
        else
            icon.Stop();
    }

    /// <summary>Runs one cycle, for the tap and for the success/error icons drawing themselves on.</summary>
    void PlayMotionIcon(View? view)
    {
        if (this.CanAnimate && view is MotionIconView icon)
            icon.Play();
    }

    /// <summary>
    /// Whether it is safe (and worth anything) to start an animation.
    /// </summary>
    /// <remarks>
    /// An implicit <c>Style</c> is applied from MAUI's own base constructor, so a style that sets
    /// <see cref="State"/> to <see cref="ButtonState.Busy"/> reaches <c>ApplyState</c> before the
    /// button is parented, let alone rendered — and starting a ticker from there animates something
    /// nobody can see, on a view with no window. Playback is therefore deferred until
    /// <c>Loaded</c>, which re-runs <c>ApplyState</c>.
    /// </remarks>
    bool CanAnimate => this.isLoaded;

    /// <summary>
    /// Plays the leading and trailing icons on tap. Deliberately not the busy indicator or the state
    /// icons: those are driven by <see cref="State"/> and a tap must not restart them mid-cycle.
    /// </summary>
    void PlaySlotMotionIcons()
    {
        this.PlayMotionIcon(this.leftOwn);
        this.PlayMotionIcon(this.rightOwn);
    }
}

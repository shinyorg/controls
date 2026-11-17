using Shiny.Controls.MotionIcons;

namespace Shiny.Maui.Controls.MotionIcons;

/// <summary>
/// One play of a motion icon that a platform compositor runs, instead of the shared ticker.
/// </summary>
/// <remarks>
/// <para>The managed path redraws the whole icon on the UI thread every 16ms for as long as it plays.
/// That is fine for a tap, and a poor trade for something that loops all day in a menu-bar app.
/// Core Animation can run the same keyframes in the render server, where a looping icon costs the
/// app nothing per frame — which is what the browser already gives the Blazor host for free.</para>
/// <para>Only playback goes native. The resting pose, scrubbing through <see cref="MotionIconView.Progress"/>
/// and every platform without a compositor stay on the managed scene, so an icon at rest is drawn
/// by exactly the code that has always drawn it.</para>
/// </remarks>
internal interface INativeMotionPlayer : IDisposable
{
    /// <summary>Raised on the UI thread when a finite play runs out, or a cycle-end stop lands.</summary>
    event EventHandler? Finished;

    /// <summary>How far into the play it is, across every cycle so far.</summary>
    TimeSpan Position { get; }

    /// <summary>Freezes the icon where it is.</summary>
    void Pause();

    /// <summary>Carries on from where <see cref="Pause"/> left it.</summary>
    void Resume();

    /// <summary>Lets the cycle in progress finish, then raises <see cref="Finished"/>.</summary>
    void FinishCycle();

    /// <summary>The view changed size: the artwork has to be refitted without restarting the motion.</summary>
    void Relayout();
}

/// <summary>Everything a native player needs to run one play.</summary>
/// <param name="Icon">The artwork.</param>
/// <param name="Spec">The motion, already retimed and padded with any interval.</param>
/// <param name="Color">The resolved icon colour.</param>
/// <param name="Accent">The accent colour, for two-tone artwork.</param>
/// <param name="StrokeWidth">The host's stroke width in viewBox units.</param>
/// <param name="Iterations">Cycles to run, or <see cref="double.PositiveInfinity"/>.</param>
/// <param name="Rate">Playback rate; always positive here.</param>
internal sealed record NativeMotionRequest(
    MotionIconDefinition Icon,
    MotionSpec Spec,
    Color Color,
    Color? Accent,
    float StrokeWidth,
    double Iterations,
    double Rate);

/// <summary>
/// Where a platform plugs in its compositor-driven player.
/// </summary>
/// <remarks>
/// Core's iOS asset sets this itself. macOS (AppKit) and Mac Catalyst get core's <c>net11.0</c>
/// asset, which cannot touch Core Animation, so <c>Shiny.Maui.Controls.Desktop</c> sets it when it
/// loads. Unset — Android, Windows, Linux, tests — every play stays on the managed ticker.
/// </remarks>
internal static class NativeMotion
{
    /// <summary>
    /// Creates a player for a loaded view, or returns null to leave the play on the managed ticker
    /// (no platform view yet, nothing the compositor can express).
    /// </summary>
    public static Func<MotionIconView, NativeMotionRequest, INativeMotionPlayer?>? Factory { get; set; }
#if IOS
        = CoreAnimationMotionPlayer.TryCreate;
#endif
}

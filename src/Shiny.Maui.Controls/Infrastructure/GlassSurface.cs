namespace Shiny.Maui.Controls.Infrastructure;

/// <summary>
/// Apple's Liquid Glass, put behind a view that MAUI drew.
/// </summary>
/// <remarks>
/// <para>A real <c>UIGlassEffect</c> in a <c>UIVisualEffectView</c>, inserted as the bottom-most
/// native subview of the host's platform view. It is a <em>backdrop</em>, not a wrapper: the view's
/// own content keeps its MAUI parent, its layout and its gestures, and the glass simply sits
/// underneath refracting whatever the page put behind the bar.</para>
/// <para>Nothing happens off iOS 26 — <see cref="IsSupported"/> is false, <see cref="Apply"/> does
/// nothing, and the caller keeps painting the fill it always did. That is deliberate: a control that
/// went transparent on a platform with no glass to show through it would simply be invisible.</para>
/// <para>The host must be a view whose own background is transparent, or the fill it paints covers
/// the glass. Callers own that decision, because they are also the ones who have to put it back
/// when the glass goes away.</para>
/// </remarks>
static partial class GlassSurface
{
    /// <summary>
    /// Forces <see cref="IsSupported"/> for a test. Null - the default - asks the platform.
    /// </summary>
    /// <remarks>
    /// The decisions worth testing are on the MAUI side: whether the fill goes transparent, whether
    /// the shadow comes off, whether the content is allowed to run under the bar. All of them are
    /// gated on the platform answer, and the test host is plain <c>net10.0</c>, where that answer is
    /// permanently false. Without a seam here none of it could be asserted at all.
    /// </remarks>
    internal static bool? SupportOverride { get; set; }


    /// <summary>Whether this head can draw glass at all — iOS 26 and up, and nothing else yet.</summary>
    public static bool IsSupported => SupportOverride ?? PlatformIsSupported();


    /// <summary>
    /// Puts glass behind <paramref name="host"/>, or updates the glass already there.
    /// </summary>
    /// <remarks>
    /// Safe to call before the host has a handler: there is no native view to attach to yet, so it
    /// returns and waits to be called again. Callers re-apply on <c>HandlerChanged</c>, which is also
    /// what re-attaches the glass when a page is re-parented and its handlers are rebuilt.
    /// </remarks>
    public static void Apply(VisualElement host, GlassSurfaceOptions options)
    {
        if (!IsSupported)
        {
            Remove(host);
            return;
        }

        PlatformApply(host, options);
    }


    /// <summary>Takes the glass back off. A host that never had any is left alone.</summary>
    public static void Remove(VisualElement host) => PlatformRemove(host);
}


/// <summary>What a pane of glass looks like.</summary>
/// <param name="Clear">
/// The <c>Clear</c> style rather than <c>Regular</c> — thinner, and it lets far more of the content
/// behind it through. It needs something with contrast behind it to read at all.
/// </param>
/// <param name="Tint">
/// A colour wash over the glass, or null for the system's own — which already answers light and dark
/// on its own. An opaque colour here produces tinted glass, not a tinted fill.
/// </param>
/// <param name="CornerRadius">The corner radius, ignored when <paramref name="Capsule"/> is set.</param>
/// <param name="Capsule">
/// Ends that are always semicircles. Not the same thing as a large radius: the radius has to track
/// the height, and a capsule tracks it natively rather than being recomputed on every resize.
/// </param>
readonly record struct GlassSurfaceOptions(bool Clear, Color? Tint, double CornerRadius, bool Capsule);

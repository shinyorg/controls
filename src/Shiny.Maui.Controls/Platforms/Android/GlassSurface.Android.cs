namespace Shiny.Maui.Controls.Infrastructure;

/// <summary>
/// Android has no Liquid Glass, and <c>RenderEffect</c> is not a substitute for one — it blurs a
/// captured bitmap of what is behind the view, which is a frosted pane rather than a refracting one
/// and costs a capture pass per frame. <c>FrostedGlassView</c> is where that trade is worth making;
/// a tab bar that redraws on every scroll is not.
/// </summary>
static partial class GlassSurface
{
    static bool PlatformIsSupported() => false;

    static void PlatformApply(VisualElement host, GlassSurfaceOptions options)
    {
    }

    static void PlatformRemove(VisualElement host)
    {
    }
}

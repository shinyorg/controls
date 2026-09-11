#if !IOS && !MACCATALYST && !ANDROID
namespace Shiny.Maui.Controls.Infrastructure;

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
#endif

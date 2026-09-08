#if !IOS && !MACCATALYST && !ANDROID
namespace Shiny.Maui.Controls.Infrastructure;

static partial class SafeAreaInsets
{
    static double PlatformBottom() => 0;

    static double PlatformTop() => 0;
}
#endif

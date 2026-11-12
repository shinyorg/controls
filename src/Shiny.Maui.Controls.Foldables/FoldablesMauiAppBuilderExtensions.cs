using Microsoft.Maui.LifecycleEvents;
using Shiny.Maui.Controls.Foldables;
using Shiny.Maui.Controls.Infrastructure;

namespace Shiny;

public static class FoldablesMauiAppBuilderExtensions
{
    /// <summary>
    /// Make Shiny.Maui.Controls fold-aware with the built-in providers: Jetpack WindowManager on Android
    /// foldables and <c>UIHinge</c> + reserved regions on iPhone Duo (iOS 27.1+). Call alongside <c>UseShinyControls</c>, in either order.
    /// </summary>
    /// <remarks>
    /// A source passed to <c>ShinyControlConfiguration.UseFoldPosture</c> always wins over this one,
    /// whichever is registered first, so an app can still substitute its own (an adapter over
    /// Shiny.Core's <c>IScreenPosture</c>, or a simulated fold for testing).
    /// <para>Android: posture streams live only while the Activity handles
    /// <c>ScreenSize | SmallestScreenSize | ScreenLayout | Orientation</c> config changes (MAUI's
    /// default <c>MainActivity</c> does); otherwise a fold recreates the Activity and the posture is
    /// re-read when it comes back.</para>
    /// </remarks>
    public static MauiAppBuilder UseShinyFoldables(this MauiAppBuilder builder)
    {
        FoldPosture.SetSource(FoldPostureProvider.Instance, isDefault: true);

        builder.ConfigureLifecycleEvents(events =>
        {
#if ANDROID
            // Layout info is per Activity, not per process: follow whichever one is in front.
            events.AddAndroid(android => android
                .OnResume(activity => FoldPostureProvider.Instance.Attach(activity))
                .OnPause(activity => FoldPostureProvider.Instance.Detach(activity))
                .OnDestroy(activity => FoldPostureProvider.Instance.Detach(activity)));
#elif SHINY_UIHINGE
            events.AddiOS(ios => ios
                .OnActivated(_ => FoldPostureProvider.Instance.Attach())
                .SceneOnActivated(_ => FoldPostureProvider.Instance.Attach())
                .WindowSceneDidUpdateCoordinateSpace((_, _, _, _) => FoldPostureProvider.Instance.Refresh()));
#endif
        });

        return builder;
    }
}

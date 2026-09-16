using Foundation;

namespace Sample;

/// <summary>
/// iOS 27 terminates an app that has not adopted the UIScene lifecycle, before any managed code runs.
/// Registered by name from UIApplicationSceneManifest in Info.plist.
/// </summary>
[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
}

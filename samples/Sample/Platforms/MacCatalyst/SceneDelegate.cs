using Foundation;

namespace Sample;

/// <summary>
/// macOS 27 terminates a Catalyst app that has not adopted the UIScene lifecycle, before any managed
/// code runs - the same rule as iOS 27. Registered by name from UIApplicationSceneManifest in Info.plist.
/// </summary>
[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
}

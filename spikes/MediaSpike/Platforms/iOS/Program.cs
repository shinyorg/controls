using UIKit;

namespace MediaSpike;

public class Program
{
    static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}


[Foundation.Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}


[Foundation.Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate { }

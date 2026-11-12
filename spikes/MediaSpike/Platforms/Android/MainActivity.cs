using Android.App;
using Android.Content.PM;
using Android.OS;

namespace MediaSpike;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, Exported = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // adb shell am start -n dev.shiny.mediaspike/crc...MainActivity --es mode effects:cycle
        Spike.Mode = this.Intent?.GetStringExtra("mode") ?? Spike.Mode;
        base.OnCreate(savedInstanceState);
    }
}


[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, Android.Runtime.JniHandleOwnership ownership) : base(handle, ownership) { }
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

namespace Shiny.Maui.Controls;

/// <summary>Whether the user has asked the OS for less motion.</summary>
static class ReducedMotion
{
    public static bool IsEnabled
    {
        get
        {
            try
            {
#if IOS || MACCATALYST
                return UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif ANDROID
                // Android has no reduce-motion switch; "Remove animations" zeroes the animator scale.
                var resolver = Android.App.Application.Context.ContentResolver;
                return Android.Provider.Settings.Global.GetFloat(resolver, Android.Provider.Settings.Global.AnimatorDurationScale, 1f) == 0f;
#elif WINDOWS
                return !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
#else
                return false;
#endif
            }
            catch
            {
                return false;
            }
        }
    }
}

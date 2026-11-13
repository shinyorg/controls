using UIKit;

namespace Shiny.Maui.Controls.SignaturePad;

public partial class SignaturePad
{
    bool? backGestureWasEnabled;

    partial void SetBackGestureEnabled(bool enabled)
    {
        var gesture = FindNavigationController()?.InteractivePopGestureRecognizer;
        if (gesture is null)
            return;

        if (!enabled)
        {
            // Remember the original state once so we restore it faithfully.
            backGestureWasEnabled ??= gesture.Enabled;
            gesture.Enabled = false;
        }
        else
        {
            gesture.Enabled = backGestureWasEnabled ?? true;
            backGestureWasEnabled = null;
        }
    }

    UINavigationController? FindNavigationController()
    {
        // Prefer the nav controller that actually hosts our view (the current page).
        var responder = signatureView?.Surface.Handler?.PlatformView as UIResponder;
        while (responder is not null)
        {
            if (responder is UIViewController vc && vc.NavigationController is { } nav)
                return nav;

            responder = responder.NextResponder;
        }

        // Fall back to searching the active window's view-controller hierarchy.
        return Search(GetKeyWindow()?.RootViewController);

        static UINavigationController? Search(UIViewController? vc)
        {
            switch (vc)
            {
                case null:
                    return null;
                case UINavigationController nav:
                    return nav;
            }

            if (vc.PresentedViewController is { } presented && Search(presented) is { } fromPresented)
                return fromPresented;

            foreach (var child in vc.ChildViewControllers)
            {
                if (Search(child) is { } fromChild)
                    return fromChild;
            }
            return null;
        }
    }

    static UIWindow? GetKeyWindow()
    {
        foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
        {
            if (scene is UIWindowScene windowScene)
            {
                foreach (var window in windowScene.Windows)
                {
                    if (window.IsKeyWindow)
                        return window;
                }
            }
        }
        return null;
    }
}

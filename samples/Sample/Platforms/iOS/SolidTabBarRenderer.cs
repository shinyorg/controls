using Microsoft.Maui.Controls.Handlers.Compatibility;
using Microsoft.Maui.Controls.Platform.Compatibility;
using Microsoft.Maui.Platform;
using UIKit;

namespace Sample.Platforms.iOS;

// Derives from ShinyShellRenderer, not ShellRenderer: UsePageTransitions registers that one as the
// Shell handler, and this registration replaces it - so a custom renderer keeps tab and flyout-item
// transitions only by inheriting them.
public class SolidTabBarRenderer : Shiny.Maui.Controls.ShinyShellRenderer
{
    protected override IShellTabBarAppearanceTracker CreateTabBarAppearanceTracker()
    {
        return new SolidTabBarAppearanceTracker();
    }
}

public class SolidTabBarAppearanceTracker : IShellTabBarAppearanceTracker
{
    public void SetAppearance(UITabBarController controller, ShellAppearance appearance)
    {
        var tabBarAppearance = new UITabBarAppearance();
        tabBarAppearance.ConfigureWithOpaqueBackground();

        var bgColor = appearance.BackgroundColor?.ToPlatform() ?? UIColor.White;
        tabBarAppearance.BackgroundColor = bgColor;

        controller.TabBar.StandardAppearance = tabBarAppearance;
        controller.TabBar.ScrollEdgeAppearance = tabBarAppearance;
    }

    public void ResetAppearance(UITabBarController controller)
    {
        var tabBarAppearance = new UITabBarAppearance();
        tabBarAppearance.ConfigureWithOpaqueBackground();
        controller.TabBar.StandardAppearance = tabBarAppearance;
        controller.TabBar.ScrollEdgeAppearance = tabBarAppearance;
    }

    public void UpdateLayout(UITabBarController controller) { }
    public void Dispose() { }
}

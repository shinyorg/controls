using Shiny.Maui.Controls;

namespace Sample.Features.Navigation;

public partial class NavigationDemoPage : ContentPage
{
    public NavigationDemoPage()
    {
        this.InitializeComponent();
        SampleSourceCode.Attach(this);
    }

    // Shell is not a host for a NavigationPage, so the demo stack is presented modally - which is
    // also the shape a real app uses for a self-contained flow with its own chrome.
    void OnOpen(object? sender, EventArgs e)
        => _ = this.Navigation.PushModalAsync(new ShinyNavigationPage(new NavInboxPage()));

    void OnOpenLarge(object? sender, EventArgs e)
        => _ = this.Navigation.PushModalAsync(new ShinyNavigationPage(new NavInboxPage())
        {
            LargeTitleDisplay = LargeTitleDisplay.Collapsing
        });

    // Nothing here asks for the status bar or the safe area: RespectSafeArea and StatusBarStyle.Auto
    // are the defaults, so a bar colour dark enough to need a white clock is the whole setup.
    void OnOpenTinted(object? sender, EventArgs e)
        => _ = this.Navigation.PushModalAsync(new ShinyNavigationPage(new NavInboxPage())
        {
            BarBackgroundColor = Color.FromArgb("#3F2B96"),
            BarTextColor = Colors.White
        });
}

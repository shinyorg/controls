using Shiny.Maui.Controls;

namespace Sample.Features.Navigation;

public partial class NavInboxPage : ContentPage
{
    bool barHidden;

    public NavInboxPage()
    {
        this.InitializeComponent();
        SampleSourceCode.Attach(this);

        for (var i = 1; i <= 25; i++)
            this.Filler.Children.Add(new Label { Text = $"Message {i}", FontSize = 14 });
    }

    void OnMenu(object? sender, EventArgs e) => this.Say("Left item: menu");

    void OnSearch(object? sender, EventArgs e) => this.Say("Right item: search");

    void OnAlerts(object? sender, EventArgs e) => this.Say("Right item: alerts");

    void OnDrafts(object? sender, EventArgs e) => this.Say("Toolbar item: drafts");

    void OnMarkAll(object? sender, EventArgs e) => this.Say("Overflow: mark all read");

    void OnDeleteAll(object? sender, EventArgs e) => this.Say("Overflow: delete all");

    void OnPush(object? sender, EventArgs e) => _ = this.Navigation.PushAsync(new NavMessagePage());

    void OnToggleBadge(object? sender, EventArgs e)
    {
        // The item's own property is bindable, so nothing about the collection has to change for the
        // bar to redraw it.
        var alerts = ShinyNav.GetRightItems(this).OfType<NavBarItem>().First(i => i.Icon == "bell");
        alerts.Badge = alerts.Badge is null ? "9" : null;
        this.Say($"Badge is now {alerts.Badge ?? "off"}");
    }

    void OnToggleToolbarBadge(object? sender, EventArgs e)
    {
        // The same thing for a plain ToolbarItem: the attached property is bindable too, so setting
        // it is all the bar needs to redraw the item.
        var badge = ShinyNav.GetBadge(this.Drafts);
        ShinyNav.SetBadge(this.Drafts, badge is null ? "2" : null);
        this.Say($"Toolbar item badge is now {ShinyNav.GetBadge(this.Drafts) ?? "off"}");
    }

    void OnToggleBar(object? sender, EventArgs e)
    {
        // ShinyNav.IsNavBarVisible is the runtime switch. NavigationPage.SetHasNavigationBar is
        // honoured too, but only as the starting value - see the docs for why.
        this.barHidden = !this.barHidden;
        ShinyNav.SetIsNavBarVisible(this, !this.barHidden);
        this.Say(this.barHidden ? "Bar hidden" : "Bar shown");
    }

    void OnClose(object? sender, EventArgs e) => _ = this.Navigation.PopModalAsync();

    void Say(string text) => this.Status.Text = text;
}

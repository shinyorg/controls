using Microsoft.Maui.Controls;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Tests;

/// <summary>
/// A Text that is cleared and set again while the badge is still animating out shows the badge
/// again. The cancelled hide animation used to finish by hiding the bubble the show had just
/// revealed, so the badge kept its text with nothing drawn - a count briefly reset while it is
/// recomputed did exactly that.
/// </summary>
[Collection(ApplicationResourcesCollection.Name)]
public class BadgeViewAnimationTests
{
    public BadgeViewAnimationTests()
    {
        TestDispatcherProvider.Install();
        _ = new Application();
    }

    static Border Bubble(BadgeView badge) => (Border)badge.Children[1];

    [Fact]
    public async Task ClearedAndSetAgainDuringTheHideAnimation_StaysVisible()
    {
        var host = new FrozenAnimationHost();
        var badge = host.Attach(new BadgeView());
        badge.Text = "13";
        await host.Finish();

        badge.Text = string.Empty; // hide animation in flight
        badge.Text = "13";
        await host.Finish();

        Bubble(badge).IsVisible.ShouldBeTrue();
        Bubble(badge).Opacity.ShouldBe(1, 0.001);
        Bubble(badge).Scale.ShouldBe(1, 0.001);
    }

    [Fact]
    public async Task SetAndClearedAgainDuringTheShowAnimation_EndsHidden()
    {
        var host = new FrozenAnimationHost();
        var badge = host.Attach(new BadgeView());

        badge.Text = "13"; // show animation in flight
        badge.Text = string.Empty;
        await host.Finish();

        Bubble(badge).IsVisible.ShouldBeFalse();
    }
}

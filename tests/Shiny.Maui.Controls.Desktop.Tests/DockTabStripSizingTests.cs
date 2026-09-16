using Microsoft.Maui.Controls;
using Shiny.Maui.Controls.Desktop.Docking;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Desktop.Tests;

/// <summary>
/// The dock tab strip must reserve enough height for its text on its own. On the AppKit head the
/// horizontal ScrollView hosting the tabs does not measure its content, so the Auto row sized to
/// whatever else was in it and every tab title was clipped top and bottom. These pin the
/// font-derived minimum that replaced that reliance.
/// </summary>
public class DockTabStripSizingTests
{
    static DockGroup Group(int count)
    {
        var group = new DockGroup();
        for (var i = 0; i < count; i++)
            group.Tabs.Add(new DockTab { PanelTypeId = "panel" + i });
        return group;
    }

    static IEnumerable<Label> LabelsOf(Border tab) =>
        ((HorizontalStackLayout)tab.Content!).Children.OfType<Label>();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_tab_is_at_least_its_tallest_label_line_plus_padding(bool isLocked)
    {
        var strip = new DockTabStrip();
        strip.SetTabs(Group(3), t => t.PanelTypeId, _ => true, isLocked, _ => "*");

        strip.TabViews.Count.ShouldBe(3);
        foreach (var (_, view) in strip.TabViews)
        {
            var tallest = LabelsOf(view).Max(l => DockTabStrip.LineHeightFor(l.FontSize));
            // a line of text is always taller than its point size
            tallest.ShouldBeGreaterThan(LabelsOf(view).Max(l => l.FontSize));
            view.MinimumHeightRequest.ShouldBeGreaterThanOrEqualTo(tallest + view.Padding.VerticalThickness);
        }
    }

    [Fact]
    public void Strip_reserves_the_tab_height_plus_its_own_padding()
    {
        var strip = new DockTabStrip();
        strip.SetTabs(Group(2), t => t.PanelTypeId, _ => true, false);

        var tallestTab = strip.TabViews.Max(t => t.View.MinimumHeightRequest);
        var stack = (HorizontalStackLayout)strip.Scroller.Content!;
        strip.Scroller.MinimumHeightRequest.ShouldBeGreaterThanOrEqualTo(tallestTab + stack.Padding.VerticalThickness);
    }

    [Fact]
    public void Empty_strip_still_reserves_a_line_of_title_text()
    {
        var strip = new DockTabStrip();
        strip.SetTabs(Group(0), t => t.PanelTypeId, _ => true, false);

        strip.Scroller.MinimumHeightRequest.ShouldBeGreaterThanOrEqualTo(
            DockTabStrip.LineHeightFor(DockTabStrip.TitleFontSize) + DockTabStrip.TabPadding.VerticalThickness);
    }
}

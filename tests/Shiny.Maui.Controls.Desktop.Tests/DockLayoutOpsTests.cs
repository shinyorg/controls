using Shiny.Maui.Controls.Desktop.Docking;
using Shouldly;
using Xunit;

namespace Shiny.Maui.Controls.Desktop.Tests;

/// <summary>
/// The layout-tree rules behind every drag, float and re-dock. The host only decides what the user
/// asked for — which guide they let go on — so these are the rules that decide whether a panel ends
/// up where the preview said it would.
/// </summary>
public class DockLayoutOpsTests
{
    static readonly DockRect Anywhere = new(10, 20, 300, 200);

    static DockTab Tab(string id) => new() { PanelTypeId = id, PanelInstanceId = id };

    static DockGroup Group(params string[] ids)
    {
        var g = new DockGroup();
        foreach (var id in ids) g.Tabs.Add(Tab(id));
        return g;
    }

    static DockRoot Root(DockNode doc, DockNode? left = null) => new()
    {
        MainWindow = new DockWindowState { DocumentArea = doc, LeftRail = left }
    };

    static IEnumerable<string> Ids(DockGroup g) => g.Tabs.Select(t => t.PanelInstanceId);

    [Fact]
    public void Dropping_a_tab_beside_itself_in_its_own_strip_changes_nothing()
    {
        var g = Group("a", "b", "c");
        var root = Root(g);

        // index 1 = the gap before "b" = right where "b" already is; 2 = right after it
        DockLayoutOps.DropTab(root, "b", g, DockZone.TabStrip, 1, Anywhere).ShouldBeFalse();
        DockLayoutOps.DropTab(root, "b", g, DockZone.TabStrip, 2, Anywhere).ShouldBeFalse();
        Ids(g).ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public void Reordering_within_a_strip_accounts_for_the_tab_leaving_its_old_slot()
    {
        var g = Group("a", "b", "c");
        var root = Root(g);

        DockLayoutOps.DropTab(root, "a", g, DockZone.TabStrip, 3, Anywhere).ShouldBeTrue();

        Ids(g).ShouldBe(["b", "c", "a"]);
        g.ActiveTabIndex.ShouldBe(2);
    }

    [Fact]
    public void Centre_guide_merges_into_the_target_and_prunes_the_emptied_source()
    {
        var source = Group("a");
        var target = Group("b");
        var root = Root(target, left: source);

        DockLayoutOps.DropTab(root, "a", target, DockZone.Center, -1, Anywhere).ShouldBeTrue();

        Ids(target).ShouldBe(["b", "a"]);
        target.ActiveTabIndex.ShouldBe(1);
        root.MainWindow.LeftRail.ShouldBeNull();
    }

    [Theory]
    [InlineData(DockZone.Left, DockOrientation.Horizontal, true)]
    [InlineData(DockZone.Right, DockOrientation.Horizontal, false)]
    [InlineData(DockZone.Top, DockOrientation.Vertical, true)]
    [InlineData(DockZone.Bottom, DockOrientation.Vertical, false)]
    public void Compass_side_guides_split_the_target_on_that_side(DockZone zone, DockOrientation orientation, bool incomingFirst)
    {
        var target = Group("doc", "other");
        var root = Root(target);

        DockLayoutOps.DropTab(root, "other", target, zone, -1, Anywhere).ShouldBeTrue();

        var split = root.MainWindow.DocumentArea.ShouldBeOfType<DockSplit>();
        split.Orientation.ShouldBe(orientation);
        var incoming = (incomingFirst ? split.First : split.Second).ShouldBeOfType<DockGroup>();
        Ids(incoming).ShouldBe(["other"]);
        (incomingFirst ? split.Second : split.First).ShouldBeSameAs(target);
    }

    [Fact]
    public void A_lone_tab_cannot_split_its_own_group()
    {
        var g = Group("a");
        var root = Root(g);

        DockLayoutOps.DropTab(root, "a", g, DockZone.Left, -1, Anywhere).ShouldBeFalse();
        root.MainWindow.DocumentArea.ShouldBeSameAs(g);
    }

    [Theory]
    [InlineData(DockZone.Left)]
    [InlineData(DockZone.Right)]
    [InlineData(DockZone.Top)]
    [InlineData(DockZone.Bottom)]
    public void Outer_edge_guides_dock_into_that_rail(DockZone zone)
    {
        var doc = Group("doc", "tool");
        var root = Root(doc);

        DockLayoutOps.DropTab(root, "tool", null, zone, -1, Anywhere).ShouldBeTrue();

        var rail = DockLayoutOps.RailNode(root.MainWindow, DockLayoutOps.AreaFor(zone)!.Value).ShouldBeOfType<DockGroup>();
        Ids(rail).ShouldBe(["tool"]);
        Ids(doc).ShouldBe(["doc"]);
    }

    [Fact]
    public void Tearing_off_floats_at_the_drop_bounds_and_remembers_home()
    {
        var home = Group("a", "b");
        var root = Root(home);

        DockLayoutOps.DropTab(root, "b", null, DockZone.TearOff, -1, new DockRect(40, 50, 320, 240)).ShouldBeTrue();

        var fw = root.FloatingWindows.ShouldHaveSingleItem();
        fw.Bounds.ShouldBe(new DockRect(40, 50, 320, 240));
        fw.RestoreGroupId.ShouldBe(home.GroupId);
        Ids(fw.DocumentArea.ShouldBeOfType<DockGroup>()).ShouldBe(["b"]);
    }

    [Fact]
    public void Tear_off_bounds_are_clamped_to_a_usable_window()
    {
        var root = Root(Group("a", "b"));

        DockLayoutOps.DropTab(root, "b", null, DockZone.TearOff, -1, new DockRect(-30, -5, 10, 10)).ShouldBeTrue();

        var b = root.FloatingWindows[0].Bounds!;
        b.X.ShouldBe(0);
        b.Y.ShouldBe(0);
        b.Width.ShouldBe(DockLayoutOps.MinFloatWidth);
        b.Height.ShouldBe(DockLayoutOps.MinFloatHeight);
    }

    [Fact]
    public void Docking_back_returns_the_window_to_the_group_it_came_from()
    {
        var home = Group("a", "b");
        var root = Root(home);
        DockLayoutOps.FloatTab(root, "b", Anywhere).ShouldNotBeNull();

        var tabs = DockLayoutOps.DockFloatingBack(root, 0);

        tabs.Select(t => t.PanelInstanceId).ShouldBe(["b"]);
        root.FloatingWindows.ShouldBeEmpty();
        Ids(home).ShouldBe(["a", "b"]);
    }

    [Fact]
    public void Docking_back_falls_back_to_the_left_rail_when_home_is_gone()
    {
        var home = Group("a");
        var root = Root(Group("doc"), left: home);
        DockLayoutOps.FloatTab(root, "a", Anywhere).ShouldNotBeNull();
        // the only tab left, so its group was pruned
        root.MainWindow.LeftRail.ShouldBeNull();

        DockLayoutOps.DockFloatingBack(root, 0);

        Ids(root.MainWindow.LeftRail.ShouldBeOfType<DockGroup>()).ShouldBe(["a"]);
    }

    [Fact]
    public void Floating_the_only_panel_of_a_window_is_a_no_op()
    {
        var root = Root(Group("doc", "x"));
        var fw = DockLayoutOps.FloatTab(root, "x", Anywhere)!;

        DockLayoutOps.FloatTab(root, "x", Anywhere).ShouldBeSameAs(fw);
        root.FloatingWindows.Count.ShouldBe(1);
    }

    [Fact]
    public void A_floating_window_dropped_on_a_side_guide_keeps_its_own_arrangement()
    {
        var target = Group("doc");
        var root = Root(target);
        var inner = new DockSplit { Orientation = DockOrientation.Vertical, First = Group("p"), Second = Group("q") };
        root.FloatingWindows.Add(new DockWindowState { DocumentArea = inner });

        DockLayoutOps.DropFloatingWindow(root, 0, target, DockZone.Right, -1).ShouldBeTrue();

        root.FloatingWindows.ShouldBeEmpty();
        var split = root.MainWindow.DocumentArea.ShouldBeOfType<DockSplit>();
        split.First.ShouldBeSameAs(target);
        split.Second.ShouldBeSameAs(inner);
    }

    [Fact]
    public void A_floating_window_dropped_on_a_strip_inserts_all_its_tabs_there()
    {
        var target = Group("a", "b");
        var root = Root(target);
        root.FloatingWindows.Add(new DockWindowState { DocumentArea = Group("x", "y") });

        DockLayoutOps.DropFloatingWindow(root, 0, target, DockZone.TabStrip, 1).ShouldBeTrue();

        Ids(target).ShouldBe(["a", "x", "y", "b"]);
        target.ActiveTabIndex.ShouldBe(2);
    }

    [Fact]
    public void A_floating_window_on_an_occupied_rail_is_stacked_beside_it()
    {
        var rail = Group("tool");
        var root = Root(Group("doc"), left: rail);
        var floating = Group("x");
        root.FloatingWindows.Add(new DockWindowState { DocumentArea = floating });

        DockLayoutOps.DropFloatingWindow(root, 0, null, DockZone.Left, -1).ShouldBeTrue();

        var split = root.MainWindow.LeftRail.ShouldBeOfType<DockSplit>();
        split.Orientation.ShouldBe(DockOrientation.Vertical);
        split.First.ShouldBeSameAs(rail);
        split.Second.ShouldBeSameAs(floating);
    }

    [Fact]
    public void A_floating_window_cannot_dock_into_itself()
    {
        var root = Root(Group("doc"));
        var own = Group("x", "y");
        root.FloatingWindows.Add(new DockWindowState { DocumentArea = own });

        DockLayoutOps.DropFloatingWindow(root, 0, own, DockZone.Center, -1).ShouldBeFalse();
        root.FloatingWindows.ShouldHaveSingleItem();
    }

    [Fact]
    public void Simplify_drops_floating_windows_left_empty()
    {
        var root = Root(Group("doc"));
        root.FloatingWindows.Add(new DockWindowState { DocumentArea = new DockGroup() });
        root.FloatingWindows.Add(new DockWindowState { DocumentArea = Group("keep") });

        DockLayoutOps.Simplify(root);

        Ids(root.FloatingWindows.ShouldHaveSingleItem().DocumentArea.ShouldBeOfType<DockGroup>()).ShouldBe(["keep"]);
    }

    [Fact]
    public void RestoreGroupId_survives_serialization()
    {
        var root = Root(Group("a", "b"));
        DockLayoutOps.FloatTab(root, "b", Anywhere);

        var json = DockSerialization.Serialize(root);
        var back = DockSerialization.Deserialize(json)!;

        back.FloatingWindows[0].RestoreGroupId.ShouldBe(root.FloatingWindows[0].RestoreGroupId);
    }
}

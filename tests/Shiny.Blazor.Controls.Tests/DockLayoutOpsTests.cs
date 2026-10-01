using Shiny.Blazor.Controls.Docking;
using Shouldly;
using Xunit;

namespace Shiny.Blazor.Controls.Tests;

/// <summary>
/// The tree rules behind every drag, float and re-dock. A wrong rule here never throws: the panel
/// just lands somewhere else, or vanishes with an emptied group, so each case pins where it ends up.
/// </summary>
public class DockLayoutOpsTests
{
    static readonly DockRect Bounds = new(100, 80, 360, 260);

    static DockTab Tab(string id) => new() { PanelTypeId = id, PanelInstanceId = id };

    static (DockRoot Root, DockGroup Docs, DockGroup Left) Layout()
    {
        var docs = new DockGroup { GroupId = "docs", Tabs = { Tab("a"), Tab("b"), Tab("c") } };
        var left = new DockGroup { GroupId = "left", Tabs = { Tab("explorer") } };
        var root = new DockRoot
        {
            MainWindow = new DockWindowState { DocumentArea = docs, LeftRail = left }
        };
        return (root, docs, left);
    }

    static string[] Ids(DockGroup g) => g.Tabs.Select(t => t.PanelInstanceId).ToArray();


    [Fact]
    public void DroppingATabBackIntoItsOwnGapChangesNothing()
    {
        var (root, docs, _) = Layout();

        DockLayoutOps.DropTab(root, "b", docs, DockZone.TabStrip, 1, Bounds).ShouldBeFalse();
        DockLayoutOps.DropTab(root, "b", docs, DockZone.TabStrip, 2, Bounds).ShouldBeFalse();
        Ids(docs).ShouldBe(["a", "b", "c"]);
    }


    [Fact]
    public void TabStripDropReordersWithinTheGroup()
    {
        var (root, docs, _) = Layout();

        DockLayoutOps.DropTab(root, "a", docs, DockZone.TabStrip, 3, Bounds).ShouldBeTrue();

        Ids(docs).ShouldBe(["b", "c", "a"]);
        docs.ActiveTabIndex.ShouldBe(2);
    }


    [Fact]
    public void CentreDropMergesAndTheEmptiedRailDisappears()
    {
        var (root, docs, _) = Layout();

        DockLayoutOps.DropTab(root, "explorer", docs, DockZone.Center, -1, Bounds).ShouldBeTrue();

        Ids(docs).ShouldBe(["a", "b", "c", "explorer"]);
        root.MainWindow.LeftRail.ShouldBeNull();
    }


    [Fact]
    public void CompassEdgeSplitsTheTargetGroup()
    {
        var (root, docs, _) = Layout();

        DockLayoutOps.DropTab(root, "c", docs, DockZone.Right, -1, Bounds).ShouldBeTrue();

        var split = root.MainWindow.DocumentArea.ShouldBeOfType<DockSplit>();
        split.Orientation.ShouldBe(DockOrientation.Horizontal);
        split.First.ShouldBeSameAs(docs);
        Ids(split.Second.ShouldBeOfType<DockGroup>()).ShouldBe(["c"]);
    }


    [Fact]
    public void SplittingYourOnlyTabAgainstYourselfIsANoOp()
    {
        var (root, _, left) = Layout();

        DockLayoutOps.DropTab(root, "explorer", left, DockZone.Bottom, -1, Bounds).ShouldBeFalse();
        root.MainWindow.LeftRail.ShouldBeSameAs(left);
    }


    [Fact]
    public void OuterGuideDocksIntoThatRail()
    {
        var (root, _, _) = Layout();

        DockLayoutOps.DropTab(root, "b", null, DockZone.Bottom, -1, Bounds).ShouldBeTrue();

        Ids(root.MainWindow.BottomRail.ShouldBeOfType<DockGroup>()).ShouldBe(["b"]);
    }


    [Fact]
    public void TearOffFloatsAtTheDropAndRemembersHome()
    {
        var (root, docs, _) = Layout();

        DockLayoutOps.DropTab(root, "b", null, DockZone.TearOff, -1, Bounds).ShouldBeTrue();

        var fw = root.FloatingWindows.ShouldHaveSingleItem();
        fw.Bounds.ShouldBe(Bounds);
        fw.RestoreGroupId.ShouldBe("docs");
        Ids(docs).ShouldBe(["a", "c"]);
    }


    [Fact]
    public void TearOffBoundsAreClampedToAUsableWindow()
    {
        var (root, _, _) = Layout();

        DockLayoutOps.DropTab(root, "a", null, DockZone.TearOff, -1, new DockRect(-50, -10, 20, 20));

        var b = root.FloatingWindows.Single().Bounds!;
        b.X.ShouldBe(0);
        b.Y.ShouldBe(0);
        b.Width.ShouldBe(DockLayoutOps.MinFloatWidth);
        b.Height.ShouldBe(DockLayoutOps.MinFloatHeight);
    }


    [Fact]
    public void DockingBackReturnsToTheGroupItCameFrom()
    {
        var (root, docs, _) = Layout();
        DockLayoutOps.FloatTab(root, "b", Bounds);

        var tabs = DockLayoutOps.DockFloatingBack(root, 0);

        tabs.Select(t => t.PanelInstanceId).ShouldBe(["b"]);
        root.FloatingWindows.ShouldBeEmpty();
        Ids(docs).ShouldBe(["a", "c", "b"]);
    }


    [Fact]
    public void DockingBackFallsBackToTheLeftRailWhenHomeIsGone()
    {
        var (root, _, left) = Layout();
        // tearing off the rail's only tab removes the rail group entirely
        DockLayoutOps.FloatTab(root, "explorer", Bounds);
        root.MainWindow.LeftRail.ShouldBeNull();

        DockLayoutOps.DockFloatingBack(root, 0);

        Ids(root.MainWindow.LeftRail.ShouldBeOfType<DockGroup>()).ShouldBe(["explorer"]);
        root.MainWindow.LeftRail.ShouldNotBeSameAs(left);
    }


    [Fact]
    public void FloatingAPanelAlreadyAloneInAWindowDoesNotNestAnother()
    {
        var (root, _, _) = Layout();
        var first = DockLayoutOps.FloatTab(root, "a", Bounds);

        DockLayoutOps.FloatTab(root, "a", Bounds).ShouldBeSameAs(first);
        root.FloatingWindows.Count.ShouldBe(1);
    }


    [Fact]
    public void AWindowDroppedOnACompassEdgeKeepsItsOwnArrangement()
    {
        var (root, docs, _) = Layout();
        var inner = new DockSplit
        {
            Orientation = DockOrientation.Vertical,
            First = new DockGroup { Tabs = { Tab("x") } },
            Second = new DockGroup { Tabs = { Tab("y") } }
        };
        root.FloatingWindows.Add(new DockWindowState { DocumentArea = inner });

        DockLayoutOps.DropFloatingWindow(root, 0, docs, DockZone.Left, -1).ShouldBeTrue();

        var split = root.MainWindow.DocumentArea.ShouldBeOfType<DockSplit>();
        split.First.ShouldBeSameAs(inner);
        split.Second.ShouldBeSameAs(docs);
        root.FloatingWindows.ShouldBeEmpty();
    }


    [Fact]
    public void AWindowDroppedOnATabStripMergesAtTheIndex()
    {
        var (root, docs, _) = Layout();
        root.FloatingWindows.Add(new DockWindowState { DocumentArea = new DockGroup { Tabs = { Tab("x"), Tab("y") } } });

        DockLayoutOps.DropFloatingWindow(root, 0, docs, DockZone.TabStrip, 1).ShouldBeTrue();

        Ids(docs).ShouldBe(["a", "x", "y", "b", "c"]);
        docs.ActiveTabIndex.ShouldBe(2);
    }


    [Fact]
    public void AWindowDroppedOnAnOccupiedRailSplitsItRatherThanFlattening()
    {
        var (root, _, left) = Layout();
        var floating = new DockGroup { Tabs = { Tab("x") } };
        root.FloatingWindows.Add(new DockWindowState { DocumentArea = floating });

        DockLayoutOps.DropFloatingWindow(root, 0, null, DockZone.Left, -1).ShouldBeTrue();

        var split = root.MainWindow.LeftRail.ShouldBeOfType<DockSplit>();
        split.Orientation.ShouldBe(DockOrientation.Vertical);
        split.First.ShouldBeSameAs(left);
        split.Second.ShouldBeSameAs(floating);
    }


    [Fact]
    public void AWindowCannotDockIntoItself()
    {
        var (root, _, _) = Layout();
        var floating = new DockGroup { Tabs = { Tab("x"), Tab("y") } };
        root.FloatingWindows.Add(new DockWindowState { DocumentArea = floating });

        DockLayoutOps.DropFloatingWindow(root, 0, floating, DockZone.Right, -1).ShouldBeFalse();
        root.FloatingWindows.ShouldHaveSingleItem();
    }


    [Fact]
    public void RestoreGroupIdSurvivesSerialization()
    {
        var (root, _, _) = Layout();
        DockLayoutOps.FloatTab(root, "b", Bounds);

        var copy = DockSerialization.Deserialize(DockSerialization.Serialize(root))!;

        copy.FloatingWindows.Single().RestoreGroupId.ShouldBe("docs");
    }
}

namespace Shiny.Blazor.Controls.Docking;

/// <summary>
/// The layout-tree mutations behind every drag, float and re-dock. Kept free of rendering so the
/// rules can be exercised directly: the host only decides <em>what</em> the user asked for, this
/// decides what the tree looks like afterwards.
/// </summary>
static class DockLayoutOps
{
    public const double DefaultFloatWidth = 360;
    public const double DefaultFloatHeight = 260;
    public const double MinFloatWidth = 200;
    public const double MinFloatHeight = 140;

    // ----------------------------------------------------------------- queries
    public static IEnumerable<DockGroup> AllGroups(DockRoot layout)
    {
        var win = layout.MainWindow;
        foreach (var node in new[] { win.DocumentArea, win.LeftRail, win.TopRail, win.RightRail, win.BottomRail })
            foreach (var group in GroupsIn(node))
                yield return group;
        foreach (var fw in layout.FloatingWindows)
            foreach (var group in GroupsIn(fw.DocumentArea))
                yield return group;
    }

    public static IEnumerable<DockGroup> GroupsIn(DockNode? node)
    {
        switch (node)
        {
            case DockGroup g:
                yield return g;
                break;
            case DockSplit s:
                foreach (var g in GroupsIn(s.First)) yield return g;
                foreach (var g in GroupsIn(s.Second)) yield return g;
                break;
        }
    }

    public static IEnumerable<DockSplit> SplitsIn(DockNode? node)
    {
        if (node is not DockSplit s) yield break;
        yield return s;
        foreach (var c in SplitsIn(s.First)) yield return c;
        foreach (var c in SplitsIn(s.Second)) yield return c;
    }

    public static DockWindowState? FloatingWindowOf(DockRoot layout, DockGroup group)
        => layout.FloatingWindows.FirstOrDefault(w => GroupsIn(w.DocumentArea).Contains(group));

    public static DockNode? RailNode(DockWindowState win, DockArea area) => area switch
    {
        DockArea.Top => win.TopRail,
        DockArea.Right => win.RightRail,
        DockArea.Bottom => win.BottomRail,
        _ => win.LeftRail
    };

    public static void SetRailNode(DockWindowState win, DockArea area, DockNode? node)
    {
        switch (area)
        {
            case DockArea.Top: win.TopRail = node; break;
            case DockArea.Right: win.RightRail = node; break;
            case DockArea.Bottom: win.BottomRail = node; break;
            default: win.LeftRail = node; break;
        }
    }

    public static DockArea? AreaFor(DockZone zone) => zone switch
    {
        DockZone.Left => DockArea.Left,
        DockZone.Top => DockArea.Top,
        DockZone.Right => DockArea.Right,
        DockZone.Bottom => DockArea.Bottom,
        _ => null
    };

    // ----------------------------------------------------------------- rails
    public static void DockIntoRail(DockRoot layout, DockArea area, DockTab tab)
    {
        var win = layout.MainWindow;
        if (GroupsIn(RailNode(win, area)).FirstOrDefault() is { } group)
        {
            group.Tabs.Add(tab);
            group.ActiveTabIndex = group.Tabs.Count - 1;
        }
        else
        {
            SetRailNode(win, area, new DockGroup { Tabs = { tab } });
        }
    }

    /// <summary>
    /// Docks a whole subtree (a floating window's content) onto a rail. An occupied rail is split so
    /// the window keeps its own arrangement instead of being flattened into the rail's first group.
    /// </summary>
    static void DockNodeIntoRail(DockRoot layout, DockArea area, DockNode node)
    {
        var win = layout.MainWindow;
        var existing = RailNode(win, area);
        if (existing is null)
        {
            SetRailNode(win, area, node);
            return;
        }
        SetRailNode(win, area, new DockSplit
        {
            // side rails stack their panels; top/bottom rails line them up
            Orientation = area is DockArea.Left or DockArea.Right ? DockOrientation.Vertical : DockOrientation.Horizontal,
            Ratio = 0.5,
            First = existing,
            Second = node
        });
    }

    // ----------------------------------------------------------------- drops
    /// <summary>Moves one tab to where it was dropped. Returns false when the drop changes nothing.</summary>
    public static bool DropTab(DockRoot layout, string instanceId, DockGroup? targetGroup, DockZone zone, int index, DockRect tearOffBounds)
    {
        var sourceGroup = AllGroups(layout).FirstOrDefault(g => g.Tabs.Any(t => t.PanelInstanceId == instanceId));
        var tab = sourceGroup?.Tabs.First(t => t.PanelInstanceId == instanceId);
        if (sourceGroup is null || tab is null) return false;

        switch (zone)
        {
            case DockZone.TabStrip when targetGroup is not null:
            {
                var oldIndex = sourceGroup.Tabs.IndexOf(tab);
                if (ReferenceEquals(sourceGroup, targetGroup) && (index == oldIndex || index == oldIndex + 1))
                    return false;
                sourceGroup.Tabs.Remove(tab);
                if (ReferenceEquals(sourceGroup, targetGroup) && oldIndex < index)
                    index--;
                index = Math.Clamp(index, 0, targetGroup.Tabs.Count);
                targetGroup.Tabs.Insert(index, tab);
                targetGroup.ActiveTabIndex = index;
                break;
            }
            case DockZone.Center when targetGroup is not null:
            {
                if (ReferenceEquals(sourceGroup, targetGroup)) return false;
                sourceGroup.Tabs.Remove(tab);
                targetGroup.Tabs.Add(tab);
                targetGroup.ActiveTabIndex = targetGroup.Tabs.Count - 1;
                break;
            }
            // dropped on an empty well (the document area with no panels left)
            case DockZone.Center:
            {
                if (layout.MainWindow.DocumentArea is not DockEmpty) return false;
                sourceGroup.Tabs.Remove(tab);
                layout.MainWindow.DocumentArea = new DockGroup { Tabs = { tab } };
                break;
            }
            case DockZone.Left or DockZone.Right or DockZone.Top or DockZone.Bottom when targetGroup is not null:
            {
                // splitting yourself when you're the only tab is a no-op
                if (ReferenceEquals(sourceGroup, targetGroup) && sourceGroup.Tabs.Count == 1) return false;
                sourceGroup.Tabs.Remove(tab);
                ReplaceNode(layout, targetGroup, SplitAround(targetGroup, new DockGroup { Tabs = { tab } }, zone));
                break;
            }
            // dropped on an outer guide → dock into (or re-create) that rail
            case DockZone.Left or DockZone.Right or DockZone.Top or DockZone.Bottom:
            {
                sourceGroup.Tabs.Remove(tab);
                DockIntoRail(layout, AreaFor(zone)!.Value, tab);
                break;
            }
            case DockZone.TearOff:
            {
                if (FloatingWindowOf(layout, sourceGroup) is { } owner && sourceGroup.Tabs.Count == 1 && GroupsIn(owner.DocumentArea).Count() == 1)
                {
                    // dragging the only tab of a floating window just moves the window
                    owner.Bounds = ClampFloat(tearOffBounds with { Width = owner.Bounds?.Width ?? tearOffBounds.Width, Height = owner.Bounds?.Height ?? tearOffBounds.Height });
                    return true;
                }
                sourceGroup.Tabs.Remove(tab);
                layout.FloatingWindows.Add(new DockWindowState
                {
                    Bounds = ClampFloat(tearOffBounds),
                    RestoreGroupId = sourceGroup.GroupId,
                    DocumentArea = new DockGroup { Tabs = { tab } }
                });
                break;
            }
            default:
                return false;
        }

        sourceGroup.ActiveTabIndex = Math.Clamp(sourceGroup.ActiveTabIndex, 0, Math.Max(0, sourceGroup.Tabs.Count - 1));
        Simplify(layout);
        return true;
    }

    /// <summary>
    /// Docks an entire floating window where it was dropped. Edge drops keep the window's own
    /// splits; centre / tab-strip drops merge its tabs into the target group.
    /// </summary>
    public static bool DropFloatingWindow(DockRoot layout, int windowIndex, DockGroup? targetGroup, DockZone zone, int index)
    {
        if (windowIndex < 0 || windowIndex >= layout.FloatingWindows.Count) return false;
        var fw = layout.FloatingWindows[windowIndex];
        // a window can't dock into itself
        if (targetGroup is not null && GroupsIn(fw.DocumentArea).Contains(targetGroup)) return false;

        var tabs = GroupsIn(fw.DocumentArea).SelectMany(g => g.Tabs).ToList();
        if (tabs.Count == 0) return false;
        var content = fw.DocumentArea;

        switch (zone)
        {
            case DockZone.TabStrip when targetGroup is not null:
                index = Math.Clamp(index, 0, targetGroup.Tabs.Count);
                targetGroup.Tabs.InsertRange(index, tabs);
                targetGroup.ActiveTabIndex = index + tabs.Count - 1;
                break;
            case DockZone.Center when targetGroup is not null:
                targetGroup.Tabs.AddRange(tabs);
                targetGroup.ActiveTabIndex = targetGroup.Tabs.Count - 1;
                break;
            case DockZone.Center:
                if (layout.MainWindow.DocumentArea is not DockEmpty) return false;
                layout.MainWindow.DocumentArea = content;
                break;
            case DockZone.Left or DockZone.Right or DockZone.Top or DockZone.Bottom when targetGroup is not null:
                ReplaceNode(layout, targetGroup, SplitAround(targetGroup, content, zone));
                break;
            case DockZone.Left or DockZone.Right or DockZone.Top or DockZone.Bottom:
                DockNodeIntoRail(layout, AreaFor(zone)!.Value, content);
                break;
            default:
                return false;
        }

        layout.FloatingWindows.Remove(fw);
        Simplify(layout);
        return true;
    }

    /// <summary>Tears a docked tab off into its own floating window.</summary>
    public static DockWindowState? FloatTab(DockRoot layout, string instanceId, DockRect bounds)
    {
        var sourceGroup = AllGroups(layout).FirstOrDefault(g => g.Tabs.Any(t => t.PanelInstanceId == instanceId));
        if (sourceGroup is null) return null;
        // already alone in a floating window — nothing to tear off
        if (FloatingWindowOf(layout, sourceGroup) is { } owner && sourceGroup.Tabs.Count == 1 && GroupsIn(owner.DocumentArea).Count() == 1)
            return owner;

        var tab = sourceGroup.Tabs.First(t => t.PanelInstanceId == instanceId);
        sourceGroup.Tabs.Remove(tab);
        sourceGroup.ActiveTabIndex = Math.Clamp(sourceGroup.ActiveTabIndex, 0, Math.Max(0, sourceGroup.Tabs.Count - 1));
        var fw = new DockWindowState
        {
            Bounds = ClampFloat(bounds),
            RestoreGroupId = sourceGroup.GroupId,
            DocumentArea = new DockGroup { Tabs = { tab } }
        };
        layout.FloatingWindows.Add(fw);
        Simplify(layout);
        return fw;
    }

    /// <summary>
    /// Returns a floating window to the main layout: back into the group it was torn from when that
    /// group still exists, otherwise onto the left rail.
    /// </summary>
    public static List<DockTab> DockFloatingBack(DockRoot layout, int windowIndex)
    {
        if (windowIndex < 0 || windowIndex >= layout.FloatingWindows.Count) return new();
        var fw = layout.FloatingWindows[windowIndex];
        var tabs = GroupsIn(fw.DocumentArea).SelectMany(g => g.Tabs).ToList();
        layout.FloatingWindows.RemoveAt(windowIndex);

        var home = fw.RestoreGroupId is null
            ? null
            : AllGroups(layout).FirstOrDefault(g => g.GroupId == fw.RestoreGroupId);
        if (home is not null)
        {
            home.Tabs.AddRange(tabs);
            home.ActiveTabIndex = home.Tabs.Count - 1;
            home.IsCollapsed = false;
        }
        else
        {
            foreach (var tab in tabs)
                DockIntoRail(layout, DockArea.Left, tab);
        }
        Simplify(layout);
        return tabs;
    }

    public static DockRect ClampFloat(DockRect r) => new(
        Math.Max(0, r.X),
        Math.Max(0, r.Y),
        Math.Clamp(r.Width, MinFloatWidth, 2400),
        Math.Clamp(r.Height, MinFloatHeight, 1600));

    static DockSplit SplitAround(DockNode target, DockNode incoming, DockZone zone)
    {
        var split = new DockSplit
        {
            Orientation = zone is DockZone.Left or DockZone.Right ? DockOrientation.Horizontal : DockOrientation.Vertical,
            Ratio = 0.5
        };
        if (zone is DockZone.Left or DockZone.Top)
        {
            split.First = incoming;
            split.Second = target;
        }
        else
        {
            split.First = target;
            split.Second = incoming;
        }
        return split;
    }

    // ----------------------------------------------------------------- tree surgery
    public static void ReplaceNode(DockRoot layout, DockNode target, DockNode replacement)
    {
        var win = layout.MainWindow;
        win.DocumentArea = ReplaceIn(win.DocumentArea, target, replacement) ?? new DockEmpty();
        win.LeftRail = ReplaceIn(win.LeftRail, target, replacement);
        win.TopRail = ReplaceIn(win.TopRail, target, replacement);
        win.RightRail = ReplaceIn(win.RightRail, target, replacement);
        win.BottomRail = ReplaceIn(win.BottomRail, target, replacement);
        foreach (var fw in layout.FloatingWindows)
            fw.DocumentArea = ReplaceIn(fw.DocumentArea, target, replacement) ?? new DockEmpty();
    }

    static DockNode? ReplaceIn(DockNode? node, DockNode target, DockNode replacement)
    {
        if (node is null) return null;
        if (ReferenceEquals(node, target)) return replacement;
        if (node is DockSplit s)
        {
            s.First = ReplaceIn(s.First, target, replacement) ?? new DockEmpty();
            s.Second = ReplaceIn(s.Second, target, replacement) ?? new DockEmpty();
        }
        return node;
    }

    /// <summary>Prunes empty groups and single-child splits, and drops floating windows left empty.</summary>
    public static void Simplify(DockRoot layout)
    {
        var win = layout.MainWindow;
        win.DocumentArea = Simplify(win.DocumentArea) ?? new DockEmpty();
        win.LeftRail = Simplify(win.LeftRail);
        win.TopRail = Simplify(win.TopRail);
        win.RightRail = Simplify(win.RightRail);
        win.BottomRail = Simplify(win.BottomRail);
        for (var i = layout.FloatingWindows.Count - 1; i >= 0; i--)
        {
            var area = Simplify(layout.FloatingWindows[i].DocumentArea);
            if (area is null)
                layout.FloatingWindows.RemoveAt(i);
            else
                layout.FloatingWindows[i].DocumentArea = area;
        }

        // a group left alone in a document well has nothing to collapse against —
        // auto-expand so it can't get stuck as a strip-only sliver
        if (win.DocumentArea is DockGroup lone)
            lone.IsCollapsed = false;
        foreach (var fw in layout.FloatingWindows)
            if (fw.DocumentArea is DockGroup floatLone)
                floatLone.IsCollapsed = false;
    }

    static DockNode? Simplify(DockNode? node)
    {
        switch (node)
        {
            case null:
            case DockEmpty:
                return null;
            case DockGroup g:
                return g.Tabs.Count == 0 ? null : g;
            case DockSplit s:
                var first = Simplify(s.First);
                var second = Simplify(s.Second);
                if (first is null && second is null) return null;
                if (first is null) return second;
                if (second is null) return first;
                s.First = first;
                s.Second = second;
                return s;
            default:
                return node;
        }
    }
}

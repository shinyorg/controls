# Gantt

A project timeline for **both MAUI and Blazor**: a task grid on the left, a scrolling time axis on
the right, draggable bars, hierarchy with rolled-up summaries, typed dependency arrows with lag,
working-time calendars, constraints, baselines, deadlines and a critical path.

- **MAUI** — `Shiny.Maui.Controls.Gantt.GanttView` (core package, `shiny:GanttView` in XAML)
- **Blazor** — `Shiny.Blazor.Controls.Gantt.GanttView`
- **Engine** — `Shiny.Controls.Gantt.Shared` (namespace `Shiny.Controls.Gantt`), referenced by both

## The one thing to understand first

**All of the scheduling lives in the shared package, and neither control does date arithmetic.**
`GanttModel`, `GanttCalendar`, `GanttScheduler`, `GanttGeometry` and `GanttLayoutMetrics` are used
verbatim by both hosts — including the exact bar rectangles and arrow polylines. When generating
code, put model and scheduling work against `Shiny.Controls.Gantt` types, not against the view.

## Model

```csharp
using Shiny.Controls.Gantt;

var tasks = new ObservableCollection<GanttTask>
{
    new() { Id = "design", Name = "Design", Start = start, End = start.AddDays(5) },
    new() { Id = "wire", Name = "Wireframes", ParentId = "design",
            Start = start, End = start.AddDays(3), Progress = 0.8, ResourceId = "Ada" },
    new() { Id = "ship", Name = "Ship", Kind = GanttTaskKind.Milestone, Start = start.AddDays(20) }
};

var links = new ObservableCollection<GanttDependency>
{
    new("wire", "ship"),                                                  // finish-to-start
    new("a", "b", GanttDependencyType.StartToStart, TimeSpan.FromDays(3)) // with lag
};
```

Items **must be `GanttTask`** — there is no generic `TItem`. Map a domain object onto one and put the
original in `GanttTask.Item`; every event and template hands the task back.

Hierarchy works **either way and both at once**: `ParentId` on a flat list, or nesting in
`Children`. The model reassembles whichever it gets and never mutates the consumer's collections.

Kinds: `Task`, `Milestone` (diamond, zero duration, `End` ignored), `Summary` (bracket, dates rolled
up from children), `Project` (root). A task with children is promoted to `Summary` automatically.

Engine outputs written back onto the task — bindable, but do not assign them: `IsCritical`,
`TotalSlack`, `Depth`, `IsVisible`, `Parent`.

## MAUI

```xml
<shiny:GanttView Tasks="{Binding Tasks}"
                 Dependencies="{Binding Dependencies}"
                 Calendar="{Binding Calendar}"
                 SelectedTask="{Binding SelectedTask, Mode=TwoWay}"
                 ShowCriticalPath="True"
                 CascadeMode="PushOnly"
                 TimeScale="Day"
                 SnapMode="Scale"
                 BarLabel="Right"
                 TaskPaneWidth="320"
                 RowHeight="34"
                 TaskChanging="OnTaskChanging"
                 TaskChanged="OnTaskChanged">
    <shiny:GanttView.Columns>
        <shiny:GanttColumn Header="Task" Field="Name" Width="160" ShowHierarchy="True" />
        <shiny:GanttColumn Header="Owner" Field="Resource" Width="90" />
        <shiny:GanttColumn Header="%" Field="Progress" Width="55" Format="P0"
                           HorizontalAlignment="End" />
    </shiny:GanttView.Columns>
</shiny:GanttView>
```

`GanttCalendar` is not a MAUI type, so XAML needs the shared assembly:

```xml
xmlns:gantt="clr-namespace:Shiny.Controls.Gantt;assembly=Shiny.Controls.Gantt.Shared"
...
Calendar="{x:Static gantt:GanttCalendar.StandardDays}"
```

Methods: `ZoomIn()`, `ZoomOut()`, `ZoomToFit()`, `SetZoom(ppd, anchorX)`, `ScrollToDate(date)`,
`ScrollToTask(task)`, `ExpandAll()`, `CollapseAll()`, `ToggleExpand(task)`, `SelectTask(task)`,
`RebuildModel()`, `RemoveDependency(dep)`.

Events: `TaskChanging` (cancellable), `TaskChanged`, `TaskTapped`, `TaskDoubleTapped`,
`DependencyCreated` (cancellable), `DependencyRemoved`, `SelectionChanged`, `ScaleChanged`,
`PlanBuilt`.

## Blazor

```razor
@using Shiny.Blazor.Controls.Gantt
@using Shiny.Controls.Gantt

<div style="height:460px">
    <GanttView @ref="gantt"
               Tasks="tasks"
               Dependencies="links"
               Calendar="GanttCalendar.StandardDays"
               ShowCriticalPath="true"
               CascadeMode="GanttCascadeMode.PushOnly"
               TimeScale="GanttTimeScale.Day"
               BarLabel="GanttBarLabel.Right"
               TaskPaneWidth="320"
               Columns="columns"
               @bind-SelectedTask="selected"
               OnTaskChanging="OnTaskChanging"
               OnTaskChanged="OnTaskChanged" />
</div>

@code {
    GanttView? gantt;

    readonly List<GanttColumnDefinition> columns =
    [
        new() { Header = "Task", Field = GanttFields.Name, Width = 170, ShowHierarchy = true },
        new() { Header = "Owner", Field = GanttFields.Resource, Width = 90 },
        new() { Header = "%", Field = GanttFields.Progress, Width = 55, Format = "P0", Align = "right" }
    ];
}
```

**The component needs a bounded height** — it is `height:100%` and will collapse in an unsized parent.

Methods are the same set, async where they touch the DOM: `await gantt.ZoomIn()`, `ZoomToFit()`,
`ScrollToDate()`, `ScrollToTask()`, plus synchronous `ExpandAll()`, `CollapseAll()`,
`ToggleExpand()`, `SelectTask()`, `Rebuild()`.

Callbacks: `OnTaskChanging`, `OnTaskChanged`, `OnTaskClick`, `OnTaskDoubleClick`,
`OnDependencyCreated`, `OnDependencyRemoved`, `OnSelectionChanged`, `OnPlanBuilt`.

## Editing and undo

Every gesture builds a `GanttSchedulePlan` **before anything moves**, listing the edited task first
and every cascaded task after it. Cancel it, or keep it for undo:

```csharp
readonly Stack<GanttSchedulePlan> undo = new();

void OnTaskChanging(GanttTaskChangingArgs e)      // MAUI: GanttTaskChangingEventArgs
{
    if (e.Plan.Changes.Any(c => c.NewEnd > freeze))
        e.Cancel = true;
}

void OnTaskChanged(GanttTaskChangedArgs e) => undo.Push(e.Plan);
void Undo() => undo.Pop().Revert();               // reverts the cascade too
```

`CascadeMode`: `PushOnly` (default — successors move later, never earlier), `Strict` (re-derived both
ways), `None` (nothing else moves; broken links are reported in `Plan.Issues`).

`AllowDependencyEdit` (**off by default**) puts connector dots on the selected bar; drag one onto
another bar and the link type falls out of which dots were used. A link that would close a cycle is
refused.

Dragging empty timeline space pans the chart (`AllowPan`, default true, MAUI). Dragging a bar moves
the bar; the press decides which before anything moves.

Per-gesture switches: `AllowMove`, `AllowResize`, `AllowProgressChange`, `AllowZoom`, `AllowPan`,
`IsReadOnly` (outranks all of them), plus `CanMove`/`CanResize`/`CanChangeProgress` per task.

## Calendars

`GanttCalendar.Continuous` (default, 24/7, short-circuits everything), `.StandardDays` (Mon-Fri, full
days), `.StandardHours` (Mon-Fri 09:00-17:00), or a constructed one with working days, `GanttShift`s,
holidays and per-date exceptions (an empty exception turns a weekday off; a populated one turns a
Saturday on).

With a calendar, moves preserve **working** duration, drops onto a weekend land on the Monday, and
lag, slack and durations are all measured in working time.

## Columns

Fields are a fixed vocabulary plus a custom bag, shared by both hosts via `GanttFields`:
`Name`, `Start`, `End`, `Duration`, `Progress`, `Resource`, `Slack`, `Deadline`, or any key in
`GanttTask.Fields`. Durations render as `3d` / `4h` / `-` for zero, never as `3.00:00:00`.

Exactly one column should set `ShowHierarchy`; if none does, the first gets it.

## Gotchas

- **`GanttColumn.GetFieldValue(task)`, not `GetValue`** — `BindableObject` already owns `GetValue`.
- **`Milestone` ignores `End`.** Set `Start` only; `Duration` is zero and the extent uses `Start`.
- **Summary bars cannot be resized** by default (`AllowSummaryResize`) — their dates are derived, so
  the resize would be undone by the next rebuild. Dragging one *moves the whole subtree*.
- **`ShowCriticalPath` is off by default**, and computing it is what fills `TotalSlack`. Slack can be
  negative when the plan already violates its own links; that is the honest answer, not a bug.
- **A dependency onto a task inside a collapsed summary draws nothing** — `RouteOf` returns empty
  rather than routing to a row that is not there.
- **Validation never throws.** Dangling links, duplicate ids, cycles, missed deadlines and clamped
  constraints all land in `GanttModel.Issues`; read it from `PlanBuilt` / `OnPlanBuilt`.
- **Blazor: give the component a height.** It fills its parent and collapses without one.
- **Blazor: pointer coordinates come from `clientX/clientY` minus the canvas origin, never
  `offsetX`** — `offsetX` is relative to the event *target*, and a press on a bar targets the bar.
- **Both hosts: `BuildDragPlan` takes its task and target as arguments** because the commit path
  clears the drag fields first. Reading them instead produces a drag that tracks the pointer and then
  snaps back.
- **MAUI panning is hand-driven, not native.** A `PanGestureRecognizer` on a `ScrollView` child
  consumes the gesture, so the scroller only ever saw fast flicks. `PanChart` locks the native
  scroller and scrolls programmatically; do not "simplify" it back to relying on the ScrollView.
- **MAUI header clipping needs an explicit `Clip`.** `IsClippedToBounds` did not hold the translated
  header GraphicsView in, and it painted over the task pane once scrolled.
- **Bar colours**: `GanttTask.Color` is a plain string — a MAUI colour name/hex, or any CSS colour.
  It wins over the palette but not over the critical-path highlight.

# Gantt

`GanttView` is a project timeline: a task grid on the left, a scrolling time axis on the right, and
bars you can drag. It covers the things that make a Gantt a Gantt rather than a bar chart of dates —
hierarchy with rolled-up summaries, typed dependency arrows with lag, working-time calendars,
constraints, baselines, deadlines, and a critical path — and it does all of it on **both hosts from
one engine**.

- **MAUI** — `Shiny.Maui.Controls.Gantt.GanttView` (in `Shiny.Maui.Controls`)
- **Blazor** — `Shiny.Blazor.Controls.Gantt.GanttView` (in `Shiny.Blazor.Controls`)
- **Engine** — `Shiny.Controls.Gantt.Shared`, referenced by both and published on its own

## Why the engine is a separate package

A Gantt is mostly arithmetic. Working out that a three-working-day task dragged onto a Friday now
finishes on Wednesday, that pushing it drags four successors with it, that one of those has a
must-start-on constraint holding it, and that the whole chain is now on the critical path — none of
that wants to know whether it is drawing into a MAUI layout or a CSS grid.

So it does not. `Shiny.Controls.Gantt.Shared` owns the model, the calendars, the scheduler, the
critical path, and the time-to-pixel geometry — including the exact rectangle every bar occupies and
the polyline every arrow follows. Both controls are thin: they turn gestures into a
`GanttSchedulePlan` and paint what the engine tells them to. A plan that schedules differently on the
two hosts is a bug neither host's own tests would ever notice, so the arrangement removes the
possibility rather than testing for it.

The package is dependency-free, trimmable and AOT-clean, and is perfectly usable on its own — for a
scheduling API, a background job, or a test — with no UI at all.

## The model

```csharp
using Shiny.Controls.Gantt;

var tasks = new ObservableCollection<GanttTask>
{
    new() { Id = "design", Name = "Design", Start = start, End = start.AddDays(5) },
    new() { Id = "wire",   Name = "Wireframes", ParentId = "design",
            Start = start, End = start.AddDays(3), Progress = 0.8 },
    new() { Id = "ship",   Name = "Ship", Kind = GanttTaskKind.Milestone, Start = start.AddDays(20) }
};

var links = new ObservableCollection<GanttDependency>
{
    new("wire", "ship")
};
```

Map your own type onto `GanttTask` and hang the original off `GanttTask.Item`; every event and
template hands the task back, so you can get to it.

### Hierarchy, either way round

The tree can be expressed either way, and the two can be mixed in one source:

- **Flat** — every task carries a `ParentId`, the way rows arrive from a database.
- **Nested** — tasks are added to a parent's `Children`, the way a view model is usually shaped.

`GanttModel` reassembles whichever it is given. It never reshapes your collections: the resolved
relationships live in the model's own indexes, because both hosts bind to the very collections being
read and re-parenting an `ObservableCollection` mid-build would raise change notifications into a
layout pass that is already running.

A task with children becomes a `Summary` automatically and its dates and progress are rolled up from
them — progress weighted by duration, so a two-hour task at 100% cannot drag a six-week task at 0% up
to "half done". Set `RollUpSummaries="False"` when parent rows carry meaningful dates of their own.

### Task properties worth knowing

| Property | |
|---|---|
| `Kind` | `Task`, `Milestone` (a diamond, zero duration), `Summary` (a bracket), `Project` (the root) |
| `Progress` | 0 to 1; drawn as the filled portion of the bar |
| `BaselineStart` / `BaselineEnd` | The originally planned dates, drawn as a thin bar *below* the live one |
| `Deadline` | Drawn as a marker; missing it is reported, never blocked |
| `Constraint` + `ConstraintDate` | `MustStartOn`, `StartNoEarlierThan`, `FinishNoLaterThan`, … |
| `ManuallyScheduled` | Links still draw and still report violations, but nothing drags this task |
| `CanMove` / `CanResize` / `CanChangeProgress` | Per-task gesture permissions |
| `IsCritical` / `TotalSlack` / `Depth` | Engine outputs, written back onto the task so templates can bind to them |
| `Fields` | A bag for custom task-pane columns the control was never compiled against |

## Dependencies

All four standard types, with lag:

```csharp
new GanttDependency("a", "b")                                          // finish-to-start
new GanttDependency("a", "b", GanttDependencyType.StartToStart,
                    TimeSpan.FromDays(3))                              // b starts 3d after a starts
new GanttDependency("a", "b", GanttDependencyType.FinishToStart,
                    TimeSpan.FromDays(-2))                             // negative lag is lead time
```

`FinishToFinish` and `StartToFinish` work too. Negative lag is legal — "start two days before the
predecessor finishes" is an ordinary plan, not an error.

Arrows are routed as elbows. When the successor is far enough past the predecessor the arrow makes a
single vertical jog; when it is behind or beside it, going straight would draw the line back through
the predecessor's own bar, so it detours through the gutter between the rows instead.

## Editing

Drag the body of a bar to move it, its edges to resize, or the progress handle to change completion.
By default successors are **pushed** later when a link would otherwise break, and never pulled
earlier — `CascadeMode="Strict"` re-derives them in both directions, `None` moves nothing else and
just reports the broken links.

Every gesture produces a `GanttSchedulePlan` before anything moves:

```csharp
void OnTaskChanging(object sender, GanttTaskChangingEventArgs e)
{
    // The whole consequence is in hand, cascade included, and nothing has changed yet.
    if (e.Plan.Changes.Any(c => c.NewEnd > this.freezeDate))
        e.Cancel = true;
}
```

That separation is what gives you undo for nothing — a plan reverts itself, cascade and all:

```csharp
readonly Stack<GanttSchedulePlan> undo = new();

void OnTaskChanged(object sender, GanttTaskChangedEventArgs e) => this.undo.Push(e.Plan);
void Undo() => this.undo.Pop().Revert();
```

### Drawing links

Set `AllowDependencyEdit="True"` and the selected bar grows a connector dot at each end. Drag from
one to another bar and the link type falls out of the shape you drew: from the finish dot onto a
start is finish-to-start, from a start dot onto a start is start-to-start, and so on. A link that
would close a cycle is refused outright — a cyclic graph has no schedule to compute.

It is **off by default**: on a phone the dots compete with the resize grips for the same few pixels.

## Working calendars

```xml
<shiny:GanttView Calendar="{x:Static gantt:GanttCalendar.StandardDays}" />
```

`GanttCalendar` carries working days, shifts within a day, holidays, and per-date exceptions (an
empty exception turns a weekday off; a populated one turns a Saturday on). With a calendar set:

- weekends and holidays are shaded, and excluded from the header's own tick shading;
- a bar dropped on a Saturday lands on the Monday, because a zero-length advance still normalizes
  onto a working boundary;
- a task keeps its **working** duration across a move, so three working days stays three working
  days and simply gets wider;
- durations, lag and slack are all measured in working time.

`GanttCalendar.Continuous` — the default — is 24/7 and short-circuits every method to plain
arithmetic, so an app that never mentions working time pays nothing for the machinery.

## Critical path

`ShowCriticalPath="True"` computes total slack for every task and highlights the ones with none,
along with the links between them. A link is only drawn as critical when **both** ends are — a red
arrow across slack that genuinely exists is worse than no highlight at all.

Slack is exposed as `GanttTask.TotalSlack` and can be shown as a task-pane column
(`Field="Slack"`). It can go negative, which is the honest answer when the plan already violates its
own dependencies. `CriticalSlackThreshold` widens what counts as critical, which a plan built from
whole days usually wants.

It is off by default: computing it is not free, and most plans are read before they are analysed.

## The task pane

Columns bind to a small vocabulary of named fields — `Name`, `Start`, `End`, `Duration`, `Progress`,
`Resource`, `Slack`, `Deadline` — or to any key in `GanttTask.Fields`. Durations render as `3d` or
`4h`, never as `3.00:00:00`, and both hosts share that formatting so the two galleries agree to the
character.

```xml
<shiny:GanttView.Columns>
    <shiny:GanttColumn Header="Task" Field="Name" Width="160" ShowHierarchy="True" />
    <shiny:GanttColumn Header="Owner" Field="Resource" Width="90" />
    <shiny:GanttColumn Header="Slack" Field="Slack" Width="70" HorizontalAlignment="End" />
</shiny:GanttView.Columns>
```

```razor
<GanttView Columns="@(new List<GanttColumnDefinition>
{
    new() { Header = "Task", Field = GanttFields.Name, Width = 160, ShowHierarchy = true },
    new() { Header = "Owner", Field = GanttFields.Resource, Width = 90 }
})" />
```

Exactly one column carries the indent and the expander chevron. If none claims it the first one gets
it — a tree with no expanders is unusable, and silently so.

Leave `Columns` empty and a Task/Start/Finish set is used. The splitter between the panes is
draggable unless `IsTaskPaneResizable` says otherwise, and `ShowTaskPane="False"` gives a bare
timeline.

## The time axis

`TimeScale` picks the header granularity — `Minute` through `Year`, or `Auto` to follow the zoom.
The header always draws **two** tiers, because a single row of day numbers tells you nothing about
which month you are looking at.

Dragging empty timeline space pans the chart in both directions; `AllowPan="False"` turns that off
when something outside the control wants the gesture. Dragging a *bar* still moves the bar — the two
never compete, because the press decides which it is before any movement happens.

`PixelsPerDay` is the zoom. Leave it unset and it is derived from the scale. Both hosts offer
`ZoomIn()`, `ZoomOut()`, `ZoomToFit()`, `ScrollToDate()` and `ScrollToTask()` — the last of which
expands whatever the task is hidden inside first, since scrolling to a row a collapsed parent is
hiding would silently do nothing.

Zoom is anchored: pinching on MAUI, or ctrl/⌘ + wheel on Blazor, keeps the instant under the pointer
where it was.

`HighlightRanges` shades arbitrary bands behind the bars — a sprint, a freeze window, a release
train.

## Templates

Both hosts draw bars themselves by default: one canvas on MAUI, absolutely positioned elements on
Blazor. That is what gives milestones their diamond and summaries their turned-down bracket, and it
is dramatically cheaper than a view per row for a plan of any size.

When a bar needs something a drawing cannot give you — an avatar, a nested control — set `BarTemplate`
(a `DataTemplate` on MAUI, a `RenderFragment<GanttTask>` on Blazor) and a view is realized per visible
row instead, positioned at the rectangle the engine computed.

## Platform notes

- Everything above works identically on both hosts. There is no MAUI-only or Blazor-only feature.
- On MAUI the timeline is one `ScrollView` with the header and task pane translated to follow it,
  rather than nested scrollers — the same arrangement `DataGrid`'s frozen columns use, and the only
  one that behaves the same on all six platforms.
- Also on MAUI, panning is driven by the control rather than by the native scroller. It has to be: the
  `PanGestureRecognizer` that bar dragging needs consumes every drag, so leaving the scroller to it
  gave a chart that scrolled on a fast flick and ignored an ordinary slow pan. Driving it by hand
  makes the behaviour identical everywhere; the cost is that a hand-driven pan carries no momentum.
  On Blazor the browser keeps the gesture — only the bars set `touch-action: none` — so panning there
  does have momentum.
- On Blazor the scroll synchronisation runs entirely in `gantt.js` and never crosses into .NET;
  routing it through interop puts a render pass between the scroll event and the transform, which
  reads as the header lagging the bars on every flick.
- Bar colours follow the theme tokens on both hosts. Set `GanttTask.Color` for a per-task colour (a
  MAUI colour string or any CSS colour) — it wins over the palette but not over the critical
  highlight, which is a statement about the plan rather than about the task.

## Validation

A plan that fails validation still renders. `GanttModel.Issues` reports dangling dependencies,
duplicate ids, dependency and hierarchy cycles, missed deadlines and clamped constraints — read it
from the `PlanBuilt` event (MAUI) or `OnPlanBuilt` (Blazor), because nothing else tells you the plan
was wrong. Real project data routinely contains all of these, and a control that threw on one would
be unusable against it.

> **TODO:** capture screenshots for `gantt` (MAUI via mauidevflow, Blazor via Playwright) and add
> them under `public/images/gantt/` in the docs repo.

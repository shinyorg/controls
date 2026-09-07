# Diagram

An interactive diagram for **both MAUI and Blazor**: a bindable graph of shapes and connections, five
auto-layouts, connectors that anchor to real shape outlines, pan/zoom, marquee selection, node
dragging, connection authoring and undo. Org charts, decision trees, flowcharts, mindmaps.

- **MAUI** — `Shiny.Maui.Controls.Diagram.DiagramView` (add-on package `Shiny.Maui.Controls.Diagram`,
  `shiny:DiagramView` in XAML)
- **Blazor** — `Shiny.Blazor.Controls.Diagram.DiagramView` (add-on package
  `Shiny.Blazor.Controls.Diagram`)
- **Engine** — `Shiny.Controls.Diagram.Shared` (namespace `Shiny.Controls.Diagramming`), referenced by
  both

**Not the same control as [Mermaid Diagrams](mermaid-diagrams.md).** That one renders a picture from
mermaid *text*. This one is a graph you bind to and edit. If the user's source of truth is markup, use
Mermaid; if it is a collection of nodes, use this.

## The one thing to understand first

**All of the layout, routing and geometry lives in the shared package, and neither control computes
any of it.** `DiagramModel`, the five layouts, `ConnectionRouter`, `ShapeGeometry` and
`DiagramHitTester` are used verbatim by both hosts. Put model work against `Shiny.Controls.Diagramming`
types, not against the view.

## Model

```csharp
using Shiny.Controls.Diagramming;

var nodes = new ObservableCollection<DiagramNode>
{
    new("start", "Ticket raised") { Shape = DiagramNodeShape.Stadium },
    new("paid",  "Paid plan?")    { Shape = DiagramNodeShape.Diamond },
    new("queue", "Support queue"),
    new("forum", "Community forum")
};

var connections = new ObservableCollection<DiagramConnection>
{
    new("start", "paid"),
    new("paid", "queue", "Yes"),   // third argument is the label on the line
    new("paid", "forum", "No")
};
```

Items **must be `DiagramNode` / `DiagramConnection`** — there is no generic `TItem`. Put the domain
object in `DiagramNode.Item`; every event and template hands the node back.

Hierarchy works **either way and both at once**: `ParentId` on a flat list, or nesting in `Children`.

**A hierarchy with no connections still draws its links** — the engine synthesises one per
parent/child pair. So an org chart is just nested nodes and an empty connection list. Declaring a
connection between the same two nodes replaces the implicit one (that is how you label it). Off via
`DiagramModel.ShowHierarchyConnections = false`.

Engine outputs written back onto the model — bindable, do not assign: `X`, `Y`, `Width`, `Height`,
`Depth`, `IsHidden`, `Bounds`, `Points`, `LabelPosition`, `IsReversed`, `IsImplicit`.

Shapes: `Rectangle` (default), `RoundedRectangle`, `Stadium`, `Circle`, `Ellipse`, `Diamond`,
`Parallelogram`, `Hexagon`, `Cylinder`, `Document`, `Triangle`.

## MAUI

```xml
<shiny:DiagramView x:Name="Diagram"
                   Nodes="{Binding Nodes}"
                   Connections="{Binding Connections}"
                   LayoutKind="Tree"
                   Direction="TopToBottom"
                   Router="Orthogonal"
                   AllowNodeDrag="True"
                   AllowConnectionEdit="True"
                   ShowGrid="True"
                   SelectedNode="{Binding SelectedNode, Mode=TwoWay}"
                   SelectionChanged="OnSelectionChanged"
                   Editing="OnEditing"
                   Edited="OnEdited"
                   Built="OnBuilt" />
```

Methods: `ZoomToFit()`, `ScrollTo(node)`, `Undo()`, `Redo()`, `DeleteSelection()`, `Select(...)`,
`Rebuild()`, `Repaint()`. Properties: `CanUndo`, `CanRedo`, `Selection`, `Model`.

## Blazor

```razor
@using Shiny.Blazor.Controls.Diagram
@using Shiny.Controls.Diagramming

<div style="height:520px">
    <DiagramView @ref="diagram"
                 Nodes="nodes"
                 Connections="connections"
                 LayoutKind="DiagramLayoutKind.Tree"
                 Direction="DiagramDirection.TopToBottom"
                 Router="DiagramConnectionRouter.Orthogonal"
                 AllowNodeDrag="true"
                 AllowConnectionEdit="true"
                 AllowDelete="true"
                 ShowGrid="true"
                 OnSelectionChanged="OnSelectionChanged"
                 OnEditing="OnEditing"
                 OnEdited="OnEdited"
                 OnBuilt="OnBuilt" />
</div>
```

Methods: `ZoomToFit()`, `ScrollTo(node)`, `Undo()`, `Redo()`, `DeleteSelectionAsync()`,
`Select(...)`, `Rebuild()`.

## Layouts

`LayoutKind`: `Tree` (default), `Layered`, `MindMap`, `Radial`, `ForceDirected`, `None`.

- **`Tree`** — org charts and decision trees. Arranges a *hierarchy*, so a graph whose branches rejoin
  loses an edge.
- **`Layered`** — use this the moment the graph rejoins or loops back. Keeps every edge.
- **`None`** — every node keeps its X/Y. What a hand-placed or JSON-loaded diagram wants.

`Direction` turns the hierarchy layouts. `TreeStyle = TipOver` stacks and indents children instead of
spreading them — much narrower, much taller, good on a phone.

All five are deterministic, force-directed included.

## Gotchas

- **It is `LayoutKind`, never `Layout`** — on both hosts. A property named `Layout` on the MAUI
  control hides `VisualElement.Layout(Rect)`; the Blazor name matches for parity.
- **Editing is off by default.** `AllowNodeDrag`, `AllowConnectionEdit`, `AllowMultiSelect` and
  `AllowDelete` are all `false` unless set. `AllowPan`, `AllowZoom` and `AllowSelection` are on.
- **`AllowDelete` is Blazor only.** MAUI has no reliable cross-platform key event and a phone has no
  Delete key — call `DeleteSelection()` from a button instead.
- **Dragging a node sets `IsPinned`**, which holds it through a re-layout. Emit `IsPinned = true` on
  any node placed by hand.
- **On Blazor the component fills its parent and needs a bounded height.** Same as `GanttView`.
- **Validation reports, it never throws.** A dangling connection, a duplicate id or a parent cycle
  still draws; read `DiagramModel.Issues` from `Built` / `OnBuilt` or nothing will tell you.
- **On MAUI, pan and marquee are the same background gesture** and `AllowPan` wins when both are on.
- Never emit a `NodeTemplate` for a large diagram without saying why: unset, every node is one drawn
  path; set, every node is a real view.

## Editing and undo

Every gesture builds a `DiagramEditPlan` and raises it *before* applying it, listing the whole
consequence — deleting one node also lists the connections going with it:

```csharp
void OnEditing(object? sender, DiagramEditingEventArgs e)
{
    if (e.Plan.AffectedNodes.Any(n => n.Id == "root"))
        e.Cancel = true;
}
```

`plan.Revert()` puts everything back, cascade included, so undo is a `Stack<DiagramEditPlan>`. The
controls keep that stack themselves — use `Undo()`/`Redo()` and only build your own if you need to
merge diagram edits into a wider undo history. `Redo` replays moves only.

## Saving

```csharp
var json = DiagramJson.Save(nodes, connections);
var (loadedNodes, loadedConnections) = DiagramJson.Load(json);
```

Round-trips the graph, shapes and hand-placed positions. Routes and depths are derived, not saved.
Source-generated, so it survives a trimmed WASM publish. Load into `LayoutKind = None` to keep the
saved positions.

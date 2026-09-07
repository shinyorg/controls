# Diagram

`DiagramView` draws a graph of **shapes** and **connections**: an org chart, a decision tree, a
flowchart, a mindmap. It lays the graph out for you, routes the lines so they meet the real outline of
each shape, and gives you a surface to pan, zoom, select on, drag nodes around and author connections
in.

It ships as an add-on package per host:

```bash
dotnet add package Shiny.Maui.Controls.Diagram
dotnet add package Shiny.Blazor.Controls.Diagram
```

Both depend on **`Shiny.Controls.Diagram.Shared`**, which you can also use on its own.

## Why the engine is a separate package

A diagram is mostly arithmetic. Packing a tidy tree so that a parent sits centred over its children
and no two subtrees collide, assigning layers to a graph that loops back on itself, working out where
a line leaving a diamond actually crosses its edge — none of that wants to know whether it is drawing
into a MAUI canvas or an SVG `viewBox`.

So it doesn't. `Shiny.Controls.Diagram.Shared` owns the model, all five layouts, the connector
routing and the shape geometry — down to the exact box every node occupies and the exact polyline
every connection follows. Both controls are thin: they turn gestures into a `DiagramEditPlan` and
paint what the engine tells them to. A layout that resolved differently on the two hosts would be a
bug neither host's own tests would notice, and this removes the possibility rather than testing for
it.

The package is dependency-free, trimmable and AOT-clean, and is perfectly usable with no UI at all.

## The model

Two collections. Nodes are `DiagramNode`, connections are `DiagramConnection` — concrete types rather
than a generic `TItem`, because the engine writes layout results back onto them. Put your own object
in `DiagramNode.Item` and bind a template to it.

```csharp
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
    new("paid", "queue", "Yes"),
    new("paid", "forum", "No")
};
```

Both collections are watched. An `ObservableCollection` redraws on add and remove, and every node and
connection is watched for property changes, so editing the source redraws without calling anything.

### Hierarchy, either way round

A hierarchy can be expressed as nesting or as a flat list with parent ids, and the two can be mixed:

```csharp
// Nested — the parent link maintains itself.
var ceo = new DiagramNode("ceo", "CEO");
ceo.Children.Add(new DiagramNode("eng", "VP Engineering"));

// Flat — DiagramModel reassembles the tree.
var rows = new[]
{
    new DiagramNode("ceo", "CEO"),
    new DiagramNode("eng", "VP Engineering") { ParentId = "ceo" }
};
```

Neither shape is "the" model: charts arrive from a database as rows and from a view model as a tree,
and forcing a conversion on the consumer is how a control ends up with two half-supported paths.

**A hierarchy with no connections of its own still draws its links.** An org chart handed over as
nested nodes has parents and children and no edges; without this it would draw as a tidy grid of
boxes joined by nothing. Declaring a connection between the same two nodes replaces the implicit one,
which is how you add a label or a different arrowhead. Turn it off with
`ShowHierarchyConnections = false` on the model.

## Shapes

`DiagramNodeShape` is the flowchart vocabulary, not a general shape library — each one means something
to a reader:

`Rectangle` (a process step, and the default) · `RoundedRectangle` · `Stadium` (start/end) · `Circle`
· `Ellipse` · `Diamond` (a decision) · `Parallelogram` (input/output) · `Hexagon` (preparation) ·
`Cylinder` (a store) · `Document` · `Triangle`

Anchoring and hit testing both work against the **real outline**, not the bounding box. That is the
difference between a decision tree that looks drawn and one that looks approximated: with a box
anchor, every arrow into a diamond stops short in mid-air at the corner of an invisible rectangle, and
a click in that same empty corner selects the node.

## Layouts

`LayoutKind` picks one. It is called `LayoutKind` rather than `Layout` on both hosts because on MAUI a
property named `Layout` hides `VisualElement.Layout(Rect)`.

| Layout | What it is for |
|---|---|
| `Tree` | Tidy hierarchy — the org chart and decision tree layout. The default. |
| `Layered` | Directed graph (Sugiyama) — a flowchart that rejoins or loops back. |
| `MindMap` | Root in the middle, branches fanned to both sides. |
| `Radial` | Root in the middle, each level on a ring around it. |
| `ForceDirected` | Physics relaxation, for a graph with no hierarchy worth speaking of. |
| `None` | Every node keeps the X/Y it was given. What a hand-placed or JSON-loaded diagram wants. |

`Direction` (`TopToBottom`, `BottomToTop`, `LeftToRight`, `RightToLeft`) turns the hierarchy layouts.
`TreeStyle = TipOver` stacks children and indents them instead of spreading them — a manager with
twelve reports is twelve node-widths across normally and one node-width across in tip-over, which is
the difference between a deep chart fitting on a phone and not.

**Tree throws edges away; Layered does not.** The tree layout arranges a hierarchy, so a graph whose
branches rejoin — three outcomes that all land on "Resolved" — has to lose an edge to become a tree.
If your graph rejoins or loops, use `Layered`.

Every layout is **deterministic**, including the force-directed one, which seeds from a fixed circle
rather than at random. The same graph draws identically on both hosts and on every rebuild.

## Connectors

`Router` picks the default; a connection can override it with its own `Router`.

- `Orthogonal` — right-angled elbows, the org-chart look. The default.
- `Straight` — a direct line.
- `Bezier` — a curve, with control points pulled along the port normals.

Ports (`SourcePort`, `TargetPort`) name which side a line leaves and enters. `Auto` — the default —
lets the layout decide: under a hierarchy layout the ports snap to the layout's own axis, so a
top-down chart's arrows arrive in the tops of its boxes rather than in their sides; under `Radial`,
`ForceDirected` and `None` there is no axis to prefer, so the nearest edge wins.

Caps are `StartCap`/`EndCap` (`None`, `Arrow`, `FilledArrow`, `Circle`, `Diamond`) and the line style
is `StrokeStyle` (`Solid`, `Dashed`, `Dotted`). A connection's `Text` is drawn at the **arc-length**
midpoint of its route, not the middle vertex — which is where a reader looks for it on a route with
one short stub and one long run.

An edge spanning several layers is threaded around the nodes in between rather than drawn straight
through them.

## Editing

Everything below is **off by default**; a diagram is read-only until you say otherwise.

| Property | What it enables |
|---|---|
| `AllowPan` | Dragging the background moves the viewport. On by default. |
| `AllowZoom` | Pinch (MAUI) or Ctrl/Cmd + wheel (Blazor). On by default. |
| `AllowSelection` | Tapping selects. On by default. |
| `AllowMultiSelect` | Marquee selection. |
| `AllowNodeDrag` | Nodes can be dragged. |
| `AllowConnectionEdit` | A selected node shows connector handles; drag one onto another node. |
| `AllowDelete` | Blazor only — Delete and Backspace remove the selection. |

Per-node and per-connection overrides — `DiagramNode.CanMove`, `CanConnect`,
`DiagramConnection.CanEdit` — narrow that further.

**Dragging a node pins it.** `IsPinned` holds a node at its position through a re-layout; an
auto-layout that snapped a hand-placed node back the next time anything changed would make dragging
useless. The space it occupies is still reserved, so its neighbours do not close over the gap.

### Every edit is a plan

A gesture builds a `DiagramEditPlan` describing everything it is about to do — deleting one node also
lists the connections that go with it — and raises it for cancellation before anything is applied:

```csharp
// MAUI
diagram.Editing += (s, e) =>
{
    if (e.Plan.AffectedNodes.Any(n => n.Id == "root"))
        e.Cancel = true;
};
```

```razor
@* Blazor *@
<DiagramView OnEditing="@(e => e.Cancel = e.Plan.AffectedNodes.Any(n => n.Id == "root"))" />
```

Because the plan recorded the old values rather than trying to recompute them, `Revert()` puts
everything back including the cascade — which is why undo is a `Stack<DiagramEditPlan>` and nothing
else. `Undo()`, `Redo()`, `CanUndo` and `CanRedo` are built in.

`Redo` replays moves only. An add or a remove was applied by the gesture as it recorded it, and
re-applying one would need the plan to know how to repeat a mutation rather than only how to invert
it.

## Templates

Unset, every node is drawn — one path, not one view, which is what lets a few hundred nodes pan
smoothly. Set `NodeTemplate` and each node becomes a real view (MAUI) or DOM element (Blazor) bound to
its `DiagramNode`, so it can hold a button, an image or an input. The cost is one element per node, so
it is off by default.

```xml
<shiny:DiagramView Nodes="{Binding Nodes}">
    <shiny:DiagramView.NodeTemplate>
        <DataTemplate x:DataType="dg:DiagramNode">
            <Border><Label Text="{Binding Text}" /></Border>
        </DataTemplate>
    </shiny:DiagramView.NodeTemplate>
</shiny:DiagramView>
```

## Saving

`DiagramJson.Save` / `DiagramJson.Load` round-trip the graph, the shapes and hand-placed positions
through a flat pair of lists. Routes, depths and selection are not saved — they are derived on the
next rebuild, so saving them would only create the possibility of a file disagreeing with itself.
Serialization is source-generated, so it survives a trimmed WASM publish.

```csharp
var json = DiagramJson.Save(nodes, connections);
var (loadedNodes, loadedConnections) = DiagramJson.Load(json);
```

Load into `LayoutKind = None` to keep the saved positions.

## Validation

Validation **reports, it does not throw**. Real data routinely contains a dangling connection, a
duplicate id or a parent chain that loops; a control that threw on one would be unusable against it,
and one that silently dropped it would be worse. The diagram still draws, and `DiagramModel.Issues`
says what was wrong — read it from the `Built` event (MAUI) or `OnBuilt` (Blazor), because nothing
else will tell you.

Issues cover `DuplicateId`, `DanglingConnection`, `ParentCycle`, `SelfConnection` and
`ConnectionCycle`.

## Platform notes

- **Blazor** — pan and zoom are the SVG `viewBox` rather than a transform on the contents, so zooming
  costs nothing at any node count. The component fills its parent and needs a **bounded height**.
- **MAUI** — the whole diagram is painted into one `GraphicsView`. Marquee selection and panning are
  the same gesture on the background, so `AllowPan` wins when both are on: a finger has no shift key.
  Delete is a `DeleteSelection()` method rather than a key handler, because a phone has no Delete key.

TODO: capture screenshots for diagram.

## See also

- [Mermaid Diagrams](mermaid-diagrams.md) — renders a diagram from mermaid *text*. Use that when the
  source of truth is markup; use this when it is a graph you bind to and edit.
- [Styling & theming](styling.md)

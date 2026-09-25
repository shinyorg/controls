# YogaLayout

[← All Shiny Controls](../../README.md)

> **Credit:** this is the layout model of **[Yoga](https://www.yogalayout.dev/)**, Meta's open-source
> (MIT) cross-platform flexbox engine, which powers React Native
> ([github.com/facebook/yoga](https://github.com/facebook/yoga)). Yoga's design, defaults and property
> names are the Yoga team's work, and its documentation at yogalayout.dev is the reference for how every
> property behaves. `YogaLayout` is an independent C# implementation of that model. It does not ship or
> bind Yoga's native library.

A flexbox layout that follows Yoga on **both hosts**. On .NET MAUI it is a pure C# layout engine with
no native dependency. On Blazor the browser already implements flexbox, so the component emits CSS set
to Yoga's defaults. The same layout, written with the same properties, gives the same boxes in both.

```xml
<!-- MAUI -->
<shiny:YogaLayout FlexDirection="Row" AlignItems="Center" Gap="8" Padding="12,8">
    <Label Text="Inbox" shiny:YogaLayout.FlexGrow="1" />
    <Label Text="Done" shiny:YogaLayout.AutoMargins="Start" />
    <Border shiny:YogaLayout.PositionType="Absolute"
            shiny:YogaLayout.Top="4" shiny:YogaLayout.Right="4" />
</shiny:YogaLayout>
```

```razor
@* Blazor *@
<YogaLayout FlexDirection="YogaFlexDirection.Row" AlignItems="YogaAlign.Center" Gap="8" Padding="8 12">
    <YogaLayout FlexGrow="1">Inbox</YogaLayout>
    <YogaLayout AutoMargins="YogaEdges.Start">Done</YogaLayout>
    <YogaLayout PositionType="YogaPositionType.Absolute" Top="4" Right="4" />
</YogaLayout>
```

## How Yoga differs from CSS flexbox

| | CSS | Yoga / `YogaLayout` |
|---|---|---|
| `FlexDirection` default | row | **Column** |
| `FlexShrink` default | 1 | **0** |
| `AlignContent` default | stretch | **FlexStart** |
| Minimum size | `auto` (content) | **0** |
| Box sizing | content-box | **border-box** |
| Position default | static | **Relative** |

## Container properties

| Property | Default | |
|---|---|---|
| `FlexDirection` | `Column` | `Column`, `ColumnReverse`, `Row`, `RowReverse` |
| `JustifyContent` | `FlexStart` | `FlexStart`, `Center`, `FlexEnd`, `SpaceBetween`, `SpaceAround`, `SpaceEvenly` |
| `AlignItems` | `Stretch` | `FlexStart`, `Center`, `FlexEnd`, `Stretch`, `Baseline` |
| `AlignContent` | `FlexStart` | also `SpaceBetween`, `SpaceAround`, `SpaceEvenly` (wrapped lines only) |
| `FlexWrap` | `NoWrap` | `NoWrap`, `Wrap`, `WrapReverse` |
| `Gap`, `RowGap`, `ColumnGap` | 0 | `RowGap`/`ColumnGap` override `Gap` for their axis |
| `Padding` | | MAUI `Thickness`; Blazor CSS shorthand (`"8 16"`, bare numbers are px) |
| Direction | inherit | MAUI uses `FlowDirection`; Blazor has `Direction` (`Inherit`, `LTR`, `RTL`) |

## Child properties

On MAUI these are attached properties (`shiny:YogaLayout.FlexGrow="1"`) and work on any view. On Blazor
they are parameters of a nested `YogaLayout`, because every Yoga node is a `YogaLayout`, just as every
React Native view is a node.

| Property | Default | |
|---|---|---|
| `FlexGrow` / `FlexShrink` | 0 / 0 | |
| `FlexBasis` | `auto` | points or `%` of the container |
| `AlignSelf` | `Auto` | overrides the container's `AlignItems` |
| `PositionType` | `Relative` | `Relative` flows and is nudged by insets, `Absolute` leaves the flow, `Static` ignores insets |
| `Left`, `Top`, `Right`, `Bottom`, `Start`, `End` | | insets; `Start`/`End` follow the flow direction |
| `Width`, `Height` (Blazor) / `NodeWidth`, `NodeHeight` (MAUI) | `auto` | points or `%` |
| `MinWidth`, `MinHeight`, `MaxWidth`, `MaxHeight` | | points or `%` |
| `AspectRatio` | | width ÷ height; one known dimension gives the other |
| `AutoMargins` | `None` | `YogaEdges` flags: an auto margin soaks up free space (`Start` pushes to the end, `Vertical` centres) |
| `Display` | `Flex` | `None` takes the child out of layout |
| Margin | | MAUI: the view's own `Margin`. Blazor: `Margin` (CSS shorthand) |

Lengths are written as text on both hosts: `"120"` (points; px on Blazor), `"50%"`, `"auto"`. In MAUI XAML a
`YogaValueTypeConverter` parses them. In C#, use `YogaValue.Point(120)`, `YogaValue.Percent(50)` or
`YogaValue.Auto`; a `double` converts implicitly.

**Why `NodeWidth` on MAUI:** Yoga calls it `width`, but on a MAUI layout the names `Width` and `Height`
already belong to `VisualElement`. MAUI's XAML compiler resolves `shiny:YogaLayout.Width` to that
`double` property, so `"50%"` does not compile. A plain `WidthRequest`/`HeightRequest` is honoured as a
point size, as are `MinimumWidthRequest`/`MaximumWidthRequest`.

## Absolute positioning

An absolute child leaves the flow and adds nothing to the container's size. Its insets and percentages are
measured against the container's **padding box**, as in Yoga. With both `Left` and `Right` set, its width
follows from them. On an axis with no insets it sits where `JustifyContent`/`AlignItems` would put a lone
child.

## Platform notes

- **MAUI baseline:** MAUI exposes no text baselines, so `Baseline` aligns each child's bottom edge. That is
  what Yoga does for a node without a baseline function. Blazor aligns real text baselines.
- **MAUI margins are physical.** `Margin.Left` stays on the left in right-to-left flow, because MAUI's
  `ComputeFrame` does not swap it. Use `AutoMargins="Start"`/insets `Start`/`End` for flow-relative edges.
- **Blazor plain children:** elements placed directly inside a `YogaLayout` (a `<span>`, a `<div>`) get
  Yoga's `flex-shrink: 0`, border-box and minimum size 0. The rule is in a CSS cascade layer, so any
  style of your own on that element still wins. For item properties such as grow, insets and percentages,
  wrap the element in a `YogaLayout`.
- **Performance (MAUI):** a measure followed by an arrange at the same size resolves the flex once. If
  you need `FlexLayout`'s API with measurement caching across passes, see
  [ShinyFlexLayout](flex-layout.md).

## Sample

`samples/Sample/Features/Yoga/YogaLayoutPage` (route `yogalayout`) and
`samples/Sample.Blazor/Pages/YogaLayoutPage.razor` (`/yoga`) build the same four scenes: a playground,
an app bar with grow and an auto margin, cards with an aspect ratio and an absolute badge, percentages,
and wrapping chips.

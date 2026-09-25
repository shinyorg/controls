# YogaLayout (MAUI + Blazor)

The layout model of [Yoga](https://www.yogalayout.dev/) (Meta's open-source flexbox engine behind React
Native; credit to the Yoga team, github.com/facebook/yoga). This is an independent implementation: pure C#
on MAUI (no native Yoga binary), and CSS with Yoga's defaults on Blazor. Core packages: `Shiny.Maui.Controls`, `Shiny.Blazor.Controls`.

Use it when the user asks for Yoga / React Native style layout, absolute positioning with insets,
percentage sizes, aspect ratio, or auto margins. For a plain MAUI `FlexLayout` replacement use
`ShinyFlexLayout` (flex-layout.md) instead.

## Yoga defaults (differ from CSS, do not "fix" them)

`FlexDirection=Column`, `FlexShrink=0`, `AlignContent=FlexStart`, `AlignItems=Stretch`, min size 0, border-box, `PositionType=Relative`.

## Enums (same names on both hosts)

- `YogaFlexDirection`: Column, ColumnReverse, Row, RowReverse
- `YogaJustify`: FlexStart, Center, FlexEnd, SpaceBetween, SpaceAround, SpaceEvenly
- `YogaAlign`: Auto, FlexStart, Center, FlexEnd, Stretch, Baseline, SpaceBetween, SpaceAround, SpaceEvenly
- `YogaWrap`: NoWrap, Wrap, WrapReverse
- `YogaPositionType`: Relative, Absolute, Static
- `YogaDisplay`: Flex, None
- `YogaEdges` (flags): Left, Top, Right, Bottom, Start, End, Horizontal, Vertical, All
- Blazor only: `YogaDirection` Inherit, LTR, RTL (MAUI uses `FlowDirection`)

## MAUI

Container properties: `FlexDirection`, `JustifyContent`, `AlignItems`, `AlignContent`, `FlexWrap`, `Gap`,
`RowGap`, `ColumnGap` (NaN = use Gap), `Padding`.

Child properties are **attached** and work on any view: `FlexGrow`, `FlexShrink`, `FlexBasis`, `AlignSelf`,
`PositionType`, `Left`/`Top`/`Right`/`Bottom`/`Start`/`End`, `NodeWidth`/`NodeHeight`,
`MinWidth`/`MinHeight`/`MaxWidth`/`MaxHeight`, `AspectRatio`, `AutoMargins`, `Display`. Margin is the
view's own `Margin`.

**Never write `shiny:YogaLayout.Width` / `.Height`.** Those resolve to `VisualElement.Width` and do not
compile with `"50%"`. The attached sizes are `NodeWidth` / `NodeHeight`. `WidthRequest` also works for points.

Lengths are `YogaValue` and are written as text in XAML: `"120"`, `"50%"`, `"auto"`. In C#:
`YogaLayout.SetNodeWidth(view, YogaValue.Percent(50))`, `YogaLayout.SetLeft(view, 10)` (double converts implicitly).

```xml
<shiny:YogaLayout FlexDirection="Row" Gap="12">
    <shiny:YogaLayout shiny:YogaLayout.FlexGrow="1" shiny:YogaLayout.FlexBasis="0">
        <Image Source="card.png" shiny:YogaLayout.AspectRatio="1.5" Aspect="AspectFill" />
        <Label Text="Aurora" Margin="8" />
        <Border shiny:YogaLayout.PositionType="Absolute"
                shiny:YogaLayout.Top="8" shiny:YogaLayout.Right="8">
            <Label Text="NEW" />
        </Border>
    </shiny:YogaLayout>
</shiny:YogaLayout>
```

Notes: `Baseline` aligns bottom edges (MAUI has no text baselines). `Margin` is physical, even in RTL.
Absolute children use the padding box and don't size the container.

## Blazor

`<YogaLayout>` is both the container and a node. Every child that needs item properties is itself a
`<YogaLayout>`, like React Native views. Lengths are **strings**: `Width="50%"`, `Width="120"` (px),
`FlexBasis="0"`. Razor would parse a non-string `50%` as C#, which is why they are strings. Enum
parameters need the type prefix: `FlexDirection="YogaFlexDirection.Row"`.

Parameters: container `FlexDirection`, `JustifyContent`, `AlignItems`, `AlignContent`, `FlexWrap`,
`Gap`, `RowGap`/`ColumnGap` (double?), `Padding` (CSS shorthand), `Direction`. Node: `FlexGrow`,
`FlexShrink`, `FlexBasis`, `AlignSelf`, `PositionType`, `Left`/`Top`/`Right`/`Bottom`/`Start`/`End`,
`Width`/`Height`, `Min*`/`Max*`, `AspectRatio` (double?), `Margin` (CSS shorthand), `AutoMargins`,
`Display`. `class`/`style` are merged, not replaced.

```razor
<YogaLayout FlexDirection="YogaFlexDirection.Row" AlignItems="YogaAlign.Center" Gap="8" Padding="8 12">
    <YogaLayout FlexGrow="1"><strong>Inbox</strong></YogaLayout>
    <YogaLayout AutoMargins="YogaEdges.Start">Done</YogaLayout>
    <YogaLayout PositionType="YogaPositionType.Absolute" Top="8" Right="8" Width="24" AspectRatio="1" />
</YogaLayout>
```

Plain elements directly inside get `flex-shrink:0`, border-box and min size 0 from a layered CSS rule,
so any style of the consumer's own still wins.

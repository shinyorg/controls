# ShinyFlexLayout

[← All Shiny Controls](../../README.md)

A CSS-flexbox layout for **.NET MAUI** with the same API as MAUI's own `FlexLayout`, plus row/column
gaps. It is built to be fast. MAUI's `FlexLayout` measures every child in every measure *and* arrange
pass, and again whenever any one of them changes. `ShinyFlexLayout` measures a child once and reuses
the result until that child changes.

> **MAUI only.** Blazor doesn't need it: CSS flexbox is native in the browser (see
> [Layout & AppLayout](layout.md) for `VStack`/`HStack`).

```xml
<shiny:ShinyFlexLayout Wrap="Wrap"
                       JustifyContent="SpaceBetween"
                       AlignItems="Center"
                       ColumnSpacing="8"
                       RowSpacing="8">
    <Label Text="One" />
    <Label Text="Takes the rest" shiny:ShinyFlexLayout.Grow="1" />
    <Label Text="Always first" shiny:ShinyFlexLayout.Order="-1" />
    <BoxView shiny:ShinyFlexLayout.Basis="25%" shiny:ShinyFlexLayout.Shrink="0" />
</shiny:ShinyFlexLayout>
```

It works with `BindableLayout.ItemsSource`/`ItemTemplate` like any other MAUI layout.

## Why it is faster

| | MAUI `FlexLayout` | `ShinyFlexLayout` |
|---|---|---|
| Pass at the same size (the platform does this constantly) | measures every child | measures **nothing**; the resolved pass is reused |
| One child changes (a label's text, an image loading) | measures every child | measures **that child** |
| Resize / rotation | measures every child | leaf children that still fit are **not re-measured** |
| Measure then arrange | the flex is resolved twice | resolved once; arrange is arithmetic |
| Allocations per pass | engine objects every pass | **none**: buffers are reused, no LINQ, no boxing |

Measured on an iOS simulator with real `Label`s in a wrapped layout. Each pass alternates the width
(like a rotation) and changes one label's text. This is the benchmark on the sample app's Flex Layout
page:

| Labels | MAUI `FlexLayout` | `ShinyFlexLayout` | |
|---|---|---|---|
| 300 | 6.7 ms / pass | 0.47 ms / pass | ~14× |
| 1000 | ~23 ms / pass | 0.9 ms / pass | ~25× |

At 1000 children, MAUI's measure pass alone is longer than a 60 fps frame (16.7 ms).

### How it knows a child changed

A child's cached measurement is thrown away when:

- the child raises `MeasureInvalidated`. That covers the Controls path: `Text`, `WidthRequest`, `Margin`,
  `IsVisible`, and anything else that calls `InvalidateMeasure()`;
- a handler calls `IView.InvalidateMeasure()` on the child **or on anything inside it**. This is the
  native path, for example an image finishing loading inside a `Grid` that is a flex child. MAUI raises
  no event for this path, so the layout hooks `ViewHandler.ViewCommandMapper`;
- the child's handler changes.

A result of zero width or height is never cached (it usually means "not loaded yet"). A layout or
content-hosting child (`Grid`, `Border`, `ContentView`…) only reuses a measurement taken under exactly
the same constraints, since a star-sized `Grid` fills whatever it is given.

If a child changes size natively and nothing calls `InvalidateMeasure()`, call it yourself, or set
`IsMeasureCacheEnabled="False"` on that layout.

## Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Direction` | `FlexDirection` | `Row` | Main axis: `Row`, `RowReverse`, `Column`, `ColumnReverse` |
| `Wrap` | `FlexWrap` | `NoWrap` | `NoWrap`, `Wrap`, or `Reverse` (lines stack from the cross end) |
| `JustifyContent` | `FlexJustify` | `Start` | Main-axis distribution: `Start`, `Center`, `End`, `SpaceBetween`, `SpaceAround`, `SpaceEvenly` |
| `AlignItems` | `FlexAlignItems` | `Stretch` | Cross-axis placement within a line: `Stretch`, `Center`, `Start`, `End` |
| `AlignContent` | `FlexAlignContent` | `Stretch` | How wrapped lines share cross space (`Stretch`, `Center`, `Start`, `End`, `SpaceBetween`, `SpaceAround`, `SpaceEvenly`). Applies only when `Wrap` is on |
| `RowSpacing` | `double` | `0` | CSS `row-gap`: between wrapped lines in a row layout, between children in a column layout |
| `ColumnSpacing` | `double` | `0` | CSS `column-gap`: between children in a row layout, between wrapped lines in a column layout |
| `IsMeasureCacheEnabled` | `bool` | `true` | Reuse child measurements and resolved passes. Turn off only for a child that resizes natively without invalidating |

Plus `Padding` and everything else from `Layout`.

## Attached properties

| Property | Type | Default | Description |
|---|---|---|---|
| `ShinyFlexLayout.Grow` | `float` | `0` | Share of the line's spare room this child takes |
| `ShinyFlexLayout.Shrink` | `float` | `1` | How readily this child gives up room when the line overflows, weighted by its size. `0` never shrinks |
| `ShinyFlexLayout.Basis` | `FlexBasis` | `Auto` | Starting main size: `Auto` (measure), a length (`120`), or a percentage of the line (`"25%"`) |
| `ShinyFlexLayout.AlignSelf` | `FlexAlignSelf` | `Auto` | Overrides `AlignItems` for this child |
| `ShinyFlexLayout.Order` | `int` | `0` | Visual order; ties keep child order. Children are never reordered |

## Migrating from `FlexLayout`

The enums (`FlexDirection`, `FlexWrap`, `FlexJustify`, `FlexAlignItems`, `FlexAlignContent`,
`FlexAlignSelf`) and `FlexBasis` are MAUI's own `Microsoft.Maui.Layouts` types. Porting a page means
changing the element name and the attached-property prefix:

```diff
- <FlexLayout Wrap="Wrap" JustifyContent="SpaceAround">
-     <Label FlexLayout.Grow="1" Margin="0,0,8,8" />
+ <shiny:ShinyFlexLayout Wrap="Wrap" JustifyContent="SpaceAround" ColumnSpacing="8" RowSpacing="8">
+     <Label shiny:ShinyFlexLayout.Grow="1" />
```

Differences to know about:

- **Shrink follows CSS.** An overflowing no-wrap line shrinks its children in proportion to their size
  and fits. MAUI's `FlexLayout` takes the same amount from each child and can still overflow the
  container.
- **Stretch respects an explicit size.** A child with a `HeightRequest` (in a row) is not stretched.
- **Right-to-left** mirrors the layout, including the cross axis of a column layout.
- `FlexLayout.Position` (absolute positioning) is not carried over. Use an `AbsoluteLayout` or a `Grid` overlay.
- There are gaps (`RowSpacing`/`ColumnSpacing`), so child margins used as spacing can go.

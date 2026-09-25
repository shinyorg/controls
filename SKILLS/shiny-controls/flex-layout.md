# ShinyFlexLayout (MAUI only)

A fast CSS-flexbox layout with the **same API as MAUI's `FlexLayout`** plus `RowSpacing`/`ColumnSpacing`
gaps. Namespace `Shiny.Maui.Controls` (XAML: `xmlns:shiny="http://shiny.net/maui/controls"`). Core package.
There is **no Blazor component**: emit plain CSS flexbox (or `VStack`/`HStack`) in Razor.

When generating MAUI code that needs flexbox (tags, chips, wrapping toolbars, responsive card rows, a
sidebar with a fixed basis), prefer `shiny:ShinyFlexLayout` over `FlexLayout`. It uses the same
`Microsoft.Maui.Layouts` enums and `FlexBasis`, so the values are identical.

```xml
<shiny:ShinyFlexLayout Direction="Row"
                       Wrap="Wrap"
                       JustifyContent="Start"
                       AlignItems="Center"
                       AlignContent="Start"
                       ColumnSpacing="8"
                       RowSpacing="8"
                       BindableLayout.ItemsSource="{Binding Tags}">
    <BindableLayout.ItemTemplate>
        <DataTemplate x:DataType="x:String">
            <Border Padding="10,4" StrokeShape="RoundRectangle 12">
                <Label Text="{Binding .}" />
            </Border>
        </DataTemplate>
    </BindableLayout.ItemTemplate>
</shiny:ShinyFlexLayout>
```

```xml
<!-- sidebar + fill + fixed -->
<shiny:ShinyFlexLayout ColumnSpacing="8">
    <ContentView shiny:ShinyFlexLayout.Basis="30%" shiny:ShinyFlexLayout.Shrink="0" />
    <ContentView shiny:ShinyFlexLayout.Grow="1" />
    <ContentView shiny:ShinyFlexLayout.Basis="60" />
</shiny:ShinyFlexLayout>
```

```csharp
var flex = new ShinyFlexLayout { Wrap = FlexWrap.Wrap, ColumnSpacing = 6, RowSpacing = 6 };
var label = new Label { Text = "grow" };
ShinyFlexLayout.SetGrow(label, 1);          // float
ShinyFlexLayout.SetBasis(label, new FlexBasis(0.25f, isRelative: true));
ShinyFlexLayout.SetOrder(label, -1);        // int
ShinyFlexLayout.SetAlignSelf(label, FlexAlignSelf.Center);
flex.Children.Add(label);
```

## Properties

- `Direction` (`FlexDirection`, `Row`), `Wrap` (`FlexWrap`, `NoWrap`; `Wrap`/`Reverse`),
  `JustifyContent` (`FlexJustify`, `Start`), `AlignItems` (`FlexAlignItems`, `Stretch`),
  `AlignContent` (`FlexAlignContent`, `Stretch`; only used when wrapping)
- `RowSpacing` / `ColumnSpacing` (`double`, `0`) follow CSS `row-gap`/`column-gap`. In a **row** layout
  `ColumnSpacing` is the gap between children and `RowSpacing` the gap between wrapped lines; a
  column layout swaps them. Use these instead of child margins for spacing.
- `IsMeasureCacheEnabled` (`bool`, `true`). Leave it on. Only turn it off if a child resizes natively
  without calling `InvalidateMeasure()`.
- Attached: `Grow` (`float`, 0), `Shrink` (`float`, 1), `Basis` (`FlexBasis`, `Auto`; XAML `"120"` or
  `"25%"`), `AlignSelf` (`FlexAlignSelf`, `Auto`), `Order` (`int`, 0).

## Rules

- Prefix attached properties with `shiny:ShinyFlexLayout.`. Never use `FlexLayout.Grow` on a
  `ShinyFlexLayout` child: it is ignored.
- Not supported: `FlexLayout.Position` (absolute). Use a `Grid` overlay or `AbsoluteLayout`.
- Shrink is CSS-weighted by size and an overflowing line fits the container (MAUI's `FlexLayout` can overflow).
- Stretch does not override an explicit cross size (`HeightRequest` in a row, `WidthRequest` in a column).
- Right-to-left `FlowDirection` mirrors the layout.
- Speed comes from caching. Children are measured once and re-measured only when they invalidate.
  Same-size passes cost no measuring. Don't call `InvalidateMeasure()` on the layout by hand to "refresh";
  it isn't needed.
- For hundreds or thousands of **data-bound** items that scroll, use a virtualized control
  (`CollectionView`, `VirtualizedGrid`, `StaggeredGrid`) rather than any layout.

# Timeline (vertical rail with content beside it)

One control on both hosts, in the **core** packages:

| Host | Control |
|---|---|
| MAUI | `TimelineView` (`Shiny.Maui.Controls`) |
| Blazor | `TimelineView<TItem>` (`Shiny.Blazor.Controls`) |

**It is `TimelineView`, not `Timeline`.** `Shiny.Controls.Keyframe.Timeline` already exists and is
referenced from `Shiny.Maui.Controls`, so the bare name is taken.

## MAUI

```xml
<shiny:TimelineView ItemsSource="{Binding Events}" ActiveIndex="2">
    <shiny:TimelineView.ItemTemplate>
        <DataTemplate>
            <VerticalStackLayout Spacing="2">
                <Label Text="{Binding Title}" FontAttributes="Bold" />
                <Label Text="{Binding Detail}" FontSize="13" Opacity="0.75" />
            </VerticalStackLayout>
        </DataTemplate>
    </shiny:TimelineView.ItemTemplate>
</shiny:TimelineView>
```

## Blazor

```razor
<TimelineView TItem="Delivery" ItemsSource="events" ActiveIndex="2" NodeClicked="OnClicked">
    <ItemTemplate>
        <div class="entry-title">@context.Title</div>
    </ItemTemplate>
    <OppositeTemplate>
        <span class="entry-time">@context.At</span>
    </OppositeTemplate>
</TimelineView>
```

`TItem` is required — it is a generic component and cannot infer from `ItemsSource` in every case.

## The active position — the part to get right

```csharp
ActiveIndex = 2;   // 0,1 complete · 2 current (draws a ring) · 3+ pending
ActiveIndex = -1;  // the DEFAULT: nothing has happened yet
AllActive = true;  // every node complete, whatever ActiveIndex says
```

- **The default is `-1`, not `0`.** A timeline handed no position must not claim its first entry is
  done. Set it explicitly when generating code that shows progress.
- **`AllActive` wins outright** over `ActiveIndex`; they are not merged. Use it for a history where
  everything has already happened and a trailing pending tail would be a lie.
- The rule lives in one place: `TimelineNode.StateFor(index, activeIndex, allActive)`. Use it rather
  than reimplementing the comparison in a converter or a marker template.

The connector fills **into** a node once it is reached, and **out of** it only once the next one is —
so the rail reads as a progress bar. Do not try to fix that with per-item colours.

## Templates bind to different things

| | Binds to |
|---|---|
| `ItemTemplate` | the **item** |
| `OppositeTemplate` | the **item** |
| `MarkerTemplate` | a **`TimelineNode`** (`Item`, `Index`, `State`, `IsFirst`, `IsLast`, `IsActive`) |

That asymmetry is deliberate — do not write `{Binding Item.Title}` in an `ItemTemplate`, and do not
expect the raw item inside a `MarkerTemplate`. Leave `MarkerTemplate` unset for the default themed dot.

## Rows size to their content

Each node is one row whose height comes from its content, and the rail stretches to fill it. Do **not**
set a fixed row height or try to draw one continuous line behind the list — content beside a timeline
is arbitrary and self-sizing, which is the whole point of the control.

`MarkerOffset` (default 4) pushes the marker down so it aligns with the **first line of text**, not the
middle of the content box.

## Everything else

```csharp
RailPosition = TimelineRailPosition.Left;  // or Right - the opposite content moves with it
MarkerSize = 14; LineThickness = 2;
ItemSpacing = 16;   // gap between nodes; the rail runs through it unbroken
RailSpacing = 12;   // gap between the rail and the content
ActiveColor = null; PendingColor = null;   // null = follow the theme
IsScrollable = true;                        // MAUI only; off when already inside a ScrollView
```

Click/tap: `NodeClicked` (Blazor, gives `(Item, Index)`) and `NodeTapped` (MAUI, gives
`CollectionItemEventArgs`).

## Not virtualized

Every item is realised on both hosts. That is the deliberate trade — a recycling list is worst at
exactly the thing this control is for, rows of differing height. Page in the app for very long
timelines; do not expect `ItemsSource` of thousands to behave.

## Theming

MAUI routes the rail through `ShinyThemeKeys.Color.Primary` / `SurfaceContainerHighest` via
`SetDynamicResource`; Blazor through `--shiny-color-primary` / `--shiny-color-surface-container-highest`.
Setting `ActiveColor`/`PendingColor` pins the colour and takes it out of the theme.

The MAUI marker is a `BoxView`, not a `Border`: a `BoxView`'s fill is a `Color` and a `Border`'s is a
`Brush`, and a colour token assigned to a brush property is silently dropped.

## Related

`Wizard` draws the same three states horizontally and owns its steps; a timeline binds a collection and
shows all of them at once. Wizard dots are only clickable — and only show a hover — when
`AllowStepSelection` is true, and with `LinearNavigation` on that still means completed steps and the
current one.

# Slider

[← All Shiny Controls](../../README.md)

A slider with a two-color gradient track, blended thumb border, tooltip, full drag/tap interaction,
labelled **stop points**, a **custom thumb**, and a **vertical** orientation.

```xml
<shiny:Slider Value="{Binding Temperature}"
              Minimum="0"
              Maximum="100"
              ColdColor="#3B82F6"
              HotColor="#EF4444"
              ShowTooltip="True" />
```

| Property | Type | Default | Description |
|---|---|---|---|
| Value | double | 0 | Current value (TwoWay) |
| Minimum | double | 0 | Minimum value |
| Maximum | double | 100 | Maximum value |
| Step | double | 1 | Snap increment |
| Orientation | SliderOrientation | Horizontal | `Vertical` puts the minimum at the bottom |
| VerticalLength | double | 220 | Track length when vertical (it has no width to stretch into) |
| ColdColor | Color/string | #3B82F6 | Left/bottom gradient color |
| HotColor | Color/string | #EF4444 | Right/top gradient color |
| TrackHeight | double | 8 | Track thickness |
| ThumbSize | double | 24 | Thumb diameter |
| ThumbWidth | double | -1 | Thumb width; `-1` keeps it square at `ThumbSize` |
| ThumbHeight | double | -1 | Thumb height; `-1` keeps it square at `ThumbSize` |
| ThumbCornerRadius | double / string? | -1 / null | `-1`/null keeps the thumb fully rounded (a circle, or a pill once it is not square) |
| ThumbPadding | Thickness / string? | 0 / null | Inset between the thumb border and its template content |
| ThumbTemplate | DataTemplate / RenderFragment&lt;double&gt; | null | Custom content inside the thumb |
| ThumbColor | Color/string | Theme surface | Thumb fill color |
| ShowTooltip | bool | true | Show value tooltip |
| TooltipTemplate | DataTemplate/RenderFragment | null | Custom tooltip content |
| ValueFormat | string? | null | Format string for tooltip value |

## Stop points

`Marks` (MAUI) / `<SliderMark>` children (Blazor) put labelled stops on the track. With
`SnapToMarks` on — the default — the thumb comes to rest on the nearest one, and `Step` is ignored;
turn it off to leave the marks as reference points.

```xml
<shiny:Slider Value="{Binding Quality}" Minimum="0" Maximum="3" ShowTooltip="False">
    <shiny:Slider.Marks>
        <shiny:SliderMark Value="0" Text="Draft"  Color="#94A3B8" />
        <shiny:SliderMark Value="1" Text="Good"   Color="#38BDF8" />
        <shiny:SliderMark Value="2" Text="Better" Color="#22C55E" />
        <shiny:SliderMark Value="3" Text="Best"   Color="#F59E0B" />
    </shiny:Slider.Marks>
</shiny:Slider>
```

```razor
<Slider @bind-Value="quality" Minimum="0" Maximum="3" ShowTooltip="false">
    <SliderMark Value="0" Text="Draft" Color="#94A3B8" />
    <SliderMark Value="3" Text="Best" Color="#F59E0B" />
</Slider>
```

| Slider property | Type | Default | Description |
|---|---|---|---|
| SnapToMarks | bool | true | Thumb comes to rest on the nearest mark |
| MarkShape | SliderMarkShape | Dot | `Dot`, `Bubble` (pill-shaped label) or `Line` (tick) |
| MarkSize | double | 10 | Dot diameter / tick thickness |
| MarkColor | Color/string | Theme surface | Fill for marks that set no color |
| MarkTextColor | Color/string | Theme on-surface-variant | Label color for marks that set no color |
| MarkFontSize | double | 11 | Label size |
| ShowMarkLabels | bool | true | Draw the labels at all |

| SliderMark | Type | Default | Description |
|---|---|---|---|
| Value | double | 0 | Position on the track |
| Text | string? | null | Label — a caption under a dot/tick, the content of a bubble |
| Color | Color/string? | null | Dot, tick or bubble fill |
| TextColor | Color/string? | null | Label color |
| Shape | SliderMarkShape? | null | Overrides the slider's `MarkShape` for this mark |
| Size | double | -1 | Overrides `MarkSize` for this mark |
| IsVisible | bool | true | A hidden mark is not drawn and is not a snap target |

The stop point itself is always the dot or tick on the track; the label sits in the band beside it.
Snapping parks the thumb on a mark by definition, so anything drawn on the track at a mark's value
would spend its life underneath the thumb.

## Custom thumb content

`ThumbTemplate` puts your own content inside the thumb — an icon, a glyph, the value itself. The
template's binding context (MAUI) / context parameter (Blazor) is the slider's current `Value`.

```xml
<shiny:Slider Value="{Binding Brightness}"
              Minimum="0" Maximum="100"
              ShowTooltip="False"
              ThumbWidth="52" ThumbHeight="28" ThumbPadding="4,0">
    <shiny:Slider.ThumbTemplate>
        <DataTemplate x:DataType="sys:Double">
            <Label Text="{Binding ., StringFormat='{0:0}%'}" FontSize="11" FontAttributes="Bold"
                   HorizontalTextAlignment="Center" VerticalTextAlignment="Center" />
        </DataTemplate>
    </shiny:Slider.ThumbTemplate>
</shiny:Slider>
```

```razor
<Slider @bind-Value="brightness" Minimum="0" Maximum="100" ShowTooltip="false"
        ThumbWidth="52" ThumbHeight="28" ThumbPadding="0 4px">
    <ThumbTemplate Context="value">
        <span style="font-size: 11px; font-weight: 700;">@value.ToString("0")%</span>
    </ThumbTemplate>
</Slider>
```

The thumb does **not** grow to fit its content — the track's travel is measured from the thumb, so its
box has to be known before anything is laid out. Size it with `ThumbSize`, or with
`ThumbWidth`/`ThumbHeight` when the content is not square; `ThumbCornerRadius` turns the default pill
into any other shape. In MAUI the template is realized once and rebound as the thumb moves, so a
template that animates keeps its state across a drag.

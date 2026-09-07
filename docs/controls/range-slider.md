# RangeSlider

[← All Shiny Controls](../../README.md)

A two-thumb variant of Slider that selects a lower/upper value pair. It reuses the gradient track, blended thumb borders, and floating tooltips, adding `MinimumRange`/`MaximumRange` gap constraints between the thumbs. The dragged thumb hard-stops at `MinimumRange`; dragging past `MaximumRange` pushes the other thumb along.

```xml
<shiny:RangeSlider LowerValue="{Binding PriceLow}"
                   UpperValue="{Binding PriceHigh}"
                   Minimum="0"
                   Maximum="1000"
                   Step="10"
                   MinimumRange="50"
                   MaximumRange="500"
                   ValueFormat="C0" />
```

| Property | Type | Default | Description |
|---|---|---|---|
| LowerValue | double | 0 | Lower thumb value (TwoWay) |
| UpperValue | double | 100 | Upper thumb value (TwoWay) |
| Minimum | double | 0 | Minimum value |
| Maximum | double | 100 | Maximum value |
| Step | double | 1 | Snap increment |
| MinimumRange | double | 0 | Minimum gap between thumbs (hard stop); 0 = off |
| MaximumRange | double | 0 | Maximum gap between thumbs (pushes the other thumb); 0 = off |
| ColdColor | Color/string | #3B82F6 | Left gradient color |
| HotColor | Color/string | #EF4444 | Right gradient color |
| TrackHeight | double | 8 | Track height |
| ThumbSize | double | 24 | Thumb diameter |
| ThumbWidth | double | -1 | Thumb width; `-1` keeps the thumbs square at `ThumbSize` |
| ThumbHeight | double | -1 | Thumb height; `-1` keeps the thumbs square at `ThumbSize` |
| ThumbCornerRadius | double / string? | -1 / null | `-1`/null keeps the thumbs fully rounded (a circle, or a pill once they are not square) |
| ThumbPadding | Thickness / string? | 0 / null | Inset between a thumb border and its template content |
| ThumbTemplate | DataTemplate / RenderFragment&lt;double&gt; | null | Custom content inside both thumbs |
| LowerThumbTemplate | DataTemplate / RenderFragment&lt;double&gt; | null | Content for the lower thumb only; falls back to `ThumbTemplate` |
| UpperThumbTemplate | DataTemplate / RenderFragment&lt;double&gt; | null | Content for the upper thumb only; falls back to `ThumbTemplate` |
| ThumbColor | Color/string | White | Thumb fill color |
| ShowTooltip | bool | true | Show a value tooltip per thumb |
| TooltipTemplate | DataTemplate/RenderFragment | null | Custom tooltip content (applied to both thumbs) |
| ValueFormat | string? | null | Format string for tooltip values |

## Custom thumb content

`ThumbTemplate` puts your own content inside both thumbs; `LowerThumbTemplate` and
`UpperThumbTemplate` override it per end. Each template's binding context (MAUI) / context parameter
(Blazor) is that thumb's own value.

```xml
<shiny:RangeSlider LowerValue="{Binding ShiftStart}"
                   UpperValue="{Binding ShiftEnd}"
                   Minimum="0" Maximum="24" Step="1"
                   ShowTooltip="False"
                   ThumbWidth="46" ThumbHeight="26">
    <shiny:RangeSlider.ThumbTemplate>
        <DataTemplate x:DataType="sys:Double">
            <Label Text="{Binding ., StringFormat='{0:00}:00'}" FontSize="10" FontAttributes="Bold"
                   HorizontalTextAlignment="Center" VerticalTextAlignment="Center" />
        </DataTemplate>
    </shiny:RangeSlider.ThumbTemplate>
</shiny:RangeSlider>
```

```razor
<RangeSlider @bind-LowerValue="shiftStart" @bind-UpperValue="shiftEnd"
             Minimum="0" Maximum="24" Step="1" ShowTooltip="false"
             ThumbWidth="46" ThumbHeight="26">
    <ThumbTemplate Context="value">
        <span style="font-size: 10px; font-weight: 700;">@value.ToString("00"):00</span>
    </ThumbTemplate>
</RangeSlider>
```

The thumbs do **not** grow to fit their content — the travel is measured from the thumb, so the box has
to be known before anything is laid out. Size them with `ThumbSize`, or with `ThumbWidth`/`ThumbHeight`
when the content is not square; `ThumbCornerRadius` turns the default pill into any other shape.
